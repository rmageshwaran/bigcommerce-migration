using System;
using System.Collections.Generic;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Supporting data models for StorageIndexOptimizer
    /// Phase 5: Task 5.2.2 - Storage Index Optimization
    /// </summary>

    /// <summary>
    /// Represents a query pattern for optimization analysis
    /// </summary>
    public class QueryPattern
    {
        /// <summary>
        /// Gets or sets the type of query operation
        /// </summary>
        public QueryType QueryType { get; set; }

        /// <summary>
        /// Gets or sets the type of storage operation
        /// </summary>
        public OperationType OperationType { get; set; }

        /// <summary>
        /// Gets or sets the frequency of this pattern per hour
        /// </summary>
        public int FrequencyPerHour { get; set; }

        /// <summary>
        /// Gets or sets the average latency for this pattern
        /// </summary>
        public double AverageLatencyMs { get; set; }

        /// <summary>
        /// Gets or sets the fields used in filtering
        /// </summary>
        public IList<string> FilterFields { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the partition key pattern
        /// </summary>
        public string PartitionKeyPattern { get; set; } = "";

        /// <summary>
        /// Gets or sets the row key pattern
        /// </summary>
        public string RowKeyPattern { get; set; } = "";
    }

    /// <summary>
    /// Types of query operations
    /// </summary>
    public enum QueryType
    {
        /// <summary>
        /// Point query for specific entity
        /// </summary>
        Point,

        /// <summary>
        /// Range query across multiple entities
        /// </summary>
        Range,

        /// <summary>
        /// Equality query with filters
        /// </summary>
        Equality,

        /// <summary>
        /// Read operation
        /// </summary>
        Read,

        /// <summary>
        /// Write operation
        /// </summary>
        Write,

        /// <summary>
        /// Scan operation across partitions
        /// </summary>
        Scan
    }

    /// <summary>
    /// Types of storage operations
    /// </summary>
    public enum OperationType
    {
        /// <summary>
        /// Read operation
        /// </summary>
        Read,

        /// <summary>
        /// Write operation
        /// </summary>
        Write,

        /// <summary>
        /// Update operation
        /// </summary>
        Update,

        /// <summary>
        /// Delete operation
        /// </summary>
        Delete,

        /// <summary>
        /// Batch operation
        /// </summary>
        Batch
    }

    /// <summary>
    /// Analysis of data distribution characteristics
    /// </summary>
    public class DataDistributionAnalysis
    {
        /// <summary>
        /// Gets or sets the total number of entities
        /// </summary>
        public int EntityCount { get; set; }

        /// <summary>
        /// Gets or sets the data skew factor (0.0 to 1.0)
        /// </summary>
        public double SkewFactor { get; set; }

        /// <summary>
        /// Gets or sets the percentage of requests hitting hot partitions
        /// </summary>
        public double HotspotPercentage { get; set; }

        /// <summary>
        /// Gets or sets the average entity size in bytes
        /// </summary>
        public long AverageEntitySize { get; set; }

        /// <summary>
        /// Gets or sets the growth rate per month
        /// </summary>
        public double GrowthRatePerMonth { get; set; }

        /// <summary>
        /// Gets or sets the distribution of entities across partitions
        /// </summary>
        public IDictionary<string, int> PartitionDistribution { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// Strategy for optimizing partition keys
    /// </summary>
    public class PartitionKeyStrategy
    {
        /// <summary>
        /// Gets or sets the entity type
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the recommended partition pattern
        /// </summary>
        public PartitionPattern RecommendedPattern { get; set; }

        /// <summary>
        /// Gets or sets the optimal number of partitions
        /// </summary>
        public int PartitionCount { get; set; }

        /// <summary>
        /// Gets or sets the load balancing strategy
        /// </summary>
        public LoadBalancingStrategy LoadBalancingStrategy { get; set; }

        /// <summary>
        /// Gets or sets the expected performance improvement percentage
        /// </summary>
        public double ExpectedImprovement { get; set; }

        /// <summary>
        /// Gets or sets the partition key templates
        /// </summary>
        public IList<string> PartitionKeyTemplates { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the hotspot prevention strategy
        /// </summary>
        public HotspotPreventionStrategy HotspotPrevention { get; set; } = new();
    }

    /// <summary>
    /// Partition patterns for different scenarios
    /// </summary>
    public enum PartitionPattern
    {
        /// <summary>
        /// Partition by store ID
        /// </summary>
        StoreBasedPartitioning,

        /// <summary>
        /// Partition using hash function
        /// </summary>
        HashBasedPartitioning,

        /// <summary>
        /// Partition by time ranges
        /// </summary>
        TimeBasedPartitioning,

        /// <summary>
        /// Partition by entity prefix
        /// </summary>
        PrefixBasedPartitioning,

        /// <summary>
        /// Custom partitioning strategy
        /// </summary>
        CustomPartitioning
    }

    /// <summary>
    /// Load balancing strategies
    /// </summary>
    public enum LoadBalancingStrategy
    {
        /// <summary>
        /// Optimized for read operations
        /// </summary>
        ReadOptimized,

        /// <summary>
        /// Optimized for write operations
        /// </summary>
        WriteOptimized,

        /// <summary>
        /// Balanced for mixed workloads
        /// </summary>
        Balanced,

        /// <summary>
        /// Round-robin distribution
        /// </summary>
        RoundRobin
    }

    /// <summary>
    /// Strategy for preventing partition hotspots
    /// </summary>
    public class HotspotPreventionStrategy
    {
        /// <summary>
        /// Gets or sets whether to use hashing salt
        /// </summary>
        public bool UseHashingSalt { get; set; }

        /// <summary>
        /// Gets or sets the partition rotation interval
        /// </summary>
        public TimeSpan RotationInterval { get; set; }

        /// <summary>
        /// Gets or sets the maximum partition utilization
        /// </summary>
        public double MaxPartitionUtilization { get; set; }

        /// <summary>
        /// Gets or sets the rebalancing threshold
        /// </summary>
        public double RebalancingThreshold { get; set; }

        /// <summary>
        /// Gets or sets the salt characters for hashing
        /// </summary>
        public string SaltCharacters { get; set; } = "ABCDEFGHIJ";
    }

    /// <summary>
    /// Represents an access pattern for entities
    /// </summary>
    public class AccessPattern
    {
        /// <summary>
        /// Gets or sets the type of access
        /// </summary>
        public AccessType AccessType { get; set; }

        /// <summary>
        /// Gets or sets the primary filter field
        /// </summary>
        public string FilterField { get; set; } = "";

        /// <summary>
        /// Gets or sets all filter fields used
        /// </summary>
        public IList<string> FilterFields { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets whether sorting is required
        /// </summary>
        public bool RequiresSorting { get; set; }

        /// <summary>
        /// Gets or sets the frequency of this access pattern
        /// </summary>
        public int Frequency { get; set; }

        /// <summary>
        /// Gets or sets the average response time
        /// </summary>
        public double AverageResponseTimeMs { get; set; }
    }

    /// <summary>
    /// Types of data access patterns
    /// </summary>
    public enum AccessType
    {
        /// <summary>
        /// Point access to specific entity
        /// </summary>
        Point,

        /// <summary>
        /// Range access across multiple entities
        /// </summary>
        Range,

        /// <summary>
        /// Scan across all entities
        /// </summary>
        Scan,

        /// <summary>
        /// Batch access to multiple entities
        /// </summary>
        Batch
    }

    /// <summary>
    /// Requirements for data sorting
    /// </summary>
    public class SortingRequirements
    {
        /// <summary>
        /// Gets or sets the fields used for sorting
        /// </summary>
        public IList<string> SortFields { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the default sort direction
        /// </summary>
        public SortDirection DefaultDirection { get; set; } = SortDirection.Ascending;

        /// <summary>
        /// Gets or sets whether descending sort is required
        /// </summary>
        public bool RequiresDescendingSort { get; set; }

        /// <summary>
        /// Gets or sets whether multiple sort orders are needed
        /// </summary>
        public bool RequiresMultipleSortOrders { get; set; }
    }

    /// <summary>
    /// Sort direction options
    /// </summary>
    public enum SortDirection
    {
        /// <summary>
        /// Ascending order
        /// </summary>
        Ascending,

        /// <summary>
        /// Descending order
        /// </summary>
        Descending
    }

    /// <summary>
    /// Strategy for optimizing row keys
    /// </summary>
    public class RowKeyStrategy
    {
        /// <summary>
        /// Gets or sets the entity type
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the recommended row key pattern
        /// </summary>
        public RowKeyPattern RecommendedPattern { get; set; }

        /// <summary>
        /// Gets or sets the sorting optimization strategy
        /// </summary>
        public SortingOptimizationStrategy SortingOptimization { get; set; } = new();

        /// <summary>
        /// Gets or sets the range query optimization
        /// </summary>
        public RangeQueryOptimization RangeQueryOptimization { get; set; } = new();

        /// <summary>
        /// Gets or sets the composite key strategy
        /// </summary>
        public CompositeKeyStrategy CompositeKeyStrategy { get; set; } = new();

        /// <summary>
        /// Gets or sets the row key templates
        /// </summary>
        public IList<string> RowKeyTemplates { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the expected query performance improvement
        /// </summary>
        public double ExpectedQueryImprovement { get; set; }

        /// <summary>
        /// Hierarchical key strategy
        /// </summary>
        public static readonly RowKeyStrategy HierarchicalKey = new() { RecommendedPattern = RowKeyPattern.Hierarchical };

        /// <summary>
        /// Composite key strategy
        /// </summary>
        public static readonly RowKeyStrategy CompositeKey = new() { RecommendedPattern = RowKeyPattern.Composite };

        /// <summary>
        /// Sequential key strategy
        /// </summary>
        public static readonly RowKeyStrategy SequentialKey = new() { RecommendedPattern = RowKeyPattern.Sequential };
    }

    /// <summary>
    /// Row key patterns for different use cases
    /// </summary>
    public enum RowKeyPattern
    {
        /// <summary>
        /// Simple sequential pattern
        /// </summary>
        Sequential,

        /// <summary>
        /// Reverse timestamp for recent-first ordering
        /// </summary>
        ReverseTimestamp,

        /// <summary>
        /// Padded numeric for consistent sorting
        /// </summary>
        PaddedNumeric,

        /// <summary>
        /// Composite key with multiple fields
        /// </summary>
        Composite,

        /// <summary>
        /// Hierarchical key for tree structures
        /// </summary>
        Hierarchical,

        /// <summary>
        /// Custom pattern for specific needs
        /// </summary>
        Custom
    }

    /// <summary>
    /// Strategy for sorting optimization
    /// </summary>
    public class SortingOptimizationStrategy
    {
        /// <summary>
        /// Gets or sets the primary sort field
        /// </summary>
        public string PrimarySort { get; set; } = "";

        /// <summary>
        /// Gets or sets the sort direction
        /// </summary>
        public SortDirection SortDirection { get; set; }

        /// <summary>
        /// Gets or sets whether to use reverse key for descending sort
        /// </summary>
        public bool UseReverseKey { get; set; }

        /// <summary>
        /// Gets or sets the composite key field order
        /// </summary>
        public IList<string> CompositeKeyOrder { get; set; } = new List<string>();
    }

    /// <summary>
    /// Optimization for range queries
    /// </summary>
    public class RangeQueryOptimization
    {
        /// <summary>
        /// Gets or sets whether to optimize for time-based ranges
        /// </summary>
        public bool OptimizeForTimeRanges { get; set; }

        /// <summary>
        /// Gets or sets whether to optimize for numeric ranges
        /// </summary>
        public bool OptimizeForNumericRanges { get; set; }

        /// <summary>
        /// Gets or sets whether to use seekable keys
        /// </summary>
        public bool UseSeekableKeys { get; set; }

        /// <summary>
        /// Gets or sets the expected speedup percentage
        /// </summary>
        public double ExpectedSpeedup { get; set; }
    }

    /// <summary>
    /// Strategy for composite keys
    /// </summary>
    public class CompositeKeyStrategy
    {
        /// <summary>
        /// Gets or sets whether to use composite keys
        /// </summary>
        public bool UseCompositeKeys { get; set; }

        /// <summary>
        /// Gets or sets the optimal field order for the key
        /// </summary>
        public IList<string> KeyFieldOrder { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the separator strategy
        /// </summary>
        public SeparatorStrategy SeparatorStrategy { get; set; }

        /// <summary>
        /// Gets or sets the padding strategy for numeric fields
        /// </summary>
        public PaddingStrategy PaddingStrategy { get; set; }
    }

    /// <summary>
    /// Strategies for field separation in composite keys
    /// </summary>
    public enum SeparatorStrategy
    {
        /// <summary>
        /// Use underscore separator
        /// </summary>
        Underscore,

        /// <summary>
        /// Use pipe separator
        /// </summary>
        Pipe,

        /// <summary>
        /// Use dash separator
        /// </summary>
        Dash,

        /// <summary>
        /// Use no separator (fixed width)
        /// </summary>
        None
    }

    /// <summary>
    /// Strategies for padding numeric values
    /// </summary>
    public enum PaddingStrategy
    {
        /// <summary>
        /// Left-pad with zeros
        /// </summary>
        LeftZeroPad,

        /// <summary>
        /// Right-pad with spaces
        /// </summary>
        RightSpacePad,

        /// <summary>
        /// No padding
        /// </summary>
        None,

        /// <summary>
        /// Custom padding strategy
        /// </summary>
        Custom
    }

    /// <summary>
    /// Represents a storage query for optimization
    /// </summary>
    public class StorageQuery
    {
        /// <summary>
        /// Gets or sets the partition key
        /// </summary>
        public string PartitionKey { get; set; } = "";

        /// <summary>
        /// Gets or sets the row key pattern
        /// </summary>
        public string RowKey { get; set; } = "";

        /// <summary>
        /// Gets or sets the query type
        /// </summary>
        public QueryType QueryType { get; set; }

        /// <summary>
        /// Gets or sets the estimated latency in milliseconds
        /// </summary>
        public double EstimatedLatencyMs { get; set; }

        /// <summary>
        /// Gets or sets the filter conditions
        /// </summary>
        public IDictionary<string, object> Filters { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Gets or sets the expected result count
        /// </summary>
        public int ExpectedResultCount { get; set; }
    }

    /// <summary>
    /// Options for query batching optimization
    /// </summary>
    public class QueryBatchingOptions
    {
        /// <summary>
        /// Gets or sets the maximum batch size
        /// </summary>
        public int MaxBatchSize { get; set; } = 100;

        /// <summary>
        /// Gets or sets the maximum concurrency level
        /// </summary>
        public int MaxConcurrency { get; set; } = 5;

        /// <summary>
        /// Gets or sets the timeout for batch operations
        /// </summary>
        public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets whether to enable adaptive batching
        /// </summary>
        public bool EnableAdaptiveBatching { get; set; } = true;

        /// <summary>
        /// Gets or sets the performance threshold for adaptive adjustments
        /// </summary>
        public double PerformanceThreshold { get; set; } = 0.8;
    }

    /// <summary>
    /// Group of related queries for batch optimization
    /// </summary>
    public class QueryGroup
    {
        /// <summary>
        /// Gets or sets the partition key for this group
        /// </summary>
        public string PartitionKey { get; set; } = "";

        /// <summary>
        /// Gets or sets the query type for this group
        /// </summary>
        public QueryType QueryType { get; set; }

        /// <summary>
        /// Gets or sets the queries in this group
        /// </summary>
        public IList<StorageQuery> Queries { get; set; } = new List<StorageQuery>();

        /// <summary>
        /// Gets or sets the estimated total latency
        /// </summary>
        public double EstimatedLatency { get; set; }
    }

    /// <summary>
    /// Optimized batch of queries for execution
    /// </summary>
    public class OptimizedQueryBatch
    {
        /// <summary>
        /// Gets or sets the batch identifier
        /// </summary>
        public int BatchId { get; set; }

        /// <summary>
        /// Gets or sets the partition key for this batch
        /// </summary>
        public string PartitionKey { get; set; } = "";

        /// <summary>
        /// Gets or sets the queries in this batch
        /// </summary>
        public IList<StorageQuery> Queries { get; set; } = new List<StorageQuery>();

        /// <summary>
        /// Gets or sets the estimated execution latency
        /// </summary>
        public double EstimatedLatency { get; set; }

        /// <summary>
        /// Gets or sets the optimization type applied
        /// </summary>
        public BatchOptimizationType OptimizationType { get; set; }
    }

    /// <summary>
    /// Types of batch optimization
    /// </summary>
    public enum BatchOptimizationType
    {
        /// <summary>
        /// Optimized for read operations
        /// </summary>
        ReadOptimized,

        /// <summary>
        /// Optimized for write operations
        /// </summary>
        WriteOptimized,

        /// <summary>
        /// Optimized for range queries
        /// </summary>
        RangeOptimized,

        /// <summary>
        /// Mixed optimization
        /// </summary>
        Mixed
    }

    /// <summary>
    /// Plan for executing query batches
    /// </summary>
    public class BatchExecutionPlan
    {
        /// <summary>
        /// Gets or sets the execution stages
        /// </summary>
        public IList<ExecutionStage> ExecutionStages { get; set; } = new List<ExecutionStage>();

        /// <summary>
        /// Gets or sets the parallelism level
        /// </summary>
        public int ParallelismLevel { get; set; }

        /// <summary>
        /// Gets or sets the estimated total latency
        /// </summary>
        public double EstimatedTotalLatency { get; set; }

        /// <summary>
        /// Gets or sets the optimization level applied
        /// </summary>
        public OptimizationLevel OptimizationLevel { get; set; }
    }

    /// <summary>
    /// Execution stage in a batch plan
    /// </summary>
    public class ExecutionStage
    {
        /// <summary>
        /// Gets or sets the stage number
        /// </summary>
        public int StageNumber { get; set; }

        /// <summary>
        /// Gets or sets the batches to execute in this stage
        /// </summary>
        public IList<OptimizedQueryBatch> Batches { get; set; } = new List<OptimizedQueryBatch>();

        /// <summary>
        /// Gets or sets the parallelism level for this stage
        /// </summary>
        public int ParallelismLevel { get; set; }

        /// <summary>
        /// Gets or sets the estimated duration for this stage
        /// </summary>
        public double EstimatedDuration { get; set; }
    }



    /// <summary>
    /// Results from query batch optimization
    /// </summary>
    public class QueryBatchOptimization
    {
        /// <summary>
        /// Gets or sets the original query count
        /// </summary>
        public int OriginalQueryCount { get; set; }

        /// <summary>
        /// Gets or sets the optimization start time
        /// </summary>
        public DateTime OptimizationStartTime { get; set; }

        /// <summary>
        /// Gets or sets the optimized batches
        /// </summary>
        public IList<OptimizedQueryBatch> OptimizedBatches { get; set; } = new List<OptimizedQueryBatch>();

        /// <summary>
        /// Gets or sets the execution plan
        /// </summary>
        public BatchExecutionPlan ExecutionPlan { get; set; } = new();

        /// <summary>
        /// Gets or sets the expected latency reduction percentage
        /// </summary>
        public double ExpectedLatencyReduction { get; set; }

        /// <summary>
        /// Gets or sets the expected throughput gain percentage
        /// </summary>
        public double ExpectedThroughputGain { get; set; }
    }

    /// <summary>
    /// Current storage performance data
    /// </summary>
    public class StoragePerformanceData
    {
        /// <summary>
        /// Gets or sets the average query latency in milliseconds
        /// </summary>
        public double AverageLatency { get; set; }

        /// <summary>
        /// Gets or sets the 95th percentile latency
        /// </summary>
        public double P95Latency { get; set; }

        /// <summary>
        /// Gets or sets the throughput in operations per second
        /// </summary>
        public double Throughput { get; set; }

        /// <summary>
        /// Gets or sets the error rate percentage
        /// </summary>
        public double ErrorRate { get; set; }

        /// <summary>
        /// Gets or sets the percentage of traffic hitting hot partitions
        /// </summary>
        public double HotPartitionPercentage { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of this measurement
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Storage index metrics tracking for an entity type
    /// </summary>
    public class StorageIndexMetrics
    {
        /// <summary>
        /// Gets or sets the entity type
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the historical performance data
        /// </summary>
        public IList<StoragePerformanceData> HistoricalData { get; set; } = new List<StoragePerformanceData>();

        /// <summary>
        /// Gets or sets the average latency over time
        /// </summary>
        public double AverageLatency { get; set; }

        /// <summary>
        /// Gets or sets the peak throughput achieved
        /// </summary>
        public double PeakThroughput { get; set; }

        /// <summary>
        /// Updates metrics with new performance data
        /// </summary>
        /// <param name="performanceData">New performance data to add</param>
        public void UpdateMetrics(StoragePerformanceData performanceData)
        {
            HistoricalData.Add(performanceData);
            
            // Keep only last 100 measurements for memory efficiency
            if (HistoricalData.Count > 100)
            {
                HistoricalData.RemoveAt(0);
            }

            // Update aggregated metrics
            AverageLatency = HistoricalData.Average(d => d.AverageLatency);
            PeakThroughput = HistoricalData.Max(d => d.Throughput);
        }
    }

    /// <summary>
    /// Performance optimization recommendations and results
    /// </summary>
    public class StoragePerformanceOptimization
    {
        /// <summary>
        /// Gets or sets the entity type being optimized
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the analysis timestamp
        /// </summary>
        public DateTime AnalysisTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the current performance baseline
        /// </summary>
        public StoragePerformanceData CurrentPerformance { get; set; } = new();

        /// <summary>
        /// Gets or sets the optimization recommendations
        /// </summary>
        public IList<OptimizationRecommendation> Recommendations { get; set; } = new List<OptimizationRecommendation>();

        /// <summary>
        /// Gets or sets the expected improvements
        /// </summary>
        public PerformanceImprovementEstimate ExpectedImprovements { get; set; } = new();

        /// <summary>
        /// Gets or sets immediate actions required
        /// </summary>
        public IList<ImmediateAction> ImmediateActions { get; set; } = new List<ImmediateAction>();
    }

    /// <summary>
    /// Types of performance issues
    /// </summary>
    public enum PerformanceIssueType
    {
        /// <summary>
        /// High query latency
        /// </summary>
        HighLatency,

        /// <summary>
        /// Low throughput
        /// </summary>
        LowThroughput,

        /// <summary>
        /// Hot partition detected
        /// </summary>
        HotPartition,

        /// <summary>
        /// High error rate
        /// </summary>
        HighErrorRate,

        /// <summary>
        /// Inefficient queries
        /// </summary>
        InefficientQueries
    }

    /// <summary>
    /// Severity levels for performance issues
    /// </summary>
    public enum PerformanceIssueSeverity
    {
        /// <summary>
        /// Low severity issue
        /// </summary>
        Low,

        /// <summary>
        /// Medium severity issue
        /// </summary>
        Medium,

        /// <summary>
        /// High severity issue
        /// </summary>
        High,

        /// <summary>
        /// Critical severity issue
        /// </summary>
        Critical
    }

    /// <summary>
    /// Represents a performance issue
    /// </summary>
    public class PerformanceIssue
    {
        /// <summary>
        /// Gets or sets the type of performance issue
        /// </summary>
        public PerformanceIssueType Type { get; set; }

        /// <summary>
        /// Gets or sets the severity level
        /// </summary>
        public PerformanceIssueSeverity Severity { get; set; }

        /// <summary>
        /// Gets or sets the issue description
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Gets or sets the recommended action
        /// </summary>
        public string RecommendedAction { get; set; } = "";

        /// <summary>
        /// Gets or sets the impact assessment
        /// </summary>
        public string Impact { get; set; } = "";
    }

    /// <summary>
    /// Types of optimization recommendations
    /// </summary>
    public enum OptimizationType
    {
        /// <summary>
        /// Query pattern optimization
        /// </summary>
        QueryOptimization,

        /// <summary>
        /// Partition key optimization
        /// </summary>
        PartitionOptimization,

        /// <summary>
        /// Index optimization
        /// </summary>
        IndexOptimization,

        /// <summary>
        /// Caching optimization
        /// </summary>
        CachingOptimization,

        /// <summary>
        /// Batch processing optimization
        /// </summary>
        BatchOptimization
    }

    /// <summary>
    /// Priority levels for recommendations
    /// </summary>
    public enum RecommendationPriority
    {
        /// <summary>
        /// Low priority recommendation
        /// </summary>
        Low,

        /// <summary>
        /// Medium priority recommendation
        /// </summary>
        Medium,

        /// <summary>
        /// High priority recommendation
        /// </summary>
        High,

        /// <summary>
        /// Critical priority recommendation
        /// </summary>
        Critical
    }

    /// <summary>
    /// Implementation effort levels
    /// </summary>
    public enum ImplementationEffort
    {
        /// <summary>
        /// Low effort required
        /// </summary>
        Low,

        /// <summary>
        /// Medium effort required
        /// </summary>
        Medium,

        /// <summary>
        /// High effort required
        /// </summary>
        High,

        /// <summary>
        /// Very high effort required
        /// </summary>
        VeryHigh
    }

    /// <summary>
    /// Optimization recommendation
    /// </summary>
    public class OptimizationRecommendation
    {
        /// <summary>
        /// Gets or sets the type of optimization
        /// </summary>
        public OptimizationType Type { get; set; }

        /// <summary>
        /// Gets or sets the priority level
        /// </summary>
        public RecommendationPriority Priority { get; set; }

        /// <summary>
        /// Gets or sets the recommendation description
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Gets or sets the expected improvement percentage
        /// </summary>
        public double ExpectedImprovement { get; set; }

        /// <summary>
        /// Gets or sets the implementation effort required
        /// </summary>
        public ImplementationEffort ImplementationEffort { get; set; }

        /// <summary>
        /// Gets or sets the estimated implementation time
        /// </summary>
        public TimeSpan EstimatedImplementationTime { get; set; }
    }

    /// <summary>
    /// Estimate of performance improvements
    /// </summary>
    public class PerformanceImprovementEstimate
    {
        /// <summary>
        /// Gets or sets the overall improvement percentage
        /// </summary>
        public double OverallImprovement { get; set; }

        /// <summary>
        /// Gets or sets the latency improvement percentage
        /// </summary>
        public double LatencyImprovement { get; set; }

        /// <summary>
        /// Gets or sets the throughput improvement percentage
        /// </summary>
        public double ThroughputImprovement { get; set; }

        /// <summary>
        /// Gets or sets the confidence level (0.0 to 1.0)
        /// </summary>
        public double ConfidenceLevel { get; set; } = 0.8;
    }

    /// <summary>
    /// Priority levels for immediate actions
    /// </summary>
    public enum ActionPriority
    {
        /// <summary>
        /// Low priority action
        /// </summary>
        Low,

        /// <summary>
        /// Medium priority action
        /// </summary>
        Medium,

        /// <summary>
        /// High priority action
        /// </summary>
        High,

        /// <summary>
        /// Urgent priority action
        /// </summary>
        Urgent
    }

    /// <summary>
    /// Immediate action required
    /// </summary>
    public class ImmediateAction
    {
        /// <summary>
        /// Gets or sets the action description
        /// </summary>
        public string Action { get; set; } = "";

        /// <summary>
        /// Gets or sets the action priority
        /// </summary>
        public ActionPriority Priority { get; set; }

        /// <summary>
        /// Gets or sets the estimated impact percentage
        /// </summary>
        public double EstimatedImpact { get; set; }

        /// <summary>
        /// Gets or sets the deadline for this action
        /// </summary>
        public DateTime? Deadline { get; set; }
    }

    // Complex query optimization models

    /// <summary>
    /// Represents a complex query pattern requiring optimization
    /// </summary>
    public class ComplexQueryPattern
    {
        /// <summary>
        /// Gets or sets the query identifier
        /// </summary>
        public string QueryId { get; set; } = "";

        /// <summary>
        /// Gets or sets the complexity score (1-10)
        /// </summary>
        public int ComplexityScore { get; set; }

        /// <summary>
        /// Gets or sets whether a secondary index is required
        /// </summary>
        public bool RequiresSecondaryIndex { get; set; }

        /// <summary>
        /// Gets or sets whether a materialized view is required
        /// </summary>
        public bool RequiresMaterializedView { get; set; }

        /// <summary>
        /// Gets or sets the field used for indexing
        /// </summary>
        public string IndexField { get; set; } = "";

        /// <summary>
        /// Gets or sets the aggregation key for materialized views
        /// </summary>
        public string AggregationKey { get; set; } = "";

        /// <summary>
        /// Gets or sets the aggregation fields
        /// </summary>
        public IList<string> AggregationFields { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets whether range queries are required
        /// </summary>
        public bool RequiresRangeQuery { get; set; }

        /// <summary>
        /// Gets or sets whether equality queries are required
        /// </summary>
        public bool RequiresEqualityQuery { get; set; }

        /// <summary>
        /// Gets or sets the frequency of this query per hour
        /// </summary>
        public int FrequencyPerHour { get; set; }
    }

    /// <summary>
    /// Analysis of query complexity
    /// </summary>
    public class QueryComplexityAnalysis
    {
        /// <summary>
        /// Gets or sets the total number of queries
        /// </summary>
        public int TotalQueries { get; set; }

        /// <summary>
        /// Gets or sets the number of complex queries
        /// </summary>
        public int ComplexQueries { get; set; }

        /// <summary>
        /// Gets or sets the average complexity score
        /// </summary>
        public double AverageComplexity { get; set; }

        /// <summary>
        /// Gets or sets whether secondary indexes are required
        /// </summary>
        public bool RequiresSecondaryIndexes { get; set; }

        /// <summary>
        /// Gets or sets whether materialized views are required
        /// </summary>
        public bool RequiresMaterializedViews { get; set; }
    }

    /// <summary>
    /// Strategy for secondary indexes
    /// </summary>
    public class SecondaryIndexStrategy
    {
        /// <summary>
        /// Gets or sets the entity type
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the field to index
        /// </summary>
        public string IndexField { get; set; } = "";

        /// <summary>
        /// Gets or sets the type of index
        /// </summary>
        public IndexType IndexType { get; set; }

        /// <summary>
        /// Gets or sets the expected improvement percentage
        /// </summary>
        public double ExpectedImprovement { get; set; }

        /// <summary>
        /// Gets or sets the maintenance cost percentage
        /// </summary>
        public double MaintenanceCost { get; set; }
    }

    /// <summary>
    /// Types of indexes
    /// </summary>
    public enum IndexType
    {
        /// <summary>
        /// Hash index for equality queries
        /// </summary>
        Hash,

        /// <summary>
        /// Range index for range queries
        /// </summary>
        Range,

        /// <summary>
        /// Composite index for multiple fields
        /// </summary>
        Composite,

        /// <summary>
        /// Full-text index for text search
        /// </summary>
        FullText
    }

    /// <summary>
    /// Strategy for materialized views
    /// </summary>
    public class MaterializedViewStrategy
    {
        /// <summary>
        /// Gets or sets the view name
        /// </summary>
        public string ViewName { get; set; } = "";

        /// <summary>
        /// Gets or sets the aggregation fields
        /// </summary>
        public IList<string> AggregationFields { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the refresh strategy
        /// </summary>
        public RefreshStrategy RefreshStrategy { get; set; }

        /// <summary>
        /// Gets or sets the expected improvement percentage
        /// </summary>
        public double ExpectedImprovement { get; set; }
    }

    /// <summary>
    /// Refresh strategies for materialized views
    /// </summary>
    public enum RefreshStrategy
    {
        /// <summary>
        /// Real-time refresh on data changes
        /// </summary>
        RealTime,

        /// <summary>
        /// Scheduled periodic refresh
        /// </summary>
        Scheduled,

        /// <summary>
        /// Manual refresh on demand
        /// </summary>
        Manual,

        /// <summary>
        /// Incremental refresh of changes only
        /// </summary>
        Incremental
    }

    /// <summary>
    /// Strategy for caching
    /// </summary>
    public class CachingStrategy
    {
        /// <summary>
        /// Gets or sets the type of cache
        /// </summary>
        public CacheType CacheType { get; set; }

        /// <summary>
        /// Gets or sets the time-to-live for cache entries
        /// </summary>
        public TimeSpan TTL { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of entries
        /// </summary>
        public int MaxEntries { get; set; }

        /// <summary>
        /// Gets or sets the eviction policy
        /// </summary>
        public EvictionPolicy EvictionPolicy { get; set; }
    }

    /// <summary>
    /// Types of caches
    /// </summary>
    public enum CacheType
    {
        /// <summary>
        /// In-memory cache
        /// </summary>
        MemoryCache,

        /// <summary>
        /// Distributed cache (Redis)
        /// </summary>
        DistributedCache,

        /// <summary>
        /// Local file cache
        /// </summary>
        FileCache,

        /// <summary>
        /// Hybrid cache strategy
        /// </summary>
        Hybrid
    }

    /// <summary>
    /// Cache eviction policies
    /// </summary>
    public enum EvictionPolicy
    {
        /// <summary>
        /// Least Recently Used
        /// </summary>
        LRU,

        /// <summary>
        /// Least Frequently Used
        /// </summary>
        LFU,

        /// <summary>
        /// Time-To-Live based
        /// </summary>
        TTL,

        /// <summary>
        /// First In, First Out
        /// </summary>
        FIFO
    }

    /// <summary>
    /// Comprehensive index optimization strategy
    /// </summary>
    public class IndexOptimizationStrategy
    {
        /// <summary>
        /// Gets or sets the entity type
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the strategy creation timestamp
        /// </summary>
        public DateTime CreationTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the query patterns analyzed
        /// </summary>
        public IList<ComplexQueryPattern> QueryPatterns { get; set; } = new List<ComplexQueryPattern>();

        /// <summary>
        /// Gets or sets the complexity analysis results
        /// </summary>
        public QueryComplexityAnalysis ComplexityAnalysis { get; set; } = new();

        /// <summary>
        /// Gets or sets the secondary index strategies
        /// </summary>
        public IList<SecondaryIndexStrategy> SecondaryIndexes { get; set; } = new List<SecondaryIndexStrategy>();

        /// <summary>
        /// Gets or sets the materialized view strategies
        /// </summary>
        public IList<MaterializedViewStrategy> MaterializedViews { get; set; } = new List<MaterializedViewStrategy>();

        /// <summary>
        /// Gets or sets the caching strategies
        /// </summary>
        public IList<CachingStrategy> CachingStrategies { get; set; } = new List<CachingStrategy>();

        /// <summary>
        /// Gets or sets the expected performance improvements
        /// </summary>
        public IndexPerformanceImprovements ExpectedImprovements { get; set; } = new();
    }

    /// <summary>
    /// Performance improvements from index optimization
    /// </summary>
    public class IndexPerformanceImprovements
    {
        /// <summary>
        /// Gets or sets the overall improvement percentage
        /// </summary>
        public double OverallImprovement { get; set; }

        /// <summary>
        /// Gets or sets the query latency reduction percentage
        /// </summary>
        public double QueryLatencyReduction { get; set; }

        /// <summary>
        /// Gets or sets the throughput increase percentage
        /// </summary>
        public double ThroughputIncrease { get; set; }

        /// <summary>
        /// Gets or sets the maintenance overhead percentage
        /// </summary>
        public double MaintenanceOverhead { get; set; }
    }

    // Storage operation optimization models

    /// <summary>
    /// Represents a storage operation for optimization
    /// </summary>
    public class StorageOperation
    {
        /// <summary>
        /// Gets or sets the operation identifier
        /// </summary>
        public string OperationId { get; set; } = "";

        /// <summary>
        /// Gets or sets the partition key
        /// </summary>
        public string PartitionKey { get; set; } = "";

        /// <summary>
        /// Gets or sets the row key
        /// </summary>
        public string RowKey { get; set; } = "";

        /// <summary>
        /// Gets or sets the operation type
        /// </summary>
        public OperationType OperationType { get; set; }

        /// <summary>
        /// Gets or sets the entity data
        /// </summary>
        public object EntityData { get; set; } = new();

        /// <summary>
        /// Gets or sets the estimated latency
        /// </summary>
        public double EstimatedLatencyMs { get; set; }

        /// <summary>
        /// Gets or sets the operation priority
        /// </summary>
        public int Priority { get; set; }
    }

    /// <summary>
    /// Options for storage optimization
    /// </summary>
    public class StorageOptimizationOptions
    {
        /// <summary>
        /// Gets or sets the batch threshold for operations
        /// </summary>
        public int BatchThreshold { get; set; } = 50;

        /// <summary>
        /// Gets or sets the maximum batch size
        /// </summary>
        public int MaxBatchSize { get; set; } = 100;

        /// <summary>
        /// Gets or sets the maximum concurrency level
        /// </summary>
        public int MaxConcurrency { get; set; } = 5;

        /// <summary>
        /// Gets or sets whether to enable compression
        /// </summary>
        public bool EnableCompression { get; set; }

        /// <summary>
        /// Gets or sets the optimization strategy
        /// </summary>
        public OptimizationStrategy OptimizationStrategy { get; set; }
    }

    /// <summary>
    /// Optimization strategies for storage operations
    /// </summary>
    public enum OptimizationStrategy
    {
        /// <summary>
        /// Optimize for latency
        /// </summary>
        LatencyOptimized,

        /// <summary>
        /// Optimize for throughput
        /// </summary>
        ThroughputOptimized,

        /// <summary>
        /// Optimize for batch processing
        /// </summary>
        BatchOptimized,

        /// <summary>
        /// Optimize for write operations
        /// </summary>
        WriteOptimized,

        /// <summary>
        /// Balanced optimization
        /// </summary>
        Balanced
    }

    /// <summary>
    /// Group of operations for optimization
    /// </summary>
    public class OperationGroup
    {
        /// <summary>
        /// Gets or sets the partition key for this group
        /// </summary>
        public string PartitionKey { get; set; } = "";

        /// <summary>
        /// Gets or sets the operation type for this group
        /// </summary>
        public OperationType OperationType { get; set; }

        /// <summary>
        /// Gets or sets the operations in this group
        /// </summary>
        public IList<StorageOperation> Operations { get; set; } = new List<StorageOperation>();

        /// <summary>
        /// Gets or sets the optimization strategy for this group
        /// </summary>
        public OptimizationStrategy OptimizationStrategy { get; set; }
    }

    /// <summary>
    /// Results from executing an operation group
    /// </summary>
    public class OperationGroupResult
    {
        /// <summary>
        /// Gets or sets the partition key
        /// </summary>
        public string PartitionKey { get; set; } = "";

        /// <summary>
        /// Gets or sets the operation type
        /// </summary>
        public OperationType OperationType { get; set; }

        /// <summary>
        /// Gets or sets the number of operations executed
        /// </summary>
        public int OperationsExecuted { get; set; }

        /// <summary>
        /// Gets or sets the execution time
        /// </summary>
        public TimeSpan ExecutionTime { get; set; }

        /// <summary>
        /// Gets or sets the success rate
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the average latency
        /// </summary>
        public double AverageLatency { get; set; }
    }

    /// <summary>
    /// Results from optimized storage operations execution
    /// </summary>
    public class OptimizedExecutionResults
    {
        /// <summary>
        /// Gets or sets the total number of operations
        /// </summary>
        public int TotalOperations { get; set; }

        /// <summary>
        /// Gets or sets the execution start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the execution end time
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the total execution duration
        /// </summary>
        public TimeSpan TotalDuration { get; set; }

        /// <summary>
        /// Gets or sets the optimization options used
        /// </summary>
        public StorageOptimizationOptions OptimizationOptions { get; set; } = new();

        /// <summary>
        /// Gets or sets the results for each operation group
        /// </summary>
        public IList<OperationGroupResult> GroupResults { get; set; } = new List<OperationGroupResult>();

        /// <summary>
        /// Gets or sets the average latency across all operations
        /// </summary>
        public double AverageLatency { get; set; }

        /// <summary>
        /// Gets or sets the overall success rate
        /// </summary>
        public double OverallSuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the throughput in operations per second
        /// </summary>
        public double Throughput { get; set; }
    }

    /// <summary>
    /// Query optimization strategy for entity types
    /// </summary>
    public class QueryOptimizationStrategy
    {
        /// <summary>
        /// Gets or sets the preferred partition pattern
        /// </summary>
        public PartitionPattern PreferredPartitionPattern { get; set; }

        /// <summary>
        /// Gets or sets the row key strategy
        /// </summary>
        public RowKeyStrategy RowKeyStrategy { get; set; } = new();

        /// <summary>
        /// Gets or sets the expected latency reduction percentage
        /// </summary>
        public double ExpectedLatencyReduction { get; set; }

        /// <summary>
        /// Gets or sets the optimal batch size
        /// </summary>
        public int OptimalBatchSize { get; set; }
    }
} 