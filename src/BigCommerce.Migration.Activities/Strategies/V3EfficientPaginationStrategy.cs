using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System;

namespace BigCommerce.Migration.Activities.Strategies;

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

            _logger.LogInformation("🔍 [V3-EFFICIENT-DEBUG] Starting metadata-only discovery for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            
            var includeParam = request.EntityConfig?.Settings?.TryGetValue("include", out var includeValue) == true ? includeValue?.ToString() : null;
            _logger.LogDebug("🔍 [V3-EFFICIENT-DEBUG] Request details - EntityConfig.Include: '{Include}', EntityConfig.PageSize: {PageSize}, SourceStore: {StoreId}", 
                includeParam, request.EntityConfig?.PageSize, request.SourceStore?.StoreId);
            
            _logger.LogInformation("🚀 [PAGINATION-OPTIMIZED] Using for {EntityType} to match configured pageSize (optimized for API efficiency)", 
                request.EntityType);
            
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 1,
                SortBy = "id",
                SortDirection = "asc",
                Include = DetermineIncludeParameter(request.EntityType, includeParam)
            };

            var entityType = DetermineEntityType(request.EntityType);

            _logger.LogInformation("🔍 [V3-EFFICIENT-DEBUG] Making API call with Include='{Include}', Limit={Limit}, entityType={entityType}", 
                paginationRequest.Include, paginationRequest.Limit, entityType);

            var response = await _apiClient.GetPaginatedEntitiesAsync(
                request.SourceStore!,
                entityType,
                paginationRequest,
                cancellationToken);
                
            _logger.LogInformation("🔍 [V3-EFFICIENT-DEBUG] API response received - TotalItems: {TotalItems}, TotalPages: {TotalPages}, PerPage: {PerPage}, Data.Count: {DataCount}", 
                response.TotalItems, response.TotalPages, response.PerPage, response.Data?.Count);

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
                //EntityIds = new List<string>(), // ✅ Empty - will use pagination-based batching
                //EntityData = new List<Dictionary<string, object>>(), // ✅ NO caching for scalability
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
            // ✅ P0-T2: Enhanced API error logging with request/response payload logging
            _logger.LogError(ex, "🔥 [V3-PAGINATION-API-ERROR] V3 efficient pagination strategy failed for {EntityType} in migration {MigrationId}. " +
                            "Store: {StoreId}, PageSize: {PageSize}, " +
                            "ErrorType: {ErrorType}, Category: Error",
                request.EntityType, request.MigrationId, request.SourceStore.StoreId, 
                request.EntityConfig.PageSize, ex.GetType().Name);

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
    /// Determine the actual entity type for parent entity components
    /// </summary>
    /// <param name="entityType"></param>
    /// <returns>parent entity type</returns>
    private string DetermineEntityType(string entityType)
    {
        var result = entityType.ToLowerInvariant() switch { "product-components" => "products", _ => entityType };
        return result;
    }

    /// <summary>
    /// Determines the appropriate include parameter based on entity type and configuration
    /// </summary>
    /// <param name="entityType">The entity type being discovered</param>
    /// <param name="configuredInclude">The include parameter from entity configuration</param>
    /// <returns>The include parameter to use for the API call</returns>
    private string? DetermineIncludeParameter(string entityType, string? configuredInclude)
    {
        // Log the determination process
        _logger.LogDebug("🔍 [V3-EFFICIENT-DEBUG] Determining include parameter for EntityType='{EntityType}', ConfiguredInclude='{ConfiguredInclude}'", 
            entityType, configuredInclude);

        var result = entityType.ToLowerInvariant() switch
        {
            "products" => "bulk_pricing_rules,custom_fields,channels,videos", // Default for products
            "product-components" => configuredInclude ?? "options,modifiers,reviews", // Use configured or default for components
            "product-variants" => null, // Variants don't support include parameters
            "product-related" => configuredInclude,
            "product-metafields" => configuredInclude,
            "product-channels" => configuredInclude,
            _ => configuredInclude // Use configured value for other entity types
        };

        _logger.LogInformation("🔍 [V3-EFFICIENT-DEBUG] ✅ Include parameter determined: EntityType='{EntityType}' → Include='{Include}'", 
            entityType, result);

        return result;
    }
} 
