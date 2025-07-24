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
        /// Current entity type being processed
        /// </summary>
        public string? CurrentEntityType { get; set; }

        /// <summary>
        /// Estimated time remaining (optional)
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }
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
} 