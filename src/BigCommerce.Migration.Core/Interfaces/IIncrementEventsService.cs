using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service for managing chunk increment events that enable real-time progress tracking
/// 
/// Purpose:
/// - Write chunk processing results immediately to database
/// - Aggregate incremental progress for real-time UI updates
/// - Prevent data loss during migration cancellations
/// 
/// Design Principles:
/// - Fire-and-forget writes don't block chunk processing
/// - Query-time aggregation ensures consistency
/// - Graceful failure handling preserves migration functionality
/// - Thread-safe operations support concurrent chunk processing
/// </summary>
public interface IIncrementEventsService
{
    #region Write Operations

    /// <summary>
    /// Writes a chunk increment event to storage immediately after chunk processing
    /// Uses fire-and-forget pattern to avoid blocking chunk processing
    /// 
    /// Implementation Notes:
    /// - Generates unique RowKey to prevent conflicts
    /// - Handles Azure Table Storage transient failures gracefully
    /// - Logs errors but doesn't throw exceptions (migration continues)
    /// - Uses retry logic with exponential backoff for transient errors
    /// </summary>
    /// <param name="incrementEvent">Chunk increment event to write</param>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>Task that completes when write is attempted (may complete before actual write)</returns>
    Task WriteChunkIncrementAsync(ChunkIncrementEvent incrementEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes multiple chunk increment events in a batch for better performance
    /// Used for scenarios where multiple chunks complete simultaneously
    /// 
    /// Implementation Notes:
    /// - Uses Azure Table Storage batch operations when possible
    /// - Falls back to individual writes if batch fails
    /// - Maintains same error handling principles as single writes
    /// </summary>
    /// <param name="incrementEvents">List of chunk increment events to write</param>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>Task that completes when batch write is attempted</returns>
    Task WriteBatchChunkIncrementsAsync(IList<ChunkIncrementEvent> incrementEvents, CancellationToken cancellationToken = default);

    #endregion

    #region Read Operations

    /// <summary>
    /// Gets all chunk increment events for a specific migration
    /// Used for detailed analysis and debugging
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>List of all chunk increment events for the migration</returns>
    Task<List<ChunkIncrementEvent>> GetChunkIncrementsAsync(string migrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunk increment events for a specific migration and entity type
    /// Used for entity-specific progress analysis
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type (e.g., "products", "categories")</param>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>List of chunk increment events for the specified entity type</returns>
    Task<List<ChunkIncrementEvent>> GetChunkIncrementsAsync(string migrationId, string entityType, CancellationToken cancellationToken = default);

    #endregion

    #region Aggregation Operations

    /// <summary>
    /// Gets aggregated progress for all entity types in a migration
    /// Uses query-time aggregation for consistency and real-time accuracy
    /// 
    /// This is the core method that solves the original problem:
    /// - Aggregates all chunk increments into entity totals
    /// - Returns accurate counts even if migration was cancelled
    /// - Provides real-time progress without stale cached data
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>Dictionary of entity types and their aggregated progress</returns>
    Task<Dictionary<string, EntityProgressSummary>> GetAggregatedProgressAsync(string migrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated progress for a specific entity type in a migration
    /// Optimized version for single entity type queries
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type (e.g., "products", "categories")</param>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>Aggregated progress summary for the entity type</returns>
    Task<EntityProgressSummary> GetEntityProgressSummaryAsync(string migrationId, string entityType, CancellationToken cancellationToken = default);

    #endregion

    #region Health and Diagnostics

    /// <summary>
    /// Checks if the increment events service is healthy and can write to storage
    /// Used for monitoring and health checks
    /// </summary>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>True if service is healthy, false otherwise</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about increment events storage
    /// Used for monitoring and performance analysis
    /// </summary>
    /// <param name="migrationId">Migration identifier (optional, null for all migrations)</param>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>Storage statistics</returns>
    Task<IncrementEventsStatistics> GetStatisticsAsync(string? migrationId = null, CancellationToken cancellationToken = default);

    #endregion

    #region Cleanup Operations

    /// <summary>
    /// Deletes chunk increment events for a completed migration
    /// Used for cleanup after successful migration completion
    /// 
    /// Note: Should only be called after migration is fully complete
    /// and final progress has been persisted to main tables
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>Number of events deleted</returns>
    Task<int> DeleteMigrationIncrementsAsync(string migrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes old chunk increment events based on age
    /// Used for periodic cleanup of historical data
    /// </summary>
    /// <param name="olderThan">Delete events older than this date</param>
    /// <param name="cancellationToken">Cancellation token (optional)</param>
    /// <returns>Number of events deleted</returns>
    Task<int> DeleteOldIncrementsAsync(DateTime olderThan, CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Summary of aggregated progress for an entity type
/// Used by GetAggregatedProgressAsync and GetEntityProgressSummaryAsync
/// </summary>
public class EntityProgressSummary
{
    /// <summary>
    /// Entity type (e.g., "products", "categories", "brands")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Total number of chunks processed for this entity type
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Total successful entities across all chunks
    /// This is the key metric that was previously lost on cancellation
    /// </summary>
    public int TotalSuccessful { get; set; }

    /// <summary>
    /// Total failed entities across all chunks
    /// </summary>
    public int TotalFailed { get; set; }

    /// <summary>
    /// Total skipped entities across all chunks
    /// </summary>
    public int TotalSkipped { get; set; }

    /// <summary>
    /// Total cancelled entities across all chunks
    /// </summary>
    public int TotalCancelled { get; set; }

    /// <summary>
    /// Total entities processed across all chunks
    /// </summary>
    public int TotalProcessed => TotalSuccessful + TotalFailed + TotalSkipped + TotalCancelled;

    /// <summary>
    /// Success rate as percentage
    /// </summary>
    public double SuccessRate => TotalProcessed > 0 ? (double)TotalSuccessful / TotalProcessed * 100.0 : 0.0;

    /// <summary>
    /// When the first chunk for this entity type started processing
    /// </summary>
    public DateTime? FirstChunkStartTime { get; set; }

    /// <summary>
    /// When the last chunk for this entity type completed processing
    /// </summary>
    public DateTime? LastChunkEndTime { get; set; }

    /// <summary>
    /// Total processing time for all chunks of this entity type
    /// </summary>
    public TimeSpan TotalProcessingTime { get; set; }

    /// <summary>
    /// Average processing time per chunk
    /// </summary>
    public TimeSpan AverageChunkProcessingTime => 
        TotalChunks > 0 ? TimeSpan.FromMilliseconds(TotalProcessingTime.TotalMilliseconds / TotalChunks) : TimeSpan.Zero;

    /// <summary>
    /// String representation for logging and debugging
    /// </summary>
    public override string ToString()
    {
        return $"EntityProgressSummary[{EntityType}] " +
               $"Chunks={TotalChunks}, Success={TotalSuccessful}, " +
               $"Failed={TotalFailed}, Skipped={TotalSkipped}, " +
               $"Cancelled={TotalCancelled}, SuccessRate={SuccessRate:F1}%";
    }
}

/// <summary>
/// Statistics about increment events storage
/// Used for monitoring and performance analysis
/// </summary>
public class IncrementEventsStatistics
{
    /// <summary>
    /// Migration ID these statistics are for (null if global statistics)
    /// </summary>
    public string? MigrationId { get; set; }

    /// <summary>
    /// Total number of increment events in storage
    /// </summary>
    public int TotalEvents { get; set; }

    /// <summary>
    /// Number of unique migrations with increment events
    /// </summary>
    public int UniqueMigrations { get; set; }

    /// <summary>
    /// Number of unique entity types across all events
    /// </summary>
    public int UniqueEntityTypes { get; set; }

    /// <summary>
    /// Total successful entities across all events
    /// </summary>
    public long TotalSuccessfulEntities { get; set; }

    /// <summary>
    /// Total failed entities across all events
    /// </summary>
    public long TotalFailedEntities { get; set; }

    /// <summary>
    /// Total storage size estimate in bytes
    /// </summary>
    public long EstimatedStorageBytes { get; set; }

    /// <summary>
    /// When statistics were calculated
    /// </summary>
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// String representation for logging and debugging
    /// </summary>
    public override string ToString()
    {
        var scope = MigrationId != null ? $"Migration[{MigrationId}]" : "Global";
        return $"IncrementEventsStatistics[{scope}] " +
               $"Events={TotalEvents}, Migrations={UniqueMigrations}, " +
               $"EntityTypes={UniqueEntityTypes}, " +
               $"SuccessfulEntities={TotalSuccessfulEntities:N0}";
    }
}