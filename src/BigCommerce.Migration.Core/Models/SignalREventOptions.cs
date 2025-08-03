using System;
using System.Collections.Generic;

namespace BigCommerce.Migration.Core.Models
{
    /// <summary>
    /// 🎯 SIGNALR EVENT OPTIONS
    /// Strongly-typed option classes for centralized SignalR Event Factory.
    /// Provides clean parameter passing and optional property configuration.
    /// </summary>

    /// <summary>
    /// Base options class for all SignalR events
    /// Contains common properties that can be set for any event type
    /// </summary>
    public abstract class SignalREventOptionsBase
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
        /// Indicates if the migration is cancelled (auto-populated if not set)
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
    /// Options for creating MigrationProgressEvent
    /// </summary>
    public class MigrationProgressOptions : SignalREventOptionsBase
    {
        /// <summary>
        /// Overall migration progress percentage (0-100)
        /// </summary>
        public double OverallProgress { get; set; }

        /// <summary>
        /// Current migration status (running, completed, failed, cancelled)
        /// Default: "running"
        /// </summary>
        public string? Status { get; set; }

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
        /// Number of entities successfully processed (optional - will be auto-calculated if not provided)
        /// If not set, will be calculated as ProcessedEntities - FailedEntities
        /// </summary>
        public int? SuccessfulEntities { get; set; }

        /// <summary>
        /// Current entity type being processed
        /// </summary>
        public string? CurrentEntityType { get; set; }

        /// <summary>
        /// When the migration started (optional - will be auto-set if not provided)
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// How long the migration has been running (optional - will be auto-calculated if not provided)
        /// </summary>
        public TimeSpan? ElapsedTime { get; set; }

        /// <summary>
        /// Current processing speed in entities per second (optional - will be auto-calculated if not provided)
        /// </summary>
        public double? EntitiesPerSecond { get; set; }

        /// <summary>
        /// Estimated time remaining (optional)
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        /// <summary>
        /// Current batch number being processed (optional)
        /// </summary>
        public int? CurrentBatchNumber { get; set; }

        /// <summary>
        /// Current processing activity (optional)
        /// </summary>
        public string? CurrentActivity { get; set; }

        /// <summary>
        /// Detailed information about the current batch (optional)
        /// </summary>
        public CurrentBatchDetails? CurrentBatch { get; set; }
    }

    /// <summary>
    /// Options for creating BatchProgressEvent
    /// </summary>
    public class BatchProgressOptions : SignalREventOptionsBase
    {
        /// <summary>
        /// Type of entity being processed in this batch (REQUIRED)
        /// </summary>
        public string? EntityType { get; set; }

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
        /// Default: "processing"
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Time taken to process this batch
        /// </summary>
        public TimeSpan? ProcessingTime { get; set; }
    }

    /// <summary>
    /// Options for creating EntityProgressEvent
    /// </summary>
    public class EntityProgressOptions : SignalREventOptionsBase
    {
        /// <summary>
        /// Type of entity (products, categories, brands, etc.) (REQUIRED)
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// Unique identifier of the entity
        /// </summary>
        public string? EntityId { get; set; }

        /// <summary>
        /// Display name of the entity
        /// </summary>
        public string? EntityName { get; set; }

        /// <summary>
        /// Entity processing status (processing, completed, failed)
        /// Default: "processing"
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Number of entities processed so far
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// Total number of entities in this context
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Error message if processing failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Time taken to process this entity
        /// </summary>
        public TimeSpan? ProcessingTime { get; set; }
    }

    /// <summary>
    /// Options for creating ErrorProgressEvent
    /// </summary>
    public class ErrorProgressOptions : SignalREventOptionsBase
    {
        /// <summary>
        /// Error message describing what went wrong (REQUIRED)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Type of entity that caused the error
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// ID of the entity that caused the error
        /// </summary>
        public string? EntityId { get; set; }

        /// <summary>
        /// Batch number where the error occurred
        /// </summary>
        public int? BatchNumber { get; set; }

        /// <summary>
        /// Error severity (Error, Warning, Critical)
        /// Default: "Error"
        /// </summary>
        public string? Severity { get; set; }

        /// <summary>
        /// Full exception details
        /// </summary>
        public string? Exception { get; set; }

        /// <summary>
        /// Stack trace for debugging
        /// </summary>
        public string? StackTrace { get; set; }
    }

    /// <summary>
    /// Options for creating StatusProgressEvent
    /// </summary>
    public class StatusProgressOptions : SignalREventOptionsBase
    {
        /// <summary>
        /// Migration status (started, completed, failed, cancelled) (REQUIRED)
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Human-readable status message
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Additional status data
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// Error information if status indicates failure
        /// </summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Options for creating SubBatchStartedEvent
    /// </summary>
    public class SubBatchStartedOptions : SignalREventOptionsBase
    {
        /// <summary>
        /// Parent batch number
        /// </summary>
        public int ParentBatchNumber { get; set; }

        /// <summary>
        /// Sub-batch number within the parent batch
        /// </summary>
        public int SubBatchNumber { get; set; }

        /// <summary>
        /// Total number of sub-batches in the parent batch
        /// </summary>
        public int TotalSubBatches { get; set; }

        /// <summary>
        /// Type of entity being processed (REQUIRED)
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// Number of entities in this sub-batch
        /// </summary>
        public int EntitiesInBatch { get; set; }
    }

    /// <summary>
    /// Options for creating SubBatchCompletedEvent
    /// </summary>
    public class SubBatchCompletedOptions : SignalREventOptionsBase
    {
        /// <summary>
        /// Parent batch number
        /// </summary>
        public int ParentBatchNumber { get; set; }

        /// <summary>
        /// Sub-batch number within the parent batch
        /// </summary>
        public int SubBatchNumber { get; set; }

        /// <summary>
        /// Total number of sub-batches in the parent batch
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
        /// Total number of entities in this sub-batch
        /// </summary>
        public int TotalEntities { get; set; }

        /// <summary>
        /// Type of entity being processed (REQUIRED)
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// Time taken to process this sub-batch
        /// </summary>
        public TimeSpan? ProcessingTime { get; set; }

        /// <summary>
        /// When the sub-batch was completed (auto-populated if not set)
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// List of errors that occurred during sub-batch processing
        /// </summary>
        public List<string>? Errors { get; set; }

        /// <summary>
        /// Cumulative successful entities across all completed sub-batches
        /// </summary>
        public int CumulativeSuccessfulEntities { get; set; }

        /// <summary>
        /// Cumulative failed entities across all completed sub-batches
        /// </summary>
        public int CumulativeFailedEntities { get; set; }

        /// <summary>
        /// Total entities expected in the entire migration
        /// </summary>
        public int TotalMigrationEntities { get; set; }

        /// <summary>
        /// Overall migration progress percentage (0-100)
        /// </summary>
        public double ProgressPercentage { get; set; }

        /// <summary>
        /// Estimated time remaining for the entire migration
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }
    }

    /// <summary>
    /// Options for creating SubBatchMigrationProgressEvent
    /// </summary>
    public class SubBatchProgressOptions : SignalREventOptionsBase
    {
        /// <summary>
        /// Total number of pages in the migration
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Number of pages completed
        /// </summary>
        public int CompletedPages { get; set; }

        /// <summary>
        /// Total number of sub-batches in the migration
        /// </summary>
        public int TotalSubBatches { get; set; }

        /// <summary>
        /// Number of sub-batches completed
        /// </summary>
        public int CompletedSubBatches { get; set; }

        /// <summary>
        /// Total successful entities across all completed sub-batches
        /// </summary>
        public int TotalSuccessfulEntities { get; set; }

        /// <summary>
        /// Total failed entities across all completed sub-batches
        /// </summary>
        public int TotalFailedEntities { get; set; }

        /// <summary>
        /// Total entities expected in the migration
        /// </summary>
        public int TotalExpectedEntities { get; set; }

        /// <summary>
        /// Current processing rate (entities per second)
        /// </summary>
        public double ProcessingRate { get; set; }

        /// <summary>
        /// Overall migration progress percentage (0-100)
        /// </summary>
        public double OverallProgressPercentage { get; set; }

        /// <summary>
        /// Estimated time remaining for the migration
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        /// <summary>
        /// When this progress update was created (auto-populated if not set)
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Total elapsed time since migration started
        /// </summary>
        public TimeSpan? ElapsedTime { get; set; }

        /// <summary>
        /// Recent errors that occurred during processing
        /// </summary>
        public List<string>? RecentErrors { get; set; }

        /// <summary>
        /// Performance metrics and statistics
        /// </summary>
        public Dictionary<string, object>? PerformanceMetrics { get; set; }
    }

    /// <summary>
    /// Options for creating CancellationProgressEvent (Task 7.1 Live Cancellation)
    /// Supports multi-level cancellation with real-time dashboard updates
    /// </summary>
    public class CancellationProgressOptions : SignalREventOptionsBase
    {
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