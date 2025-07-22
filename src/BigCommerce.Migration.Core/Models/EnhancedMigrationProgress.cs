using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Enhanced migration progress with detailed batch tracking and current processing context
/// Extends the existing MigrationProgress with granular real-time information
/// </summary>
public class EnhancedMigrationProgress : MigrationProgress
{
    /// <summary>
    /// Current processing context - what's happening right now
    /// </summary>
    public ProcessingContext CurrentProcessing { get; set; } = new();
    
    /// <summary>
    /// Batch-level progress tracking
    /// </summary>
    public BatchProgressSummary BatchProgress { get; set; } = new();
    
    /// <summary>
    /// Remaining entities and batches calculation
    /// </summary>
    public RemainingWorkload RemainingWork { get; set; } = new();
    
    /// <summary>
    /// Real-time performance metrics
    /// </summary>
    public RealTimeMetrics Performance { get; set; } = new();
}

/// <summary>
/// Current processing context - what's happening right now
/// </summary>
public class ProcessingContext
{
    /// <summary>
    /// Current entity being processed
    /// </summary>
    public string CurrentEntity { get; set; } = string.Empty;
    
    /// <summary>
    /// Current batch being processed
    /// </summary>
    public int CurrentBatchNumber { get; set; }
    
    /// <summary>
    /// Current batch details
    /// </summary>
    public CurrentBatchDetails CurrentBatch { get; set; } = new();
    
    /// <summary>
    /// Current phase description
    /// </summary>
    public string CurrentPhase { get; set; } = string.Empty;
    
    /// <summary>
    /// Current activity description
    /// </summary>
    public string CurrentActivity { get; set; } = string.Empty;
    
    /// <summary>
    /// Processing start time for current batch
    /// </summary>
    public DateTime CurrentBatchStartTime { get; set; }
    
    /// <summary>
    /// Estimated completion time for current batch
    /// </summary>
    public DateTime EstimatedBatchCompletion { get; set; }
}

/// <summary>
/// Current batch being processed details
/// </summary>
public class CurrentBatchDetails
{
    /// <summary>
    /// Batch ID or number
    /// </summary>
    public int BatchNumber { get; set; }
    
    /// <summary>
    /// Total entities in current batch
    /// </summary>
    public int BatchSize { get; set; }
    
    /// <summary>
    /// Entities processed in current batch
    /// </summary>
    public int ProcessedInBatch { get; set; }
    
    /// <summary>
    /// Batch progress percentage
    /// </summary>
    public double BatchProgressPercentage { get; set; }
    
    /// <summary>
    /// Batch processing speed (entities/second)
    /// </summary>
    public double BatchProcessingSpeed { get; set; }
    
    /// <summary>
    /// Time elapsed for current batch
    /// </summary>
    public TimeSpan BatchElapsedTime { get; set; }
    
    /// <summary>
    /// Estimated time remaining for current batch
    /// </summary>
    public TimeSpan EstimatedBatchTimeRemaining { get; set; }
}

/// <summary>
/// Batch progress summary across all entities
/// </summary>
public class BatchProgressSummary
{
    /// <summary>
    /// Total batches across all entities
    /// </summary>
    public int TotalBatches { get; set; }
    
    /// <summary>
    /// Completed batches
    /// </summary>
    public int CompletedBatches { get; set; }
    
    /// <summary>
    /// Currently processing batches
    /// </summary>
    public int ProcessingBatches { get; set; }
    
    /// <summary>
    /// Remaining batches
    /// </summary>
    public int RemainingBatches { get; set; }
    
    /// <summary>
    /// Batch completion percentage
    /// </summary>
    public double BatchCompletionPercentage { get; set; }
    
    /// <summary>
    /// Entity-specific batch progress
    /// </summary>
    public Dictionary<string, EntityBatchProgress> EntityBatches { get; set; } = new();
}

/// <summary>
/// Batch progress for a specific entity
/// </summary>
public class EntityBatchProgress
{
    /// <summary>
    /// Entity type
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Total batches for this entity
    /// </summary>
    public int TotalBatches { get; set; }
    
    /// <summary>
    /// Completed batches for this entity
    /// </summary>
    public int CompletedBatches { get; set; }
    
    /// <summary>
    /// Current batch being processed
    /// </summary>
    public int CurrentBatch { get; set; }
    
    /// <summary>
    /// Remaining batches for this entity
    /// </summary>
    public int RemainingBatches { get; set; }
    
    /// <summary>
    /// Batch size for this entity
    /// </summary>
    public int BatchSize { get; set; }
    
    /// <summary>
    /// Entity batch completion percentage
    /// </summary>
    public double CompletionPercentage { get; set; }
}

/// <summary>
/// Remaining workload calculations
/// </summary>
public class RemainingWorkload
{
    /// <summary>
    /// Total entities remaining
    /// </summary>
    public int RemainingEntities { get; set; }
    
    /// <summary>
    /// Total batches remaining
    /// </summary>
    public int RemainingBatches { get; set; }
    
    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan EstimatedTimeRemaining { get; set; }
    
    /// <summary>
    /// Entities remaining by type
    /// </summary>
    public Dictionary<string, int> RemainingByEntityType { get; set; } = new();
    
    /// <summary>
    /// Batches remaining by type
    /// </summary>
    public Dictionary<string, int> RemainingBatchesByEntityType { get; set; } = new();
}

/// <summary>
/// Real-time performance metrics
/// </summary>
public class RealTimeMetrics
{
    /// <summary>
    /// Current processing speed (entities/second)
    /// </summary>
    public double CurrentProcessingSpeed { get; set; }
    
    /// <summary>
    /// Average processing speed
    /// </summary>
    public double AverageProcessingSpeed { get; set; }
    
    /// <summary>
    /// Peak processing speed achieved
    /// </summary>
    public double PeakProcessingSpeed { get; set; }
    
    /// <summary>
    /// Current API call rate (calls/second)
    /// </summary>
    public double CurrentApiCallRate { get; set; }
    
    /// <summary>
    /// Current error rate
    /// </summary>
    public double CurrentErrorRate { get; set; }
    
    /// <summary>
    /// Performance trend (improving, stable, declining)
    /// </summary>
    public string PerformanceTrend { get; set; } = "stable";
    
    /// <summary>
    /// Last performance calculation time
    /// </summary>
    public DateTime LastCalculation { get; set; }
}

/// <summary>
/// Enhanced SignalR event for detailed progress updates
/// </summary>
public class DetailedProgressEvent
{
    /// <summary>
    /// Migration ID
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Event timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Event type
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    
    /// <summary>
    /// Enhanced progress data
    /// </summary>
    public EnhancedMigrationProgress Progress { get; set; } = new();
    
    /// <summary>
    /// Additional event data
    /// </summary>
    public Dictionary<string, object> EventData { get; set; } = new();
} 