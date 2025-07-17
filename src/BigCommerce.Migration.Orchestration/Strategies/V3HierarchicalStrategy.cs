using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

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

            // Fetch ALL entities to perform global hierarchical sorting
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250, // Use large limit to minimize pagination
                IncludeDeleted = request.EntityConfig.IncludeDeleted,
                IncludeDrafts = request.EntityConfig.IncludeDrafts,
                CategoryTreeId = request.CategoryTreeContext?.SourceCategoryTreeId,
                SortBy = "id",
                SortDirection = "asc"
            };

            var allEntities = new List<Dictionary<string, object>>();
            var currentPage = 1;
            BigCommerceV3Pagination? v3Metadata = null;

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
                    allEntities.AddRange(response.Data);
                    _logger.LogDebug("Cached {PageEntities} {EntityType} from page {Page}", 
                        response.Data.Count, request.EntityType, currentPage);
                }

                // Store V3 metadata from first page
                if (currentPage == 1 && response.Meta != null)
                {
                    v3Metadata = response.Meta.Pagination;
                }

                // Use V3 metadata for efficient pagination
                if (response.TotalPages.HasValue && currentPage >= response.TotalPages.Value)
                {
                    _logger.LogDebug("Reached last page {TotalPages} for {EntityType}", 
                        response.TotalPages.Value, request.EntityType);
                    break;
                }

                currentPage++;

                // Safety check - prevent infinite loops
                if (currentPage > 50)
                {
                    _logger.LogWarning("Breaking pagination loop after 50 pages for {EntityType} to prevent infinite loop", 
                        request.EntityType);
                    break;
                }

            } while (true);

            _logger.LogInformation("Pagination completed for {EntityType}: Cached {ActualCount} entities across {PagesProcessed} pages", 
                request.EntityType, allEntities.Count, currentPage);

            // Sort entities hierarchically
            var sortedEntities = SortEntitiesHierarchically(allEntities);
            
            // Extract entity IDs in hierarchical order
            var hierarchicalEntityIds = ExtractEntityIds(sortedEntities);

            _logger.LogInformation("Hierarchical sorting completed for {EntityType}: {EntityCount} entities sorted and cached", 
                request.EntityType, sortedEntities.Count);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                EntityIds = hierarchicalEntityIds,
                EntityData = sortedEntities, // Store hierarchically sorted entity data
                TotalCount = sortedEntities.Count,
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = false,
                V3PaginationMetadata = v3Metadata,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "Strategy", "HierarchicalCaching" },
                    { "TotalPages", currentPage },
                    { "PageSize", paginationRequest.Limit },
                    { "HierarchicallySorted", true },
                    { "DataCached", true },
                    { "OptimizedForHierarchy", true }
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
            if (entity.TryGetValue("id", out var idObj) && int.TryParse(idObj.ToString(), out var entityId))
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
            e.TryGetValue("id", out var idObj) && 
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
            if (entity.TryGetValue("id", out var idValue))
            {
                entityIds.Add(idValue.ToString() ?? string.Empty);
            }
        }
        
        return entityIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
    }
} 