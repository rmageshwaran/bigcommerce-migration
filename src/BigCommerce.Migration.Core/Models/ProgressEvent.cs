using System;
using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Core.Models
{
    /// <summary>
    /// Base class for all SignalR progress events sent through Azure Storage Queues
    /// SOLID: Single Responsibility - represents a progress event for SignalR broadcasting
    /// Queue-based approach: Decouples SignalR broadcasting from orchestrators
    /// Phase 4.2: Enhanced with soft cancellation token support for efficient cancellation filtering
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
    [JsonDerivedType(typeof(MigrationProgressEvent), "progress")]
    [JsonDerivedType(typeof(BatchProgressEvent), "batch")]
    [JsonDerivedType(typeof(EntityProgressEvent), "entity")]
    [JsonDerivedType(typeof(ErrorProgressEvent), "error")]
    [JsonDerivedType(typeof(StatusProgressEvent), "status")]
    [JsonDerivedType(typeof(SubBatchStartedEvent), "subbatch-started")]
    [JsonDerivedType(typeof(SubBatchCompletedEvent), "subbatch-completed")]
    [JsonDerivedType(typeof(SubBatchMigrationProgressEvent), "subbatch-progress")]
    [JsonDerivedType(typeof(CancellationProgressEvent), "cancellation-progress")]
    public abstract class ProgressEvent
    {
        /// <summary>
        /// Unique identifier for the migration this event belongs to
        /// </summary>
        public string MigrationId { get; set; } = string.Empty;

        /// <summary>
        /// Type of progress event (progress, batch, entity, error, status)
        /// </summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// UTC timestamp when the event was created
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// SignalR hub method to call when broadcasting this event
        /// </summary>
        public string HubMethod { get; set; } = string.Empty;

        /// <summary>
        /// Optional connection ID to send to specific client
        /// </summary>
        public string? ConnectionId { get; set; }

        /// <summary>
        /// Optional group name to broadcast to specific group
        /// </summary>
        public string? GroupName { get; set; }

        /// <summary>
        /// Phase 4.2: Soft cancellation token - indicates if the migration is cancelled
        /// This is passed from orchestrator to avoid storage calls in SignalR functions
        /// </summary>
        public bool IsCancelled { get; set; }

        /// <summary>
        /// Phase 4.2: Cancellation reason (if cancelled)
        /// </summary>
        public string? CancellationReason { get; set; }

        /// <summary>
        /// Phase 4.2: When the cancellation was detected (if cancelled)
        /// </summary>
        public DateTime? CancelledAt { get; set; }
    }

    /// <summary>
    /// Progress event for overall migration progress updates
    /// SOLID: Single Responsibility - handles migration-level progress
    /// </summary>
    public class MigrationProgressEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of MigrationProgressEvent
        /// </summary>
        [JsonConstructor]
        public MigrationProgressEvent()
        {
            EventType = "progress";
            HubMethod = "MigrationProgressUpdated";
        }

        /// <summary>
        /// Overall migration progress percentage (0-100)
        /// </summary>
        public double OverallProgress { get; set; }

        /// <summary>
        /// Current migration status (running, completed, failed, cancelled)
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Total number of entities to process
        /// </summary>
        public int TotalEntities { get; set; }

        /// <summary>
        /// Number of entities successfully processed
        /// </summary>
        public int ProcessedEntities { get; set; }

        /// <summary>
        /// Number of entities that failed to process
        /// </summary>
        public int FailedEntities { get; set; }

        /// <summary>
        /// Number of entities successfully processed (ProcessedEntities - FailedEntities)
        /// Calculated by backend to avoid frontend computation
        /// </summary>
        public int SuccessfulEntities { get; set; }

        /// <summary>
        /// Current entity type being processed
        /// </summary>
        public string? CurrentEntityType { get; set; }

        /// <summary>
        /// When the migration started
        /// Used to calculate elapsed time and processing speed
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// How long the migration has been running
        /// Calculated from StartTime to current time
        /// </summary>
        public TimeSpan? ElapsedTime { get; set; }

        /// <summary>
        /// Current processing speed in entities per second
        /// Calculated as ProcessedEntities / ElapsedTime.TotalSeconds
        /// </summary>
        public double? EntitiesPerSecond { get; set; }

        /// <summary>
        /// Estimated time remaining (optional)
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        /// <summary>
        /// Current batch number being processed
        /// Used for "Current Processing Status" section
        /// </summary>
        public int? CurrentBatchNumber { get; set; }

        /// <summary>
        /// Current processing activity (e.g., "Fetching", "Processing", "Transforming")
        /// Used for "Current Processing Status" section
        /// </summary>
        public string? CurrentActivity { get; set; }

        /// <summary>
        /// Detailed information about the current batch being processed
        /// Used for "Current Processing Status" section batch progress display
        /// </summary>
        public CurrentBatchDetails? CurrentBatch { get; set; }
    }

    /// <summary>
    /// Progress event for batch processing updates
    /// SOLID: Single Responsibility - handles batch-level progress
    /// </summary>
    public class BatchProgressEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of BatchProgressEvent
        /// </summary>
        [JsonConstructor]
        public BatchProgressEvent()
        {
            EventType = "batch";
            HubMethod = "BatchProgressUpdated";
        }

        /// <summary>
        /// Type of entity being processed in this batch
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Batch number within the entity type
        /// </summary>
        public int BatchNumber { get; set; }

        /// <summary>
        /// Total number of batches for this entity type
        /// </summary>
        public int TotalBatches { get; set; }

        /// <summary>
        /// Number of entities in this batch
        /// </summary>
        public int BatchSize { get; set; }

        /// <summary>
        /// Number of entities successfully processed in this batch
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// Number of entities that failed in this batch
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// Batch processing status (starting, processing, completed, failed)
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Time taken to process this batch
        /// </summary>
        public TimeSpan? ProcessingTime { get; set; }
    }

    /// <summary>
    /// Progress event for individual entity processing updates
    /// SOLID: Single Responsibility - handles entity-level progress
    /// </summary>
    public class EntityProgressEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of EntityProgressEvent
        /// </summary>
        [JsonConstructor]
        public EntityProgressEvent()
        {
            EventType = "entity";
            HubMethod = "EntityProgressUpdated";
        }

        /// <summary>
        /// Type of entity (products, categories, brands, etc.)
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Total number of entities of this type
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Number of entities processed so far
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// Number of entities successfully created
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Number of entities that failed to process
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Entity processing status (starting, processing, completed)
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Progress percentage for this entity type (0-100)
        /// </summary>
        public double Progress => TotalCount > 0 ? (double)ProcessedCount / TotalCount * 100 : 0;

        /// <summary>
        /// Time taken to process entities of this type so far
        /// </summary>
        public TimeSpan? ProcessingTime { get; set; }
    }

    /// <summary>
    /// Progress event for error notifications
    /// SOLID: Single Responsibility - handles error broadcasting
    /// </summary>
    public class ErrorProgressEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of ErrorProgressEvent
        /// </summary>
        [JsonConstructor]
        public ErrorProgressEvent()
        {
            EventType = "error";
            HubMethod = "ErrorOccurred";
        }

        /// <summary>
        /// Error severity level (warning, error, critical)
        /// </summary>
        public string Severity { get; set; } = "error";

        /// <summary>
        /// Error message to display
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Error property for backward compatibility (aliases Message)
        /// </summary>
        public string Error 
        { 
            get => Message; 
            set => Message = value; 
        }

        /// <summary>
        /// Entity type where error occurred (optional)
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// Entity ID where error occurred (optional)
        /// </summary>
        public string? EntityId { get; set; }

        /// <summary>
        /// Batch number where error occurred (optional)
        /// </summary>
        public int? BatchNumber { get; set; }

        /// <summary>
        /// Detailed error information for debugging
        /// </summary>
        public string? Details { get; set; }

        /// <summary>
        /// Whether the migration can continue after this error
        /// </summary>
        public bool IsContinuable { get; set; } = true;
    }

    /// <summary>
    /// Progress event for migration status changes
    /// SOLID: Single Responsibility - handles status broadcasting
    /// </summary>
    public class StatusProgressEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of StatusProgressEvent
        /// </summary>
        [JsonConstructor]
        public StatusProgressEvent()
        {
            EventType = "status";
            HubMethod = "MigrationStatusChanged";
        }

        /// <summary>
        /// New migration status (started, running, completed, failed, cancelled)
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Status message to display
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Additional metadata about the status change
        /// </summary>
        public object? Metadata { get; set; }
    }

    /// <summary>
    /// 🎯 SUB-BATCH PROGRESS: Event fired when a sub-batch starts processing
    /// Provides granular progress tracking within pages for real-time dashboard updates
    /// </summary>
    public class SubBatchStartedEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of SubBatchStartedEvent
        /// </summary>
        [JsonConstructor]
        public SubBatchStartedEvent()
        {
            EventType = "subbatch-started";
            HubMethod = "SubBatchStarted";
        }

        /// <summary>
        /// Original page/batch number (1-4 for 192 brands)
        /// </summary>
        public int ParentBatchNumber { get; set; }
        
        /// <summary>
        /// Sub-batch number within the parent batch (1-10 for each page)
        /// </summary>
        public int SubBatchNumber { get; set; }
        
        /// <summary>
        /// Total number of sub-batches in this page
        /// </summary>
        public int TotalSubBatches { get; set; }
        
        /// <summary>
        /// Number of entities in this sub-batch (typically 5)
        /// </summary>
        public int EntitiesInSubBatch { get; set; }
        
        /// <summary>
        /// Maximum concurrency for this sub-batch
        /// </summary>
        public int MaxConcurrency { get; set; }
        
        /// <summary>
        /// Entity type being processed
        /// </summary>
        public string EntityType { get; set; } = string.Empty;
        
        /// <summary>
        /// When this sub-batch started processing
        /// </summary>
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 🎯 SUB-BATCH PROGRESS: Event fired when a sub-batch completes processing
    /// Enables 10x more granular progress updates (40 total vs 4 page-level updates)
    /// </summary>
    public class SubBatchCompletedEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of SubBatchCompletedEvent
        /// </summary>
        [JsonConstructor]
        public SubBatchCompletedEvent()
        {
            EventType = "subbatch-completed";
            HubMethod = "SubBatchCompleted";
        }

        /// <summary>
        /// Original page/batch number (1-4 for 192 brands)
        /// </summary>
        public int ParentBatchNumber { get; set; }
        
        /// <summary>
        /// Sub-batch number within the parent batch (1-10 for each page)
        /// </summary>
        public int SubBatchNumber { get; set; }
        
        /// <summary>
        /// Total number of sub-batches in this page
        /// </summary>
        public int TotalSubBatches { get; set; }
        
        /// <summary>
        /// Number of entities successfully processed in this sub-batch
        /// </summary>
        public int SuccessfulEntities { get; set; }
        
        /// <summary>
        /// Number of entities that failed in this sub-batch
        /// </summary>
        public int FailedEntities { get; set; }
        
        /// <summary>
        /// Total entities processed in this sub-batch
        /// </summary>
        public int TotalEntities { get; set; }
        
        /// <summary>
        /// Entity type being processed
        /// </summary>
        public string EntityType { get; set; } = string.Empty;
        
        /// <summary>
        /// Time taken to process this sub-batch
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }
        
        /// <summary>
        /// When this sub-batch completed
        /// </summary>
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// List of errors that occurred in this sub-batch
        /// </summary>
        public List<string> Errors { get; set; } = new();
        
        /// <summary>
        /// Cumulative successful entities across all completed sub-batches in this migration
        /// </summary>
        public int CumulativeSuccessfulEntities { get; set; }
        
        /// <summary>
        /// Cumulative failed entities across all completed sub-batches in this migration
        /// </summary>
        public int CumulativeFailedEntities { get; set; }
        
        /// <summary>
        /// Total entities expected to be processed in the entire migration
        /// </summary>
        public int TotalMigrationEntities { get; set; }
        
        /// <summary>
        /// Progress percentage based on completed sub-batches (0-100)
        /// Calculated as: (completed sub-batches / total sub-batches) * 100
        /// </summary>
        public double ProgressPercentage { get; set; }
        
        /// <summary>
        /// Estimated time remaining based on current processing speed
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }
    }

    /// <summary>
    /// 🎯 SUB-BATCH PROGRESS: Aggregate progress event for the entire migration
    /// Combines progress from all pages and sub-batches for dashboard display
    /// </summary>
    public class SubBatchMigrationProgressEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of SubBatchMigrationProgressEvent
        /// </summary>
        [JsonConstructor]
        public SubBatchMigrationProgressEvent()
        {
            EventType = "subbatch-progress";
            HubMethod = "SubBatchMigrationProgress";
        }

        /// <summary>
        /// Total number of pages in this migration
        /// </summary>
        public int TotalPages { get; set; }
        
        /// <summary>
        /// Number of pages that have completed processing
        /// </summary>
        public int CompletedPages { get; set; }
        
        /// <summary>
        /// Total number of sub-batches across all pages
        /// </summary>
        public int TotalSubBatches { get; set; }
        
        /// <summary>
        /// Number of sub-batches that have completed processing
        /// </summary>
        public int CompletedSubBatches { get; set; }
        
        /// <summary>
        /// Total entities successfully processed across all sub-batches
        /// </summary>
        public int TotalSuccessfulEntities { get; set; }
        
        /// <summary>
        /// Total entities that failed across all sub-batches
        /// </summary>
        public int TotalFailedEntities { get; set; }
        
        /// <summary>
        /// Total entities expected to be processed
        /// </summary>
        public int TotalExpectedEntities { get; set; }
        
        /// <summary>
        /// Current processing rate (entities per second)
        /// </summary>
        public double ProcessingRate { get; set; }
        
        /// <summary>
        /// Overall progress percentage (0-100)
        /// </summary>
        public double OverallProgressPercentage { get; set; }
        
        /// <summary>
        /// Estimated time remaining for the entire migration
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }
        
        /// <summary>
        /// When this progress update was generated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Time elapsed since migration started
        /// </summary>
        public TimeSpan ElapsedTime { get; set; }
        
        /// <summary>
        /// List of recent errors across all sub-batches
        /// </summary>
        public List<string> RecentErrors { get; set; } = new();
        
        /// <summary>
        /// Performance metrics for monitoring
        /// </summary>
        public Dictionary<string, object> PerformanceMetrics { get; set; } = new();
    }

    /// <summary>
    /// Progress event for live cancellation updates and real-time dashboard notifications
    /// Part of Task 7.1 Live Cancellation Integration
    /// SOLID: Single Responsibility - handles cancellation progress communication
    /// </summary>
    public class CancellationProgressEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of CancellationProgressEvent
        /// </summary>
        [JsonConstructor]
        public CancellationProgressEvent()
        {
            EventType = "cancellation-progress";
            HubMethod = "CancellationProgressUpdated";
        }

        /// <summary>
        /// Cancellation scope level (Migration, EntityType, Batch, Store)
        /// </summary>
        public CancellationScope Scope { get; set; }

        /// <summary>
        /// Current cancellation status (requested, processing, completed, failed)
        /// </summary>
        public string Status { get; set; } = "requested";

        /// <summary>
        /// Human-readable reason for the cancellation
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Entity type being cancelled (when Scope = EntityType)
        /// Examples: "categories", "products", "brands"
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// Batch identifier being cancelled (when Scope = Batch)
        /// </summary>
        public string? BatchId { get; set; }

        /// <summary>
        /// Store identifier being cancelled (when Scope = Store)
        /// </summary>
        public string? StoreId { get; set; }

        /// <summary>
        /// Estimated time for cancellation to complete (in seconds)
        /// </summary>
        public int? EstimatedTimeToComplete { get; set; }

        /// <summary>
        /// When the cancellation was propagated to all instances
        /// </summary>
        public DateTime? PropagatedAt { get; set; }

        /// <summary>
        /// Who requested the cancellation (user email, system, etc.)
        /// </summary>
        public string? RequestedBy { get; set; }

        /// <summary>
        /// Number of active instances that need to process the cancellation
        /// </summary>
        public int? TotalInstances { get; set; }

        /// <summary>
        /// Number of instances that have acknowledged the cancellation
        /// </summary>
        public int? AcknowledgedInstances { get; set; }

        /// <summary>
        /// Additional context information for the cancellation
        /// </summary>
        public Dictionary<string, object>? AdditionalContext { get; set; }
    }
} 