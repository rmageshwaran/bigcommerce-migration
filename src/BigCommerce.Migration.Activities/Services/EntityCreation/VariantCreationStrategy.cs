using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Models;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating product variants in destination stores using BigCommerce Batch API
/// Implements Phase 3 of Enhanced Product Migration with parallel sub-batch processing
/// Uses PUT /v3/catalog/variants API with 50 variants per batch for optimal performance
/// 
/// ✅ TASK 4 COMPLETED: Implements option-combination-based uniqueness (adopted from Rollback API)
/// - Replaces flawed SKU-based duplicate prevention with sophisticated option-combination logic
/// - Only skips variants with identical option combinations, not just matching SKUs
/// - Allows legitimate variants that share SKUs but have different option combinations
/// - Aligns with BigCommerce's actual duplicate detection behavior
/// </summary>
public class VariantCreationStrategy : IEntityCreationStrategy
{
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly ISubBatchProcessor _subBatchProcessor;
    private readonly ISubBatchConfigurationService _configService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly ILogger<VariantCreationStrategy> _logger;

    /// <summary>
    /// Executes an operation with retry logic for "Too many simultaneous requests" errors
    /// Implements smart retry to handle initial concurrent burst issues without over-engineering
    /// Enhanced with comprehensive debug logging to analyze retry patterns
    /// </summary>
    private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 2000;
        var startTime = DateTime.UtcNow;
        
        _logger.LogDebug("🔄 [RETRY-START] Starting {Operation} with retry logic (max {MaxRetries} attempts)", 
            operationName, maxRetries);
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var attemptStartTime = DateTime.UtcNow;
            
            try 
            {
                _logger.LogDebug("🚀 [RETRY-ATTEMPT-{Attempt}] Executing {Operation} (attempt {Attempt}/{MaxRetries})", 
                    attempt, operationName, attempt, maxRetries);
                    
                var result = await operation();
                
                var attemptDuration = DateTime.UtcNow - attemptStartTime;
                var totalDuration = DateTime.UtcNow - startTime;
                
                if (attempt == 1)
                {
                    _logger.LogDebug("✅ [RETRY-SUCCESS-FIRST] {Operation} succeeded on first attempt in {Duration}ms", 
                        operationName, attemptDuration.TotalMilliseconds);
                }
                else
                {
                    _logger.LogInformation("✅ [RETRY-SUCCESS-AFTER-{PreviousAttempts}] {Operation} succeeded on attempt {Attempt}/{MaxRetries} after {TotalDuration}ms total (this attempt: {AttemptDuration}ms)", 
                        attempt - 1, operationName, attempt, maxRetries, totalDuration.TotalMilliseconds, attemptDuration.TotalMilliseconds);
                }
                
                return result;
            }
            catch (Exception ex) when (
                ex.Message.Contains("Too many simultaneous requests", StringComparison.OrdinalIgnoreCase) && 
                attempt < maxRetries)
            {
                var attemptDuration = DateTime.UtcNow - attemptStartTime;
                var delay = baseDelayMs * attempt; // 2s, 4s, 6s exponential backoff
                
                _logger.LogWarning("🚨 [RETRY-FAILED-{Attempt}] {Operation} failed on attempt {Attempt}/{MaxRetries} after {AttemptDuration}ms due to rate limit: {Error}. Retrying in {Delay}ms", 
                    attempt, operationName, attempt, maxRetries, attemptDuration.TotalMilliseconds, ex.Message, delay);
                    
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                var attemptDuration = DateTime.UtcNow - attemptStartTime;
                var totalDuration = DateTime.UtcNow - startTime;
                
                _logger.LogError("❌ [RETRY-FAILED-FINAL] {Operation} failed on attempt {Attempt}/{MaxRetries} after {AttemptDuration}ms with non-retryable error (total time: {TotalDuration}ms): {Error}", 
                    operationName, attempt, maxRetries, attemptDuration.TotalMilliseconds, totalDuration.TotalMilliseconds, ex.Message);
                throw;
            }
        }
        
        var finalTotalDuration = DateTime.UtcNow - startTime;
        _logger.LogError("❌ [RETRY-EXHAUSTED] {Operation} exhausted all {MaxRetries} retry attempts after {TotalDuration}ms", 
            operationName, maxRetries, finalTotalDuration.TotalMilliseconds);
            
        throw new InvalidOperationException($"All {maxRetries} retry attempts exhausted for {operationName}");
    }

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
    public string EntityType => "variants";  // ✅ FIXED: Match orchestrator expectation for variants

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

        // 🚀 PERFORMANCE: Clear single-item cache at the start of each batch
        _cachedProductId = null;
        _cachedProductMapping = null;
        //_logger.LogDebug("🗑️ [CACHE-CLEAR] Cleared single-item product mapping cache for new batch");

        _logger.LogInformation("🚀 Creating {Count} variants using optimized cached lookups for migration {MigrationId}", 
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
                var batchId = Guid.NewGuid().ToString("N")[..8]; // Short batch ID for tracking
                var outerBatchStartTime = DateTime.UtcNow;
                
                _logger.LogInformation("🎯 [BATCH-{BatchId}] Starting processing of {Count} variants at {StartTime} for migration {MigrationId}", 
                    batchId, batch50Variants.Count, outerBatchStartTime.ToString("HH:mm:ss.fff"), migrationId);
                
                try
                {
                    // 1. Transform all 50 variants (bulk ID mapping)
                    var transformedVariants = await TransformVariantsBulkAsync(batch50Variants, migrationId, ct);
                    
                    // 2. 🎯 TASK 4 FIX: Skip API call if all variants were skipped (empty array)
                    if (!transformedVariants.Any())
                    {
                        _logger.LogInformation("🚫 [ALL-SKIPPED] All {Count} variants in batch were skipped as duplicates - no API call needed", 
                            batch50Variants.Count);
                        
                        // Return success result representing the skipped variants
                        var skippedResults = new List<Dictionary<string, object>>();
                        for (int i = 0; i < batch50Variants.Count; i++)
                        {
                            skippedResults.Add(new Dictionary<string, object>
                            {
                                ["status"] = "skipped",
                                ["reason"] = "duplicate_option_combination", // ✅ Updated reason
                                ["source_index"] = i
                            });
                        }
                        return skippedResults;
                    }
                    
                    // 3. Single batch API call (PUT /catalog/variants) - only if we have variants to create
                    var url = $"{destinationStore.GetApiBaseUrl()}/catalog/variants";
                    requestPayload = JsonSerializer.Serialize(transformedVariants);
                    
                    var apiCallStartTime = DateTime.UtcNow;
                    _logger.LogInformation("🚀 [BATCH-START] PUT {Url} with {Count} variants (out of {OriginalCount} after skipping duplicates) at {StartTime}", 
                        url, transformedVariants.Count, batch50Variants.Count, apiCallStartTime.ToString("HH:mm:ss.fff"));
                    
                    // 🔄 SMART RETRY: Wrap API call with retry logic for "Too many simultaneous requests"
                    var response = await ExecuteWithRetry(
                        async () => {
                            var apiCallStart = DateTime.UtcNow;
                            _logger.LogDebug("🌐 [API-CALL-START] Making BigCommerce API call for {Count} variants at {ApiStartTime}", 
                                transformedVariants.Count, apiCallStart.ToString("HH:mm:ss.fff"));
                                
                            var result = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(
                                ApiRequest.CreatePut(url, requestPayload, destinationStore), 
                                ct);
                                
                            var apiCallDuration = DateTime.UtcNow - apiCallStart;
                            _logger.LogDebug("✅ [API-CALL-SUCCESS] BigCommerce API call completed in {Duration}ms for {Count} variants", 
                                apiCallDuration.TotalMilliseconds, transformedVariants.Count);
                                
                            return result;
                        },
                        $"Variant batch creation [BATCH-{batchId}] ({transformedVariants.Count} variants) - Started at {outerBatchStartTime:HH:mm:ss.fff}",
                        ct);
                    
                    // 🔧 FIX: Extract variants from BigCommerce API response wrapper
                    var createdVariants = ExtractVariantsFromResponse(response);
                    
                    var batchCompletionTime = DateTime.UtcNow;
                    var totalBatchDuration = batchCompletionTime - outerBatchStartTime;
                    _logger.LogInformation("✅ [BATCH-{BatchId}] Successfully created {Count} variants via batch API in {Duration}ms (started at {StartTime})", 
                        batchId, createdVariants.Count, totalBatchDuration.TotalMilliseconds, outerBatchStartTime.ToString("HH:mm:ss.fff"));
                    
                    // 🚨 STATUS FIX: Include skipped entities in the result for proper counting
                    var allResults = new List<Dictionary<string, object>>(createdVariants);
                    
                    // Add skipped entities (those that were filtered out during transformation)
                    var skippedCount = batch50Variants.Count - transformedVariants.Count;
                    for (int i = 0; i < skippedCount; i++)
                    {
                        allResults.Add(new Dictionary<string, object>
                        {
                            ["status"] = "skipped",
                            ["reason"] = "duplicate_default_variant",
                            ["source_index"] = transformedVariants.Count + i
                        });
                    }
                    
                    _logger.LogInformation("📊 [SKIPPED-FIX] Returning {TotalCount} results: {CreatedCount} created + {SkippedCount} skipped", 
                        allResults.Count, createdVariants.Count, skippedCount);
                    
                    // 4. ❌ NO MAPPING STORAGE - Variants are leaf entities with no dependents
                    return allResults;
                }
                catch (Exception ex)
                {
                    var batchFailureTime = DateTime.UtcNow;
                    var failureDuration = batchFailureTime - outerBatchStartTime;
                    _logger.LogError("❌ [BATCH-{BatchId}] Batch failed after {Duration}ms (started at {StartTime}): {Error}", 
                        batchId, failureDuration.TotalMilliseconds, outerBatchStartTime.ToString("HH:mm:ss.fff"), ex.Message);
                    
                    // 🚫 CANCELLATION FIX: Distinguish between actual failures and cancellation-induced failures
                    bool isCancellationError = ex.Message.Contains("Migration cancelled", StringComparison.OrdinalIgnoreCase) ||
                                             ex.Message.Contains("User requested cancellation", StringComparison.OrdinalIgnoreCase) ||
                                             ex is OperationCanceledException;
                    
                    if (isCancellationError)
                    {
                        _logger.LogInformation("🚫 Batch of {Count} variants was cancelled in migration {MigrationId}: {ErrorMessage}",
                            batch50Variants.Count, migrationId, ex.Message);
                        
                        // Return cancelled entities instead of empty list
                        var cancelledVariants = new List<Dictionary<string, object>>();
                        for (int i = 0; i < batch50Variants.Count; i++)
                        {
                            var variant = batch50Variants[i];
                            var sku = variant.TryGetValue("sku", out var skuValue) ? skuValue?.ToString() : "unknown";
                            var productId = variant.TryGetValue("product_id", out var prodId) ? prodId?.ToString() : "unknown";
                            
                            cancelledVariants.Add(new Dictionary<string, object>
                            {
                                ["status"] = "cancelled",
                                ["reason"] = "migration_cancelled",
                                ["original_product_id"] = productId,
                                ["sku"] = sku,
                                ["batch_index"] = i
                            });
                        }
                        
                        return cancelledVariants;
                    }
                    else
                    {
                        _logger.LogError(ex, "❌ Failed to create batch of {Count} variants", batch50Variants.Count);
                        
                        // ✅ ENHANCED ERROR HANDLING: Categorize and log with structured data
                        await HandleBatchErrorAsync(ex, batch50Variants, migrationId, destinationStore, requestPayload, responsePayload, ct);
                        
                        // Continue-on-error policy: return empty list to continue with other batches
                        return new List<Dictionary<string, object>>();
                    }
                }
            }

            // Use SubBatchProcessor: 250 variants → 5 concurrent batches of 50
            var result = await _subBatchProcessor.ProcessInSubBatchesAsync(
                entities, config, BatchVariantProcessor, "variants", migrationId, cancellationToken);

            _logger.LogInformation("✅ Successfully processed {ProcessedCount}/{TotalCount} variants for migration {MigrationId}", 
                result?.Count ?? 0, entities.Count, migrationId);

            // 🎯 VARIANT PIPELINE TRACKING: Add detailed creation summary
            var createdCount = result?.Count ?? 0;
            var skippedCount = entities.Count - createdCount;
            _logger.LogInformation("🎯 [VARIANT-PIPELINE-CREATION] ===== VARIANT CREATION COMPLETED ===== " +
                                  "MigrationId: {MigrationId}, InputVariants: {InputCount}, CreatedVariants: {CreatedCount}, " +
                                  "SkippedVariants: {SkippedCount}, SuccessRate: {SuccessRate:P1}",
                migrationId, entities.Count, createdCount, skippedCount, 
                entities.Count > 0 ? (double)createdCount / entities.Count : 0);

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
    /// ✅ TASK 4 FIX: Implements option-combination-based uniqueness instead of flawed SKU-based filtering
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
        
        // 🎯 TASK 4 FIX: Track unique option combinations instead of SKUs
        var uniqueVariantCombinations = new Dictionary<string, Dictionary<string, object>>();
        var skippedDuplicates = 0;
        var skippedNoOptions = 0;
        var skippedErrors = 0;
        
        // 🔍 DEBUG: Transformation input
        _logger.LogInformation("🔍 [VARIANT-TRANSFORM-DEBUG] Starting transformation of {InputCount} variants for migration {MigrationId}", 
            variants.Count, migrationId);

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
                    //_logger.LogDebug("🔗 Mapped product_id: {SourceId} → {DestinationId}", sourceProductId, destinationProductId);
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
                    //_logger.LogDebug("🔍 [VARIANT-TRANSFORM] Processing option_values for variant with product_id {ProductId}: {OptionValuesType}", 
                    //    sourceProductId, optionValues?.GetType().Name ?? "null");
                    
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
                        //_logger.LogDebug("🔍 [VARIANT-TRANSFORM] Parsed {Count} option values from JsonElement", optionValuesList.Count);
                    }
                    else if (optionValues is List<object> objectList)
                    {
                        optionValuesList = objectList.Cast<Dictionary<string, object>>().ToList();
                        //_logger.LogDebug("🔍 [VARIANT-TRANSFORM] Using {Count} option values from List<object>", optionValuesList.Count);
                    }
                    else if (optionValues is string jsonString && !string.IsNullOrEmpty(jsonString))
                    {
                        // 🔧 FIX: Handle JSON string format (common case from BigCommerce API)
                        try
                        {
                            // 🔍 LOG: Raw variant option_values JSON string before parsing
                            //_logger.LogDebug("🎯 [VARIANT-RAW] Raw option_values JSON string for product {ProductId}: {RawOptionValues}", 
                            //    sourceProductId, jsonString);
                            
                            var parsedArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString);
                            if (parsedArray != null)
                            {
                                optionValuesList = parsedArray;
                                //_logger.LogDebug("🔍 [VARIANT-TRANSFORM] Parsed {Count} option values from JSON string", optionValuesList.Count);
                                
                                // 🔍 LOG: Each parsed option value for comparison with mapping data
                                foreach (var ovDict in parsedArray)
                                {
                                    if (ovDict.TryGetValue("option_id", out var optId) && ovDict.TryGetValue("id", out var valueId))
                                    {
                                        //_logger.LogDebug("🎯 [VARIANT-OPTION] Source variant option_value: option_id={OptionId}, id={ValueId}", 
                                        //    optId, valueId);
                                    }
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "❌ [VARIANT-TRANSFORM] Failed to parse option_values JSON string: {JsonString}", jsonString);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [VARIANT-TRANSFORM] Unexpected option_values type: {Type}, value: {Value}", 
                            optionValues?.GetType().Name ?? "null", JsonSerializer.Serialize(optionValues));
                    }
                    
                    // Transform each option value
                    if (optionValuesList != null && optionValuesList.Count > 0)
                    {
                        //_logger.LogDebug("🔄 [VARIANT-TRANSFORM] Transforming {Count} option values", optionValuesList.Count);
                        
                        foreach (var ovDict in optionValuesList)
                        {
                            //_logger.LogDebug("🔍 [VARIANT-TRANSFORM] Source option_value: {OptionValue}", JsonSerializer.Serialize(ovDict));
                            
                            try
                            {
                                var transformedOV = await TransformOptionValueAsync(ovDict, sourceProductId.ToString()!, migrationId);
                                
                                // Ensure we have the required fields after transformation
                                if (transformedOV.ContainsKey("id") && transformedOV.ContainsKey("option_id"))
                                {
                                    var cleanOV = new Dictionary<string, object>
                                    {
                                        ["id"] = transformedOV["id"],         // option_value ID (integer)
                                        ["option_id"] = transformedOV["option_id"]  // option ID (integer)
                                    };
                                    transformedOptionValues.Add(cleanOV);
                                    //_logger.LogDebug("✅ [VARIANT-TRANSFORM] Transformed option_value: {TransformedOptionValue}", JsonSerializer.Serialize(cleanOV));
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
                    //_logger.LogDebug("📦 [VARIANT-TRANSFORM] Creating variant with {OptionValuesCount} option_values for product_id {ProductId}", transformedOptionValues.Count, sourceProductId);
                }
                
                //_logger.LogDebug("🎯 [VARIANT-TRANSFORM] Final variant payload: {VariantPayload}", JsonSerializer.Serialize(transformedVariant));
                
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
                
                // 🎯 TASK 4 FIX: Option-combination-based uniqueness (adopted from Rollback API)
                // Generate unique key based on option combinations, not SKU
                var variantSku = variant.TryGetValue("sku", out var skuValue) ? skuValue?.ToString() : "no-sku";
                
                if (transformedVariant.TryGetValue("option_values", out var optionValuesObj) && 
                    optionValuesObj is List<Dictionary<string, object>> transformedOptionValuesList && 
                    transformedOptionValuesList.Any())
                {
                    // Create unique key from option combinations (Rollback API approach)
                    var optionKey = string.Join("-", transformedOptionValuesList
                        .OrderBy(ov => ov.TryGetValue("option_id", out var oid) ? oid?.ToString() : "0")
                        .Select(ov => 
                        {
                            var optionId = ov.TryGetValue("option_id", out var oid) ? oid?.ToString() : "0";
                            var valueId = ov.TryGetValue("id", out var vid) ? vid?.ToString() : "0";
                            return $"{optionId}:{valueId}";
                        }));
                    
                    // Check for duplicate option combinations
                    if (!uniqueVariantCombinations.ContainsKey(optionKey))
                    {
                        uniqueVariantCombinations[optionKey] = transformedVariant;
                        transformedVariants.Add(transformedVariant);
                        
                        _logger.LogDebug("✅ [OPTION-UNIQUENESS] Added variant with SKU '{VariantSku}' and option combination: {OptionKey}", 
                            variantSku, optionKey);
                    }
                    else
                    {
                        skippedDuplicates++;
                        _logger.LogInformation("🚫 [OPTION-UNIQUENESS] Skipping variant with SKU '{VariantSku}' - duplicate option combination: {OptionKey}", 
                            variantSku, optionKey);
                    }
                }
                else
                {
                    // Variant has no options - allow based on SKU uniqueness within this batch
                    var noOptionKey = $"no-options-{variantSku}";
                    if (!uniqueVariantCombinations.ContainsKey(noOptionKey))
                    {
                        uniqueVariantCombinations[noOptionKey] = transformedVariant;
                        transformedVariants.Add(transformedVariant);
                        
                        _logger.LogDebug("✅ [NO-OPTIONS-UNIQUENESS] Added variant with SKU '{VariantSku}' (no options)", variantSku);
                    }
                    else
                    {
                        skippedNoOptions++;
                        _logger.LogInformation("🚫 [NO-OPTIONS-UNIQUENESS] Skipping variant with SKU '{VariantSku}' - duplicate SKU for variant without options", 
                            variantSku);
                    }
                }
            }
            catch (Exception ex)
            {
                skippedErrors++;
                _logger.LogError(ex, "❌ Failed to transform variant with product_id {ProductId}", 
                    variant.TryGetValue("product_id", out var pid) ? pid : "unknown");
                
                // Continue-on-error: skip this variant and continue with others
                continue;
            }
        }

        // 🎯 TASK 4 FIX: Enhanced transformation results with detailed uniqueness tracking
        var totalSkipped = skippedDuplicates + skippedNoOptions + skippedErrors;
        _logger.LogInformation("🔍 [VARIANT-TRANSFORM-DEBUG] ===== TRANSFORMATION COMPLETED ===== " +
                              "InputCount: {InputCount}, TransformedCount: {OutputCount}, TotalSkipped: {TotalSkipped} " +
                              "(DuplicateOptions: {DuplicateOptions}, DuplicateNoOptions: {DuplicateNoOptions}, Errors: {Errors}) " +
                              "for migration {MigrationId}", 
            variants.Count, transformedVariants.Count, totalSkipped, 
            skippedDuplicates, skippedNoOptions, skippedErrors, migrationId);

        // 🎯 TASK 4 SUCCESS: Log the improvement from SKU-based to option-combination-based logic
        _logger.LogInformation("✅ [TASK-4-SUCCESS] Option-combination-based uniqueness implemented successfully. " +
                              "Processed {UniqueCount} unique option combinations, skipped {DuplicateCount} true duplicates", 
            uniqueVariantCombinations.Count, skippedDuplicates + skippedNoOptions);

        return transformedVariants;
    }

    /// <summary>
    /// Single-item cache for current product mapping to avoid repeated database calls for variants of the same product
    /// Only holds one product's mapping data at a time to minimize memory usage
    /// </summary>
    private string? _cachedProductId = null;
    private ProductMappingData? _cachedProductMapping = null;

    /// <summary>
    /// Extracts variant data from BigCommerce API response wrapper
    /// BigCommerce bulk APIs return data wrapped in a "data" field
    /// </summary>
    /// <param name="response">API response from BigCommerce</param>
    /// <returns>List of created variants</returns>
    private List<Dictionary<string, object>> ExtractVariantsFromResponse(Dictionary<string, object>? response)
    {
        if (response?.TryGetValue("data", out var dataValue) == true)
        {
            List<Dictionary<string, object>>? variants = null;

            // Handle JsonElement (typical API response)
            if (dataValue is JsonElement dataElement && dataElement.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    variants = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText());
                    //_logger.LogDebug("✅ [RESPONSE-PARSE] Extracted {Count} variants from JsonElement data wrapper", variants?.Count ?? 0);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "❌ [RESPONSE-PARSE] Failed to deserialize variants from JsonElement");
                }
            }
            // Handle direct List<Dictionary> (test scenarios)
            else if (dataValue is List<Dictionary<string, object>> directList)
            {
                variants = directList;
                //_logger.LogDebug("✅ [RESPONSE-PARSE] Extracted {Count} variants from direct list", variants.Count);
            }
            // Handle IEnumerable<object> (alternative format)
            else if (dataValue is IEnumerable<object> enumerable)
            {
                try
                {
                    variants = enumerable.Cast<Dictionary<string, object>>().ToList();
                    // _logger.LogDebug("✅ [RESPONSE-PARSE] Extracted {Count} variants from enumerable", variants.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [RESPONSE-PARSE] Failed to cast enumerable to variant list");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ [RESPONSE-PARSE] Unexpected data format in response: {DataType}", dataValue?.GetType().Name ?? "null");
            }

            return variants ?? new List<Dictionary<string, object>>();
        }

        _logger.LogWarning("⚠️ [RESPONSE-PARSE] No 'data' field found in BigCommerce API response");
        return new List<Dictionary<string, object>>();
    }

    /// <summary>
    /// Data structure to hold all mapping information for a product in one consolidated object
    /// Eliminates need for 3 separate database calls per variant
    /// </summary>
    private class ProductMappingData
    {
        public string DestinationProductId { get; set; } = string.Empty;
        public Dictionary<string, string> OptionIdMappings { get; set; } = new();
        public Dictionary<string, Dictionary<string, string>> OptionValueMappings { get; set; } = new();
        public string? ProductSku { get; set; } = null; // SKU from metadata to identify default variant
    }

    /// <summary>
    /// 🚀 PERFORMANCE OPTIMIZED: Gets all required mapping data for a product in a single database call
    /// Uses single-item cache to hold only current product's data (not all products)
    /// Uses specific rowkey (products_{sourceProductId}) instead of loading all products
    /// </summary>
    private async Task<ProductMappingData> GetProductMappingDataAsync(string sourceProductId, string migrationId)
    {
        // Check single-item cache first - avoid repeated database calls for same product
        if (_cachedProductId == sourceProductId && _cachedProductMapping != null)
        {
            //_logger.LogDebug("📋 [CACHE-HIT] Using cached mapping data for product {ProductId}", sourceProductId);
            return _cachedProductMapping;
        }

        //_logger.LogDebug("🔍 [CACHE-MISS] Fetching mapping data for product {ProductId} with specific rowkey", sourceProductId);

        // 🎯 PERFORMANCE FIX: Use GetEntityMappingAsync with specific rowkey instead of GetEntityMappingsAsync
        // This targets exactly one row (products_{sourceProductId}) instead of loading all products
        var mapping = await _migrationStorageService.GetEntityMappingAsync(
            migrationId, "products", sourceProductId);
        
        if (mapping == null)
        {
            throw new InvalidOperationException($"Product mapping not found for source ID: {sourceProductId}. Ensure Phase 1 (Products) completed successfully.");
        }

                    var productData = new ProductMappingData
            {
                DestinationProductId = mapping.DestinationId ?? throw new InvalidOperationException($"DestinationId is null for product {sourceProductId}")
            };

            // Extract product SKU from metadata to identify default variant that should be skipped
            if (!string.IsNullOrEmpty(mapping.Metadata))
            {
                try
                {
                    var metadataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(mapping.Metadata);
                    if (metadataDict != null && metadataDict.TryGetValue("Sku", out var skuObj))
                    {
                        productData.ProductSku = skuObj?.ToString();
                        //_logger.LogDebug("🔍 [SKU-EXTRACT] Found product SKU in metadata: {ProductSku} for product {ProductId}", 
                        //    productData.ProductSku, sourceProductId);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to parse metadata JSON for product {ProductId}", sourceProductId);
                }
            }

        // Parse and cache option mappings if available
        if (!string.IsNullOrEmpty(mapping.OptionsMappingData))
        {
            try
            {
                // 🔍 LOG: Raw OptionsMappingData from database before deserialization
                //_logger.LogDebug("🗃️ [MAPPING-RAW] Raw OptionsMappingData from database for product {ProductId}: {RawMappingData}", 
                //    sourceProductId, mapping.OptionsMappingData);
                
                var optionsData = JsonSerializer.Deserialize<Dictionary<string, object>>(mapping.OptionsMappingData);
                
                if (optionsData != null && optionsData.TryGetValue("options", out var optionsArray) && 
                    optionsArray is JsonElement optionsElement && optionsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var optionElement in optionsElement.EnumerateArray())
                    {
                        var optionDict = JsonSerializer.Deserialize<Dictionary<string, object>>(optionElement.GetRawText());
                        
                        if (optionDict != null && 
                            optionDict.TryGetValue("sourceOptionId", out var sourceIdObj) && 
                            optionDict.TryGetValue("destinationOptionId", out var destIdObj))
                        {
                            var sourceOptionIdStr = sourceIdObj?.ToString();
                            var destOptionIdStr = destIdObj?.ToString();
                            
                            if (!string.IsNullOrEmpty(sourceOptionIdStr) && !string.IsNullOrEmpty(destOptionIdStr))
                            {
                                // Cache option ID mapping
                                productData.OptionIdMappings[sourceOptionIdStr] = destOptionIdStr;
                                //_logger.LogDebug("🗺️ [MAPPING-CACHE] Cached option ID mapping: {SourceOptionId} → {DestOptionId}", 
                                //    sourceOptionIdStr, destOptionIdStr);

                                // Cache option value mappings for this option
                                if (optionDict.TryGetValue("optionValues", out var optionValuesObj) &&
                                    optionValuesObj is JsonElement optionValuesElement && optionValuesElement.ValueKind == JsonValueKind.Array)
                                {
                                    var valueIdMappings = new Dictionary<string, string>();
                                    
                                    foreach (var valueElement in optionValuesElement.EnumerateArray())
                                    {
                                        var valueDict = JsonSerializer.Deserialize<Dictionary<string, object>>(valueElement.GetRawText());
                                        
                                        if (valueDict != null &&
                                            valueDict.TryGetValue("sourceId", out var sourceValueIdObj) && 
                                            valueDict.TryGetValue("destinationId", out var destValueIdObj))
                                        {
                                            var sourceValueIdStr = sourceValueIdObj?.ToString();
                                            var destValueIdStr = destValueIdObj?.ToString();
                                            
                                            if (!string.IsNullOrEmpty(sourceValueIdStr) && !string.IsNullOrEmpty(destValueIdStr))
                                            {
                                                valueIdMappings[sourceValueIdStr] = destValueIdStr;
                                                //_logger.LogDebug("🗺️ [MAPPING-CACHE] Cached option value mapping: option{OptionId}.value{SourceValueId} → {DestValueId}", 
                                                //    sourceOptionIdStr, sourceValueIdStr, destValueIdStr);
                                            }
                                        }
                                    }
                                    
                                    productData.OptionValueMappings[sourceOptionIdStr] = valueIdMappings;
                                    //_logger.LogDebug("🗺️ [MAPPING-COMPLETE] Cached {ValueCount} option value mappings for option {OptionId}", 
                                    //    valueIdMappings.Count, sourceOptionIdStr);
                                }
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to parse option mappings JSON for product {ProductId}", sourceProductId);
            }
        }

        // Cache the result in single-item cache for subsequent variants of the same product
        _cachedProductId = sourceProductId;
        _cachedProductMapping = productData;
        
        //_logger.LogDebug("✅ [CACHE-STORE] Cached mapping data for product {ProductId}: destinationId={DestId}, sku={ProductSku}, optionMappings={OptionCount}, optionValueMappings={ValueCount}", 
        //    sourceProductId, productData.DestinationProductId, productData.ProductSku,
        //    productData.OptionIdMappings.Count, 
        //    productData.OptionValueMappings.Count);

        return productData;
    }

    /// <summary>
    /// Gets destination product ID from consolidated mapping data
    /// </summary>
    private async Task<string> GetDestinationProductIdAsync(string sourceProductId, string migrationId)
    {
        var mappingData = await GetProductMappingDataAsync(sourceProductId, migrationId);
        return mappingData.DestinationProductId;
    }

    /// <summary>
    /// 🚀 PERFORMANCE OPTIMIZED: Transforms option values using consolidated mapping data
    /// Uses cached product mapping data instead of separate database calls
    /// </summary>
    /// <param name="sourceOptionValue">Source option value to transform</param>
    /// <param name="sourceProductId">Source product ID for mapping lookup</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Transformed option value with destination IDs</returns>
        private async Task<Dictionary<string, object>> TransformOptionValueAsync(
        Dictionary<string, object> sourceOptionValue, 
        string sourceProductId,
        string migrationId)
    {
        //_logger.LogDebug("🔍 [OPTION-TRANSFORM] Input source option_value: {SourceOptionValue}", JsonSerializer.Serialize(sourceOptionValue));
        
        var transformedOV = new Dictionary<string, object>();
        
        if (sourceOptionValue.TryGetValue("option_id", out var sourceOptionId) &&
            sourceOptionValue.TryGetValue("id", out var sourceOptionValueId) &&
            sourceOptionId != null && sourceOptionValueId != null)
        {
            //_logger.LogDebug("🔍 [OPTION-TRANSFORM] Looking up mappings for source option_id={SourceOptionId}, source option_value_id={SourceValueId}", 
            //    sourceOptionId, sourceOptionValueId);
            
            try
            {
                // 🚀 PERFORMANCE OPTIMIZED: Use consolidated mapping lookup instead of 2 separate database calls
                var mappingData = await GetProductMappingDataAsync(sourceProductId, migrationId);
                
                var destinationOptionId = LookupDestinationOptionIdFromCache(sourceOptionId.ToString()!, mappingData);
                var destinationOptionValueId = LookupDestinationOptionValueIdFromCache(
                    sourceOptionId.ToString()!, sourceOptionValueId.ToString()!, mappingData);
                
                //_logger.LogDebug("🔍 [OPTION-TRANSFORM] Lookup results: option_id {SourceOptionId}→{DestOptionId}, option_value_id {SourceValueId}→{DestValueId}", 
                //    sourceOptionId, destinationOptionId, sourceOptionValueId, destinationOptionValueId);
                
                // 🔧 FIX: Convert to integers as required by BigCommerce API
                transformedOV["option_id"] = int.Parse(destinationOptionId);
                transformedOV["id"] = int.Parse(destinationOptionValueId);
                
                //_logger.LogDebug("✅ [OPTION-TRANSFORM] Successfully mapped option_value: option_id {SourceOptionId}→{DestOptionId}, id {SourceValueId}→{DestValueId}", 
                //    sourceOptionId, destinationOptionId, sourceOptionValueId, destinationOptionValueId);
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
        
        //_logger.LogDebug("🎯 [OPTION-TRANSFORM] Final transformed option_value: {TransformedOptionValue}", JsonSerializer.Serialize(transformedOV));
        return transformedOV;
    }

    /// <summary>
    /// 🚀 PERFORMANCE OPTIMIZED: Looks up destination option ID from cached mapping data
    /// Eliminates database calls by using pre-loaded mapping data
    /// </summary>
    private string LookupDestinationOptionIdFromCache(string sourceOptionId, ProductMappingData mappingData)
    {
        if (mappingData.OptionIdMappings.TryGetValue(sourceOptionId, out var destinationOptionId))
        {
            //_logger.LogDebug("✅ [CACHE-LOOKUP] Found option ID mapping: {SourceId} → {DestinationId}", sourceOptionId, destinationOptionId);
            return destinationOptionId;
        }

        _logger.LogWarning("⚠️ [CACHE-LOOKUP] Option ID mapping not found for {SourceOptionId}", sourceOptionId);
        throw new InvalidOperationException($"Option mapping not found for source option ID: {sourceOptionId}");
    }

    /// <summary>
    /// 🚀 PERFORMANCE OPTIMIZED: Looks up destination option value ID from cached mapping data
    /// Eliminates database calls by using pre-loaded mapping data
    /// </summary>
    private string LookupDestinationOptionValueIdFromCache(string sourceOptionId, string sourceOptionValueId, ProductMappingData mappingData)
    {
        if (mappingData.OptionValueMappings.TryGetValue(sourceOptionId, out var optionValueMappings) &&
            optionValueMappings.TryGetValue(sourceOptionValueId, out var destinationOptionValueId))
        {
            // _logger.LogDebug("✅ [CACHE-LOOKUP] Found option value ID mapping: {SourceOptionId}.{SourceValueId} → {DestinationValueId}", 
            //    sourceOptionId, sourceOptionValueId, destinationOptionValueId);
            return destinationOptionValueId;
        }

        _logger.LogWarning("⚠️ [CACHE-LOOKUP] Option value ID mapping not found for {SourceOptionId}.{SourceValueId}", 
            sourceOptionId, sourceOptionValueId);
        throw new InvalidOperationException($"Option value mapping not found for source option ID: {sourceOptionId}, option value ID: {sourceOptionValueId}");
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
                
                // _logger.LogDebug("🔗 [VARIANT-ERROR] Logged variant '{VariantSku}' (ID: {VariantId}) batch error to OpenSearch", 
                //    variantSku, variantId);
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