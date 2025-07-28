using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading;
using System.Text.Json.Serialization;

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

    public BrandCreationStrategy(IApiRequestHandler apiRequestHandler, ILogger<BrandCreationStrategy> logger)
    {
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        
        _logger.LogInformation("🏪 [BRAND-{ExecutionId}] Creating {BrandCount} brands for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        try
        {
            // Create brands individually (BigCommerce brands API doesn't support batch creation)
            var createdBrands = new List<Dictionary<string, object>>();
            
            for (int i = 0; i < entities.Count; i++)
            {
                var brand = entities[i];
                try
                {
                    var brandName = brand.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                    _logger.LogDebug("🏪 [BRAND-{ExecutionId}] Creating individual brand {Index}/{Total}: '{BrandName}' in migration {MigrationId}",
                        executionId, i + 1, entities.Count, brandName, migrationId);

                    var createdBrand = await CreateSingleBrandAsync(destinationStore, brand, cancellationToken);
                    if (createdBrand != null)
                    {
                        createdBrands.Add(createdBrand);
                    }
                }
                catch (Exception ex)
                {
                    var brandName = brand.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                    var brandId = brand.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
                    
                    _logger.LogError(ex, "🏪 [BRAND-{ExecutionId}] ❌ Failed to create individual brand '{BrandName}' (ID: {BrandId}) in migration {MigrationId}",
                        executionId, brandName, brandId, migrationId);

                    // ✅ Create enhanced exception with preserved API error details
                    // This ensures the stack trace and response payload are preserved for error logging
                    var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
                    var enhancedException = new InvalidOperationException(
                        $"API Error creating brand '{brandName}' (ID: {brandId}): {detailedErrorMessage}", ex);
                    
                    // Add the original exception as inner exception to preserve stack trace
                    // Add response payload and other details to the exception data
                    var responsePayload = ExtractResponsePayloadFromException(ex);
                    if (!string.IsNullOrEmpty(responsePayload))
                    {
                        enhancedException.Data["ResponsePayload"] = responsePayload;
                    }
                    enhancedException.Data["ApiErrorMessage"] = detailedErrorMessage;
                    enhancedException.Data["OriginalStackTrace"] = ex.StackTrace ?? string.Empty;
                    enhancedException.Data["EntityName"] = brandName;
                    enhancedException.Data["EntityId"] = brandId;
                    
                    // Re-throw the enhanced exception to trigger proper error logging
                    _logger.LogWarning("🏪 [BRAND-{ExecutionId}] Re-throwing enhanced exception for proper error logging for brand '{BrandName}' in migration {MigrationId}", 
                        executionId, brandName, migrationId);
                    
                    throw enhancedException;
                }
            }

            _logger.LogInformation("✅ [BRAND-{ExecutionId}] Successfully created {CreatedCount}/{TotalCount} brands for migration {MigrationId}", 
                executionId, createdBrands.Count, entities.Count, migrationId);

            return createdBrands;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "🏪 [BRAND-{ExecutionId}] ❌ Failed to create {BrandCount} brands in migration {MigrationId}. {DetailedError}",
                executionId, entities.Count, migrationId, detailedErrorMessage);

            // Return empty list to avoid causing Durable Functions replay issues
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
        
        // 🚨 RACE CONDITION DEBUG: Add detailed tracing
        var threadId = Thread.CurrentThread.ManagedThreadId;
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        var debugId = $"T{threadId}-{timestamp}";
        
        _logger.LogInformation("🚨 [DEBUG-{DebugId}] STARTING BRAND CREATION: Name='{BrandName}', SourceId={SourceId}, OriginalId={OriginalId}, ThreadId={ThreadId}", 
            debugId, brandName, brandId, originalId, threadId);

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

        _logger.LogInformation("🚨 [DEBUG-{DebugId}] ABOUT TO CALL API: URL={Url}, Payload={Payload}, ThreadId={ThreadId}", 
            debugId, url, jsonContent, threadId);

        // Create and execute the API request
        var request = ApiRequest.CreatePost(url, jsonContent, storeConfig);
        
        try
        {
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            
            _logger.LogInformation("🚨 [DEBUG-{DebugId}] API SUCCESS: Brand='{BrandName}', ThreadId={ThreadId}", 
                debugId, brandName, threadId);

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

            // 🚨 RACE CONDITION FIXED AT SOURCE: No 409 handling needed on clean store
            // Any 409 conflicts indicate remaining race condition issues that need fixing
            var parsedResult = ParseBrandCreationResponse(response, brandName);
            
            if (parsedResult == null)
            {
                _logger.LogError("🚨 [DEBUG-{DebugId}] PARSE FAILED: Brand='{BrandName}', ThreadId={ThreadId} - ParseBrandCreationResponse returned null", 
                    debugId, brandName, threadId);
                throw new InvalidOperationException($"Failed to parse brand creation response for '{brandName}' - no entities returned from API");
            }
            
            _logger.LogInformation("🚨 [DEBUG-{DebugId}] PARSE SUCCESS: Brand='{BrandName}', CreatedId={CreatedId}, ThreadId={ThreadId}", 
                debugId, brandName, parsedResult.GetValueOrDefault("id")?.ToString() ?? "unknown", threadId);
            
            return parsedResult;
        }
        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("409") || httpEx.Message.Contains("Conflict"))
        {
            _logger.LogError("🚨 [DEBUG-{DebugId}] API 409 CONFLICT: Brand='{BrandName}', Error={Error}, ThreadId={ThreadId}", 
                debugId, brandName, httpEx.Message, threadId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [DEBUG-{DebugId}] API UNEXPECTED ERROR: Brand='{BrandName}', ThreadId={ThreadId}", 
                debugId, brandName, threadId);
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