using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models
{
    /// <summary>
    /// Configuration model for RowNumber service production optimization
    /// ✅ PHASE 8: Production-tuned settings based on Phase 7 integration test results
    /// </summary>
    public class RowNumberServiceConfiguration
    {
        /// <summary>
        /// Maximum retry attempts for concurrency conflicts (ETag failures)
        /// Phase 7 results: Optimized from 5 to 7 for better production resilience
        /// </summary>
        [Range(3, 10)]
        public int MaxRetryAttempts { get; set; } = 7;

        /// <summary>
        /// Base retry delay in milliseconds for exponential backoff
        /// Phase 7 results: Optimized from 100ms to 50ms for faster recovery
        /// </summary>
        [Range(25, 200)]
        public int BaseRetryDelayMs { get; set; } = 50;

        /// <summary>
        /// Maximum retry delay cap in milliseconds to prevent excessive waits
        /// ✅ NEW: Based on Phase 7 stress testing - prevents >1.6s delays
        /// </summary>
        [Range(500, 5000)]
        public int MaxRetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Maximum number of RowNumbers that can be allocated in a single range operation
        /// Prevents excessive range allocations and ensures reasonable batch sizes
        /// </summary>
        [Range(100, 50000)]
        public int MaxRangeAllocationSize { get; set; } = 10000;

        /// <summary>
        /// Threshold for preferring range allocation over individual assignments
        /// When batch size >= threshold, recommend using AllocateRangeAsync
        /// </summary>
        [Range(5, 100)]
        public int RangeAllocationThreshold { get; set; } = 10;

        /// <summary>
        /// Enable detailed performance metrics logging
        /// Useful for production monitoring and performance analysis
        /// </summary>
        public bool EnableDetailedMetrics { get; set; } = true;

        /// <summary>
        /// Interval for metrics aggregation in milliseconds
        /// Controls how frequently performance metrics are aggregated
        /// </summary>
        [Range(5000, 300000)]
        public int MetricsAggregationIntervalMs { get; set; } = 60000; // 1 minute

        /// <summary>
        /// Enable automatic performance optimization suggestions
        /// Logs recommendations when suboptimal usage patterns detected
        /// </summary>
        public bool EnablePerformanceRecommendations { get; set; } = true;
    }

    /// <summary>
    /// Azure Table Storage configuration for RowNumber service optimization
    /// ✅ PHASE 8: Production-tuned Azure Table Storage settings
    /// </summary>
    public class AzureTableStorageConfiguration
    {
        /// <summary>
        /// Maximum number of connections in the connection pool
        /// Optimized for Azure Functions scaling scenarios
        /// </summary>
        [Range(50, 500)]
        public int MaxConnections { get; set; } = 100;

        /// <summary>
        /// Request timeout in milliseconds for Azure Table Storage operations
        /// Balanced for reliability vs responsiveness
        /// </summary>
        [Range(5000, 60000)]
        public int RequestTimeoutMs { get; set; } = 30000; // 30 seconds

        /// <summary>
        /// Maximum retry attempts at the Azure SDK level
        /// Works in conjunction with application-level retries
        /// </summary>
        [Range(1, 5)]
        public int SdkMaxRetries { get; set; } = 3;

        /// <summary>
        /// Base delay for Azure SDK retry policy in milliseconds
        /// </summary>
        [Range(250, 2000)]
        public int SdkRetryDelayMs { get; set; } = 500;

        /// <summary>
        /// Maximum delay for Azure SDK retry policy in milliseconds
        /// </summary>
        [Range(2000, 10000)]
        public int SdkMaxDelayMs { get; set; } = 5000;

        /// <summary>
        /// Enable connection pooling optimization
        /// Reduces connection overhead for high-throughput scenarios
        /// </summary>
        public bool EnableConnectionPooling { get; set; } = true;
    }
}
