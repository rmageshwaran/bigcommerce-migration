using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Services.EntityCreation;

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
        if (categoryTreeContext == null || categoryTreeContext.CategoryTreeIdMapping == null || !categoryTreeContext.CategoryTreeIdMapping.Any())
        {
            var errorMessage = "Category tree ID mapping is required for creating categories";
            _logger.LogError("{ErrorMessage} in migration {MigrationId}", errorMessage, migrationId);
            throw new InvalidOperationException(errorMessage);
        }

        var executionId = $"{migrationId}-{entities.Count}".GetHashCode().ToString("X8");
        _logger.LogInformation("🏷️ [CAT-{ExecutionId}] ⭐ STARTING: Creating {CategoryCount} categories for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        var allCreatedCategories = new List<Dictionary<string, object>>();
        var entitiesBySourceTree = entities.GroupBy(e => e.GetValueOrDefault("_sourceTreeId")?.ToString()).ToList();

        foreach (var group in entitiesBySourceTree)
        {
            var sourceTreeId = group.Key;
            if (string.IsNullOrEmpty(sourceTreeId) || !categoryTreeContext.CategoryTreeIdMapping.TryGetValue(sourceTreeId, out var destinationTreeId))
            {
                _logger.LogWarning("Could not find destination tree for source tree {SourceTreeId}. Skipping {Count} categories.", sourceTreeId, group.Count());
                continue;
            }

            var categoriesForTree = group.ToList();
            _logger.LogInformation("Creating {Count} categories for source tree {SourceTreeId} in destination tree {DestinationTreeId}",
                categoriesForTree.Count, sourceTreeId, destinationTreeId);

            if (int.TryParse(destinationTreeId, out var treeIdInt))
            {
                foreach (var category in categoriesForTree)
                {
                    category["tree_id"] = treeIdInt;
                }
            }
            else
            {
                 _logger.LogError("🏷️ [CAT-{ExecutionId}] ❌ SAFETY-FAILED: Cannot parse destination tree ID '{TreeId}' as integer", 
                    executionId, destinationTreeId);
                throw new InvalidOperationException($"Invalid destination tree ID: {destinationTreeId}");
            }
            
            var result = await _apiClient.CreateCategoriesAsync(
                destinationStore, 
                destinationTreeId, 
                categoriesForTree, 
                cancellationToken);

            if (result != null)
            {
                allCreatedCategories.AddRange(result);
            }
        }
        
        return allCreatedCategories;
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