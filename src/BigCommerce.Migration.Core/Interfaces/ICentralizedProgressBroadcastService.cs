using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Centralized service for broadcasting all SignalR progress events.
    /// This is the SINGLE point of truth for all real-time progress communication.
    /// 
    /// SOLID Principles:
    /// - Single Responsibility: Only handles SignalR broadcasting
    /// - Open/Closed: Extensible for new event types without modification
    /// - Interface Segregation: Focused interface for progress broadcasting only
    /// - Dependency Inversion: Abstracts the broadcasting implementation
    /// </summary>
    public interface ICentralizedProgressBroadcastService
    {
        /// <summary>
        /// Broadcasts migration started event.
        /// Called once at the beginning of each migration.
        /// </summary>
        Task BroadcastMigrationStartedAsync(string migrationId, string sourceStore, string destinationStore, List<EntityInfo> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcasts entity started event.
        /// Called when an individual entity type starts processing (after discovery).
        /// </summary>
        Task BroadcastEntityStartedAsync(string migrationId, string entityType, int totalCount, string message = "", CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcasts entity chunk progress event.
        /// Called after each chunk of entities is processed - rate limited to prevent spam.
        /// </summary>
        Task BroadcastEntityChunkProgressAsync(string migrationId, EntityChunkProgress progress, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcasts entity completion event.
        /// Called when an individual entity type finishes processing.
        /// </summary>
        Task BroadcastEntityCompletedAsync(string migrationId, string entityType, string status, int totalProcessed, int totalSuccess, int totalFailed, int totalSkipped, int totalCancelled, bool showTotalCount, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcasts migration completed event.
        /// Called once at the end of each migration (success or cancellation).
        /// </summary>
        Task BroadcastMigrationCompletedAsync(string migrationId, MigrationCompletionInfo completion, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcasts error event.
        /// Called when significant errors occur during migration.
        /// </summary>
        Task BroadcastErrorAsync(string migrationId, ErrorInfo error, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Data structure for migration start information.
    /// </summary>
    public class MigrationStartInfo
    {
        /// <summary>
        /// The type of entity being migrated (e.g., "products", "customers").
        /// </summary>
        public string EntityType { get; set; } = string.Empty;
        
        /// <summary>
        /// Total number of entities expected to be migrated.
        /// </summary>
        public int TotalEntities { get; set; }
        
        /// <summary>
        /// The current status of the migration (e.g., "initializing", "running").
        /// </summary>
        public string Status { get; set; } = "Initializing";
        
        /// <summary>
        /// A human-readable message describing the current state.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Data structure for entity chunk progress information.
    /// </summary>
    public class EntityChunkProgress
    {
        /// <summary>
        /// The type of entity being migrated (e.g., "products", "customers").
        /// </summary>
        public string EntityType { get; set; } = string.Empty;
        
        /// <summary>
        /// The chunk number being processed.
        /// </summary>
        public int ChunkNumber { get; set; }
        
        /// <summary>
        /// Total number of entities in this specific chunk.
        /// </summary>
        public int ChunkSize { get; set; }
        
        /// <summary>
        /// Number of entities successfully processed in this chunk.
        /// </summary>
        public int ProcessedInChunk { get; set; }
        
        /// <summary>
        /// Number of entities that failed processing in this chunk.
        /// </summary>
        public int FailedInChunk { get; set; }
        
        /// <summary>
        /// Cumulative count of entities successfully processed across all chunks for this entity type.
        /// </summary>
        public int CumulativeProcessed { get; set; }
        
        /// <summary>
        /// Cumulative count of entities that failed processing across all chunks for this entity type.
        /// </summary>
        public int CumulativeFailed { get; set; }
        
        /// <summary>
        /// Cumulative count of entities that were skipped across all chunks for this entity type.
        /// </summary>
        public int CumulativeSkipped { get; set; }
        
        /// <summary>
        /// Cumulative count of entities that were cancelled across all chunks for this entity type.
        /// </summary>
        public int CumulativeCancelled { get; set; }
        
        /// <summary>
        /// Total number of entities expected for this entity type (can be estimated).
        /// </summary>
        public int TotalEntitiesForType { get; set; }
        
        /// <summary>
        /// Progress percentage for this entity type (0-100).
        /// </summary>
        public double ProgressPercentage { get; set; }
        
        /// <summary>
        /// The current status of the chunk processing (e.g., "processing", "completed", "failed").
        /// </summary>
        public string Status { get; set; } = string.Empty;
        
        /// <summary>
        /// A human-readable message describing the current state.
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
        /// <summary>
        /// Time taken to process this chunk (in milliseconds).
        /// </summary>
        public long ProcessingTimeMs { get; set; }
        
        /// <summary>
        /// Whether to show total count in UI (false for dynamic discovery phases)
        /// </summary>
        public bool ShowTotalCount { get; set; } = true;
    }

    /// <summary>
    /// Data structure for migration completion information.
    /// </summary>
    public class MigrationCompletionInfo
    {
        /// <summary>
        /// Final status of the migration (e.g., "completed", "completed-with-errors").
        /// </summary>
        public string Status { get; set; } = "Completed";
        
        /// <summary>
        /// A final message about the migration outcome.
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
        /// <summary>
        /// Total number of entities processed across all types.
        /// </summary>
        public int TotalProcessedEntities { get; set; }
        
        /// <summary>
        /// Total number of entities that failed across all types.
        /// </summary>
        public int TotalFailedEntities { get; set; }
        
        /// <summary>
        /// Total duration of the migration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; }
    }

    /// <summary>
    /// Data structure for error information.
    /// </summary>
    public class ErrorInfo
    {
        /// <summary>
        /// The type of error (e.g., "validation", "api", "system").
        /// </summary>
        public string ErrorType { get; set; } = string.Empty;
        
        /// <summary>
        /// A detailed error message.
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
        
        /// <summary>
        /// Optional: Stack trace of the exception.
        /// </summary>
        public string? StackTrace { get; set; }
        
        /// <summary>
        /// Optional: The entity type related to the error, if applicable.
        /// </summary>
        public string? EntityType { get; set; }
        
        /// <summary>
        /// Optional: The ID of the entity related to the error, if applicable.
        /// </summary>
        public string? EntityId { get; set; }
    }
}