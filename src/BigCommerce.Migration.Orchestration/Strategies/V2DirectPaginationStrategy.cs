using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Discovery strategy for BigCommerce V2 APIs
/// Skips discovery phase and uses direct pagination during processing
/// Follows Single Responsibility Principle - handles only V2 API discovery logic
/// </summary>
public class V2DirectPaginationStrategy : IEntityDiscoveryStrategy
{
    private readonly ILogger<V2DirectPaginationStrategy> _logger;

    /// <summary>
    /// Gets the BigCommerce API version this strategy supports
    /// </summary>
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V2;

    /// <summary>
    /// Initializes a new instance of V2DirectPaginationStrategy
    /// </summary>
    /// <param name="logger">Logger for the strategy</param>
    public V2DirectPaginationStrategy(ILogger<V2DirectPaginationStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers entities using V2 API strategy - skips discovery phase
    /// V2 APIs will use direct pagination during processing phase instead
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result indicating to skip discovery</returns>
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // V2 APIs: Skip discovery phase completely
            // Processing will use direct pagination (page 1, 2, 3...) instead of pre-discovered entity IDs
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0, // Will be determined during processing
                EntityIds = new List<string>(), // Not needed for V2 - use direct pagination
                EntityData = new List<Dictionary<string, object>>(), // No data caching for V2
                ApiVersion = BigCommerceApiVersion.V2,
                SkipDiscovery = true,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V2" },
                    { "Strategy", "DirectPagination" },
                    { "SkipDiscovery", true },
                    { "UseDirectPagination", true },
                    { "MemoryOptimized", true }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "V2 direct pagination strategy failed for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { ex.Message },
                ApiVersion = BigCommerceApiVersion.V2
            };
        }
    }
} 