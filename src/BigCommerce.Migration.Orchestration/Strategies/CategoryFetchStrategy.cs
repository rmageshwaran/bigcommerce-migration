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

        _logger.LogInformation("Fetching {Count} specific categories for migration {MigrationId}", 
            entityIds.Count, migrationId);
        
        var fetchedCategories = new List<Dictionary<string, object>>();

        try
        {
            // LSP COMPLIANCE: Check for cancellation consistently across all strategies
            cancellationToken.ThrowIfCancellationRequested();

            // Determine category tree ID for fetching
            var categoryTreeId = categoryTreeContext?.SourceCategoryTreeId ?? "1"; // Default to tree 1
            
            _logger.LogDebug("Using category tree ID {CategoryTreeId} for fetching categories", categoryTreeId);

            // OPTIMIZATION: Fetch ALL categories once instead of making multiple API calls
            // This matches the original EntityFetchService approach and is much more efficient
            var allCategories = await _apiClient.GetCategoriesAsync(sourceStore, categoryTreeId, cancellationToken);
            
            if (allCategories?.Any() == true)
            {
                // Filter to get only the requested categories
                var filteredCategories = allCategories
                    .Where(category => 
                    {
                        var categoryId = category.TryGetValue("id", out var id) ? id.ToString() : null;
                        return categoryId != null && entityIds.Contains(categoryId);
                    })
                    .ToList();

                // Add original entity ID tracking for error reporting
                foreach (var category in filteredCategories)
                {
                    var categoryId = category.TryGetValue("id", out var id) ? id.ToString() : null;
                    category["_original_entity_id"] = categoryId;
                }

                fetchedCategories.AddRange(filteredCategories);
                
                _logger.LogInformation("Found {FoundCount}/{RequestedCount} categories from {TotalFetched} total categories", 
                    filteredCategories.Count, entityIds.Count, allCategories.Count);
            }
            else
            {
                _logger.LogWarning("No categories found in store {StoreId} tree {TreeId}", 
                    sourceStore.StoreId, categoryTreeId);
            }

            _logger.LogInformation("Successfully fetched {SuccessCount} of {TotalCount} categories for migration {MigrationId}", 
                fetchedCategories.Count, entityIds.Count, migrationId);

            return fetchedCategories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch categories for migration {MigrationId}", migrationId);
            
            // Return empty list to allow migration to continue with other batches
            return new List<Dictionary<string, object>>();
        }
    }


} 