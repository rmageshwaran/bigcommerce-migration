using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating products in destination stores
/// Implements Open/Closed Principle: specific to products without modifying base service
/// Note: BigCommerce products API requires individual product creation, not batch operations
/// </summary>
public class ProductCreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<ProductCreationStrategy> _logger;
    private readonly IEntityErrorHandlingService _errorHandlingService;

    public ProductCreationStrategy(IBigCommerceApiClient apiClient, ILogger<ProductCreationStrategy> logger, IEntityErrorHandlingService errorHandlingService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
    }

    /// <summary>
    /// The entity type this strategy handles
    /// </summary>
    public string EntityType => "products";

    /// <summary>
    /// Creates products in the destination store
    /// </summary>
    /// <param name="entities">Products to create</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (optional for products)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created products with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No products provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        // Use deterministic execution ID for logging (Durable Functions compliance)
        var executionId = $"{migrationId}-{entities.Count}".GetHashCode().ToString("X8");
        
        _logger.LogInformation("📦 [PROD-{ExecutionId}] Creating {ProductCount} products for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        try
        {
            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Log product details for debugging
            foreach (var product in entities)
            {
                var productName = product.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                var sku = product.TryGetValue("sku", out var skuValue) ? skuValue?.ToString() : "no-sku";
                
                _logger.LogDebug("📦 [PROD-{ExecutionId}] Creating product: Name='{ProductName}', SKU='{Sku}' in migration {MigrationId}",
                    executionId, productName, sku, migrationId);
            }

            // Create products using the API client (individual creation)
            var result = await _apiClient.CreateProductsAsync(destinationStore, entities, cancellationToken);

            _logger.LogInformation("✅ [PROD-{ExecutionId}] Successfully created {CreatedCount}/{TotalCount} products for migration {MigrationId}", 
                executionId, result?.Count ?? 0, entities.Count, migrationId);

            return result;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "📦 [PROD-{ExecutionId}] ❌ Failed to create {ProductCount} products in migration {MigrationId}. {DetailedError}",
                executionId, entities.Count, migrationId, detailedErrorMessage);

            // 🚨 FIX: Log each failed product to OpenSearch before returning empty list
            // This ensures product creation errors appear in OpenSearch logs
            // By logging here and NOT re-throwing, we prevent double logging from EntityCreateService
            try
            {
                _logger.LogInformation("📦 [PROD-{ExecutionId}] 📦 OPENSEARCH LOGGING: Logging {ProductCount} product entities with actual API error details to OpenSearch", 
                    executionId, entities.Count);

                foreach (var entity in entities)
                {
                    // ✅ FIX: Use original source entity ID instead of destination ID (which doesn't exist yet)
                    var productId = entity.TryGetValue("_original_entity_id", out var originalId) ? originalId?.ToString() :
                                  entity.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
                    var productName = entity.TryGetValue("name", out var name) ? name?.ToString() : "unknown";

                    // 📦 Prepare request payload for OpenSearch
                    var requestPayload = System.Text.Json.JsonSerializer.Serialize(entity, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    var responsePayload = ExtractResponsePayloadFromException(ex);

                    // ✅ Extract simple error message for OpenSearch errorMessage field
                    var simpleErrorMessage = ExtractSimpleErrorMessage(detailedErrorMessage);
                    
                    // ✅ Create enhanced exception with actual API error details (same as brands)
                    var productFailureException = new InvalidOperationException(
                        $"API Error creating product '{productName}' (ID: {productId}): {detailedErrorMessage}", ex);
                    
                    // Add response payload and other details to the exception data (same as brands)
                    if (!string.IsNullOrEmpty(responsePayload))
                    {
                        productFailureException.Data["ResponsePayload"] = responsePayload;
                    }
                    productFailureException.Data["ApiErrorMessage"] = detailedErrorMessage;
                    productFailureException.Data["OriginalStackTrace"] = ex.StackTrace ?? string.Empty;
                    productFailureException.Data["EntityName"] = productName;
                    productFailureException.Data["EntityId"] = productId;

                    // Create a minimal batch request for logging compatibility
                    var logBatch = new BatchProcessingRequest
                    {
                        MigrationId = migrationId,
                        EntityType = "products",
                        BatchNumber = 1,
                        SourceStore = destinationStore, // Best approximation for logging
                        DestinationStore = destinationStore
                    };

                    // ✅ FIX: Check if there's an enhanced LogEntityErrorAsync that accepts request and response payloads
                    // For now, use the standard method with responsePayload and simpleErrorMessage
                    await _errorHandlingService.LogEntityErrorAsync(
                        productFailureException,
                        entity,
                        logBatch,
                        productId,
                        responsePayload ?? "No response - batch failure",
                        simpleErrorMessage,
                        cancellationToken);

                    _logger.LogDebug("📦 [PROD-{ExecutionId}] ✅ Logged product '{ProductName}' (ID: {ProductId}) error to OpenSearch", 
                        executionId, productName, productId);
                }

                _logger.LogInformation("📦 [PROD-{ExecutionId}] ✅ Successfully logged all {ProductCount} products with actual API error details to OpenSearch", 
                    executionId, entities.Count);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "📦 [PROD-{ExecutionId}] ❌ CRITICAL: Failed to log product errors to OpenSearch for migration {MigrationId}", 
                    executionId, migrationId);
                // Don't re-throw to avoid masking the original product creation failure
            }

            // Return empty list to avoid causing Durable Functions replay issues
            // 🔑 KEY: By NOT re-throwing the exception, EntityCreateService won't store error details 
            // in request.AdditionalData, preventing ProcessEntityBatchActivity from double-logging
            _logger.LogWarning("📦 [PROD-{ExecutionId}] Returning empty result to prevent replay in migration {MigrationId}", 
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
    /// <returns>Simple error message (e.g., "A duplicate product with the name: X was found") or empty string</returns>
    private static string ExtractSimpleErrorMessage(string detailedErrorMessage)
    {
        if (string.IsNullOrEmpty(detailedErrorMessage)) return string.Empty;

        try
        {
            // Look for BigCommerce API error format: {"title":"A duplicate product with the name: X was found",...}
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
                return detailedErrorMessage.Contains("product", StringComparison.OrdinalIgnoreCase) 
                    ? "Duplicate product name found" 
                    : "Duplicate entity found";
            }

            // Check for validation errors
            if (detailedErrorMessage.Contains("required", StringComparison.OrdinalIgnoreCase))
            {
                return "Required field validation failed";
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
} 