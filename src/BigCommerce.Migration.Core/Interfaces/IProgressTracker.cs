namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service for tracking and reporting migration progress
/// </summary>
public interface IProgressTracker
{
    /// <summary>
    /// Updates the progress for a migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="update">Progress update information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateProgressAsync(string migrationId, ProgressUpdate update, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current progress for a migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current migration progress</returns>
    Task<MigrationProgress> GetProgressAsync(string migrationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Records the start of entity processing
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Type of entity being processed</param>
    /// <param name="totalCount">Total number of entities to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartEntityProcessingAsync(string migrationId, string entityType, int totalCount, CancellationToken cancellationToken = default);
    

    /// <summary>
    /// Marks an entity type as completed
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Type of entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CompleteEntityProcessingAsync(string migrationId, string entityType, CancellationToken cancellationToken = default);
    

}

/// <summary>
/// Progress update information
/// Phase 4.2: Enhanced with soft cancellation token support for SignalR filtering
/// </summary>
public class ProgressUpdate
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Entity type being processed
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Current phase of processing
    /// </summary>
    public string Phase { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of entities processed
    /// </summary>
    public int ProcessedCount { get; set; }
    
    /// <summary>
    /// Number of successful entities
    /// </summary>
    public int SuccessCount { get; set; }
    
    /// <summary>
    /// Number of failed entities
    /// </summary>
    public int FailureCount { get; set; }
    
    /// <summary>
    /// Number of skipped entities
    /// </summary>
    public int SkippedCount { get; set; }
    
    /// <summary>
    /// Current batch being processed
    /// </summary>
    public int CurrentBatch { get; set; }
    
    /// <summary>
    /// Total number of batches
    /// </summary>
    public int TotalBatches { get; set; }
    
    /// <summary>
    /// Current status message
    /// </summary>
    public string StatusMessage { get; set; } = string.Empty;
    
    /// <summary>
    /// Timestamp of the update
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Phase 4.2: Soft cancellation token - indicates if the migration is cancelled
    /// This is passed from activity to avoid storage calls in ProgressTracker
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
/// Complete migration progress information
/// </summary>
public class MigrationProgress
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Current migration status
    /// </summary>
    public string Status { get; set; } = string.Empty;
    
    /// <summary>
    /// Migration start time
    /// </summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>
    /// Last update time
    /// </summary>
    public DateTime LastUpdated { get; set; }
    
    /// <summary>
    /// Total elapsed time
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }
    
    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan EstimatedTimeRemaining { get; set; }
    
    /// <summary>
    /// Total number of entities across all types
    /// </summary>
    public int TotalEntities { get; set; }
    
    /// <summary>
    /// Number of entities processed
    /// </summary>
    public int ProcessedEntities { get; set; }
    
    /// <summary>
    /// Number of successful entities
    /// </summary>
    public int SuccessfulEntities { get; set; }
    
    /// <summary>
    /// Number of failed entities
    /// </summary>
    public int FailedEntities { get; set; }
    
    /// <summary>
    /// Number of skipped entities
    /// </summary>
    public int SkippedEntities { get; set; }
    
    /// <summary>
    /// Overall progress percentage (0.0 to 100.0)
    /// </summary>
    public double OverallProgressPercentage { get; set; }
    
    /// <summary>
    /// Progress by entity type
    /// </summary>
    public Dictionary<string, EntityProgress> EntityProgress { get; set; } = new();
    
    /// <summary>
    /// Current processing phase
    /// </summary>
    public string CurrentPhase { get; set; } = string.Empty;
    
    /// <summary>
    /// Current entity being processed
    /// </summary>
    public string CurrentEntity { get; set; } = string.Empty;
    
    /// <summary>
    /// Processing speed (entities per second)
    /// </summary>
    public double EntitiesPerSecond { get; set; }
    
    /// <summary>
    /// Error rate (0.0 to 1.0)
    /// </summary>
    public double ErrorRate { get; set; }

    /// <summary>
    /// Phase 4.2: Soft cancellation token - indicates if the migration is cancelled
    /// This is propagated from activities to enable SignalR filtering
    /// </summary>
    public bool? IsCancelled { get; set; }

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
/// Progress information for a specific entity type
/// </summary>
public class EntityProgress
{
    /// <summary>
    /// Entity type
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Total count of entities
    /// </summary>
    public int TotalCount { get; set; }
    
    /// <summary>
    /// Number of entities processed
    /// </summary>
    public int ProcessedCount { get; set; }
    
    /// <summary>
    /// Number of successful entities
    /// </summary>
    public int SuccessCount { get; set; }
    
    /// <summary>
    /// Number of failed entities
    /// </summary>
    public int FailureCount { get; set; }
    
    /// <summary>
    /// Number of skipped entities (e.g., duplicates, transformations)
    /// </summary>
    public int SkippedCount { get; set; }
    
    /// <summary>
    /// Progress percentage for this entity type (0.0 to 100.0)
    /// </summary>
    public double ProgressPercentage { get; set; }
    
    /// <summary>
    /// Current status of entity processing
    /// </summary>
    public string Status { get; set; } = string.Empty;
    
    /// <summary>
    /// Start time for entity processing
    /// </summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>
    /// End time for entity processing
    /// </summary>
    public DateTime? EndTime { get; set; }
    
    /// <summary>
    /// Time taken to process this entity type
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }
} 