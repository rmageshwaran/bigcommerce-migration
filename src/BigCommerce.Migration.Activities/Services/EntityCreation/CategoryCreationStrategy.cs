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

        var result = await _apiClient.CreateCategoriesAsync(
                destinationStore,
                entities,
                cancellationToken);

        return result;
    }
} 
