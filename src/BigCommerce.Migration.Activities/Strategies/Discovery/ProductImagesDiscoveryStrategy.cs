using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Discovery;

/// <summary>
/// Custom discovery strategy for product-images phase that queries EntityProgress table
/// instead of source BigCommerce API to get total successful products count.
/// 
/// This strategy is designed for Phase 4 of product migration where we need to update
/// existing products with image data based on products successfully migrated in Phase 1.
/// 
/// Key Features:
/// - Queries EntityProgress table for "products" entity type
/// - Returns SuccessCount as TotalCount for product-images processing
/// - Follows IEntityDiscoveryStrategy interface for seamless integration
/// - Uses blob-based cancellation with ICancellationStore (not token-based)
/// - Uses EntityMappings-based pagination like ProductRelated strategy
/// </summary>
public class ProductImagesDiscoveryStrategy : IEntityDiscoveryStrategy
{
    private readonly IMigrationStorageService _storageService;
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<ProductImagesDiscoveryStrategy> _logger;

    /// <summary>
    /// Gets the BigCommerce API version this strategy supports (V3 for compatibility)
    /// Note: This strategy doesn't actually call BigCommerce API but maintains V3 compatibility
    /// </summary>
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V3;

    /// <summary>
    /// Initializes a new instance of ProductImagesDiscoveryStrategy
    /// </summary>
    /// <param name="storageService">Migration storage service for EntityProgress queries</param>
    /// <param name="cancellationStore">Cancellation store for blob-based cancellation</param>
    /// <param name="logger">Logger for the strategy</param>
    public ProductImagesDiscoveryStrategy(
        IMigrationStorageService storageService,
        ICancellationStore cancellationStore,
        ILogger<ProductImagesDiscoveryStrategy> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers entities using EntityProgress table instead of BigCommerce API
    /// Queries for successful products count from Phase 1 to determine total entities
    /// available for product-images processing.
    /// </summary>
    /// <param name="request">Entity discovery request (must be "product-images" entity type)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with SuccessCount from products phase as TotalCount</returns>
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        // ✅ VALIDATION: Check entity type first (outside try-catch to allow exception to bubble up)
        if (!request.EntityType.Equals("product-images", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"ProductImagesDiscoveryStrategy only supports 'product-images' entity type, but received '{request.EntityType}'");
        }

        try
        {
            // 🚫 CANCELLATION: Check blob-based cancellation at start
            await CheckCancellationAsync(request.MigrationId);

            _logger.LogInformation("🔍 [PRODUCT-IMAGES-DISCOVERY] Starting EntityProgress-based discovery for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);

            // Query EntityProgress table for "products" entity type to get successful products count
            var progressEntries = await _storageService.GetEntityProgressAsync(request.MigrationId, "products");
            
            // 🚫 CANCELLATION: Check blob-based cancellation after storage operation
            await CheckCancellationAsync(request.MigrationId);
            
            _logger.LogDebug("🔍 [PRODUCT-IMAGES-DISCOVERY] Retrieved {Count} progress entries for 'products' entity type", 
                progressEntries.Count);

            // Find the products progress entry
            var productsProgress = progressEntries.FirstOrDefault(p => p.EntityType.Equals("products", StringComparison.OrdinalIgnoreCase));
            
            if (productsProgress == null)
            {
                _logger.LogWarning("⚠️ [PRODUCT-IMAGES-DISCOVERY] No EntityProgress entry found for 'products' entity type in migration {MigrationId}. " +
                                 "This indicates Phase 1 (products) may not be completed yet.", request.MigrationId);
                
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    Errors = new List<string> { "No products progress found. Phase 1 (products) must be completed before product-images phase." },
                    ApiVersion = BigCommerceApiVersion.V3,
                    SkipDiscovery = true // Skip processing if no products were migrated
                };
            }

            // Use SuccessCount as TotalCount for product-images processing
            var totalSuccessfulProducts = productsProgress.SuccessCount;
            
            _logger.LogInformation("✅ [PRODUCT-IMAGES-DISCOVERY] EntityProgress discovery completed for {EntityType} - " +
                                 "Found {SuccessfulProducts} successful products from Phase 1 (Total: {TotalProducts}, Failed: {FailedProducts}, Skipped: {SkippedProducts})", 
                request.EntityType, totalSuccessfulProducts, productsProgress.TotalCount, 
                productsProgress.FailureCount, productsProgress.SkippedCount);

            // Check if any products were successfully migrated
            if (totalSuccessfulProducts == 0)
            {
                _logger.LogWarning("⚠️ [PRODUCT-IMAGES-DISCOVERY] No successful products found in Phase 1. " +
                                 "Product-images phase will be skipped for migration {MigrationId}", request.MigrationId);
                
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
                        { "Strategy", "ProductImagesDiscovery" },
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
                    { "Strategy", "ProductImagesDiscovery" },
                    { "DataSource", "EntityProgress" },
                    { "UseEntityMappingsPagination", true },
                    { "ProductsPhaseTotal", productsProgress.TotalCount },
                    { "ProductsPhaseSuccess", totalSuccessfulProducts },
                    { "ProductsPhaseFailure", productsProgress.FailureCount },
                    { "ProductsPhaseSkipped", productsProgress.SkippedCount },
                    { "ProductsPhaseStatus", productsProgress.Status },
                    { "MemoryOptimized", true },
                    { "CachingDisabled", true },
                    { "MaxImagesPerProduct", 1000 },
                    { "ImageChunkSize", 50 },
                    { "ImageChunkDelayMs", 300 },
                    { "MaxConcurrency", 5 }
                }
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🚫 [PRODUCT-IMAGES-DISCOVERY-CANCEL] Product-images discovery cancelled for migration {MigrationId}", 
                request.MigrationId);
            throw; // Re-throw to propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PRODUCT-IMAGES-DISCOVERY-ERROR] ProductImagesDiscoveryStrategy failed for {EntityType} in migration {MigrationId}. " +
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

    /// <summary>
    /// Checks cancellation status using blob-based cancellation and throws OperationCanceledException if migration is cancelled
    /// Uses cooperative cancellation pattern suitable for Azure Functions activities
    /// </summary>
    /// <param name="migrationId">Migration ID to check cancellation for</param>
    private async Task CheckCancellationAsync(string migrationId)
    {
        try
        {
            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            if (isCancelled)
            {
                var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                _logger.LogInformation("🚫 [PRODUCT-IMAGES-DISCOVERY-CANCEL] Product-images discovery cancelled: {Reason}", reason);
                throw new OperationCanceledException($"Migration cancelled: {reason}");
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CANCELLATION-CHECK] Failed to check cancellation flag for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't throw - continue processing if cancellation check fails
        }
    }
}
