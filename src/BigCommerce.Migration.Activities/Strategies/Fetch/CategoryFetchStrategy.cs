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
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entityIds == null || string.IsNullOrWhiteSpace(migrationId) || sourceStore == null)
        {
            _logger.LogWarning("Invalid fetch strategy input: entityIds, migrationId, or sourceStore is null/empty. Returning empty list.");
            return new List<Dictionary<string, object>>();
        }
        if (entityIds.Count == 0)
        {
            return new List<Dictionary<string, object>>();
        }

        _logger.LogInformation("Fetching {Count} specific categories for migration {MigrationId}",
            entityIds.Count, migrationId);

        // Log requested entity IDs for debugging
        _logger.LogDebug("Requested category IDs: [{EntityIds}] for migration {MigrationId}",
            string.Join(", ", entityIds), migrationId);

        // Determine category tree ID for fetching
        var categoryTreeId = categoryTreeContext?.SourceCategoryTreeId ?? "1"; // Default to tree 1

        _logger.LogDebug("Using category tree ID {CategoryTreeId} for fetching categories", categoryTreeId);

        // ✅ FIX: Implement proper pagination to get ALL categories from the tree
        var allCategories = new List<Dictionary<string, object>>();
        var currentPage = 1;

        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var paginationRequest = new BigCommercePaginationRequest
                {
                    Page = currentPage,
                    Limit = 250, // Keep large limit for efficiency
                    CategoryTreeId = categoryTreeId,
                    SortBy = "id",
                    SortDirection = "asc"
                };

                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    sourceStore,
                    "categories",
                    paginationRequest,
                    cancellationToken);

                var pageCategories = response.Data ?? new List<Dictionary<string, object>>();

                if (pageCategories.Any())
                {
                    allCategories.AddRange(pageCategories);
                    _logger.LogDebug("Fetched {PageCount} categories from page {Page} (total so far: {Total})",
                        pageCategories.Count, currentPage, allCategories.Count);
                }

                // ✅ ROBUST PAGINATION: Use multiple exit conditions for reliability
                // 1. No more data returned
                if (!pageCategories.Any())
                {
                    _logger.LogDebug("No more categories returned from page {Page}, ending pagination", currentPage);
                    break;
                }

                // 2. API metadata indicates last page (if available)
                if (response.TotalPages.HasValue && currentPage >= response.TotalPages.Value)
                {
                    _logger.LogDebug("Reached API-indicated last page {TotalPages}", response.TotalPages.Value);
                    break;
                }

                // 3. Returned fewer items than limit (indicates last page)
                if (pageCategories.Count < paginationRequest.Limit)
                {
                    _logger.LogDebug("Page {Page} returned {Count} items (less than limit {Limit}), ending pagination",
                        currentPage, pageCategories.Count, paginationRequest.Limit);
                    break;
                }

                currentPage++;

                // 4. Safety check - prevent infinite loops
                if (currentPage > 50)
                {
                    _logger.LogWarning("Breaking pagination loop after 50 pages for categories to prevent infinite loop");
                    break;
                }

            } while (true);

            _logger.LogInformation("✅ Pagination completed: Retrieved {TotalCount} categories from {Pages} pages for tree {TreeId}",
                allCategories.Count, currentPage, categoryTreeId);
        }
        catch (Exception ex)
        {
            // ✅ P0-T2: Enhanced API error logging with request/response payload logging
            _logger.LogError(ex, "🔥 [CATEGORY-FETCH-API-ERROR] Failed to fetch categories for migration {MigrationId}. " +
                            "Store: {StoreId}, CategoryTreeId: {CategoryTreeId}, CurrentPage: {CurrentPage}, " +
                            "RequestedCategoryIds: {CategoryCount}, RequestedIds: [{CategoryIds}], " +
                            "ErrorType: {ErrorType}, Category: Error",
                migrationId, sourceStore.StoreId, categoryTreeId, currentPage,
                entityIds.Count, string.Join(",", entityIds), ex.GetType().Name);

            // Return empty list to allow migration to continue with other entity types
            return new List<Dictionary<string, object>>();
        }

        // ✅ DEBUG: Log all retrieved categories for debugging
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Retrieved categories from API:");
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
        var sortedCategories = SortCategoriesHierarchically(allCategories, cancellationToken);

        _logger.LogDebug("Hierarchical sorting completed: {SortedCount} categories sorted from {OriginalCount} total",
            sortedCategories.Count, allCategories.Count);

        // ✅ VALIDATION: Check if any categories were dropped during sorting
        if (sortedCategories.Count != allCategories.Count)
        {
            var droppedCount = allCategories.Count - sortedCategories.Count;
            _logger.LogWarning("⚠️ Hierarchical sorting dropped {DroppedCount} categories! Original: {OriginalCount}, Sorted: {SortedCount}",
                droppedCount, allCategories.Count, sortedCategories.Count);

            // Find which categories were dropped
            var sortedIds = new HashSet<string>(sortedCategories.Select(c => c.GetValueOrDefault("id")?.ToString() ?? ""));
            var droppedCategories = allCategories.Where(c => !sortedIds.Contains(c.GetValueOrDefault("id")?.ToString() ?? ""));

            foreach (var dropped in droppedCategories)
            {
                var id = dropped.GetValueOrDefault("id")?.ToString() ?? "unknown";
                var name = dropped.GetValueOrDefault("name")?.ToString() ?? "unknown";
                var parentId = dropped.GetValueOrDefault("parent_id");
                _logger.LogWarning("⚠️ Dropped category: ID={Id}, Name='{Name}', ParentId={ParentId}", id, name, parentId);
            }
        }

        // Filter to only the requested categories while preserving hierarchical order
        var requestedEntityIdsSet = new HashSet<string>(entityIds);
        var filteredCategories = sortedCategories
            .Where(cat => requestedEntityIdsSet.Contains(cat.GetValueOrDefault("id")?.ToString() ?? ""))
            .ToList();

        _logger.LogDebug("After hierarchical sorting and filtering, found {Count} matching categories:",
            filteredCategories.Count);

        // ✅ VALIDATION: Check for missing requested categories
        var foundIds = new HashSet<string>(filteredCategories.Select(c => c.GetValueOrDefault("id")?.ToString() ?? ""));
        var missingIds = entityIds.Where(id => !foundIds.Contains(id)).ToList();

        if (missingIds.Any())
        {
            _logger.LogWarning("⚠️ Missing {MissingCount} requested categories from fetch results: [{MissingIds}]",
                missingIds.Count, string.Join(", ", missingIds));
        }

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

        _logger.LogInformation("✅ Successfully fetched {Count} of {Total} categories for migration {MigrationId}",
            filteredCategories.Count, entityIds.Count, migrationId);

        return filteredCategories;
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