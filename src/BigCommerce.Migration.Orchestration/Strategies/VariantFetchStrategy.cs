using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Fetch strategy for product variant entities
/// Handles variant-specific fetching logic requiring product context
/// </summary>
public class VariantFetchStrategy : IEntityFetchStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<VariantFetchStrategy> _logger;

    public string EntityType => "variants";

    public VariantFetchStrategy(IBigCommerceApiClient apiClient, ILogger<VariantFetchStrategy> logger)
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

        // Variants require product context - use pagination to find variants
        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = 50,
            SortBy = "id",
            SortDirection = "asc"
        };

        var fetchedVariants = new List<Dictionary<string, object>>();

        try
        {
            // LSP COMPLIANCE: Check for cancellation consistently across all strategies
            cancellationToken.ThrowIfCancellationRequested();

            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    sourceStore,
                    "variants",
                    paginationRequest,
                    cancellationToken);

                if (response.Data == null || response.Data.Count == 0)
                    break;

                // Filter to get only the requested variants
                var filteredVariants = response.Data
                    .Where(variant => 
                    {
                        var variantId = variant.TryGetValue("id", out var id) ? id.ToString() : null;
                        return variantId != null && entityIds.Contains(variantId);
                    })
                    .ToList();

                if (filteredVariants.Any())
                {
                    // Add original entity ID tracking for error reporting
                    foreach (var variant in filteredVariants)
                    {
                        var variantId = variant.TryGetValue("id", out var id) ? id.ToString() : null;
                        variant["_original_entity_id"] = variantId;
                    }

                    fetchedVariants.AddRange(filteredVariants);
                }

                // If we found all requested variants, stop fetching
                if (fetchedVariants.Count >= entityIds.Count)
                    break;

                if (!response.HasNextPage)
                    break;

                paginationRequest.Page++;
            }

            _logger.LogInformation("Successfully fetched {FetchedCount}/{RequestedCount} variants for migration {MigrationId}", 
                fetchedVariants.Count, entityIds.Count, migrationId);

            return fetchedVariants;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch variants for migration {MigrationId}", migrationId);
            
            // Return empty list to allow migration to continue with other batches
            return new List<Dictionary<string, object>>();
        }
    }
} 