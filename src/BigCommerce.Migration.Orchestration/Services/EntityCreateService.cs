using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Implementation of IEntityCreateService for creating entities in destination stores
/// Implements Open/Closed Principle: new entity types can be added without modifying this service
/// Uses Strategy Pattern to delegate entity creation to appropriate strategies
/// </summary>
public class EntityCreateService : IEntityCreateService
{
    private readonly IEntityCreationStrategyFactory _strategyFactory;
    private readonly ILogger<EntityCreateService> _logger;
    private readonly IEntityErrorHandlingService _errorHandlingService;

    public EntityCreateService(
        IEntityCreationStrategyFactory strategyFactory,
        ILogger<EntityCreateService> logger,
        IEntityErrorHandlingService errorHandlingService)
    {
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
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

        try
        {
            // 🎯 STRATEGY PATTERN: Use factory to get appropriate strategy (replaces switch statement)
            _logger.LogDebug("🔄 [EXEC-{ExecutionId}] Selecting creation strategy for {EntityType}", executionId, request.EntityType);
            var strategy = _strategyFactory.GetStrategy(request.EntityType);
            
            _logger.LogInformation("🔄 [EXEC-{ExecutionId}] Using {StrategyType} for {EntityType} in migration {MigrationId}", 
                executionId, strategy.GetType().Name, request.EntityType, request.MigrationId);

            // Delegate to strategy implementation
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