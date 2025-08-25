using Azure;
using Azure.Data.Tables;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Service for paginating through EntityMappings table using Azure Table Storage continuation tokens
/// Provides efficient pagination for product-related phase processing
/// 
/// Key Features:
/// - True continuation token-based pagination (no in-memory loading)
/// - Memory efficient for large datasets
/// - Optimal Azure Table Storage performance
/// - Comprehensive error handling and logging
/// - Cancellation support
/// </summary>
public class EntityMappingsPaginationService : IEntityMappingsPaginationService
{
    #region Private Fields

    private readonly IAzureTableInitializationService _tableInitializationService;
    private readonly ILogger<EntityMappingsPaginationService> _logger;
    private const string EntityMappingsTableName = "EntityMappings";

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of EntityMappingsPaginationService
    /// </summary>
    /// <param name="tableInitializationService">Azure Table initialization service</param>
    /// <param name="logger">Logger for the service</param>
    public EntityMappingsPaginationService(
        IAzureTableInitializationService tableInitializationService,
        ILogger<EntityMappingsPaginationService> logger)
    {
        _tableInitializationService = tableInitializationService ?? throw new ArgumentNullException(nameof(tableInitializationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets a page of entity mappings using continuation token-based pagination
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type to filter by (e.g., "products")</param>
    /// <param name="pageSize">Number of records per page (default: 250)</param>
    /// <param name="continuationToken">Continuation token from previous page (null for first page)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated entity mappings result</returns>
    public async Task<EntityMappingsPageResult> GetEntityMappingsPageAsync(
        string migrationId,
        string entityType,
        int pageSize = 250,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(migrationId))
                throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

            if (string.IsNullOrWhiteSpace(entityType))
                throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));

            if (pageSize <= 0 || pageSize > 1000)
                throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be between 1 and 1000");

            _logger.LogInformation(
                "📄 [PAGINATION] Getting EntityMappings page: MigrationId={MigrationId}, EntityType={EntityType}, PageSize={PageSize}, HasContinuation={HasContinuation}",
                migrationId, entityType, pageSize, !string.IsNullOrEmpty(continuationToken));

            var tableClient = await GetTableClientAsync();

            // Build filter for PartitionKey = migrationId AND EntityType = entityType
            // Note: EntityType is a property, not part of the RowKey
            var filter = $"PartitionKey eq '{migrationId}' and EntityType eq '{entityType}'";

            var result = new EntityMappingsPageResult
            {
                MigrationId = migrationId,
                EntityType = entityType,
                PageSize = pageSize,
                Mappings = new List<EntityMapping>()
            };

            // Execute query with pagination using AsPages for continuation token support
            var queryResults = tableClient.QueryAsync<TableEntity>(
                filter: filter,
                maxPerPage: pageSize,
                cancellationToken: cancellationToken);

            var pageEnumerator = queryResults.AsPages(
                continuationToken: continuationToken,
                pageSizeHint: pageSize).GetAsyncEnumerator(cancellationToken);

            try
            {
                if (await pageEnumerator.MoveNextAsync())
                {
                    var page = pageEnumerator.Current;
                    
                    // Convert TableEntity results to EntityMapping objects
                    foreach (var entity in page.Values)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        var mapping = ConvertFromTableEntity(entity);
                        result.Mappings.Add(mapping);
                    }

                    // Set continuation token for next page if available
                    result.ContinuationToken = page.ContinuationToken;

                    _logger.LogInformation(
                        "✅ [PAGINATION] Successfully retrieved EntityMappings page: Count={Count}, HasMorePages={HasMorePages}",
                        result.Count, result.HasMorePages);
                }
                else
                {
                    _logger.LogInformation("📄 [PAGINATION] No EntityMappings found for specified criteria");
                }
            }
            finally
            {
                await pageEnumerator.DisposeAsync();
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("❌ [PAGINATION] EntityMappings pagination cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "❌ [PAGINATION] Error getting EntityMappings page: MigrationId={MigrationId}, EntityType={EntityType}",
                migrationId, entityType);
            throw;
        }
    }

    /// <summary>
    /// Gets all entity mappings for a migration and entity type using async enumerable
    /// Uses continuation token pagination internally for memory efficiency
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type to filter by</param>
    /// <param name="pageSize">Page size for internal pagination (default: 250)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of entity mappings</returns>
    public async IAsyncEnumerable<EntityMapping> GetAllEntityMappingsAsync(
        string migrationId,
        string entityType,
        int pageSize = 250,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? continuationToken = null;
        
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var pageResult = await GetEntityMappingsPageAsync(
                migrationId, entityType, pageSize, continuationToken, cancellationToken);

            foreach (var mapping in pageResult.Mappings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return mapping;
            }

            continuationToken = pageResult.ContinuationToken;
            
        } while (!string.IsNullOrEmpty(continuationToken));
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Gets the Azure Table Storage client for EntityMappings table
    /// </summary>
    /// <returns>Table client instance</returns>
    private async Task<TableClient> GetTableClientAsync()
    {
        return await _tableInitializationService.GetTableClientAsync(EntityMappingsTableName);
    }

    /// <summary>
    /// Converts TableEntity to EntityMapping object
    /// </summary>
    /// <param name="entity">Azure Table Storage entity</param>
    /// <returns>EntityMapping object</returns>
    private static EntityMapping ConvertFromTableEntity(TableEntity entity)
    {
        return new EntityMapping
        {
            MigrationId = entity.PartitionKey!,
            EntityType = entity.GetString("EntityType") ?? string.Empty,
            SourceId = entity.GetString("SourceId") ?? string.Empty,
            DestinationId = entity.GetString("DestinationId") ?? string.Empty,
            SourceStoreId = entity.GetString("SourceStoreId") ?? string.Empty,
            DestinationStoreId = entity.GetString("DestinationStoreId") ?? string.Empty,
            Status = entity.GetString("Status") ?? string.Empty,
            Metadata = entity.GetString("Metadata"),
            RelatedProductsData = entity.GetString("RelatedProductsData"),
            ChannelsData = entity.GetString("ChannelsData"),
            OptionsMappingData = entity.GetString("OptionsMappingData"),
            CreatedAt = entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
            UpdatedAt = entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow
        };
    }

    #endregion
}
