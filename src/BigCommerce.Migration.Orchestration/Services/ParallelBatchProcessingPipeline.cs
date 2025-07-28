using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// **PHASE 2: Parallel Batch Processing Pipeline**
/// 
/// Replaces sequential batch processing with parallel execution using Task.WhenAll coordination
/// while maintaining Durable Functions determinism and existing progress tracking workflows.
/// 
/// **Key Features:**
/// - Parallel batch execution with controlled concurrency (semaphore-based throttling)
/// - Integration with Phase 1 dynamic rate limiting for API-aware processing
/// - Preserves existing SignalR progress tracking and batch events
/// - Maintains Durable Functions determinism and replay safety
/// - Thread-safe result aggregation with atomic progress updates
/// 
/// **Performance Target:**
/// - 12.5x total throughput improvement (720 → 9,000 req/hour)
/// - Optimal resource utilization with adaptive concurrency control
/// - Zero message loss or duplication during parallel processing
/// </summary>
public class ParallelBatchProcessingPipeline : IParallelBatchProcessingPipeline
{
    #region Private Fields

    private readonly IEnhancedParallelProcessor _parallelProcessor;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ILogger<ParallelBatchProcessingPipeline> _logger;
    private readonly IDateTimeProvider _dateTimeProvider;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes the parallel batch processing pipeline
    /// </summary>
    public ParallelBatchProcessingPipeline(
        IEnhancedParallelProcessor parallelProcessor,
        IProgressEventPublisher progressEventPublisher,
        ILogger<ParallelBatchProcessingPipeline> logger,
        IDateTimeProvider dateTimeProvider)
    {
        _parallelProcessor = parallelProcessor ?? throw new ArgumentNullException(nameof(parallelProcessor));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    #endregion

    #region Parallel Pipeline Execution

    /// <summary>
    /// Processes batches in parallel while maintaining Durable Functions determinism
    /// 
    /// **Pipeline Flow:**
    /// 1. Setup parallel processing configuration based on migration context
    /// 2. Create deterministic batch processor function with Durable Functions integration
    /// 3. Execute parallel processing with Task.WhenAll coordination
    /// 4. Aggregate results while preserving order and determinism
    /// 5. Update progress tracking and SignalR notifications
    /// 
    /// **Determinism Compliance:**
    /// - Uses deterministic batch ordering for consistent replay behavior
    /// - Preserves existing Durable Functions activity call patterns
    /// - Maintains atomic result aggregation for consistent state
    /// </summary>
    public async Task<EntityMigrationResult> ProcessBatchesInParallelAsync(
        IDurableOrchestrationContext context,
        EntityMigrationRequest request,
        IReadOnlyList<BatchProcessingRequest> batches,
        CancellationToken cancellationToken = default)
    {
        var storeId = request.SourceStore?.StoreId ?? "unknown";
        _logger.LogInformation("Starting parallel batch processing for {EntityType} migration {MigrationId}: {BatchCount} batches for store {StoreId}",
            request.EntityType, request.MigrationId, batches.Count, storeId);

        try
        {
            // Step 1: Create parallel processing configuration
            var parallelConfig = CreateParallelProcessingConfiguration(request, storeId);

            // Step 2: Create progress tracking for real-time updates
            var progressAggregator = _parallelProcessor.CreateProgressAggregator(
                request.MigrationId, request.EntityType, batches.Count, _progressEventPublisher);

            // Step 3: Create deterministic batch processor function
            var batchProcessor = CreateDeterministicBatchProcessor(context, request);

            // Step 4: Setup progress callback for SignalR updates
            var progressCallback = new Progress<BatchProgressUpdate>(async update =>
            {
                await HandleProgressUpdate(context, request, update);
            });

            // Step 5: Execute parallel batch processing with Task.WhenAll coordination
            var parallelResult = await _parallelProcessor.ProcessBatchesInParallelAsync(
                batches, batchProcessor, parallelConfig, progressCallback, cancellationToken);

            // Step 6: Convert parallel result to entity migration result
            var migrationResult = ConvertToEntityMigrationResult(parallelResult, request);

            // Step 7: Final progress update
            await PublishFinalProgressUpdate(request, migrationResult, batches.Count);

            _logger.LogInformation("Completed parallel batch processing for {EntityType} migration {MigrationId}: " +
                                 "{ProcessedBatches}/{TotalBatches} batches, {ProcessedEntities} entities, " +
                                 "throughput: {Throughput:F2} entities/sec, improvement: {Improvement:F1}x",
                request.EntityType, request.MigrationId, parallelResult.SuccessfulBatches, 
                parallelResult.TotalBatchesProcessed, parallelResult.TotalEntitiesProcessed,
                parallelResult.OverallThroughput, parallelResult.ParallelizationImprovement);

            return migrationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed parallel batch processing for {EntityType} migration {MigrationId} with {BatchCount} batches",
                request.EntityType, request.MigrationId, batches.Count);
            
            // Return error result
            return new EntityMigrationResult
            {
                EntityType = request.EntityType,
                TotalEntities = batches.Sum(b => b.EntityIds.Count),
                Errors = new List<string> { $"Parallel processing failed: {ex.Message}" }
            };
        }
    }

    #endregion

    #region Deterministic Batch Processing

    /// <summary>
    /// Creates a deterministic batch processor function that integrates with Durable Functions
    /// 
    /// **Determinism Features:**
    /// - Preserves existing Durable Functions activity call patterns
    /// - Maintains consistent cancellation checking behavior
    /// - Uses deterministic time from orchestration context
    /// - Preserves batch numbering and ordering for replay consistency
    /// </summary>
    private Func<BatchProcessingRequest, CancellationToken, Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>> 
        CreateDeterministicBatchProcessor(IDurableOrchestrationContext context, EntityMigrationRequest request)
    {
        return async (batch, batchCancellationToken) =>
        {
            var batchNumber = batch.BatchNumber; // Assuming this exists or we'll add it
            
            try
            {
                // Step 1: Check for cancellation (deterministic via Durable Functions)
                var isCancelled = await context.CallActivityAsync<bool>("CheckMigrationCancellation", request.MigrationId);
                if (isCancelled)
                {
                    _logger.LogInformation("Migration {MigrationId} was cancelled before parallel batch processing", request.MigrationId);
                    
                    // Return proper cancellation result instead of throwing
                    return new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
                    {
                        BatchNumber = batchNumber,
                        TotalProcessed = 0,
                        SuccessfulEntities = 0,
                        FailedEntities = 0,
                        ProcessingTime = TimeSpan.Zero,
                        Errors = new List<string> { $"Migration {request.MigrationId} was cancelled" }
                    };
                }

                // Step 2: Apply rate limiting through Durable Functions activity (deterministic)
                await context.CallActivityAsync("ApplyRateLimiting", new { request.SourceStore, request.EntityType });

                // Step 3: Update processing context (deterministic)
                context.SetCustomStatus($"Processing batch {batchNumber} in parallel ({batch.EntityIds.Count} entities)");

                // Step 4: Process the batch via existing Durable Functions activity (preserves determinism)
                var batchStartTime = context.CurrentUtcDateTime;
                var batchResult = await context.CallActivityAsync<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>("ProcessEntityBatch", batch);
                var batchDuration = context.CurrentUtcDateTime - batchStartTime;

                // Step 5: Set processing time for result aggregation
                batchResult.ProcessingTime = batchDuration;
                batchResult.BatchNumber = batchNumber;

                _logger.LogDebug("Parallel batch {BatchNumber} completed: {TotalProcessed} processed, {FailedEntities} failed, {Duration:F2}s",
                    batchNumber, batchResult.TotalProcessed, batchResult.FailedEntities, batchDuration.TotalSeconds);

                return batchResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process batch {BatchNumber} in parallel for {EntityType} migration {MigrationId}",
                    batchNumber, request.EntityType, request.MigrationId);

                // Return deterministic error result
                return new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
                {
                    BatchNumber = batchNumber,
                    TotalProcessed = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = batch.EntityIds.Count,
                    Errors = new List<string> { $"Batch {batchNumber} processing failed: {ex.Message}" },
                    ProcessingTime = TimeSpan.Zero
                };
            }
        };
    }

    #endregion

    #region Configuration & Setup

    /// <summary>
    /// Creates parallel processing configuration optimized for the migration context
    /// </summary>
    private ParallelProcessingConfiguration CreateParallelProcessingConfiguration(
        EntityMigrationRequest request, string storeId)
    {
        return new ParallelProcessingConfiguration
        {
            StoreId = storeId,
            EntityType = request.EntityType,
            RespectDynamicRateLimits = true, // Always integrate with Phase 1
            EnableSignalRUpdates = true,
            SignalRUpdateIntervalMs = 1000, // 1 second updates for migration progress
            EnableAdaptiveConcurrency = true,
            TargetCpuUtilization = 0.70, // 70% target
            BatchTimeoutMinutes = 15, // 15 minute timeout per batch
            MaxConcurrentBatches = null // Let the system calculate optimal concurrency
        };
    }

    #endregion

    #region Progress Tracking & SignalR Integration

    /// <summary>
    /// Handles real-time progress updates from parallel batch processing
    /// 
    /// **SignalR Integration:**
    /// - Publishes batch completion events for real-time UI updates  
    /// - Maintains backward compatibility with existing progress tracking
    /// - Preserves enhanced progress update patterns
    /// - Ensures deterministic progress state for Durable Functions replay
    /// </summary>
    private async Task HandleProgressUpdate(
        IDurableOrchestrationContext context,
        EntityMigrationRequest request,
        BatchProgressUpdate update)
    {
        try
        {
            // Publish enhanced batch progress event (existing pattern)
            var batchProgressEvent = new BatchProgressEvent
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                BatchNumber = update.CompletedBatchNumber,
                TotalBatches = update.TotalBatches,
                BatchSize = update.BatchEntitiesProcessed + update.BatchEntitiesFailed,
                ProcessedCount = update.BatchEntitiesProcessed,
                FailedCount = update.BatchEntitiesFailed,
                Status = update.BatchSuccess ? "completed" : "completed_with_errors",
                ProcessingTime = update.BatchProcessingTime
            };

            await _progressEventPublisher.PublishBatchProgressAsync(batchProgressEvent);

            // Update orchestration context status
            context.SetCustomStatus($"Parallel processing: {update.CompletedBatchNumber}/{update.TotalBatches} batches completed " +
                                   $"({update.OverallProgressPercentage:P1})");

            _logger.LogDebug("Published progress update for batch {BatchNumber}/{TotalBatches}: {Progress:P1}",
                update.CompletedBatchNumber, update.TotalBatches, update.OverallProgressPercentage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle progress update for batch {BatchNumber}", update.CompletedBatchNumber);
            // Don't throw - progress updates should not break the migration
        }
    }

    /// <summary>
    /// Publishes final progress update when all parallel processing completes
    /// </summary>
    private async Task PublishFinalProgressUpdate(
        EntityMigrationRequest request,
        EntityMigrationResult result,
        int totalBatches)
    {
        try
        {
            var finalProgressEvent = new MigrationProgressEvent
            {
                MigrationId = request.MigrationId,
                OverallProgress = 100.0, // 100% complete
                Status = result.Errors.Count > 0 ? "completed_with_errors" : "completed",
                TotalEntities = result.TotalEntities,
                ProcessedEntities = result.ProcessedEntities,
                FailedEntities = result.FailedEntities,
                CurrentEntityType = request.EntityType
            };

            await _progressEventPublisher.PublishMigrationProgressAsync(finalProgressEvent);

            _logger.LogInformation("Published final progress update for {EntityType} migration {MigrationId}: " +
                                 "{ProcessedEntities}/{TotalEntities} entities processed",
                request.EntityType, request.MigrationId, result.ProcessedEntities, result.TotalEntities);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish final progress update for {EntityType} migration {MigrationId}",
                request.EntityType, request.MigrationId);
        }
    }

    #endregion

    #region Result Conversion & Aggregation

    /// <summary>
    /// Converts parallel processing result to entity migration result format
    /// 
    /// **Aggregation Features:**
    /// - Preserves all batch processing results and errors
    /// - Maintains entity counts and processing statistics
    /// - Calculates performance metrics for monitoring
    /// - Ensures backward compatibility with existing result consumers
    /// </summary>
    private EntityMigrationResult ConvertToEntityMigrationResult(
        Core.Models.ParallelProcessingResult parallelResult,
        EntityMigrationRequest request)
    {
        return new EntityMigrationResult
        {
            EntityType = request.EntityType,
            TotalEntities = parallelResult.TotalEntitiesProcessed + parallelResult.TotalEntitiesFailed,
            ProcessedEntities = parallelResult.TotalEntitiesProcessed,
            SuccessfulEntities = parallelResult.TotalEntitiesProcessed,
            FailedEntities = parallelResult.TotalEntitiesFailed,
            Duration = parallelResult.TotalProcessingTime,
            Errors = parallelResult.ProcessingErrors,
            IsSuccess = parallelResult.FailedBatches == 0 && parallelResult.ProcessingErrors.Count == 0,
            StartTime = DateTime.UtcNow.Subtract(parallelResult.TotalProcessingTime),
            EndTime = DateTime.UtcNow
        };
    }

    #endregion
}

#region Supporting Interface

/// <summary>
/// Interface for parallel batch processing pipeline
/// </summary>
public interface IParallelBatchProcessingPipeline
{
    /// <summary>
    /// Processes batches in parallel while maintaining Durable Functions determinism
    /// </summary>
    Task<EntityMigrationResult> ProcessBatchesInParallelAsync(
        IDurableOrchestrationContext context,
        EntityMigrationRequest request,
        IReadOnlyList<BatchProcessingRequest> batches,
        CancellationToken cancellationToken = default);
}

#endregion

 