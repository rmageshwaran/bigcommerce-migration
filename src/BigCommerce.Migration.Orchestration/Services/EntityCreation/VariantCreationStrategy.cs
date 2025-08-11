using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Models;
using System.Text.Json;

namespace BigCommerce.Migration.Orchestration.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating product variants in destination stores using BigCommerce Batch API
/// Implements Phase 3 of Enhanced Product Migration with parallel sub-batch processing
/// Uses PUT /v3/catalog/variants API with 50 variants per batch for optimal performance
/// </summary>
public class VariantCreationStrategy : IEntityCreationStrategy
{
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly ISubBatchProcessor _subBatchProcessor;
    private readonly ISubBatchConfigurationService _configService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly ILogger<VariantCreationStrategy> _logger;

    public VariantCreationStrategy(
        IApiRequestHandler apiRequestHandler,
        IMigrationStorageService migrationStorageService,
        ISubBatchProcessor subBatchProcessor,
        ISubBatchConfigurationService configService,
        IEntityErrorHandlingService errorHandlingService,
        ILogger<VariantCreationStrategy> logger)
    {
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _subBatchProcessor = subBatchProcessor ?? throw new ArgumentNullException(nameof(subBatchProcessor));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The entity type this strategy handles
    /// </summary>
    public string EntityType => "product-variants";  // 🔧 FIX: Match Phase 3 entity type

    /// <summary>
    /// Creates product variants in the destination store using BigCommerce Batch API
    /// Processes variants in sub-batches of 50 using PUT /v3/catalog/variants for optimal performance
    /// </summary>
    /// <param name="entities">Variants to create (250 variants from chunk)</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (not used for variants)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created variants with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("🚀 No variants provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        _logger.LogInformation("🚀 Creating {Count} variants using batch API for migration {MigrationId}", 
            entities.Count, migrationId);

        try
        {
            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Get variants configuration
            var config = _configService.GetConfiguration("variants");
            
            // Batch processor function for 50 variants
            async Task<List<Dictionary<string, object>>> BatchVariantProcessor(
                List<Dictionary<string, object>> batch50Variants, CancellationToken ct)
            {
                string? requestPayload = null;
                string? responsePayload = null;
                
                try
                {
                    // 1. Transform all 50 variants (bulk ID mapping)
                    var transformedVariants = await TransformVariantsBulkAsync(batch50Variants, migrationId, ct);
                    
                    // 2. Single batch API call (PUT /catalog/variants) 
                    var url = $"{destinationStore.GetApiBaseUrl()}/catalog/variants";
                    requestPayload = JsonSerializer.Serialize(transformedVariants);
                    
                    _logger.LogDebug("🚀 Batch API Call: PUT {Url} with {Count} variants", url, transformedVariants.Count);
                    
                    var response = await _apiRequestHandler.ExecuteRequestAsync<List<Dictionary<string, object>>>(
                        ApiRequest.CreatePut(url, requestPayload, destinationStore), 
                        ct);
                    
                    _logger.LogInformation("✅ Successfully created {Count} variants via batch API", batch50Variants.Count);
                    
                    // 3. ❌ NO MAPPING STORAGE - Variants are leaf entities with no dependents
                    return response ?? new List<Dictionary<string, object>>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to create batch of {Count} variants", batch50Variants.Count);
                    
                    // ✅ ENHANCED ERROR HANDLING: Categorize and log with structured data
                    await HandleBatchErrorAsync(ex, batch50Variants, migrationId, destinationStore, requestPayload, responsePayload, ct);
                    
                    // Continue-on-error policy: return empty list to continue with other batches
                    return new List<Dictionary<string, object>>();
                }
            }

            // Use SubBatchProcessor: 250 variants → 5 concurrent batches of 50
            var result = await _subBatchProcessor.ProcessInSubBatchesAsync(
                entities, config, BatchVariantProcessor, "variants", migrationId, cancellationToken);

            _logger.LogInformation("✅ Successfully processed {ProcessedCount}/{TotalCount} variants for migration {MigrationId}", 
                result?.Count ?? 0, entities.Count, migrationId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create {VariantCount} variants in migration {MigrationId}", 
                entities.Count, migrationId);

            // ✅ ENHANCED ERROR HANDLING: Categorize infrastructure vs application errors
            if (IsInfrastructureError(ex))
            {
                _logger.LogCritical("🚨 INFRASTRUCTURE ERROR: {ErrorMessage} - STOPPING MIGRATION", ex.Message);
                throw; // Infrastructure errors must stop migration
            }

            // ✅ LOG APPLICATION ERRORS: Log to OpenSearch with structured data for continue-on-error
            await HandleMigrationLevelErrorAsync(ex, entities, migrationId, destinationStore, cancellationToken);

            // Return empty list to avoid causing Durable Functions replay issues (continue-on-error)
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Transforms variants by mapping product_id and option_values from Phase 1 and Phase 2 entity mappings
    /// Performs bulk ID mapping for optimal performance with 50 variants per batch
    /// </summary>
    /// <param name="variants">Source variants to transform</param>
    /// <param name="migrationId">Migration identifier for entity mapping lookup</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transformed variants ready for BigCommerce Batch API</returns>
    private async Task<List<Dictionary<string, object>>> TransformVariantsBulkAsync(
        List<Dictionary<string, object>> variants, 
        string migrationId, 
        CancellationToken cancellationToken)
    {
        var transformedVariants = new List<Dictionary<string, object>>();

        foreach (var variant in variants)
        {
            try
            {
                // 🔧 Create clean variant payload with only required/allowed fields
                var transformedVariant = new Dictionary<string, object>();
                
                // 1. REQUIRED: Map product_id from Phase 1 entity mappings  
                if (variant.TryGetValue("product_id", out var sourceProductId) && sourceProductId != null)
                {
                    var destinationProductId = await GetDestinationProductIdAsync(sourceProductId.ToString()!, migrationId);
                    transformedVariant["product_id"] = int.Parse(destinationProductId);
                    _logger.LogDebug("🔗 Mapped product_id: {SourceId} → {DestinationId}", sourceProductId, destinationProductId);
                }
                else
                {
                    _logger.LogError("❌ Missing required product_id for variant - skipping");
                    continue;
                }
                
                // 2. REQUIRED: Ensure SKU is present
                if (variant.TryGetValue("sku", out var sku) && sku != null)
                {
                    transformedVariant["sku"] = sku.ToString();
                }
                else
                {
                    // Generate SKU if missing
                    transformedVariant["sku"] = $"VARIANT-{Guid.NewGuid():N}";
                    _logger.LogWarning("⚠️ Generated missing SKU for variant");
                }
                
                // 3. REQUIRED: Map option_values from Phase 2 option mappings
                var transformedOptionValues = new List<Dictionary<string, object>>();
                
                if (variant.TryGetValue("option_values", out var optionValues))
                {
                    _logger.LogDebug("🔍 [VARIANT-TRANSFORM] Processing option_values for variant with product_id {ProductId}: {OptionValuesType}", 
                        sourceProductId, optionValues?.GetType().Name ?? "null");
                    
                    // Handle different data types for option_values
                    List<Dictionary<string, object>>? optionValuesList = null;
                    
                    if (optionValues is JsonElement ovElement && ovElement.ValueKind == JsonValueKind.Array)
                    {
                        optionValuesList = new List<Dictionary<string, object>>();
                        foreach (var ovJsonElement in ovElement.EnumerateArray())
                        {
                            var ovDict = JsonSerializer.Deserialize<Dictionary<string, object>>(ovJsonElement.GetRawText());
                            if (ovDict != null) optionValuesList.Add(ovDict);
                        }
                        _logger.LogDebug("🔍 [VARIANT-TRANSFORM] Parsed {Count} option values from JsonElement", optionValuesList.Count);
                    }
                    else if (optionValues is List<object> objectList)
                    {
                        optionValuesList = objectList.Cast<Dictionary<string, object>>().ToList();
                        _logger.LogDebug("🔍 [VARIANT-TRANSFORM] Using {Count} option values from List<object>", optionValuesList.Count);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [VARIANT-TRANSFORM] Unexpected option_values type: {Type}, value: {Value}", 
                            optionValues?.GetType().Name ?? "null", JsonSerializer.Serialize(optionValues));
                    }
                    
                    // Transform each option value
                    if (optionValuesList != null && optionValuesList.Count > 0)
                    {
                        _logger.LogDebug("🔄 [VARIANT-TRANSFORM] Transforming {Count} option values", optionValuesList.Count);
                        
                        foreach (var ovDict in optionValuesList)
                        {
                            _logger.LogDebug("🔍 [VARIANT-TRANSFORM] Source option_value: {OptionValue}", JsonSerializer.Serialize(ovDict));
                            
                            try
                            {
                                var transformedOV = await TransformOptionValueAsync(ovDict, migrationId);
                                
                                // Ensure we have the required fields after transformation
                                if (transformedOV.ContainsKey("id") && transformedOV.ContainsKey("option_id"))
                                {
                                    var cleanOV = new Dictionary<string, object>
                                    {
                                        ["id"] = transformedOV["id"],         // option_value ID (integer)
                                        ["option_id"] = transformedOV["option_id"]  // option ID (integer)
                                    };
                                    transformedOptionValues.Add(cleanOV);
                                    _logger.LogDebug("✅ [VARIANT-TRANSFORM] Transformed option_value: {TransformedOptionValue}", JsonSerializer.Serialize(cleanOV));
                                }
                                else
                                {
                                    _logger.LogError("❌ [VARIANT-TRANSFORM] Missing required fields after transformation: {TransformedOptionValue}", JsonSerializer.Serialize(transformedOV));
                                }
                            }
                            catch (InvalidOperationException ex) when (ex.Message.Contains("mapping not found"))
                            {
                                _logger.LogWarning("⚠️ [VARIANT-TRANSFORM] Option mapping not found for option_value: {OptionValue}. Will create variant with empty option_values array.", JsonSerializer.Serialize(ovDict));
                                // Break out of the loop - if one option mapping is missing, we'll create variant without option_values
                                transformedOptionValues.Clear();
                                break;
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [VARIANT-TRANSFORM] No option values to transform for variant with product_id {ProductId}", sourceProductId);
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ [VARIANT-TRANSFORM] Variant missing option_values for product_id {ProductId}", sourceProductId);
                }
                
                transformedVariant["option_values"] = transformedOptionValues;
                
                if (transformedOptionValues.Count == 0)
                {
                    _logger.LogInformation("📦 [VARIANT-TRANSFORM] Creating variant with empty option_values array (no option mappings found) for product_id {ProductId}", sourceProductId);
                }
                else
                {
                    _logger.LogDebug("📦 [VARIANT-TRANSFORM] Creating variant with {OptionValuesCount} option_values for product_id {ProductId}", transformedOptionValues.Count, sourceProductId);
                }
                
                _logger.LogDebug("🎯 [VARIANT-TRANSFORM] Final variant payload: {VariantPayload}", JsonSerializer.Serialize(transformedVariant));
                
                // 4. OPTIONAL: Copy over standard variant fields if present
                var optionalFields = new[] { 
                    "cost_price", "price", "sale_price", "retail_price", 
                    "weight", "width", "height", "depth",
                    "is_free_shipping", "fixed_cost_shipping_price",
                    "purchasing_disabled", "purchasing_disabled_message",
                    "upc", "inventory_level", "inventory_warning_level", "bin_picking_number"
                };
                
                foreach (var field in optionalFields)
                {
                    if (variant.TryGetValue(field, out var value) && value != null)
                    {
                        transformedVariant[field] = value;
                    }
                }
                
                transformedVariants.Add(transformedVariant);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to transform variant with product_id {ProductId}", 
                    variant.TryGetValue("product_id", out var pid) ? pid : "unknown");
                
                // Continue-on-error: skip this variant and continue with others
                continue;
            }
        }

        _logger.LogDebug("✅ Successfully transformed {TransformedCount}/{TotalCount} variants", 
            transformedVariants.Count, variants.Count);

        return transformedVariants;
    }

    /// <summary>
    /// Gets destination product ID from Phase 1 entity mappings
    /// </summary>
    /// <param name="sourceProductId">Source product ID</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Destination product ID</returns>
    private async Task<string> GetDestinationProductIdAsync(string sourceProductId, string migrationId)
    {
        var mapping = await _migrationStorageService.GetEntityMappingAsync(
            migrationId, "products", sourceProductId);
        
        return mapping?.DestinationId ?? 
               throw new InvalidOperationException($"Product mapping not found for source ID: {sourceProductId}. Ensure Phase 1 (Products) completed successfully.");
    }

    /// <summary>
    /// Transforms option values by mapping option_id and id from Phase 2 option mappings
    /// Uses hierarchical JSON format stored in EntityMapping.OptionsMappingData
    /// </summary>
    /// <param name="sourceOptionValue">Source option value to transform</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Transformed option value with destination IDs</returns>
        private async Task<Dictionary<string, object>> TransformOptionValueAsync(
        Dictionary<string, object> sourceOptionValue, 
        string migrationId)
    {
        _logger.LogDebug("🔍 [OPTION-TRANSFORM] Input source option_value: {SourceOptionValue}", JsonSerializer.Serialize(sourceOptionValue));
        
        var transformedOV = new Dictionary<string, object>();
        
        if (sourceOptionValue.TryGetValue("option_id", out var sourceOptionId) &&
            sourceOptionValue.TryGetValue("id", out var sourceOptionValueId) &&
            sourceOptionId != null && sourceOptionValueId != null)
        {
            _logger.LogDebug("🔍 [OPTION-TRANSFORM] Looking up mappings for source option_id={SourceOptionId}, source option_value_id={SourceValueId}", 
                sourceOptionId, sourceOptionValueId);
            
            try
            {
                var destinationOptionId = await LookupDestinationOptionIdAsync(sourceOptionId.ToString()!, migrationId);
                var destinationOptionValueId = await LookupDestinationOptionValueIdAsync(
                    sourceOptionId.ToString()!, sourceOptionValueId.ToString()!, migrationId);
                
                _logger.LogDebug("🔍 [OPTION-TRANSFORM] Lookup results: option_id {SourceOptionId}→{DestOptionId}, option_value_id {SourceValueId}→{DestValueId}", 
                    sourceOptionId, destinationOptionId, sourceOptionValueId, destinationOptionValueId);
                
                // 🔧 FIX: Convert to integers as required by BigCommerce API
                transformedOV["option_id"] = int.Parse(destinationOptionId);
                transformedOV["id"] = int.Parse(destinationOptionValueId);
                
                _logger.LogDebug("✅ [OPTION-TRANSFORM] Successfully mapped option_value: option_id {SourceOptionId}→{DestOptionId}, id {SourceValueId}→{DestValueId}", 
                    sourceOptionId, destinationOptionId, sourceOptionValueId, destinationOptionValueId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [OPTION-TRANSFORM] Failed to map option_value option_id={SourceOptionId}, id={SourceValueId}", 
                    sourceOptionId, sourceOptionValueId);
                
                // Keep original IDs as fallback (may cause API errors, but allows debugging)
                _logger.LogWarning("⚠️ [OPTION-TRANSFORM] Using original option IDs as fallback");
                transformedOV["option_id"] = sourceOptionId;
                transformedOV["id"] = sourceOptionValueId;
            }
        }
        else
        {
            _logger.LogError("❌ [OPTION-TRANSFORM] Source option_value missing required fields (option_id, id): {SourceOptionValue}", JsonSerializer.Serialize(sourceOptionValue));
            
            // Copy any available fields as fallback
            if (sourceOptionValue.ContainsKey("option_id")) transformedOV["option_id"] = sourceOptionValue["option_id"];
            if (sourceOptionValue.ContainsKey("id")) transformedOV["id"] = sourceOptionValue["id"];
        }
        
        _logger.LogDebug("🎯 [OPTION-TRANSFORM] Final transformed option_value: {TransformedOptionValue}", JsonSerializer.Serialize(transformedOV));
        return transformedOV;
    }

    /// <summary>
    /// Looks up destination option ID from Phase 2 option mappings stored in EntityMapping.OptionsMappingData
    /// Searches through all product entity mappings to find the one containing the requested option
    /// </summary>
    private async Task<string> LookupDestinationOptionIdAsync(string sourceOptionId, string migrationId)
    {
        try
        {
            // Strategy: Search through all product entity mappings to find option mappings
            // This is a brute-force approach but ensures we find the mapping regardless of which product it belongs to
            var productMappings = await _migrationStorageService.GetEntityMappingsAsync(migrationId, "products");
            
            foreach (var productMapping in productMappings)
            {
                if (!string.IsNullOrEmpty(productMapping.OptionsMappingData))
                {
                    try
                    {
                        var optionsData = JsonSerializer.Deserialize<Dictionary<string, object>>(productMapping.OptionsMappingData);
                        
                        if (optionsData != null && optionsData.TryGetValue("options", out var optionsArray) && 
                            optionsArray is JsonElement optionsElement && optionsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var optionElement in optionsElement.EnumerateArray())
                            {
                                var optionDict = JsonSerializer.Deserialize<Dictionary<string, object>>(optionElement.GetRawText());
                                
                                if (optionDict != null && 
                                    optionDict.TryGetValue("sourceOptionId", out var sourceIdObj) && 
                                    sourceIdObj?.ToString() == sourceOptionId &&
                                    optionDict.TryGetValue("destinationOptionId", out var destIdObj))
                                {
                                    var destinationId = destIdObj?.ToString() ?? sourceOptionId;
                                    _logger.LogDebug("✅ Found option ID mapping: {SourceId} → {DestinationId}", sourceOptionId, destinationId);
                                    return destinationId;
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to parse option mappings JSON for product {ProductId}", productMapping.SourceId);
                        continue;
                    }
                }
            }
            
            // Fallback: Option mapping not found, throw exception to indicate mapping failure
            _logger.LogWarning("⚠️ Option ID mapping not found for {SourceOptionId}", sourceOptionId);
            throw new InvalidOperationException($"Option mapping not found for source option ID: {sourceOptionId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to lookup option ID mapping for {SourceOptionId}", sourceOptionId);
            return sourceOptionId; // Fallback to source ID
        }
    }

    /// <summary>
    /// Looks up destination option value ID from Phase 2 option mappings stored in EntityMapping.OptionsMappingData
    /// Searches for the specific option and then the specific option value within that option
    /// </summary>
    private async Task<string> LookupDestinationOptionValueIdAsync(string sourceOptionId, string sourceOptionValueId, string migrationId)
    {
        try
        {
            // Strategy: Search through all product entity mappings to find option value mappings
            var productMappings = await _migrationStorageService.GetEntityMappingsAsync(migrationId, "products");
            
            foreach (var productMapping in productMappings)
            {
                if (!string.IsNullOrEmpty(productMapping.OptionsMappingData))
                {
                    try
                    {
                        var optionsData = JsonSerializer.Deserialize<Dictionary<string, object>>(productMapping.OptionsMappingData);
                        
                        if (optionsData != null && optionsData.TryGetValue("options", out var optionsArray) && 
                            optionsArray is JsonElement optionsElement && optionsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var optionElement in optionsElement.EnumerateArray())
                            {
                                var optionDict = JsonSerializer.Deserialize<Dictionary<string, object>>(optionElement.GetRawText());
                                
                                if (optionDict != null && 
                                    optionDict.TryGetValue("sourceOptionId", out var sourceIdObj) && 
                                    sourceIdObj?.ToString() == sourceOptionId &&
                                    optionDict.TryGetValue("optionValues", out var optionValuesObj) &&
                                    optionValuesObj is JsonElement optionValuesElement && optionValuesElement.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var valueElement in optionValuesElement.EnumerateArray())
                                    {
                                        var valueDict = JsonSerializer.Deserialize<Dictionary<string, object>>(valueElement.GetRawText());
                                        
                                        if (valueDict != null &&
                                            valueDict.TryGetValue("sourceId", out var sourceValueIdObj) && 
                                            sourceValueIdObj?.ToString() == sourceOptionValueId &&
                                            valueDict.TryGetValue("destinationId", out var destValueIdObj))
                                        {
                                            var destinationId = destValueIdObj?.ToString() ?? sourceOptionValueId;
                                            _logger.LogDebug("✅ Found option value ID mapping: {SourceOptionId}.{SourceValueId} → {DestinationValueId}", 
                                                sourceOptionId, sourceOptionValueId, destinationId);
                                            return destinationId;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to parse option mappings JSON for product {ProductId}", productMapping.SourceId);
                        continue;
                    }
                }
            }
            
            // Fallback: Option value mapping not found, throw exception to indicate mapping failure
            _logger.LogWarning("⚠️ Option value ID mapping not found for {SourceOptionId}.{SourceValueId}", 
                sourceOptionId, sourceOptionValueId);
            throw new InvalidOperationException($"Option value mapping not found for source option ID: {sourceOptionId}, option value ID: {sourceOptionValueId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to lookup option value ID mapping for {SourceOptionId}.{SourceValueId}", 
                sourceOptionId, sourceOptionValueId);
            return sourceOptionValueId; // Fallback to source ID
        }
    }

    /// <summary>
    /// Handles batch-level errors with structured logging and continue-on-error policy
    /// Logs individual variant errors to OpenSearch for debugging and monitoring
    /// </summary>
    private async Task HandleBatchErrorAsync(
        Exception exception, 
        List<Dictionary<string, object>> batchVariants, 
        string migrationId, 
        StoreConfiguration destinationStore,
        string? requestPayload,
        string? responsePayload,
        CancellationToken cancellationToken)
    {
        try
        {
            // Extract response payload from exception if not provided
            responsePayload ??= _errorHandlingService.ExtractResponsePayloadFromException(exception);
            
            // Create batch processing request for error logging
            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = migrationId,
                EntityType = "variants",
                BatchNumber = 1, // Sub-batch number not available here
                TotalBatches = 1,
                EntityIds = batchVariants.Select(v => v.TryGetValue("id", out var id) ? id?.ToString() ?? "unknown" : "unknown").ToList(),
                SourceStore = destinationStore, // Best approximation for logging context
                DestinationStore = destinationStore
            };

            // Log structured batch error
            await _errorHandlingService.LogStructuredMigrationErrorAsync(
                exception, batchVariants, batchRequest, "batch-create", cancellationToken);

            // Log individual variant errors for detailed analysis
            foreach (var variant in batchVariants)
            {
                var variantId = variant.TryGetValue("id", out var id) ? id?.ToString() ?? "unknown" : "unknown";
                var variantSku = variant.TryGetValue("sku", out var sku) ? sku?.ToString() ?? "no-sku" : "no-sku";
                
                var simpleErrorMessage = $"Variant batch creation failed: {exception.Message}";
                
                await _errorHandlingService.LogEntityErrorAsync(
                    exception, variant, batchRequest, variantId, responsePayload, simpleErrorMessage, cancellationToken);
                
                _logger.LogDebug("🔗 [VARIANT-ERROR] Logged variant '{VariantSku}' (ID: {VariantId}) batch error to OpenSearch", 
                    variantSku, variantId);
            }

            _logger.LogInformation("✅ Successfully logged batch error for {VariantCount} variants to OpenSearch", batchVariants.Count);
        }
        catch (Exception logEx)
        {
            _logger.LogError(logEx, "🚨 CRITICAL: Failed to log variant batch errors to OpenSearch for migration {MigrationId}", migrationId);
        }
    }

    /// <summary>
    /// Handles migration-level errors with structured logging
    /// Used for top-level failures that affect the entire variant processing operation
    /// </summary>
    private async Task HandleMigrationLevelErrorAsync(
        Exception exception, 
        List<Dictionary<string, object>> allVariants, 
        string migrationId, 
        StoreConfiguration destinationStore,
        CancellationToken cancellationToken)
    {
        try
        {
            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = migrationId,
                EntityType = "variants",
                BatchNumber = 1,
                TotalBatches = 1,
                EntityIds = allVariants.Select(v => v.TryGetValue("id", out var id) ? id?.ToString() ?? "unknown" : "unknown").ToList(),
                SourceStore = destinationStore,
                DestinationStore = destinationStore
            };

            await _errorHandlingService.LogStructuredMigrationErrorAsync(
                exception, allVariants, batchRequest, "migration-level", cancellationToken);

            _logger.LogInformation("✅ Successfully logged migration-level error for {VariantCount} variants to OpenSearch", allVariants.Count);
        }
        catch (Exception logEx)
        {
            _logger.LogError(logEx, "🚨 CRITICAL: Failed to log migration-level error to OpenSearch for migration {MigrationId}", migrationId);
        }
    }

    /// <summary>
    /// Determines if an exception represents an infrastructure error that should stop migration
    /// Infrastructure errors: storage unavailable, network failures, authentication issues
    /// Application errors: BigCommerce API errors, validation failures, data transformation issues
    /// </summary>
    private static bool IsInfrastructureError(Exception exception)
    {
        // Infrastructure error patterns based on critical memory requirements
        return exception switch
        {
            // Network and connectivity issues
            System.Net.Http.HttpRequestException => true,
            System.Net.Sockets.SocketException => true,
            TimeoutException => true,
            
            // Authentication and authorization issues  
            UnauthorizedAccessException => true,
            System.Security.SecurityException => true,
            
            // Storage and database issues
            System.Data.Common.DbException => true,
            Azure.RequestFailedException ex when ex.Status >= 500 => true, // Azure service errors
            
            // Memory and system resource issues
            OutOfMemoryException => true,
            StackOverflowException => true,
            
            // Configuration and dependency injection issues
            InvalidOperationException ex when ex.Message.Contains("configuration") => true,
            InvalidOperationException ex when ex.Message.Contains("service") || ex.Message.Contains("dependency") => true,
            
            // All other exceptions are considered application-level errors (continue-on-error)
            _ => false
        };
    }

    /// <summary>
    /// Validates store configuration
    /// </summary>
    /// <param name="storeConfig">Store configuration to validate</param>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    private static void ValidateStoreConfiguration(StoreConfiguration storeConfig)
    {
        if (storeConfig == null || !storeConfig.IsValid())
            throw new ArgumentException("Invalid destination store configuration");
    }
} 