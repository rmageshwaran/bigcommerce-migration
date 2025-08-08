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

        var fetchedProducts = new List<Dictionary<string, object>>();
        var pageSize = 50;
        var page = 1;

        while (!cancellationToken.IsCancellationRequested)
        {
            var products = await _apiClient.GetProductsAsync(
                sourceStore, 
                page, 
                pageSize, 
                "custom_fields,channels",
                cancellationToken);

            if (products == null || products.Count == 0)
                break;

            // Filter to get only the requested products if specific IDs were provided
            var filteredProducts = products;
            if (entityIds.Any())
            {
                filteredProducts = products
                    .Where(product => 
                    {
                        var productId = product.TryGetValue("id", out var id) ? id.ToString() : null;
                        return productId != null && entityIds.Contains(productId);
                    })
                    .ToList();
            }

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

            // If we found all requested products, stop fetching
            if (entityIds.Any() && fetchedProducts.Count >= entityIds.Count)
                break;

            page++;
        }

        return fetchedProducts;
    }
} 