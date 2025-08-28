using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Strategies.Transform;

/// <summary>
/// Transform strategy for product-images phase - Phase 4 of product migration
/// 
/// Handles transformation of product image data into BigCommerce product update payloads
/// with proper image URL processing and field mapping for individual product updates.
/// 
/// Key Features:
/// - Transforms arrays of product images for bulk update API calls
/// - Maintains image sort order and properties from source
/// - Maps image properties according to BigCommerce API requirements
/// - Handles image URL validation and field cleanup
/// - Creates individual product update payloads (not batch API)
/// - Preserves image metadata (alt_text, sort_order, is_thumbnail)
/// 
/// Business Logic:
/// - Input: Array of product images from ProductImagesFetchService
/// - Processing: Transform each image's properties and validate URLs
/// - Output: Product update payload { "id": productId, "images": [transformed_images] }
/// - Skip Case: Empty image array → skip product (counted as processed)
/// - URL Validation: Ensures image_url is present and valid
/// </summary>
public class ProductImagesTransformStrategy : IEntityTransformStrategy
{
    #region Private Fields

    private readonly ILogger<ProductImagesTransformStrategy> _logger;

    // Required image fields for BigCommerce API (per user specification)
    private static readonly string[] RequiredImageFields = 
    { 
        "image_url", "is_thumbnail", "sort_order", "description"
    };

    // Fields to remove from source images (BigCommerce source-specific)
    private static readonly string[] SourceOnlyFields = 
    { 
        "id", "product_id", "date_created", "date_modified", "alt_text"
    };

    #endregion

    #region Properties

    /// <summary>
    /// Entity type handled by this strategy
    /// </summary>
    public string EntityType => "product-images";

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of ProductImagesTransformStrategy
    /// </summary>
    /// <param name="logger">Logger for the strategy</param>
    public ProductImagesTransformStrategy(ILogger<ProductImagesTransformStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Transforms product image data into product update payload for BigCommerce Individual Product API
    /// 
    /// Note: For product-images phase, the input contains:
    /// - productId: The destination product ID to update
    /// - images: Array of image objects from ProductImagesFetchService
    /// </summary>
    /// <param name="entity">Data containing productId and images array</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="sourceStore">Source store configuration (used for logging)</param>
    /// <param name="destinationStore">Destination store configuration (used for logging)</param>
    /// <param name="categoryTreeContext">Category tree context (not used in this phase)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Product update payload for BigCommerce Individual Product API, or null if should be skipped</returns>
    public async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> entity,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            _logger.LogWarning("⚠️ [PRODUCT-IMAGES-TRANSFORM] Null entity provided for migration {MigrationId}", migrationId);
            return null!;
        }
        
        _logger.LogInformation("🔄 [TRANSFORM-DEBUG] ===== STARTING IMAGE TRANSFORM ===== Migration: {MigrationId}, Entity keys: [{Keys}]", 
            migrationId, string.Join(", ", entity.Keys));

        try
        {
            // Extract product ID and images from input
            var (productId, images) = ExtractProductAndImages(entity, migrationId);
            
            if (string.IsNullOrEmpty(productId))
            {
                _logger.LogWarning("⚠️ [PRODUCT-IMAGES-TRANSFORM] Missing or invalid productId in entity for migration {MigrationId}", migrationId);
                return null!;
            }

            // This strategy should ONLY receive actual image data, not EntityMappings
            if (images == null || images.Count == 0)
            {
                _logger.LogWarning("⚠️ [PRODUCT-IMAGES-TRANSFORM] No images provided for transformation. This strategy should only handle actual image data, not EntityMappings (product: {ProductId}, migration: {MigrationId})", 
                    productId, migrationId);
                return null!;
            }

            _logger.LogInformation("🖼️ [PRODUCT-IMAGES-TRANSFORM] Transforming {ImageCount} images for product {ProductId} (migration: {MigrationId})", 
                images.Count, productId, migrationId);

            // Transform each image
            var transformedImages = new List<Dictionary<string, object>>();
            var validImageCount = 0;
            var skippedImageCount = 0;

            for (int i = 0; i < images.Count; i++)
            {
                var image = images[i];
                var transformedImage = await TransformSingleImageAsync(image, i + 1, productId, sourceStore, migrationId, cancellationToken);
                
                if (transformedImage != null)
                {
                    transformedImages.Add(transformedImage);
                    validImageCount++;
                }
                else
                {
                    skippedImageCount++;
                }
            }

            if (transformedImages.Count == 0)
            {
                _logger.LogWarning("⚠️ [PRODUCT-IMAGES-TRANSFORM] No valid images after transformation for product {ProductId} (migration: {MigrationId})", 
                    productId, migrationId);
                return null!; // No valid images to update
            }

            // Create the product update payload with source information for image fetching
            var updatePayload = new Dictionary<string, object>
            {
                ["id"] = productId,
                ["images"] = transformedImages,
                // Include source product information for the creation strategy to fetch images
                ["source_id"] = entity.GetValueOrDefault("SourceId")?.ToString() ?? entity.GetValueOrDefault("source_id")?.ToString() ?? "",
                ["destination_id"] = productId,
                ["source_store_id"] = sourceStore?.StoreId ?? "",
                ["source_store_token"] = sourceStore?.AccessToken ?? ""
            };

            _logger.LogInformation("✅ [PRODUCT-IMAGES-TRANSFORM] Transformed product {ProductId}: {ValidImages} valid images, {SkippedImages} skipped (migration: {MigrationId})", 
                productId, validImageCount, skippedImageCount, migrationId);

            return updatePayload;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PRODUCT-IMAGES-TRANSFORM] Failed to transform images for migration {MigrationId}: {ErrorType}", 
                migrationId, ex.GetType().Name);
            throw;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Extracts product ID and images array from the input entity
    /// </summary>
    private (string productId, List<Dictionary<string, object>>? images) ExtractProductAndImages(
        Dictionary<string, object> entity, 
        string migrationId)
    {
        try
        {
            // Extract product ID - check both EntityMappings format (DestinationId) and direct format (productId)
            string? productId = null;
            
            // First try DestinationId (from EntityMappings table)
            if (entity.TryGetValue("DestinationId", out var destIdValue) && destIdValue != null)
            {
                productId = destIdValue.ToString();
            }
            // Fallback to destination_id (lowercase)
            else if (entity.TryGetValue("destination_id", out var destIdLowerValue) && destIdLowerValue != null)
            {
                productId = destIdLowerValue.ToString();
            }
            // Fallback to productId (direct API format)
            else if (entity.TryGetValue("productId", out var productIdValue) && productIdValue != null)
            {
                productId = productIdValue.ToString();
            }
            
            if (string.IsNullOrEmpty(productId))
            {
                _logger.LogWarning("⚠️ [PRODUCT-IMAGES-TRANSFORM] Missing 'DestinationId', 'destination_id', or 'productId' field in entity for migration {MigrationId}", migrationId);
                return (string.Empty, null);
            }

            // Extract images array
            if (!entity.TryGetValue("images", out var imagesValue) || imagesValue == null)
            {
                _logger.LogDebug("⏭️ [PRODUCT-IMAGES-TRANSFORM] No 'images' field in entity for product {ProductId} (migration: {MigrationId})", 
                    productId, migrationId);
                return (productId, null);
            }

            List<Dictionary<string, object>>? images = null;

            // Handle different input formats
            if (imagesValue is List<Dictionary<string, object>> imagesList)
            {
                images = imagesList;
            }
            else if (imagesValue is IEnumerable<object> enumerable)
            {
                images = enumerable
                    .OfType<Dictionary<string, object>>()
                    .ToList();
            }
            else if (imagesValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                images = new List<Dictionary<string, object>>();
                foreach (var imageElement in jsonElement.EnumerateArray())
                {
                    var imageDict = ConvertJsonElementToDictionary(imageElement);
                    if (imageDict.Count > 0)
                    {
                        images.Add(imageDict);
                    }
                }
            }

            if (images == null)
            {
                _logger.LogWarning("⚠️ [PRODUCT-IMAGES-TRANSFORM] Unable to parse 'images' field for product {ProductId} (migration: {MigrationId})", 
                    productId, migrationId);
                return (productId, null);
            }

            _logger.LogDebug("📤 [PRODUCT-IMAGES-TRANSFORM] Extracted {ImageCount} images for product {ProductId} (migration: {MigrationId})", 
                images.Count, productId, migrationId);

            return (productId, images);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PRODUCT-IMAGES-TRANSFORM] Failed to extract product and images from entity for migration {MigrationId}: {ErrorType}", 
                migrationId, ex.GetType().Name);
            return (string.Empty, null);
        }
    }

    /// <summary>
    /// Transforms a single image object for BigCommerce API
    /// </summary>
    private Task<Dictionary<string, object>?> TransformSingleImageAsync(
        Dictionary<string, object> sourceImage,
        int imageIndex,
        string productId,
        StoreConfiguration sourceStore,
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var transformedImage = new Dictionary<string, object>();

            // Validate required fields
            if (!ValidateRequiredImageFields(sourceImage, imageIndex, migrationId))
            {
                return Task.FromResult<Dictionary<string, object>?>(null); // Skip invalid images
            }

            // Copy and transform image fields (only the required fields)
            foreach (var field in RequiredImageFields)
            {
                if (field == "image_url")
                {
                    // Special handling for image_url - construct virtual path from image_file
                    transformedImage[field] = ConstructVirtualPathImageUrl(sourceImage, sourceStore, imageIndex, migrationId);
                }
                else if (sourceImage.TryGetValue(field, out var value) && value != null)
                {
                    transformedImage[field] = TransformImageFieldValue(field, value, imageIndex, sourceStore, migrationId);
                }
            }

            // Set default values for missing required fields
            SetDefaultImageValues(transformedImage, imageIndex, migrationId, productId);

            // Validate the transformed image
            if (!ValidateTransformedImage(transformedImage, imageIndex, migrationId))
            {
                return Task.FromResult<Dictionary<string, object>?>(null);
            }

            _logger.LogDebug("🔄 [PRODUCT-IMAGES-TRANSFORM] Transformed image {ImageIndex}: url={ImageUrl}, sort_order={SortOrder} (migration: {MigrationId})", 
                imageIndex, 
                transformedImage.TryGetValue("image_url", out var url) ? url?.ToString()?[..Math.Min(50, url.ToString()?.Length ?? 0)] + "..." : "N/A",
                transformedImage.TryGetValue("sort_order", out var sortOrder) ? sortOrder : "N/A",
                migrationId);

            return Task.FromResult<Dictionary<string, object>?>(transformedImage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PRODUCT-IMAGES-TRANSFORM] Failed to transform image {ImageIndex} for migration {MigrationId}: {ErrorType}", 
                imageIndex, migrationId, ex.GetType().Name);
            return Task.FromResult<Dictionary<string, object>?>(null); // Skip failed images
        }
    }

    /// <summary>
    /// Validates that required fields are present in the source image
    /// Only image_file is truly required for virtual path URL construction - others will have defaults
    /// </summary>
    private bool ValidateRequiredImageFields(Dictionary<string, object> sourceImage, int imageIndex, string migrationId)
    {
        // Only image_file is truly required to construct virtual path URL
        // If image_file is missing, we can fallback to url_standard
        if (!sourceImage.TryGetValue("image_file", out var imageFileValue) || 
            string.IsNullOrWhiteSpace(imageFileValue?.ToString()))
        {
            // Check for fallback URL
            if (!sourceImage.TryGetValue("url_standard", out var urlValue) || 
                string.IsNullOrWhiteSpace(urlValue?.ToString()))
            {
                _logger.LogWarning("⚠️ [PRODUCT-IMAGES-TRANSFORM] Image {ImageIndex} missing both 'image_file' and 'url_standard' fields (migration: {MigrationId})", 
                    imageIndex, migrationId);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Transforms a specific field value for BigCommerce API compatibility
    /// </summary>
    private object TransformImageFieldValue(string fieldName, object value, int imageIndex, StoreConfiguration sourceStore, string migrationId)
    {
        try
        {
            return fieldName.ToLowerInvariant() switch
            {
                "description" => value?.ToString()?.Trim() ?? string.Empty,
                "sort_order" => ConvertToInteger(value, imageIndex, migrationId),
                "is_thumbnail" => ConvertToBoolean(value, imageIndex, migrationId),
                _ => value
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PRODUCT-IMAGES-TRANSFORM] Failed to transform field '{FieldName}' for image {ImageIndex} (migration: {MigrationId}): using original value", 
                fieldName, imageIndex, migrationId);
            return value;
        }
    }

    /// <summary>
    /// Constructs virtual path image URL using the image_file field from API response
    /// Format: https://store-{storeId}.mybigcommerce.com/product_images/{image_file}
    /// </summary>
    private string ConstructVirtualPathImageUrl(Dictionary<string, object> sourceImage, StoreConfiguration sourceStore, int imageIndex, string migrationId)
    {
        // Get the image_file field from the API response
        if (!sourceImage.TryGetValue("image_file", out var imageFileValue) || 
            string.IsNullOrWhiteSpace(imageFileValue?.ToString()))
        {
            _logger.LogWarning("⚠️ [PRODUCT-IMAGES-TRANSFORM] Missing or empty 'image_file' field for image {ImageIndex} (migration: {MigrationId})", 
                imageIndex, migrationId);
            
            // Fallback to url_standard if available
            if (sourceImage.TryGetValue("url_standard", out var urlStandardValue) && 
                !string.IsNullOrWhiteSpace(urlStandardValue?.ToString()))
            {
                _logger.LogDebug("🔄 [PRODUCT-IMAGES-TRANSFORM] Using url_standard as fallback for image {ImageIndex} (migration: {MigrationId})", 
                    imageIndex, migrationId);
                return urlStandardValue.ToString()!.Trim();
            }
            
            return string.Empty;
        }

        var imageFile = imageFileValue.ToString()!.Trim();
        
        // Construct virtual path URL: https://store-{storeId}.mybigcommerce.com/product_images/{image_file}
        var virtualPathUrl = $"https://store-{sourceStore.StoreId}.mybigcommerce.com/product_images/{imageFile}";
        
        _logger.LogDebug("🔄 [PRODUCT-IMAGES-TRANSFORM] Constructed virtual path for image {ImageIndex}: {ImageFile} -> {VirtualPath} (migration: {MigrationId})", 
            imageIndex, imageFile, virtualPathUrl, migrationId);
            
        return virtualPathUrl;
    }

    /// <summary>
    /// Converts a value to integer with fallback
    /// </summary>
    private int ConvertToInteger(object value, int imageIndex, string migrationId)
    {
        if (value == null) return imageIndex; // Use image index as default sort order

        if (value is int intValue) return intValue;
        if (value is long longValue) return (int)longValue;
        if (value is double doubleValue) return (int)doubleValue;
        if (value is decimal decimalValue) return (int)decimalValue;

        if (int.TryParse(value.ToString(), out var parsedValue))
        {
            return parsedValue;
        }

        _logger.LogDebug("🔄 [PRODUCT-IMAGES-TRANSFORM] Could not convert '{Value}' to integer for image {ImageIndex}, using {DefaultValue} (migration: {MigrationId})", 
            value, imageIndex, imageIndex, migrationId);
        
        return imageIndex; // Fallback to image index
    }

    /// <summary>
    /// Converts a value to boolean with fallback
    /// </summary>
    private bool ConvertToBoolean(object value, int imageIndex, string migrationId)
    {
        if (value == null) return false;

        if (value is bool boolValue) return boolValue;

        if (bool.TryParse(value.ToString(), out var parsedValue))
        {
            return parsedValue;
        }

        // Handle common boolean representations
        var stringValue = value.ToString()?.ToLowerInvariant();
        if (stringValue == "1" || stringValue == "true" || stringValue == "yes") return true;
        if (stringValue == "0" || stringValue == "false" || stringValue == "no") return false;

        _logger.LogDebug("🔄 [PRODUCT-IMAGES-TRANSFORM] Could not convert '{Value}' to boolean for image {ImageIndex}, using false (migration: {MigrationId})", 
            value, imageIndex, migrationId);
        
        return false; // Default to false
    }

    /// <summary>
    /// Sets default values for missing required fields
    /// </summary>
    private void SetDefaultImageValues(Dictionary<string, object> transformedImage, int imageIndex, string migrationId, string productId)
    {
        // Set product_id (required for BigCommerce API)
        transformedImage["product_id"] = productId;

        // Set default sort order if not present (use image index for natural ordering)
        if (!transformedImage.ContainsKey("sort_order"))
        {
            transformedImage["sort_order"] = imageIndex;
        }

        // Set default is_thumbnail if not present (only first image is thumbnail by default)
        if (!transformedImage.ContainsKey("is_thumbnail"))
        {
            transformedImage["is_thumbnail"] = imageIndex == 1;
        }

        // Set default description if not present
        if (!transformedImage.ContainsKey("description"))
        {
            transformedImage["description"] = string.Empty;
        }
    }

    /// <summary>
    /// Validates the transformed image before including it in the update payload
    /// </summary>
    private bool ValidateTransformedImage(Dictionary<string, object> transformedImage, int imageIndex, string migrationId)
    {
        // Ensure image_url is present and not empty
        if (!transformedImage.TryGetValue("image_url", out var imageUrl) || 
            string.IsNullOrWhiteSpace(imageUrl?.ToString()))
        {
            _logger.LogWarning("⚠️ [PRODUCT-IMAGES-TRANSFORM] Transformed image {ImageIndex} has no valid image_url (migration: {MigrationId})", 
                imageIndex, migrationId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Converts a JsonElement to a Dictionary<string, object> for consistent data handling
    /// </summary>
    private Dictionary<string, object> ConvertJsonElementToDictionary(JsonElement element)
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
    private object? ConvertJsonElementToObject(JsonElement element)
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

    #endregion
}
