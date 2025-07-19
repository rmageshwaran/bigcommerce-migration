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
        // LSP COMPLIANCE: Consistent parameter validation across all strategies
        if (entityIds == null) throw new ArgumentNullException(nameof(entityIds));
        if (string.IsNullOrWhiteSpace(migrationId)) throw new ArgumentNullException(nameof(migrationId));
        if (sourceStore == null) throw new ArgumentNullException(nameof(sourceStore));

        _logger.LogInformation("Fetching {Count} specific brands for migration {MigrationId}", 
            entityIds.Count, migrationId);

        var fetchedBrands = new List<Dictionary<string, object>>();

        try
        {
            // LSP COMPLIANCE: Check for cancellation consistently across all strategies
            cancellationToken.ThrowIfCancellationRequested();

            if (!entityIds.Any())
            {
                _logger.LogInformation("No specific brand IDs provided for migration {MigrationId}", migrationId);
                return fetchedBrands;
            }

            // Use pagination API approach (brands follow same pattern as products)
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
                    "brands",
                    paginationRequest,
                    cancellationToken);

                if (response.Data == null || response.Data.Count == 0)
                    break;

                // Filter to get only the requested brands
                var filteredBrands = response.Data
                    .Where(brand => 
                    {
                        var brandId = brand.TryGetValue("id", out var id) ? id.ToString() : null;
                        return brandId != null && entityIds.Contains(brandId);
                    })
                    .ToList();

                if (filteredBrands.Any())
                {
                    // Add original entity ID tracking for error reporting
                    foreach (var brand in filteredBrands)
                    {
                        var brandId = brand.TryGetValue("id", out var id) ? id.ToString() : null;
                        brand["_original_entity_id"] = brandId;
                    }

                    fetchedBrands.AddRange(filteredBrands);
                }

                _logger.LogDebug("Fetched {Count} brands from page {Page} (filtered: {FilteredCount})", 
                    response.Data.Count, paginationRequest.Page, filteredBrands.Count);

                // If we found all requested brands, stop fetching
                if (fetchedBrands.Count >= entityIds.Count)
                    break;

                if (!response.HasNextPage)
                    break;

                paginationRequest.Page++;
            }

            _logger.LogInformation("Successfully fetched {FetchedCount}/{RequestedCount} brands for migration {MigrationId}", 
                fetchedBrands.Count, entityIds.Count, migrationId);

            return fetchedBrands;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch brands for migration {MigrationId}", migrationId);
            
            // Return empty list to allow migration to continue with other batches
            return new List<Dictionary<string, object>>();
        }
    }
} 