using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Implementation of IEntityCreateService for creating entities in destination stores
/// Implements Open/Closed Principle: new entity types can be added without modifying this service
/// Uses Strategy Pattern to delegate entity creation to appropriate strategies
/// Phase 3.3.2: Enhanced with blob-based cooperative cancellation support
/// </summary>
public class EntityCreateService : IEntityCreateService
{
    private readonly IEntityCreationStrategyFactory _strategyFactory;
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<EntityCreateService> _logger;
    private readonly IEntityErrorHandlingService _errorHandlingService;

    public EntityCreateService(
        IEntityCreationStrategyFactory strategyFactory,
        ICancellationStore cancellationStore,
        ILogger<EntityCreateService> logger,
        IEntityErrorHandlingService errorHandlingService)
    {
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
    }

    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No entities provided for creation in migration {MigrationId}", request.MigrationId);
            return new List<Dictionary<string, object>>();
        }

        // ✅ Add detailed execution tracking to detect replays
        var executionId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("🔄 [EXEC-{ExecutionId}] Starting entity creation: {Count} {EntityType} entities for migration {MigrationId}", 
            executionId, entities.Count, request.EntityType, request.MigrationId);

        // Phase 3.3.2: Check for cancellation at start of entity creation
        await CheckCancellationAsync(request.MigrationId);

        try
        {
            // Phase 3.3.2: Check for cancellation before strategy execution
            await CheckCancellationAsync(request.MigrationId);

            // 🎯 STRATEGY PATTERN: Use factory to get appropriate strategy (replaces switch statement)
            var strategy = _strategyFactory.GetStrategy(request.EntityType);
            
            _logger.LogInformation("🔄 [EXEC-{ExecutionId}] Using {StrategyType} for {EntityType} in migration {MigrationId}", 
                executionId, strategy.GetType().Name, request.EntityType, request.MigrationId);

            // Delegate to strategy implementation
            // For product-images, we need to include source store information in the entities
            if (request.EntityType.Equals("product-images", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🔧 [ENTITY-CREATE-DEBUG] Adding source store info to {EntityCount} product-images entities. SourceStore: {StoreId} (migration: {MigrationId})", 
                    entities.Count, request.SourceStore?.StoreId ?? "NULL", request.MigrationId);
                
                // Add source store information to each entity for product-images processing
                foreach (var entity in entities)
                {
                    entity["_source_store_id"] = request.SourceStore?.StoreId ?? "";
                    entity["_source_store_token"] = request.SourceStore?.AccessToken ?? "";
                    entity["_source_store_channel_id"] = request.SourceStore?.ChannelId ?? "1"; // Default to "1" if not specified
                    
                    _logger.LogDebug("🔧 [ENTITY-CREATE-DEBUG] Entity keys after adding source store: [{Keys}] (migration: {MigrationId})", 
                        string.Join(", ", entity.Keys), request.MigrationId);
                }
                
                _logger.LogInformation("✅ [ENTITY-CREATE-DEBUG] Successfully added source store info to all {EntityCount} entities (migration: {MigrationId})", 
                    entities.Count, request.MigrationId);
            }
            
            var result = await strategy.CreateEntitiesAsync(
                entities, 
                request.MigrationId, 
                request.DestinationStore, 
                request.CategoryTreeContext, 
                cancellationToken);
            
            _logger.LogInformation("✅ [EXEC-{ExecutionId}] Completed entity creation: {EntityType} for migration {MigrationId}", 
                executionId, request.EntityType, request.MigrationId);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [EXEC-{ExecutionId}] Failed to create {EntityType} entities for migration {MigrationId}", 
                executionId, request.EntityType, request.MigrationId);
            
            // ✅ Store error details in the request context for later error logging
            // This preserves the original exception without throwing (prevents replay)
            request.AdditionalData["_api_error_message"] = ex.Message;
            request.AdditionalData["_api_exception"] = ex;
            request.AdditionalData["_api_stack_trace"] = ex.StackTrace ?? string.Empty;
            request.AdditionalData["_api_inner_exception"] = ex.InnerException?.Message ?? string.Empty;
            
            // Return empty list to prevent replay
            _logger.LogWarning("🔄 [EXEC-{ExecutionId}] Returning empty result instead of throwing exception to prevent replay", executionId);
            return new List<Dictionary<string, object>>();
        }
    }

    // 🎯 LEGACY METHODS REMOVED - All creation logic now handled by Strategy Pattern
    // - CreateCategoriesAsync() → CategoryCreationStrategy
    // - CreateProductsAsync() → ProductCreationStrategy  
    // - CreateBrandsAsync() → BrandCreationStrategy
    // - CreateVariantsAsync() → VariantCreationStrategy
    // - CreateImagesAsync() → ImageCreationStrategy
    // - CreateModifiersAsync() → ModifierCreationStrategy
    // 
    // Benefits of Strategy Pattern:
    // ✅ Open/Closed Principle: New entity types can be added without modifying this service
    // ✅ Single Responsibility: Each strategy handles one entity type
    // ✅ Testability: Each strategy can be tested independently
    // ✅ Maintainability: Entity-specific logic is encapsulated in dedicated classes

    /// <summary>
    /// Phase 3.3.2: Helper method to check for blob-based cancellation
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
                _logger.LogInformation("🚫 [CANCELLATION] Entity creation cancelled: {Reason}", reason);
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

    /// <summary>
    /// Validates store configuration
    /// </summary>
    /// <param name="storeConfig">Store configuration to validate</param>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    private static StoreConfiguration ValidateStoreConfiguration(StoreConfiguration? storeConfig)
    {
        if (storeConfig == null || !storeConfig.IsValid())
            throw new ArgumentException("Invalid destination store configuration");
        return storeConfig;
    }
} 