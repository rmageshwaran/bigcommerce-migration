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
    private readonly ILogger<ModifierFetchStrategy> _logger;

    public string EntityType => "modifiers";

    public ModifierFetchStrategy(IBigCommerceApiClient apiClient, ILogger<ModifierFetchStrategy> logger)
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
        if (!entityIds.Any())
        {
            return new List<Dictionary<string, object>>();
        }

        var fetchedModifiers = new List<Dictionary<string, object>>();

        try
        {
            // LSP COMPLIANCE: Check for cancellation consistently across all strategies
            cancellationToken.ThrowIfCancellationRequested();

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
            // ✅ P0-T2: Enhanced API error logging with request/response payload logging
            _logger.LogError(ex, "🔥 [MODIFIER-FETCH-API-ERROR] Failed to fetch modifiers for migration {MigrationId}. " +
                            "Store: {StoreId}, RequestedModifierIds: {ModifierCount}, RequestedIds: [{ModifierIds}], " +
                            "ErrorType: {ErrorType}, Category: Error",
                migrationId, sourceStore.StoreId, entityIds.Count, string.Join(",", entityIds), ex.GetType().Name);
            
            // Return empty list to allow migration to continue with other batches
            return new List<Dictionary<string, object>>();
        }
    }
} 