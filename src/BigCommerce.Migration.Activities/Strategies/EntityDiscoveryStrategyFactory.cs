using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Strategies.Discovery;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies;

/// <summary>
/// Factory for selecting appropriate entity discovery strategies
/// Encapsulates strategy selection logic based on API version and entity characteristics
/// Follows Strategy Pattern and Single Responsibility Principle
/// </summary>
public class EntityDiscoveryStrategyFactory : IEntityDiscoveryStrategyFactory
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<EntityDiscoveryStrategyFactory> _logger;
    private readonly ILogger<V2DirectPaginationStrategy> _v2Logger;
    private readonly ILogger<V3EfficientPaginationStrategy> _v3EfficientLogger;
    private readonly ILogger<V3HierarchicalStrategy> _v3HierarchicalLogger;
    private readonly ILogger<ProductRelatedDiscoveryStrategy> _productRelatedLogger;
    //private readonly ILogger<V3ProductComponentsDiscoveryStrategy> _v3ProductComponentsLogger;
    private readonly ICancellationStore _cancellationStore;
    private readonly IMigrationStorageService _storageService;

    /// <summary>
    /// Initializes a new instance of EntityDiscoveryStrategyFactory
    /// </summary>
    /// <param name="apiClient">BigCommerce API client for version detection</param>
    /// <param name="logger">Logger for the factory</param>
    /// <param name="v2Logger">Logger for V2 strategy</param>
    /// <param name="v3EfficientLogger">Logger for V3 efficient strategy</param>
    /// <param name="v3HierarchicalLogger">Logger for V3 hierarchical strategy</param>
    /// <param name="productRelatedLogger">Logger for ProductRelated discovery strategy</param>
    /// <param name="cancellationStore">Cancellation store for blob-based cancellation</param>
    /// <param name="storageService">Migration storage service for EntityProgress queries</param>
    public EntityDiscoveryStrategyFactory(
        IBigCommerceApiClient apiClient,
        ILogger<EntityDiscoveryStrategyFactory> logger,
        ILogger<V2DirectPaginationStrategy> v2Logger,
        ILogger<V3EfficientPaginationStrategy> v3EfficientLogger,
        ILogger<V3HierarchicalStrategy> v3HierarchicalLogger,
        ILogger<ProductRelatedDiscoveryStrategy> productRelatedLogger,
        ICancellationStore cancellationStore,
        IMigrationStorageService storageService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _v2Logger = v2Logger ?? throw new ArgumentNullException(nameof(v2Logger));
        _v3EfficientLogger = v3EfficientLogger ?? throw new ArgumentNullException(nameof(v3EfficientLogger));
        _v3HierarchicalLogger = v3HierarchicalLogger ?? throw new ArgumentNullException(nameof(v3HierarchicalLogger));
        _productRelatedLogger = productRelatedLogger ?? throw new ArgumentNullException(nameof(productRelatedLogger));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    /// <summary>
    /// Gets the appropriate entity discovery strategy based on store configuration and entity type
    /// </summary>
    /// <param name="storeConfig">Store configuration to determine API version</param>
    /// <param name="entityType">Type of entity being discovered</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Strategy instance appropriate for the given context</returns>
    /// <exception cref="NotSupportedException">Thrown when no strategy supports the detected API version</exception>
    public async Task<IEntityDiscoveryStrategy> GetStrategyAsync(
        StoreConfiguration storeConfig, 
        string entityType, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Detect API version for the store
            var apiVersion = await _apiClient.DetectApiVersionAsync(storeConfig, cancellationToken);
            
            _logger.LogInformation("🏭 [STRATEGY-FACTORY-DEBUG] Detected API version {ApiVersion} for {EntityType} discovery", 
                apiVersion, entityType);
            
            _logger.LogDebug("🏭 [STRATEGY-FACTORY-DEBUG] Store details - StoreId: {StoreId}, EntityType: {EntityType}", 
                storeConfig?.StoreId, entityType);

            return apiVersion switch
            {
                BigCommerceApiVersion.V2 => CreateV2Strategy(),
                BigCommerceApiVersion.V3 => CreateV3Strategy(entityType),
                _ => throw new NotSupportedException($"No discovery strategy found for API version {apiVersion}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get discovery strategy for {EntityType}", entityType);
            throw;
        }
    }

    /// <summary>
    /// Creates V2 direct pagination strategy
    /// </summary>
    /// <returns>V2 strategy instance</returns>
    private IEntityDiscoveryStrategy CreateV2Strategy()
    {
        _logger.LogDebug("Creating V2 direct pagination strategy");
        return new V2DirectPaginationStrategy(_v2Logger);
    }

    /// <summary>
    /// Creates appropriate V3 strategy based on entity type characteristics
    /// </summary>
    /// <param name="entityType">Type of entity</param>
    /// <returns>V3 strategy instance</returns>
    private IEntityDiscoveryStrategy CreateV3Strategy(string entityType)
    {
        _logger.LogInformation("🏭 [STRATEGY-FACTORY-DEBUG] Selecting V3 strategy for EntityType: {EntityType}", entityType);
        
        // 🔧 SPECIAL CASE: product-related uses EntityProgress table instead of BigCommerce API
        if (entityType.Equals("product-related", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("🏭 [STRATEGY-FACTORY-DEBUG] ✅ Creating ProductRelatedDiscoveryStrategy for {EntityType} - will query EntityProgress table", entityType);
            return new ProductRelatedDiscoveryStrategy(_storageService, _productRelatedLogger);
        }
        
        // 🔧 SPECIAL CASE: product-components and individual component types are not real BigCommerce API endpoints
        // They are logical entities that represent components within products (options, modifiers, reviews)
        //var componentTypes = new[] { "product-components", "options", "modifiers", "reviews" };
        //if (componentTypes.Contains(entityType, StringComparer.OrdinalIgnoreCase))
        //{
        //    _logger.LogInformation("🏭 [STRATEGY-FACTORY-DEBUG] ✅ Creating V3 product-components strategy for {EntityType} - will extract components from products", entityType);
        //    return new V3ProductComponentsDiscoveryStrategy(_apiClient, _v3ProductComponentsLogger, _cancellationStore);
        //}
        
        // 🎯 EXPLICIT STRATEGY SELECTION: Ensure correct processing approach
        if (IsHierarchicalEntity(entityType))
        {
            _logger.LogInformation("🏭 [STRATEGY-FACTORY-DEBUG] ✅ Creating V3 hierarchical strategy for {EntityType} - will use ID-based batching with full data caching", entityType);
            return new V3HierarchicalStrategy(_apiClient, _v3HierarchicalLogger);
        }
        else
        {
            _logger.LogInformation("🏭 [STRATEGY-FACTORY-DEBUG] ✅ Creating V3 efficient pagination strategy for {EntityType} - will use page-by-page processing", entityType);
            return new V3EfficientPaginationStrategy(_apiClient, _v3EfficientLogger);
        }
    }

    /// <summary>
    /// Determines if an entity type requires hierarchical processing
    /// </summary>
    /// <param name="entityType">The entity type to check</param>
    /// <returns>True if hierarchical, false otherwise</returns>
    private static bool IsHierarchicalEntity(string entityType)
    {
        // 🎯 EXPLICIT HIERARCHICAL ENTITIES: Only categories require hierarchical processing
        // All other entities (brands, products, etc.) use simple page-by-page processing
        var isHierarchical = entityType.Equals("categories", StringComparison.OrdinalIgnoreCase);
        
        return isHierarchical;
    }


} 
