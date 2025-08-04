using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Fetch strategy for product entities
/// Handles product-specific fetching logic with parallel processing for performance
/// </summary>
public class ProductFetchStrategy : IEntityFetchStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<ProductFetchStrategy> _logger;

    public string EntityType => "products";

    public ProductFetchStrategy(IBigCommerceApiClient apiClient, ILogger<ProductFetchStrategy> logger)
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

        _logger.LogInformation("Fetching {Count} specific products for migration {MigrationId}", 
            entityIds.Count, migrationId);

        var fetchedProducts = new List<Dictionary<string, object>>();

        try
        {
            // LSP COMPLIANCE: Check for cancellation consistently across all strategies
            cancellationToken.ThrowIfCancellationRequested();

            // Use pagination API approach (products follow same pattern as brands)
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 50,
                SortBy = "id",
                SortDirection = "asc",
                // Include sub-resources needed for complete product data
                AdditionalParams = new Dictionary<string, string>
                {
                    ["include"] = "bulk_pricing_rules,modifiers,options,parent_relations,custom_fields,videos"
                }
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    sourceStore,
                    "products",
                    paginationRequest,
                    cancellationToken);

                if (response.Data == null || response.Data.Count == 0)
                    break;

                // Filter to get only the requested products
                var filteredProducts = response.Data
                    .Where(product => 
                    {
                        var productId = product.TryGetValue("id", out var id) ? id.ToString() : null;
                        return productId != null && entityIds.Contains(productId);
                    })
                    .ToList();

                if (filteredProducts.Any())
                {
                    // Add original entity ID tracking for error reporting
                    foreach (var product in filteredProducts)
                    {
                        var productId = product.TryGetValue("id", out var id) ? id.ToString() : null;
                        product["_original_entity_id"] = productId;
                    }

                    fetchedProducts.AddRange(filteredProducts);
                }

                _logger.LogDebug("Fetched {Count} products from page {Page} (filtered: {FilteredCount})", 
                    response.Data.Count, paginationRequest.Page, filteredProducts.Count);

                // If we found all requested products, stop fetching
                if (fetchedProducts.Count >= entityIds.Count)
                    break;

                if (!response.HasNextPage)
                    break;

                paginationRequest.Page++;
            }

            _logger.LogInformation("Successfully fetched {FetchedCount}/{RequestedCount} products for migration {MigrationId}", 
                fetchedProducts.Count, entityIds.Count, migrationId);

            return fetchedProducts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch products for migration {MigrationId}", migrationId);
            
            // Return empty list to allow migration to continue with other batches
            return new List<Dictionary<string, object>>();
        }
    }
} 