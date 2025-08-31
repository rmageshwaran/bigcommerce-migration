using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Transform;

/// <summary>
/// Transform strategy for product-metafields entities
/// Handles basic field validation and data preparation for creation phase
/// 
/// This strategy provides minimal transformation since the main mapping logic
/// (resource_id mapping from source to destination product IDs) is handled 
/// in the ProductMetafieldsCreationStrategy for optimal performance.
/// 
/// Key Features:
/// - Basic field validation and null value removal
/// - Preserves all metafield properties for creation phase
/// - Validates required fields exist (resource_id, key, value)
/// - Simple, lightweight transformation suitable for large datasets
/// - Follows IEntityTransformStrategy interface for seamless integration
/// 
/// Architecture Compliance:
/// - Follows SOLID principles (Single Responsibility)
/// - Stateless design for multi-instance Azure Functions
/// - Enterprise-grade error handling and logging
/// - Complete XML documentation
/// </summary>
public class ProductMetafieldsTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<ProductMetafieldsTransformStrategy> _logger;

    /// <summary>
    /// Gets the entity type this strategy handles
    /// </summary>
    public string EntityType => "product-metafields";

    /// <summary>
    /// Initializes a new instance of ProductMetafieldsTransformStrategy
    /// </summary>
    /// <param name="logger">Logger for the strategy</param>
    public ProductMetafieldsTransformStrategy(ILogger<ProductMetafieldsTransformStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Transforms product metafield data with basic validation and cleanup
    /// 
    /// Transformation Process:
    /// 1. Copy all source metafield properties
    /// 2. Validate required fields exist (resource_id, key, value)
    /// 3. Remove null values for clean API payload
    /// 4. Preserve all metafield properties for creation phase mapping
    /// 
    /// Note: Product ID mapping (resource_id transformation) is handled in 
    /// ProductMetafieldsCreationStrategy for optimal batch performance.
    /// </summary>
    /// <param name="entity">Source metafield entity to transform</param>
    /// <param name="migrationId">Migration identifier for logging context</param>
    /// <param name="sourceStore">Source store configuration (not used for metafields)</param>
    /// <param name="destinationStore">Destination store configuration (not used for metafields)</param>
    /// <param name="categoryTreeContext">Category tree context (not used for metafields)</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Transformed metafield entity ready for creation phase</returns>
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
            throw new ArgumentNullException(nameof(entity));
        }

        // Live cancellation support
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Create a copy of the entity for transformation
            var transformed = new Dictionary<string, object>(entity);

            // Validate required fields for metafields
            ValidateRequiredFields(transformed, migrationId);

            // Remove null values for clean API payload
            RemoveNullValues(transformed);

            // Log transformation completion
            _logger.LogDebug("✅ [PRODUCT-METAFIELDS-TRANSFORM] Successfully transformed metafield for migration {MigrationId}. " +
                           "ResourceId: {ResourceId}, Key: {Key}, Namespace: {Namespace}", 
                migrationId, 
                transformed.TryGetValue("resource_id", out var resourceId) ? resourceId : "unknown",
                transformed.TryGetValue("key", out var key) ? key : "unknown",
                transformed.TryGetValue("namespace", out var ns) ? ns : "unknown");

            return await Task.FromResult(transformed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PRODUCT-METAFIELDS-TRANSFORM] Failed to transform metafield entity for migration {MigrationId}: {ErrorMessage}", 
                migrationId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Validates that required fields exist in the metafield entity
    /// BigCommerce metafields require: permission_set, namespace, key, value, description, resource_id
    /// </summary>
    /// <param name="entity">Metafield entity to validate</param>
    /// <param name="migrationId">Migration identifier for logging context</param>
    /// <exception cref="InvalidOperationException">Thrown when required fields are missing</exception>
    private void ValidateRequiredFields(Dictionary<string, object> entity, string migrationId)
    {
        var requiredFields = new[] { "permission_set", "namespace", "key", "value", "description", "resource_id" };
        var missingFields = new List<string>();

        foreach (var field in requiredFields)
        {
            if (!entity.ContainsKey(field) || entity[field] == null || 
                (entity[field] is string strValue && string.IsNullOrWhiteSpace(strValue)))
            {
                missingFields.Add(field);
            }
        }

        if (missingFields.Any())
        {
            var metafieldId = entity.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
            var errorMessage = $"Metafield ID {metafieldId} missing required fields: {string.Join(", ", missingFields)}";
            
            _logger.LogError("❌ [PRODUCT-METAFIELDS-TRANSFORM] {ErrorMessage} for migration {MigrationId}", 
                errorMessage, migrationId);
            
            throw new InvalidOperationException($"Invalid metafield entity: {errorMessage}");
        }

        // Log successful validation
        _logger.LogDebug("✅ [PRODUCT-METAFIELDS-TRANSFORM] Required field validation passed for metafield in migration {MigrationId}", migrationId);
    }

    /// <summary>
    /// Removes null values from the entity to create clean API payload
    /// </summary>
    /// <param name="entity">Entity to clean up</param>
    private void RemoveNullValues(Dictionary<string, object> entity)
    {
        var keysToRemove = entity
            .Where(kvp => kvp.Value == null)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            entity.Remove(key);
            _logger.LogDebug("🧹 [PRODUCT-METAFIELDS-TRANSFORM] Removed null field: {FieldName}", key);
        }

        if (keysToRemove.Any())
        {
            _logger.LogDebug("🧹 [PRODUCT-METAFIELDS-TRANSFORM] Cleaned up {Count} null fields from metafield entity", keysToRemove.Count);
        }
    }
}
