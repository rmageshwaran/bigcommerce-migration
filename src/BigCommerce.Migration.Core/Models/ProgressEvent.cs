using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Core.Models
{
    /// <summary>
    /// Simplified base class for all SignalR progress events
    /// Following the clean architecture approach with only essential events
    /// 
    /// ✅ SIMPLIFIED APPROACH:
    /// - Only 4 event types (vs 11 previously)
    /// - Chunk-level progress only (no sub-batches)
    /// - Clean lifecycle: Start → ChunkProgress → Complete/Error
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
    [JsonDerivedType(typeof(MigrationStartedEvent), "migration-started")]
    [JsonDerivedType(typeof(EntityStartedEvent), "entity-started")]
    [JsonDerivedType(typeof(EntityChunkProgressEvent), "chunk-progress")]
    [JsonDerivedType(typeof(EntityCompletedEvent), "entity-completed")]
    [JsonDerivedType(typeof(MigrationCompletedEvent), "migration-completed")]
    [JsonDerivedType(typeof(ErrorProgressEvent), "error")]
    public abstract class ProgressEvent
    {
        /// <summary>
        /// Unique identifier for the migration this event belongs to
        /// </summary>
        public string MigrationId { get; set; } = string.Empty;

        /// <summary>
        /// Type of progress event
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
        /// Indicates if the migration is cancelled
        /// </summary>
        public bool IsCancelled { get; set; }

        /// <summary>
        /// Cancellation reason (if cancelled)
        /// </summary>
        public string? CancellationReason { get; set; }

        /// <summary>
        /// When the cancellation was detected (if cancelled)
        /// </summary>
        public DateTime? CancelledAt { get; set; }
    }

    /// <summary>
    /// Event fired when a migration starts
    /// Provides initial migration context and entities to be processed
    /// </summary>
    public class MigrationStartedEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of MigrationStartedEvent
        /// </summary>
        [JsonConstructor]
        public MigrationStartedEvent()
        {
            EventType = "migration-started";
            HubMethod = "MigrationStarted";
        }

        /// <summary>
        /// Source store identifier
        /// </summary>
        public string SourceStore { get; set; } = string.Empty;

        /// <summary>
        /// Destination store identifier
        /// </summary>
        public string DestinationStore { get; set; } = string.Empty;

        /// <summary>
        /// When the migration started
        /// </summary>
        public DateTime StartDateTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Estimated completion time (if available)
        /// </summary>
        public DateTime? EstimatedEndTime { get; set; }

        /// <summary>
        /// List of entities that will be migrated
        /// </summary>
        public List<EntityInfo> Entities { get; set; } = new();
    }

    /// <summary>
    /// Event triggered when an individual entity type starts processing (after discovery)
    /// </summary>
    public class EntityStartedEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of EntityStartedEvent
        /// </summary>
        public EntityStartedEvent()
        {
            EventType = "entity-started";
            HubMethod = "EntityStarted";
        }

        /// <summary>
        /// Type of entity that is starting (e.g., products, options, modifiers)
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Total count of entities discovered for this type
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Estimated duration for processing this entity type (optional)
        /// </summary>
        public int? EstimatedDurationMs { get; set; }

        /// <summary>
        /// Status message for this entity start
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Date and time when this entity started processing
        /// </summary>
        public string StartDateTime { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event fired when an entity phase completes processing
    /// This provides explicit completion notification instead of inferring from chunk progress
    /// </summary>
    public class EntityCompletedEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of EntityCompletedEvent
        /// </summary>
        public EntityCompletedEvent()
        {
            EventType = "entity-completed";
            HubMethod = "EntityCompleted";
        }

        /// <summary>
        /// Type of entity that completed (e.g., "products", "variants", "categories")
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Total entities that were processed for this type
        /// </summary>
        public int TotalProcessed { get; set; }

        /// <summary>
        /// Total entities successfully processed for this type
        /// </summary>
        public int TotalSuccess { get; set; }

        /// <summary>
        /// Total entities that failed processing for this type
        /// </summary>
        public int TotalFailed { get; set; }

        /// <summary>
        /// Total entities that were skipped for this type
        /// </summary>
        public int TotalSkipped { get; set; }

        /// <summary>
        /// Total entities that were cancelled for this type
        /// </summary>
        public int TotalCancelled { get; set; }

        /// <summary>
        /// Final status of the entity processing
        /// </summary>
        public string Status { get; set; } = "completed";

        /// <summary>
        /// Whether to show total count in UI (false for dynamic discovery phases)
        /// </summary>
        public bool ShowTotalCount { get; set; } = true;

        /// <summary>
        /// Total time taken to process this entity type (in milliseconds for frontend compatibility)
        /// </summary>
        public long ProcessingTimeMs { get; set; }

        /// <summary>
        /// Date and time when this entity completed processing
        /// </summary>
        public string CompletedDateTime { get; set; } = string.Empty;

        /// <summary>
        /// Completion message for this entity
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event fired when an entity chunk completes processing
    /// This is the main progress event - fired after each chunk is processed
    /// </summary>
    public class EntityChunkProgressEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of EntityChunkProgressEvent
        /// </summary>
        [JsonConstructor]
        public EntityChunkProgressEvent()
        {
            EventType = "chunk-progress";
            HubMethod = "EntityChunkProgress";
        }

        /// <summary>
        /// Type of entity being processed (e.g., "products", "categories", "brands")
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Chunk number that was just processed
        /// </summary>
        public int ChunkNumber { get; set; }

        /// <summary>
        /// Total number of chunks for this entity type
        /// </summary>
        public int TotalChunks { get; set; }

        /// <summary>
        /// Total entities processed cumulatively across all chunks for this entity type
        /// </summary>
        public int TotalProcessed { get; set; }

        /// <summary>
        /// Total entities successfully processed cumulatively across all chunks for this entity type
        /// </summary>
        public int TotalSuccess { get; set; }

        /// <summary>
        /// Total entities that failed processing cumulatively across all chunks for this entity type
        /// </summary>
        public int TotalFailed { get; set; }

        /// <summary>
        /// Total entities that were skipped in this chunk
        /// </summary>
        public int TotalSkipped { get; set; }

        /// <summary>
        /// Total entities that were cancelled in this chunk
        /// </summary>
        public int TotalCancelled { get; set; }

        /// <summary>
        /// Total entities expected for this entity type (from discovery phase)
        /// </summary>
        public int TotalEntitiesForType { get; set; }

        /// <summary>
        /// Progress percentage for this entity type (0-100)
        /// </summary>
        public double ProgressPercentage { get; set; }

        /// <summary>
        /// Status of the chunk processing
        /// </summary>
        public string Status { get; set; } = "completed";

        /// <summary>
        /// Whether to show total count in UI (false for dynamic discovery phases)
        /// </summary>
        public bool ShowTotalCount { get; set; } = true;

        /// <summary>
        /// Time taken to process this chunk
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>
        /// Current processing rate (entities per second)
        /// </summary>
        public double? EntitiesPerSecond { get; set; }

        /// <summary>
        /// Estimated time remaining for this entity type
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }
    }

    /// <summary>
    /// Event fired when a migration completes (successfully, with errors, or cancelled)
    /// Provides final summary of the entire migration
    /// </summary>
    public class MigrationCompletedEvent : ProgressEvent
    {
        /// <summary>
        /// Initializes a new instance of MigrationCompletedEvent
        /// </summary>
        [JsonConstructor]
        public MigrationCompletedEvent()
        {
            EventType = "migration-completed";
            HubMethod = "MigrationCompleted";
        }

        /// <summary>
        /// Final migration status
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// When the migration ended
        /// </summary>
        public DateTime EndDateTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Total duration of the migration
        /// </summary>
        public TimeSpan TotalDuration { get; set; }

        /// <summary>
        /// Final counts across all entities
        /// </summary>
        public MigrationFinalCounts FinalCounts { get; set; } = new();

        /// <summary>
        /// Summary information for each entity type processed
        /// </summary>
        public List<EntitySummary> Entities { get; set; } = new();

        /// <summary>
        /// Optional completion message
        /// </summary>
        public string? Message { get; set; }
    }

    /// <summary>
    /// Event fired when an error occurs during migration
    /// Used for error notifications and logging
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
        /// Error severity level
        /// </summary>
        public string Severity { get; set; } = "error";

        /// <summary>
        /// Error message to display
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Entity type where error occurred (optional)
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// Entity ID where error occurred (optional)
        /// </summary>
        public string? EntityId { get; set; }

        /// <summary>
        /// Chunk number where error occurred (optional)
        /// </summary>
        public int? ChunkNumber { get; set; }

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
    /// Information about an entity type in the migration
    /// </summary>
    public class EntityInfo
    {
        /// <summary>
        /// Type of entity (e.g., "products", "categories", "brands")
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Total number of entities to be processed
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Estimated processing time for this entity type
        /// </summary>
        public TimeSpan? EstimatedDuration { get; set; }
    }

    /// <summary>
    /// Final counts for the entire migration
    /// </summary>
    public class MigrationFinalCounts
    {
        /// <summary>
        /// Total entities processed across all types
        /// </summary>
        public int TotalProcessed { get; set; }

        /// <summary>
        /// Total entities successfully processed
        /// </summary>
        public int TotalSuccess { get; set; }

        /// <summary>
        /// Total entities that failed processing
        /// </summary>
        public int TotalFailed { get; set; }

        /// <summary>
        /// Total entities that were skipped
        /// </summary>
        public int TotalSkipped { get; set; }

        /// <summary>
        /// Total entities that were cancelled
        /// </summary>
        public int TotalCancelled { get; set; }
    }

    /// <summary>
    /// Summary information for a specific entity type
    /// </summary>
    public class EntitySummary
    {
        /// <summary>
        /// Type of entity
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Total entities processed for this type
        /// </summary>
        public int TotalProcessed { get; set; }

        /// <summary>
        /// Successful entities for this type
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Failed entities for this type
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// Skipped entities for this type
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// Cancelled entities for this type
        /// </summary>
        public int CancelledCount { get; set; }

        /// <summary>
        /// Time taken to process this entity type
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>
        /// Final status for this entity type
        /// </summary>
        public string Status { get; set; } = string.Empty;
    }

    // === OPTIONS CLASSES ===

    /// <summary>
    /// Base options class for all progress events
    /// </summary>
    public abstract class ProgressOptionsBase
    {
        /// <summary>
        /// Optional connection ID to send to specific client
        /// </summary>
        public string? ConnectionId { get; set; }

        /// <summary>
        /// Optional group name to broadcast to specific group
        /// </summary>
        public string? GroupName { get; set; }

        /// <summary>
        /// Indicates if the migration is cancelled
        /// </summary>
        public bool? IsCancelled { get; set; }

        /// <summary>
        /// Cancellation reason (if cancelled)
        /// </summary>
        public string? CancellationReason { get; set; }

        /// <summary>
        /// When the cancellation was detected (if cancelled)
        /// </summary>
        public DateTime? CancelledAt { get; set; }
    }

    /// <summary>
    /// Options for creating a MigrationStartedEvent
    /// </summary>
    public class MigrationStartedOptions : ProgressOptionsBase
    {
        /// <summary>
        /// Source store identifier
        /// </summary>
        public string SourceStore { get; set; } = string.Empty;

        /// <summary>
        /// Destination store identifier
        /// </summary>
        public string DestinationStore { get; set; } = string.Empty;

        /// <summary>
        /// When the migration started
        /// </summary>
        public DateTime? StartDateTime { get; set; }

        /// <summary>
        /// Estimated completion time (if available)
        /// </summary>
        public DateTime? EstimatedEndTime { get; set; }

        /// <summary>
        /// List of entities that will be migrated
        /// </summary>
        public List<EntityInfo>? Entities { get; set; }
    }

    /// <summary>
    /// Options for creating an EntityStartedEvent
    /// </summary>
    public class EntityStartedOptions : ProgressOptionsBase
    {
        /// <summary>
        /// Type of entity that is starting (e.g., products, options, modifiers)
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Total count of entities discovered for this type
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Estimated duration for processing this entity type (optional)
        /// </summary>
        public int? EstimatedDurationMs { get; set; }

        /// <summary>
        /// Status message for this entity start
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Date and time when this entity started processing
        /// </summary>
        public DateTime? StartDateTime { get; set; }
    }

    /// <summary>
    /// Options for creating an EntityChunkProgressEvent
    /// </summary>
    public class EntityChunkProgressOptions : ProgressOptionsBase
    {
        /// <summary>
        /// Type of entity being processed (required)
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Chunk number that was just processed
        /// </summary>
        public int ChunkNumber { get; set; }

        /// <summary>
        /// Total number of chunks for this entity type
        /// </summary>
        public int TotalChunks { get; set; }

        /// <summary>
        /// Total entities processed in this chunk
        /// </summary>
        public int TotalProcessed { get; set; }

        /// <summary>
        /// Total entities successfully processed in this chunk
        /// </summary>
        public int TotalSuccess { get; set; }

        /// <summary>
        /// Total entities that failed processing in this chunk
        /// </summary>
        public int TotalFailed { get; set; }

        /// <summary>
        /// Total entities that were skipped in this chunk
        /// </summary>
        public int TotalSkipped { get; set; }

        /// <summary>
        /// Total entities that were cancelled in this chunk
        /// </summary>
        public int TotalCancelled { get; set; }

        /// <summary>
        /// Total entities expected for this entity type (from discovery phase)
        /// </summary>
        public int? TotalEntitiesForType { get; set; }

        /// <summary>
        /// Progress percentage for this entity type (0-100)
        /// </summary>
        public double? ProgressPercentage { get; set; }

        /// <summary>
        /// Status of the chunk processing
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Time taken to process this chunk
        /// </summary>
        public TimeSpan? ProcessingTime { get; set; }

        /// <summary>
        /// Current processing rate (entities per second)
        /// </summary>
        public double? EntitiesPerSecond { get; set; }

        /// <summary>
        /// Estimated time remaining for this entity type
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        /// <summary>
        /// Whether to show total count in UI (false for dynamic discovery phases)
        /// </summary>
        public bool? ShowTotalCount { get; set; }
    }

    /// <summary>
    /// Options for creating an EntityCompletedEvent
    /// </summary>
    public class EntityCompletedOptions : ProgressOptionsBase
    {
        /// <summary>
        /// Type of entity that completed (required)
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Total entities that were processed for this type
        /// </summary>
        public int TotalProcessed { get; set; }

        /// <summary>
        /// Total entities successfully processed for this type
        /// </summary>
        public int TotalSuccess { get; set; }

        /// <summary>
        /// Total entities that failed processing for this type
        /// </summary>
        public int TotalFailed { get; set; }

        /// <summary>
        /// Total entities that were skipped for this type
        /// </summary>
        public int TotalSkipped { get; set; }

        /// <summary>
        /// Total entities that were cancelled for this type
        /// </summary>
        public int TotalCancelled { get; set; }

        /// <summary>
        /// Final status of the entity processing
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Whether to show total count in UI (false for dynamic discovery phases)
        /// </summary>
        public bool? ShowTotalCount { get; set; }

        /// <summary>
        /// Total time taken to process this entity type (in milliseconds for frontend compatibility)
        /// </summary>
        public long? ProcessingTimeMs { get; set; }

        /// <summary>
        /// Date and time when this entity completed processing
        /// </summary>
        public DateTime? CompletedDateTime { get; set; }

        /// <summary>
        /// Completion message for this entity
        /// </summary>
        public string? Message { get; set; }
    }

    /// <summary>
    /// Options for creating a MigrationCompletedEvent
    /// </summary>
    public class MigrationCompletedOptions : ProgressOptionsBase
    {
        /// <summary>
        /// Final migration status (required)
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// When the migration ended
        /// </summary>
        public DateTime? EndDateTime { get; set; }

        /// <summary>
        /// Total duration of the migration
        /// </summary>
        public TimeSpan? TotalDuration { get; set; }

        /// <summary>
        /// Final counts across all entities
        /// </summary>
        public MigrationFinalCounts? FinalCounts { get; set; }

        /// <summary>
        /// Summary information for each entity type processed
        /// </summary>
        public List<EntitySummary>? Entities { get; set; }

        /// <summary>
        /// Optional completion message
        /// </summary>
        public string? Message { get; set; }
    }

    /// <summary>
    /// Options for creating an ErrorProgressEvent
    /// </summary>
    public class ErrorProgressOptions : ProgressOptionsBase
    {
        /// <summary>
        /// Error message to display (required)
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Error severity level
        /// </summary>
        public string? Severity { get; set; }

        /// <summary>
        /// Entity type where error occurred (optional)
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// Entity ID where error occurred (optional)
        /// </summary>
        public string? EntityId { get; set; }

        /// <summary>
        /// Chunk number where error occurred (optional)
        /// </summary>
        public int? ChunkNumber { get; set; }

        /// <summary>
        /// Exception that caused the error (for detailed information)
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// Whether the migration can continue after this error
        /// </summary>
        public bool? IsContinuable { get; set; }
    }
}