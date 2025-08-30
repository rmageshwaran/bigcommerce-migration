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
    /// 🆕 RANGE QUERY: Gets entity mappings by RowNumber range for efficient parallel processing
    /// Uses composite RowKey range queries for optimal Azure Table Storage performance
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type to filter by (e.g., "products")</param>
    /// <param name="startRowNumber">Start of RowNumber range (inclusive)</param>
    /// <param name="endRowNumber">End of RowNumber range (inclusive)</param>
    /// <param name="dataFieldFilter">Optional data field that must be non-empty (e.g., "ChannelsData")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entity mappings in the specified RowNumber range</returns>
    public async Task<List<EntityMapping>> GetEntityMappingsByRowNumberRangeAsync(
        string migrationId,
        string entityType,
        long startRowNumber,
        long endRowNumber,
        string? dataFieldFilter = null,
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
            if (startRowNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(startRowNumber), "Start RowNumber must be >= 1");
            if (endRowNumber < startRowNumber)
                throw new ArgumentOutOfRangeException(nameof(endRowNumber), "End RowNumber must be >= start RowNumber");

            _logger.LogInformation(
                "🔍 [RANGE-QUERY] Querying EntityMappings range: MigrationId={MigrationId}, EntityType={EntityType}, " +
                "Range={StartRow}-{EndRow}, DataFilter={DataFilter}",
                migrationId, entityType, startRowNumber, endRowNumber, dataFieldFilter ?? "none");

            var tableClient = await GetTableClientAsync();

            // 🆕 COMPOSITE ROWKEY RANGE QUERY: Build efficient range filter using RowKey
            var startRowKey = $"{startRowNumber:D10}_{entityType}_";
            var endRowKey = $"{endRowNumber + 1:D10}_{entityType}_"; // +1 for exclusive upper bound
            
            // Build filter: PartitionKey + RowKey range only
            // ✅ CORRECT APPROACH: No data field filtering at database level
            // Transform phase will handle filtering and add to skip counts appropriately
            var filter = $"PartitionKey eq '{migrationId}' and RowKey ge '{startRowKey}' and RowKey lt '{endRowKey}'";
            
            if (!string.IsNullOrEmpty(dataFieldFilter))
            {
                _logger.LogInformation("🔍 [RANGE-QUERY] Retrieving all EntityMappings in range - {DataField} filtering will be handled in transform phase", 
                    dataFieldFilter);
            }

            _logger.LogDebug(
                "🔍 [RANGE-QUERY] Azure Table Storage filter: {Filter}",
                filter);

            var results = new List<EntityMapping>();

            // Calculate optimal page size based on expected range size
            var expectedRecordCount = endRowNumber - startRowNumber + 1;
            var optimalPageSize = Math.Min(Math.Max((int)expectedRecordCount, 250), 1000); // Between 250-1000
            
            _logger.LogDebug("🔍 [RANGE-QUERY] Calculated optimal page size: {PageSize} for expected {ExpectedCount} records", 
                optimalPageSize, expectedRecordCount);

            // Execute efficient range query using native Azure Table Storage indexing
            var queryResults = tableClient.QueryAsync<TableEntity>(
                filter: filter,
                maxPerPage: optimalPageSize, // Dynamic page size based on range
                cancellationToken: cancellationToken);

            await foreach (var entity in queryResults.WithCancellation(cancellationToken))
            {
                var mapping = ConvertFromTableEntity(entity);
                results.Add(mapping);
            }

            _logger.LogInformation(
                "✅ [RANGE-QUERY] Successfully retrieved {Count} EntityMappings for range {StartRow}-{EndRow}",
                results.Count, startRowNumber, endRowNumber);

            return results;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("❌ [RANGE-QUERY] Range query cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "❌ [RANGE-QUERY] Error getting EntityMappings by range: MigrationId={MigrationId}, EntityType={EntityType}, Range={StartRow}-{EndRow}",
                migrationId, entityType, startRowNumber, endRowNumber);
            throw;
        }
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
            RowNumber = entity.GetInt64("RowNumber") ?? 0,
            CreatedAt = entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
            UpdatedAt = entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow
        };
    }

    #endregion
}
