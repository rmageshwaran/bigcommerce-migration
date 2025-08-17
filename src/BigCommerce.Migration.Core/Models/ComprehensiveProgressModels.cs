using System.Text.Json.Serialization;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Core.Models;

#region Comprehensive Entity Migration Models

/// <summary>
/// Result model for comprehensive entity processing with sub-entity statistics
/// Used in Enhanced Product Migration Phase 2 for tracking parallel sub-entity processing
/// </summary>
public class ComprehensiveEntityProcessingResult : BatchProcessingResult
{
    /// <summary>
    /// Statistics for sub-entity processing (options, modifiers, images, reviews)
    /// </summary>
    [JsonPropertyName("subEntityStatistics")]
    public Dictionary<string, SubEntityStatistics> SubEntityStatistics { get; set; } = new();

    /// <summary>
    /// Total processing time for all sub-entities
    /// </summary>
    [JsonPropertyName("subEntityProcessingTime")]
    public TimeSpan SubEntityProcessingTime { get; set; }

    /// <summary>
    /// Whether parallel sub-entity processing was enabled
    /// </summary>
    [JsonPropertyName("parallelSubEntityProcessingEnabled")]
    public bool ParallelSubEntityProcessingEnabled { get; set; }

    /// <summary>
    /// Maximum concurrency used for sub-entity processing
    /// </summary>
    [JsonPropertyName("subEntityMaxConcurrency")]
    public int SubEntityMaxConcurrency { get; set; }

    /// <summary>
    /// Overall success rate including sub-entities
    /// </summary>
    [JsonIgnore]
    public double OverallSuccessRate => TotalProcessed > 0 
        ? (double)SuccessfulEntities / TotalProcessed 
        : 0.0;

    /// <summary>
    /// Sub-entity success rate across all entity types
    /// </summary>
    [JsonIgnore]
    public double SubEntitySuccessRate
    {
        get
        {
            var totalSubEntities = SubEntityStatistics.Values.Sum(s => s.TotalProcessed);
            var successfulSubEntities = SubEntityStatistics.Values.Sum(s => s.SuccessfulCount);
            return totalSubEntities > 0 ? (double)successfulSubEntities / totalSubEntities : 0.0;
        }
    }
}

/// <summary>
/// Statistics for individual sub-entity type processing (options, modifiers, images, reviews)
/// </summary>
public class SubEntityStatistics
{
    /// <summary>
    /// Sub-entity type (e.g., "options", "modifiers", "images", "reviews")
    /// </summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Total number of sub-entities discovered for processing
    /// </summary>
    [JsonPropertyName("totalDiscovered")]
    public int TotalDiscovered { get; set; }

    /// <summary>
    /// Total number of sub-entities processed (attempted)
    /// </summary>
    [JsonPropertyName("totalProcessed")]
    public int TotalProcessed { get; set; }

    /// <summary>
    /// Number of successfully created sub-entities
    /// </summary>
    [JsonPropertyName("successfulCount")]
    public int SuccessfulCount { get; set; }

    /// <summary>
    /// Number of failed sub-entity creations
    /// </summary>
    [JsonPropertyName("failedCount")]
    public int FailedCount { get; set; }

    /// <summary>
    /// Number of sub-entities skipped (e.g., already exist, invalid data)
    /// </summary>
    [JsonPropertyName("skippedCount")]
    public int SkippedCount { get; set; }

    /// <summary>
    /// Processing time for this sub-entity type
    /// </summary>
    [JsonPropertyName("processingTime")]
    public TimeSpan ProcessingTime { get; set; }

    /// <summary>
    /// Throughput in sub-entities per second
    /// </summary>
    [JsonIgnore]
    public double Throughput => ProcessingTime.TotalSeconds > 0 
        ? TotalProcessed / ProcessingTime.TotalSeconds 
        : 0.0;

    /// <summary>
    /// Success rate for this sub-entity type
    /// </summary>
    [JsonIgnore]
    public double SuccessRate => TotalProcessed > 0 
        ? (double)SuccessfulCount / TotalProcessed 
        : 0.0;
}

#endregion

#region Dual-Tier Progress Aggregation Models

/// <summary>
/// Tier 1 Progress: Pipeline-level progress for individual chunk processing
/// Provides real-time updates within a single ProcessEntityChunkActivity execution
/// </summary>
public class PipelineProgressUpdate
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    [JsonPropertyName("migrationId")]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Parent entity type being processed (e.g., "enhanced-products")
    /// </summary>
    [JsonPropertyName("parentEntityType")]
    public string ParentEntityType { get; set; } = string.Empty;

    /// <summary>
    /// Current batch/chunk number being processed
    /// </summary>
    [JsonPropertyName("batchNumber")]
    public int BatchNumber { get; set; }

    /// <summary>
    /// Total number of batches/chunks for this entity type
    /// </summary>
    [JsonPropertyName("totalBatches")]
    public int TotalBatches { get; set; }

    /// <summary>
    /// Current product being processed within the batch
    /// </summary>
    [JsonPropertyName("currentProduct")]
    public int CurrentProduct { get; set; }

    /// <summary>
    /// Total products in this batch
    /// </summary>
    [JsonPropertyName("totalProductsInBatch")]
    public int TotalProductsInBatch { get; set; }

    /// <summary>
    /// Progress for individual sub-entity types within current product
    /// </summary>
    [JsonPropertyName("subEntityProgress")]
    public Dictionary<string, SubEntityProgress> SubEntityProgress { get; set; } = new();

    /// <summary>
    /// Current phase of processing (e.g., "Fetching", "Creating Products", "Processing Options")
    /// </summary>
    [JsonPropertyName("currentPhase")]
    public string CurrentPhase { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of this progress update
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Estimated time remaining for current batch
    /// </summary>
    [JsonPropertyName("estimatedTimeRemaining")]
    public TimeSpan? EstimatedTimeRemaining { get; set; }
}

/// <summary>
/// Progress information for individual sub-entity type within a product
/// </summary>
public class SubEntityProgress
{
    /// <summary>
    /// Sub-entity type (e.g., "options", "modifiers", "images", "reviews")
    /// </summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Current processing status
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // "pending", "processing", "completed", "failed"

    /// <summary>
    /// Number of sub-entities processed for this type
    /// </summary>
    [JsonPropertyName("processedCount")]
    public int ProcessedCount { get; set; }

    /// <summary>
    /// Total number of sub-entities discovered for this type
    /// </summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    /// <summary>
    /// Number of successful creations
    /// </summary>
    [JsonPropertyName("successCount")]
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of failed creations
    /// </summary>
    [JsonPropertyName("failureCount")]
    public int FailureCount { get; set; }

    /// <summary>
    /// Processing start time
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Processing completion time
    /// </summary>
    [JsonPropertyName("completionTime")]
    public DateTime? CompletionTime { get; set; }

    /// <summary>
    /// Current throughput in entities per second
    /// </summary>
    [JsonIgnore]
    public double Throughput
    {
        get
        {
            if (!StartTime.HasValue || ProcessedCount == 0) return 0.0;
            var elapsed = (CompletionTime ?? DateTime.UtcNow) - StartTime.Value;
            return elapsed.TotalSeconds > 0 ? ProcessedCount / elapsed.TotalSeconds : 0.0;
        }
    }
}

/// <summary>
/// Tier 2 Progress: Universal migration progress across all entity types
/// Provides ecosystem-wide visibility for the entire migration
/// </summary>
public class UniversalMigrationProgress
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    [JsonPropertyName("migrationId")]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Progress for primary entity types (brands, products, enhanced-products)
    /// </summary>
    [JsonPropertyName("primaryEntityProgress")]
    public Dictionary<string, EntityTypeProgress> PrimaryEntityProgress { get; set; } = new();

    /// <summary>
    /// Progress for comprehensive sub-entities grouped by parent type
    /// </summary>
    [JsonPropertyName("comprehensiveEntityProgress")]
    public Dictionary<string, Dictionary<string, EntityTypeProgress>> ComprehensiveEntityProgress { get; set; } = new();

    /// <summary>
    /// Overall migration statistics
    /// </summary>
    [JsonPropertyName("overallStatistics")]
    public MigrationStatistics OverallStatistics { get; set; } = new();

    /// <summary>
    /// Last update timestamp
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Progress information for a specific entity type
/// </summary>
public class EntityTypeProgress
{
    /// <summary>
    /// Entity type name
    /// </summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Total entities discovered/estimated for migration
    /// </summary>
    [JsonPropertyName("totalEntities")]
    public int TotalEntities { get; set; }

    /// <summary>
    /// Number of entities processed (attempted)
    /// </summary>
    [JsonPropertyName("processedEntities")]
    public int ProcessedEntities { get; set; }

    /// <summary>
    /// Number of successful entity migrations
    /// </summary>
    [JsonPropertyName("successfulEntities")]
    public int SuccessfulEntities { get; set; }

    /// <summary>
    /// Number of failed entity migrations
    /// </summary>
    [JsonPropertyName("failedEntities")]
    public int FailedEntities { get; set; }

    /// <summary>
    /// Number of skipped entity migrations
    /// </summary>
    [JsonPropertyName("skippedEntities")]
    public int SkippedEntities { get; set; }

    /// <summary>
    /// Number of cancelled entity migrations
    /// </summary>
    [JsonPropertyName("cancelledEntities")]
    public int CancelledEntities { get; set; }

    /// <summary>
    /// Current processing status
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // "pending", "processing", "completed", "failed"

    /// <summary>
    /// Processing start time
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Processing completion time
    /// </summary>
    [JsonPropertyName("completionTime")]
    public DateTime? CompletionTime { get; set; }

    /// <summary>
    /// Current throughput in entities per second
    /// </summary>
    [JsonIgnore]
    public double Throughput
    {
        get
        {
            if (!StartTime.HasValue || ProcessedEntities == 0) return 0.0;
            var elapsed = (CompletionTime ?? DateTime.UtcNow) - StartTime.Value;
            return elapsed.TotalSeconds > 0 ? ProcessedEntities / elapsed.TotalSeconds : 0.0;
        }
    }

    /// <summary>
    /// Success rate for this entity type
    /// </summary>
    [JsonIgnore]
    public double SuccessRate => ProcessedEntities > 0 
        ? (double)SuccessfulEntities / ProcessedEntities 
        : 0.0;
}

/// <summary>
/// Overall migration statistics across all entity types
/// </summary>
public class MigrationStatistics
{
    /// <summary>
    /// Total entities across all types
    /// </summary>
    [JsonPropertyName("totalEntities")]
    public int TotalEntities { get; set; }

    /// <summary>
    /// Total processed entities across all types
    /// </summary>
    [JsonPropertyName("processedEntities")]
    public int ProcessedEntities { get; set; }

    /// <summary>
    /// Total successful entities across all types
    /// </summary>
    [JsonPropertyName("successfulEntities")]
    public int SuccessfulEntities { get; set; }

    /// <summary>
    /// Total failed entities across all types
    /// </summary>
    [JsonPropertyName("failedEntities")]
    public int FailedEntities { get; set; }

    /// <summary>
    /// Migration start time
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Estimated completion time based on current throughput
    /// </summary>
    [JsonPropertyName("estimatedCompletionTime")]
    public DateTime? EstimatedCompletionTime { get; set; }

    /// <summary>
    /// Overall migration success rate
    /// </summary>
    [JsonIgnore]
    public double OverallSuccessRate => ProcessedEntities > 0 
        ? (double)SuccessfulEntities / ProcessedEntities 
        : 0.0;

    /// <summary>
    /// Overall migration progress percentage
    /// </summary>
    [JsonIgnore]
    public double ProgressPercentage => TotalEntities > 0 
        ? (double)ProcessedEntities / TotalEntities * 100.0 
        : 0.0;

    /// <summary>
    /// Overall throughput in entities per second
    /// </summary>
    [JsonIgnore]
    public double OverallThroughput
    {
        get
        {
            if (!StartTime.HasValue || ProcessedEntities == 0) return 0.0;
            var elapsed = DateTime.UtcNow - StartTime.Value;
            return elapsed.TotalSeconds > 0 ? ProcessedEntities / elapsed.TotalSeconds : 0.0;
        }
    }
}

#endregion