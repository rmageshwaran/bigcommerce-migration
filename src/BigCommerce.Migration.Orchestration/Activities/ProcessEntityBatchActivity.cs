using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using System.Diagnostics;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Refactored activity function for processing entity batches following SOLID principles
/// Single Responsibility: Batch processing coordination only
/// 
/// SOLID Principles Applied:
/// - Single Responsibility: Only coordinates batch processing workflow
/// - Open/Closed: Extensible through service interfaces
/// - Liskov Substitution: All services implement interfaces
/// - Interface Segregation: Specific interfaces for each responsibility
/// - Dependency Inversion: Depends on abstractions, not concretions
/// </summary>
public class ProcessEntityBatchActivity
{
    private readonly ILogger<ProcessEntityBatchActivity> _logger;
    private readonly IEntityFetchService _entityFetchService;
    private readonly IEntityTransformService _entityTransformService;
    private readonly IEntityCreateService _entityCreateService;
    private readonly IEntityMappingService _entityMappingService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly IRateLimitService _rateLimitService;
    private readonly IOpenSearchService _openSearchService;
    private readonly IMigrationStorageService _migrationStorageService;
    // TODO: Replace with queue-based progress broadcasting
    // Removed IMigrationSignalRService dependency
    private readonly IErrorMessageFormatter _errorMessageFormatter;

    public ProcessEntityBatchActivity(
        ILogger<ProcessEntityBatchActivity> logger,
        IEntityFetchService entityFetchService,
        IEntityTransformService entityTransformService,
        IEntityCreateService entityCreateService,
        IEntityMappingService entityMappingService,
        IEntityErrorHandlingService errorHandlingService,
        IRateLimitService rateLimitService,
        IOpenSearchService openSearchService,
        IMigrationStorageService migrationStorageService,

        IErrorMessageFormatter errorMessageFormatter)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityFetchService = entityFetchService ?? throw new ArgumentNullException(nameof(entityFetchService));
        _entityTransformService = entityTransformService ?? throw new ArgumentNullException(nameof(entityTransformService));
        _entityCreateService = entityCreateService ?? throw new ArgumentNullException(nameof(entityCreateService));
        _entityMappingService = entityMappingService ?? throw new ArgumentNullException(nameof(entityMappingService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));

        _errorMessageFormatter = errorMessageFormatter ?? throw new ArgumentNullException(nameof(errorMessageFormatter));
    }

    /// <summary>
    /// Main batch processing coordination method
    /// Single Responsibility: Orchestrates the batch processing workflow
    /// </summary>
    [Function("ProcessEntityBatchActivity")]
    public async Task<BatchProcessingResult> Run(
        [ActivityTrigger] BatchProcessingRequest request, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // ✅ DEBUG: Log what we're receiving at the activity level
        _logger.LogDebug("ProcessEntityBatchActivity - EntityType: {EntityType}, BatchNumber: {BatchNumber}, EntityIds: [{EntityIds}], CachedDataCount: {CachedDataCount}",
            request.EntityType, request.BatchNumber, string.Join(", ", request.EntityIds ?? new List<string>()), request.CachedEntityData?.Count ?? 0);
        
        var result = new BatchProcessingResult
        {
            BatchNumber = request.BatchNumber,
            TotalProcessed = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            EntityMappings = new List<EntityMapping>(),
            Errors = new List<string>()
        };

        try
        {
            _logger.LogInformation("Starting batch processing for {EntityType} batch {BatchNumber}/{TotalBatches} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.TotalBatches, request.MigrationId);

            // Step 1: Validate request
            var validationErrors = ValidateRequest(request);
            if (validationErrors.Any())
            {
                result.Errors.AddRange(validationErrors);
                return CompleteBatch(result, stopwatch);
            }

            // Step 2: Handle empty batch
            if (!request.EntityIds.Any())
            {
                _logger.LogInformation("Empty batch for {EntityType} batch {BatchNumber} - skipping processing",
                    request.EntityType, request.BatchNumber);
                return CompleteBatch(result, stopwatch);
            }

            // Step 3: Check for cancellation
            if (await IsMigrationCancelledAsync(request.MigrationId))
            {
                result.Errors.Add("Migration was cancelled before processing");
                return CompleteBatch(result, stopwatch);
            }

            // Step 4: Fetch source entities
            var sourceEntities = await FetchSourceEntitiesWithErrorHandlingAsync(request, result, cancellationToken);
            if (sourceEntities == null)
            {
                return CompleteBatch(result, stopwatch);
            }

            // Step 5: Process entities individually (continue on failures)
            await ProcessEntitiesIndividuallyAsync(request, sourceEntities, result, cancellationToken);

            // Step 6: Log batch completion
            await LogBatchCompletionAsync(request, result, cancellationToken);

            return CompleteBatch(result, stopwatch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing batch {BatchNumber} for {EntityType} in migration {MigrationId}",
                request.BatchNumber, request.EntityType, request.MigrationId);
            
            result.Errors.Add($"Batch processing failed: {ex.Message}");
            return CompleteBatch(result, stopwatch);
        }
    }

    /// <summary>
    /// Validates the batch processing request
    /// Single Responsibility: Request validation only
    /// </summary>
    private static List<string> ValidateRequest(BatchProcessingRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.MigrationId))
            errors.Add("MigrationId is required");

        if (string.IsNullOrWhiteSpace(request.EntityType))
            errors.Add("EntityType is required");

        if (request.BatchNumber < 1)
            errors.Add("BatchNumber must be greater than 0");

        if (request.TotalBatches < 1)
            errors.Add("TotalBatches must be greater than 0");

        if (request.SourceStore == null || !request.SourceStore.IsValid())
            errors.Add("Valid SourceStore is required");

        if (request.DestinationStore == null || !request.DestinationStore.IsValid())
            errors.Add("Valid DestinationStore is required");

        return errors;
    }

    /// <summary>
    /// Checks if migration is cancelled
    /// Single Responsibility: Cancellation checking only
    /// </summary>
    private async Task<bool> IsMigrationCancelledAsync(string migrationId)
    {
        try
        {
            var cancellationToken = await _migrationStorageService.GetCancellationTokenAsync(migrationId);
            return cancellationToken != null && !cancellationToken.IsProcessed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check migration cancellation status for {MigrationId}", migrationId);
            return false; // Continue processing if cancellation check fails
        }
    }

    /// <summary>
    /// Fetches source entities with error handling
    /// Single Responsibility: Entity fetching coordination only
    /// </summary>
    private async Task<List<Dictionary<string, object>>?> FetchSourceEntitiesWithErrorHandlingAsync(
        BatchProcessingRequest request, 
        BatchProcessingResult result, 
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Apply rate limiting before fetching
            await ApplyRateLimitingAsync(request.SourceStore?.StoreId);
            
            return await _entityFetchService.FetchEntitiesAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Migration batch {BatchNumber} for {EntityType} was cancelled",
                request.BatchNumber, request.EntityType);
            result.Errors.Add("Migration was cancelled");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch source entities for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
            
            await _errorHandlingService.LogStructuredMigrationErrorAsync(
                ex, new List<Dictionary<string, object>>(), request, "fetch", cancellationToken);
            
            result.Errors.Add($"Failed to fetch source entities: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Processes entities individually with continue-on-failure
    /// Single Responsibility: Entity processing coordination only
    /// </summary>
    private async Task ProcessEntitiesIndividuallyAsync(
        BatchProcessingRequest request, 
        List<Dictionary<string, object>> sourceEntities, 
        BatchProcessingResult result,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < sourceEntities.Count; i++)
        {
            // Check for cancellation
            cancellationToken.ThrowIfCancellationRequested();
            
            if (await IsMigrationCancelledAsync(request.MigrationId))
            {
                _logger.LogInformation("Migration {MigrationId} was cancelled during batch processing - stopping at entity {EntityIndex}/{TotalEntities}", 
                    request.MigrationId, i + 1, sourceEntities.Count);
                result.Errors.Add($"Migration was cancelled during batch processing at entity {i + 1}/{sourceEntities.Count}");
                return;
            }
            
            var entity = sourceEntities[i];
            result.TotalProcessed++;
            
            try
            {
                // Apply rate limiting
                await ApplyRateLimitingAsync(request.DestinationStore.StoreId!);

                // ✅ Extract entity ID BEFORE transformation (ID gets removed during transformation)
                var originalEntityId = ExtractEntityId(entity, request.EntityType);

                // Transform entity
                var transformedEntity = await _entityTransformService.TransformEntityAsync(entity, request);

                // ✅ Store the original entity ID in the transformed entity for error logging
                // This allows EntityCreateService to access the correct ID for error logging
                transformedEntity["_original_entity_id"] = originalEntityId;

                // Create entity in destination
                var createdEntities = await _entityCreateService.CreateEntitiesAsync(
                    new List<Dictionary<string, object>> { transformedEntity }, 
                    request, 
                    cancellationToken);

                if (createdEntities != null && createdEntities.Any())
                {
                    result.SuccessfulEntities++;
                    
                    // Create and store entity mapping
                    var createdEntity = createdEntities.First();
                    
                    // Log the created entity structure for troubleshooting (debug level)
                    _logger.LogDebug("Created entity from BigCommerce API: {CreatedEntity} for {EntityType} in migration {MigrationId}",
                        System.Text.Json.JsonSerializer.Serialize(createdEntity), request.EntityType, request.MigrationId);
                    
                    var mapping = _entityMappingService.CreateEntityMapping(entity, createdEntity, request);
                    result.EntityMappings.Add(mapping);

                    // Store mapping immediately for hierarchy relationships
                    await _entityMappingService.StoreEntityMappingAsync(mapping, cancellationToken);

                    _logger.LogDebug("Successfully processed {EntityType} {EntityId} in migration {MigrationId}",
                        request.EntityType, originalEntityId, request.MigrationId);
                }
                else
                {
                    result.FailedEntities++;
                    
                    // ✅ Check for preserved API error details from EntityCreateService
                    var apiErrorMessage = request.AdditionalData.TryGetValue("_api_error_message", out var errorMsg) ? errorMsg?.ToString() : null;
                    var apiStackTrace = request.AdditionalData.TryGetValue("_api_stack_trace", out var stackTrace) ? stackTrace?.ToString() : null;
                    var apiInnerException = request.AdditionalData.TryGetValue("_api_inner_exception", out var innerEx) ? innerEx?.ToString() : null;
                    var apiResponsePayload = request.AdditionalData.TryGetValue("_api_response_payload", out var responsePayload) ? responsePayload?.ToString() : null;
                    
                    // ✅ Create simple, user-friendly error message for main display (SOLID: Single Responsibility)
                    var simpleErrorMessage = _errorMessageFormatter.CreateSimpleErrorMessage(request.EntityType, originalEntityId, apiErrorMessage);
                    result.Errors.Add(simpleErrorMessage);
                    _logger.LogWarning(simpleErrorMessage);
                    
                    // ✅ Create enhanced exception with preserved API error details
                    var entityErrorException = new InvalidOperationException(simpleErrorMessage);
                    
                    // If we have preserved API error details, create a more detailed exception
                    if (!string.IsNullOrEmpty(apiErrorMessage))
                    {
                        var detailedException = new InvalidOperationException(
                            $"API Error creating {request.EntityType} {originalEntityId}: {apiErrorMessage}", 
                            entityErrorException);
                        
                        // Add stack trace and inner exception details if available
                        if (!string.IsNullOrEmpty(apiStackTrace))
                        {
                            detailedException.Data["StackTrace"] = apiStackTrace;
                        }
                        if (!string.IsNullOrEmpty(apiInnerException))
                        {
                            detailedException.Data["InnerException"] = apiInnerException;
                        }
                        
                        entityErrorException = detailedException;
                    }
                    
                    await _errorHandlingService.LogEntityErrorAsync(
                        entityErrorException, entity, request, originalEntityId, apiResponsePayload, simpleErrorMessage, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                result.FailedEntities++;
                
                // ✅ Extract entity ID from ORIGINAL entity before transformation
                var originalEntityId = ExtractEntityId(entity, request.EntityType);
                
                await _errorHandlingService.LogEntityErrorAsync(
                    ex, entity, request, originalEntityId, cancellationToken);
                
                var error = $"Failed to process {request.EntityType} {originalEntityId}: {ex.Message}";
                result.Errors.Add(error);
                _logger.LogWarning(ex, error);
                
                // Continue processing other entities (continue-on-failure requirement)
            }
        }
    }

    /// <summary>
    /// Applies rate limiting
    /// Single Responsibility: Rate limiting coordination only
    /// </summary>
    private async Task ApplyRateLimitingAsync(string? storeId)
    {
        if (string.IsNullOrEmpty(storeId)) return;
        
        var rateLimitResult = await _rateLimitService.CheckRateLimitAsync(storeId);
        
        if (rateLimitResult != null && !rateLimitResult.CanProceed && rateLimitResult.DelayMs > 0)
        {
            _logger.LogDebug("Rate limit delay: {DelayMs}ms for store {StoreId}", 
                rateLimitResult.DelayMs, storeId);
            
            await Task.Delay(rateLimitResult.DelayMs);
        }
    }

    /// <summary>
    /// Logs batch completion
    /// Single Responsibility: Logging coordination only
    /// </summary>
    private async Task LogBatchCompletionAsync(
        BatchProcessingRequest request, 
        BatchProcessingResult result, 
        CancellationToken cancellationToken)
    {
        try
        {
            var logData = new
            {
                migrationId = request.MigrationId,
                entityType = request.EntityType,
                batchNumber = request.BatchNumber,
                totalBatches = request.TotalBatches,
                totalProcessed = result.TotalProcessed,
                successfulEntities = result.SuccessfulEntities,
                failedEntities = result.FailedEntities,
                processingTimeMs = result.ProcessingTime.TotalMilliseconds,
                errors = result.Errors,
                entityMappings = result.EntityMappings.Count
            };

            await _openSearchService.LogEntityBatchProcessingAsync(
                request.MigrationId, 
                request.EntityType, 
                request.BatchNumber, 
                logData,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log batch processing to OpenSearch for migration {MigrationId}",
                request.MigrationId);
        }
    }

    /// <summary>
    /// Completes batch processing with timing
    /// Single Responsibility: Batch completion coordination only
    /// </summary>
    private BatchProcessingResult CompleteBatch(BatchProcessingResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        result.ProcessingTime = stopwatch.Elapsed;
        
        _logger.LogInformation("Completed batch processing: {SuccessfulEntities} successful, {FailedEntities} failed",
            result.SuccessfulEntities, result.FailedEntities);
        
        return result;
    }

    /// <summary>
    /// Extracts entity ID from entity data with entity-specific field handling
    /// </summary>
    /// <param name="entity">Entity data dictionary</param>
    /// <param name="entityType">Type of entity</param>
    /// <returns>Entity ID or entity name as fallback</returns>
    private static string ExtractEntityId(Dictionary<string, object> entity, string entityType)
    {
        if (entity == null || entity.Count == 0)
            return "unknown";

        try
        {
            // Entity-specific ID field mapping
            var idFields = entityType.ToLowerInvariant() switch
            {
                "categories" or "category" => new[] { "id", "category_id", "Id", "ID" },
                "products" or "product" => new[] { "id", "product_id", "Id", "ID" },
                "brands" or "brand" => new[] { "id", "brand_id", "Id", "ID" },
                "variants" or "variant" => new[] { "id", "variant_id", "Id", "ID" },
                "images" or "image" => new[] { "id", "image_id", "Id", "ID" },
                "modifiers" or "modifier" => new[] { "id", "modifier_id", "Id", "ID" },
                _ => new[] { "id", "Id", "ID" }
            };

            // Try to find ID in preferred order
            foreach (var field in idFields)
            {
                if (entity.TryGetValue(field, out var idValue) && idValue != null)
                {
                    var idString = idValue.ToString();
                    if (!string.IsNullOrWhiteSpace(idString))
                    {
                        return idString;
                    }
                }
            }

            // Fallback: Use entity name with prefix
            var name = ExtractEntityName(entity, entityType);
            return !string.IsNullOrWhiteSpace(name) ? $"name:{name}" : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Extracts entity name from entity data
    /// </summary>
    /// <param name="entity">Entity data dictionary</param>
    /// <param name="entityType">Type of entity</param>
    /// <returns>Entity name or null</returns>
    private static string? ExtractEntityName(Dictionary<string, object> entity, string entityType)
    {
        try
        {
            return entityType.ToLowerInvariant() switch
            {
                "categories" or "category" => entity.TryGetValue("name", out var categoryName) ? categoryName?.ToString() : null,
                "products" or "product" => entity.TryGetValue("name", out var productName) ? productName?.ToString() : null,
                "brands" or "brand" => entity.TryGetValue("name", out var brandName) ? brandName?.ToString() : null,
                "variants" or "variant" => entity.TryGetValue("sku", out var variantSku) ? variantSku?.ToString() : null,
                "modifiers" or "modifier" => entity.TryGetValue("display_name", out var modifierName) ? modifierName?.ToString() : null,
                _ => entity.TryGetValue("name", out var genericName) ? genericName?.ToString() : null
            };
        }
        catch
        {
            return null;
        }
    }


} 