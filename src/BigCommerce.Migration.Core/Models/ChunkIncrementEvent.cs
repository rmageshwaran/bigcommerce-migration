using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Azure Table Storage entity for tracking incremental progress updates from chunk processing
/// Enables real-time progress tracking and prevents data loss during migration cancellations
/// 
/// Design Principles:
/// - Each chunk writes to unique RowKey (no conflicts)
/// - Query-time aggregation for consistent totals
/// - Fire-and-forget writes don't block chunk processing
/// - Graceful failure handling preserves migration functionality
/// </summary>
public class ChunkIncrementEvent : ITableEntity
{
    #region Azure Table Storage Required Fields

    /// <summary>
    /// Partition key: Migration ID (e.g., "47969ba9-ebba-49c2-ab09-131482562568")
    /// Groups all chunks by migration for efficient queries
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key: Unique chunk identifier (e.g., "products-chunk-001-20250117102345123-000001")
    /// Format: {EntityType}-chunk-{ChunkNumber:D3}-{Timestamp}-{Sequence}
    /// Ensures no conflicts even with concurrent chunk processing
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// ETag for optimistic concurrency control (Azure Table Storage managed)
    /// </summary>
    public ETag ETag { get; set; }

    /// <summary>
    /// Timestamp for Azure Table Storage (automatically managed)
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    #endregion

    #region Migration Context

    /// <summary>
    /// Migration ID (redundant for queries, same as PartitionKey)
    /// Enables easier querying and data consistency validation
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type being processed (e.g., "products", "categories", "brands", "variants")
    /// Used for grouping and aggregation
    /// </summary>
    [Required]
    public string EntityType { get; set; } = string.Empty;

    #endregion

    #region Chunk Identification

    /// <summary>
    /// Sequential chunk number within the entity type (1, 2, 3, ...)
    /// Used for ordering and progress calculation
    /// </summary>
    [Required]
    public int ChunkNumber { get; set; }

    /// <summary>
    /// Starting entity index for this chunk (0, 250, 500, ...)
    /// Enables resume functionality and progress tracking
    /// </summary>
    [Required]
    public int ChunkStartIndex { get; set; }

    /// <summary>
    /// Number of entities in this chunk (typically 250-500)
    /// Used for progress percentage calculations
    /// </summary>
    [Required]
    public int ChunkSize { get; set; }

    #endregion

    #region Progress Counts - The Core Data

    /// <summary>
    /// Number of entities successfully created in BigCommerce API
    /// This is the key metric that was previously lost on cancellation
    /// </summary>
    [Required]
    public int SuccessfulEntities { get; set; }

    /// <summary>
    /// Number of entities that failed to create due to errors
    /// Includes API failures, validation errors, etc.
    /// </summary>
    [Required]
    public int FailedEntities { get; set; }

    /// <summary>
    /// Number of entities skipped (duplicates, validation failures, etc.)
    /// Entities that were intentionally not processed
    /// </summary>
    [Required]
    public int SkippedEntities { get; set; }

    /// <summary>
    /// Number of entities cancelled mid-processing due to migration cancellation
    /// Tracks work interrupted by user cancellation
    /// </summary>
    [Required]
    public int CancelledEntities { get; set; }

    #endregion

    #region Processing Metadata

    /// <summary>
    /// When chunk processing started (UTC)
    /// Used for performance analysis and debugging
    /// </summary>
    [Required]
    public DateTime ProcessingStartTime { get; set; }

    /// <summary>
    /// When chunk processing completed (UTC)
    /// Used for performance analysis and progress timeline
    /// </summary>
    [Required]
    public DateTime ProcessingEndTime { get; set; }

    /// <summary>
    /// Processing duration in milliseconds
    /// Used for performance monitoring and optimization
    /// </summary>
    [Required]
    public long ProcessingTimeMs { get; set; }

    #endregion

    #region Source Information

    /// <summary>
    /// Source BigCommerce store ID (e.g., "in2msaitrc")
    /// Enables cross-store analysis and debugging
    /// </summary>
    [Required]
    public string SourceStore { get; set; } = string.Empty;

    /// <summary>
    /// Destination BigCommerce store ID (e.g., "4diwbwzw1t")
    /// Enables cross-store analysis and debugging
    /// </summary>
    [Required]
    public string DestinationStore { get; set; } = string.Empty;

    #endregion

    #region Error Information

    /// <summary>
    /// Whether this chunk encountered any errors during processing
    /// Quick flag for filtering and analysis
    /// </summary>
    public bool HasErrors { get; set; }

    /// <summary>
    /// JSON array of error messages (if any, limited to first 5 errors)
    /// Provides detailed error information for debugging
    /// Nullable to save storage space for successful chunks
    /// </summary>
    public string? ErrorSummary { get; set; }

    #endregion

    #region Audit Trail

    /// <summary>
    /// When this record was created (UTC)
    /// Used for audit trail and debugging
    /// </summary>
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Which component created this record
    /// Typically "ProcessEntityChunkActivity" for chunk processing
    /// </summary>
    [Required]
    public string CreatedBy { get; set; } = "ProcessEntityChunkActivity";

    #endregion

    #region Computed Properties

    /// <summary>
    /// Total entities processed in this chunk (computed property)
    /// Sum of successful + failed + skipped + cancelled entities
    /// </summary>
    public int TotalProcessed => SuccessfulEntities + FailedEntities + SkippedEntities + CancelledEntities;

    /// <summary>
    /// Processing duration as TimeSpan (computed property)
    /// Converts ProcessingTimeMs to TimeSpan for easier use
    /// </summary>
    public TimeSpan ProcessingDuration => TimeSpan.FromMilliseconds(ProcessingTimeMs);

    /// <summary>
    /// Success rate for this chunk as percentage (computed property)
    /// Used for quality analysis and reporting
    /// </summary>
    public double SuccessRate => TotalProcessed > 0 ? (double)SuccessfulEntities / TotalProcessed * 100.0 : 0.0;

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a ChunkIncrementEvent from chunk processing results
    /// Simplifies creation from ProcessEntityChunkActivity
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Type of entity being processed</param>
    /// <param name="chunkNumber">Sequential chunk number</param>
    /// <param name="chunkStartIndex">Starting index of entities in this chunk</param>
    /// <param name="chunkSize">Number of entities in this chunk</param>
    /// <param name="successfulEntities">Number of successful entities</param>
    /// <param name="failedEntities">Number of failed entities</param>
    /// <param name="skippedEntities">Number of skipped entities</param>
    /// <param name="cancelledEntities">Number of cancelled entities</param>
    /// <param name="processingStartTime">When processing started</param>
    /// <param name="processingEndTime">When processing ended</param>
    /// <param name="sourceStore">Source store identifier</param>
    /// <param name="destinationStore">Destination store identifier</param>
    /// <param name="errors">List of error messages (optional)</param>
    /// <returns>Configured ChunkIncrementEvent ready for storage</returns>
    public static ChunkIncrementEvent Create(
        string migrationId,
        string entityType,
        int chunkNumber,
        int chunkStartIndex,
        int chunkSize,
        int successfulEntities,
        int failedEntities,
        int skippedEntities,
        int cancelledEntities,
        DateTime processingStartTime,
        DateTime processingEndTime,
        string sourceStore,
        string destinationStore,
        IList<string>? errors = null)
    {
        var incrementEvent = new ChunkIncrementEvent
        {
            PartitionKey = migrationId,
            // RowKey will be set by the service using unique generation
            MigrationId = migrationId,
            EntityType = entityType,
            ChunkNumber = chunkNumber,
            ChunkStartIndex = chunkStartIndex,
            ChunkSize = chunkSize,
            SuccessfulEntities = successfulEntities,
            FailedEntities = failedEntities,
            SkippedEntities = skippedEntities,
            CancelledEntities = cancelledEntities,
            ProcessingStartTime = processingStartTime,
            ProcessingEndTime = processingEndTime,
            ProcessingTimeMs = (long)(processingEndTime - processingStartTime).TotalMilliseconds,
            SourceStore = sourceStore,
            DestinationStore = destinationStore,
            HasErrors = errors?.Any() == true,
            ErrorSummary = errors?.Any() == true ? 
                System.Text.Json.JsonSerializer.Serialize(errors.Take(5).ToArray()) : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "ProcessEntityChunkActivity"
        };

        return incrementEvent;
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validates the chunk increment event data
    /// Ensures data integrity before storage
    /// </summary>
    /// <returns>List of validation errors (empty if valid)</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(MigrationId))
            errors.Add("MigrationId is required");

        if (string.IsNullOrEmpty(EntityType))
            errors.Add("EntityType is required");

        if (ChunkNumber <= 0)
            errors.Add("ChunkNumber must be positive");

        if (ChunkStartIndex < 0)
            errors.Add("ChunkStartIndex cannot be negative");

        if (ChunkSize <= 0)
            errors.Add("ChunkSize must be positive");

        if (SuccessfulEntities < 0)
            errors.Add("SuccessfulEntities cannot be negative");

        if (FailedEntities < 0)
            errors.Add("FailedEntities cannot be negative");

        if (SkippedEntities < 0)
            errors.Add("SkippedEntities cannot be negative");

        if (CancelledEntities < 0)
            errors.Add("CancelledEntities cannot be negative");

        if (ProcessingStartTime > ProcessingEndTime)
            errors.Add("ProcessingStartTime cannot be after ProcessingEndTime");

        if (string.IsNullOrEmpty(SourceStore))
            errors.Add("SourceStore is required");

        if (string.IsNullOrEmpty(DestinationStore))
            errors.Add("DestinationStore is required");

        return errors;
    }

    #endregion

    #region ToString Override

    /// <summary>
    /// String representation for logging and debugging
    /// </summary>
    public override string ToString()
    {
        return $"ChunkIncrementEvent[{MigrationId}:{EntityType}:Chunk{ChunkNumber}] " +
               $"Success={SuccessfulEntities}, Failed={FailedEntities}, " +
               $"Skipped={SkippedEntities}, Cancelled={CancelledEntities}, " +
               $"Duration={ProcessingDuration.TotalSeconds:F1}s";
    }

    #endregion
}