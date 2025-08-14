using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Activities.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating product images in destination stores
/// Implements Open/Closed Principle: specific to images without modifying base service
/// Phase 3.3.4.3: Enhanced with blob-based cooperative cancellation support
/// </summary>
public class ImageCreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<ImageCreationStrategy> _logger;
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly IMigrationStorageService _migrationStorageService;

    public ImageCreationStrategy(
        IBigCommerceApiClient apiClient,
        ICancellationStore cancellationStore,
        ILogger<ImageCreationStrategy> logger,
        IApiRequestHandler apiRequestHandler,
        IMigrationStorageService migrationStorageService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
    }

    /// <summary>
    /// The entity type this strategy handles
    /// </summary>
    public string EntityType => "images";

    /// <summary>
    /// Creates product images in the destination store
    /// </summary>
    /// <param name="entities">Images to create</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (not used for images)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created images with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No images provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        var executionId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("🖼️ [IMG-{ExecutionId}] Creating {Count} images for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        // Phase 3.3.4.3: Check for cancellation at start of images creation
        await CheckCancellationAsync(migrationId);

        try
        {
            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Log image details for debugging
            foreach (var image in entities)
            {
                var imageUrl = image.TryGetValue("image_url", out var url) ? url?.ToString() : "unknown";
                var productId = image.TryGetValue("product_id", out var prodId) ? prodId?.ToString() : "unknown";
                
                _logger.LogDebug("🖼️ [IMG-{ExecutionId}] Creating image: URL='{ImageUrl}', ProductId='{ProductId}' in migration {MigrationId}",
                    executionId, imageUrl, productId, migrationId);
            }

            // Note: The API client doesn't have individual image creation methods, so we'll implement mock creation
            var createdImages = new List<Dictionary<string, object>>();

            int imageIndex = 0;
            foreach (var image in entities)
            {
                // Phase 3.3.4.3: Periodic cancellation check (every 5 images)
                if (imageIndex > 0 && imageIndex % 5 == 0)
                {
                    await CheckCancellationAsync(migrationId);
                }
                imageIndex++;

                try
                {
                    if (!image.TryGetValue("product_id", out var productId))
                    {
                        _logger.LogWarning("🖼️ [IMG-{ExecutionId}] Image missing product_id for migration {MigrationId}", 
                            executionId, migrationId);
                        continue;
                    }

                    // Create image using BigCommerce API
                    // Based on: https://developer.bigcommerce.com/docs/rest-catalog/products/images#create-a-product-image
                    var createdImage = await CreateImageAsync(image, destinationStore, migrationId, executionId, cancellationToken);
                    if (createdImage != null)
                    {
                        createdImages.Add(createdImage);
                    }
                    
                    var imageUrl = image.TryGetValue("image_url", out var url) ? url?.ToString() : "no-url";
                    _logger.LogDebug("🖼️ [IMG-{ExecutionId}] Successfully created image URL='{ImageUrl}' for product {ProductId} in migration {MigrationId}", 
                        executionId, imageUrl, productId, migrationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🖼️ [IMG-{ExecutionId}] Failed to create individual image for migration {MigrationId}", 
                        executionId, migrationId);
                    // Continue with next image instead of failing entire batch
                }
            }

            _logger.LogInformation("✅ [IMG-{ExecutionId}] Successfully created {CreatedCount}/{TotalCount} images for migration {MigrationId}", 
                executionId, createdImages.Count, entities.Count, migrationId);

            return createdImages;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "🖼️ [IMG-{ExecutionId}] ❌ Failed to create {ImageCount} images in migration {MigrationId}. {DetailedError}",
                executionId, entities.Count, migrationId, detailedErrorMessage);

            // Return empty list to avoid causing Durable Functions replay issues
            _logger.LogWarning("🖼️ [IMG-{ExecutionId}] Returning empty result to prevent replay in migration {MigrationId}", 
                executionId, migrationId);
            
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Validates store configuration
    /// </summary>
    /// <param name="storeConfig">Store configuration to validate</param>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    private static void ValidateStoreConfiguration(StoreConfiguration storeConfig)
    {
        if (storeConfig == null || !storeConfig.IsValid())
            throw new ArgumentException("Invalid destination store configuration");
    }

    /// <summary>
    /// Extracts detailed error information from API exceptions
    /// </summary>
    /// <param name="exception">The exception to analyze</param>
    /// <returns>Detailed error message if available, otherwise empty string</returns>
    private static string ExtractDetailedErrorMessage(Exception exception)
    {
        if (exception == null) return string.Empty;

        // Check for inner exceptions with detailed error messages
        var innerException = exception.InnerException;
        while (innerException != null)
        {
            if (!string.IsNullOrEmpty(innerException.Message) && 
                innerException.Message != exception.Message)
            {
                return innerException.Message;
            }
            innerException = innerException.InnerException;
        }

        return exception.Message;
    }

    /// <summary>
    /// Creates a single image using BigCommerce API
    /// Based on: https://developer.bigcommerce.com/docs/rest-catalog/products/images#create-a-product-image
    /// </summary>
    private async Task<Dictionary<string, object>?> CreateImageAsync(
        Dictionary<string, object> image,
        StoreConfiguration destinationStore,
        string migrationId,
        string executionId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!image.TryGetValue("product_id", out var productIdObj) || productIdObj == null)
            {
                _logger.LogWarning("🖼️ [IMG-{ExecutionId}] Cannot create image - missing product_id", executionId);
                return null;
            }

                        // Use product_id directly (already mapped by transform strategy)
            var destinationProductId = productIdObj.ToString()!;
            _logger.LogDebug("🖼️ [IMG-{ExecutionId}] Using product_id {ProductId} (already mapped by transform strategy)", 
                executionId, destinationProductId);

            // Prepare image payload for BigCommerce API
            var imagePayload = await PrepareImagePayloadAsync(image, destinationProductId, migrationId);
            
            // Build the API request URL for product images
            var url = $"{destinationStore.GetApiBaseUrl()}/catalog/products/{destinationProductId}/images";

            // Serialize the image data
            var jsonContent = JsonSerializer.Serialize(imagePayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            _logger.LogDebug("🖼️ [IMG-{ExecutionId}] 🌐 API CALL: URL={Url}, ProductId={ProductId}", 
                executionId, url, destinationProductId);
            
            // Create and execute the API request
            var request = ApiRequest.CreatePost(url, jsonContent, destinationStore);
            
            var apiStartTime = DateTime.UtcNow;
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            var apiDuration = DateTime.UtcNow - apiStartTime;
            
            _logger.LogDebug("🖼️ [IMG-{ExecutionId}] ✅ API SUCCESS: ProductId={ProductId}, Duration={Duration}ms", 
                executionId, destinationProductId, apiDuration.TotalMilliseconds);
            
            // Handle the response data 
            Dictionary<string, object>? createdImage = null;
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                createdImage = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText());
            }
            else if (response != null && response.ContainsKey("id"))
            {
                createdImage = response;
            }

            if (createdImage != null)
            {
                var createdId = createdImage.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                _logger.LogDebug("✅ [IMG-{ExecutionId}] Successfully created image with ID {ImageId} for product {ProductId}",
                    executionId, createdId, destinationProductId);
                return createdImage;
            }

            _logger.LogWarning("⚠️ [IMG-{ExecutionId}] Unexpected API response format for image creation on product {ProductId}",
                executionId, destinationProductId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🖼️ [IMG-{ExecutionId}] Failed to create image", executionId);
            return null;
        }
    }

    /// <summary>
    /// Gets destination product ID from entity mapping
    /// </summary>
    private async Task<string?> GetDestinationProductId(string sourceProductId, string migrationId)
    {
        try
        {
            var mapping = await _migrationStorageService.GetEntityMappingAsync(migrationId, "products", sourceProductId);
            return mapping?.DestinationId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get destination product ID for source product {SourceProductId} in migration {MigrationId}", 
                sourceProductId, migrationId);
            return null;
        }
    }

    /// <summary>
    /// Prepares image payload according to BigCommerce API requirements
    /// </summary>
    private async Task<Dictionary<string, object>> PrepareImagePayloadAsync(Dictionary<string, object> sourceImage, string destinationProductId, string migrationId)
    {
        var payload = new Dictionary<string, object>();

        // ✅ Copy ONLY the specified fields: image_url, is_thumbnail, sort_order, description
        if (sourceImage.TryGetValue("is_thumbnail", out var isThumbnail)) payload["is_thumbnail"] = isThumbnail;
        if (sourceImage.TryGetValue("sort_order", out var sortOrder)) payload["sort_order"] = sortOrder;
        if (sourceImage.TryGetValue("description", out var description)) payload["description"] = description;

        // ✅ Construct image_url from image_file using proper store URL format
        if (sourceImage.TryGetValue("image_file", out var imageFileObj) && !string.IsNullOrWhiteSpace(imageFileObj?.ToString()))
        {
            var imageFile = imageFileObj.ToString();
            
            try
            {
                // Get source store hash from migration context
                var migration = await _migrationStorageService.GetMigrationAsync(migrationId);
                var sourceStoreId = migration?.SourceStoreId ?? "unknown-store";
                var constructedImageUrl = $"https://store-{sourceStoreId}.mybigcommerce.com/product_images/{imageFile}";
                
                payload["image_url"] = constructedImageUrl;
                _logger.LogDebug("🖼️ [IMG] Constructed image_url from image_file: {ImageFile} → {ImageUrl} (Source Store: {SourceStoreId})", 
                    imageFile, constructedImageUrl, sourceStoreId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🖼️ [IMG] Failed to get source store ID for migration {MigrationId}, cannot construct image URL", migrationId);
                return null; // Skip image creation if we can't construct proper URL
            }
        }
        else if (sourceImage.TryGetValue("image_url", out var existingImageUrl) && !string.IsNullOrWhiteSpace(existingImageUrl?.ToString()))
        {
            // Use existing image_url if available
            payload["image_url"] = existingImageUrl;
            _logger.LogDebug("🖼️ [IMG] Using existing image_url: {ImageUrl}", existingImageUrl);
        }
        else
        {
            _logger.LogWarning("🖼️ [IMG] No image_file or image_url found - skipping image creation");
            return null; // Skip image creation if no image source is available
        }

        return payload;
    }

    /// <summary>
    /// Phase 3.3.4.3: Helper method to check for blob-based cancellation
    /// Uses cooperative cancellation pattern suitable for Azure Functions activities
    /// </summary>
    private async Task CheckCancellationAsync(string migrationId)
    {
        try
        {
            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            if (isCancelled)
            {
                var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                _logger.LogInformation("🚫 [CANCELLATION] Images creation cancelled: {Reason}", reason);
                throw new OperationCanceledException($"Migration cancelled: {reason}");
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CANCELLATION-CHECK] Failed to check cancellation flag for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't throw - continue processing if cancellation check fails
        }
    }
} 