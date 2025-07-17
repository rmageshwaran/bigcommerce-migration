using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Strategy pattern interface for creating entities in destination stores
/// Implements Open/Closed Principle: open for extension, closed for modification
/// Each entity type (categories, products, brands, etc.) implements this interface
/// </summary>
public interface IEntityCreationStrategy
{
    /// <summary>
    /// The entity type this strategy handles (e.g., "categories", "products", "brands")
    /// </summary>
    string EntityType { get; }

    /// <summary>
    /// Creates entities of the specified type in the destination store
    /// </summary>
    /// <param name="entities">List of entities to create</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (for categories)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created entities with destination IDs, or null if creation failed</returns>
    Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default);
} 