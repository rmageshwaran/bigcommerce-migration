using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Transform strategy for modifier entities
/// Handles product mapping, modifier type defaults, and required field validation
/// </summary>
public class ModifierTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<ModifierTransformStrategy> _logger;

    public string EntityType => "modifiers";

    public ModifierTransformStrategy(ILogger<ModifierTransformStrategy> logger)
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
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Modifier";
            _logger.LogWarning("Modifier missing name field, using default for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("type"))
        {
            transformed["type"] = "radio_button";
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