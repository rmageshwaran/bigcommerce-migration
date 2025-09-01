namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Azure Table Storage platform limits and constraints
/// </summary>
public static class AzureTableStorageLimits
{
    /// <summary>
    /// Maximum number of operations allowed in a single Azure Table Storage transaction
    /// This is a platform constraint enforced by Azure Table Storage
    /// Reference: https://docs.microsoft.com/en-us/rest/api/storageservices/performing-entity-group-transactions
    /// </summary>
    public const int MaxTransactionOperations = 100;
    
    /// <summary>
    /// Maximum size of a single entity property in Azure Table Storage (64KB)
    /// </summary>
    public const int MaxEntityPropertySizeBytes = 64 * 1024;
    
    /// <summary>
    /// Maximum total size of all properties for a single entity (1MB)
    /// </summary>
    public const int MaxEntitySizeBytes = 1024 * 1024;
}

/// <summary>
/// Entity ID mapping for tracking source to destination entity relationships
/// </summary>
public class EntityMapping
{
    /// <summary>
    /// Migration ID this mapping belongs to
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type (e.g., "product", "category", "brand")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Row number for RowNumber-based pagination (used in Azure Table Storage RowKey)
    /// </summary>
    public long RowNumber { get; set; }

    /// <summary>
    /// Source entity ID
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Destination entity ID
    /// </summary>
    public string DestinationId { get; set; } = string.Empty;

    /// <summary>
    /// Source store ID
    /// </summary>
    public string SourceStoreId { get; set; } = string.Empty;

    /// <summary>
    /// Destination store ID
    /// </summary>
    public string DestinationStoreId { get; set; } = string.Empty;

    /// <summary>
    /// Entity status (e.g., "pending", "mapped", "failed")
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Additional metadata about the mapping
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Related products data stored as JSON (for products with related products)
    /// </summary>
    public string? RelatedProductsData { get; set; }

    /// <summary>
    /// Channels data stored as JSON (for products with channel assignments)
    /// </summary>
    public string? ChannelsData { get; set; }

    /// <summary>
    /// Option and option value mappings stored as JSON for variant migration
    /// Format: { "options": [{ "sourceOptionId": "123", "destinationOptionId": "456", "optionValues": [{ "sourceId": "111", "destinationId": "222" }] }] }
    /// </summary>
    public string? OptionsMappingData { get; set; }

    /// <summary>
    /// When the mapping was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the mapping was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// API call tracking entry for rate limiting and monitoring
/// </summary>
public class ApiCallTracking
{
    /// <summary>
    /// Migration ID this API call belongs to
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Store ID the API call was made to
    /// </summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// API endpoint called
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method used
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code returned
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Response time in milliseconds
    /// </summary>
    public int ResponseTimeMs { get; set; }

    /// <summary>
    /// Request timestamp
    /// </summary>
    public DateTime RequestTimestamp { get; set; }

    /// <summary>
    /// Response timestamp
    /// </summary>
    public DateTime ResponseTimestamp { get; set; }

    /// <summary>
    /// Error message if the call failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether this call was successful
    /// </summary>
    public bool IsSuccessful { get; set; }
}

/// <summary>
/// API call statistics for rate limiting
/// </summary>
public class ApiCallStatistics
{
    /// <summary>
    /// Store ID
    /// </summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// Time window for these statistics
    /// </summary>
    public TimeSpan TimeWindow { get; set; }

    /// <summary>
    /// Total number of API calls in the time window
    /// </summary>
    public int TotalCalls { get; set; }

    /// <summary>
    /// Number of successful calls
    /// </summary>
    public int SuccessfulCalls { get; set; }

    /// <summary>
    /// Number of failed calls
    /// </summary>
    public int FailedCalls { get; set; }

    /// <summary>
    /// Average response time in milliseconds
    /// </summary>
    public double AverageResponseTimeMs { get; set; }

    /// <summary>
    /// Calls per minute rate
    /// </summary>
    public double CallsPerMinute { get; set; }

    /// <summary>
    /// Statistics calculation timestamp
    /// </summary>
    public DateTime CalculatedAt { get; set; }
}

/// <summary>
/// Cancellation token entry for migration cancellation
/// </summary>
public class CancellationTokenEntry
{
    /// <summary>
    /// Migration ID this cancellation token belongs to
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Cancellation reason
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Who requested the cancellation
    /// </summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>
    /// When the cancellation was requested
    /// </summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>
    /// Whether the cancellation has been processed
    /// </summary>
    public bool IsProcessed { get; set; }

    /// <summary>
    /// When the cancellation was processed
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Current status of the cancellation
    /// </summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Query request for migrations list
/// </summary>
public class MigrationQueryRequest
{
    /// <summary>
    /// Filter by migration status
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Filter by source store ID
    /// </summary>
    public string? SourceStoreId { get; set; }

    /// <summary>
    /// Filter by destination store ID
    /// </summary>
    public string? DestinationStoreId { get; set; }

    /// <summary>
    /// Filter by specific migration ID
    /// </summary>
    public string? MigrationId { get; set; }

    /// <summary>
    /// Filter by created date range start
    /// </summary>
    public DateTime? CreatedAfter { get; set; }

    /// <summary>
    /// Filter by created date range end
    /// </summary>
    public DateTime? CreatedBefore { get; set; }

    /// <summary>
    /// Page number for pagination (1-based)
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size for pagination
    /// </summary>
    public int PageSize { get; set; } = 10;

    /// <summary>
    /// Sort field
    /// </summary>
    public string SortBy { get; set; } = "CreatedAt";

    /// <summary>
    /// Sort direction (asc/desc)
    /// </summary>
    public string SortDirection { get; set; } = "desc";
}

/// <summary>
/// Result for migrations list query
/// </summary>
public class MigrationListResult
{
    /// <summary>
    /// List of migration entries
    /// </summary>
    public List<MigrationEntry> Migrations { get; set; } = new();

    /// <summary>
    /// Total count of migrations matching the query
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Current page number
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Page size used
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Whether there are more pages
    /// </summary>
    public bool HasMorePages { get; set; }
}

/// <summary>
/// Query request for API call history
/// </summary>
public class ApiCallQueryRequest
{
    /// <summary>
    /// Filter by store ID
    /// </summary>
    public string? StoreId { get; set; }

    /// <summary>
    /// Filter by endpoint
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Filter by HTTP method
    /// </summary>
    public string? Method { get; set; }

    /// <summary>
    /// Filter by success status
    /// </summary>
    public bool? IsSuccessful { get; set; }

    /// <summary>
    /// Filter by request timestamp range start
    /// </summary>
    public DateTime? RequestedAfter { get; set; }

    /// <summary>
    /// Filter by request timestamp range end
    /// </summary>
    public DateTime? RequestedBefore { get; set; }

    /// <summary>
    /// Page number for pagination (1-based)
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size for pagination
    /// </summary>
    public int PageSize { get; set; } = 10;

    /// <summary>
    /// Sort field
    /// </summary>
    public string SortBy { get; set; } = "RequestTimestamp";

    /// <summary>
    /// Sort direction (asc/desc)
    /// </summary>
    public string SortDirection { get; set; } = "desc";
}

/// <summary>
/// Result for API call history query
/// </summary>
public class ApiCallListResult
{
    /// <summary>
    /// List of API call tracking entries
    /// </summary>
    public List<ApiCallTracking> ApiCalls { get; set; } = new();

    /// <summary>
    /// Total count of API calls matching the query
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Current page number
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Page size used
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Whether there are more pages
    /// </summary>
    public bool HasMorePages { get; set; }
}

/// <summary>
/// Migration entry for tracking migration status
/// Moved from MigrationHttpFunctions.cs for better organization
/// </summary>
public class MigrationEntry
{
    /// <summary>
    /// Unique migration ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Source store ID
    /// </summary>
    public string SourceStoreId { get; set; } = string.Empty;

    /// <summary>
    /// Destination store ID
    /// </summary>
    public string DestinationStoreId { get; set; } = string.Empty;

    /// <summary>
    /// Source channel ID
    /// </summary>
    public string SourceChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Destination channel ID
    /// </summary>
    public string DestinationChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Entities to migrate
    /// </summary>
    public List<string> Entities { get; set; } = new();

    /// <summary>
    /// Migration status
    /// </summary>
    public MigrationStatus Status { get; set; }

    /// <summary>
    /// When the migration was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the migration was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Migration progress percentage (0-100)
    /// </summary>
    public int ProgressPercentage { get; set; }

    /// <summary>
    /// Current migration phase
    /// </summary>
    public string? CurrentPhase { get; set; }

    /// <summary>
    /// Error message if the migration failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Total number of entities to migrate
    /// </summary>
    public int TotalEntities { get; set; }

    /// <summary>
    /// Number of entities processed
    /// </summary>
    public int ProcessedEntities { get; set; }

    /// <summary>
    /// Number of entities that failed
    /// </summary>
    public int FailedEntities { get; set; }

    /// <summary>
    /// Number of entities that were skipped (duplicates, etc.)
    /// </summary>
    public int SkippedEntities { get; set; }

    /// <summary>
    /// Number of entities that were successfully migrated
    /// </summary>
    public int SuccessfulEntities { get; set; }

    /// <summary>
    /// Number of entities that were cancelled (due to migration cancellation)
    /// </summary>
    public int CancelledEntities { get; set; }
}

/// <summary>
/// Migration status enumeration
/// </summary>
public enum MigrationStatus
{
    /// <summary>
    /// Migration is queued for processing
    /// </summary>
    Queued,

    /// <summary>
    /// Migration is currently in progress
    /// </summary>
    InProgress,

    /// <summary>
    /// Migration completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Migration failed
    /// </summary>
    Failed,

    /// <summary>
    /// Migration was cancelled
    /// </summary>
    Cancelled
}

/// <summary>
/// Entity batch message for queue processing
/// </summary>
public class EntityBatchMessage
{
    /// <summary>
    /// Migration ID this batch belongs to
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type being processed (e.g., "product", "category")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Batch number for this entity type
    /// </summary>
    public int BatchNumber { get; set; }

    /// <summary>
    /// Total number of batches for this entity type
    /// </summary>
    public int TotalBatches { get; set; }

    /// <summary>
    /// List of entity IDs to process in this batch
    /// </summary>
    public List<string> EntityIds { get; set; } = new();

    /// <summary>
    /// Source store configuration
    /// </summary>
    public StoreConfiguration SourceStore { get; set; } = new();

    /// <summary>
    /// Destination store configuration
    /// </summary>
    public StoreConfiguration DestinationStore { get; set; } = new();

    /// <summary>
    /// Processing priority (1-10, higher is more important)
    /// </summary>
    public int Priority { get; set; } = 5;

    /// <summary>
    /// Maximum retry attempts for this batch
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Current retry attempt
    /// </summary>
    public int RetryAttempt { get; set; } = 0;

    /// <summary>
    /// When this batch was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When this batch should be processed (for delayed processing)
    /// </summary>
    public DateTime? ScheduledAt { get; set; }
}

/// <summary>
/// Queue message wrapper
/// </summary>
public class QueueMessage
{
    /// <summary>
    /// Queue message ID
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// Message content (JSON serialized)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Message type identifier
    /// </summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>
    /// Pop receipt for message operations
    /// </summary>
    public string PopReceipt { get; set; } = string.Empty;

    /// <summary>
    /// Number of times this message has been dequeued
    /// </summary>
    public int DequeueCount { get; set; }

    /// <summary>
    /// When the message was inserted into the queue
    /// </summary>
    public DateTime InsertionTime { get; set; }

    /// <summary>
    /// When the message expires (TTL)
    /// </summary>
    public DateTime ExpirationTime { get; set; }

    /// <summary>
    /// When the message becomes visible again
    /// </summary>
    public DateTime? NextVisibleTime { get; set; }
}

/// <summary>
/// Batch completion message
/// </summary>
public class BatchCompletionMessage
{
    /// <summary>
    /// Migration ID this batch belongs to
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type that was processed
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Batch number that was completed
    /// </summary>
    public int BatchNumber { get; set; }

    /// <summary>
    /// Total number of batches for this entity type
    /// </summary>
    public int TotalBatches { get; set; }

    /// <summary>
    /// Number of entities successfully processed
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of entities that failed processing
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Number of entities that were skipped
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// List of failed entity IDs with error details
    /// </summary>
    public List<EntityProcessingError> Failures { get; set; } = new();

    /// <summary>
    /// Processing start time
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Processing completion time
    /// </summary>
    public DateTime CompletionTime { get; set; }

    /// <summary>
    /// Worker that processed this batch
    /// </summary>
    public string WorkerId { get; set; } = string.Empty;
}

/// <summary>
/// Entity processing error details
/// </summary>
public class EntityProcessingError
{
    /// <summary>
    /// Entity ID that failed
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Error message
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Error code or type
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Full exception details
    /// </summary>
    public string? ExceptionDetails { get; set; }

    /// <summary>
    /// When the error occurred
    /// </summary>
    public DateTime ErrorTime { get; set; }
}

/// <summary>
/// Dead letter message for failed processing
/// </summary>
public class DeadLetterMessage
{
    /// <summary>
    /// Dead letter message ID
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// Original queue message
    /// </summary>
    public QueueMessage OriginalMessage { get; set; } = new();

    /// <summary>
    /// Reason for moving to dead letter queue
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Error details
    /// </summary>
    public string ErrorDetails { get; set; } = string.Empty;

    /// <summary>
    /// Number of retry attempts made
    /// </summary>
    public int RetryAttempts { get; set; }

    /// <summary>
    /// When the message was moved to dead letter queue
    /// </summary>
    public DateTime MovedAt { get; set; }

    /// <summary>
    /// Whether this message has been requeued
    /// </summary>
    public bool IsRequeued { get; set; }

    /// <summary>
    /// When the message was requeued (if applicable)
    /// </summary>
    public DateTime? RequeuedAt { get; set; }
}

/// <summary>
/// Queue statistics for monitoring
/// </summary>
public class QueueStatistics
{
    /// <summary>
    /// Queue name
    /// </summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>
    /// Approximate number of messages in the queue
    /// </summary>
    public int ApproximateMessageCount { get; set; }

    /// <summary>
    /// Average message age in the queue
    /// </summary>
    public TimeSpan AverageMessageAge { get; set; }

    /// <summary>
    /// Queue throughput (messages per minute)
    /// </summary>
    public double ThroughputPerMinute { get; set; }

    /// <summary>
    /// Average processing time per message
    /// </summary>
    public TimeSpan AverageProcessingTime { get; set; }

    /// <summary>
    /// Number of messages currently being processed
    /// </summary>
    public int MessagesInFlight { get; set; }

    /// <summary>
    /// Number of messages in dead letter queue
    /// </summary>
    public int DeadLetterCount { get; set; }

    /// <summary>
    /// When these statistics were calculated
    /// </summary>
    public DateTime CalculatedAt { get; set; }
}

/// <summary>
/// Report metadata for blob storage
/// </summary>
public class ReportMetadata
{
    /// <summary>
    /// Report file name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Blob URL of the report
    /// </summary>
    public string BlobUrl { get; set; } = string.Empty;

    /// <summary>
    /// Migration ID this report belongs to
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Type of report (e.g., "summary", "detailed", "errors")
    /// </summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Content type of the file
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// When the report was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the report was last modified
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// ETag for the blob
    /// </summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>
    /// Number of records in the report
    /// </summary>
    public int RecordCount { get; set; }

    /// <summary>
    /// Custom metadata for the report
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Blob metadata for file operations
/// </summary>
public class BlobMetadata
{
    /// <summary>
    /// Blob name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full blob URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Container name
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Content type of the blob
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Content encoding (e.g., "gzip")
    /// </summary>
    public string? ContentEncoding { get; set; }

    /// <summary>
    /// Content MD5 hash
    /// </summary>
    public string? ContentMD5 { get; set; }

    /// <summary>
    /// ETag for the blob
    /// </summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>
    /// When the blob was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the blob was last modified
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Custom metadata for the blob
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Access tier (Hot, Cool, Archive)
    /// </summary>
    public string? AccessTier { get; set; }

    /// <summary>
    /// Lease state of the blob
    /// </summary>
    public string? LeaseState { get; set; }

    /// <summary>
    /// Whether the blob is a snapshot
    /// </summary>
    public bool IsSnapshot { get; set; }

    /// <summary>
    /// Snapshot timestamp if applicable
    /// </summary>
    public DateTime? SnapshotTime { get; set; }
}

/// <summary>
/// Container metadata for blob storage
/// </summary>
public class ContainerMetadata
{
    /// <summary>
    /// Container name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Container URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Public access level
    /// </summary>
    public string PublicAccess { get; set; } = string.Empty;

    /// <summary>
    /// When the container was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the container was last modified
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// ETag for the container
    /// </summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>
    /// Custom metadata for the container
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Lease state of the container
    /// </summary>
    public string? LeaseState { get; set; }

    /// <summary>
    /// Whether the container has immutability policy
    /// </summary>
    public bool HasImmutabilityPolicy { get; set; }

    /// <summary>
    /// Whether the container has legal hold
    /// </summary>
    public bool HasLegalHold { get; set; }
}

/// <summary>
/// Storage usage statistics
/// </summary>
public class StorageUsageStatistics
{
    /// <summary>
    /// Container name
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Total number of blobs
    /// </summary>
    public int TotalBlobCount { get; set; }

    /// <summary>
    /// Total storage size in bytes
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Storage size formatted as human-readable string
    /// </summary>
    public string TotalSizeFormatted { get; set; } = string.Empty;

    /// <summary>
    /// Number of blobs in Hot tier
    /// </summary>
    public int HotTierCount { get; set; }

    /// <summary>
    /// Storage size in Hot tier (bytes)
    /// </summary>
    public long HotTierSizeBytes { get; set; }

    /// <summary>
    /// Number of blobs in Cool tier
    /// </summary>
    public int CoolTierCount { get; set; }

    /// <summary>
    /// Storage size in Cool tier (bytes)
    /// </summary>
    public long CoolTierSizeBytes { get; set; }

    /// <summary>
    /// Number of blobs in Archive tier
    /// </summary>
    public int ArchiveTierCount { get; set; }

    /// <summary>
    /// Storage size in Archive tier (bytes)
    /// </summary>
    public long ArchiveTierSizeBytes { get; set; }

    /// <summary>
    /// Average blob size in bytes
    /// </summary>
    public double AverageBlobSizeBytes { get; set; }

    /// <summary>
    /// Largest blob size in bytes
    /// </summary>
    public long LargestBlobSizeBytes { get; set; }

    /// <summary>
    /// Smallest blob size in bytes
    /// </summary>
    public long SmallestBlobSizeBytes { get; set; }

    /// <summary>
    /// When these statistics were calculated
    /// </summary>
    public DateTime CalculatedAt { get; set; }

    /// <summary>
    /// Estimated monthly cost (if pricing information is available)
    /// </summary>
    public decimal? EstimatedMonthlyCost { get; set; }
}

#region Message Processing Results

/// <summary>
/// Result of message processing operations
/// </summary>
public class MessageProcessingResult
{
    /// <summary>
    /// Indicates whether the message was processed successfully
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Processing result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error details if processing failed
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// Processing duration
    /// </summary>
    public TimeSpan ProcessingDuration { get; set; }

    /// <summary>
    /// Timestamp when processing completed
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Indicates whether the message should be retried
    /// </summary>
    public bool ShouldRetry { get; set; }

    /// <summary>
    /// Retry count if applicable
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Additional processing metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Result of message validation operations
/// </summary>
public class MessageValidationResult
{
    /// <summary>
    /// Indicates whether the message is valid
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation error message if invalid
    /// </summary>
    public string? ValidationError { get; set; }

    /// <summary>
    /// List of validation issues
    /// </summary>
    public List<string> ValidationIssues { get; set; } = new();

    /// <summary>
    /// Message schema version if applicable
    /// </summary>
    public string? SchemaVersion { get; set; }

    /// <summary>
    /// Timestamp when validation was performed
    /// </summary>
    public DateTime ValidatedAt { get; set; }
}

/// <summary>
/// Result of dead letter queue message processing
/// </summary>
public class DeadLetterProcessingResult
{
    /// <summary>
    /// Indicates whether the dead letter message was processed successfully
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Processing result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error details if processing failed
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// Indicates whether the message should be retried
    /// </summary>
    public bool ShouldRetry { get; set; }

    /// <summary>
    /// Recommended retry delay
    /// </summary>
    public TimeSpan? RetryDelay { get; set; }

    /// <summary>
    /// Maximum retry attempts before permanent failure
    /// </summary>
    public int MaxRetryAttempts { get; set; }

    /// <summary>
    /// Current retry attempt number
    /// </summary>
    public int CurrentRetryAttempt { get; set; }

    /// <summary>
    /// Timestamp when processing completed
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Indicates whether the message should be permanently discarded
    /// </summary>
    public bool ShouldDiscard { get; set; }

    /// <summary>
    /// Reason for discarding the message
    /// </summary>
    public string? DiscardReason { get; set; }
}

/// <summary>
/// Entity progress tracking entry for persisting entity-level progress data
/// </summary>
public class EntityProgressEntry
{
    /// <summary>
    /// Migration ID this progress entry belongs to
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type (e.g., "product", "category", "brand")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Total count of entities for this type
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
    /// Number of cancelled entities (e.g., due to migration cancellation)
    /// </summary>
    public int CancelledCount { get; set; }

    /// <summary>
    /// Progress percentage for this entity type (0.0 to 100.0)
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// Current status of entity processing
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Whether to show total count in UI (false for dynamic discovery phases)
    /// </summary>
    public bool ShowTotalCount { get; set; } = true;

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

    /// <summary>
    /// When the progress entry was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the progress entry was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}


#endregion 