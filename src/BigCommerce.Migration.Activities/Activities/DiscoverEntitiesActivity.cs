using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Activities;

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
            
            _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Starting discovery for migration {MigrationId}, Entity Type: {EntityType}", 
                request.MigrationId, request.EntityType);
            
            var includeParam = request.EntityConfig?.Settings?.TryGetValue("include", out var includeValue) == true ? includeValue?.ToString() : null;
            _logger.LogDebug("🔍 [DISCOVERY-DEBUG] Request details - SourceStore: {StoreId}, Include: {Include}, EntityConfig: {EntityConfig}", 
                request.SourceStore?.StoreId, includeParam, request.EntityConfig != null ? $"ChunkSize:{request.EntityConfig.ChunkSize}, PageSize:{request.EntityConfig.PageSize}" : "null");

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
            _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Selecting discovery strategy for {EntityType}", request.EntityType);
            var strategy = await _strategyFactory.GetStrategyAsync(request.SourceStore, request.EntityType, cancellationToken);
            
            _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Using {StrategyType} for {EntityType} in migration {MigrationId}", 
                strategy.GetType().Name, request.EntityType, request.MigrationId);

            // Delegate to strategy implementation
            var result = await strategy.DiscoverEntitiesAsync(request, cancellationToken);

            _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Discovery completed for {EntityType}: {Count} entities found with {DataCount} entity data cached", 
                request.EntityType, result.TotalCount, result.EntityData.Count);
            
            _logger.LogDebug("🔍 [DISCOVERY-DEBUG] Discovery result details - IsSuccessful: {IsSuccessful}, Errors: [{Errors}], HasPaginationMetadata: {HasPaginationMetadata}", 
                result.IsSuccessful, string.Join(", ", result.Errors), result.PaginationMetadata != null);

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
            // 🚫 DISCOVERY CANCELLATION FIX: Check for cancellation messages in general exceptions
            bool isCancellationError = ex.Message.Contains("Migration cancelled", StringComparison.OrdinalIgnoreCase) ||
                                     ex.Message.Contains("User requested cancellation", StringComparison.OrdinalIgnoreCase);
            
            if (isCancellationError)
            {
                _logger.LogInformation("🚫 Entity discovery was cancelled for migration {MigrationId}, Entity Type: {EntityType}: {ErrorMessage}",
                    request.MigrationId, request.EntityType, ex.Message);
                
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    Errors = new List<string> { $"Discovery was cancelled: {ex.Message}" }
                };
            }
            else
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
    }

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
