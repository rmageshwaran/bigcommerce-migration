using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for BigCommerce Product API operations
/// Follows Interface Segregation Principle - only product-related methods
/// </summary>
public interface IProductApiClient
{
    /// <summary>
    /// Gets products from a specific store and channel with pagination
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="page">Page number for pagination</param>
    /// <param name="limit">Number of products per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of products</returns>
    Task<List<Dictionary<string, object>>> GetProductsAsync(
        StoreConfiguration storeConfig, 
        int page = 1, 
        int limit = 50, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates products in a specific store and channel
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="products">Products to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created products</returns>
    Task<List<Dictionary<string, object>>> CreateProductsAsync(
        StoreConfiguration storeConfig, 
        List<Dictionary<string, object>> products, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets product variants for a specific product
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="productId">Product ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of product variant summaries</returns>
    Task<List<ProductVariantSummary>> GetProductVariantsAsync(
        StoreConfiguration storeConfig, 
        int productId, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets product images for a specific product
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="productId">Product ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of product image summaries</returns>
    Task<List<ProductImageSummary>> GetProductImagesAsync(
        StoreConfiguration storeConfig, 
        int productId, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets product modifiers for a specific product
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="productId">Product ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of product modifier summaries</returns>
    Task<List<ProductModifierSummary>> GetProductModifiersAsync(
        StoreConfiguration storeConfig, 
        int productId, 
        CancellationToken cancellationToken);
} 