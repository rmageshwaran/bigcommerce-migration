using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for BigCommerce batch API operations
/// Provides bulk operations to reduce API call count and improve performance
/// Follows Interface Segregation Principle - focused on batch operations only
/// </summary>
public interface IBatchApiClient
{
    /// <summary>
    /// Creates multiple products in a single batch operation
    /// Reduces API calls from N individual calls to 1 batch call
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="products">Products to create in batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created products with assigned IDs</returns>
    Task<List<Dictionary<string, object>>> CreateProductsBatchAsync(
        StoreConfiguration storeConfig,
        List<Dictionary<string, object>> products,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates multiple products in a single batch operation
    /// Uses BigCommerce PUT /v3/catalog/products endpoint for batch updates
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="products">Products to update in batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of updated products</returns>
    Task<List<Dictionary<string, object>>> UpdateProductsBatchAsync(
        StoreConfiguration storeConfig,
        List<Dictionary<string, object>> products,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates multiple categories in a single batch operation
    /// Reduces API calls significantly for category tree creation
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="categories">Categories to create in batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created categories with assigned IDs</returns>
    Task<List<Dictionary<string, object>>> CreateCategoriesBatchAsync(
        StoreConfiguration storeConfig,
        List<Dictionary<string, object>> categories,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates multiple variants for a product in a single batch operation
    /// Uses BigCommerce batch variant creation for efficiency
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="productId">Parent product ID</param>
    /// <param name="variants">Variants to create in batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created variants with assigned IDs</returns>
    Task<List<Dictionary<string, object>>> CreateVariantsBatchAsync(
        StoreConfiguration storeConfig,
        string productId,
        List<Dictionary<string, object>> variants,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates multiple variants in a single batch operation
    /// Uses BigCommerce PUT /v3/catalog/products/{id}/variants endpoint
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="productId">Parent product ID</param>
    /// <param name="variants">Variants to update in batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of updated variants</returns>
    Task<List<Dictionary<string, object>>> UpdateVariantsBatchAsync(
        StoreConfiguration storeConfig,
        string productId,
        List<Dictionary<string, object>> variants,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates multiple brands in a single batch operation
    /// Optimizes brand creation for large migrations
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="brands">Brands to create in batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created brands with assigned IDs</returns>
    Task<List<Dictionary<string, object>>> CreateBrandsBatchAsync(
        StoreConfiguration storeConfig,
        List<Dictionary<string, object>> brands,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets performance metrics for batch operations
    /// Helps track API call reduction and performance improvements
    /// </summary>
    /// <param name="storeConfig">Store configuration</param>
    /// <param name="timeframeHours">Timeframe for metrics (hours)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch operation performance metrics</returns>
    Task<BatchPerformanceMetrics> GetBatchPerformanceMetricsAsync(
        StoreConfiguration storeConfig,
        int timeframeHours,
        CancellationToken cancellationToken);
} 