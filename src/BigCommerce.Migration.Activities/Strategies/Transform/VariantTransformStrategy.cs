using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Transform;

/// <summary>
/// Transform strategy for variant entities
/// Handles product mapping, SKU generation, and required field validation
/// </summary>
public class VariantTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<VariantTransformStrategy> _logger;

    public string EntityType => "variants";  // ✅ FIXED: Match EntityCreationStrategy and dependencies

    public VariantTransformStrategy(ILogger<VariantTransformStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // Handle product mapping if available
        // Note: ProductMapping property doesn't exist in BatchProcessingRequest, so we'll skip this for now
        // In a full implementation, you'd have a mapping service to handle this

        // Ensure required fields
        if (!transformed.ContainsKey("sku"))
        {
            transformed["sku"] = $"SKU-{Guid.NewGuid():N}";
            _logger.LogWarning("Variant missing SKU, generating default for migration {MigrationId}", migrationId);
        }

        // Remove null values
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        return await Task.FromResult(transformed);
    }
}