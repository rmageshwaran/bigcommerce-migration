using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating categories in destination stores
/// Implements Open/Closed Principle: specific to categories without modifying base service
/// </summary>
public class CategoryCreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<CategoryCreationStrategy> _logger;

    public CategoryCreationStrategy(IBigCommerceApiClient apiClient, ILogger<CategoryCreationStrategy> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The entity type this strategy handles
    /// </summary>
    public string EntityType => "categories";

    /// <summary>
    /// Creates categories in the destination store with hierarchical support
    /// </summary>
    /// <param name="entities">Categories to create</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (required for categories)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created categories with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No categories provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        // Validate category tree context is provided
        if (categoryTreeContext == null || string.IsNullOrWhiteSpace(categoryTreeContext.DestinationCategoryTreeId))
        {
            var errorMessage = "Destination category tree ID is required for creating categories";
            _logger.LogError("{ErrorMessage} in migration {MigrationId}", errorMessage, migrationId);
            throw new InvalidOperationException(errorMessage);
        }

        var executionId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("🏷️ [CAT-{ExecutionId}] Creating {Count} categories for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        try
        {
            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Log category details for debugging
            foreach (var category in entities)
            {
                var categoryName = category.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                var parentId = category.TryGetValue("parent_id", out var parent) ? parent?.ToString() : "null";
                
                _logger.LogDebug("🏷️ [CAT-{ExecutionId}] Creating category: Name='{CategoryName}', ParentId={ParentId} in migration {MigrationId}",
                    executionId, categoryName, parentId, migrationId);
            }

            // Create categories using the API client
            var result = await _apiClient.CreateCategoriesAsync(
                destinationStore, 
                categoryTreeContext.DestinationCategoryTreeId, 
                entities, 
                cancellationToken);

            // ✅ Check if the API returned an empty result and throw an exception to capture error details
            if (result == null || !result.Any())
            {
                var errorMessage = $"BigCommerce API returned empty response for {entities.Count} categories";
                _logger.LogError("🏷️ [CAT-{ExecutionId}] {ErrorMessage} in migration {MigrationId}", 
                    executionId, errorMessage, migrationId);
                
                // Throw an exception to trigger the error handling flow with stack trace preservation
                throw new InvalidOperationException(errorMessage);
            }

            _logger.LogInformation("✅ [CAT-{ExecutionId}] Successfully created {CreatedCount} categories for migration {MigrationId}", 
                executionId, result?.Count ?? 0, migrationId);

            return result;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "🏷️ [CAT-{ExecutionId}] ❌ Failed to create {CategoryCount} categories in migration {MigrationId}. {DetailedError}",
                executionId, entities.Count, migrationId, detailedErrorMessage);

            // ✅ Create enhanced exception with preserved API error details
            // This ensures the stack trace and response payload are preserved for error logging
            var enhancedException = new InvalidOperationException(
                $"API Error creating {entities.Count} categories: {detailedErrorMessage}", ex);
            
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
            _logger.LogWarning("🏷️ [CAT-{ExecutionId}] Re-throwing enhanced exception for proper error logging in migration {MigrationId}", 
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
    /// Extracts response payload from API exceptions
    /// </summary>
    /// <param name="exception">The exception to analyze</param>
    /// <returns>Response payload if available, otherwise null</returns>
    private static string? ExtractResponsePayloadFromException(Exception exception)
    {
        if (exception == null) return null;

        try
        {
            // Try to extract response payload from common exception types
            if (exception is HttpRequestException httpEx)
            {
                // Check if the exception has response content
                if (httpEx.Data.Contains("ResponseContent"))
                {
                    return httpEx.Data["ResponseContent"]?.ToString();
                }
            }

            // Check for inner exceptions
            var innerException = exception.InnerException;
            while (innerException != null)
            {
                if (innerException is HttpRequestException innerHttpEx)
                {
                    if (innerHttpEx.Data.Contains("ResponseContent"))
                    {
                        return innerHttpEx.Data["ResponseContent"]?.ToString();
                    }
                }
                innerException = innerException.InnerException;
            }

            // Enhanced extraction from exception message for BigCommerce API errors
            var message = exception.Message;
            
            // Look for BigCommerce API error pattern: "API request failed with status XXX: {JSON_CONTENT}"
            if (message.Contains("API request failed with status"))
            {
                var colonIndex = message.LastIndexOf(':');
                if (colonIndex > 0 && colonIndex < message.Length - 1)
                {
                    var content = message.Substring(colonIndex + 1).Trim();
                    
                    // Validate it's actually JSON
                    try
                    {
                        System.Text.Json.JsonDocument.Parse(content);
                        return content;
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // Not valid JSON, continue to other extraction methods
                    }
                }
            }

            // Try to extract from exception message if it contains JSON-like content
            if (message.Contains("{") && message.Contains("}"))
            {
                var startIndex = message.IndexOf('{');
                var endIndex = message.LastIndexOf('}');
                if (startIndex >= 0 && endIndex > startIndex)
                {
                    var jsonContent = message.Substring(startIndex, endIndex - startIndex + 1);
                    // Validate it's actually JSON
                    try
                    {
                        System.Text.Json.JsonDocument.Parse(jsonContent);
                        return jsonContent;
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // Not valid JSON, continue
                    }
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
} 