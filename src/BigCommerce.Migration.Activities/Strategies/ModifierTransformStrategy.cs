using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Strategies;

/// <summary>
/// Transform strategy for modifier entities
/// Handles product mapping, modifier type defaults, and required field validation
/// </summary>
public class ModifierTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<ModifierTransformStrategy> _logger;
    private readonly IMigrationStorageService _migrationStorageService;

    public string EntityType => "modifiers";

    public ModifierTransformStrategy(
        ILogger<ModifierTransformStrategy> logger,
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

        // 🔗 PRODUCT ASSIGNMENT: Ensure product_id is properly mapped for modifiers
        await EnsureProductIdMappingAsync(transformed, migrationId, cancellationToken);

        // 🔧 MODIFIER VALIDATION: Ensure required fields
        ValidateAndSetRequiredFields(transformed, migrationId);

        // 🧹 CLEANUP: Remove null values and source-specific fields
        CleanupModifierData(transformed);

        _logger.LogDebug("✅ [MODIFIERS-TRANSFORM] Transformed modifier entity for migration {MigrationId}: type={Type}, name={Name}", 
            migrationId, transformed.TryGetValue("type", out var type) ? type : "unknown",
            transformed.TryGetValue("name", out var modifierName) ? modifierName : "unknown");

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
            _logger.LogWarning("⚠️ [MODIFIERS-TRANSFORM] Modifier missing product_id for migration {MigrationId}", migrationId);
            return;
        }

        var sourceProductId = productIdValue.ToString();
        if (string.IsNullOrEmpty(sourceProductId))
        {
            _logger.LogWarning("⚠️ [MODIFIERS-TRANSFORM] Modifier has empty product_id for migration {MigrationId}", migrationId);
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
                _logger.LogDebug("🔗 [MODIFIERS-TRANSFORM] Mapped product_id: {SourceProductId} → {DestinationProductId}", 
                    sourceProductId, productMapping.DestinationId);
            }
            else
            {
                _logger.LogWarning("⚠️ [MODIFIERS-TRANSFORM] No product mapping found for product {SourceProductId} in migration {MigrationId}", 
                    sourceProductId, migrationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [MODIFIERS-TRANSFORM] Failed to map product_id for modifier in migration {MigrationId}", migrationId);
        }
    }

    /// <summary>
    /// Validates and sets required fields for modifiers
    /// </summary>
    private void ValidateAndSetRequiredFields(Dictionary<string, object> transformed, string migrationId)
    {
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Modifier";
            _logger.LogWarning("⚠️ [MODIFIERS-TRANSFORM] Modifier missing name field, using default for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("type"))
        {
            transformed["type"] = "radio_button";
            _logger.LogWarning("⚠️ [MODIFIERS-TRANSFORM] Modifier missing type field, using default 'radio_button' for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("sort_order"))
        {
            transformed["sort_order"] = 0;
        }
    }

    /// <summary>
    /// Cleans up modifier data by removing null values and source-specific fields
    /// </summary>
    private void CleanupModifierData(Dictionary<string, object> transformed)
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

        // ✅ CRITICAL FIX: Remove 'id' fields from option_values array to prevent BigCommerce API rejection
        if (transformed.TryGetValue("option_values", out var optionValuesObj))
        {
            if (optionValuesObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                // Handle JsonElement array - deserialize to List<Dictionary<string, object>>
                var optionValuesList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonElement.GetRawText());
                if (optionValuesList != null)
                {
                    foreach (var optionValueData in optionValuesList)
                    {
                        // Remove id fields that cause "cannot have an id when creating an option" errors
                        optionValueData.Remove("id");
                        optionValueData.Remove("modifier_id");
                    }
                    
                    // Replace the JsonElement with the cleaned List
                    transformed["option_values"] = optionValuesList;
                    _logger.LogDebug("✅ [MODIFIER-TRANSFORM] Removed ID fields from {Count} option values (JsonElement type)", optionValuesList.Count);
                }
            }
            else if (optionValuesObj is List<object> optionValuesList)
            {
                foreach (var optionValueData in optionValuesList.OfType<Dictionary<string, object>>())
                {
                    // Remove id fields that cause "cannot have an id when creating an option" errors
                    optionValueData.Remove("id");
                    optionValueData.Remove("modifier_id");
                }
                _logger.LogDebug("✅ [MODIFIER-TRANSFORM] Removed ID fields from {Count} option values (List<object> type)", optionValuesList.Count);
            }
        }
    }
} 