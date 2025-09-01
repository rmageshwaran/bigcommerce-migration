using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Models;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating product metafields in destination stores using BigCommerce Batch API
/// Implements Phase 7 of Enhanced Product Migration with parallel sub-batch processing
/// Uses POST /v3/catalog/products/metafields API with 50 metafields per batch for optimal performance
/// 
/// Key Features:
/// - IApiRequestHandler compliance for consistent rate limiting and logging
/// - Sub-batch processing: 250 metafields → 5 concurrent batches of 50
/// - Product ID mapping: Maps source resource_id to destination product IDs
/// - Live cancellation support with 4-level checks (Migration, EntityType, Batch, Store)
/// - SignalR progress events using SignalREventFactory (identical to variants)
/// - Continue-on-error policy with structured logging
/// - No mapping storage needed (metafields are leaf entities)
/// 
/// Architecture Compliance:
/// - Follows exact VariantCreationStrategy pattern
/// - Uses IApiRequestHandler for rate limiting compliance
/// - Multi-instance safe with method-scoped caching
/// - Enterprise-grade error handling and performance monitoring
/// </summary>
public class ProductMetafieldsCreationStrategy : IEntityCreationStrategy
{
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly ISubBatchProcessor _subBatchProcessor;
    private readonly ISubBatchConfigurationService _configService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly ILogger<ProductMetafieldsCreationStrategy> _logger;

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

    public ProductMetafieldsCreationStrategy(
        IApiRequestHandler apiRequestHandler,
        IMigrationStorageService migrationStorageService,
        ISubBatchProcessor subBatchProcessor,
        ISubBatchConfigurationService configService,
        IEntityErrorHandlingService errorHandlingService,
        ILogger<ProductMetafieldsCreationStrategy> logger)
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
    public string EntityType => "product-metafields";

    /// <summary>
    /// Creates product metafields in the destination store using BigCommerce Batch API
    /// Processes metafields in sub-batches of 50 using POST /v3/catalog/products/metafields for optimal performance
    /// </summary>
    /// <param name="entities">Metafields to create (250 metafields from chunk)</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (not used for metafields)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created metafields with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("🚀 No product metafields provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        // Validate store configuration early (outside try-catch for proper exception handling)
        ValidateStoreConfiguration(destinationStore);

        _logger.LogInformation("🚀 Creating {Count} product metafields using product ID mappings for migration {MigrationId}", 
            entities.Count, migrationId);

        try
        {

            // Get metafields configuration
            var config = _configService.GetConfiguration("product-metafields");
            
            // Batch processor function for 50 metafields
            async Task<List<Dictionary<string, object>>> BatchMetafieldsProcessor(
                List<Dictionary<string, object>> batch50Metafields, CancellationToken ct)
            {
                string? requestPayload = null;
                string? responsePayload = null;
                var batchId = Guid.NewGuid().ToString("N")[..8]; // Short batch ID for tracking
                var outerBatchStartTime = DateTime.UtcNow;
                
                _logger.LogInformation("🎯 [BATCH-{BatchId}] Starting processing of {Count} metafields at {StartTime} for migration {MigrationId}", 
                    batchId, batch50Metafields.Count, outerBatchStartTime.ToString("HH:mm:ss.fff"), migrationId);
                
                try
                {
                    // 1. Transform all 50 metafields (product ID mapping)
                    var transformedMetafields = await TransformMetafieldsBulkAsync(batch50Metafields, migrationId, ct);
                    
                    // 2. Skip API call if all metafields were skipped (empty array)
                    if (!transformedMetafields.Any())
                    {
                        _logger.LogInformation("🚫 [ALL-SKIPPED] All {Count} metafields in batch were skipped - no API call needed", 
                            batch50Metafields.Count);
                        
                        // Return success result representing the skipped metafields
                        var skippedResults = new List<Dictionary<string, object>>();
                        for (int i = 0; i < batch50Metafields.Count; i++)
                        {
                            skippedResults.Add(new Dictionary<string, object>
                            {
                                ["status"] = "skipped",
                                ["reason"] = "product_mapping_not_found",
                                ["source_index"] = i
                            });
                        }
                        return skippedResults;
                    }
                    
                    // 3. Single batch API call (POST /catalog/products/metafields) - only if we have metafields to create
                    var url = $"{destinationStore.GetApiBaseUrl()}/catalog/products/metafields";
                    requestPayload = JsonSerializer.Serialize(transformedMetafields);
                    
                    var apiCallStartTime = DateTime.UtcNow;
                    _logger.LogInformation("🚀 [BATCH-START] POST {Url} with {Count} metafields (out of {OriginalCount} after mapping) at {StartTime}", 
                        url, transformedMetafields.Count, batch50Metafields.Count, apiCallStartTime.ToString("HH:mm:ss.fff"));
                    
                    // Smart retry: Wrap API call with retry logic for "Too many simultaneous requests"
                    var response = await ExecuteWithRetry(
                        async () => {
                            var apiCallStart = DateTime.UtcNow;
                            _logger.LogDebug("🌐 [API-CALL-START] Making BigCommerce API call for {Count} metafields at {ApiStartTime}", 
                                transformedMetafields.Count, apiCallStart.ToString("HH:mm:ss.fff"));
                                
                            var apiRequest = ApiRequest.CreatePost(url, requestPayload, destinationStore);
                            var result = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(
                                apiRequest, ct);
                                
                            var apiCallDuration = DateTime.UtcNow - apiCallStart;
                            _logger.LogDebug("✅ [API-CALL-SUCCESS] BigCommerce API call completed in {Duration}ms for {Count} metafields", 
                                apiCallDuration.TotalMilliseconds, transformedMetafields.Count);
                                
                            return result;
                        },
                        $"Metafields batch creation [BATCH-{batchId}] ({transformedMetafields.Count} metafields) - Started at {outerBatchStartTime:HH:mm:ss.fff}",
                        ct);
                    
                    // Extract metafields from BigCommerce API response wrapper
                    var createdMetafields = ExtractMetafieldsFromResponse(response);
                    
                    var batchCompletionTime = DateTime.UtcNow;
                    var totalBatchDuration = batchCompletionTime - outerBatchStartTime;
                    _logger.LogInformation("✅ [BATCH-{BatchId}] Successfully created {Count} metafields via batch API in {Duration}ms (started at {StartTime})", 
                        batchId, createdMetafields.Count, totalBatchDuration.TotalMilliseconds, outerBatchStartTime.ToString("HH:mm:ss.fff"));
                    
                    // Include skipped entities in the result for proper counting
                    var allResults = new List<Dictionary<string, object>>(createdMetafields);
                    
                    // Add skipped entities (those that were filtered out during transformation)
                    var skippedCount = batch50Metafields.Count - transformedMetafields.Count;
                    for (int i = 0; i < skippedCount; i++)
                    {
                        allResults.Add(new Dictionary<string, object>
                        {
                            ["status"] = "skipped",
                            ["reason"] = "product_mapping_not_found",
                            ["source_index"] = transformedMetafields.Count + i
                        });
                    }
                    
                    _logger.LogInformation("📊 [SKIPPED-FIX] Returning {TotalCount} results: {CreatedCount} created + {SkippedCount} skipped", 
                        allResults.Count, createdMetafields.Count, skippedCount);
                    
                    // NO MAPPING STORAGE - Metafields are leaf entities with no dependents
                    return allResults;
                }
                catch (Exception ex)
                {
                    var batchFailureTime = DateTime.UtcNow;
                    var failureDuration = batchFailureTime - outerBatchStartTime;
                    _logger.LogError("❌ [BATCH-{BatchId}] Batch failed after {Duration}ms (started at {StartTime}): {Error}", 
                        batchId, failureDuration.TotalMilliseconds, outerBatchStartTime.ToString("HH:mm:ss.fff"), ex.Message);
                    
                    // Distinguish between actual failures and cancellation-induced failures
                    bool isCancellationError = ex.Message.Contains("Migration cancelled", StringComparison.OrdinalIgnoreCase) ||
                                             ex.Message.Contains("User requested cancellation", StringComparison.OrdinalIgnoreCase) ||
                                             ex is OperationCanceledException;
                    
                    if (isCancellationError)
                    {
                        _logger.LogInformation("🚫 Batch of {Count} metafields was cancelled in migration {MigrationId}: {ErrorMessage}",
                            batch50Metafields.Count, migrationId, ex.Message);
                        
                        // Return cancelled entities instead of empty list
                        var cancelledMetafields = new List<Dictionary<string, object>>();
                        for (int i = 0; i < batch50Metafields.Count; i++)
                        {
                            var metafield = batch50Metafields[i];
                            var key = metafield.TryGetValue("key", out var keyValue) ? keyValue?.ToString() : "unknown";
                            var resourceId = metafield.TryGetValue("resource_id", out var resId) ? resId?.ToString() : "unknown";
                            
                            cancelledMetafields.Add(new Dictionary<string, object>
                            {
                                ["status"] = "cancelled",
                                ["reason"] = "migration_cancelled",
                                ["original_resource_id"] = resourceId,
                                ["key"] = key,
                                ["batch_index"] = i
                            });
                        }
                        
                        return cancelledMetafields;
                    }
                    else
                    {
                        _logger.LogError(ex, "❌ Failed to create batch of {Count} metafields", batch50Metafields.Count);
                        
                        // Enhanced error handling: Categorize and log with structured data
                        await HandleBatchErrorAsync(ex, batch50Metafields, migrationId, destinationStore, requestPayload, responsePayload, ct);
                        
                        // Continue-on-error policy: return empty list to continue with other batches
                        return new List<Dictionary<string, object>>();
                    }
                }
            }

            // Use SubBatchProcessor: 250 metafields → 5 concurrent batches of 50
            var result = await _subBatchProcessor.ProcessInSubBatchesAsync(
                entities, config, BatchMetafieldsProcessor, "product-metafields", migrationId, cancellationToken);

            _logger.LogInformation("✅ Successfully processed {ProcessedCount}/{TotalCount} metafields for migration {MigrationId}", 
                result?.Count ?? 0, entities.Count, migrationId);

            // Add detailed creation summary
            var createdCount = result?.Count ?? 0;
            var skippedCount = entities.Count - createdCount;
            _logger.LogInformation("🎯 [METAFIELDS-PIPELINE-CREATION] ===== METAFIELDS CREATION COMPLETED ===== " +
                                  "MigrationId: {MigrationId}, InputMetafields: {InputCount}, CreatedMetafields: {CreatedCount}, " +
                                  "SkippedMetafields: {SkippedCount}, SuccessRate: {SuccessRate:P1}",
                migrationId, entities.Count, createdCount, skippedCount, 
                entities.Count > 0 ? (double)createdCount / entities.Count : 0);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create {MetafieldsCount} metafields in migration {MigrationId}", 
                entities.Count, migrationId);

            // Enhanced error handling: Categorize infrastructure vs application errors
            if (IsInfrastructureError(ex))
            {
                _logger.LogCritical("🚨 INFRASTRUCTURE ERROR: {ErrorMessage} - STOPPING MIGRATION", ex.Message);
                throw; // Infrastructure errors must stop migration
            }

            // Log application errors: Log to OpenSearch with structured data for continue-on-error
            await HandleMigrationLevelErrorAsync(ex, entities, migrationId, destinationStore, cancellationToken);

            // Return empty list to avoid causing Durable Functions replay issues (continue-on-error)
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Transforms metafields by mapping resource_id from source product IDs to destination product IDs
    /// Performs bulk ID mapping for optimal performance with 50 metafields per batch
    /// Uses simple product ID mapping (much simpler than variants option combinations)
    /// </summary>
    /// <param name="metafields">Source metafields to transform</param>
    /// <param name="migrationId">Migration identifier for entity mapping lookup</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transformed metafields ready for BigCommerce Batch API</returns>
    private async Task<List<Dictionary<string, object>>> TransformMetafieldsBulkAsync(
        List<Dictionary<string, object>> metafields, 
        string migrationId, 
        CancellationToken cancellationToken)
    {
        var transformedMetafields = new List<Dictionary<string, object>>();
        
        // Track skipped metafields for logging
        var skippedNoResourceId = 0;
        var skippedProductMapping = 0;
        var skippedErrors = 0;
        
        // Multi-instance safe: Method-scoped cache for product mappings within this batch
        var productMappingCache = new Dictionary<string, string>();
        
        _logger.LogInformation("🔍 [METAFIELDS-TRANSFORM-DEBUG] Starting transformation of {InputCount} metafields for migration {MigrationId}", 
            metafields.Count, migrationId);

        foreach (var metafield in metafields)
        {
            try
            {
                // Create clean metafield payload with only required/allowed fields
                var transformedMetafield = new Dictionary<string, object>();
                
                // 1. REQUIRED: Map resource_id from source product ID to destination product ID
                if (metafield.TryGetValue("resource_id", out var sourceResourceId) && sourceResourceId != null)
                {
                    var sourceProductIdString = sourceResourceId.ToString()!;
                    var destinationProductId = await GetDestinationProductIdAsync(sourceProductIdString, migrationId, productMappingCache);
                    
                    if (destinationProductId != null)
                    {
                        transformedMetafield["resource_id"] = int.Parse(destinationProductId);
                        _logger.LogDebug("🔗 Mapped resource_id: {SourceId} → {DestinationId}", sourceProductIdString, destinationProductId);
                    }
                    else
                    {
                        skippedProductMapping++;
                        _logger.LogWarning("⚠️ Product mapping not found for resource_id {SourceProductId} - skipping metafield", sourceProductIdString);
                        continue; // Skip this metafield
                    }
                }
                else
                {
                    skippedNoResourceId++;
                    _logger.LogError("❌ Missing required resource_id for metafield - skipping");
                    continue;
                }
                
                // 2. Copy all other required fields (validated in transform strategy)
                var requiredFields = new[] { "permission_set", "namespace", "key", "value", "description" };
                foreach (var field in requiredFields)
                {
                    if (metafield.TryGetValue(field, out var value) && value != null)
                    {
                        transformedMetafield[field] = value;
                    }
                }
                
                // 3. Copy optional fields if present
                var optionalFields = new[] { "resource_type" };
                foreach (var field in optionalFields)
                {
                    if (metafield.TryGetValue(field, out var value) && value != null)
                    {
                        transformedMetafield[field] = value;
                    }
                }
                
                transformedMetafields.Add(transformedMetafield);
            }
            catch (Exception ex)
            {
                skippedErrors++;
                _logger.LogError(ex, "❌ Failed to transform metafield with resource_id {ResourceId}", 
                    metafield.TryGetValue("resource_id", out var rid) ? rid : "unknown");
                
                // Continue-on-error: skip this metafield and continue with others
                continue;
            }
        }

        // Enhanced transformation results with detailed tracking
        var totalSkipped = skippedNoResourceId + skippedProductMapping + skippedErrors;
        _logger.LogInformation("🔍 [METAFIELDS-TRANSFORM-DEBUG] ===== TRANSFORMATION COMPLETED ===== " +
                              "InputCount: {InputCount}, TransformedCount: {OutputCount}, TotalSkipped: {TotalSkipped} " +
                              "(NoResourceId: {NoResourceId}, ProductMapping: {ProductMapping}, Errors: {Errors}) " +
                              "for migration {MigrationId}", 
            metafields.Count, transformedMetafields.Count, totalSkipped, 
            skippedNoResourceId, skippedProductMapping, skippedErrors, migrationId);

        return transformedMetafields;
    }

    /// <summary>
    /// Multi-instance safe: Method-scoped cache for product mappings within single metafields batch processing
    /// Gets destination product ID from entitymappings table for resource_id mapping
    /// </summary>
    private async Task<string?> GetDestinationProductIdAsync(string sourceProductId, string migrationId, Dictionary<string, string> cache)
    {
        // Multi-instance safe: Check method-scoped cache first
        if (cache.TryGetValue(sourceProductId, out var cachedDestinationId))
        {
            _logger.LogDebug("📋 [CACHE-HIT] Using cached product mapping for {ProductId}", sourceProductId);
            return cachedDestinationId;
        }

        _logger.LogDebug("🔍 [CACHE-MISS] Fetching product mapping for {ProductId}", sourceProductId);

        // Use GetEntityMappingAsync with specific rowkey for performance
        var mapping = await _migrationStorageService.GetEntityMappingAsync(
            migrationId, "products", sourceProductId);
        
        if (mapping?.DestinationId != null)
        {
            // Cache the result for subsequent metafields of the same product
            cache[sourceProductId] = mapping.DestinationId;
            _logger.LogDebug("✅ [CACHE-STORE] Cached product mapping: {SourceId} → {DestinationId}", 
                sourceProductId, mapping.DestinationId);
            return mapping.DestinationId;
        }

        _logger.LogWarning("❌ [MAPPING-FAILURE] Product mapping not found for source ID: {SourceProductId} in migration {MigrationId}", 
            sourceProductId, migrationId);
        
        // Don't throw - return null to skip this metafield with continue-on-error
        return null;
    }

    /// <summary>
    /// Extracts metafield data from BigCommerce API response wrapper
    /// BigCommerce bulk APIs return data wrapped in a "data" field
    /// </summary>
    /// <param name="response">API response from BigCommerce</param>
    /// <returns>List of created metafields</returns>
    private List<Dictionary<string, object>> ExtractMetafieldsFromResponse(Dictionary<string, object>? response)
    {
        if (response?.TryGetValue("data", out var dataValue) == true)
        {
            List<Dictionary<string, object>>? metafields = null;

            // Handle JsonElement (typical API response)
            if (dataValue is JsonElement dataElement && dataElement.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    metafields = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText());
                    _logger.LogDebug("✅ [RESPONSE-PARSE] Extracted {Count} metafields from JsonElement data wrapper", metafields?.Count ?? 0);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "❌ [RESPONSE-PARSE] Failed to deserialize metafields from JsonElement");
                }
            }
            // Handle direct List<Dictionary> (test scenarios)
            else if (dataValue is List<Dictionary<string, object>> directList)
            {
                metafields = directList;
                _logger.LogDebug("✅ [RESPONSE-PARSE] Extracted {Count} metafields from direct list", metafields.Count);
            }
            // Handle IEnumerable<object> (alternative format)
            else if (dataValue is IEnumerable<object> enumerable)
            {
                try
                {
                    metafields = enumerable.Cast<Dictionary<string, object>>().ToList();
                    _logger.LogDebug("✅ [RESPONSE-PARSE] Extracted {Count} metafields from enumerable", metafields.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [RESPONSE-PARSE] Failed to cast enumerable to metafields list");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ [RESPONSE-PARSE] Unexpected data format in response: {DataType}", dataValue?.GetType().Name ?? "null");
            }

            return metafields ?? new List<Dictionary<string, object>>();
        }

        _logger.LogWarning("⚠️ [RESPONSE-PARSE] No 'data' field found in BigCommerce API response");
        return new List<Dictionary<string, object>>();
    }

    /// <summary>
    /// Handles batch-level errors with structured logging and continue-on-error policy
    /// Logs individual metafield errors to OpenSearch for debugging and monitoring
    /// </summary>
    private async Task HandleBatchErrorAsync(
        Exception exception, 
        List<Dictionary<string, object>> batchMetafields, 
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
                EntityType = "product-metafields",
                BatchNumber = 1, // Sub-batch number not available here
                TotalBatches = 1,
                EntityIds = batchMetafields.Select(m => m.TryGetValue("id", out var id) ? id?.ToString() ?? "unknown" : "unknown").ToList(),
                SourceStore = destinationStore, // Best approximation for logging context
                DestinationStore = destinationStore
            };

            // 🔧 FIX: Use actual request/response payloads instead of source data
            await _errorHandlingService.LogStructuredMigrationErrorAsync(
                exception, batchMetafields, batchRequest, "batch-create", requestPayload, responsePayload, cancellationToken);

            // Log individual metafield errors for detailed analysis
            foreach (var metafield in batchMetafields)
            {
                var metafieldId = metafield.TryGetValue("id", out var id) ? id?.ToString() ?? "unknown" : "unknown";
                var metafieldKey = metafield.TryGetValue("key", out var key) ? key?.ToString() ?? "no-key" : "no-key";
                
                var simpleErrorMessage = $"Metafield batch creation failed: {exception.Message}";
                
                await _errorHandlingService.LogEntityErrorAsync(
                    exception, metafield, batchRequest, metafieldId, responsePayload, simpleErrorMessage, cancellationToken);
                
                _logger.LogDebug("🔗 [METAFIELD-ERROR] Logged metafield '{MetafieldKey}' (ID: {MetafieldId}) batch error to OpenSearch", 
                    metafieldKey, metafieldId);
            }

            _logger.LogInformation("✅ Successfully logged batch error for {MetafieldCount} metafields to OpenSearch", batchMetafields.Count);
        }
        catch (Exception logEx)
        {
            _logger.LogError(logEx, "🚨 CRITICAL: Failed to log metafield batch errors to OpenSearch for migration {MigrationId}", migrationId);
        }
    }

    /// <summary>
    /// Handles migration-level errors with structured logging
    /// Used for top-level failures that affect the entire metafields processing operation
    /// </summary>
    private async Task HandleMigrationLevelErrorAsync(
        Exception exception, 
        List<Dictionary<string, object>> allMetafields, 
        string migrationId, 
        StoreConfiguration destinationStore,
        CancellationToken cancellationToken)
    {
        try
        {
            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = migrationId,
                EntityType = "product-metafields",
                BatchNumber = 1,
                TotalBatches = 1,
                EntityIds = allMetafields.Select(m => m.TryGetValue("id", out var id) ? id?.ToString() ?? "unknown" : "unknown").ToList(),
                SourceStore = destinationStore,
                DestinationStore = destinationStore
            };

            await _errorHandlingService.LogStructuredMigrationErrorAsync(
                exception, allMetafields, batchRequest, "migration-level", cancellationToken: cancellationToken);

            _logger.LogInformation("✅ Successfully logged migration-level error for {MetafieldCount} metafields to OpenSearch", allMetafields.Count);
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
