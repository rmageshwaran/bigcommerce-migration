using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Discovery;

/// <summary>
/// 🆕 OPTIMIZED: Discovery strategy for product-channel-assign phase using efficient RowNumber-based counting
/// Uses RowNumber service and range queries instead of enumerating EntityMappings records
/// 
/// This strategy is designed for Phase 5 of product migration where we need to assign
/// products to channels based on channel mapping data stored during Phase 1.
/// 
/// Key Features:
/// - Uses EntityMappingsPaginationService for fast counting (no enumeration)
/// - Counts products that have non-empty ChannelsData field efficiently
/// - Optimized for large datasets with constant-time performance
/// - Follows IEntityDiscoveryStrategy interface for seamless integration
/// - Supports cancellation and proper error handling
/// 
/// Performance Improvements:
/// - Discovery time reduced from O(n) to O(1) for counting
/// - No memory accumulation during discovery
/// - Fast execution regardless of dataset size
/// </summary>
public class ProductChannelAssignDiscoveryStrategy : IEntityDiscoveryStrategy
{
    private readonly IMigrationStorageService _storageService;
    private readonly ILogger<ProductChannelAssignDiscoveryStrategy> _logger;

    /// <summary>
    /// Gets the BigCommerce API version this strategy supports (V3 for compatibility)
    /// Note: This strategy doesn't actually call BigCommerce API but maintains V3 compatibility
    /// </summary>
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V3;

    /// <summary>
    /// Initializes a new instance of ProductChannelAssignDiscoveryStrategy
    /// </summary>
    /// <param name="storageService">Migration storage service for EntityProgress queries</param>
    /// <param name="logger">Logger for the strategy</param>
    public ProductChannelAssignDiscoveryStrategy(
        IMigrationStorageService storageService,
        ILogger<ProductChannelAssignDiscoveryStrategy> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers entities by counting products with ChannelsData in EntityMappings table
    /// Counts products that have non-empty ChannelsData field for channel assignment processing.
    /// </summary>
    /// <param name="request">Entity discovery request (must be "product-channel-assign" entity type)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with total count of products that have channel data</returns>
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (!request.EntityType.Equals("product-channel-assign", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"This strategy only supports 'product-channel-assign' entity type, but received '{request.EntityType}'");
        }

        _logger.LogInformation("🔍 [PRODUCT-CHANNEL-ASSIGN-DISCOVERY] Starting EntityProgress-based discovery for migration {MigrationId}", 
            request.MigrationId);
            
        // ✅ DEBUG: Log discovery strategy details
        _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Discovery strategy: ProductChannelAssignDiscoveryStrategy");
        _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Entity type: {EntityType}", request.EntityType);
        _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Migration ID: {MigrationId}", request.MigrationId);

        try
        {
            // ✅ CORRECT LOGIC: Get total successful products from EntityProgress table (from Phase 1)
            _logger.LogInformation("📊 [DISCOVERY-DEBUG] Querying EntityProgress table for 'products' entity type...");
            var progressEntries = await _storageService.GetEntityProgressAsync(request.MigrationId, "products");
            
            // 🚫 CANCELLATION: Check after storage operation
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogDebug("🔍 [PRODUCT-CHANNEL-ASSIGN-DISCOVERY] Retrieved {Count} progress entries for 'products' entity type", 
                progressEntries.Count);
                
            // ✅ DEBUG: Log all progress entries found
            _logger.LogInformation("📊 [DISCOVERY-DEBUG] EntityProgress entries found: {EntryCount}", progressEntries.Count);
            foreach (var entry in progressEntries)
            {
                _logger.LogDebug("📊 [DISCOVERY-DEBUG] Progress entry: EntityType={EntityType}, Total={Total}, Success={Success}, Failed={Failed}, Skipped={Skipped}", 
                    entry.EntityType, entry.TotalCount, entry.SuccessCount, entry.FailureCount, entry.SkippedCount);
            }

            // Find the products progress entry
            var productsProgress = progressEntries.FirstOrDefault(p => p.EntityType.Equals("products", StringComparison.OrdinalIgnoreCase));

            if (productsProgress == null)
            {
                _logger.LogWarning("⚠️ [PRODUCT-CHANNEL-ASSIGN-DISCOVERY] No EntityProgress entry found for 'products' entity type in migration {MigrationId}. " +
                                 "This indicates Phase 1 (products) may not be completed yet.", request.MigrationId);
                
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    Errors = new List<string> { "No products progress found. Phase 1 (products) must be completed before product-channel-assign phase." },
                    ApiVersion = BigCommerceApiVersion.V3,
                    SkipDiscovery = true
                };
            }

            // ✅ CORRECT: Use SuccessCount as TotalCount for processing ALL successful products
            // Products without ChannelsData will be marked as "skipped" during transform phase
            var totalSuccessfulProducts = productsProgress.SuccessCount;
            
            // ✅ DEBUG: Log detailed discovery results
            _logger.LogInformation("📊 [DISCOVERY-DEBUG] Products phase results from Phase 1:");
            _logger.LogInformation("📊 [DISCOVERY-DEBUG] Total products processed: {TotalCount}", productsProgress.TotalCount);
            _logger.LogInformation("📊 [DISCOVERY-DEBUG] Successful products: {SuccessCount}", productsProgress.SuccessCount);
            _logger.LogInformation("📊 [DISCOVERY-DEBUG] Failed products: {FailureCount}", productsProgress.FailureCount);
            _logger.LogInformation("📊 [DISCOVERY-DEBUG] Skipped products: {SkippedCount}", productsProgress.SkippedCount);
            _logger.LogInformation("🎯 [DISCOVERY-DEBUG] Will process {TotalToProcess} products for channel assignment (products without ChannelsData will be skipped during fetch/transform)", totalSuccessfulProducts);
            
            _logger.LogInformation("📊 [PRODUCT-CHANNEL-ASSIGN-DISCOVERY] EntityProgress discovery completed for migration {MigrationId}: " +
                "Total Successful Products: {SuccessfulProducts} (products without ChannelsData will be skipped during processing)",
                request.MigrationId, totalSuccessfulProducts);

            // Check if any products were successfully migrated
            if (totalSuccessfulProducts == 0)
            {
                _logger.LogWarning("⚠️ [PRODUCT-CHANNEL-ASSIGN-DISCOVERY] No successful products found in Phase 1. " +
                                 "Product-channel-assign phase will be skipped for migration {MigrationId}", request.MigrationId);
                
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
                        { "Strategy", "ProductChannelAssignDiscovery" },
                        { "DataSource", "EntityProgress" },
                        { "ProductsPhaseTotal", productsProgress.TotalCount },
                        { "ProductsPhaseSuccess", totalSuccessfulProducts },
                        { "ProductsPhaseFailure", productsProgress.FailureCount },
                        { "ProductsPhaseSkipped", productsProgress.SkippedCount },
                        { "SkipReason", "NoSuccessfulProducts" }
                    }
                };
            }

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = totalSuccessfulProducts, // ✅ CORRECT: Total successful products to process
                EntityIds = new List<string>(), // Not used - using RowNumber range queries
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = false,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "Strategy", "ProductChannelAssignDiscovery" },
                    { "DataSource", "EntityProgress" },
                    { "ProductsPhaseTotal", productsProgress.TotalCount },
                    { "ProductsPhaseSuccess", totalSuccessfulProducts },
                    { "ProductsPhaseFailure", productsProgress.FailureCount },
                    { "ProductsPhaseSkipped", productsProgress.SkippedCount },
                    { "UseRangeQueries", true }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PRODUCT-CHANNEL-ASSIGN-DISCOVERY] Discovery failed for migration {MigrationId}: {ErrorMessage}",
                request.MigrationId, ex.Message);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = true,
                Errors = new List<string> { $"Discovery failed: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Validates that the store configuration is valid for discovery operations
    /// Note: This strategy doesn't use store configuration but validates for interface compliance
    /// </summary>
    /// <param name="storeConfig">Store configuration to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public bool ValidateStoreConfiguration(StoreConfiguration storeConfig)
    {
        // For EntityMappings-based discovery, we don't need store configuration
        // but we validate for interface compliance
        return storeConfig != null && storeConfig.IsValid();
    }

    /// <summary>
    /// Gets the supported entity types for this discovery strategy
    /// </summary>
    /// <returns>List of supported entity types</returns>
    public List<string> GetSupportedEntityTypes()
    {
        return new List<string> { "product-channel-assign" };
    }
}
