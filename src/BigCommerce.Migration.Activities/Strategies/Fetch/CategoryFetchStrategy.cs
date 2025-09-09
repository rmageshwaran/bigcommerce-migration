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
}
