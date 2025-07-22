using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Implementation of IEntityTransformService for transforming entities between source and destination formats
/// Implements Open/Closed Principle: new entity types can be added without modifying this service
/// Uses Strategy Pattern to delegate entity transformation to appropriate strategies
/// </summary>
public class EntityTransformService : IEntityTransformService
{
    private readonly IEntityTransformStrategyFactory _strategyFactory;
    private readonly ILogger<EntityTransformService> _logger;

    public EntityTransformService(
        IEntityTransformStrategyFactory strategyFactory,
        ILogger<EntityTransformService> logger)
    {
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> sourceEntity, 
        BatchProcessingRequest request)
    {
        if (sourceEntity == null)
            throw new ArgumentNullException(nameof(sourceEntity));
        
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            // ✅ Add detailed execution tracking to detect replays
            var executionId = Guid.NewGuid().ToString("N")[..8];
            _logger.LogInformation("🔄 [EXEC-{ExecutionId}] Starting entity transformation: {EntityType} for migration {MigrationId}", 
                executionId, request.EntityType, request.MigrationId);

            // Create a copy to avoid modifying the original entity
            var transformedEntity = new Dictionary<string, object>(sourceEntity);

            // Common cleanup - remove BigCommerce system fields that shouldn't be migrated
            RemoveSystemFields(transformedEntity);

            // 🎯 STRATEGY PATTERN: Use factory to get appropriate strategy (replaces switch statement)
            var strategy = _strategyFactory.GetStrategy(request.EntityType);
            
            _logger.LogDebug("🔄 [EXEC-{ExecutionId}] Delegating to {StrategyType} for {EntityType} transformation", 
                executionId, strategy.GetType().Name, request.EntityType);

            // Delegate to strategy - map BatchProcessingRequest to strategy parameters
            var result = await strategy.TransformEntityAsync(
                transformedEntity,
                request.MigrationId,
                request.SourceStore,
                request.DestinationStore,
                request.CategoryTreeContext,
                CancellationToken.None);

            _logger.LogInformation("✅ [EXEC-{ExecutionId}] Successfully transformed {EntityType} entity for migration {MigrationId}", 
                executionId, request.EntityType, request.MigrationId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to transform {EntityType} entity for migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            throw new InvalidOperationException($"Entity transformation failed for {request.EntityType}", ex);
        }
    }

    private static void RemoveSystemFields(Dictionary<string, object> entity)
    {
        // Remove BigCommerce system fields that shouldn't be migrated
        entity.Remove("id"); // Source entity ID
        entity.Remove("date_created");
        entity.Remove("date_modified");
        entity.Remove("_links"); // Remove BigCommerce API links
        entity.Remove("_meta"); // Remove BigCommerce API metadata
        entity.Remove("_original_entity_id"); // Remove internal field used for error logging
    }
} 