using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// 🚀 LEVEL-BY-LEVEL: Discovery strategy for category entities using level-by-level processing
/// 
/// Each level uses the same EntityMigrationOrchestrator workflow:
/// Level 0: parent_id:in=0        → 50 categories → Store mappings
/// Level 1: parent_id:in=1,2,3... → 25 categories → Store mappings  
/// Level 2: parent_id:in=51,52... → 10 categories → Store mappings
/// Continue until no results
/// 
/// Benefits:
/// - Same workflow for every level (consistent batching, transformation, error handling)
/// - No custom Phase 2 service needed
/// - Proper batch size (25) for all levels
/// - Reuses existing EntityMigrationOrchestrator infrastructure
/// </summary>
public class LevelByLevelCategoryStrategy : IEntityDiscoveryStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<LevelByLevelCategoryStrategy> _logger;

    public string EntityType => "categories";
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V3;

    public LevelByLevelCategoryStrategy(IBigCommerceApiClient apiClient, ILogger<LevelByLevelCategoryStrategy> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 🚀 LEVEL-BY-LEVEL: Discovers categories for a specific level using parent_id filtering
    /// This strategy is called once per level, each time with different parent IDs
    /// </summary>
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        // 🚨 EXTREME DEBUG: Test if method is called at all - bypassing logger
        Console.WriteLine("🚨🚨🚨 [CONSOLE-TEST] LevelByLevelCategoryStrategy.DiscoverEntitiesAsync CALLED!");
        System.Diagnostics.Debug.WriteLine("🚨🚨🚨 [DEBUG-TEST] LevelByLevelCategoryStrategy.DiscoverEntitiesAsync CALLED!");
        
        // 🚨 CRITICAL DEBUG: Test if method is called at all
        _logger.LogError("🚨 [CRITICAL-TEST] LevelByLevelCategoryStrategy.DiscoverEntitiesAsync CALLED!");
        
        try
        {
            _logger.LogInformation("🔍 [LEVEL-DISCOVERY] Starting category discovery for migration {MigrationId}", request.MigrationId);

            // Extract level information from request (passed by orchestrator)
                    // ✅ SIMPLIFIED: Use typed properties instead of Dictionary<string, object> parsing
        var level = request.EntityConfig?.Level ?? 0;
        var parentIds = request.EntityConfig?.ParentIds;
            
            _logger.LogInformation("🔍 [LEVEL-DISCOVERY] Starting discovery for migration {MigrationId}, EntityType: {EntityType}, Level: {Level}", 
                request.MigrationId, request.EntityType, level);
            _logger.LogInformation("🔍 [LEVEL-DISCOVERY] Processing Level {Level} with {ParentCount} parent IDs", 
                level, parentIds?.Count ?? 0);
            
            if (parentIds?.Count > 0)
            {
                _logger.LogDebug("🔍 [LEVEL-DISCOVERY] Parent IDs for Level {Level}: {ParentIds}", 
                    level, string.Join(",", parentIds.Take(10)) + (parentIds.Count > 10 ? "..." : ""));
            }

            // Build API filter based on level
            string apiFilter;
            if (level == 0)
            {
                // Level 0: Root categories
                apiFilter = "parent_id:in=0";
            }
            else
            {
                // Level N: Children of previous level
                if (parentIds == null || !parentIds.Any())
                {
                    _logger.LogInformation("✅ [LEVEL-DISCOVERY] No parent IDs for Level {Level} - migration complete", level);
                    return CreateEmptyResult();
                }
                
                apiFilter = $"parent_id:in={string.Join(",", parentIds)}";
            }

            _logger.LogInformation("🔍 [LEVEL-DISCOVERY] Level {Level} API filter: {ApiFilter}", level, apiFilter);

            // Get total count for this level
            var totalCount = await GetCategoryCountForLevel(request.SourceStore, apiFilter, request.CategoryTreeContext, cancellationToken);
            
            if (totalCount == 0)
            {
                _logger.LogInformation("✅ [LEVEL-DISCOVERY] Level {Level} has 0 categories - migration complete", level);
                return CreateEmptyResult();
            }

            _logger.LogInformation("📊 [LEVEL-DISCOVERY] Level {Level} discovered {TotalCount} categories", level, totalCount);

            // Create placeholder entity IDs for orchestrator chunking (same as current Two-Phase approach)
            var placeholderEntityIds = Enumerable.Range(1, totalCount)
                .Select(i => $"level-{level}-{i}")
                .ToList();

            // 🚀 CRITICAL: Prevent infinite loops by using different strategy for sub-orchestrators
            var strategyName = request.EntityType.Equals("categories-level", StringComparison.OrdinalIgnoreCase) 
                ? "EfficientPagination"  // Sub-orchestrators use normal chunking
                : "LevelByLevel";        // Main orchestrator uses Level-by-Level

            return new EntityDiscoveryResult
            {
                EntityType = "categories",
                EntityIds = placeholderEntityIds, // Placeholder IDs for orchestrator chunking  
                TotalCount = totalCount,
                EntityData = new List<Dictionary<string, object>>(), // Empty - using API filtering, not cached data
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "Strategy", strategyName },
                    { "Level", level },
                    { "UseApiFiltering", true },
                    { "ApiFilter", apiFilter },
                    { "ParentIds", parentIds ?? new List<string>() },
                    { "ProcessingMode", strategyName }
                },
                Errors = new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [LEVEL-DISCOVERY] Discovery failed for migration {MigrationId}: {ErrorMessage}", 
                request.MigrationId, ex.Message);
            
            return new EntityDiscoveryResult
            {
                EntityType = "categories",
                EntityIds = new List<string>(),
                TotalCount = 0,
                EntityData = new List<Dictionary<string, object>>(),
                PaginationMetadata = new Dictionary<string, object>(),
                Errors = new List<string> { $"Level discovery failed: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Get category count for a specific level using API filtering
    /// </summary>
    private async Task<int> GetCategoryCountForLevel(
        StoreConfiguration sourceStore,
        string apiFilter,
        CategoryTreeContext? categoryTreeContext,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("🔍 [LEVEL-COUNT] Getting count for API filter: {ApiFilter}", apiFilter);
            
            // Extract parent IDs from the apiFilter
            var parentIdsValue = ExtractParentIds(apiFilter);
            _logger.LogDebug("🔍 [LEVEL-COUNT] Extracted parent IDs: {ParentIds}", parentIdsValue);
            
            // Get first page to check total count
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 1, // Minimal limit, we just need the count
                CategoryTreeId = categoryTreeContext?.SourceCategoryTreeId,
                AdditionalParams = new Dictionary<string, string> { { "parent_id:in", parentIdsValue } }
            };

            _logger.LogDebug("🔍 [LEVEL-COUNT] Making API call with CategoryTreeId: {TreeId}, parent_id:in: {ParentIds}", 
                categoryTreeContext?.SourceCategoryTreeId, parentIdsValue);

            var response = await _apiClient.GetPaginatedEntitiesAsync(
                sourceStore, 
                "categories", 
                paginationRequest, 
                cancellationToken);

            var totalCount = response?.Meta?.Pagination?.Total ?? 0;
            _logger.LogInformation("🔍 [LEVEL-COUNT] API filter '{ApiFilter}' returned {TotalCount} categories", apiFilter, totalCount);
            return totalCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [LEVEL-COUNT] Failed to get category count for filter '{ApiFilter}': {ErrorMessage}", apiFilter, ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// Extract parent IDs from API filter string
    /// </summary>
    private string ExtractParentIds(string apiFilter)
    {
        // Extract "1,2,3" from "parent_id:in=1,2,3"
        var equalIndex = apiFilter.IndexOf('=');
        return equalIndex >= 0 ? apiFilter.Substring(equalIndex + 1) : "0";
    }



    /// <summary>
    /// Create empty result when no more categories found
    /// </summary>
    private EntityDiscoveryResult CreateEmptyResult()
    {
        return new EntityDiscoveryResult
        {
            EntityType = "categories",
            EntityIds = new List<string>(),
            TotalCount = 0,
            EntityData = new List<Dictionary<string, object>>(),
            PaginationMetadata = new Dictionary<string, object>
            {
                { "Strategy", "LevelByLevel" },
                { "ProcessingMode", "Complete" }
            },
            Errors = new List<string>()
        };
    }
}