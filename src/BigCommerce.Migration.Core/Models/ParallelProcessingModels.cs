using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Core.Models;

#region Core Parallel Processing Models

/// <summary>
/// Configuration for parallel batch processing behavior
/// Controls concurrency, rate limiting integration, and progress tracking
/// </summary>
public class ParallelProcessingConfiguration
{
    /// <summary>
    /// Maximum number of batches to process concurrently
    /// If null, will be calculated dynamically based on system health
    /// </summary>
    [JsonPropertyName("maxConcurrentBatches")]
    public int? MaxConcurrentBatches { get; set; }

    /// <summary>
    /// Migration identifier for progress tracking context
    /// Task 3.2: Required for batch-level incremental progress updates
    /// </summary>
    [JsonPropertyName("migrationId")]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Store ID for rate limit context from Phase 1 dynamic rate limiting
    /// </summary>
    [JsonPropertyName("storeId")]
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type being processed (for rate limit optimization)
    /// </summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Whether to respect Phase 1 dynamic rate limits
    /// </summary>
    [JsonPropertyName("respectDynamicRateLimits")]
    public bool RespectDynamicRateLimits { get; set; } = true;

    /// <summary>
    /// Whether to enable real-time SignalR progress updates
    /// </summary>
    [JsonPropertyName("enableSignalRUpdates")]
    public bool EnableSignalRUpdates { get; set; } = true;

    /// <summary>
    /// Minimum delay between SignalR progress updates (to prevent flooding)
    /// </summary>
    [JsonPropertyName("signalRUpdateIntervalMs")]
    public int SignalRUpdateIntervalMs { get; set; } = 250; // 🎯 SUB-BATCH OPTIMIZATION: Default 250ms for granular progress

    /// <summary>
    /// Whether to enable adaptive concurrency adjustments during processing
    /// </summary>
    [JsonPropertyName("enableAdaptiveConcurrency")]
    public bool EnableAdaptiveConcurrency { get; set; } = true;

    /// <summary>
    /// 🎯 SUB-BATCH OPTIMIZATION: Per-entity sub-batch configuration settings
    /// Allows fine-tuning of sub-batch behavior for different entity types
    /// </summary>
    [JsonPropertyName("subBatchConfigurations")]
    public Dictionary<string, SubBatchConfiguration> SubBatchConfigurations { get; set; } = new();

    /// <summary>
    /// 🎯 SUB-BATCH OPTIMIZATION: Default sub-batch configuration for entities not explicitly configured
    /// </summary>
    [JsonPropertyName("defaultSubBatchConfiguration")]
    public SubBatchConfiguration DefaultSubBatchConfiguration { get; set; } = new();

    /// <summary>
    /// Whether to enable sub-batch optimization for non-hierarchical entities
    /// </summary>
    [JsonPropertyName("enableSubBatchOptimization")]
    public bool EnableSubBatchOptimization { get; set; } = true;

    /// <summary>
    /// Target CPU utilization percentage (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("targetCpuUtilization")]
    public double TargetCpuUtilization { get; set; } = 0.70;

    /// <summary>
    /// Timeout for individual batch processing
    /// </summary>
    [JsonPropertyName("batchTimeoutMinutes")]
    public int BatchTimeoutMinutes { get; set; } = 10;
}

/// <summary>
/// Aggregated results from parallel batch processing
/// </summary>
public class ParallelProcessingResult
{
    /// <summary>
    /// Total number of batches processed
    /// </summary>
    [JsonPropertyName("totalBatchesProcessed")]
    public int TotalBatchesProcessed { get; set; }

    /// <summary>
    /// Number of batches that completed successfully
    /// </summary>
    [JsonPropertyName("successfulBatches")]
    public int SuccessfulBatches { get; set; }

    /// <summary>
    /// Number of batches that failed
    /// </summary>
    [JsonPropertyName("failedBatches")]
    public int FailedBatches { get; set; }

    /// <summary>
    /// Total entities processed across all batches
    /// </summary>
    [JsonPropertyName("totalEntitiesProcessed")]
    public int TotalEntitiesProcessed { get; set; }

    /// <summary>
    /// Total entities that failed processing
    /// </summary>
    [JsonPropertyName("totalEntitiesFailed")]
    public int TotalEntitiesFailed { get; set; }

    /// <summary>
    /// Total entities that were skipped during processing
    /// </summary>
    [JsonPropertyName("totalEntitiesSkipped")]
    public int TotalEntitiesSkipped { get; set; }

    /// <summary>
    /// Total entities that were cancelled during processing
    /// </summary>
    [JsonPropertyName("totalEntitiesCancelled")]
    public int TotalEntitiesCancelled { get; set; }

    /// <summary>
    /// Total processing time for all parallel batches
    /// </summary>
    [JsonPropertyName("totalProcessingTime")]
    public TimeSpan TotalProcessingTime { get; set; }

    /// <summary>
    /// Average processing time per batch
    /// </summary>
    [JsonPropertyName("averageBatchProcessingTime")]
    public TimeSpan AverageBatchProcessingTime { get; set; }

    /// <summary>
    /// Peak concurrency level achieved during processing
    /// </summary>
    [JsonPropertyName("peakConcurrency")]
    public int PeakConcurrency { get; set; }

    /// <summary>
    /// Average concurrency level during processing
    /// </summary>
    [JsonPropertyName("averageConcurrency")]
    public double AverageConcurrency { get; set; }

    /// <summary>
    /// Overall throughput in entities per second
    /// </summary>
    [JsonPropertyName("overallThroughput")]
    public double OverallThroughput { get; set; }

    /// <summary>
    /// Improvement factor compared to sequential processing
    /// </summary>
    [JsonPropertyName("parallelizationImprovement")]
    public double ParallelizationImprovement { get; set; }

    /// <summary>
    /// Collection of errors that occurred during processing
    /// </summary>
    [JsonPropertyName("processingErrors")]
    public List<string> ProcessingErrors { get; set; } = new();

    /// <summary>
    /// Performance metrics collected during parallel processing
    /// </summary>
    [JsonPropertyName("performanceMetrics")]
    public ParallelPerformanceMetrics? PerformanceMetrics { get; set; }

    /// <summary>
    /// Whether the parallel processing completed successfully
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => FailedBatches == 0 && ProcessingErrors.Count == 0;

    /// <summary>
    /// Success rate as a percentage (0.0 to 1.0)
    /// </summary>
    [JsonIgnore]
    public double SuccessRate => TotalBatchesProcessed > 0 
        ? (double)SuccessfulBatches / TotalBatchesProcessed 
        : 0.0;

    /// <summary>
    /// Convenience property for compatibility with EntityMigrationDurableOrchestrator
    /// Maps to TotalEntitiesProcessed - TotalEntitiesFailed for successful entities
    /// </summary>
    [JsonIgnore]
    public int SuccessfulEntities => TotalEntitiesProcessed - TotalEntitiesFailed;

    /// <summary>
    /// Convenience property for compatibility with EntityMigrationDurableOrchestrator
    /// Maps to TotalEntitiesFailed
    /// </summary>
    [JsonIgnore]
    public int FailedEntities => TotalEntitiesFailed;

    /// <summary>
    /// Convenience property for compatibility with EntityMigrationDurableOrchestrator
    /// Maps to TotalEntitiesSkipped
    /// </summary>
    [JsonIgnore]
    public int SkippedEntities => TotalEntitiesSkipped;

    /// <summary>
    /// Convenience property for compatibility with EntityMigrationDurableOrchestrator
    /// Maps to TotalEntitiesCancelled
    /// </summary>
    [JsonIgnore]
    public int CancelledEntities => TotalEntitiesCancelled;
}

/// <summary>
/// Progress update for individual batch completion in parallel processing
/// </summary>
public class BatchProgressUpdate
{
    /// <summary>
    /// Migration ID for context
    /// </summary>
    [JsonPropertyName("migrationId")]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type being processed
    /// </summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Batch number that completed (1-based)
    /// </summary>
    [JsonPropertyName("completedBatchNumber")]
    public int CompletedBatchNumber { get; set; }

    /// <summary>
    /// Total number of batches in the migration
    /// </summary>
    [JsonPropertyName("totalBatches")]
    public int TotalBatches { get; set; }

    /// <summary>
    /// Number of entities processed in the completed batch
    /// </summary>
    [JsonPropertyName("batchEntitiesProcessed")]
    public int BatchEntitiesProcessed { get; set; }

    /// <summary>
    /// Number of entities that failed in the completed batch
    /// </summary>
    [JsonPropertyName("batchEntitiesFailed")]
    public int BatchEntitiesFailed { get; set; }

    /// <summary>
    /// Processing time for the completed batch
    /// </summary>
    [JsonPropertyName("batchProcessingTime")]
    public TimeSpan BatchProcessingTime { get; set; }

    /// <summary>
    /// Current overall progress percentage (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("overallProgressPercentage")]
    public double OverallProgressPercentage { get; set; }

    /// <summary>
    /// Current concurrency level when this batch completed
    /// </summary>
    [JsonPropertyName("currentConcurrency")]
    public int CurrentConcurrency { get; set; }

    /// <summary>
    /// Timestamp when the batch completed
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the batch completed successfully
    /// </summary>
    [JsonIgnore]
    public bool BatchSuccess => BatchEntitiesFailed == 0;

    /// <summary>
    /// Batch throughput in entities per second
    /// </summary>
    [JsonIgnore]
    public double BatchThroughput => BatchProcessingTime.TotalSeconds > 0 
        ? BatchEntitiesProcessed / BatchProcessingTime.TotalSeconds 
        : 0.0;
}

#endregion

/// <summary>
/// 🎯 SUB-BATCH OPTIMIZATION: Configuration for sub-batch processing behavior per entity type
/// Allows fine-tuning parallelism, batch sizes, and concurrency for optimal performance
/// </summary>
public class SubBatchConfiguration
{
    /// <summary>
    /// Entity type this configuration applies to (e.g., "brands", "products", "variants", "customers")
    /// </summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Page size for initial batch creation (how many entities per page)
    /// Default: 50 entities per page for optimal parallelism
    /// </summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// Chunk size for orchestrator-level chunking (how many entities per chunk)
    /// Default: 50 entities per chunk for optimal rate limiting
    /// </summary>
    [JsonPropertyName("chunkSize")]
    public int ChunkSize { get; set; } = 50;

    /// <summary>
    /// Fetch batch size for API calls (limit parameter for pagination)
    /// Default: 250 for most entities, 50 for brands
    /// </summary>
    [JsonPropertyName("fetchBatchSize")]
    public int FetchBatchSize { get; set; } = 250;

    /// <summary>
    /// Number of entities per sub-batch (within each page)
    /// Default: 5 entities per sub-batch for balanced concurrency
    /// </summary>
    [JsonPropertyName("subBatchSize")]
    public int SubBatchSize { get; set; } = 5;

    /// <summary>
    /// Maximum number of entities to process concurrently within a sub-batch
    /// Default: 5 for optimal API rate limit usage
    /// </summary>
    [JsonPropertyName("maxConcurrency")]
    public int MaxConcurrency { get; set; } = 5;

    /// <summary>
    /// Whether to enable sub-batch optimization for this entity type
    /// Default: true for non-hierarchical entities
    /// </summary>
    [JsonPropertyName("enableSubBatching")]
    public bool EnableSubBatching { get; set; } = true;

    /// <summary>
    /// Delay between sub-batch processing in milliseconds (0 = no delay)
    /// Useful for rate limit management or system load control
    /// </summary>
    [JsonPropertyName("subBatchDelayMs")]
    public int SubBatchDelayMs { get; set; } = 0;

    /// <summary>
    /// Whether to process sub-batches sequentially (true) or in parallel (false)
    /// Default: true to maintain order and prevent race conditions
    /// </summary>
    [JsonPropertyName("processSubBatchesSequentially")]
    public bool ProcessSubBatchesSequentially { get; set; } = true;

    /// <summary>
    /// Include parameter for BigCommerce API calls (e.g., "bulk_pricing_rules,custom_fields,channels,videos")
    /// Used for enhanced product migration to fetch additional entity data
    /// </summary>
    [JsonPropertyName("include")]
    public string? Include { get; set; }

    /// <summary>
    /// Whether to enable parallel processing of sub-entities (options, modifiers, images, reviews)
    /// Used for Enhanced Product Migration Phase 2 comprehensive entity processing
    /// </summary>
    [JsonPropertyName("enableParallelSubEntities")]
    public bool EnableParallelSubEntities { get; set; } = false;

    /// <summary>
    /// Custom settings specific to this entity type
    /// </summary>
    [JsonPropertyName("customSettings")]
    public Dictionary<string, object> CustomSettings { get; set; } = new();

    /// <summary>
    /// Creates default configurations for common entity types
    /// </summary>
    public static Dictionary<string, SubBatchConfiguration> GetDefaultConfigurations()
    {
        return new Dictionary<string, SubBatchConfiguration>
        {
            ["brands"] = new SubBatchConfiguration
            {
                EntityType = "brands",
                PageSize = 50,
                ChunkSize = 50,
                FetchBatchSize = 50,
                SubBatchSize = 5,
                MaxConcurrency = 5,
                EnableSubBatching = true,
                SubBatchDelayMs = 0,
                ProcessSubBatchesSequentially = true
            },
            ["products"] = new SubBatchConfiguration
            {
                EntityType = "products",
                PageSize = 25, // Products are more complex, smaller pages
                ChunkSize = 25,
                FetchBatchSize = 250,
                SubBatchSize = 3, // Fewer per sub-batch due to complexity
                MaxConcurrency = 3,
                EnableSubBatching = true,
                SubBatchDelayMs = 100, // Small delay for complex entities
                ProcessSubBatchesSequentially = true,
                Include = "bulk_pricing_rules,custom_fields,channels,videos" // ✅ Enhanced Product Migration Phase 1
            },
            ["variants"] = new SubBatchConfiguration
            {
                EntityType = "variants",
                PageSize = 100, // Variants are simpler, larger pages
                ChunkSize = 100,
                FetchBatchSize = 250,
                SubBatchSize = 10,
                MaxConcurrency = 8,
                EnableSubBatching = true,
                SubBatchDelayMs = 0,
                ProcessSubBatchesSequentially = true
            },
            ["customers"] = new SubBatchConfiguration
            {
                EntityType = "customers",
                PageSize = 75,
                ChunkSize = 75,
                FetchBatchSize = 250,
                SubBatchSize = 7,
                MaxConcurrency = 6,
                EnableSubBatching = true,
                SubBatchDelayMs = 50,
                ProcessSubBatchesSequentially = true
            }
        };
    }

    /// <summary>
    /// Gets the effective configuration for an entity type, falling back to defaults
    /// </summary>
    public static SubBatchConfiguration GetEffectiveConfiguration(
        string entityType, 
        Dictionary<string, SubBatchConfiguration>? customConfigurations = null)
    {
        var defaultConfigs = GetDefaultConfigurations();
        
        // Try custom configurations first
        if (customConfigurations?.TryGetValue(entityType.ToLowerInvariant(), out var customConfig) == true)
        {
            return customConfig;
        }
        
        // Try default configurations
        if (defaultConfigs.TryGetValue(entityType.ToLowerInvariant(), out var defaultConfig))
        {
            return defaultConfig;
        }
        
        // Fallback to generic default
        return new SubBatchConfiguration
        {
            EntityType = entityType,
            PageSize = 50,
            SubBatchSize = 5,
            MaxConcurrency = 5,
            EnableSubBatching = true,
            SubBatchDelayMs = 0,
            ProcessSubBatchesSequentially = true
        };
    }
}

#region Concurrency Management Models

/// <summary>
/// Result of optimal concurrency calculation for parallel batch processing
/// </summary>
public class OptimalConcurrencyResult
{
    /// <summary>
    /// Recommended number of concurrent batches
    /// </summary>
    [JsonPropertyName("optimalConcurrency")]
    public int OptimalConcurrency { get; set; }

    /// <summary>
    /// Reasoning for the concurrency recommendation
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// Current API health score (0.0 to 100.0)
    /// </summary>
    [JsonPropertyName("apiHealthScore")]
    public double ApiHealthScore { get; set; }

    /// <summary>
    /// Current system resource utilization (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("systemResourceUtilization")]
    public double SystemResourceUtilization { get; set; }

    /// <summary>
    /// Estimated maximum safe rate (requests per second) across all parallel batches
    /// </summary>
    [JsonPropertyName("estimatedMaxSafeRate")]
    public double EstimatedMaxSafeRate { get; set; }

    /// <summary>
    /// Confidence level in the concurrency recommendation (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("confidenceLevel")]
    public double ConfidenceLevel { get; set; }

    /// <summary>
    /// Timestamp when the calculation was performed
    /// </summary>
    [JsonPropertyName("calculatedAt")]
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of concurrency adjustment during parallel processing
/// </summary>
public class ConcurrencyAdjustmentResult
{
    /// <summary>
    /// New recommended concurrency level
    /// </summary>
    [JsonPropertyName("newConcurrency")]
    public int NewConcurrency { get; set; }

    /// <summary>
    /// Previous concurrency level
    /// </summary>
    [JsonPropertyName("previousConcurrency")]
    public int PreviousConcurrency { get; set; }

    /// <summary>
    /// Type of adjustment made
    /// </summary>
    [JsonPropertyName("adjustmentType")]
    public ConcurrencyAdjustmentType AdjustmentType { get; set; }

    /// <summary>
    /// Reason for the adjustment
    /// </summary>
    [JsonPropertyName("adjustmentReason")]
    public string AdjustmentReason { get; set; } = string.Empty;

    /// <summary>
    /// Performance trigger that caused the adjustment
    /// </summary>
    [JsonPropertyName("performanceTrigger")]
    public PerformanceTrigger PerformanceTrigger { get; set; }

    /// <summary>
    /// Timestamp when the adjustment was made
    /// </summary>
    [JsonPropertyName("adjustedAt")]
    public DateTime AdjustedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Type of concurrency adjustment
/// </summary>
public enum ConcurrencyAdjustmentType
{
    /// <summary>No concurrency adjustment needed</summary>
    NoChange,
    /// <summary>Increase concurrency due to good performance/health</summary>
    Increase,
    /// <summary>Decrease concurrency due to performance/health degradation</summary>
    Decrease,
    /// <summary>Emergency reduction due to critical conditions</summary>
    Emergency
}

/// <summary>
/// Performance trigger that caused concurrency adjustment
/// </summary>
public enum PerformanceTrigger
{
    /// <summary>No specific performance trigger</summary>
    None,
    /// <summary>API health metrics improved</summary>
    ApiHealthImproved,
    /// <summary>API health metrics degraded</summary>
    ApiHealthDegraded,
    /// <summary>Rate limit threshold approaching</summary>
    RateLimitApproaching,
    /// <summary>High CPU utilization detected</summary>
    HighCpuUsage,
    /// <summary>High memory utilization detected</summary>
    HighMemoryUsage,
    /// <summary>Error rate increased beyond threshold</summary>
    ErrorRateIncreased,
    /// <summary>Response time increased beyond threshold</summary>
    ResponseTimeIncreased,
    /// <summary>Throughput decreased below threshold</summary>
    ThroughputDecreased
}

#endregion

#region Performance Monitoring Models

/// <summary>
/// Real-time performance metrics for parallel processing
/// </summary>
public class ParallelPerformanceMetrics
{
    /// <summary>
    /// Current CPU utilization (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("cpuUtilization")]
    public double CpuUtilization { get; set; }

    /// <summary>
    /// Current memory utilization (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("memoryUtilization")]
    public double MemoryUtilization { get; set; }

    /// <summary>
    /// Average response time across all parallel batches (milliseconds)
    /// </summary>
    [JsonPropertyName("averageResponseTimeMs")]
    public double AverageResponseTimeMs { get; set; }

    /// <summary>
    /// Current error rate across all parallel processing (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }

    /// <summary>
    /// Current throughput in entities per second
    /// </summary>
    [JsonPropertyName("currentThroughput")]
    public double CurrentThroughput { get; set; }

    /// <summary>
    /// Number of active concurrent batches
    /// </summary>
    [JsonPropertyName("activeConcurrentBatches")]
    public int ActiveConcurrentBatches { get; set; }

    /// <summary>
    /// Number of batches waiting in queue
    /// </summary>
    [JsonPropertyName("queuedBatches")]
    public int QueuedBatches { get; set; }

    /// <summary>
    /// Current rate limit utilization from Phase 1 (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("rateLimitUtilization")]
    public double RateLimitUtilization { get; set; }

    /// <summary>
    /// SignalR update success rate (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("signalRUpdateSuccessRate")]
    public double SignalRUpdateSuccessRate { get; set; }

    /// <summary>
    /// Timestamp when metrics were captured
    /// </summary>
    [JsonPropertyName("capturedAt")]
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Comprehensive health metrics for parallel processing system
/// </summary>
public class ParallelProcessingHealthMetrics
{
    /// <summary>
    /// Overall health score (0.0 to 100.0)
    /// </summary>
    [JsonPropertyName("overallHealthScore")]
    public double OverallHealthScore { get; set; }

    /// <summary>
    /// Current performance metrics
    /// </summary>
    [JsonPropertyName("currentPerformance")]
    public ParallelPerformanceMetrics CurrentPerformance { get; set; } = new();

    /// <summary>
    /// Average performance over the last hour
    /// </summary>
    [JsonPropertyName("hourlyAveragePerformance")]
    public ParallelPerformanceMetrics HourlyAveragePerformance { get; set; } = new();

    /// <summary>
    /// Number of concurrency adjustments made in the last hour
    /// </summary>
    [JsonPropertyName("hourlyConcurrencyAdjustments")]
    public int HourlyConcurrencyAdjustments { get; set; }

    /// <summary>
    /// Recent errors in parallel processing
    /// </summary>
    [JsonPropertyName("recentErrors")]
    public List<string> RecentErrors { get; set; } = new();

    /// <summary>
    /// System resource pressure indicators
    /// </summary>
    [JsonPropertyName("systemPressure")]
    public SystemPressureIndicators SystemPressure { get; set; } = new();

    /// <summary>
    /// Integration health with Phase 1 dynamic rate limiting
    /// </summary>
    [JsonPropertyName("rateLimitingIntegrationHealth")]
    public bool RateLimitingIntegrationHealth { get; set; } = true;

    /// <summary>
    /// SignalR connectivity health
    /// </summary>
    [JsonPropertyName("signalRConnectivityHealth")]
    public bool SignalRConnectivityHealth { get; set; } = true;
}

/// <summary>
/// System resource pressure indicators
/// </summary>
public class SystemPressureIndicators
{
    /// <summary>
    /// Whether CPU pressure is detected
    /// </summary>
    [JsonPropertyName("cpuPressure")]
    public bool CpuPressure { get; set; }

    /// <summary>
    /// Whether memory pressure is detected
    /// </summary>
    [JsonPropertyName("memoryPressure")]
    public bool MemoryPressure { get; set; }

    /// <summary>
    /// Whether connection pool pressure is detected
    /// </summary>
    [JsonPropertyName("connectionPoolPressure")]
    public bool ConnectionPoolPressure { get; set; }

    /// <summary>
    /// Whether thread pool pressure is detected
    /// </summary>
    [JsonPropertyName("threadPoolPressure")]
    public bool ThreadPoolPressure { get; set; }
}

/// <summary>
/// Event arguments for parallel performance changes
/// </summary>
public class ParallelPerformanceChangedEventArgs : EventArgs
{
    /// <summary>
    /// Store ID context
    /// </summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// Previous performance metrics
    /// </summary>
    public ParallelPerformanceMetrics PreviousMetrics { get; set; } = new();

    /// <summary>
    /// Current performance metrics
    /// </summary>
    public ParallelPerformanceMetrics CurrentMetrics { get; set; } = new();

    /// <summary>
    /// Severity of the performance change
    /// </summary>
    public PerformanceChangeSeverity Severity { get; set; }

    /// <summary>
    /// Timestamp when the change was detected
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Severity level of performance changes
/// </summary>
public enum PerformanceChangeSeverity
{
    /// <summary>Informational performance change</summary>
    Info,
    /// <summary>Warning level performance change</summary>
    Warning,
    /// <summary>Critical performance change requiring immediate attention</summary>
    Critical
}

#endregion 