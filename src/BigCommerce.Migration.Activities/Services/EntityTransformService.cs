using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Implementation of IEntityTransformService for transforming entities between source and destination formats
/// Implements Open/Closed Principle: new entity types can be added without modifying this service
/// Uses Strategy Pattern to delegate entity transformation to appropriate strategies
/// Phase 3.3.1: Enhanced with blob-based cooperative cancellation support
/// </summary>
public class EntityTransformService : IEntityTransformService
{
    private readonly IEntityTransformStrategyFactory _strategyFactory;
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<EntityTransformService> _logger;

    public EntityTransformService(
        IEntityTransformStrategyFactory strategyFactory,
        ICancellationStore cancellationStore,
        ILogger<EntityTransformService> logger)
    {
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
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

            // Phase 3.3.1: Check for cancellation at start of transformation
            await CheckCancellationAsync(request.MigrationId);

            // Create a copy to avoid modifying the original entity
            var transformedEntity = new Dictionary<string, object>(sourceEntity);

            // 🔗 HIERARCHICAL MAPPING: Preserve source entity ID for options before removal
            if (request.EntityType == "options" && transformedEntity.TryGetValue("id", out var sourceId))
            {
                transformedEntity["_source_option_id"] = sourceId;
                _logger.LogInformation("🔗 [ENTITY-TRANSFORM] Preserved source option ID for hierarchical mapping: {SourceOptionId}", sourceId);
            }

            // Common cleanup - remove BigCommerce system fields that shouldn't be migrated
            RemoveSystemFields(transformedEntity);

            // Phase 3.3.1: Check for cancellation before strategy execution
            await CheckCancellationAsync(request.MigrationId);

            // 🖼️ PRODUCT-IMAGES OPTIMIZATION: Skip transform for EntityMappings data
            if (request.EntityType.Equals("product-images", StringComparison.OrdinalIgnoreCase))
            {
                // For product-images, EntityMappings data should go directly to Creation strategy
                // The Creation strategy will fetch images and handle transformation internally
                _logger.LogInformation("🖼️ [PRODUCT-IMAGES-TRANSFORM] Skipping transform for EntityMappings data - passing directly to Creation strategy (ExecutionId: {ExecutionId})", 
                    executionId);
                
                return transformedEntity; // Return EntityMappings data unchanged
            }

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

    /// <summary>
    /// Phase 3.3.1: Helper method to check for blob-based cancellation
    /// Uses cooperative cancellation pattern suitable for Azure Functions activities
    /// </summary>
    private async Task CheckCancellationAsync(string migrationId)
    {
        try
        {
            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            if (isCancelled)
            {
                var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                _logger.LogInformation("🚫 [CANCELLATION] Entity transformation cancelled: {Reason}", reason);
                throw new OperationCanceledException($"Migration cancelled: {reason}");
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CANCELLATION-CHECK] Failed to check cancellation flag for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't throw - continue processing if cancellation check fails
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