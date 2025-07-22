using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Enhanced SignalR service for detailed real-time migration updates
/// Extends the existing IMigrationSignalRService with granular progress broadcasting
/// </summary>
public interface IEnhancedMigrationSignalRService : IMigrationSignalRService
{
    /// <summary>
    /// Broadcast detailed progress update with batch tracking and current context
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="progress">Enhanced progress data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastDetailedProgressAsync(string migrationId, EnhancedMigrationProgress progress, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast current processing context update (what's happening right now)
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="context">Current processing context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastCurrentProcessingContextAsync(string migrationId, ProcessingContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast batch started notification with detailed batch information
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="batchDetails">Detailed batch information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastBatchStartedAsync(string migrationId, string entityType, CurrentBatchDetails batchDetails, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast batch progress update (real-time batch completion)
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="batchProgress">Current batch progress</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastBatchProgressAsync(string migrationId, string entityType, CurrentBatchDetails batchProgress, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast batch completed notification with summary
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="batchNumber">Completed batch number</param>
    /// <param name="batchSummary">Batch completion summary</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastBatchCompletedAsync(string migrationId, string entityType, int batchNumber, BatchCompletionSummary batchSummary, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast remaining workload update
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="remainingWork">Remaining workload information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastRemainingWorkloadAsync(string migrationId, RemainingWorkload remainingWork, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast performance metrics update
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="metrics">Real-time performance metrics</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastPerformanceMetricsAsync(string migrationId, RealTimeMetrics metrics, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast entity phase transition (detailed entity status change)
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="fromPhase">Previous phase</param>
    /// <param name="toPhase">New phase</param>
    /// <param name="phaseData">Phase transition data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastEntityPhaseTransitionAsync(string migrationId, string entityType, string fromPhase, string toPhase, object phaseData, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast migration milestone reached (e.g., 25%, 50%, 75% completion)
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="milestone">Milestone percentage</param>
    /// <param name="milestoneData">Milestone achievement data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastMigrationMilestoneAsync(string migrationId, int milestone, MilestoneData milestoneData, CancellationToken cancellationToken = default);
}

/// <summary>
/// Batch completion summary data
/// </summary>
public class BatchCompletionSummary
{
    /// <summary>
    /// Batch number that was completed
    /// </summary>
    public int BatchNumber { get; set; }
    
    /// <summary>
    /// Total entities processed in batch
    /// </summary>
    public int EntitiesProcessed { get; set; }
    
    /// <summary>
    /// Successful entities in batch
    /// </summary>
    public int SuccessfulEntities { get; set; }
    
    /// <summary>
    /// Failed entities in batch
    /// </summary>
    public int FailedEntities { get; set; }
    
    /// <summary>
    /// Batch processing duration
    /// </summary>
    public TimeSpan ProcessingDuration { get; set; }
    
    /// <summary>
    /// Batch processing speed (entities/second)
    /// </summary>
    public double ProcessingSpeed { get; set; }
    
    /// <summary>
    /// Batch error rate
    /// </summary>
    public double ErrorRate { get; set; }
    
    /// <summary>
    /// Remaining batches after this completion
    /// </summary>
    public int RemainingBatches { get; set; }
    
    /// <summary>
    /// Remaining entities after this completion
    /// </summary>
    public int RemainingEntities { get; set; }
}

/// <summary>
/// Migration milestone achievement data
/// </summary>
public class MilestoneData
{
    /// <summary>
    /// Milestone percentage (25, 50, 75, etc.)
    /// </summary>
    public int Percentage { get; set; }
    
    /// <summary>
    /// Time taken to reach this milestone
    /// </summary>
    public TimeSpan TimeToMilestone { get; set; }
    
    /// <summary>
    /// Entities processed at milestone
    /// </summary>
    public int EntitiesProcessed { get; set; }
    
    /// <summary>
    /// Average processing speed up to milestone
    /// </summary>
    public double AverageSpeed { get; set; }
    
    /// <summary>
    /// Estimated time to completion based on current performance
    /// </summary>
    public TimeSpan EstimatedTimeToCompletion { get; set; }
    
    /// <summary>
    /// Milestone achievement message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Additional milestone data
    /// </summary>
    public Dictionary<string, object> AdditionalData { get; set; } = new();
} 