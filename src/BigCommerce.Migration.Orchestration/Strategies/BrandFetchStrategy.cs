using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Fetch strategy for brand entities
/// Handles brand-specific fetching logic with pagination
/// </summary>
public class BrandFetchStrategy : IEntityFetchStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<BrandFetchStrategy> _logger;

    public string EntityType => "brands";

    public BrandFetchStrategy(IBigCommerceApiClient apiClient, ILogger<BrandFetchStrategy> logger)
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
        
        // 🚨 CRITICAL DEBUG: This strategy should NOT be used for brands with direct pagination
        _logger.LogError("🚨 [BRAND-FETCH-STRATEGY] ❌ CRITICAL: BrandFetchStrategy.FetchEntitiesAsync called! " +
                        "This indicates a routing issue - brands should use direct pagination only. " +
                        "EntityIds.Count={EntityIdsCount}, MigrationId={MigrationId}", 
            entityIds?.Count ?? 0, migrationId);
            
        if (entityIds == null || string.IsNullOrWhiteSpace(migrationId) || sourceStore == null)
        {
            _logger.LogWarning("🚨 [BRAND-FETCH-STRATEGY] Invalid fetch strategy input: entityIds, migrationId, or sourceStore is null/empty. Returning empty list.");
            return new List<Dictionary<string, object>>();
        }
        if (!entityIds.Any())
        {
            _logger.LogWarning("🚨 [BRAND-FETCH-STRATEGY] Empty entityIds provided - this is expected for direct pagination. Returning empty list.");
            return new List<Dictionary<string, object>>();
        }

        // 🚨 EMERGENCY FALLBACK: If we reach here, something is wrong with routing
        // Log detailed information for debugging
        _logger.LogError("🚨 [BRAND-FETCH-STRATEGY] EMERGENCY FALLBACK: Processing {Count} specific brand IDs " +
                        "but this should use direct pagination instead. EntityIds: [{EntityIds}], MigrationId={MigrationId}", 
            entityIds.Count, string.Join(", ", entityIds.Take(10)), migrationId);

        var fetchedBrands = new List<Dictionary<string, object>>();

        try
        {
            // 🚨 FIXED: Use single page fetch with entity ID filter instead of pagination loop
            // This eliminates the while loop that was causing pagination conflicts
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = Math.Min(entityIds.Count, 250), // Use actual count or max limit
                SortBy = "id",
                SortDirection = "asc"
            };

            var response = await _apiClient.GetPaginatedEntitiesAsync(
                sourceStore,
                "brands",
                paginationRequest,
                cancellationToken);

            if (response?.Data != null)
            {
                // Filter to get only the requested brands (API doesn't support ID filtering, so filter locally)
                var filteredBrands = response.Data
                    .Where(brand => 
                    {
                        var brandId = brand.TryGetValue("id", out var id) ? id.ToString() : null;
                        return brandId != null && entityIds.Contains(brandId);
                    })
                    .ToList();

                foreach (var brand in filteredBrands)
                {
                    var brandId = brand.TryGetValue("id", out var id) ? id.ToString() : null;
                    brand["_original_entity_id"] = brandId;
                    fetchedBrands.Add(brand);
                }
            }
            
            _logger.LogWarning("🚨 [BRAND-FETCH-STRATEGY] EMERGENCY FALLBACK completed: " +
                              "Fetched {FetchedCount}/{RequestedCount} brands for migration {MigrationId}", 
                fetchedBrands.Count, entityIds.Count, migrationId);

            return fetchedBrands;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [BRAND-FETCH-STRATEGY] ❌ EMERGENCY FALLBACK FAILED: " +
                            "Failed to fetch {Count} brands for migration {MigrationId}", 
                entityIds.Count, migrationId);
            
            // Return empty list to prevent blocking migration
            return new List<Dictionary<string, object>>();
        }
    }
} 