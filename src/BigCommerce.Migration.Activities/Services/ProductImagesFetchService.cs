using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Service for fetching product images with streaming pagination support.
/// Implements memory-efficient fetching of up to 1000 images per product
/// using paginated requests to the BigCommerce API.
/// 
/// Key Features:
/// - Streaming fetch: Processes images in 50-image batches to manage memory
/// - 1000 image limit: Enforces maximum of 1000 images per product
/// - Timeout-safe: Designed to stay within function timeout limits
/// - Blob-based cancellation: Uses ICancellationStore for cancellation checks
/// - Comprehensive logging: Tracks progress and performance metrics
/// </summary>
public class ProductImagesFetchService : IProductImagesFetchService
{
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<ProductImagesFetchService> _logger;

    // Configuration constants for timeout-safe operation
    private const int DefaultPageSize = 50;  // BigCommerce API limit for images
    private const int MaxImagesPerProduct = 1000;  // User-specified limit
    private const int CancellationCheckInterval = 5;  // Check cancellation every 5 pages

    /// <summary>
    /// Initializes a new instance of ProductImagesFetchService
    /// </summary>
    /// <param name="apiRequestHandler">API request handler for making HTTP requests</param>
    /// <param name="cancellationStore">Cancellation store for blob-based cancellation</param>
    /// <param name="logger">Logger for the service</param>
    public ProductImagesFetchService(
        IApiRequestHandler apiRequestHandler,
        ICancellationStore cancellationStore,
        ILogger<ProductImagesFetchService> logger)
    {
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Fetches all images for a specific product using TRUE streaming pagination.
    /// Processes images in chunks of 50 via callback, avoiding memory accumulation.
    /// This is the RECOMMENDED method for products with many images.
    /// </summary>
    public async Task<int> FetchProductImagesStreamingAsync(
        string sourceProductId, 
        StoreConfiguration sourceStore,
        string migrationId,
        Func<List<Dictionary<string, object>>, Task> onImageChunkFetched,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🖼️ [FETCH-DEBUG] ===== STARTING IMAGE FETCH ===== ProductId: {ProductId}, Store: {StoreId}, Migration: {MigrationId}", 
            sourceProductId, sourceStore?.StoreId ?? "NULL", migrationId);
        
        if (string.IsNullOrWhiteSpace(sourceProductId))
        {
            _logger.LogError("🚨 [FETCH-DEBUG] FETCH FAILED: Source product ID is null/empty");
            throw new ArgumentException("Source product ID cannot be null or empty", nameof(sourceProductId));
        }
        
        if (sourceStore?.IsValid() != true)
        {
            _logger.LogError("🚨 [FETCH-DEBUG] FETCH FAILED: Invalid source store - StoreId: {StoreId}, IsValid: {IsValid}", 
                sourceStore?.StoreId ?? "NULL", sourceStore?.IsValid() ?? false);
            throw new ArgumentException("Invalid source store configuration", nameof(sourceStore));
        }

        if (string.IsNullOrWhiteSpace(migrationId))
        {
            _logger.LogError("🚨 [FETCH-DEBUG] FETCH FAILED: Migration ID is null/empty");
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        }

        if (onImageChunkFetched == null)
            throw new ArgumentNullException(nameof(onImageChunkFetched));

        var startTime = DateTime.UtcNow;
        var page = 1;
        var totalFetched = 0;

        _logger.LogInformation("🖼️ [PRODUCT-IMAGES-STREAM] Starting streaming fetch for product {ProductId} in migration {MigrationId} - Max: {MaxImages} images", 
            sourceProductId, migrationId, MaxImagesPerProduct);

        try
        {
            // 🚫 CANCELLATION: Initial check before starting
            await CheckCancellationAsync(migrationId);

            while (totalFetched < MaxImagesPerProduct)
            {
                // Calculate how many images to fetch on this page
                var remainingImages = MaxImagesPerProduct - totalFetched;
                var pageSize = Math.Min(DefaultPageSize, remainingImages);

                _logger.LogDebug("🖼️ [PRODUCT-IMAGES-STREAM] Fetching page {Page} (size: {PageSize}) for product {ProductId}", 
                    page, pageSize, sourceProductId);

                // Fetch the current page
                var pageImages = await FetchProductImagesPageAsync(
                    sourceProductId, sourceStore, page, pageSize, migrationId, cancellationToken);
                
                _logger.LogDebug("🖼️ [FETCH-DEBUG] Page {Page} fetched: {ImageCount} images for product {ProductId}", 
                    page, pageImages?.Count ?? 0, sourceProductId);

                // If no images returned, we've reached the end
                if (pageImages.Count == 0)
                {
                    _logger.LogDebug("🖼️ [PRODUCT-IMAGES-STREAM] No more images found at page {Page} for product {ProductId}", 
                        page, sourceProductId);
                    break;
                }

                // 🚀 STREAMING: Process this chunk immediately via callback
                await onImageChunkFetched(pageImages);
                
                totalFetched += pageImages.Count;

                _logger.LogDebug("🖼️ [PRODUCT-IMAGES-STREAM] Page {Page} processed: {PageCount} images (Total: {TotalFetched}/{MaxLimit})", 
                    page, pageImages.Count, totalFetched, MaxImagesPerProduct);

                // If we got fewer images than requested, we've reached the end
                if (pageImages.Count < pageSize)
                {
                    _logger.LogDebug("🖼️ [PRODUCT-IMAGES-STREAM] Reached end of images at page {Page} for product {ProductId} (got {ActualCount} < {RequestedCount})", 
                        page, sourceProductId, pageImages.Count, pageSize);
                    break;
                }

                // 🚫 CANCELLATION: Periodic check every 5 pages
                if (page % CancellationCheckInterval == 0)
                {
                    await CheckCancellationAsync(migrationId);
                }

                page++;
            }

            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("✅ [PRODUCT-IMAGES-STREAM] Streaming fetch completed for product {ProductId} - " +
                                 "Fetched: {TotalImages} images, Pages: {TotalPages}, Duration: {Duration}ms, Migration: {MigrationId}", 
                sourceProductId, totalFetched, page - 1, duration.TotalMilliseconds, migrationId);
            
            _logger.LogDebug("🖼️ [FETCH-DEBUG] ===== IMAGE FETCH COMPLETED ===== ProductId: {ProductId}, TotalImages: {TotalFetched}, Migration: {MigrationId}", 
                sourceProductId, totalFetched, migrationId);

            return totalFetched;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🚫 [PRODUCT-IMAGES-STREAM] Stream cancelled for product {ProductId} in migration {MigrationId} after {TotalFetched} images", 
                sourceProductId, migrationId, totalFetched);
            throw;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "🔥 [PRODUCT-IMAGES-STREAM] Failed to stream images for product {ProductId} in migration {MigrationId} - " +
                            "Fetched: {TotalFetched} images, Duration: {Duration}ms, Error: {ErrorType}", 
                sourceProductId, migrationId, totalFetched, duration.TotalMilliseconds, ex.GetType().Name);
            throw;
        }
    }



    /// <summary>
    /// Fetches a specific page of images for a product.
    /// Used internally by FetchProductImagesStreamingAsync for pagination,
    /// but exposed for testing and advanced scenarios.
    /// </summary>
    public async Task<List<Dictionary<string, object>>> FetchProductImagesPageAsync(
        string sourceProductId,
        StoreConfiguration sourceStore,
        int page,
        int limit,
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceProductId))
            throw new ArgumentException("Source product ID cannot be null or empty", nameof(sourceProductId));
        
        if (sourceStore?.IsValid() != true)
            throw new ArgumentException("Invalid source store configuration", nameof(sourceStore));

        if (page < 1)
            throw new ArgumentException("Page must be greater than 0", nameof(page));

        if (limit < 1 || limit > DefaultPageSize)
            throw new ArgumentException($"Limit must be between 1 and {DefaultPageSize}", nameof(limit));

        try
        {
            // Build the API URL with pagination parameters
            var url = $"{sourceStore.GetApiBaseUrl()}/catalog/products/{sourceProductId}/images?page={page}&limit={limit}";

            _logger.LogDebug("🌐 [PRODUCT-IMAGES-API] API Call: {Url}", url);

            // Create and execute the API request
            var request = ApiRequest.CreateGet(url, sourceStore);
            var apiStartTime = DateTime.UtcNow;
            
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            
            var apiDuration = DateTime.UtcNow - apiStartTime;

            // Parse the response to extract image data
            var images = ParseImageResponse(response, sourceProductId, migrationId);

            _logger.LogDebug("✅ [PRODUCT-IMAGES-API] Page {Page} success: {ImageCount} images, Duration: {Duration}ms, Product: {ProductId}", 
                page, images.Count, apiDuration.TotalMilliseconds, sourceProductId);

            return images;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PRODUCT-IMAGES-API] Failed to fetch page {Page} for product {ProductId} in migration {MigrationId}: {ErrorType}", 
                page, sourceProductId, migrationId, ex.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// Parses the BigCommerce API response to extract image data
    /// </summary>
    private List<Dictionary<string, object>> ParseImageResponse(
        Dictionary<string, object>? response, 
        string productId, 
        string migrationId)
    {
        var images = new List<Dictionary<string, object>>();

        try
        {
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var imageElement in dataElement.EnumerateArray())
                    {
                        // Convert JsonElement to Dictionary<string, object> for consistency
                        var imageDict = ConvertJsonElementToDictionary(imageElement);
                        
                        // Ensure product_id is set for downstream processing
                        if (!imageDict.ContainsKey("product_id"))
                        {
                            imageDict["product_id"] = productId;
                        }

                        images.Add(imageDict);
                    }
                }

                _logger.LogDebug("🔍 [PRODUCT-IMAGES-PARSE] Parsed {ImageCount} images from API response for product {ProductId}", 
                    images.Count, productId);
            }
            else
            {
                _logger.LogDebug("🔍 [PRODUCT-IMAGES-PARSE] No image data found in API response for product {ProductId}", productId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PRODUCT-IMAGES-PARSE] Failed to parse image response for product {ProductId} in migration {MigrationId}: {ErrorType}", 
                productId, migrationId, ex.GetType().Name);
            // Return empty list rather than throwing to allow migration to continue
        }

        return images;
    }

    /// <summary>
    /// Checks for blob-based cancellation using ICancellationStore
    /// </summary>
    private async Task CheckCancellationAsync(string migrationId)
    {
        try
        {
            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            if (isCancelled)
            {
                _logger.LogInformation("🚫 [PRODUCT-IMAGES-CANCEL] Migration {MigrationId} has been cancelled", migrationId);
                throw new OperationCanceledException($"Migration {migrationId} was cancelled via cancellation store");
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PRODUCT-IMAGES-CANCEL] Failed to check cancellation for migration {MigrationId}: {ErrorType}", 
                migrationId, ex.GetType().Name);
            // Continue processing if cancellation check fails - don't block migration
        }
    }

    /// <summary>
    /// Converts a JsonElement to a Dictionary<string, object> for consistent data handling
    /// </summary>
    private static Dictionary<string, object> ConvertJsonElementToDictionary(JsonElement element)
    {
        var dictionary = new Dictionary<string, object>();
        
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var value = ConvertJsonElementToObject(property.Value);
                if (value != null)
                {
                    dictionary[property.Name] = value;
                }
            }
        }
        
        return dictionary;
    }

    /// <summary>
    /// Converts a JsonElement to its appropriate object type
    /// </summary>
    private static object? ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : 
                                   element.TryGetInt64(out var longValue) ? longValue : 
                                   element.TryGetDecimal(out var decimalValue) ? decimalValue : 
                                   element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToObject).ToArray(),
            JsonValueKind.Object => ConvertJsonElementToDictionary(element),
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }
}
