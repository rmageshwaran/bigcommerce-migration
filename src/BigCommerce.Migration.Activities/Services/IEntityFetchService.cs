using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Service responsible for fetching entities from source stores
/// Single Responsibility: Entity data retrieval only
/// </summary>
public interface IEntityFetchService
{
    /// <summary>
    /// Fetches entities of the specified type from the source store
    /// </summary>
    /// <param name="request">Batch processing request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of source entities</returns>
    Task<List<Dictionary<string, object>>> FetchEntitiesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches categories with hierarchical ordering
    /// </summary>
    Task<List<Dictionary<string, object>>> FetchCategoriesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches products with memory-efficient pagination
    /// </summary>
    Task<List<Dictionary<string, object>>> FetchProductsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches brands with memory-efficient pagination
    /// </summary>
    Task<List<Dictionary<string, object>>> FetchBrandsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches variants for a specific product
    /// </summary>
    Task<List<Dictionary<string, object>>> FetchVariantsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches images for a specific product
    /// </summary>
    Task<List<Dictionary<string, object>>> FetchImagesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches modifiers for a specific product
    /// </summary>
    Task<List<Dictionary<string, object>>> FetchModifiersAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken);
} 