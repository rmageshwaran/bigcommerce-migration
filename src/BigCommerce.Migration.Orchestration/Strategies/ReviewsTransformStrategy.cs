using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Transform strategy for product reviews entities
/// Handles product mapping, review validation, and rating normalization
/// </summary>
public class ReviewsTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<ReviewsTransformStrategy> _logger;
    private readonly IMigrationStorageService _migrationStorageService;

    public string EntityType => "reviews";

    public ReviewsTransformStrategy(
        ILogger<ReviewsTransformStrategy> logger,
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

        // 🔗 PRODUCT ASSIGNMENT: Ensure product_id is properly mapped for reviews
        await EnsureProductIdMappingAsync(transformed, migrationId, cancellationToken);

        // 🔧 REVIEW VALIDATION: Validate and set required fields
        ValidateAndSetRequiredFields(transformed, migrationId);

        // 🧹 CLEANUP: Remove null values and source-specific fields
        CleanupReviewData(transformed);

        _logger.LogDebug("✅ [REVIEWS-TRANSFORM] Transformed review entity for migration {MigrationId}: rating={Rating}, status={Status}", 
            migrationId, transformed.TryGetValue("rating", out var finalRating) ? finalRating : "unknown",
            transformed.TryGetValue("status", out var status) ? status : "unknown");

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
            _logger.LogWarning("⚠️ [REVIEWS-TRANSFORM] Review missing product_id for migration {MigrationId}", migrationId);
            return;
        }

        var sourceProductId = productIdValue.ToString();
        if (string.IsNullOrEmpty(sourceProductId))
        {
            _logger.LogWarning("⚠️ [REVIEWS-TRANSFORM] Review has empty product_id for migration {MigrationId}", migrationId);
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
                _logger.LogDebug("🔗 [REVIEWS-TRANSFORM] Mapped product_id: {SourceProductId} → {DestinationProductId}", 
                    sourceProductId, productMapping.DestinationId);
            }
            else
            {
                _logger.LogWarning("⚠️ [REVIEWS-TRANSFORM] No product mapping found for product {SourceProductId} in migration {MigrationId}", 
                    sourceProductId, migrationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [REVIEWS-TRANSFORM] Failed to map product_id for review in migration {MigrationId}", migrationId);
        }
    }

    /// <summary>
    /// Validates and sets required fields for reviews
    /// </summary>
    private void ValidateAndSetRequiredFields(Dictionary<string, object> transformed, string migrationId)
    {
        if (!transformed.ContainsKey("title"))
        {
            transformed["title"] = "Review";
            _logger.LogWarning("⚠️ [REVIEWS-TRANSFORM] Review missing title field, using default for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("text"))
        {
            transformed["text"] = "";
            _logger.LogWarning("⚠️ [REVIEWS-TRANSFORM] Review missing text field, using empty string for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("rating"))
        {
            transformed["rating"] = 5;
            _logger.LogWarning("⚠️ [REVIEWS-TRANSFORM] Review missing rating field, using default 5 for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Anonymous";
            _logger.LogWarning("⚠️ [REVIEWS-TRANSFORM] Review missing name field, using 'Anonymous' for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("email"))
        {
            transformed["email"] = "noemail@example.com";
            _logger.LogWarning("⚠️ [REVIEWS-TRANSFORM] Review missing email field, using placeholder for migration {MigrationId}", migrationId);
        }

        // Ensure status has a valid value
        if (!transformed.ContainsKey("status"))
        {
            transformed["status"] = "approved";
        }

        // Validate and normalize rating
        if (transformed.TryGetValue("rating", out var ratingValue))
        {
            if (ratingValue is not int rating || rating < 1 || rating > 5)
            {
                transformed["rating"] = 5;
                _logger.LogWarning("⚠️ [REVIEWS-TRANSFORM] Review has invalid rating value, normalized to 5 for migration {MigrationId}", migrationId);
            }
        }
    }

    /// <summary>
    /// Cleans up review data by removing null values and source-specific fields
    /// </summary>
    private static void CleanupReviewData(Dictionary<string, object> transformed)
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