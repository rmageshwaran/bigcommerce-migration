using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Strategy pattern interface for fetching entities from source stores
/// Implements Open/Closed Principle: open for extension, closed for modification
/// Each entity type (categories, products, brands, etc.) implements this interface
/// </summary>
public interface IEntityFetchStrategy
{
    /// <summary>
    /// The entity type this strategy handles (e.g., "categories", "products", "brands")
    /// </summary>
    string EntityType { get; }

    /// <summary>
    /// Fetches entities of the specified type from the source store
    /// Handles entity-specific fetching logic such as parallel processing, API endpoints, etc.
    /// </summary>
    /// <param name="entityIds">List of entity IDs to fetch</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="sourceStore">Source store configuration</param>
    /// <param name="discoveredEntities">A list of previously discovered entities to be used instead of fetching from the API.</param>
    /// <param name="categoryTreeContext">Category tree context (for categories)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of fetched entities, or empty list if no entities found</returns>
    Task<List<Dictionary<string, object>>> FetchEntitiesAsync(
        List<string> entityIds,
        string migrationId,
        StoreConfiguration sourceStore,
        List<Dictionary<string, object>>? discoveredEntities = null,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default);
} 