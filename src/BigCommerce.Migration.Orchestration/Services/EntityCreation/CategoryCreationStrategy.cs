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

        // Use deterministic execution ID for logging (Durable Functions compliance)
        var executionId = $"{migrationId}-{entities.Count}".GetHashCode().ToString("X8");
        
        _logger.LogInformation("🏷️ [CAT-{ExecutionId}] ⭐ STARTING: Creating {CategoryCount} categories for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        // Log detailed category information
        for (int i = 0; i < entities.Count; i++)
        {
            var category = entities[i];
            var categoryName = category.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
            var categoryId = category.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
            var parentId = category.TryGetValue("parent_id", out var parent) ? parent?.ToString() : "null";
            var treeId = category.TryGetValue("tree_id", out var tree) ? tree?.ToString() : "null";
            
            _logger.LogDebug("🏷️ [CAT-{ExecutionId}] INPUT[{Index}]: ID={CategoryId}, Name='{CategoryName}', ParentId={ParentId}, TreeId={TreeId}", 
                executionId, i, categoryId, categoryName, parentId, treeId);
        }

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

            // 🔧 SAFETY NET: Ensure tree_id is set on all entities before API call
            _logger.LogInformation("🏷️ [CAT-{ExecutionId}] 🔧 SAFETY-CHECK: Ensuring tree_id is set on all {CategoryCount} categories", 
                executionId, entities.Count);
            
            var destinationTreeId = categoryTreeContext.DestinationCategoryTreeId;
            if (int.TryParse(destinationTreeId, out var treeIdInt))
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    var category = entities[i];
                    var categoryName = category.GetValueOrDefault("name")?.ToString() ?? "unknown";
                    var existingTreeId = category.GetValueOrDefault("tree_id");
                    
                    // Force set tree_id to ensure it's present
                    category["tree_id"] = treeIdInt;
                    
                    _logger.LogDebug("🏷️ [CAT-{ExecutionId}] SAFETY[{Index}]: '{CategoryName}' - tree_id forced to {TreeId} (was: {ExistingTreeId})", 
                        executionId, i, categoryName, treeIdInt, existingTreeId ?? "NULL");
                }
                
                _logger.LogInformation("🏷️ [CAT-{ExecutionId}] ✅ SAFETY-COMPLETE: All categories have tree_id={TreeId}", 
                    executionId, treeIdInt);
            }
            else
            {
                _logger.LogError("🏷️ [CAT-{ExecutionId}] ❌ SAFETY-FAILED: Cannot parse destination tree ID '{TreeId}' as integer", 
                    executionId, destinationTreeId);
                throw new InvalidOperationException($"Invalid destination tree ID: {destinationTreeId}");
            }

            // Create categories using the API client
            _logger.LogInformation("🏷️ [CAT-{ExecutionId}] 🚀 CALLING API: Creating {CategoryCount} categories via BigCommerce API", 
                executionId, entities.Count);
            
            var result = await _apiClient.CreateCategoriesAsync(
                destinationStore, 
                categoryTreeContext.DestinationCategoryTreeId, 
                entities, 
                cancellationToken);

            _logger.LogInformation("🏷️ [CAT-{ExecutionId}] 📨 API RESPONSE: Received response with {ResultCount} entities", 
                executionId, result?.Count ?? 0);

            // ✅ Handle partial success scenarios (207 responses)
            var inputCount = entities.Count;
            var createdCount = result?.Count ?? 0;
            
            // Log detailed response information with full entity structure debugging
            if (result != null && result.Any())
            {
                for (int i = 0; i < Math.Min(3, result.Count); i++) // Log first 3 entities for debugging
                {
                    var created = result[i];
                    var createdId = created.GetValueOrDefault("category_id")?.ToString() ?? "unknown"; // 🔧 FIX: Use category_id
                    var createdName = created.GetValueOrDefault("name")?.ToString() ?? "";
                    var createdParentId = created.GetValueOrDefault("parent_id")?.ToString() ?? "0";
                    
                    _logger.LogInformation("🏷️ [CAT-{ExecutionId}] OUTPUT[{Index}]: ID={CreatedId}, Name='{CreatedName}', ParentId={CreatedParentId}", 
                        executionId, i, createdId, createdName, createdParentId);
                    
                    // 🔍 ENTITY STRUCTURE DEBUG: Log all keys in the created entity
                    var entityKeys = string.Join(", ", created.Keys);
                    _logger.LogInformation("🔍 [ENTITY-STRUCTURE] Entity {Index} keys: {Keys}", i, entityKeys);
                    
                    // 🔍 FULL ENTITY DEBUG: Log the complete entity structure (first entity only)
                    if (i == 0)
                    {
                        var entityJson = System.Text.Json.JsonSerializer.Serialize(created);
                        _logger.LogInformation("🔍 [FULL-ENTITY] Complete entity structure: {EntityJson}", entityJson);
                    }
                }
            }
            
            if (result == null || createdCount == 0)
            {
                var errorMessage = $"BigCommerce API returned empty response for {inputCount} categories";
                _logger.LogError("🏷️ [CAT-{ExecutionId}] ❌ TOTAL FAILURE: {ErrorMessage} in migration {MigrationId}", 
                    executionId, errorMessage, migrationId);
                
                // Throw an exception to trigger the error handling flow with stack trace preservation
                throw new InvalidOperationException(errorMessage);
            }

            // 🚨 CRITICAL FIX: Detect partial success scenarios
            if (createdCount < inputCount)
            {
                var failedCount = inputCount - createdCount;
                _logger.LogWarning("🏷️ [CAT-{ExecutionId}] ⚠️ PARTIAL SUCCESS: Created {CreatedCount}/{InputCount} categories in migration {MigrationId}. {FailedCount} categories failed!",
                    executionId, createdCount, inputCount, migrationId, failedCount);
                
                // Log which categories were created vs failed for debugging
                var createdIds = result.Select(r => r.GetValueOrDefault("category_id")?.ToString() ?? "unknown").ToList(); // 🔧 FIX: Use category_id
                var inputIds = entities.Select(e => e.GetValueOrDefault("category_id")?.ToString() ?? "unknown").ToList(); // 🔧 FIX: Use category_id
                var failedIds = inputIds.Except(createdIds).ToList();
                
                _logger.LogWarning("🏷️ [CAT-{ExecutionId}] 📊 FAILED IDs: {FailedIds}", executionId, string.Join(", ", failedIds));
                _logger.LogInformation("🏷️ [CAT-{ExecutionId}] ✅ SUCCESS IDs: {SuccessIds}", executionId, string.Join(", ", createdIds));
                
                // Still return the partial results, but caller needs to handle the count discrepancy
            }
            else
            {
                _logger.LogInformation("🏷️ [CAT-{ExecutionId}] 🎉 FULL SUCCESS: All {CreatedCount} categories created successfully", 
                    executionId, createdCount);
            }

            _logger.LogInformation("🏷️ [CAT-{ExecutionId}] ✅ COMPLETED: Successfully created {CreatedCount}/{InputCount} categories for migration {MigrationId}", 
                executionId, createdCount, inputCount, migrationId);

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