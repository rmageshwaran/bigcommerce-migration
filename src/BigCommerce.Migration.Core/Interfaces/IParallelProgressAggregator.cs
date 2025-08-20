using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// **Thread-Safe Progress Aggregator for Parallel Batch Processing**
/// 
/// Provides thread-safe progress tracking and aggregation for concurrent batch processing
/// with real-time SignalR updates and Durable Functions determinism compliance.
/// 
/// **Key Features:**
/// - Thread-safe concurrent updates from multiple batch processors
/// - Rate-limited SignalR progress notifications to prevent UI flooding
/// - Atomic progress calculations with accurate entity counts
/// - Deterministic progress ordering for Durable Functions replay safety
/// - Real-time throughput and performance metrics
/// 
/// **SignalR Integration:**
/// - Batch completion notifications with detailed context
/// - Aggregated progress percentage updates
/// - Error reporting with batch-specific information
/// - Performance metrics for monitoring dashboards
/// </summary>
public interface IParallelProgressAggregator : IDisposable
{
    #region Batch Progress Tracking

    /// <summary>
    /// Reports completion of a batch with thread-safe aggregation
    /// 
    /// **Thread Safety:**
    /// - Safe to call concurrently from multiple batch processors
    /// - Uses atomic operations for accurate count aggregation
    /// - Maintains deterministic ordering for Durable Functions replay
    /// 
    /// **SignalR Integration:**
    /// - Triggers real-time progress updates (rate-limited)
    /// - Sends batch completion notifications
    /// - Updates overall migration progress percentage
    /// </summary>
    /// <param name="batchNumber">Batch number that completed (1-based)</param>
    /// <param name="entitiesProcessed">Number of entities successfully processed</param>
    /// <param name="entitiesFailed">Number of entities that failed processing</param>
    /// <param name="processingTime">Time taken to process the batch</param>
    /// <param name="batchErrors">Collection of errors that occurred in the batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async progress update operation</returns>
    Task ReportBatchCompletionAsync(
        int batchNumber,
        int entitiesProcessed,
        int entitiesFailed,
        TimeSpan processingTime,
        IEnumerable<string>? batchErrors = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports batch start for tracking active parallel batches
    /// 
    /// **Concurrency Monitoring:**
    /// - Tracks number of active concurrent batches
    /// - Provides real-time concurrency level metrics
    /// - Enables adaptive concurrency adjustments
    /// </summary>
    /// <param name="batchNumber">Batch number that started (1-based)</param>
    /// <param name="batchSize">Number of entities in the batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task ReportBatchStartAsync(
        int batchNumber,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports completion of a sub-batch with thread-safe aggregation
    /// 
    /// **Sub-Batch Granularity:**
    /// - Enables granular progress tracking within pages for 10x more progress updates
    /// - Thread-safe concurrent updates from multiple sub-batch processors
    /// - Provides real-time entity-level progress for smooth UI updates
    /// 
    /// **Global Coordination:**
    /// - Coordinates progress across all parallel batches
    /// - Ensures consistent cumulative entity counts across batches
    /// - Eliminates progress jumps by using global aggregation
    /// </summary>
    /// <param name="parentBatchNumber">Parent batch number (1-based)</param>
    /// <param name="subBatchNumber">Sub-batch number within parent batch (1-based)</param>
    /// <param name="entitiesProcessed">Number of entities successfully processed in this sub-batch</param>
    /// <param name="entitiesFailed">Number of entities that failed processing in this sub-batch</param>
    /// <param name="processingTime">Time taken to process the sub-batch</param>
    /// <param name="subBatchErrors">Collection of errors that occurred in the sub-batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async progress update operation</returns>
    Task ReportSubBatchCompletionAsync(
        int parentBatchNumber,
        int subBatchNumber,
        int entitiesProcessed,
        int entitiesFailed,
        TimeSpan processingTime,
        IEnumerable<string>? subBatchErrors = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Progress Aggregation

    /// <summary>
    /// Gets current aggregated progress across all parallel batches
    /// 
    /// **Thread Safety:**
    /// - Returns consistent snapshot of current progress state
    /// - Safe to call during concurrent batch processing
    /// - Provides atomic view of aggregated counters
    /// </summary>
    /// <returns>Current aggregated progress information</returns>
    Task<AggregatedProgressInfo> GetCurrentProgressAsync();

    /// <summary>
    /// Gets overall progress percentage (0.0 to 1.0)
    /// 
    /// **Calculation:**
    /// - Based on completed batches vs total batches
    /// - Weighted by entity counts for accuracy
    /// - Includes real-time throughput metrics
    /// </summary>
    /// <returns>Progress percentage and throughput information</returns>
    Task<ProgressPercentageInfo> GetProgressPercentageAsync();

    #endregion

    #region Performance Metrics

    /// <summary>
    /// Gets real-time parallel processing performance metrics
    /// 
    /// **Metrics Included:**
    /// - Current and average throughput (entities/second)
    /// - Average batch processing time
    /// - Current concurrency level
    /// - Error rates and trends
    /// - SignalR update delivery rates
    /// </summary>
    /// <returns>Comprehensive performance metrics</returns>
    Task<ParallelProcessingMetrics> GetPerformanceMetricsAsync();

    /// <summary>
    /// Records a concurrency level change for performance tracking
    /// 
    /// **Use Cases:**
    /// - Track adaptive concurrency adjustments
    /// - Monitor impact of concurrency changes on throughput
    /// - Provide data for machine learning optimization
    /// </summary>
    /// <param name="newConcurrency">New concurrency level</param>
    /// <param name="reason">Reason for the change</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task RecordConcurrencyChangeAsync(
        int newConcurrency,
        string reason,
        CancellationToken cancellationToken = default);

    #endregion

    // Note: SignalR Integration section removed - handled by CentralizedProgressBroadcastService

    #region Event Notifications

    /// <summary>
    /// Event triggered when a batch completes processing
    /// 
    /// **Use Cases:**
    /// - Custom progress tracking logic
    /// - Integration with external monitoring systems
    /// - Debugging and troubleshooting
    /// </summary>
    event EventHandler<BatchCompletedEventArgs>? BatchCompleted;

    /// <summary>
    /// Event triggered when overall progress reaches significant milestones
    /// 
    /// **Milestones:**
    /// - 25%, 50%, 75%, 100% completion
    /// - Error rate thresholds
    /// - Performance degradation alerts
    /// </summary>
    event EventHandler<ProgressMilestoneEventArgs>? ProgressMilestone;

    /// <summary>
    /// Event triggered when parallel processing performance changes significantly
    /// 
    /// **Performance Changes:**
    /// - Throughput increases/decreases by >20%
    /// - Error rate changes by >5%
    /// - Concurrency adjustments
    /// </summary>
    event EventHandler<ParallelPerformanceChangedEventArgs>? PerformanceChanged;

    #endregion

    #region Durable Functions Determinism

    /// <summary>
    /// Ensures deterministic progress tracking for Durable Functions replay
    /// 
    /// **Determinism Features:**
    /// - Consistent progress calculation across replays
    /// - Ordered batch completion tracking
    /// - Replay-safe SignalR update patterns
    /// - Atomic state transitions
    /// 
    /// **Replay Behavior:**
    /// - Progress state is reconstructed consistently
    /// - SignalR updates are idempotent during replay
    /// - Batch completion order is preserved
    /// </summary>
    /// <param name="enableDeterministicMode">Whether to enable deterministic mode</param>
    void ConfigureDeterministicMode(bool enableDeterministicMode);

    /// <summary>
    /// Gets deterministic progress state for Durable Functions persistence
    /// 
    /// **State Information:**
    /// - Completed batch numbers (ordered)
    /// - Total entity counts processed
    /// - Aggregate error information
    /// - Performance metrics snapshot
    /// </summary>
    /// <returns>Serializable progress state for persistence</returns>
    Task<DeterministicProgressState> GetDeterministicStateAsync();

    /// <summary>
    /// Restores progress state from Durable Functions persistence
    /// 
    /// **Restoration Process:**
    /// - Rebuilds internal progress counters
    /// - Restores batch completion tracking
    /// - Maintains consistent state for continued processing
    /// </summary>
    /// <param name="state">Previously persisted progress state</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async restoration operation</returns>
    Task RestoreDeterministicStateAsync(
        DeterministicProgressState state,
        CancellationToken cancellationToken = default);

    #endregion
}

#region Supporting Models for Progress Aggregation

/// <summary>
/// Current aggregated progress information across all parallel batches
/// </summary>
public class AggregatedProgressInfo
{
    /// <summary>
    /// Total number of batches in the migration
    /// </summary>
    public int TotalBatches { get; set; }

    /// <summary>
    /// Number of batches completed
    /// </summary>
    public int CompletedBatches { get; set; }

    /// <summary>
    /// Number of batches currently being processed
    /// </summary>
    public int ActiveBatches { get; set; }

    /// <summary>
    /// Number of batches waiting to be processed
    /// </summary>
    public int PendingBatches { get; set; }

    /// <summary>
    /// Total entities processed across all completed batches
    /// </summary>
    public int TotalEntitiesProcessed { get; set; }

    /// <summary>
    /// Total entities failed across all completed batches
    /// </summary>
    public int TotalEntitiesFailed { get; set; }

    /// <summary>
    /// Current throughput in entities per second
    /// </summary>
    public double CurrentThroughput { get; set; }

    /// <summary>
    /// Average batch processing time
    /// </summary>
    public TimeSpan AverageBatchProcessingTime { get; set; }

    /// <summary>
    /// Timestamp of the last progress update
    /// </summary>
    public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Progress percentage information with throughput metrics
/// </summary>
public class ProgressPercentageInfo
{
    /// <summary>
    /// Overall progress percentage (0.0 to 1.0)
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// Estimated time remaining based on current throughput
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Current throughput in entities per second
    /// </summary>
    public double CurrentThroughput { get; set; }

    /// <summary>
    /// Average throughput over the migration
    /// </summary>
    public double AverageThroughput { get; set; }

    /// <summary>
    /// Time elapsed since migration started
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }
}

/// <summary>
/// Event arguments for batch completion
/// </summary>
public class BatchCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Batch number that completed
    /// </summary>
    public int BatchNumber { get; set; }

    /// <summary>
    /// Number of entities processed in the batch
    /// </summary>
    public int EntitiesProcessed { get; set; }

    /// <summary>
    /// Number of entities that failed in the batch
    /// </summary>
    public int EntitiesFailed { get; set; }

    /// <summary>
    /// Time taken to process the batch
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }

    /// <summary>
    /// Errors that occurred in the batch
    /// </summary>
    public List<string> BatchErrors { get; set; } = new();

    /// <summary>
    /// Current overall progress percentage
    /// </summary>
    public double OverallProgressPercentage { get; set; }
}

/// <summary>
/// Event arguments for progress milestones
/// </summary>
public class ProgressMilestoneEventArgs : EventArgs
{
    /// <summary>
    /// Type of milestone reached
    /// </summary>
    public ProgressMilestoneType MilestoneType { get; set; }

    /// <summary>
    /// Progress percentage when milestone was reached
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// Current performance metrics at milestone
    /// </summary>
    public ParallelProcessingMetrics? PerformanceMetrics { get; set; }

    /// <summary>
    /// Additional milestone-specific information
    /// </summary>
    public string AdditionalInfo { get; set; } = string.Empty;
}

/// <summary>
/// Types of progress milestones
/// </summary>
public enum ProgressMilestoneType
{
    /// <summary>Migration started</summary>
    Started,
    /// <summary>25% completion milestone</summary>
    TwentyFivePercent,
    /// <summary>50% completion milestone</summary>
    FiftyPercent,
    /// <summary>75% completion milestone</summary>
    SeventyFivePercent,
    /// <summary>90% completion milestone</summary>
    NinetyPercent,
    /// <summary>Migration completed</summary>
    Completed,
    /// <summary>Error rate exceeded threshold</summary>
    ErrorThresholdExceeded,
    /// <summary>Performance degraded below acceptable levels</summary>
    PerformanceDegraded
}

/// <summary>
/// Deterministic progress state for Durable Functions persistence
/// </summary>
public class DeterministicProgressState
{
    /// <summary>
    /// Ordered list of completed batch numbers
    /// </summary>
    public List<int> CompletedBatches { get; set; } = new();

    /// <summary>
    /// Total entities processed
    /// </summary>
    public int TotalEntitiesProcessed { get; set; }

    /// <summary>
    /// Total entities failed
    /// </summary>
    public int TotalEntitiesFailed { get; set; }

    /// <summary>
    /// Aggregated processing time
    /// </summary>
    public TimeSpan TotalProcessingTime { get; set; }

    /// <summary>
    /// Aggregated errors
    /// </summary>
    public List<string> AggregatedErrors { get; set; } = new();

    /// <summary>
    /// Performance metrics snapshot
    /// </summary>
    public ParallelProcessingMetrics? PerformanceSnapshot { get; set; }

    /// <summary>
    /// Timestamp when state was captured
    /// </summary>
    public DateTime StateCapturedAt { get; set; } = DateTime.UtcNow;
}

#endregion 