using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Service for storing hierarchical option mappings in the OptionsMappingData field
/// Required for Phase 3 variant migration to lookup option and option_value ID mappings
/// </summary>
public class HierarchicalOptionMappingService : IHierarchicalOptionMappingService
{
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly ILogger<HierarchicalOptionMappingService> _logger;

    public HierarchicalOptionMappingService(
        IMigrationStorageService migrationStorageService,
        ILogger<HierarchicalOptionMappingService> logger)
    {
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Stores hierarchical option mapping in the product's OptionsMappingData field
    /// Updates the product's entity mapping with option and option_value ID mappings
    /// </summary>
    public async Task StoreHierarchicalOptionMappingAsync(
        Dictionary<string, object> sourceOption,
        Dictionary<string, object> createdOption,
        string sourceOptionId,
        string destinationOptionId,
        string sourceProductId,
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] ===== METHOD CALLED ===== START");
            _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] INPUT PARAMS: sourceOptionId={SourceOptionId}, destinationOptionId={DestinationOptionId}, sourceProductId={SourceProductId}, migrationId={MigrationId}", 
                sourceOptionId, destinationOptionId, sourceProductId, migrationId);
            _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] SOURCE OPTION: {SourceOption}", 
                JsonSerializer.Serialize(sourceOption, new JsonSerializerOptions { WriteIndented = true }));
            _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] CREATED OPTION: {CreatedOption}", 
                JsonSerializer.Serialize(createdOption, new JsonSerializerOptions { WriteIndented = true }));
            // Get the product's entity mapping
            _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] STEP 1: Getting product mapping for migrationId={MigrationId}, productId={ProductId}", migrationId, sourceProductId);
            
            EntityMapping? productMapping = null;
            try
            {
                productMapping = await _migrationStorageService.GetEntityMappingAsync(migrationId, "products", sourceProductId);
                _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] STEP 1 SUCCESS: Product mapping retrieved, IsNull={IsNull}", productMapping == null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔗 [HIERARCHICAL-MAPPING] STEP 1 ERROR: Failed to get product mapping");
                return;
            }
            
            if (productMapping == null)
            {
                _logger.LogWarning("📋 [HIERARCHICAL-MAPPING] Product mapping not found for product {ProductId}", sourceProductId);
                return;
            }

            // Build the option mapping for this single option
            var optionValueMappings = new List<object>();
            
            // Extract option values if they exist in the created option
            // ✅ FIXED: Handle both JsonElement arrays and List<object> for BigCommerce API responses
            if (createdOption.TryGetValue("option_values", out var optionValuesObj))
            {
                _logger.LogInformation("🔍 [HIERARCHICAL-MAPPING] Found option_values in created option, type: {Type}", optionValuesObj?.GetType().Name ?? "null");
                List<Dictionary<string, object>>? optionValuesList = null;
                
                // Handle JsonElement array (from API response deserialization)
                if (optionValuesObj is JsonElement ovElement && ovElement.ValueKind == JsonValueKind.Array)
                {
                    optionValuesList = new List<Dictionary<string, object>>();
                    foreach (var ovJsonElement in ovElement.EnumerateArray())
                    {
                        var ovDict = JsonSerializer.Deserialize<Dictionary<string, object>>(ovJsonElement.GetRawText());
                        if (ovDict != null) optionValuesList.Add(ovDict);
                    }
                    _logger.LogDebug("🔍 [HIERARCHICAL-MAPPING] Parsed {Count} option values from JsonElement array", optionValuesList.Count);
                }
                // Handle List<object> (already converted)
                else if (optionValuesObj is List<object> objectList)
                {
                    optionValuesList = objectList.Cast<Dictionary<string, object>>().ToList();
                    _logger.LogDebug("🔍 [HIERARCHICAL-MAPPING] Using {Count} option values from List<object>", optionValuesList.Count);
                }
                else
                {
                    _logger.LogWarning("⚠️ [HIERARCHICAL-MAPPING] Unexpected option_values type: {Type}, value: {Value}", 
                        optionValuesObj?.GetType().Name ?? "null", JsonSerializer.Serialize(optionValuesObj));
                }
                
                // Process option values if we successfully extracted them
                if (optionValuesList != null && optionValuesList.Count > 0)
                {
                    // Get source option values for positional matching
                    var sourceOptionValues = GetSourceOptionValuesList(sourceOption);
                    
                    for (int i = 0; i < optionValuesList.Count; i++)
                    {
                        var destinationOptionValue = optionValuesList[i];
                        var destinationOptionValueId = destinationOptionValue.TryGetValue("id", out var ovId) ? ovId?.ToString() : null;
                        
                        if (!string.IsNullOrEmpty(destinationOptionValueId) && i < sourceOptionValues.Count)
                        {
                            var sourceOptionValue = sourceOptionValues[i];
                            var sourceLabel = sourceOptionValue.TryGetValue("label", out var srcLabel) ? srcLabel?.ToString() : "Unknown";
                            var destinationLabel = destinationOptionValue.TryGetValue("label", out var destLabel) ? destLabel?.ToString() : "Unknown";
                            
                            // Use real source option value ID (now preserved by OptionsTransformStrategy)
                            var realSourceId = sourceOptionValue.TryGetValue("_source_option_value_id", out var realId) ? realId?.ToString() : null;
                            
                            if (!string.IsNullOrEmpty(realSourceId))
                            {
                                optionValueMappings.Add(new
                                {
                                    sourceId = realSourceId,
                                    sourcePosition = i,
                                    sourceLabel = sourceLabel,
                                    destinationId = destinationOptionValueId,
                                    destinationLabel = destinationLabel
                                });
                                
                                _logger.LogInformation("✅ [HIERARCHICAL-MAPPING] Mapped option value at position {Position}: '{SourceLabel}' (REAL ID: {SourceId}) → {DestinationId} ('{DestinationLabel}')", 
                                    i, sourceLabel, realSourceId, destinationOptionValueId, destinationLabel);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ [HIERARCHICAL-MAPPING] Missing real source option value ID at position {Position} for '{SourceLabel}' - skipping mapping", 
                                    i, sourceLabel);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ [HIERARCHICAL-MAPPING] Could not map option value at position {Position} - destinationId: {DestinationId}", 
                                i, destinationOptionValueId ?? "null");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ [HIERARCHICAL-MAPPING] No option values found to process for option {OptionId}", sourceOptionId);
                }
            }
            else
            {
                _logger.LogInformation("🔍 [HIERARCHICAL-MAPPING] No option_values found in created option");
            }
            
            _logger.LogInformation("🔍 [HIERARCHICAL-MAPPING] Total option value mappings found: {Count}", optionValueMappings.Count);

            // Get existing option mappings or create new structure
            var existingOptionsData = new List<object>();
            if (!string.IsNullOrEmpty(productMapping.OptionsMappingData))
            {
                try
                {
                    var existingData = JsonSerializer.Deserialize<Dictionary<string, object>>(productMapping.OptionsMappingData);
                    if (existingData?.TryGetValue("options", out var existingOptions) == true && existingOptions is JsonElement optionsElement)
                    {
                        existingOptionsData = JsonSerializer.Deserialize<List<object>>(optionsElement.GetRawText()) ?? new List<object>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "📋 [HIERARCHICAL-MAPPING] Failed to parse existing options data, starting fresh");
                    existingOptionsData = new List<object>();
                }
            }

            // Add the new option mapping
            existingOptionsData.Add(new
            {
                sourceOptionId = sourceOptionId,
                destinationOptionId = destinationOptionId,
                optionValues = optionValueMappings
            });

            // Create the final JSON structure
            var optionMappingsJson = new { options = existingOptionsData };
            var jsonString = JsonSerializer.Serialize(optionMappingsJson, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            // Update the product's entity mapping immediately
            _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] STEP 3: Updating entity mapping with JSON data");
            _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] FINAL JSON: {JsonString}", jsonString);
            
            try
            {
                productMapping.OptionsMappingData = jsonString;
                productMapping.UpdatedAt = DateTime.UtcNow;
                
                _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] STEP 3: About to call UpdateEntityMappingAsync");
                await _migrationStorageService.UpdateEntityMappingAsync(productMapping);
                _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] STEP 3 SUCCESS: UpdateEntityMappingAsync completed successfully");

                _logger.LogInformation("✅ [HIERARCHICAL-MAPPING] Successfully stored option mapping: Source={SourceOptionId} → Destination={DestinationOptionId} with {OptionValueCount} option values for product {ProductId}", 
                    sourceOptionId, destinationOptionId, optionValueMappings.Count, sourceProductId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔗 [HIERARCHICAL-MAPPING] STEP 3 ERROR: Failed to update entity mapping");
                throw; // Re-throw to be caught by outer catch block
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [HIERARCHICAL-MAPPING] Failed to store option mapping for option {OptionId} in product {ProductId}", sourceOptionId, sourceProductId);
            // Don't throw - this is not critical enough to fail the entire migration
        }
    }

    /// <summary>
    /// Helper method to extract source option values list from source option
    /// </summary>
    private List<Dictionary<string, object>> GetSourceOptionValuesList(Dictionary<string, object> sourceOption)
    {
        // Get source option values - handle both JsonElement and List<object>
        if (!sourceOption.TryGetValue("option_values", out var sourceValuesObj))
        {
            _logger.LogWarning("⚠️ [OPTION-VALUE-EXTRACT] Source option missing 'option_values' field");
            return new List<Dictionary<string, object>>();
        }

        List<Dictionary<string, object>>? sourceValuesList = null;
        
        // Handle JsonElement array (like we did for created option values)
        if (sourceValuesObj is JsonElement sourceElement && sourceElement.ValueKind == JsonValueKind.Array)
        {
            sourceValuesList = new List<Dictionary<string, object>>();
            foreach (var sourceJsonElement in sourceElement.EnumerateArray())
            {
                var sourceDict = JsonSerializer.Deserialize<Dictionary<string, object>>(sourceJsonElement.GetRawText());
                if (sourceDict != null) sourceValuesList.Add(sourceDict);
            }
            _logger.LogDebug("🔍 [OPTION-VALUE-EXTRACT] Parsed {Count} source option values from JsonElement array", sourceValuesList.Count);
        }
        // Handle List<object> (already converted)
        else if (sourceValuesObj is List<object> objectList)
        {
            sourceValuesList = new List<Dictionary<string, object>>();
            foreach (var item in objectList)
            {
                if (item is Dictionary<string, object> dictItem)
                {
                    sourceValuesList.Add(dictItem);
                }
                else if (item is JsonElement jsonItem)
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonItem.GetRawText());
                    if (dict != null) sourceValuesList.Add(dict);
                }
                else
                {
                    // Try to serialize/deserialize as fallback
                    try
                    {
                        var json = JsonSerializer.Serialize(item);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (dict != null) sourceValuesList.Add(dict);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("⚠️ [OPTION-VALUE-EXTRACT] Could not convert item to Dictionary<string, object>: {Type}, error: {Error}", 
                            item?.GetType().Name ?? "null", ex.Message);
                    }
                }
            }
            _logger.LogDebug("🔍 [OPTION-VALUE-EXTRACT] Processed {Count} source option values from List<object>", sourceValuesList.Count);
        }
        // Handle IEnumerable/IList generically
        else if (sourceValuesObj is IEnumerable enumerable and not string)
        {
            sourceValuesList = new List<Dictionary<string, object>>();
            foreach (var item in enumerable)
            {
                if (item is Dictionary<string, object> dictItem)
                {
                    sourceValuesList.Add(dictItem);
                }
                else if (item is JsonElement jsonItem)
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonItem.GetRawText());
                    if (dict != null) sourceValuesList.Add(dict);
                }
                else
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(item);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (dict != null) sourceValuesList.Add(dict);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("⚠️ [OPTION-VALUE-EXTRACT] Could not convert item to Dictionary<string, object>: {Type}, error: {Error}", 
                            item?.GetType().Name ?? "null", ex.Message);
                    }
                }
            }
            _logger.LogDebug("🔍 [OPTION-VALUE-EXTRACT] Processed {Count} source option values from IEnumerable", sourceValuesList.Count);
        }
        else
        {
            // Last resort: try to serialize and deserialize the whole thing
            try
            {
                var json = JsonSerializer.Serialize(sourceValuesObj);
                sourceValuesList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                _logger.LogInformation("✅ [OPTION-VALUE-EXTRACT] Successfully converted via serialization: {Count} option values", sourceValuesList?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError("❌ [OPTION-VALUE-EXTRACT] Failed to convert source option_values via serialization: {Error}", ex.Message);
                return new List<Dictionary<string, object>>();
            }
        }

        return sourceValuesList ?? new List<Dictionary<string, object>>();
    }
}