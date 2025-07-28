using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.Orchestration.Orchestrators;

/// <summary>
/// Entity-specific migration sub-orchestrator that handles batch processing,
/// rate limiting, and progress tracking for individual entity types
/// </summary>
public class EntityMigrationOrchestrator
{
    private readonly ILogger<EntityMigrationOrchestrator> _logger;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly IParallelBatchProcessingPipeline _parallelPipeline;

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

    public EntityMigrationOrchestrator(
        ILogger<EntityMigrationOrchestrator> logger, 
        IProgressEventPublisher progressEventPublisher,
        IParallelBatchProcessingPipeline parallelPipeline)
    {
        _logger = logger;
        _progressEventPublisher = progressEventPublisher;
        _parallelPipeline = parallelPipeline;
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
            
            // 🎯 CRITICAL FIX: Handle both ID-based and page-based discovery strategies
            bool hasEntities = false;
            
            if (discoveryResult == null)
            {
                context.SetCustomStatus($"Discovery failed for {request.EntityType} entities");
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                return result;
            }
            
            // Check for entities based on strategy type
            if (discoveryResult.PaginationMetadata?.ContainsKey("UseDirectPagination") == true && 
                discoveryResult.PaginationMetadata["UseDirectPagination"].ToString() == "True")
            {
                // Page-based strategy: Check TotalCount instead of EntityIds
                hasEntities = discoveryResult.TotalCount > 0;
                _logger.LogInformation($"📄 Page-based discovery for {request.EntityType}: TotalCount={discoveryResult.TotalCount}, HasEntities={hasEntities}");
            }
            else
            {
                // ID-based strategy: Check EntityIds count
                hasEntities = discoveryResult.EntityIds.Count > 0;
                _logger.LogInformation($"📋 ID-based discovery for {request.EntityType}: EntityIds={discoveryResult.EntityIds.Count}, HasEntities={hasEntities}");
            }
            
            if (!hasEntities)
            {
                context.SetCustomStatus($"No {request.EntityType} entities found to migrate");
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                return result;
            }

            result.TotalEntities = discoveryResult.TotalCount;
            
            // Step 3: Create batches for processing
            List<BatchProcessingRequest> batches;
            
            // 🎯 STRATEGY-BASED BATCH CREATION: Use correct approach based on discovery strategy
            if (discoveryResult.PaginationMetadata?.ContainsKey("UseDirectPagination") == true && 
                discoveryResult.PaginationMetadata["UseDirectPagination"].ToString() == "True")
            {
                // Page-based processing: Use parallel pipeline for direct pagination
                _logger.LogInformation($"📄 Using page-based batch processing for {request.EntityType} with {discoveryResult.TotalCount} entities", 
                    request.EntityType, discoveryResult.TotalCount);
                
                // Create a request for the parallel pipeline
                var parallelRequest = new ProcessParallelBatchesRequest
                {
                    MigrationId = request.MigrationId,
                    EntityType = request.EntityType,
                    TotalBatches = 1, // Will be calculated by the parallel processor
                    BatchSize = 250, // Use same as discovery page size
                    EntityIds = new List<string>(), // Empty for page-based processing
                    SourceStore = request.SourceStore,
                    DestinationStore = request.DestinationStore,
                    CategoryTreeContext = request.CategoryTreeContext ?? new CategoryTreeContext(),
                    PaginationMetadata = discoveryResult.PaginationMetadata,
                    UseDirectPagination = true,
                    IsCancelled = false,
                    CancellationReason = "",
                    CancelledAt = null
                };
                
                // Use parallel pipeline for page-based processing
                context.SetCustomStatus($"Processing {request.EntityType} using page-based approach");
                await ProcessWithParallelPipeline(context, request, parallelRequest, result);
                
                // Step 5: Finalize result
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                context.SetCustomStatus($"Completed {request.EntityType} migration: {result.SuccessfulEntities}/{result.TotalEntities} successful");
                
                return result;
            }
            else
            {
                // ID-based processing: Use traditional batch creation
                _logger.LogInformation($"📋 Using ID-based batch processing for {request.EntityType} with {discoveryResult.EntityIds.Count} entity IDs", 
                    request.EntityType, discoveryResult.EntityIds.Count);
                
                batches = CreateBatches(discoveryResult.EntityIds, request, discoveryResult);
            }
            
            context.SetCustomStatus($"Created {batches.Count} batches for {request.EntityType} migration");

            // Step 4: Process batches with rate limiting and enhanced progress tracking
            await ProcessBatchesAsync(context, request, batches, result);

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
    /// **P2.5: PARALLEL BATCH PROCESSING INTEGRATION**
    /// Processes batches using enhanced parallel pipeline with integrated rate limiting,
    /// progress tracking, and all existing sequential functionality preserved
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <param name="batches">Batches to process</param>
    /// <param name="result">Result to update</param>
    private async Task ProcessBatchesAsync(
        IDurableOrchestrationContext context,
        EntityMigrationRequest request,
        List<BatchProcessingRequest> batches,
        EntityMigrationResult result)
    {
        // 🚨 CRITICAL FIX: Categories MUST use original sequential processing until hierarchy issues are resolved
        if (request.EntityType.ToLower() == "categories")
        {
            _logger.LogWarning("🚨 [CATEGORIES] ⚠️ FALLBACK TO ORIGINAL: Using original sequential processing for categories to avoid hierarchy issues in migration {MigrationId}", request.MigrationId);
            
            // Use the original sequential processing logic for categories
            await ProcessBatchesSequentiallyOriginal(context, request, batches, result);
            return;
        }

        _logger.LogInformation("🚀 P2.5: Starting PARALLEL batch processing for {EntityType} - {BatchCount} batches",
            request.EntityType, batches.Count);

        // Initialize enhanced progress tracking for this entity via queue event
        try
        {
            var startEvent = new EntityProgressEvent
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                Status = "started",
                TotalCount = result.TotalEntities,
                ProcessedCount = 0,
                FailureCount = 0,
                ProcessingTime = TimeSpan.Zero
            };

            await _progressEventPublisher.PublishEntityProgressAsync(startEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish entity start event for {EntityType}", request.EntityType);
            // Don't throw - progress events should not break the migration
        }

        try
        {
            // ✅ **P2.5: REPLACE SEQUENTIAL LOOP WITH PARALLEL PIPELINE**
            // This single call replaces the entire sequential for-loop with sophisticated
            // parallel processing while preserving all existing functionality
            var parallelProcessingStartTime = context.CurrentUtcDateTime;
            
            var parallelResult = await _parallelPipeline.ProcessBatchesInParallelAsync(
                context, 
                request, 
                batches);

            var parallelProcessingDuration = context.CurrentUtcDateTime - parallelProcessingStartTime;

            _logger.LogInformation("🎉 P2.5: PARALLEL processing completed for {EntityType} in {Duration}ms - {Processed}/{Total} entities",
                request.EntityType, parallelProcessingDuration.TotalMilliseconds, 
                parallelResult.ProcessedEntities, parallelResult.TotalEntities);

            // ✅ **PRESERVED FUNCTIONALITY**: Convert parallel result to legacy result format
            result.ProcessedEntities = parallelResult.ProcessedEntities;
            result.SuccessfulEntities = parallelResult.SuccessfulEntities;
            result.FailedEntities = parallelResult.FailedEntities;
            result.ProcessingTime = parallelResult.Duration;
            
            // Merge errors from parallel processing
            if (parallelResult.Errors?.Any() == true)
            {
                foreach (var error in parallelResult.Errors)
                {
                    if (!result.Errors.Contains(error))
                    {
                        result.Errors.Add(error);
                    }
                }
            }

            // ✅ **PRESERVED FUNCTIONALITY**: Maintain existing progress tracking
            await UpdateEnhancedProgress(context, request, batches.Count, batches.Count, result);
            await UpdateProgress(context, request, batches.Count, batches.Count, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 P2.5: PARALLEL processing failed for {EntityType} - falling back to error handling", request.EntityType);
            
            // ✅ **PRESERVED FUNCTIONALITY**: Maintain existing error handling
            var errorMessage = $"Parallel batch processing failed for {request.EntityType}: {ex.Message}";
            result.Errors.Add(errorMessage);
            
            if (!result.Errors.Contains(ex.Message))
            {
                result.Errors.Add(ex.Message);
            }

            // Mark all entities as failed if parallel processing completely fails
            result.FailedEntities = result.TotalEntities;
            result.ProcessedEntities = result.TotalEntities;

            // Report failure via existing error event system
            try
            {
                var errorEvent = new ErrorProgressEvent
                {
                    MigrationId = request.MigrationId,
                    EntityType = request.EntityType,
                    BatchNumber = 0,
                    Message = ex.Message,
                    Details = ex.ToString(),
                    Timestamp = context.CurrentUtcDateTime
                };

                await _progressEventPublisher.PublishErrorAsync(errorEvent);
            }
            catch (Exception publishEx)
            {
                _logger.LogWarning(publishEx, "Failed to publish error event for {EntityType} processing failure", request.EntityType);
                // Don't throw - just log the warning
            }
        }
    }

    /// <summary>
    /// Processes entities using the parallel pipeline for page-based strategies
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <param name="parallelRequest">Parallel processing request</param>
    /// <param name="result">Result to update</param>
    private async Task ProcessWithParallelPipeline(
        IDurableOrchestrationContext context,
        EntityMigrationRequest request,
        ProcessParallelBatchesRequest parallelRequest,
        EntityMigrationResult result)
    {
        try
        {
            _logger.LogInformation("🚀 Starting page-based parallel processing for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);

            // Process using the parallel pipeline that supports direct pagination
            var parallelResult = await context.CallActivityAsync<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>(
                "ProcessParallelBatches", 
                parallelRequest);

            if (parallelResult != null)
            {
                // Merge results from parallel processing
                result.ProcessedEntities = parallelResult.TotalProcessed;
                result.SuccessfulEntities = parallelResult.SuccessfulEntities;
                result.FailedEntities = parallelResult.FailedEntities;
                result.Errors.AddRange(parallelResult.Errors ?? new List<string>());
                
                _logger.LogInformation("✅ Page-based parallel processing completed for {EntityType}: {Successful}/{Total} successful", 
                    request.EntityType, result.SuccessfulEntities, result.ProcessedEntities);
            }
            else
            {
                _logger.LogError("❌ Page-based parallel processing returned null result for {EntityType}", request.EntityType);
                result.Errors.Add("Parallel processing failed to return results");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during page-based parallel processing for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            
            result.Errors.Add($"Page-based processing failed: {ex.Message}");
            
            // Set failed count to total if we don't have better information
            if (result.ProcessedEntities == 0 && result.TotalEntities > 0)
            {
                result.ProcessedEntities = result.TotalEntities;
                result.FailedEntities = result.TotalEntities;
            }
        }
    }

    /// <summary>
    /// 🚨 FALLBACK: Original sequential processing for categories until hierarchy issues are resolved
    /// This preserves the original working logic from develop branch
    /// </summary>
    private async Task ProcessBatchesSequentiallyOriginal(
        IDurableOrchestrationContext context,
        EntityMigrationRequest request,
        List<BatchProcessingRequest> batches,
        EntityMigrationResult result)
    {
        _logger.LogInformation("🔄 [CATEGORIES] ⭐ STARTING: Original sequential processing for {EntityType} - {BatchCount} batches", 
            request.EntityType, batches.Count);

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing batch {BatchNumber} for entity type {EntityType} in migration {MigrationId}",
                    batchNumber, request.EntityType, request.MigrationId);

                // Add error details
                result.Errors.Add($"Batch {batchNumber} failed: {ex.Message}");

                // Update failed count
                result.FailedEntities += batch.EntityIds.Count;
                result.ProcessedEntities += batch.EntityIds.Count;

                // Report batch failure
                try
                {
                    var errorEvent = new ErrorProgressEvent
                    {
                        MigrationId = request.MigrationId,
                        EntityType = request.EntityType,
                        BatchNumber = batchNumber,
                        Message = ex.Message,
                        Details = ex.ToString(),
                        Timestamp = context.CurrentUtcDateTime
                    };

                    await _progressEventPublisher.PublishErrorAsync(errorEvent);
                }
                catch (Exception publishEx)
                {
                    _logger.LogWarning(publishEx, "Failed to publish error event for batch {BatchNumber}", batchNumber);
                    // Don't throw - just log the warning
                }
            }
        }

        _logger.LogInformation("🏁 [CATEGORIES] ✅ COMPLETED: Original sequential processing for {EntityType} - {Processed}/{Total} entities processed",
            request.EntityType, result.ProcessedEntities, result.TotalEntities);
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

 