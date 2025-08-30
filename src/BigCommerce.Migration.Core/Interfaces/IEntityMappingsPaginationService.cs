using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service for paginating through EntityMappings table using Azure Table Storage
/// Provides efficient range-based pagination for EntityMappings processing
/// </summary>
public interface IEntityMappingsPaginationService
{
    /// <summary>
    /// Gets entity mappings by RowNumber range for efficient parallel processing
    /// Uses composite RowKey range queries for optimal Azure Table Storage performance
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type to filter by (e.g., "products")</param>
    /// <param name="startRowNumber">Start of RowNumber range (inclusive)</param>
    /// <param name="endRowNumber">End of RowNumber range (inclusive)</param>
    /// <param name="dataFieldFilter">Optional data field that must be non-empty (e.g., "ChannelsData")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entity mappings in the specified RowNumber range</returns>
    Task<List<EntityMapping>> GetEntityMappingsByRowNumberRangeAsync(
        string migrationId,
        string entityType,
        long startRowNumber,
        long endRowNumber,
        string? dataFieldFilter = null,
        CancellationToken cancellationToken = default);
}