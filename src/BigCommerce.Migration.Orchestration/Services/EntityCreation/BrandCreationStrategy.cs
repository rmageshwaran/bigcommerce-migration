using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading;
using System.Text.Json.Serialization;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.Orchestration.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating brands in destination stores
/// Implements Open/Closed Principle: specific to brands without modifying base service
/// NOTE: BigCommerce brands API only supports individual brand creation, not batch operations
/// </summary>
public class BrandCreationStrategy : IEntityCreationStrategy
{
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly ILogger<BrandCreationStrategy> _logger;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly ISubBatchConfigurationService _configService;
    private readonly ISubBatchProcessor _subBatchProcessor;

    public BrandCreationStrategy(
        IApiRequestHandler apiRequestHandler, 
        ILogger<BrandCreationStrategy> logger, 
        IEntityErrorHandlingService errorHandlingService,
        ISubBatchConfigurationService configService,
        ISubBatchProcessor subBatchProcessor)
    {
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _subBatchProcessor = subBatchProcessor ?? throw new ArgumentNullException(nameof(subBatchProcessor));
    }

    /// <summary>
    /// The entity type this strategy handles
    /// </summary>
    public string EntityType => "brands";

    /// <summary>
    /// Creates brands in the destination store
    /// </summary>
    /// <param name="entities">Brands to create</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (not used for brands)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created brands with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No brands provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        // Use deterministic execution ID for logging (Durable Functions compliance)
        var executionId = $"{migrationId}-{entities.Count}".GetHashCode().ToString("X8");
        
        _logger.LogInformation("🏪 [BRAND-{ExecutionId}] 🚀 STARTING: Creating {BrandCount} brands for migration {MigrationId}", 
            executionId, entities.Count, migrationId);
        
        _logger.LogInformation("🔍 [BRAND-{ExecutionId}] BATCH DETAILS: EntityCount={EntityCount}, MigrationId={MigrationId}, ExecutionId={ExecutionId}", 
            executionId, entities.Count, migrationId, executionId);

        try
        {
            // 🚀 SUB-BATCH PROCESSING: Use configured sub-batching for optimal rate limiting
            var config = _configService.GetConfiguration("brands");
            
            _logger.LogInformation("🔍 [BRAND-{ExecutionId}] SUB-BATCH CONFIG: SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, TotalBrands={TotalBrands}", 
                executionId, config.SubBatchSize, config.MaxConcurrency, entities.Count);
            
            _logger.LogInformation("🚀 [BRAND-{ExecutionId}] Starting SUB-BATCH creation of {BrandCount} brands " +
                                 "using sub-batches of {SubBatchSize} with max {MaxConcurrency} concurrent for migration {MigrationId}", 
                executionId, entities.Count, config.SubBatchSize, config.MaxConcurrency, migrationId);

            // Individual brand processor function
            async Task<Dictionary<string, object>?> IndividualBrandProcessor(Dictionary<string, object> brand, CancellationToken ct)
            {
                try
                {
                    var brandName = brand.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                    var brandId = brand.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
                    var originalId = brand.TryGetValue("_original_entity_id", out var origId) ? origId?.ToString() : "unknown";
                    
                    _logger.LogInformation("🔍 [BRAND-{ExecutionId}] PROCESSING: Brand='{BrandName}', SourceId={SourceId}, OriginalId={OriginalId} (SUB-BATCH) in migration {MigrationId}",
                        executionId, brandName, brandId, originalId, migrationId);

                    var createdBrand = await CreateSingleBrandAsync(destinationStore, brand, ct);
                    
                    if (createdBrand != null)
                    {
                        _logger.LogDebug("✅ [BRAND-{ExecutionId}] Successfully created brand '{BrandName}' (SUB-BATCH) in migration {MigrationId}",
                            executionId, brandName, migrationId);
                    }
                    
                    return createdBrand;
                }
                catch (Exception ex)
                {
                    var brandName = brand.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                    var brandId = brand.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
                    
                    _logger.LogError(ex, "🏪 [BRAND-{ExecutionId}] ❌ Failed to create brand '{BrandName}' (ID: {BrandId}) (SUB-BATCH) in migration {MigrationId}",
                        executionId, brandName, brandId, migrationId);

                    // Return null for failed brands - sub-batch processor will filter them out
                    return null;
                }
            }

            // Process brands using sub-batch processor
            var createdBrands = await _subBatchProcessor.ProcessIndividuallyInSubBatchesAsync(
                entities, config, IndividualBrandProcessor, "brands", migrationId, cancellationToken);
            
            var failedCount = entities.Count - createdBrands.Count;
            
            if (failedCount > 0)
            {
                _logger.LogWarning("⚠️ [BRAND-{ExecutionId}] {FailedCount}/{TotalCount} brand creations failed during sub-batch processing for migration {MigrationId}", 
                    executionId, failedCount, entities.Count, migrationId);
            }

            _logger.LogInformation("✅ [BRAND-{ExecutionId}] SUB-BATCH processing completed: {CreatedCount}/{TotalCount} brands created successfully for migration {MigrationId}", 
                executionId, createdBrands.Count, entities.Count, migrationId);

            return createdBrands;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "🏪 [BRAND-{ExecutionId}] ❌ Failed to create {BrandCount} brands in migration {MigrationId}. {DetailedError}",
                executionId, entities.Count, migrationId, detailedErrorMessage);

            // 🚨 FIX: Log each failed brand to OpenSearch before returning empty list
            // This ensures "already exists" and other brand errors appear in OpenSearch logs
            // By logging here and NOT re-throwing, we prevent double logging from EntityCreateService
            try
            {
                _logger.LogInformation("🏪 [BRAND-{ExecutionId}] 📦 OPENSEARCH LOGGING: Logging {BrandCount} brand entities with actual API error details to OpenSearch", 
                    executionId, entities.Count);

                foreach (var entity in entities)
                {
                    // ✅ FIX: Use original source entity ID instead of destination ID (which doesn't exist yet)
                    var brandId = entity.TryGetValue("_original_entity_id", out var originalId) ? originalId?.ToString() :
                                  entity.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
                    var brandName = entity.TryGetValue("name", out var name) ? name?.ToString() : "unknown";

                    // 📦 Prepare request payload for OpenSearch
                    var requestPayload = System.Text.Json.JsonSerializer.Serialize(entity, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    var responsePayload = ExtractResponsePayloadFromException(ex);

                    // ✅ Extract simple error message for OpenSearch errorMessage field
                    var simpleErrorMessage = ExtractSimpleErrorMessage(detailedErrorMessage);
                    
                    // ✅ Create enhanced exception with actual API error details (same as categories)
                    var brandFailureException = new InvalidOperationException(
                        $"API Error creating brand '{brandName}' (ID: {brandId}): {detailedErrorMessage}", ex);
                    
                    // Add response payload and other details to the exception data (same as categories)
                    if (!string.IsNullOrEmpty(responsePayload))
                    {
                        brandFailureException.Data["ResponsePayload"] = responsePayload;
                    }
                    brandFailureException.Data["ApiErrorMessage"] = detailedErrorMessage;
                    brandFailureException.Data["OriginalStackTrace"] = ex.StackTrace ?? string.Empty;
                    brandFailureException.Data["EntityName"] = brandName;
                    brandFailureException.Data["EntityId"] = brandId;

                    // Create a minimal batch request for logging compatibility
                    var logBatch = new Models.BatchProcessingRequest
                    {
                        MigrationId = migrationId,
                        EntityType = "brands",
                        BatchNumber = 1,
                        SourceStore = destinationStore, // Best approximation for logging
                        DestinationStore = destinationStore
                    };

                    // ✅ FIX: Check if there's an enhanced LogEntityErrorAsync that accepts request and response payloads
                    // For now, use the standard method with responsePayload and simpleErrorMessage
                    await _errorHandlingService.LogEntityErrorAsync(
                        brandFailureException,
                        entity,
                        logBatch,
                        brandId,
                        responsePayload ?? "No response - batch failure",
                        simpleErrorMessage,
                        cancellationToken);

                    _logger.LogDebug("🏪 [BRAND-{ExecutionId}] ✅ Logged brand '{BrandName}' (ID: {BrandId}) error to OpenSearch", 
                        executionId, brandName, brandId);
                }

                _logger.LogInformation("🏪 [BRAND-{ExecutionId}] ✅ Successfully logged all {BrandCount} brands with actual API error details to OpenSearch", 
                    executionId, entities.Count);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "🏪 [BRAND-{ExecutionId}] ❌ CRITICAL: Failed to log brand errors to OpenSearch for migration {MigrationId}", 
                    executionId, migrationId);
                // Don't re-throw to avoid masking the original brand creation failure
            }

            // Return empty list to avoid causing Durable Functions replay issues
            // 🔑 KEY: By NOT re-throwing the exception, EntityCreateService won't store error details 
            // in request.AdditionalData, preventing ProcessEntityBatchActivity from double-logging
            _logger.LogWarning("🏪 [BRAND-{ExecutionId}] Returning empty result to prevent replay in migration {MigrationId}", 
                executionId, migrationId);
            
            return new List<Dictionary<string, object>>();
        }
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

    /// <summary>
    /// Extracts detailed error information from API exceptions
    /// </summary>
    /// <param name="exception">The exception to analyze</param>
    /// <returns>Detailed error message if available, otherwise empty string</returns>
    private static string ExtractDetailedErrorMessage(Exception exception)
    {
        if (exception == null) return string.Empty;

        // Check for inner exceptions with detailed error messages
        var innerException = exception.InnerException;
        while (innerException != null)
        {
            if (!string.IsNullOrEmpty(innerException.Message) && 
                innerException.Message != exception.Message)
            {
                return innerException.Message;
            }
            innerException = innerException.InnerException;
        }

        return exception.Message;
    }

    /// <summary>
    /// Extracts response payload from API exceptions for error logging
    /// </summary>
    /// <param name="exception">The exception to analyze</param>
    /// <returns>Response payload if available, otherwise empty string</returns>
    private static string ExtractResponsePayloadFromException(Exception exception)
    {
        if (exception == null) return string.Empty;

        // Check exception data for response payload
        if (exception.Data.Contains("ResponsePayload") && exception.Data["ResponsePayload"] is string payload)
        {
            return payload;
        }

        // Check inner exceptions for response payload
        var innerException = exception.InnerException;
        while (innerException != null)
        {
            if (innerException.Data.Contains("ResponsePayload") && innerException.Data["ResponsePayload"] is string innerPayload)
            {
                return innerPayload;
            }
            innerException = innerException.InnerException;
        }

        return string.Empty;
    }

    /// <summary>
    /// Extracts simple error message from API error response for OpenSearch errorMessage field
    /// </summary>
    /// <param name="detailedErrorMessage">The detailed error message containing API response</param>
    /// <returns>Simple error message (e.g., "A duplicate brand with the name: X was found") or empty string</returns>
    private static string ExtractSimpleErrorMessage(string detailedErrorMessage)
    {
        if (string.IsNullOrEmpty(detailedErrorMessage)) return string.Empty;

        try
        {
            // Look for BigCommerce API error format: {"title":"A duplicate brand with the name: X was found",...}
            var titleStart = detailedErrorMessage.IndexOf("\"title\":\"", StringComparison.OrdinalIgnoreCase);
            if (titleStart >= 0)
            {
                titleStart += "\"title\":\"".Length;
                var titleEnd = detailedErrorMessage.IndexOf("\"", titleStart);
                if (titleEnd > titleStart)
                {
                    return detailedErrorMessage.Substring(titleStart, titleEnd - titleStart);
                }
            }

            // Fallback: Look for common error patterns
            if (detailedErrorMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                return detailedErrorMessage.Contains("brand", StringComparison.OrdinalIgnoreCase) 
                    ? "Duplicate brand name found" 
                    : "Duplicate entity found";
            }

            // If no specific pattern found, return first sentence or up to 100 chars
            var firstSentence = detailedErrorMessage.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(firstSentence) 
                ? (detailedErrorMessage.Length > 100 ? detailedErrorMessage.Substring(0, 100) + "..." : detailedErrorMessage)
                : firstSentence;
        }
        catch
        {
            // If parsing fails, return truncated version
            return detailedErrorMessage.Length > 100 ? detailedErrorMessage.Substring(0, 100) + "..." : detailedErrorMessage;
        }
    }

    /// <summary>
    /// Creates a single brand using the BigCommerce API
    /// </summary>
    /// <param name="storeConfig">Destination store configuration</param>
    /// <param name="brand">Brand data to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created brand with destination ID, or null if creation failed</returns>
    private async Task<Dictionary<string, object>?> CreateSingleBrandAsync(
        StoreConfiguration storeConfig,
        Dictionary<string, object> brand,
        CancellationToken cancellationToken)
    {
                        var brandName = brand.GetValueOrDefault("name")?.ToString() ?? "unknown";
                var brandId = brand.GetValueOrDefault("id")?.ToString() ?? "unknown";
                var originalId = brand.GetValueOrDefault("_original_entity_id")?.ToString() ?? "unknown";
                var chunkNumber = brand.GetValueOrDefault("_chunk_number")?.ToString() ?? "unknown";
                var apiPage = brand.GetValueOrDefault("_api_page")?.ToString() ?? "unknown";
                var migrationId = brand.GetValueOrDefault("_migration_id")?.ToString() ?? "unknown";
                
                // 🚨 ENHANCED DUPLICATE DETECTION: Add comprehensive tracing and duplicate checking
                var threadId = Thread.CurrentThread.ManagedThreadId;
                var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
                var debugId = $"T{threadId}-{timestamp}";
        
        _logger.LogInformation("🚨 [BRAND-CREATE-{DebugId}] 🚀 STARTING CREATION: Name='{BrandName}', SourceId={SourceId}, " +
                              "OriginalId={OriginalId}, CHUNK={ChunkNumber}, API_PAGE={ApiPage}, MigrationId={MigrationId}, " +
                              "ThreadId={ThreadId}, Timestamp={Timestamp}", 
            debugId, brandName, brandId, originalId, chunkNumber, apiPage, migrationId, threadId, timestamp);
        
        _logger.LogInformation("🔍 [BRAND-CREATE-{DebugId}] API PREPARATION: Will call BigCommerce API to create brand '{BrandName}' on thread {ThreadId}", 
            debugId, brandName, threadId);

        // 🚀 PERFORMANCE OPTIMIZATION: Skip duplicate checks for clean destination stores
        // Only rely on 409 conflict handling if actual duplicates exist

        // Remove fields that shouldn't be sent to the API (like source IDs)
        var cleanBrand = new Dictionary<string, object>(brand);
        cleanBrand.Remove("id"); // Remove source ID
        cleanBrand.Remove("_original_entity_id"); // Remove tracking fields

        // Ensure required fields are present and valid according to BigCommerce API
        if (!cleanBrand.ContainsKey("name") || string.IsNullOrWhiteSpace(cleanBrand["name"]?.ToString()))
        {
            throw new ArgumentException("Brand name is required for creation");
        }

        var cleanedName = cleanBrand.GetValueOrDefault("name")?.ToString() ?? "unknown";
        
        _logger.LogInformation("🚨 [DEBUG-{DebugId}] AFTER TRANSFORM: CleanedName='{CleanedName}', ThreadId={ThreadId}", 
            debugId, cleanedName, threadId);

        // Build the API request URL
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/brands";

        // Serialize the brand data
        var jsonContent = JsonSerializer.Serialize(cleanBrand, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        _logger.LogInformation("🚨 [BRAND-CREATE-{DebugId}] 🌐 API CALL: URL={Url}, Payload={Payload}, ThreadId={ThreadId}", 
            debugId, url, jsonContent, threadId);
        
        _logger.LogInformation("🔍 [BRAND-CREATE-{DebugId}] REQUEST DETAILS: Method=POST, ContentType=application/json, PayloadSize={PayloadSize} bytes", 
            debugId, jsonContent.Length);

        // Create and execute the API request
        var request = ApiRequest.CreatePost(url, jsonContent, storeConfig);
        
        try
        {
            var apiStartTime = DateTime.UtcNow;
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            var apiDuration = DateTime.UtcNow - apiStartTime;
            
                                _logger.LogInformation("🚨 [BRAND-CREATE-{DebugId}] ✅ API SUCCESS: Brand='{BrandName}', CHUNK={ChunkNumber}, " +
                                          "API_PAGE={ApiPage}, Duration={Duration}ms, ThreadId={ThreadId}", 
                        debugId, brandName, chunkNumber, apiPage, apiDuration.TotalMilliseconds, threadId);
            
            _logger.LogInformation("🔍 [BRAND-CREATE-{DebugId}] TIMING: API call completed in {Duration}ms for brand '{BrandName}' on thread {ThreadId}", 
                debugId, apiDuration.TotalMilliseconds, brandName, threadId);

            // 🚨 ENHANCED DEBUG: Log the actual API response for debugging
            if (response != null)
            {
                var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false });
                var responsePreview = responseJson.Length > 500 ? responseJson.Substring(0, 500) + "..." : responseJson;
                _logger.LogInformation("🚨 [DEBUG-{DebugId}] RAW API RESPONSE: Brand='{BrandName}', Response={ResponsePreview}, ThreadId={ThreadId}", 
                    debugId, brandName, responsePreview, threadId);
            }
            else
            {
                _logger.LogWarning("🚨 [DEBUG-{DebugId}] NULL API RESPONSE: Brand='{BrandName}', ThreadId={ThreadId}", 
                    debugId, brandName, threadId);
            }

            // 🚀 PERFORMANCE: Direct brand creation - 409 conflicts indicate actual duplicates
            var parsedResult = ParseBrandCreationResponse(response, brandName);
            
            if (parsedResult == null)
            {
                _logger.LogError("🚨 [DEBUG-{DebugId}] PARSE FAILED: Brand='{BrandName}', ThreadId={ThreadId} - ParseBrandCreationResponse returned null", 
                    debugId, brandName, threadId);
                throw new InvalidOperationException($"Failed to parse brand creation response for '{brandName}' - no entities returned from API");
            }
            
            var createdId = parsedResult.GetValueOrDefault("id")?.ToString() ?? "unknown";
            _logger.LogInformation("🚨 [DEBUG-{DebugId}] ✅ PARSE SUCCESS: Brand='{BrandName}', CreatedId={CreatedId}, ThreadId={ThreadId}", 
                debugId, brandName, createdId, threadId);
            
            _logger.LogInformation("🔍 [BRAND-CREATE-{DebugId}] FINAL RESULT: Successfully created brand '{BrandName}' with destination ID={CreatedId}", 
                debugId, brandName, createdId);
            
            return parsedResult;
        }
                        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("409") || httpEx.Message.Contains("Conflict"))
                {
                    _logger.LogInformation("✅ [BRAND-CREATE-{DebugId}] ⏭️ DUPLICATE SKIPPED: Brand='{BrandName}' already exists in destination store. " +
                                         "CHUNK={ChunkNumber}, API_PAGE={ApiPage}, MigrationId={MigrationId}. " +
                                         "This is normal when source data contains duplicate brand names with different IDs. " +
                                         "ThreadId={ThreadId}", 
                        debugId, brandName, chunkNumber, apiPage, migrationId, threadId);
            
            // Return null for existing brands to continue processing other brands
            _logger.LogDebug("✅ [BRAND-CREATE-{DebugId}] ⏭️ Successfully skipped duplicate brand '{BrandName}' - continuing with batch", 
                debugId, brandName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [BRAND-CREATE-{DebugId}] ❌ API UNEXPECTED ERROR: Brand='{BrandName}', " +
                           "ThreadId={ThreadId}, RequestPayload={Payload}", 
                debugId, brandName, threadId, jsonContent);
            throw;
        }
    }

    /// <summary>
    /// Parses the BigCommerce API response for brand creation
    /// </summary>
    private Dictionary<string, object>? ParseBrandCreationResponse(Dictionary<string, object>? response, string brandName)
    {
        if (response == null)
        {
            return null;
        }

        // Try to extract the created brand data from various possible response formats
        Dictionary<string, object>? createdBrand = null;

        // Format 1: Response wrapped in "data" field (V3 API pattern)
        if (response.TryGetValue("data", out var dataValue))
        {
            if (dataValue is JsonElement dataElement)
            {
                try
                {
                    createdBrand = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText());
                }
                catch (JsonException)
                {
                    // If JSON deserialization fails, try direct cast
                    createdBrand = dataValue as Dictionary<string, object>;
                }
            }
            else if (dataValue is Dictionary<string, object> dataDict)
            {
                createdBrand = dataDict;
            }
        }
        
        // Format 2: Response is the brand data directly (no wrapper)
        if (createdBrand == null && response.ContainsKey("id"))
        {
            createdBrand = response;
        }

        // Format 3: Response is wrapped in another format - extract any object with an ID
        if (createdBrand == null)
        {
            foreach (var kvp in response)
            {
                if (kvp.Value is Dictionary<string, object> nestedDict && nestedDict.ContainsKey("id"))
                {
                    createdBrand = nestedDict;
                    break;
                }
                if (kvp.Value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText());
                        if (parsed?.ContainsKey("id") == true)
                        {
                            createdBrand = parsed;
                            break;
                        }
                    }
                    catch (JsonException)
                    {
                        // Continue trying other formats
                    }
                }
            }
        }

        // 🚨 ENHANCED LOGGING: Log the response format for debugging
        if (createdBrand != null)
        {
            var createdId = createdBrand.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
            _logger.LogDebug("✅ Successfully parsed API response for brand '{BrandName}' - Created ID: {CreatedId}", 
                brandName, createdId);
        }
        else
        {
            _logger.LogWarning("⚠️ Failed to parse API response for brand '{BrandName}'. Response keys: {ResponseKeys}", 
                brandName, string.Join(", ", response.Keys));
            
            // Log first 200 chars of response for debugging
            var responseJson = JsonSerializer.Serialize(response);
            var preview = responseJson.Length > 200 ? responseJson.Substring(0, 200) + "..." : responseJson;
            _logger.LogDebug("⚠️ Response preview for brand '{BrandName}': {ResponsePreview}", brandName, preview);
        }

        return createdBrand;
    }
} 