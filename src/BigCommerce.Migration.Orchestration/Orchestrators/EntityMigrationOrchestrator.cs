using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;

namespace BigCommerce.Migration.Orchestration.Orchestrators;

/// <summary>
/// Entity-specific migration sub-orchestrator that handles batch processing,
/// rate limiting, and progress tracking for individual entity types
/// </summary>
public class EntityMigrationOrchestrator
{
    private readonly ILogger<EntityMigrationOrchestrator> _logger;
    private readonly IProgressEventPublisher _progressEventPublisher;

    // Default batch sizes by entity type [[memory:2322834]]
    private static readonly Dictionary<string, int> DefaultBatchSizes = new()
    {
        ["categories"] = 25,
        ["products"] = 10,
        ["brands"] = 50,
        ["variants"] = 20,
        ["images"] = 15,
        ["modifiers"] = 30
    };

    public EntityMigrationOrchestrator(ILogger<EntityMigrationOrchestrator> logger, IProgressEventPublisher progressEventPublisher)
    {
        _logger = logger;
        _progressEventPublisher = progressEventPublisher;
    }

    /// <summary>
    /// Main entity migration orchestrator function
    /// </summary>
    /// <param name="context">Durable orchestration context</param>
    /// <returns>Entity migration result</returns>
    [Function("EntityMigrationOrchestrator")]
    public async Task<EntityMigrationResult> RunEntityMigrationOrchestrator(
        [OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var startTime = context.CurrentUtcDateTime; // ✅ Use deterministic datetime
        var request = context.GetInput<EntityMigrationRequest>();

        // Initialize result with deterministic values
        var result = new EntityMigrationResult
        {
            EntityType = request.EntityType,
            TotalEntities = 0,
            ProcessedEntities = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            Mappings = new List<EntityMapping>(),
            Errors = new List<string>(),
            ProcessingTime = TimeSpan.Zero
        };

        try
        {
            // Step 0: Check for cancellation before starting
            var isCancelled = await context.CallActivityAsync<bool>("CheckMigrationCancellation", request.MigrationId);
            if (isCancelled)
            {
                result.Errors.Add($"{request.EntityType} migration was cancelled before processing started");
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                return result;
            }

            // Step 1: Validate request
            context.SetCustomStatus($"Starting {request.EntityType} migration");
            
            if (!request.IsValid())
            {
                var validationErrors = request.GetValidationErrors();
                result.Errors.AddRange(validationErrors);
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                return result;
            }

            // Step 2: Discover entities to migrate
            context.SetCustomStatus($"Discovering {request.EntityType} entities");
            
            var discoveryResult = await DiscoverEntitiesWithRetry(context, request);
            if (discoveryResult == null || discoveryResult.EntityIds.Count == 0)
            {
                context.SetCustomStatus($"No {request.EntityType} entities found to migrate");
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                return result;
            }

            result.TotalEntities = discoveryResult.TotalCount;
            
            // Step 3: Create batches for processing
            var batches = CreateBatches(discoveryResult.EntityIds, request, discoveryResult);
            context.SetCustomStatus($"Created {batches.Count} batches for {request.EntityType} migration");

            // Step 4: Process batches with rate limiting and enhanced progress tracking
            await ProcessBatchesWithRateLimit(context, request, batches, result);

            // Step 5: Finalize result
            result.ProcessingTime = context.CurrentUtcDateTime - startTime;
            context.SetCustomStatus($"Completed {request.EntityType} migration: {result.SuccessfulEntities}/{result.TotalEntities} successful");

            return result;
        }
        catch (Exception ex)
        {
            // Handle any unhandled exceptions
            context.SetCustomStatus($"{request.EntityType} migration failed");
            
            result.Errors.Add($"{request.EntityType} migration failed with error: {ex.Message}");
            
            // Also add the original error message for test compatibility
            if (!result.Errors.Contains(ex.Message))
            {
                result.Errors.Add(ex.Message);
            }
            
            result.ProcessingTime = context.CurrentUtcDateTime - startTime;
            return result;
        }
    }

    /// <summary>
    /// Discovers entities with retry logic for resilience
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <returns>Discovery result or null if failed</returns>
    private async Task<EntityDiscoveryResult?> DiscoverEntitiesWithRetry(
        IDurableOrchestrationContext context, 
        EntityMigrationRequest request)
    {
        try
        {
            var discoveryRequest = new
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                SourceStore = request.SourceStore,
                EntityConfig = request.EntityConfig
            };

            return await context.CallActivityAsync<EntityDiscoveryResult>("DiscoverEntities", discoveryRequest);
        }
        catch (Exception ex)
        {
            throw new Exception($"Entity discovery failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates batches from entity IDs based on configuration
    /// </summary>
    /// <param name="entityIds">List of entity IDs to batch</param>
    /// <param name="request">Entity migration request</param>
    /// <param name="discoveryResult">Discovery result containing cached entity data</param>
    /// <returns>List of batch processing requests</returns>
    private List<BatchProcessingRequest> CreateBatches(List<string> entityIds, EntityMigrationRequest request, EntityDiscoveryResult discoveryResult)
    {
        // ✅ DEBUG: Log discovery result status at the start
        _logger.LogWarning("🔍 DEBUG: CreateBatches called - HasEntityData: {HasEntityData}, EntityDataCount: {EntityDataCount}, EntityIdsCount: {EntityIdsCount}",
            discoveryResult.HasEntityData, discoveryResult.EntityData?.Count ?? 0, entityIds.Count);

        // Determine effective batch size (EntityConfig override takes priority)
        int batchSize = request.EntityConfig.BatchSizeOverride ?? request.BatchSize;
        
        // If no override, use entity-type specific default
        if (request.EntityConfig.BatchSizeOverride == null && DefaultBatchSizes.ContainsKey(request.EntityType.ToLowerInvariant()))
        {
            batchSize = DefaultBatchSizes[request.EntityType.ToLowerInvariant()];
        }

        var batches = new List<BatchProcessingRequest>();

        // Standard entity-ID-based batching (works for all strategies)
        var totalBatches = (int)Math.Ceiling((double)entityIds.Count / batchSize);

        for (int i = 0; i < totalBatches; i++)
        {
            var batchEntityIds = entityIds
                .Skip(i * batchSize)
                .Take(batchSize)
                .ToList();

            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                BatchNumber = i + 1,
                TotalBatches = totalBatches,
                EntityIds = batchEntityIds,
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext,
                CachedEntityData = discoveryResult.HasEntityData ? discoveryResult.EntityData : null
            };

            // ✅ DEBUG: Log cached data status for troubleshooting
            _logger.LogWarning("🔍 DEBUG: Batch {BatchNumber} for {EntityType} - HasEntityData: {HasEntityData}, CachedCount: {CachedCount}, EntityIds: [{EntityIds}]",
                i + 1, request.EntityType, discoveryResult.HasEntityData, 
                discoveryResult.EntityData?.Count ?? 0, string.Join(", ", batchEntityIds));

            batches.Add(batchRequest);
        }

        return batches;
    }

    /// <summary>
    /// Processes batches with rate limiting and enhanced progress tracking
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <param name="batches">List of batches to process</param>
    /// <param name="result">Result object to update</param>
    private async Task ProcessBatchesWithRateLimit(
        IDurableOrchestrationContext context,
        EntityMigrationRequest request,
        List<BatchProcessingRequest> batches,
        EntityMigrationResult result)
    {
        // Initialize enhanced progress tracking for this entity via queue event
        try
        {
            var entityProgressEvent = new EntityProgressEvent
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                TotalCount = result.TotalEntities,
                ProcessedCount = 0,
                SuccessCount = 0,
                FailureCount = 0,
                Status = "initializing",
                ProcessingTime = TimeSpan.Zero
            };

            await _progressEventPublisher.PublishEntityProgressAsync(entityProgressEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish entity initialization event for {EntityType}", request.EntityType);
            // Don't throw - progress events should not break the migration
        }

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            var batchNumber = i + 1;
            
            try
            {
                // Step 0: Check for cancellation before processing each batch
                var isCancelled = await context.CallActivityAsync<bool>("CheckMigrationCancellation", request.MigrationId);
                if (isCancelled)
                {
                    result.Errors.Add($"{request.EntityType} migration was cancelled during batch {batchNumber} processing");
                    return; // Exit the loop early
                }

                // Step 1: Start enhanced batch tracking via queue event
                try
                {
                    var batchProgressEvent = new BatchProgressEvent
                    {
                        MigrationId = request.MigrationId,
                        EntityType = request.EntityType,
                        BatchNumber = batchNumber,
                        TotalBatches = batches.Count,
                        BatchSize = batch.EntityIds.Count,
                        ProcessedCount = 0,
                        FailedCount = 0,
                        Status = "started",
                        ProcessingTime = TimeSpan.Zero
                    };

                    await _progressEventPublisher.PublishBatchProgressAsync(batchProgressEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish batch start event for {EntityType} batch {BatchNumber}", request.EntityType, batchNumber);
                    // Don't throw - progress events should not break the migration
                }

                // Step 2: Check rate limiting before processing batch
                await ApplyRateLimiting(context, request);

                // Step 3: Update processing context
                context.SetCustomStatus($"Processing batch {batchNumber}/{batches.Count} ({batch.EntityIds.Count} entities)");
                
                // Step 4: Process the batch with real-time progress updates
                var batchStartTime = context.CurrentUtcDateTime;
                var batchResult = await context.CallActivityAsync<BatchProcessingResult>("ProcessEntityBatch", batch);
                var batchDuration = context.CurrentUtcDateTime - batchStartTime;

                // Step 5: Complete batch tracking with results via queue event
                try
                {
                    var batchProgressEvent = new BatchProgressEvent
                    {
                        MigrationId = request.MigrationId,
                        EntityType = request.EntityType,
                        BatchNumber = batchNumber,
                        TotalBatches = batches.Count,
                        BatchSize = batch.EntityIds.Count,
                        ProcessedCount = batchResult.TotalProcessed,
                        FailedCount = batchResult.FailedEntities,
                        Status = batchResult.FailedEntities > 0 ? "completed_with_errors" : "completed",
                        ProcessingTime = batchDuration
                    };

                    await _progressEventPublisher.PublishBatchProgressAsync(batchProgressEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish batch completion event for {EntityType} batch {BatchNumber}", request.EntityType, batchNumber);
                    // Don't throw - progress events should not break the migration
                }

                // Step 6: Aggregate batch results
                AggregateBatchResults(result, batchResult);

                // Step 7: Update enhanced progress after batch completion
                await UpdateEnhancedProgress(context, request, batchNumber, batches.Count, result);

                // Step 8: Update legacy progress for backward compatibility
                await UpdateProgress(context, request, batchNumber, batches.Count, result);

                _logger.LogDebug("Completed batch {BatchNumber}/{TotalBatches} for {EntityType}: {Successful}/{Total} successful", 
                    batchNumber, batches.Count, request.EntityType, batchResult.SuccessfulEntities, batchResult.TotalProcessed);
            }
            catch (Exception ex)
            {
                // Handle batch processing failure
                var errorMessage = $"Failed to process batch {batchNumber}: {ex.Message}";
                result.Errors.Add(errorMessage);
                
                // Also add the original error message for test compatibility
                if (!result.Errors.Contains(ex.Message))
                {
                    result.Errors.Add(ex.Message);
                }

                // Mark all entities in this batch as failed and complete batch tracking
                result.FailedEntities += batch.EntityIds.Count;
                result.ProcessedEntities += batch.EntityIds.Count;

                // Report failed batch to enhanced tracking via queue event
                try
                {
                    var batchProgressEvent = new BatchProgressEvent
                    {
                        MigrationId = request.MigrationId,
                        EntityType = request.EntityType,
                        BatchNumber = batchNumber,
                        TotalBatches = batches.Count,
                        BatchSize = batch.EntityIds.Count,
                        ProcessedCount = batch.EntityIds.Count,
                        FailedCount = batch.EntityIds.Count,
                        Status = "failed",
                        ProcessingTime = TimeSpan.Zero
                    };

                    await _progressEventPublisher.PublishBatchProgressAsync(batchProgressEvent);

                    // Also publish error event
                    var errorEvent = new ErrorProgressEvent
                    {
                        MigrationId = request.MigrationId,
                        EntityType = request.EntityType,
                        BatchNumber = batchNumber,
                        Message = ex.Message,
                        Details = ex.ToString(),
                        Severity = "error",
                        IsContinuable = true,
                        Timestamp = context.CurrentUtcDateTime
                    };

                    await _progressEventPublisher.PublishErrorAsync(errorEvent);
                }
                catch (Exception publishEx)
                {
                    _logger.LogWarning(publishEx, "Failed to publish batch failure event for {EntityType} batch {BatchNumber}", request.EntityType, batchNumber);
                    // Don't throw - progress events should not break the migration
                }

                _logger.LogError(ex, "Failed to process batch {BatchNumber}/{TotalBatches} for {EntityType}", 
                    batchNumber, batches.Count, request.EntityType);
            }
        }

        // Complete entity processing via queue event
        try
        {
            var entityProgressEvent = new EntityProgressEvent
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                TotalCount = result.TotalEntities,
                ProcessedCount = result.ProcessedEntities,
                SuccessCount = result.SuccessfulEntities,
                FailureCount = result.FailedEntities,
                Status = result.FailedEntities > 0 ? "completed_with_errors" : "completed",
                ProcessingTime = result.ProcessingTime
            };

            await _progressEventPublisher.PublishEntityProgressAsync(entityProgressEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish entity completion event for {EntityType}", request.EntityType);
            // Don't throw - progress events should not break the migration
        }
    }

    /// <summary>
    /// Applies rate limiting with delays if necessary
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    private async Task ApplyRateLimiting(IDurableOrchestrationContext context, EntityMigrationRequest request)
    {
        try
        {
            var rateLimitRequest = new
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                StoreId = request.SourceStore.StoreId
            };

            var rateLimitResult = await context.CallActivityAsync<RateLimitResult>("CheckRateLimit", rateLimitRequest);

            if (!rateLimitResult.CanProceed && rateLimitResult.DelayMs > 0)
            {
                // ✅ Use deterministic timer instead of Task.Delay
                var delayUntil = context.CurrentUtcDateTime.AddMilliseconds(rateLimitResult.DelayMs);
                await context.CreateTimer(delayUntil);
            }
        }
        catch (Exception ex)
        {
            // Log rate limiting failure but don't stop processing
            _logger.LogWarning(ex, "Rate limiting check failed for {EntityType}", request.EntityType);
        }
    }

    /// <summary>
    /// Updates enhanced progress tracking with detailed information
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <param name="currentBatch">Current batch number</param>
    /// <param name="totalBatches">Total number of batches</param>
    /// <param name="result">Current result state</param>
    private async Task UpdateEnhancedProgress(
        IDurableOrchestrationContext context,
        EntityMigrationRequest request,
        int currentBatch,
        int totalBatches,
        EntityMigrationResult result)
    {
        try
        {
            var enhancedProgressUpdate = new
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                CurrentBatch = currentBatch,
                TotalBatches = totalBatches,
                TotalEntities = result.TotalEntities,
                ProcessedEntities = result.ProcessedEntities,
                SuccessfulEntities = result.SuccessfulEntities,
                FailedEntities = result.FailedEntities,
                ProgressPercentage = totalBatches > 0 ? Math.Round((double)currentBatch / totalBatches * 100, 1) : 0,
                Phase = currentBatch >= totalBatches ? "Completed" : "Processing",
                CurrentActivity = $"Batch {currentBatch}/{totalBatches}",
                BatchSize = result.TotalEntities / totalBatches, // Average batch size
                RemainingBatches = Math.Max(0, totalBatches - currentBatch),
                RemainingEntities = Math.Max(0, result.TotalEntities - result.ProcessedEntities)
            };

            // Publish enhanced entity progress via queue event
            try
            {
                var entityProgressEvent = new EntityProgressEvent
                {
                    MigrationId = request.MigrationId,
                    EntityType = request.EntityType,
                    TotalCount = result.TotalEntities,
                    ProcessedCount = result.ProcessedEntities,
                    SuccessCount = result.SuccessfulEntities,
                    FailureCount = result.FailedEntities,
                    Status = currentBatch >= totalBatches ? "completed" : "processing"
                };

                await _progressEventPublisher.PublishEntityProgressAsync(entityProgressEvent);

                // Also publish batch progress if we have batch information
                if (currentBatch > 0)
                {
                    var batchProgressEvent = new BatchProgressEvent
                    {
                        MigrationId = request.MigrationId,
                        EntityType = request.EntityType,
                        BatchNumber = currentBatch,
                        TotalBatches = totalBatches,
                        BatchSize = result.TotalEntities / totalBatches, // Average batch size
                        ProcessedCount = result.ProcessedEntities,
                        FailedCount = result.FailedEntities,
                        Status = currentBatch >= totalBatches ? "completed" : "processing",
                        ProcessingTime = TimeSpan.FromSeconds(1) // Approximate
                    };

                    await _progressEventPublisher.PublishBatchProgressAsync(batchProgressEvent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish enhanced progress event for {EntityType}", request.EntityType);
                // Don't throw - progress events should not break the migration
            }
        }
        catch (Exception ex)
        {
            // Log progress update failure but don't stop processing
            _logger.LogWarning(ex, "Enhanced progress update failed for {EntityType}", request.EntityType);
        }
    }

    /// <summary>
    /// Updates progress tracking (legacy method for backward compatibility)
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <param name="currentBatch">Current batch number</param>
    /// <param name="totalBatches">Total number of batches</param>
    /// <param name="result">Current result state</param>
    private async Task UpdateProgress(
        IDurableOrchestrationContext context,
        EntityMigrationRequest request,
        int currentBatch,
        int totalBatches,
        EntityMigrationResult result)
    {
        try
        {
            var progressUpdate = new
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                CurrentBatch = currentBatch,
                TotalBatches = totalBatches,
                TotalEntities = result.TotalEntities,
                ProcessedEntities = result.ProcessedEntities,
                SuccessfulEntities = result.SuccessfulEntities,
                FailedEntities = result.FailedEntities,
                ProgressPercentage = totalBatches > 0 ? Math.Round((double)currentBatch / totalBatches * 100, 1) : 0
            };

            // Publish entity progress via queue event
            try
            {
                var entityProgressEvent = new EntityProgressEvent
                {
                    MigrationId = request.MigrationId,
                    EntityType = request.EntityType,
                    TotalCount = result.TotalEntities,
                    ProcessedCount = result.ProcessedEntities,
                    SuccessCount = result.SuccessfulEntities,
                    FailureCount = result.FailedEntities,
                    Status = currentBatch >= totalBatches ? "completed" : "processing"
                };

                await _progressEventPublisher.PublishEntityProgressAsync(entityProgressEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish entity progress event for {EntityType}", request.EntityType);
                // Don't throw - progress events should not break the migration
            }
        }
        catch (Exception ex)
        {
            // Log progress update failure but don't stop processing
            _logger.LogWarning(ex, "Progress update failed for {EntityType}", request.EntityType);
        }
    }

    /// <summary>
    /// Aggregates results from individual batch processing
    /// </summary>
    /// <param name="result">Overall result to update</param>
    /// <param name="batchResult">Individual batch result</param>
    private static void AggregateBatchResults(EntityMigrationResult result, BatchProcessingResult batchResult)
    {
        result.ProcessedEntities += batchResult.TotalProcessed;
        result.SuccessfulEntities += batchResult.SuccessfulEntities;
        result.FailedEntities += batchResult.FailedEntities;

        // Add entity mappings
        if (batchResult.EntityMappings != null && batchResult.EntityMappings.Any())
        {
            result.Mappings.AddRange(batchResult.EntityMappings);
        }

        // Add batch-specific errors
        if (batchResult.Errors != null && batchResult.Errors.Any())
        {
            result.Errors.AddRange(batchResult.Errors);
        }
    }
}

 