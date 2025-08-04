using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Orchestration.Models;

/// <summary>
/// Request model for processing a manageable chunk of entities to avoid Azure Functions timeout
/// Provides chunked processing for better performance and timeout safety
/// </summary>
public class ProcessEntityChunkRequest
{
    /// <summary>
    /// Unique identifier for the migration
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Type of entity being processed (products, categories, brands, etc.)
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Starting index for this chunk (0-based)
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Number of entities in this chunk (max 500 to stay under timeout limit)
    /// </summary>
    public int ChunkSize { get; set; }

    /// <summary>
    /// Chunk number for tracking and logging purposes
    /// </summary>
    public int ChunkNumber { get; set; }

    /// <summary>
    /// Total number of chunks for this migration
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Source store configuration
    /// </summary>
    public StoreConfiguration SourceStore { get; set; } = new();

    /// <summary>
    /// Destination store configuration
    /// </summary>
    public StoreConfiguration DestinationStore { get; set; } = new();

    /// <summary>
    /// Category tree context for category and product migrations
    /// </summary>
    public CategoryTreeContext? CategoryTreeContext { get; set; }

    /// <summary>
    /// Pagination metadata for direct pagination scenarios
    /// </summary>
    public Dictionary<string, object>? PaginationMetadata { get; set; }

    /// <summary>
    /// Whether to use direct pagination instead of entity discovery
    /// </summary>
    public bool UseDirectPagination { get; set; }

    /// <summary>
    /// List of specific entity IDs to process (when not using direct pagination)
    /// </summary>
    public List<string> EntityIds { get; set; } = new();

    /// <summary>
    /// Whether this chunk has been cancelled
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Reason for cancellation if applicable
    /// </summary>
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Timestamp when cancellation occurred
    /// </summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Retry attempt number for this chunk
    /// </summary>
    public int RetryAttempt { get; set; } = 0;

    /// <summary>
    /// Maximum retry attempts allowed for this chunk
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
}