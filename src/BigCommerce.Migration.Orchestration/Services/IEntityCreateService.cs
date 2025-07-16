using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Service responsible for creating entities in destination stores
/// Single Responsibility: Entity creation only
/// </summary>
public interface IEntityCreateService
{
    /// <summary>
    /// Creates entities of the specified type in the destination store
    /// </summary>
    /// <param name="entities">Entities to create</param>
    /// <param name="request">Batch processing request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created entities with destination IDs</returns>
    Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates categories with hierarchical relationships
    /// </summary>
    Task<List<Dictionary<string, object>>?> CreateCategoriesAsync(
        List<Dictionary<string, object>> categories, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates products with variant relationships
    /// </summary>
    Task<List<Dictionary<string, object>>?> CreateProductsAsync(
        List<Dictionary<string, object>> products, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates brands
    /// </summary>
    Task<List<Dictionary<string, object>>?> CreateBrandsAsync(
        List<Dictionary<string, object>> brands, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates variants for a specific product
    /// </summary>
    Task<List<Dictionary<string, object>>?> CreateVariantsAsync(
        List<Dictionary<string, object>> variants, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates images for a specific product
    /// </summary>
    Task<List<Dictionary<string, object>>?> CreateImagesAsync(
        List<Dictionary<string, object>> images, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates modifiers for a specific product
    /// </summary>
    Task<List<Dictionary<string, object>>?> CreateModifiersAsync(
        List<Dictionary<string, object>> modifiers, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken);
} 