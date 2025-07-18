using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Fetch strategy for product modifier entities
/// Handles modifier-specific fetching logic requiring product context
/// </summary>
public class ModifierFetchStrategy : IEntityFetchStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger _logger;

    public string EntityType => "modifiers";

    public ModifierFetchStrategy(IBigCommerceApiClient apiClient, ILogger logger)
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

        _logger.LogInformation("Fetching {Count} specific modifiers for migration {MigrationId}", 
            entityIds.Count, migrationId);

        var fetchedModifiers = new List<Dictionary<string, object>>();

        try
        {
            // LSP COMPLIANCE: Check for cancellation consistently across all strategies
            cancellationToken.ThrowIfCancellationRequested();

            if (!entityIds.Any())
            {
                _logger.LogInformation("No specific modifier IDs provided for migration {MigrationId}", migrationId);
                return fetchedModifiers;
            }

            // Modifiers require product context - use pagination to find modifiers
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 50,
                SortBy = "id",
                SortDirection = "asc"
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    sourceStore,
                    "modifiers",
                    paginationRequest,
                    cancellationToken);

                if (response.Data == null || response.Data.Count == 0)
                    break;

                // Filter to get only the requested modifiers
                var filteredModifiers = response.Data
                    .Where(modifier => 
                    {
                        var modifierId = modifier.TryGetValue("id", out var id) ? id.ToString() : null;
                        return modifierId != null && entityIds.Contains(modifierId);
                    })
                    .ToList();

                if (filteredModifiers.Any())
                {
                    // Add original entity ID tracking for error reporting
                    foreach (var modifier in filteredModifiers)
                    {
                        var modifierId = modifier.TryGetValue("id", out var id) ? id.ToString() : null;
                        modifier["_original_entity_id"] = modifierId;
                    }

                    fetchedModifiers.AddRange(filteredModifiers);
                }

                _logger.LogDebug("Fetched {Count} modifiers from page {Page} (filtered: {FilteredCount})", 
                    response.Data.Count, paginationRequest.Page, filteredModifiers.Count);

                // If we found all requested modifiers, stop fetching
                if (fetchedModifiers.Count >= entityIds.Count)
                    break;

                if (!response.HasNextPage)
                    break;

                paginationRequest.Page++;
            }

            _logger.LogInformation("Successfully fetched {FetchedCount}/{RequestedCount} modifiers for migration {MigrationId}", 
                fetchedModifiers.Count, entityIds.Count, migrationId);

            return fetchedModifiers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch modifiers for migration {MigrationId}", migrationId);
            
            // Return empty list to allow migration to continue with other batches
            return new List<Dictionary<string, object>>();
        }
    }
} 