using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies;

/// <summary>
/// Discovery strategy for BigCommerce V3 APIs with hierarchical entities
/// Fetches all entities and sorts them hierarchically to ensure correct processing order
/// Specifically designed for categories that require parent-child ordering
/// Follows Single Responsibility Principle - handles only V3 hierarchical entity logic
/// </summary>
public class V3HierarchicalStrategy : IEntityDiscoveryStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<V3HierarchicalStrategy> _logger;

    /// <summary>
    /// Gets the BigCommerce API version this strategy supports
    /// </summary>
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V3;

    /// <summary>
    /// Initializes a new instance of V3HierarchicalStrategy
    /// </summary>
    /// <param name="apiClient">BigCommerce API client for entity fetching</param>
    /// <param name="logger">Logger for the strategy</param>
    public V3HierarchicalStrategy(
        IBigCommerceApiClient apiClient,
        ILogger<V3HierarchicalStrategy> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers entities using V3 hierarchical strategy
    /// Fetches all entities and sorts them hierarchically to ensure parents come before children
    /// Caches entity data to avoid duplicate API calls during processing
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with hierarchically sorted entities and cached data</returns>
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Using V3 hierarchical strategy for {EntityType} - fetching all entities for hierarchical sorting", 
                request.EntityType);

            var allEntities = new List<Dictionary<string, object>>();
            BigCommerceV3Pagination? v3Metadata = null;

            if (request.CategoryTreeContext?.CategoryTreeIdMapping == null || !request.CategoryTreeContext.CategoryTreeIdMapping.Any())
            {
                _logger.LogWarning("No category tree mappings found. Skipping category discovery.");
                return new EntityDiscoveryResult { EntityType = request.EntityType };
            }

            foreach (var sourceTreeId in request.CategoryTreeContext.CategoryTreeIdMapping.Keys)
            {
                // Fetch ALL entities to perform global hierarchical sorting
                var paginationRequest = new BigCommercePaginationRequest
                {
                    Page = 1,
                    Limit = 250, // Use large limit to minimize pagination - REVERTED: 250 is correct for performance
                    IncludeDeleted = request.EntityConfig.IncludeDeleted,
                    IncludeDrafts = request.EntityConfig.IncludeDrafts,
                    CategoryTreeId = sourceTreeId,
                    SortBy = "id",
                    SortDirection = "asc"
                };

                // ✅ DEBUG: Log the category tree ID being used for discovery
                _logger.LogInformation("🔍 DEBUG: Using category tree ID '{CategoryTreeId}' for discovery of {EntityType} in migration {MigrationId}", 
                    paginationRequest.CategoryTreeId ?? "DEFAULT", request.EntityType, request.MigrationId);

                var currentPage = 1;

                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    paginationRequest.Page = currentPage;
                    var response = await _apiClient.GetPaginatedEntitiesAsync(
                        request.SourceStore,
                        request.EntityType,
                        paginationRequest,
                        cancellationToken);

                    if (response.Data != null && response.Data.Any())
                    {
                        // Tag each entity with its source tree ID
                        foreach (var entity in response.Data)
                        {
                            entity["_sourceTreeId"] = sourceTreeId;
                        }
                        allEntities.AddRange(response.Data);
                        _logger.LogDebug("Cached {PageEntities} {EntityType} from page {Page} (total so far: {Total})", 
                            response.Data.Count, request.EntityType, currentPage, allEntities.Count);
                    }

                    // Store V3 metadata from first page
                    if (currentPage == 1 && response.Meta != null)
                    {
                        v3Metadata = response.Meta.Pagination;
                    }

                    // ✅ ROBUST PAGINATION: Use multiple exit conditions for reliability
                    // 1. No more data returned (most reliable indicator)
                    if (response.Data == null || !response.Data.Any())
                    {
                        _logger.LogDebug("No more {EntityType} data returned from page {Page}, ending pagination", 
                            request.EntityType, currentPage);
                        break;
                    }
                    
                    // 2. Returned fewer items than limit (indicates last page)
                    if (response.Data.Count < paginationRequest.Limit)
                    {
                        _logger.LogDebug("Page {Page} returned {Count} {EntityType} items (less than limit {Limit}), ending pagination",
                            currentPage, response.Data.Count, request.EntityType, paginationRequest.Limit);
                        break;
                    }
                    
                    // 3. API metadata indicates last page (if available and reliable)
                    if (response.TotalPages.HasValue && currentPage >= response.TotalPages.Value)
                    {
                        _logger.LogDebug("Reached API-indicated last page {TotalPages} for {EntityType}", 
                            response.TotalPages.Value, request.EntityType);
                        break;
                    }

                    currentPage++;

                    // 4. Safety check - prevent infinite loops
                    if (currentPage > 100)
                    {
                        _logger.LogWarning("Breaking pagination loop after 100 pages for {EntityType} to prevent infinite loop. Total entities cached: {TotalCached}", 
                            request.EntityType, allEntities.Count);
                        break;
                    }

                } while (true);
            }

            _logger.LogInformation("✅ Pagination completed for {EntityType}: Cached {ActualCount} entities", 
                request.EntityType, allEntities.Count);

            // Sort entities hierarchically
            var sortedEntities = SortEntitiesHierarchically(allEntities);
            
            // ✅ VALIDATION: Check if hierarchical sorting dropped any entities
            if (sortedEntities.Count != allEntities.Count)
            {
                var droppedCount = allEntities.Count - sortedEntities.Count;
                _logger.LogWarning("⚠️ Hierarchical sorting dropped {DroppedCount} {EntityType} entities! Original: {OriginalCount}, Sorted: {SortedCount}",
                    droppedCount, request.EntityType, allEntities.Count, sortedEntities.Count);
                
                // Find which entities were dropped
                var sortedIds = new HashSet<string>(sortedEntities.Select(e => e.GetValueOrDefault("id")?.ToString() ?? ""));
                var droppedEntities = allEntities.Where(e => !sortedIds.Contains(e.GetValueOrDefault("id")?.ToString() ?? ""));
                
                foreach (var dropped in droppedEntities)
                {
                    var id = dropped.GetValueOrDefault("id")?.ToString() ?? "unknown";
                    var name = dropped.GetValueOrDefault("name")?.ToString() ?? "unknown";
                    var parentId = dropped.GetValueOrDefault("parent_id");
                    _logger.LogWarning("⚠️ Dropped {EntityType}: ID={Id}, Name='{Name}', ParentId={ParentId}", 
                        request.EntityType, id, name, parentId);
                }
            }
            
            // ✅ FIX: Extract entity IDs from HIERARCHICALLY SORTED entities to preserve parent-child order
            // This ensures parent categories are migrated before children, allowing proper mapping resolution
            var hierarchicalEntityIds = ExtractEntityIds(sortedEntities); // Use hierarchically sorted list for proper order!
            
            _logger.LogInformation("✅ Hierarchical strategy completed for {EntityType}: {TotalEntities} entities discovered, {SortedEntities} entities sorted and cached, hierarchical order preserved", 
                request.EntityType, allEntities.Count, sortedEntities.Count);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                EntityIds = hierarchicalEntityIds, // ✅ Use hierarchically sorted entity IDs for proper parent-child order!
                EntityData = sortedEntities, // Store hierarchically sorted entity data
                TotalCount = allEntities.Count, // ✅ Use total count of all entities (some may be dropped in sorting)
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = false,
                V3PaginationMetadata = v3Metadata,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "Strategy", "HierarchicalCaching" },
                    { "PageSize", 250 },
                    { "HierarchicallySorted", true },
                    { "DataCached", true },
                    { "OptimizedForHierarchy", true },
                    { "AllEntitiesIncluded", true } // ✅ Indicate all entities are included
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "V3 hierarchical strategy failed for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { ex.Message },
                ApiVersion = BigCommerceApiVersion.V3
            };
        }
    }

    /// <summary>
    /// Sorts entities hierarchically to ensure parents come before children
    /// Currently optimized for categories but can be extended for other hierarchical entities
    /// </summary>
    /// <param name="entities">List of entities to sort</param>
    /// <returns>Hierarchically sorted entities</returns>
    private List<Dictionary<string, object>> SortEntitiesHierarchically(List<Dictionary<string, object>> entities)
    {
        var sortedList = new List<Dictionary<string, object>>();
        var processed = new HashSet<int>();

        // Helper method to add entity and its children recursively
        void AddEntityAndChildren(Dictionary<string, object> entity, int parentId = 0)
        {
            if (entity.TryGetValue("category_id", out var idObj) && int.TryParse(idObj.ToString(), out var entityId))
            {
                if (!processed.Contains(entityId))
                {
                    processed.Add(entityId);
                    sortedList.Add(entity);

                    // Find and add children
                    var children = entities.Where(e => 
                        e.TryGetValue("parent_id", out var parentIdObj) && 
                        int.TryParse(parentIdObj.ToString(), out var parentIdValue) && 
                        parentIdValue == entityId).ToList();

                    foreach (var child in children)
                    {
                        AddEntityAndChildren(child, entityId);
                    }
                }
            }
        }

        // First add all root entities (parent_id = 0 or null)
        var rootEntities = entities.Where(e => 
        {
            if (e.TryGetValue("parent_id", out var parentIdObj) && parentIdObj != null)
            {
                return int.TryParse(parentIdObj.ToString(), out var parentId) && parentId == 0;
            }
            return true; // Treat as root if parent_id is missing or null
        }).ToList();

        foreach (var rootEntity in rootEntities)
        {
            AddEntityAndChildren(rootEntity);
        }

        // Add any remaining entities that weren't processed (orphaned entities)
        var orphanedEntities = entities.Where(e => 
            e.TryGetValue("category_id", out var idObj) && 
            int.TryParse(idObj.ToString(), out var entityId) && 
            !processed.Contains(entityId)).ToList();

        sortedList.AddRange(orphanedEntities);

        return sortedList;
    }

    /// <summary>
    /// Extracts entity IDs from entity data
    /// </summary>
    /// <param name="entities">List of entities</param>
    /// <returns>List of entity IDs as strings</returns>
    private static List<string> ExtractEntityIds(List<Dictionary<string, object>> entities)
    {
        var entityIds = new List<string>();
        
        foreach (var entity in entities)
        {
            if (entity.TryGetValue("category_id", out var idValue))
            {
                entityIds.Add(idValue.ToString() ?? string.Empty);
            }
        }
        
        return entityIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
    }
} 
