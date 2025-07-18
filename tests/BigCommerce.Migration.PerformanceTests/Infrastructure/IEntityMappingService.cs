namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// Simplified entity mapping service interface for performance testing
/// Focuses on the critical lookup operations we want to benchmark
/// </summary>
public interface IEntityMappingService
{
    /// <summary>
    /// Gets a single destination entity ID for a source entity ID (individual lookup)
    /// This represents the current approach that may be slow
    /// </summary>
    /// <param name="entityType">Type of entity (e.g., "products", "categories")</param>
    /// <param name="sourceEntityId">Source entity ID to look up</param>
    /// <returns>Destination entity ID or null if not found</returns>
    Task<string?> GetDestinationEntityIdAsync(string entityType, string sourceEntityId);

    /// <summary>
    /// Gets multiple destination entity IDs for a batch of source entity IDs (batch lookup)
    /// This represents the optimized approach that should be much faster
    /// </summary>
    /// <param name="entityType">Type of entity (e.g., "products", "categories")</param>
    /// <param name="sourceEntityIds">List of source entity IDs to look up</param>
    /// <returns>Dictionary mapping source IDs to destination IDs</returns>
    Task<Dictionary<string, string?>> GetDestinationEntityIdsBatchAsync(string entityType, List<string> sourceEntityIds);

    /// <summary>
    /// Gets all mappings for an entity type (used for cache analysis)
    /// </summary>
    /// <param name="entityType">Type of entity</param>
    /// <returns>Dictionary of all source to destination mappings</returns>
    Task<Dictionary<string, string>> GetAllMappingsAsync(string entityType);

    /// <summary>
    /// Checks if a mapping exists for a source entity ID (existence check performance)
    /// </summary>
    /// <param name="entityType">Type of entity</param>
    /// <param name="sourceEntityId">Source entity ID to check</param>
    /// <returns>True if mapping exists, false otherwise</returns>
    Task<bool> MappingExistsAsync(string entityType, string sourceEntityId);
} 