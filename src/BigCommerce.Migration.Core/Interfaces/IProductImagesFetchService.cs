using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service for fetching product images with streaming pagination support.
/// Designed specifically for Product-Images phase migration where we need to handle
/// up to 1000 images per product with memory-efficient streaming.
/// </summary>
public interface IProductImagesFetchService
{
    /// <summary>
    /// Fetches all images for a specific product using streaming pagination.
    /// Automatically handles pagination to retrieve up to 1000 images while
    /// managing memory efficiently by processing in batches of 50.
    /// 
    /// This method processes images in a streaming fashion using a callback,
    /// avoiding loading all 1000 images into memory simultaneously.
    /// </summary>
    /// <param name="sourceProductId">Source product ID to fetch images for</param>
    /// <param name="sourceStore">Source store configuration</param>
    /// <param name="migrationId">Migration ID for logging and tracking</param>
    /// <param name="onImageChunkFetched">Callback to process each chunk of images as they're fetched</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total number of images fetched</returns>
    Task<int> FetchProductImagesStreamingAsync(
        string sourceProductId, 
        StoreConfiguration sourceStore,
        string migrationId,
        Func<List<Dictionary<string, object>>, Task> onImageChunkFetched,
        CancellationToken cancellationToken = default);



    /// <summary>
    /// Fetches a specific page of images for a product.
    /// Used internally by FetchProductImagesStreamingAsync for pagination,
    /// but exposed for testing and advanced scenarios.
    /// </summary>
    /// <param name="sourceProductId">Source product ID to fetch images for</param>
    /// <param name="sourceStore">Source store configuration</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="limit">Number of images per page (max 50)</param>
    /// <param name="migrationId">Migration ID for logging and tracking</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of images for the specified page</returns>
    Task<List<Dictionary<string, object>>> FetchProductImagesPageAsync(
        string sourceProductId,
        StoreConfiguration sourceStore,
        int page,
        int limit,
        string migrationId,
        CancellationToken cancellationToken = default);
}
