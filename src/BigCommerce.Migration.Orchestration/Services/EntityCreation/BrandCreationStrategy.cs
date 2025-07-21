using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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

        var executionId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("🏪 [BRAND-{ExecutionId}] Creating {Count} brands for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        try
        {
            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Log brand details for debugging
            foreach (var brand in entities)
            {
                var brandName = brand.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                var brandId = brand.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
                
                _logger.LogDebug("🏪 [BRAND-{ExecutionId}] Creating brand: Name='{BrandName}', ID='{BrandId}' in migration {MigrationId}",
                    executionId, brandName, brandId, migrationId);
            }

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

            // ✅ Check if we have partial failures and log appropriately
            if (createdBrands.Count == 0 && entities.Count > 0)
            {
                var errorMessage = $"BigCommerce API returned no successful brand creations for {entities.Count} brands";
                _logger.LogError("🏪 [BRAND-{ExecutionId}] {ErrorMessage} in migration {MigrationId}", 
                    executionId, errorMessage, migrationId);
                
                // Create enhanced exception to preserve error details for logging
                var enhancedException = new InvalidOperationException(errorMessage);
                enhancedException.Data["ApiErrorMessage"] = errorMessage;
                enhancedException.Data["EntityCount"] = entities.Count;
                
                throw enhancedException;
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

            // ✅ Create enhanced exception with preserved API error details
            // This ensures the stack trace and response payload are preserved for error logging
            var enhancedException = new InvalidOperationException(
                $"API Error creating {entities.Count} brands: {detailedErrorMessage}", ex);
            
            // Add the original exception as inner exception to preserve stack trace
            // Add response payload and other details to the exception data
            var responsePayload = ExtractResponsePayloadFromException(ex);
            if (!string.IsNullOrEmpty(responsePayload))
            {
                enhancedException.Data["ResponsePayload"] = responsePayload;
            }
            enhancedException.Data["ApiErrorMessage"] = detailedErrorMessage;
            enhancedException.Data["OriginalStackTrace"] = ex.StackTrace ?? string.Empty;
            
            // Re-throw the enhanced exception to trigger proper error logging
            _logger.LogWarning("🏪 [BRAND-{ExecutionId}] Re-throwing enhanced exception for proper error logging in migration {MigrationId}", 
                executionId, migrationId);
            
            throw enhancedException;
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
        // Remove fields that shouldn't be sent to the API (like source IDs)
        var cleanBrand = new Dictionary<string, object>(brand);
        cleanBrand.Remove("id"); // Remove source ID
        cleanBrand.Remove("_original_entity_id"); // Remove tracking fields

        // Ensure required fields are present and valid according to BigCommerce API
        if (!cleanBrand.ContainsKey("name") || string.IsNullOrWhiteSpace(cleanBrand["name"]?.ToString()))
        {
            throw new ArgumentException("Brand name is required for creation");
        }

        // Build the API request URL
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/brands";

        // Serialize the brand data
        var jsonContent = JsonSerializer.Serialize(cleanBrand, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        // Create and execute the API request
        var request = ApiRequest.CreatePost(url, jsonContent, storeConfig);
        var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);

        // Extract the created brand data from the response
        if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
        {
            var createdBrand = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText());
            return createdBrand;
        }

        return response; // Return the response if it's already in the expected format
    }
} 