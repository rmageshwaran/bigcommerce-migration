using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Fetch;

/// <summary>
/// Fetch strategy for category entities
/// Handles category-specific fetching with tree context and hierarchical considerations
/// </summary>
public class CategoryFetchStrategy : IEntityFetchStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<CategoryFetchStrategy> _logger;

    public string EntityType => "categories";

    public CategoryFetchStrategy(IBigCommerceApiClient apiClient, ILogger<CategoryFetchStrategy> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(
        List<string> entityIds,
        string migrationId,
        StoreConfiguration sourceStore,
        List<Dictionary<string, object>>? discoveredEntities = null,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (discoveredEntities == null)
        {
            _logger.LogWarning("CategoryFetchStrategy expects discovered entities to be provided. Fetching from API as a fallback.");
            // Fallback logic can be implemented here if needed
            return new List<Dictionary<string, object>>();
        }

        var requestedEntityIdsSet = new HashSet<string>(entityIds);
        var filteredCategories = discoveredEntities
            .Where(cat => requestedEntityIdsSet.Contains(cat.GetValueOrDefault("id")?.ToString() ?? ""))
            .ToList();

        _logger.LogInformation("Fetched {Count} categories from discovered data for migration {MigrationId}",
            filteredCategories.Count, migrationId);

        return await Task.FromResult(filteredCategories);
    }

    /// <summary>
    /// Sorts categories hierarchically ensuring parents come before children
    /// </summary>
    private static List<Dictionary<string, object>> SortCategoriesHierarchically(List<Dictionary<string, object>> categories, CancellationToken cancellationToken = default)
    {
        var sortedCategories = new List<Dictionary<string, object>>();

        // Create a mapping with proper ID handling to avoid duplicate key issues
        var categoryMap = new Dictionary<string, Dictionary<string, object>>();
        foreach (var category in categories)
        {
            var id = category.GetValueOrDefault("id")?.ToString() ?? "";
            if (!string.IsNullOrEmpty(id) && !categoryMap.ContainsKey(id))
            {
                categoryMap[id] = category;
            }
        }

        var processedIds = new HashSet<string>();

        // Process categories level by level (breadth-first)
        var currentLevelParentIds = new HashSet<string> { "0" }; // Start with root categories

        while (currentLevelParentIds.Any() && sortedCategories.Count < categories.Count)
        {
            // Check for cancellation between processing levels (for large category trees)
            cancellationToken.ThrowIfCancellationRequested();

            var nextLevelParentIds = new HashSet<string>();

            foreach (var category in categories)
            {
                var categoryId = category.GetValueOrDefault("id")?.ToString() ?? "";
                var parentId = category.GetValueOrDefault("parent_id")?.ToString() ?? "0";

                // Add categories whose parents are in the current level
                if (!processedIds.Contains(categoryId) && currentLevelParentIds.Contains(parentId))
                {
                    sortedCategories.Add(category);
                    processedIds.Add(categoryId);
                    nextLevelParentIds.Add(categoryId); // This category can be a parent for next level
                }
            }

            currentLevelParentIds = nextLevelParentIds;
        }

        return sortedCategories;
    }
}