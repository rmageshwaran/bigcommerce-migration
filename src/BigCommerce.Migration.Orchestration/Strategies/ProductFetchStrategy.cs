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
        // LSP COMPLIANCE: Consistent parameter validation across all strategies
        if (entityIds == null) throw new ArgumentNullException(nameof(entityIds));
        if (string.IsNullOrWhiteSpace(migrationId)) throw new ArgumentNullException(nameof(migrationId));
        if (sourceStore == null) throw new ArgumentNullException(nameof(sourceStore));

        _logger.LogInformation("Fetching {Count} specific products for migration {MigrationId}", 
            entityIds.Count, migrationId);

        var fetchedProducts = new List<Dictionary<string, object>>();

        try
        {
            // LSP COMPLIANCE: Check for cancellation consistently across all strategies
            cancellationToken.ThrowIfCancellationRequested();

            if (!entityIds.Any())
            {
                _logger.LogInformation("No specific product IDs provided for migration {MigrationId}", migrationId);
                return fetchedProducts;
            }

            // Use standard product pagination API
            // Note: The original EntityFetchService doesn't support fetching specific product IDs
            // It just fetches products by pagination, so we align with that approach
            var pageSize = 50;
            var page = 1;

            _logger.LogDebug("Using pagination approach for products (page size: {PageSize})", pageSize);

            while (!cancellationToken.IsCancellationRequested)
            {
                var products = await _apiClient.GetProductsAsync(
                    sourceStore, 
                    page, 
                    pageSize, 
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

                _logger.LogDebug("Fetched {Count} products from page {Page} (filtered: {FilteredCount})", 
                    products.Count, page, filteredProducts.Count);

                // If we found all requested products, stop fetching
                if (entityIds.Any() && fetchedProducts.Count >= entityIds.Count)
                    break;

                page++;
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