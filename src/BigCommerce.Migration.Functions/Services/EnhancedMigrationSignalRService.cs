using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Functions.Hubs;

namespace BigCommerce.Migration.Functions.Services;

/// <summary>
/// Enhanced SignalR service implementation for detailed real-time migration updates
/// Extends the existing MigrationSignalRService with granular progress broadcasting
/// </summary>
public class EnhancedMigrationSignalRService : IEnhancedMigrationSignalRService
{
    private readonly IMigrationSignalRService _baseSignalRService;
    private readonly IMigrationHub _migrationHub;
    private readonly ILogger<EnhancedMigrationSignalRService> _logger;

    public EnhancedMigrationSignalRService(
        IMigrationSignalRService baseSignalRService,
        IMigrationHub migrationHub,
        ILogger<EnhancedMigrationSignalRService> logger)
    {
        _baseSignalRService = baseSignalRService ?? throw new ArgumentNullException(nameof(baseSignalRService));
        _migrationHub = migrationHub ?? throw new ArgumentNullException(nameof(migrationHub));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region IEnhancedMigrationSignalRService Implementation

    /// <inheritdoc />
    public async Task BroadcastDetailedProgressAsync(string migrationId, EnhancedMigrationProgress progress, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting detailed progress for migration {MigrationId}", migrationId);

            var detailedEvent = new DetailedProgressEvent
            {
                MigrationId = migrationId,
                EventType = "DetailedProgress",
                Progress = progress,
                EventData = new Dictionary<string, object>
                {
                    ["currentEntity"] = progress.CurrentProcessing.CurrentEntity,
                    ["currentBatch"] = progress.CurrentProcessing.CurrentBatchNumber,
                    ["totalBatches"] = progress.BatchProgress.TotalBatches,
                    ["remainingBatches"] = progress.RemainingWork.RemainingBatches,
                    ["remainingEntities"] = progress.RemainingWork.RemainingEntities,
                    ["processingSpeed"] = progress.Performance.CurrentProcessingSpeed,
                    ["estimatedCompletion"] = progress.RemainingWork.EstimatedTimeRemaining.ToString()
                }
            };

            await _migrationHub.SendToMigrationGroup(migrationId, "DetailedProgress", detailedEvent, cancellationToken);
            
            _logger.LogDebug("Successfully broadcasted detailed progress for migration {MigrationId}", migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast detailed progress for migration {MigrationId}", migrationId);
            // Don't rethrow to avoid breaking migration flow
        }
    }

    /// <inheritdoc />
    public async Task BroadcastCurrentProcessingContextAsync(string migrationId, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting current processing context for migration {MigrationId}: Entity={Entity}, Batch={Batch}", 
                migrationId, context.CurrentEntity, context.CurrentBatchNumber);

            var contextEvent = new
            {
                MigrationId = migrationId,
                EventType = "ProcessingContext",
                Timestamp = DateTime.UtcNow,
                Context = context,
                Summary = new
                {
                    currentEntity = context.CurrentEntity,
                    currentBatch = context.CurrentBatchNumber,
                    currentPhase = context.CurrentPhase,
                    currentActivity = context.CurrentActivity,
                    batchProgress = context.CurrentBatch.BatchProgressPercentage,
                    batchEta = context.EstimatedBatchCompletion
                }
            };

            await _migrationHub.SendToMigrationGroup(migrationId, "ProcessingContext", contextEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast processing context for migration {MigrationId}", migrationId);
        }
    }

    /// <inheritdoc />
    public async Task BroadcastBatchStartedAsync(string migrationId, string entityType, CurrentBatchDetails batchDetails, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting batch started for migration {MigrationId}: {EntityType} batch {BatchNumber}", 
                migrationId, entityType, batchDetails.BatchNumber);

            var batchStartEvent = new
            {
                MigrationId = migrationId,
                EventType = "BatchStarted",
                Timestamp = DateTime.UtcNow,
                EntityType = entityType,
                BatchDetails = batchDetails,
                Message = $"Started processing {entityType} batch {batchDetails.BatchNumber} ({batchDetails.BatchSize} entities)"
            };

            await _migrationHub.SendToMigrationGroup(migrationId, "BatchStarted", batchStartEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast batch started for migration {MigrationId}", migrationId);
        }
    }

    /// <inheritdoc />
    public async Task BroadcastBatchProgressAsync(string migrationId, string entityType, CurrentBatchDetails batchProgress, CancellationToken cancellationToken = default)
    {
        try
        {
            var batchProgressEvent = new
            {
                MigrationId = migrationId,
                EventType = "BatchProgress",
                Timestamp = DateTime.UtcNow,
                EntityType = entityType,
                BatchProgress = batchProgress,
                Summary = new
                {
                    batchNumber = batchProgress.BatchNumber,
                    progressPercentage = batchProgress.BatchProgressPercentage,
                    processedInBatch = batchProgress.ProcessedInBatch,
                    batchSize = batchProgress.BatchSize,
                    processingSpeed = batchProgress.BatchProcessingSpeed,
                    estimatedTimeRemaining = batchProgress.EstimatedBatchTimeRemaining.ToString()
                }
            };

            await _migrationHub.SendToMigrationGroup(migrationId, "BatchProgress", batchProgressEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast batch progress for migration {MigrationId}", migrationId);
        }
    }

    /// <inheritdoc />
    public async Task BroadcastBatchCompletedAsync(string migrationId, string entityType, int batchNumber, BatchCompletionSummary batchSummary, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting batch completed for migration {MigrationId}: {EntityType} batch {BatchNumber} - {Successful}/{Total} successful", 
                migrationId, entityType, batchNumber, batchSummary.SuccessfulEntities, batchSummary.EntitiesProcessed);

            var batchCompletedEvent = new
            {
                MigrationId = migrationId,
                EventType = "BatchCompleted",
                Timestamp = DateTime.UtcNow,
                EntityType = entityType,
                BatchNumber = batchNumber,
                Summary = batchSummary,
                Message = $"Completed {entityType} batch {batchNumber}: {batchSummary.SuccessfulEntities}/{batchSummary.EntitiesProcessed} successful ({batchSummary.RemainingBatches} batches remaining)"
            };

            await _migrationHub.SendToMigrationGroup(migrationId, "BatchCompleted", batchCompletedEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast batch completed for migration {MigrationId}", migrationId);
        }
    }

    /// <inheritdoc />
    public async Task BroadcastRemainingWorkloadAsync(string migrationId, RemainingWorkload remainingWork, CancellationToken cancellationToken = default)
    {
        try
        {
            var remainingWorkEvent = new
            {
                MigrationId = migrationId,
                EventType = "RemainingWorkload",
                Timestamp = DateTime.UtcNow,
                RemainingWork = remainingWork,
                Summary = new
                {
                    remainingEntities = remainingWork.RemainingEntities,
                    remainingBatches = remainingWork.RemainingBatches,
                    estimatedTimeRemaining = remainingWork.EstimatedTimeRemaining.ToString(),
                    entitiesByType = remainingWork.RemainingByEntityType,
                    batchesByType = remainingWork.RemainingBatchesByEntityType
                }
            };

            await _migrationHub.SendToMigrationGroup(migrationId, "RemainingWorkload", remainingWorkEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast remaining workload for migration {MigrationId}", migrationId);
        }
    }

    /// <inheritdoc />
    public async Task BroadcastPerformanceMetricsAsync(string migrationId, RealTimeMetrics metrics, CancellationToken cancellationToken = default)
    {
        try
        {
            var performanceEvent = new
            {
                MigrationId = migrationId,
                EventType = "PerformanceMetrics",
                Timestamp = DateTime.UtcNow,
                Metrics = metrics,
                Summary = new
                {
                    currentSpeed = metrics.CurrentProcessingSpeed,
                    averageSpeed = metrics.AverageProcessingSpeed,
                    peakSpeed = metrics.PeakProcessingSpeed,
                    apiCallRate = metrics.CurrentApiCallRate,
                    errorRate = metrics.CurrentErrorRate,
                    trend = metrics.PerformanceTrend
                }
            };

            await _migrationHub.SendToMigrationGroup(migrationId, "PerformanceMetrics", performanceEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast performance metrics for migration {MigrationId}", migrationId);
        }
    }

    /// <inheritdoc />
    public async Task BroadcastEntityPhaseTransitionAsync(string migrationId, string entityType, string fromPhase, string toPhase, object phaseData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting entity phase transition for migration {MigrationId}: {EntityType} {FromPhase} → {ToPhase}", 
                migrationId, entityType, fromPhase, toPhase);

            var transitionEvent = new
            {
                MigrationId = migrationId,
                EventType = "EntityPhaseTransition",
                Timestamp = DateTime.UtcNow,
                EntityType = entityType,
                FromPhase = fromPhase,
                ToPhase = toPhase,
                PhaseData = phaseData,
                Message = $"{entityType} transitioned from {fromPhase} to {toPhase}"
            };

            await _migrationHub.SendToMigrationGroup(migrationId, "EntityPhaseTransition", transitionEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast entity phase transition for migration {MigrationId}", migrationId);
        }
    }

    /// <inheritdoc />
    public async Task BroadcastMigrationMilestoneAsync(string migrationId, int milestone, MilestoneData milestoneData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Broadcasting migration milestone for migration {MigrationId}: {Milestone}% completed", 
                migrationId, milestone);

            var milestoneEvent = new
            {
                MigrationId = migrationId,
                EventType = "MigrationMilestone",
                Timestamp = DateTime.UtcNow,
                Milestone = milestone,
                MilestoneData = milestoneData,
                Message = $"🎉 Migration {milestone}% complete! {milestoneData.Message}"
            };

            await _migrationHub.SendToMigrationGroup(migrationId, "MigrationMilestone", milestoneEvent, cancellationToken);
            
            // Also send to all connected clients as a celebration notification
            await _migrationHub.SendToAllClients("MigrationMilestone", milestoneEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast migration milestone for migration {MigrationId}", migrationId);
        }
    }

    #endregion

    #region Delegate to Base Service (Existing Methods)

    /// <inheritdoc />
    public async Task BroadcastProgressUpdateAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastProgressUpdateAsync(migrationId, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastStatusUpdateAsync(string migrationId, object status, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastStatusUpdateAsync(migrationId, status, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastEntityStartAsync(string migrationId, string entityType, int totalCount, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastEntityStartAsync(migrationId, entityType, totalCount, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastEntityCompletionAsync(string migrationId, string entityType, object results, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastEntityCompletionAsync(migrationId, entityType, results, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastBatchCompletionAsync(string migrationId, string entityType, int batchNumber, object batchResults, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastBatchCompletionAsync(migrationId, entityType, batchNumber, batchResults, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastSystemHealthAsync(object healthData, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastSystemHealthAsync(healthData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastMigrationStartedAsync(string migrationId, object migrationData, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastMigrationStartedAsync(migrationId, migrationData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastMigrationCompletedAsync(string migrationId, object completionData, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastMigrationCompletedAsync(migrationId, completionData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastMigrationFailedAsync(string migrationId, object errorData, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastMigrationFailedAsync(migrationId, errorData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastMigrationCancelledAsync(string migrationId, object cancellationData, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastMigrationCancelledAsync(migrationId, cancellationData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastEntityPhaseStartAsync(string migrationId, string entityType, object phaseData, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastEntityPhaseStartAsync(migrationId, entityType, phaseData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BroadcastEntityPhaseCompletedAsync(string migrationId, string entityType, object completionData, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastEntityPhaseCompletedAsync(migrationId, entityType, completionData, cancellationToken);
    }

    public async Task BroadcastErrorNotificationAsync(string migrationId, object errorData, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastErrorNotificationAsync(migrationId, errorData, cancellationToken);
    }

    public async Task BroadcastSystemAlertAsync(string alertType, object alertData, CancellationToken cancellationToken = default)
    {
        await _baseSignalRService.BroadcastSystemAlertAsync(alertType, alertData, cancellationToken);
    }

    #endregion
} 