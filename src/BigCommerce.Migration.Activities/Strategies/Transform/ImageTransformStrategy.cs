using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Transform;

/// <summary>
/// Transform strategy for image entities
/// Handles product mapping and image URL validation
/// </summary>
public class ImageTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<ImageTransformStrategy> _logger;
    private readonly IMigrationStorageService _migrationStorageService;

    public string EntityType => "images";

    public ImageTransformStrategy(
        ILogger<ImageTransformStrategy> logger,
        IMigrationStorageService migrationStorageService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
    }

    public async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> entity,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        var transformed = new Dictionary<string, object>(entity);

        // 🔗 PRODUCT ASSIGNMENT: Ensure product_id is properly mapped for images
        await EnsureProductIdMappingAsync(transformed, migrationId, cancellationToken);

        // 🔧 IMAGE VALIDATION: Validate and set required fields
        ValidateAndSetRequiredFields(transformed, migrationId);

        // 🧹 CLEANUP: Remove null values and source-specific fields
        CleanupImageData(transformed);

        _logger.LogDebug("✅ [IMAGES-TRANSFORM] Transformed image entity for migration {MigrationId}: has_image_url={HasImageUrl}",
            migrationId, transformed.ContainsKey("image_url"));

        return transformed;
    }

    /// <summary>
    /// Ensures product_id is properly mapped from source to destination product ID
    /// </summary>
    private async Task EnsureProductIdMappingAsync(
        Dictionary<string, object> transformed,
        string migrationId,
        CancellationToken cancellationToken)
    {
        if (!transformed.TryGetValue("product_id", out var productIdValue) || productIdValue == null)
        {
            _logger.LogWarning("⚠️ [IMAGES-TRANSFORM] Image missing product_id for migration {MigrationId}", migrationId);
            return;
        }

        var sourceProductId = productIdValue.ToString();
        if (string.IsNullOrEmpty(sourceProductId))
        {
            _logger.LogWarning("⚠️ [IMAGES-TRANSFORM] Image has empty product_id for migration {MigrationId}", migrationId);
            return;
        }

        try
        {
            // Get the product mapping to find destination product ID
            var productMapping = await _migrationStorageService.GetEntityMappingAsync(migrationId, "products", sourceProductId);
            if (productMapping?.DestinationId != null)
            {
                // Update to destination product ID
                transformed["product_id"] = productMapping.DestinationId;
                _logger.LogDebug("🔗 [IMAGES-TRANSFORM] Mapped product_id: {SourceProductId} → {DestinationProductId}",
                    sourceProductId, productMapping.DestinationId);
            }
            else
            {
                _logger.LogWarning("⚠️ [IMAGES-TRANSFORM] No product mapping found for product {SourceProductId} in migration {MigrationId}",
                    sourceProductId, migrationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [IMAGES-TRANSFORM] Failed to map product_id for image in migration {MigrationId}", migrationId);
        }
    }

    /// <summary>
    /// Validates and sets required fields for images
    /// </summary>
    private void ValidateAndSetRequiredFields(Dictionary<string, object> transformed, string migrationId)
    {
        // Validate image URL exists
        if (!transformed.ContainsKey("image_url") || string.IsNullOrEmpty(transformed["image_url"]?.ToString()))
        {
            _logger.LogWarning("⚠️ [IMAGES-TRANSFORM] Image missing or empty image_url field for migration {MigrationId}", migrationId);
        }

        // Set default sort order if not present
        if (!transformed.ContainsKey("sort_order"))
        {
            transformed["sort_order"] = 0;
        }

        // Set default is_thumbnail if not present
        if (!transformed.ContainsKey("is_thumbnail"))
        {
            transformed["is_thumbnail"] = false;
        }
    }

    /// <summary>
    /// Cleans up image data by removing null values and source-specific fields
    /// </summary>
    private static void CleanupImageData(Dictionary<string, object> transformed)
    {
        // Remove null values
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        // Remove source-specific fields that don't belong in destination (except product_id which we mapped)
        var sourceOnlyFields = new[] { "id" };
        foreach (var field in sourceOnlyFields)
        {
            transformed.Remove(field);
        }
    }
}