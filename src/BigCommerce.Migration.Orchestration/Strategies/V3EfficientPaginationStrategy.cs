using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Discovery strategy for BigCommerce V3 APIs with non-hierarchical entities
/// Uses efficient pagination metadata approach without caching entity data
/// Optimized for large-scale entities like products, brands, variants
/// Follows Single Responsibility Principle - handles only V3 efficient pagination logic
/// </summary>
public class V3EfficientPaginationStrategy : IEntityDiscoveryStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<V3EfficientPaginationStrategy> _logger;

    /// <summary>
    /// Gets the BigCommerce API version this strategy supports
    /// </summary>
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V3;

    /// <summary>
    /// Initializes a new instance of V3EfficientPaginationStrategy
    /// </summary>
    /// <param name="apiClient">BigCommerce API client for metadata discovery</param>
    /// <param name="logger">Logger for the strategy</param>
    public V3EfficientPaginationStrategy(
        IBigCommerceApiClient apiClient,
        ILogger<V3EfficientPaginationStrategy> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers entities using V3 efficient pagination strategy
    /// Gets metadata from first page only to enable direct pagination during batch processing
    /// This strategy is memory-optimized and designed for non-hierarchical entities
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with pagination metadata only (no entity data cached)</returns>
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("🔍 V3 Efficient Discovery: Starting metadata-only discovery for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);

            // ✅ CORRECT DESIGN: Fetch ONLY first page to get total count and pagination metadata
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250, // Use large limit for efficiency, but only fetch first page
                IncludeDeleted = request.EntityConfig.IncludeDeleted,
                IncludeDrafts = request.EntityConfig.IncludeDrafts,
                SortBy = "id",
                SortDirection = "asc"
            };

            var response = await _apiClient.GetPaginatedEntitiesAsync(
                request.SourceStore,
                request.EntityType,
                paginationRequest,
                cancellationToken);

            // Calculate total entity count and pages based on pagination metadata
            var totalCount = response.TotalItems ?? 0;
            var totalPages = response.TotalPages ?? 1;
            var pageSize = response.PerPage;

            _logger.LogInformation("✅ V3 Discovery: Completed metadata discovery for {EntityType} - Found {TotalCount} entities across {TotalPages} pages", 
                request.EntityType, totalCount, totalPages);

            // ✅ MEMORY EFFICIENT: Return pagination metadata instead of all entity IDs
            // Batch processing will use page-based fetching during processing
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                EntityIds = new List<string>(), // ✅ Empty - will use pagination-based batching
                EntityData = new List<Dictionary<string, object>>(), // ✅ NO caching for scalability
                TotalCount = totalCount,
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = false,
                V3PaginationMetadata = response.Meta?.Pagination,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "Strategy", "EfficientPagination" },
                    { "TotalPages", totalPages },
                    { "PageSize", pageSize },
                    { "TotalCount", totalCount },
                    { "HierarchicallySorted", false },
                    { "CachingDisabled", true },
                    { "MemoryOptimized", true },
                    { "UseDirectPagination", true }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 V3 efficient pagination strategy failed for {EntityType} in migration {MigrationId}", 
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
} 