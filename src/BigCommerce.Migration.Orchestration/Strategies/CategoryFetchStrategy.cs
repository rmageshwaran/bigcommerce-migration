using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

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
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        // LSP COMPLIANCE: Consistent parameter validation across all strategies
        if (entityIds == null) throw new ArgumentNullException(nameof(entityIds));
        if (string.IsNullOrWhiteSpace(migrationId)) throw new ArgumentNullException(nameof(migrationId));
        if (sourceStore == null) throw new ArgumentNullException(nameof(sourceStore));
        
        // Handle cancellation first
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Fetching {Count} specific categories for migration {MigrationId}", 
            entityIds.Count, migrationId);
        
        // Log requested entity IDs for debugging
        _logger.LogDebug("Requested category IDs: [{EntityIds}] for migration {MigrationId}",
            string.Join(", ", entityIds), migrationId);

        // Determine category tree ID for fetching
        var categoryTreeId = categoryTreeContext?.SourceCategoryTreeId ?? "1"; // Default to tree 1
        
        // Handle empty entity IDs case
        if (entityIds.Count == 0)
        {
            _logger.LogInformation("No category IDs provided, returning empty list for migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }
        
        _logger.LogDebug("Using category tree ID {CategoryTreeId} for fetching categories", categoryTreeId);

        // Get all categories from the tree to ensure proper hierarchical sorting
        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = 250, // Large limit to get all categories in one call
            CategoryTreeId = categoryTreeId,
            SortBy = "id",
            SortDirection = "asc"
        };

        var response = await _apiClient.GetPaginatedEntitiesAsync(
            sourceStore,
            "categories",
            paginationRequest,
            cancellationToken);

        var allCategories = response.Data ?? new List<Dictionary<string, object>>();

        _logger.LogDebug("API returned {TotalCount} categories from tree {TreeId}",
            allCategories.Count, categoryTreeId);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var cat in allCategories)
            {
                var id = cat.GetValueOrDefault("id")?.ToString() ?? "unknown";
                var name = cat.GetValueOrDefault("name")?.ToString() ?? "unknown";
                var parentId = cat.GetValueOrDefault("parent_id");
                _logger.LogDebug("- Category ID: {Id}, Name: '{Name}', ParentId: {ParentId}",
                    id, name, parentId);
            }
        }

        // ✅ HIERARCHICAL SORTING: Sort categories so parents come before children
        var sortedCategories = SortCategoriesHierarchically(allCategories);
        
        // Filter to only the requested categories while preserving hierarchical order
        var requestedEntityIdsSet = new HashSet<string>(entityIds);
        var filteredCategories = sortedCategories
            .Where(cat => requestedEntityIdsSet.Contains(cat.GetValueOrDefault("id")?.ToString() ?? ""))
            .ToList();

        _logger.LogDebug("After hierarchical sorting and filtering, found {Count} matching categories:",
            filteredCategories.Count);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var cat in filteredCategories)
            {
                var id = cat.GetValueOrDefault("id")?.ToString() ?? "unknown";
                var name = cat.GetValueOrDefault("name")?.ToString() ?? "unknown";
                _logger.LogDebug("- Sorted Category ID: {Id}, Name: '{Name}'", id, name);
            }
        }

        // Add original entity ID tracking for error reporting
        foreach (var category in filteredCategories)
        {
            var categoryId = category.GetValueOrDefault("id")?.ToString() ?? "";
            category["_original_entity_id"] = categoryId;
        }

        _logger.LogInformation("Found {FoundCount}/{RequestedCount} categories from {TotalCount} total categories", 
            filteredCategories.Count, entityIds.Count, allCategories.Count);

        _logger.LogInformation("Successfully fetched {Count} of {Total} categories for migration {MigrationId}", 
            filteredCategories.Count, entityIds.Count, migrationId);

        return filteredCategories;
    }

    /// <summary>
    /// Sorts categories hierarchically ensuring parents come before children
    /// </summary>
    private static List<Dictionary<string, object>> SortCategoriesHierarchically(List<Dictionary<string, object>> categories)
    {
        var sortedCategories = new List<Dictionary<string, object>>();
        var categoryMap = categories.ToDictionary(
            cat => cat.GetValueOrDefault("id")?.ToString() ?? "",
            cat => cat
        );
        var processedIds = new HashSet<string>();

        // Process categories level by level (breadth-first)
        var currentLevelParentIds = new HashSet<string> { "0" }; // Start with root categories

        while (currentLevelParentIds.Any() && sortedCategories.Count < categories.Count)
        {
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