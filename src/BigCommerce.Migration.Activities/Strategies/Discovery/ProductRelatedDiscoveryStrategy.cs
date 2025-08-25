using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Discovery;

/// <summary>
/// Custom discovery strategy for product-related phase that queries EntityProgress table
/// instead of source BigCommerce API to get total successful products count.
/// 
/// This strategy is designed for Phase 3 of product migration where we need to update
/// existing products with related product IDs based on data already stored during Phase 1.
/// 
/// Key Features:
/// - Queries EntityProgress table for "products" entity type
/// - Returns SuccessCount as TotalCount for product-related processing
/// - Follows IEntityDiscoveryStrategy interface for seamless integration
/// - Supports cancellation and proper error handling
/// </summary>
public class ProductRelatedDiscoveryStrategy : IEntityDiscoveryStrategy
{
    private readonly IMigrationStorageService _storageService;
    private readonly ILogger<ProductRelatedDiscoveryStrategy> _logger;

    /// <summary>
    /// Gets the BigCommerce API version this strategy supports (V3 for compatibility)
    /// Note: This strategy doesn't actually call BigCommerce API but maintains V3 compatibility
    /// </summary>
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V3;

    /// <summary>
    /// Initializes a new instance of ProductRelatedDiscoveryStrategy
    /// </summary>
    /// <param name="storageService">Migration storage service for EntityProgress queries</param>
    /// <param name="logger">Logger for the strategy</param>
    public ProductRelatedDiscoveryStrategy(
        IMigrationStorageService storageService,
        ILogger<ProductRelatedDiscoveryStrategy> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers entities using EntityProgress table instead of BigCommerce API
    /// Queries for successful products count from Phase 1 to determine total entities
    /// available for product-related processing.
    /// </summary>
    /// <param name="request">Entity discovery request (must be "product-related" entity type)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with SuccessCount from products phase as TotalCount</returns>
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!request.EntityType.Equals("product-related", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"ProductRelatedDiscoveryStrategy only supports 'product-related' entity type, but received '{request.EntityType}'");
            }

            _logger.LogInformation("🔍 [PRODUCT-RELATED-DISCOVERY] Starting EntityProgress-based discovery for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);

            // Query EntityProgress table for "products" entity type to get successful products count
            var progressEntries = await _storageService.GetEntityProgressAsync(request.MigrationId, "products");
            
            // 🚫 CANCELLATION: Check after storage operation
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogDebug("🔍 [PRODUCT-RELATED-DISCOVERY] Retrieved {Count} progress entries for 'products' entity type", 
                progressEntries.Count);

            // Find the products progress entry
            var productsProgress = progressEntries.FirstOrDefault(p => p.EntityType.Equals("products", StringComparison.OrdinalIgnoreCase));
            
            if (productsProgress == null)
            {
                _logger.LogWarning("⚠️ [PRODUCT-RELATED-DISCOVERY] No EntityProgress entry found for 'products' entity type in migration {MigrationId}. " +
                                 "This indicates Phase 1 (products) may not be completed yet.", request.MigrationId);
                
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    Errors = new List<string> { "No products progress found. Phase 1 (products) must be completed before product-related phase." },
                    ApiVersion = BigCommerceApiVersion.V3,
                    SkipDiscovery = true // Skip processing if no products were migrated
                };
            }

            // Use SuccessCount as TotalCount for product-related processing
            var totalSuccessfulProducts = productsProgress.SuccessCount;
            
            _logger.LogInformation("✅ [PRODUCT-RELATED-DISCOVERY] EntityProgress discovery completed for {EntityType} - " +
                                 "Found {SuccessfulProducts} successful products from Phase 1 (Total: {TotalProducts}, Failed: {FailedProducts}, Skipped: {SkippedProducts})", 
                request.EntityType, totalSuccessfulProducts, productsProgress.TotalCount, 
                productsProgress.FailureCount, productsProgress.SkippedCount);

            // Check if any products were successfully migrated
            if (totalSuccessfulProducts == 0)
            {
                _logger.LogWarning("⚠️ [PRODUCT-RELATED-DISCOVERY] No successful products found in Phase 1. " +
                                 "Product-related phase will be skipped for migration {MigrationId}", request.MigrationId);
                
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    ApiVersion = BigCommerceApiVersion.V3,
                    SkipDiscovery = true,
                    PaginationMetadata = new Dictionary<string, object>
                    {
                        { "ApiVersion", "V3" },
                        { "Strategy", "ProductRelatedDiscovery" },
                        { "DataSource", "EntityProgress" },
                        { "ProductsPhaseTotal", productsProgress.TotalCount },
                        { "ProductsPhaseSuccess", totalSuccessfulProducts },
                        { "ProductsPhaseFailure", productsProgress.FailureCount },
                        { "ProductsPhaseSkipped", productsProgress.SkippedCount },
                        { "SkipReason", "NoSuccessfulProducts" }
                    }
                };
            }

            // Return discovery result for EntityMappings-based pagination
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = totalSuccessfulProducts, // Use successful products count as total
                EntityIds = new List<string>(), // Empty - will use EntityMappings pagination
                EntityData = new List<Dictionary<string, object>>(), // Empty - no caching for memory efficiency
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = false,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "Strategy", "ProductRelatedDiscovery" },
                    { "DataSource", "EntityProgress" },
                    { "UseEntityMappingsPagination", true },
                    { "ProductsPhaseTotal", productsProgress.TotalCount },
                    { "ProductsPhaseSuccess", totalSuccessfulProducts },
                    { "ProductsPhaseFailure", productsProgress.FailureCount },
                    { "ProductsPhaseSkipped", productsProgress.SkippedCount },
                    { "ProductsPhaseStatus", productsProgress.Status },
                    { "MemoryOptimized", true },
                    { "CachingDisabled", true }
                }
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🚫 [PRODUCT-RELATED-DISCOVERY-CANCEL] Product-related discovery cancelled for migration {MigrationId}", 
                request.MigrationId);
            throw; // Re-throw to propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PRODUCT-RELATED-DISCOVERY-ERROR] ProductRelatedDiscoveryStrategy failed for {EntityType} in migration {MigrationId}. " +
                            "ErrorType: {ErrorType}, Category: Error",
                request.EntityType, request.MigrationId, ex.GetType().Name);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { $"EntityProgress query failed: {ex.Message}" },
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = true
            };
        }
    }
}
