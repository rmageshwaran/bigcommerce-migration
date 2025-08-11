using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BigCommerce.Migration.Orchestration.Services;

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
        _logger.LogInformation("🔗 [HIERARCHICAL-MAPPING] CALLED: Storing option mapping {SourceOptionId} → {DestinationOptionId} for product {ProductId}", 
            sourceOptionId, destinationOptionId, sourceProductId);
            
        try
        {
            // Get the product's entity mapping
            var productMapping = await _migrationStorageService.GetEntityMappingAsync(migrationId, "products", sourceProductId);
            if (productMapping == null)
            {
                _logger.LogWarning("📋 [HIERARCHICAL-MAPPING] Product mapping not found for product {ProductId}", sourceProductId);
                return;
            }

            // Build the option mapping for this single option
            var optionValueMappings = new List<object>();
            
            // Extract option values if they exist in the created option
            if (createdOption.TryGetValue("option_values", out var optionValuesObj) && 
                optionValuesObj is List<object> optionValuesList)
            {
                foreach (var optionValue in optionValuesList.Cast<Dictionary<string, object>>())
                {
                    var sourceOptionValueId = GetSourceOptionValueId(sourceOption, optionValue);
                    var destinationOptionValueId = optionValue.TryGetValue("id", out var ovId) ? ovId?.ToString() : null;

                    if (!string.IsNullOrEmpty(sourceOptionValueId) && !string.IsNullOrEmpty(destinationOptionValueId))
                    {
                        optionValueMappings.Add(new
                        {
                            sourceId = sourceOptionValueId,
                            destinationId = destinationOptionValueId
                        });
                    }
                }
            }

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
            productMapping.OptionsMappingData = jsonString;
            productMapping.UpdatedAt = DateTime.UtcNow;
            await _migrationStorageService.UpdateEntityMappingAsync(productMapping);

            _logger.LogInformation("✅ [HIERARCHICAL-MAPPING] Successfully stored option mapping: Source={SourceOptionId} → Destination={DestinationOptionId} with {OptionValueCount} option values for product {ProductId}", 
                sourceOptionId, destinationOptionId, optionValueMappings.Count, sourceProductId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [HIERARCHICAL-MAPPING] Failed to store option mapping for option {OptionId} in product {ProductId}", sourceOptionId, sourceProductId);
            // Don't throw - this is not critical enough to fail the entire migration
        }
    }

    /// <summary>
    /// Helper method to extract source option value ID by matching with source option
    /// </summary>
    private static string? GetSourceOptionValueId(Dictionary<string, object> sourceOption, Dictionary<string, object> createdOptionValue)
    {
        // Try to get the label from created option value to match with source
        if (!createdOptionValue.TryGetValue("label", out var createdLabel)) return null;
        
        // Get source option values to find matching label
        if (!sourceOption.TryGetValue("option_values", out var sourceValuesObj) || 
            sourceValuesObj is not List<object> sourceValuesList) return null;

        // Find the source option value with matching label
        foreach (var sourceValue in sourceValuesList.Cast<Dictionary<string, object>>())
        {
            if (sourceValue.TryGetValue("label", out var sourceLabel) && 
                string.Equals(sourceLabel?.ToString(), createdLabel?.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return sourceValue.TryGetValue("id", out var sourceId) ? sourceId?.ToString() : null;
            }
        }

        return null;
    }
}