using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Strategy pattern interface for transforming entities during migration
/// Implements Open/Closed Principle: open for extension, closed for modification
/// Each entity type (categories, products, brands, etc.) implements this interface
/// </summary>
public interface IEntityTransformStrategy
{
    /// <summary>
    /// The entity type this strategy handles (e.g., "categories", "products", "brands")
    /// </summary>
    string EntityType { get; }

    /// <summary>
    /// Transforms entity data for the specified entity type
    /// Applies entity-specific transformation logic such as field mapping, data normalization, etc.
    /// </summary>
    /// <param name="entity">Entity data to transform</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="sourceStore">Source store configuration</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (for categories)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transformed entity data ready for destination store</returns>
    Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> entity,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default);
} 