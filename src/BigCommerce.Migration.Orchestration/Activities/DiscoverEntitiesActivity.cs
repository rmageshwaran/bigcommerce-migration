using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for discovering entities in BigCommerce stores
/// Implements Azure Durable Functions activity pattern
/// Now uses Strategy Pattern for different discovery approaches while maintaining backward compatibility
/// </summary>
public class DiscoverEntitiesActivity
{
    private readonly IEntityDiscoveryStrategyFactory _strategyFactory;
    private readonly ILogger<DiscoverEntitiesActivity> _logger;

    public DiscoverEntitiesActivity(IEntityDiscoveryStrategyFactory strategyFactory, ILogger<DiscoverEntitiesActivity> logger)
    {
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers entities of a specific type from source store
    /// Uses Strategy Pattern to delegate to appropriate discovery strategy based on API version and entity type
    /// Maintains backward compatibility with existing orchestrators and functions
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity discovery result</returns>
    [Function("DiscoverEntitiesActivity")]
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync([ActivityTrigger] EntityDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Discovering entities for migration {MigrationId}, Entity Type: {EntityType}", 
                request.MigrationId, request.EntityType);

            // Validate request
            var validationErrors = ValidateRequest(request);
            if (validationErrors.Any())
            {
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    Errors = validationErrors
                };
            }

            // STRATEGY PATTERN: Get appropriate strategy based on API version and entity type
            _logger.LogDebug("Selecting discovery strategy for {EntityType}", request.EntityType);
            var strategy = await _strategyFactory.GetStrategyAsync(request.SourceStore, request.EntityType, cancellationToken);
            
            _logger.LogInformation("Using {StrategyType} for {EntityType} in migration {MigrationId}", 
                strategy.GetType().Name, request.EntityType, request.MigrationId);

            // 🚨 CRITICAL DEBUG: About to call strategy.DiscoverEntitiesAsync
            _logger.LogError("🚨 [CRITICAL-ACTIVITY] About to call strategy.DiscoverEntitiesAsync on {StrategyType}!", strategy.GetType().Name);
            
            // Delegate to strategy implementation
            var result = await strategy.DiscoverEntitiesAsync(request, cancellationToken);
            
            // 🚨 CRITICAL DEBUG: Strategy call completed
            _logger.LogError("🚨 [CRITICAL-ACTIVITY] Strategy.DiscoverEntitiesAsync COMPLETED! TotalCount: {TotalCount}", result.TotalCount);

            _logger.LogInformation("Discovery completed for {EntityType}: {Count} entities found with {DataCount} entity data cached", 
                request.EntityType, result.TotalCount, result.EntityData.Count);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Entity discovery was cancelled for migration {MigrationId}, Entity Type: {EntityType}",
                request.MigrationId, request.EntityType);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { "Discovery was cancelled" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering entities for migration {MigrationId}, Entity Type: {EntityType}", 
                request.MigrationId, request.EntityType);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { ex.Message }
            };
        }
    }

    // All legacy discovery methods have been replaced by Strategy Pattern implementations
    // See: V2DirectPaginationStrategy, V3EfficientPaginationStrategy, V3HierarchicalStrategy

    /// <summary>
    /// Validates the entity discovery request
    /// </summary>
    private List<string> ValidateRequest(EntityDiscoveryRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(request.MigrationId))
        {
            errors.Add("MigrationId is required");
        }

        if (string.IsNullOrEmpty(request.EntityType))
        {
            errors.Add("EntityType is required");
        }

        if (request.SourceStore == null)
        {
            errors.Add("SourceStore is required");
        }
        else
        {
            if (string.IsNullOrEmpty(request.SourceStore.StoreId))
            {
                errors.Add("SourceStore.StoreId is required");
            }

            if (string.IsNullOrEmpty(request.SourceStore.AccessToken))
            {
                errors.Add("SourceStore.AccessToken is required");
            }
        }

        return errors;
    }
} 