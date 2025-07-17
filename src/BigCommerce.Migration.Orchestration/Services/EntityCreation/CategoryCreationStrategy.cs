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

            // Return empty list to avoid causing Durable Functions replay issues
            _logger.LogWarning("🏷️ [CAT-{ExecutionId}] Returning empty result to prevent replay in migration {MigrationId}", 
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
} 