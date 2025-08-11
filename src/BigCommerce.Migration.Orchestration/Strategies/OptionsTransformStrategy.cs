using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Transform strategy for product options entities
/// Handles product mapping, option type validation, option value structure, and option mapping storage
/// </summary>
public class OptionsTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<OptionsTransformStrategy> _logger;
    private readonly IMigrationStorageService _migrationStorageService;

    public string EntityType => "options";

    public OptionsTransformStrategy(
        ILogger<OptionsTransformStrategy> logger,
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
        // 🔍 STAGE 3 DEBUG: Log complete input payload to transform
        _logger.LogInformation("🔍 [STAGE-3-TRANSFORM-INPUT] ===== COMPLETE INPUT PAYLOAD ===== {InputPayload}", 
            JsonSerializer.Serialize(entity, new JsonSerializerOptions { WriteIndented = true }));
            
        _logger.LogInformation("🔗 [OPTIONS-TRANSFORM] ===== TRANSFORM CALLED ===== Entity Keys: {EntityKeys}", 
            string.Join(", ", entity.Keys));
        
        _logger.LogDebug("🔗 [OPTIONS-TRANSFORM] Creating transformed dictionary...");
        var transformed = new Dictionary<string, object>(entity);
        _logger.LogDebug("🔗 [OPTIONS-TRANSFORM] Transformed dictionary created successfully");

        // 🔗 PRODUCT ASSIGNMENT: Ensure product_id is properly mapped for options
        _logger.LogDebug("🔗 [OPTIONS-TRANSFORM] BEFORE EnsureProductIdMappingAsync - product_id: {ProductId}", 
            transformed.TryGetValue("product_id", out var beforeProductId) ? beforeProductId : "MISSING");
        await EnsureProductIdMappingAsync(transformed, migrationId, cancellationToken);
        _logger.LogDebug("🔗 [OPTIONS-TRANSFORM] AFTER EnsureProductIdMappingAsync - product_id: {ProductId}, _source_product_id: {SourceProductId}", 
            transformed.TryGetValue("product_id", out var afterProductId) ? afterProductId : "MISSING",
            transformed.TryGetValue("_source_product_id", out var sourceProductId) ? sourceProductId : "MISSING");

        // 🔧 OPTION VALIDATION: Ensure required fields
        ValidateAndSetRequiredFields(transformed, migrationId);

        // 🧹 CLEANUP: Remove source-specific fields and null values
        CleanupOptionData(transformed);

        // 🔍 STAGE 3 DEBUG: Log complete output payload from transform
        _logger.LogInformation("🔍 [STAGE-3-TRANSFORM-OUTPUT] ===== COMPLETE OUTPUT PAYLOAD ===== {OutputPayload}", 
            JsonSerializer.Serialize(transformed, new JsonSerializerOptions { WriteIndented = true }));
            
        _logger.LogDebug("✅ [OPTIONS-TRANSFORM] Transformed option entity for migration {MigrationId}: type={Type}, name={Name}", 
            migrationId, transformed.TryGetValue("type", out var type) ? type : "unknown",
            transformed.TryGetValue("name", out var optionName) ? optionName : "unknown");

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
            _logger.LogWarning("⚠️ [OPTIONS-TRANSFORM] Option missing product_id for migration {MigrationId}", migrationId);
            return;
        }

        var sourceProductId = productIdValue.ToString();
        _logger.LogInformation("🔗 [OPTIONS-TRANSFORM] EnsureProductIdMappingAsync - sourceProductId: {SourceProductId}", sourceProductId);
        if (string.IsNullOrEmpty(sourceProductId))
        {
            _logger.LogWarning("⚠️ [OPTIONS-TRANSFORM] Option has empty product_id for migration {MigrationId}", migrationId);
            return;
        }

        try
        {
            // Get the product mapping to find destination product ID
            _logger.LogInformation("🔍 [OPTIONS-TRANSFORM] Looking up product mapping for sourceProductId: {SourceProductId}, migrationId: {MigrationId}", sourceProductId, migrationId);
            var productMapping = await _migrationStorageService.GetEntityMappingAsync(migrationId, "products", sourceProductId);
            
            if (productMapping?.DestinationId != null)
            {
                // Update to destination product ID
                transformed["product_id"] = productMapping.DestinationId;
                
                // 🔗 HIERARCHICAL MAPPING: Store source product ID for option mapping service
                transformed["_source_product_id"] = sourceProductId;
                
                _logger.LogInformation("✅ [OPTIONS-TRANSFORM] Successfully mapped product_id: {SourceProductId} → {DestinationProductId}", 
                    sourceProductId, productMapping.DestinationId);
                _logger.LogInformation("✅ [OPTIONS-TRANSFORM] Added _source_product_id to transformed data: {SourceProductId}", sourceProductId);
            }
            else
            {
                _logger.LogError("❌ [OPTIONS-TRANSFORM] CRITICAL ERROR: No product mapping found for product {SourceProductId} in migration {MigrationId} - productMapping is {ProductMapping}. This should NOT happen if product migration completed successfully!", 
                    sourceProductId, migrationId, productMapping == null ? "null" : "not null but DestinationId is null");
                
                // DO NOT add _source_product_id - this will cause the hierarchical mapping to fail as expected
                // This forces the issue to be visible and fixed rather than silently continuing
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [OPTIONS-TRANSFORM] CRITICAL ERROR: Exception occurred while mapping product_id for option in migration {MigrationId} with sourceProductId {SourceProductId}. This should NOT happen!", migrationId, sourceProductId);
            
            // DO NOT add _source_product_id - let the hierarchical mapping fail to surface the issue
            // This ensures the problem is visible and gets fixed rather than silently continuing
        }
    }

    /// <summary>
    /// Validates and sets required fields for options
    /// </summary>
    private void ValidateAndSetRequiredFields(Dictionary<string, object> transformed, string migrationId)
    {
        if (!transformed.ContainsKey("display_name"))
        {
            transformed["display_name"] = transformed.TryGetValue("name", out var name) ? name : "Unnamed Option";
            _logger.LogWarning("⚠️ [OPTIONS-TRANSFORM] Option missing display_name field, using name or default for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = transformed.TryGetValue("display_name", out var displayName) ? displayName : "unnamed-option";
            _logger.LogWarning("⚠️ [OPTIONS-TRANSFORM] Option missing name field, using display_name or default for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("type"))
        {
            transformed["type"] = "dropdown";
            _logger.LogWarning("⚠️ [OPTIONS-TRANSFORM] Option missing type field, using default 'dropdown' for migration {MigrationId}", migrationId);
        }

        // Ensure sort_order has a default value
        if (!transformed.ContainsKey("sort_order"))
        {
            transformed["sort_order"] = 0;
        }
    }

    /// <summary>
    /// Cleans up option data by removing null values and source-specific fields
    /// </summary>
    private void CleanupOptionData(Dictionary<string, object> transformed)
    {
        // Remove null values
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        // ✅ ID PRESERVATION: Now handled by EntityTransformService before reaching this strategy
        // The EntityTransformService preserves the source option ID as '_source_option_id' before removing 'id'
        _logger.LogDebug("🔗 [OPTIONS-TRANSFORM] Source option ID preservation handled by EntityTransformService");

        // ✅ CRITICAL FIX: Remove 'id' fields from option_values array to prevent BigCommerce API rejection
        if (transformed.TryGetValue("option_values", out var optionValuesObj))
        {
            if (optionValuesObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                // Handle JsonElement array - deserialize to List<Dictionary<string, object>>
                var optionValuesList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonElement.GetRawText());
                if (optionValuesList != null)
                {
                    var preservedCount = 0;
                    foreach (var optionValueData in optionValuesList)
                    {
                        // 🔗 PRESERVE option value ID before removal for hierarchical mapping
                        if (optionValueData.TryGetValue("id", out var optionValueId))
                        {
                            optionValueData["_source_option_value_id"] = optionValueId;
                            preservedCount++;
                        }
                        
                        // Remove id fields that cause "cannot have an id when creating an option" errors
                        optionValueData.Remove("id");
                        optionValueData.Remove("option_id");
                    }
                    _logger.LogInformation("🔗 [OPTIONS-TRANSFORM] Preserved {PreservedCount} option value IDs as '_source_option_value_id'", preservedCount);
                    
                    // Replace the JsonElement with the cleaned List
                    transformed["option_values"] = optionValuesList;
                    _logger.LogDebug("✅ [OPTIONS-TRANSFORM] Removed ID fields from {Count} option values (JsonElement type)", optionValuesList.Count);
                }
            }
            else if (optionValuesObj is List<object> optionValuesList)
            {
                var preservedCount = 0;
                foreach (var optionValueData in optionValuesList.OfType<Dictionary<string, object>>())
                {
                    // 🔗 PRESERVE option value ID before removal for hierarchical mapping
                    if (optionValueData.TryGetValue("id", out var optionValueId))
                    {
                        optionValueData["_source_option_value_id"] = optionValueId;
                        preservedCount++;
                    }
                    
                    // Remove id fields that cause "cannot have an id when creating an option" errors
                    optionValueData.Remove("id");
                    optionValueData.Remove("option_id");
                }
                _logger.LogInformation("🔗 [OPTIONS-TRANSFORM] Preserved {PreservedCount} option value IDs as '_source_option_value_id'", preservedCount);
                _logger.LogDebug("✅ [OPTIONS-TRANSFORM] Removed ID fields from {Count} option values (List<object> type)", optionValuesList.Count);
            }
        }
    }
}