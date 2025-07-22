using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity functions for broadcasting real-time progress updates via SignalR
/// These are called from orchestrator level to prevent replay issues
/// </summary>
public class BroadcastProgressActivity
{
    private readonly ILogger<BroadcastProgressActivity> _logger;
    private readonly IEnhancedMigrationSignalRService _enhancedSignalRService;

    public BroadcastProgressActivity(
        ILogger<BroadcastProgressActivity> logger,
        IEnhancedMigrationSignalRService enhancedSignalRService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enhancedSignalRService = enhancedSignalRService ?? throw new ArgumentNullException(nameof(enhancedSignalRService));
    }

    /// <summary>
    /// Broadcasts entity initialization to clients
    /// </summary>
    [Function("BroadcastEntityInitialization")]
    public async Task BroadcastEntityInitializationAsync(
        [ActivityTrigger] EntityInitializationBroadcast request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting entity initialization for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);

            // Broadcast entity phase transition
            await _enhancedSignalRService.BroadcastEntityPhaseTransitionAsync(
                request.MigrationId,
                request.EntityType,
                "pending",
                "initializing",
                new { totalBatches = request.TotalBatches, totalEntities = request.TotalEntities },
                cancellationToken);

            // Broadcast initial detailed progress
            var detailedProgress = new EnhancedMigrationProgress
            {
                MigrationId = request.MigrationId,
                CurrentPhase = "initializing",
                CurrentEntity = request.EntityType,
                LastUpdated = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                ElapsedTime = TimeSpan.Zero,
                CurrentProcessing = new ProcessingContext
                {
                    CurrentEntity = request.EntityType,
                    CurrentBatchNumber = 0,
                    CurrentPhase = "initializing",
                    CurrentActivity = "Initializing batch processing",
                    CurrentBatchStartTime = DateTime.UtcNow,
                    CurrentBatch = new CurrentBatchDetails
                    {
                        BatchNumber = 0,
                        BatchSize = 0,
                        ProcessedInBatch = 0,
                        BatchProgressPercentage = 0,
                        BatchProcessingSpeed = 0,
                        BatchElapsedTime = TimeSpan.Zero,
                        EstimatedBatchTimeRemaining = TimeSpan.Zero
                    }
                },
                BatchProgress = new BatchProgressSummary
                {
                    TotalBatches = request.TotalBatches,
                    CompletedBatches = 0,
                    ProcessingBatches = 0,
                    RemainingBatches = request.TotalBatches,
                    BatchCompletionPercentage = 0
                },
                RemainingWork = new RemainingWorkload
                {
                    RemainingEntities = request.TotalEntities,
                    RemainingBatches = request.TotalBatches,
                    EstimatedTimeRemaining = TimeSpan.Zero
                },
                Performance = new RealTimeMetrics
                {
                    CurrentProcessingSpeed = 0,
                    AverageProcessingSpeed = 0,
                    PeakProcessingSpeed = 0,
                    CurrentApiCallRate = 0,
                    CurrentErrorRate = 0
                }
            };

            await _enhancedSignalRService.BroadcastDetailedProgressAsync(
                request.MigrationId,
                detailedProgress,
                cancellationToken);

            _logger.LogInformation("Successfully broadcast entity initialization for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast entity initialization for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
            // Don't rethrow - SignalR failures shouldn't stop migration
        }
    }

    /// <summary>
    /// Broadcasts batch start to clients
    /// </summary>
    [Function("BroadcastBatchStart")]
    public async Task BroadcastBatchStartAsync(
        [ActivityTrigger] BatchStartBroadcast request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting batch start for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);

            var batchDetails = new CurrentBatchDetails
            {
                BatchNumber = request.BatchNumber,
                BatchSize = request.BatchSize,
                ProcessedInBatch = 0,
                BatchProgressPercentage = 0,
                BatchProcessingSpeed = 0,
                BatchElapsedTime = TimeSpan.Zero,
                EstimatedBatchTimeRemaining = TimeSpan.Zero
            };

            await _enhancedSignalRService.BroadcastBatchStartedAsync(
                request.MigrationId,
                request.EntityType,
                batchDetails,
                cancellationToken);

            _logger.LogInformation("Successfully broadcast batch start for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast batch start for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);
            // Don't rethrow - SignalR failures shouldn't stop migration
        }
    }

    /// <summary>
    /// Broadcasts batch completion to clients
    /// </summary>
    [Function("BroadcastBatchCompletion")]
    public async Task BroadcastBatchCompletionAsync(
        [ActivityTrigger] BatchCompletionBroadcast request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting batch completion for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);

            var batchSummary = new BatchCompletionSummary
            {
                BatchNumber = request.BatchNumber,
                EntitiesProcessed = request.EntitiesProcessed,
                SuccessfulEntities = request.SuccessfulEntities,
                FailedEntities = request.FailedEntities,
                ProcessingDuration = request.ProcessingDuration,
                ProcessingSpeed = request.EntitiesProcessed > 0 
                    ? request.EntitiesProcessed / request.ProcessingDuration.TotalSeconds 
                    : 0,
                ErrorRate = request.EntitiesProcessed > 0 
                    ? (double)request.FailedEntities / request.EntitiesProcessed 
                    : 0
            };

            await _enhancedSignalRService.BroadcastBatchCompletedAsync(
                request.MigrationId,
                request.EntityType,
                request.BatchNumber,
                batchSummary,
                cancellationToken);

            _logger.LogInformation("Successfully broadcast batch completion for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast batch completion for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);
            // Don't rethrow - SignalR failures shouldn't stop migration
        }
    }

    /// <summary>
    /// Broadcasts detailed progress update to clients
    /// </summary>
    [Function("BroadcastDetailedProgress")]
    public async Task BroadcastDetailedProgressAsync(
        [ActivityTrigger] DetailedProgressBroadcast request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting detailed progress for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);

            var detailedProgress = new EnhancedMigrationProgress
            {
                MigrationId = request.MigrationId,
                CurrentPhase = request.Phase,
                CurrentEntity = request.EntityType,
                LastUpdated = DateTime.UtcNow,
                StartTime = request.StartTime,
                ElapsedTime = DateTime.UtcNow - request.StartTime,
                CurrentProcessing = new ProcessingContext
                {
                    CurrentEntity = request.EntityType,
                    CurrentBatchNumber = request.CurrentBatch,
                    CurrentPhase = request.Phase,
                    CurrentActivity = request.CurrentActivity,
                    CurrentBatchStartTime = DateTime.UtcNow,
                    CurrentBatch = new CurrentBatchDetails
                    {
                        BatchNumber = request.CurrentBatch,
                        BatchSize = request.BatchSize,
                        ProcessedInBatch = request.ProcessedInBatch,
                        BatchProgressPercentage = request.BatchSize > 0 ? (double)request.ProcessedInBatch / request.BatchSize * 100 : 0,
                        BatchProcessingSpeed = 0,
                        BatchElapsedTime = TimeSpan.Zero,
                        EstimatedBatchTimeRemaining = TimeSpan.Zero
                    }
                },
                BatchProgress = new BatchProgressSummary
                {
                    TotalBatches = request.TotalBatches,
                    CompletedBatches = request.CurrentBatch - 1,
                    ProcessingBatches = 1,
                    RemainingBatches = request.TotalBatches - request.CurrentBatch,
                    BatchCompletionPercentage = request.TotalBatches > 0 ? (double)(request.CurrentBatch - 1) / request.TotalBatches * 100 : 0
                },
                RemainingWork = new RemainingWorkload
                {
                    RemainingEntities = request.RemainingEntities,
                    RemainingBatches = request.TotalBatches - request.CurrentBatch,
                    EstimatedTimeRemaining = TimeSpan.Zero
                },
                Performance = new RealTimeMetrics
                {
                    CurrentProcessingSpeed = 0,
                    AverageProcessingSpeed = 0,
                    PeakProcessingSpeed = 0,
                    CurrentApiCallRate = 0,
                    CurrentErrorRate = 0
                }
            };

            await _enhancedSignalRService.BroadcastDetailedProgressAsync(
                request.MigrationId,
                detailedProgress,
                cancellationToken);

            _logger.LogInformation("Successfully broadcast detailed progress for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast detailed progress for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
            // Don't rethrow - SignalR failures shouldn't stop migration
        }
    }

    /// <summary>
    /// Broadcasts entity completion to clients
    /// </summary>
    [Function("BroadcastEntityCompletion")]
    public async Task BroadcastEntityCompletionAsync(
        [ActivityTrigger] EntityCompletionBroadcast request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting entity completion for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);

            // Broadcast entity phase transition to completed
            await _enhancedSignalRService.BroadcastEntityPhaseTransitionAsync(
                request.MigrationId,
                request.EntityType,
                "processing",
                "completed",
                new { 
                    totalProcessed = request.TotalProcessed,
                    successfulEntities = request.SuccessfulEntities,
                    failedEntities = request.FailedEntities,
                    successRate = request.TotalProcessed > 0 ? (double)request.SuccessfulEntities / request.TotalProcessed * 100 : 0
                },
                cancellationToken);

            // Broadcast final entity completion
            await _enhancedSignalRService.BroadcastEntityCompletionAsync(
                request.MigrationId,
                request.EntityType,
                new {
                    totalProcessed = request.TotalProcessed,
                    successfulEntities = request.SuccessfulEntities,
                    failedEntities = request.FailedEntities,
                    successRate = request.TotalProcessed > 0 ? (double)request.SuccessfulEntities / request.TotalProcessed * 100 : 0,
                    completedAt = DateTime.UtcNow
                },
                cancellationToken);

            _logger.LogInformation("Successfully broadcast entity completion for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast entity completion for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
            // Don't rethrow - SignalR failures shouldn't stop migration
        }
    }
}

// Broadcast request models
public class EntityInitializationBroadcast
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int TotalBatches { get; set; }
    public int TotalEntities { get; set; }
}

public class BatchStartBroadcast
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public int BatchSize { get; set; }
}

public class BatchCompletionBroadcast
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public int EntitiesProcessed { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
}

public class DetailedProgressBroadcast
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string CurrentActivity { get; set; } = string.Empty;
    public int CurrentBatch { get; set; }
    public int TotalBatches { get; set; }
    public int BatchSize { get; set; }
    public int ProcessedInBatch { get; set; }
    public int RemainingEntities { get; set; }
    public DateTime StartTime { get; set; }
}

public class EntityCompletionBroadcast
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int TotalProcessed { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
} 