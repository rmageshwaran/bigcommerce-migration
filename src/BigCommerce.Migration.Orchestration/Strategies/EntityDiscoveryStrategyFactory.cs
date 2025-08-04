using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

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
    private readonly ILogger<LevelByLevelCategoryStrategy> _levelByLevelLogger;

    /// <summary>
    /// Initializes a new instance of EntityDiscoveryStrategyFactory
    /// </summary>
    /// <param name="apiClient">BigCommerce API client for version detection</param>
    /// <param name="logger">Logger for the factory</param>
    /// <param name="v2Logger">Logger for V2 strategy</param>
    /// <param name="v3EfficientLogger">Logger for V3 efficient strategy</param>
    /// <param name="v3HierarchicalLogger">Logger for V3 hierarchical strategy</param>
    /// <param name="levelByLevelLogger">Logger for Level-by-Level category strategy</param>
    public EntityDiscoveryStrategyFactory(
        IBigCommerceApiClient apiClient,
        ILogger<EntityDiscoveryStrategyFactory> logger,
        ILogger<V2DirectPaginationStrategy> v2Logger,
        ILogger<V3EfficientPaginationStrategy> v3EfficientLogger,
        ILogger<V3HierarchicalStrategy> v3HierarchicalLogger,
        ILogger<LevelByLevelCategoryStrategy> levelByLevelLogger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _v2Logger = v2Logger ?? throw new ArgumentNullException(nameof(v2Logger));
        _v3EfficientLogger = v3EfficientLogger ?? throw new ArgumentNullException(nameof(v3EfficientLogger));
        _v3HierarchicalLogger = v3HierarchicalLogger ?? throw new ArgumentNullException(nameof(v3HierarchicalLogger));
        _levelByLevelLogger = levelByLevelLogger ?? throw new ArgumentNullException(nameof(levelByLevelLogger));
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
            
            _logger.LogInformation("Detected API version {ApiVersion} for {EntityType} discovery", 
                apiVersion, entityType);

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
        // 🎯 ENHANCED STRATEGY SELECTION: Use optimized strategies based on entity characteristics
        if (entityType.Equals("categories", StringComparison.OrdinalIgnoreCase) || 
            entityType.Equals("categories-level", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("🚀 Creating Level-by-Level strategy for {EntityType} - will use level-by-level processing with same orchestrator workflow for each level", entityType);
            // ✅ PROPER FIX: Use properly injected logger following same pattern as other strategies
            return new LevelByLevelCategoryStrategy(_apiClient, _levelByLevelLogger);
        }
        else
        {
            _logger.LogInformation("📄 Creating V3 efficient pagination strategy for {EntityType} - will use page-by-page processing", entityType);
            return new V3EfficientPaginationStrategy(_apiClient, _v3EfficientLogger);
        }
    }

    /// <summary>
    /// Determines if an entity type requires hierarchical processing
    /// UPDATED: Categories now use page-based processing like other entities for better performance
    /// </summary>
    /// <param name="entityType">The entity type to check</param>
    /// <returns>True if hierarchical, false otherwise</returns>
    private static bool IsHierarchicalEntity(string entityType)
    {
        // 🚀 PERFORMANCE FIX: All entities now use page-based processing to avoid memory issues and race conditions
        // Categories will use Phase 1 (page-based bulk creation) + Phase 2 (parent relationship fixup)
        // This eliminates loading all entities into memory and prevents race conditions
        
        // No entities use hierarchical processing during discovery anymore
        return false;
        
        // OLD CODE (caused race conditions):
        // var isHierarchical = entityType.Equals("categories", StringComparison.OrdinalIgnoreCase);
        // return isHierarchical;
    }
} 