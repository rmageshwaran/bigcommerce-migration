using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service for paginating through EntityMappings table using Azure Table Storage continuation tokens
/// Provides efficient pagination for product-related phase processing
/// </summary>
public interface IEntityMappingsPaginationService
{
    /// <summary>
    /// Gets a page of entity mappings using continuation token-based pagination
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type to filter by (e.g., "products")</param>
    /// <param name="pageSize">Number of records per page (default: 250)</param>
    /// <param name="continuationToken">Continuation token from previous page (null for first page)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated entity mappings result</returns>
    Task<EntityMappingsPageResult> GetEntityMappingsPageAsync(
        string migrationId,
        string entityType,
        int pageSize = 250,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all entity mappings for a migration and entity type using async enumerable
    /// Uses continuation token pagination internally for memory efficiency
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type to filter by</param>
    /// <param name="pageSize">Page size for internal pagination (default: 250)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of entity mappings</returns>
    IAsyncEnumerable<EntityMapping> GetAllEntityMappingsAsync(
        string migrationId,
        string entityType,
        int pageSize = 250,
        CancellationToken cancellationToken = default);
}
