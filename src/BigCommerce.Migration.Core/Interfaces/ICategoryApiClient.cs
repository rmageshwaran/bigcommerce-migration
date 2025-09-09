using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for BigCommerce Category API operations
/// Follows Interface Segregation Principle - only category-related methods
/// </summary>
public interface ICategoryApiClient
{
    /// <summary>
    /// Gets category trees for a specific store and channel
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of category trees</returns>
    Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(
        StoreConfiguration storeConfig, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets category trees for a specific store and a list of channels
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="channelIds">A list of channel IDs to filter the trees by</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of category trees</returns>
    Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(
        StoreConfiguration storeConfig, 
        List<string> channelIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets categories from a specific category tree
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="categoryTreeId">Category tree identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of categories</returns>
    Task<List<Dictionary<string, object>>> GetCategoriesAsync(
        StoreConfiguration storeConfig, 
        string categoryTreeId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates categories in a specific category tree
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="categories">Categories to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created categories</returns>
    Task<List<Dictionary<string, object>>> CreateCategoriesAsync(
        StoreConfiguration storeConfig, 
        List<Dictionary<string, object>> categories, 
        CancellationToken cancellationToken = default);
} 
