using System;
using System.Collections.Generic;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Supporting data models for IntelligentBulkProcessor
    /// Phase 5: Task 5.2.1 - Intelligent Bulk Processing
    /// </summary>

    /// <summary>
    /// Represents a group of entities for bulk processing
    /// </summary>
    /// <typeparam name="T">Type of entities in the group</typeparam>
    public class EntityGroup<T>
    {
        /// <summary>
        /// Gets or sets the entity type identifier
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the collection of entities
        /// </summary>
        public IEnumerable<T> Entities { get; set; } = new List<T>();

        /// <summary>
        /// Gets or sets the estimated size per entity in bytes
        /// </summary>
        public int EstimatedEntitySize { get; set; }

        /// <summary>
        /// Gets or sets the processing priority (1=highest, 3=lowest)
        /// </summary>
        public int Priority { get; set; } = 2;

        /// <summary>
        /// Gets or sets the entity types this group depends on
        /// </summary>
        public IList<string>? Dependencies { get; set; }
    }

    /// <summary>
    /// Represents an optimized entity group with calculated processing parameters
    /// </summary>
    /// <typeparam name="T">Type of entities in the group</typeparam>
    public class OptimizedEntityGroup<T> : EntityGroup<T>
    {
        /// <summary>
        /// Gets or sets the entity complexity level
        /// </summary>
        public EntityComplexity Complexity { get; set; }

        /// <summary>
        /// Gets or sets the optimal batch size for this group
        /// </summary>
        public int OptimalBatchSize { get; set; }

        /// <summary>
        /// Gets or sets the processing weight for prioritization
        /// </summary>
        public double ProcessingWeight { get; set; }
    }

    /// <summary>
    /// Represents characteristics of entities for processing optimization
    /// </summary>
    public class EntityCharacteristics
    {
        /// <summary>
        /// Gets or sets the entity type
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the total count of entities
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Gets or sets the complexity level
        /// </summary>
        public EntityComplexity Complexity { get; set; }

        /// <summary>
        /// Gets or sets the estimated size per entity in bytes
        /// </summary>
        public int EstimatedSizePerEntity { get; set; }

        /// <summary>
        /// Gets or sets whether entities have dependencies
        /// </summary>
        public bool HasDependencies { get; set; }

        /// <summary>
        /// Gets or sets whether this is time-series data
        /// </summary>
        public bool IsTimeSeriesData { get; set; }

        /// <summary>
        /// Gets or sets whether ordering is required during processing
        /// </summary>
        public bool RequiresOrdering { get; set; }
    }

    /// <summary>
    /// Configuration for batch processing optimization
    /// </summary>
    public class BatchConfiguration
    {
        /// <summary>
        /// Gets or sets the optimal batch size
        /// </summary>
        public int OptimalBatchSize { get; set; }

        /// <summary>
        /// Gets or sets the optimal concurrency level
        /// </summary>
        public int OptimalConcurrency { get; set; }

        /// <summary>
        /// Gets or sets whether to use adaptive sizing
        /// </summary>
        public bool UseAdaptiveSizing { get; set; }

        /// <summary>
        /// Gets or sets whether to preserve ordering
        /// </summary>
        public bool PreserveOrdering { get; set; }

        /// <summary>
        /// Gets or sets whether to enable dependency tracking
        /// </summary>
        public bool EnableDependencyTracking { get; set; }

        /// <summary>
        /// Gets or sets whether to use memory optimized mode
        /// </summary>
        public bool MemoryOptimizedMode { get; set; }
    }

    /// <summary>
    /// Represents a smart batch with optimization parameters
    /// </summary>
    /// <typeparam name="T">Type of entities in the batch</typeparam>
    public class SmartBatch<T>
    {
        /// <summary>
        /// Gets or sets the batch identifier
        /// </summary>
        public int BatchId { get; set; }

        /// <summary>
        /// Gets or sets the entities in this batch
        /// </summary>
        public IList<T> Entities { get; set; } = new List<T>();

        /// <summary>
        /// Gets or sets the batch size
        /// </summary>
        public int BatchSize { get; set; }

        /// <summary>
        /// Gets or sets the estimated processing time
        /// </summary>
        public TimeSpan EstimatedProcessingTime { get; set; }

        /// <summary>
        /// Gets or sets the batch priority
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Gets or sets the batch configuration
        /// </summary>
        public BatchConfiguration Configuration { get; set; } = new();
    }

    /// <summary>
    /// Represents a processing plan with staged execution
    /// </summary>
    /// <typeparam name="T">Type of entities being processed</typeparam>
    public class ProcessingPlan<T>
    {
        /// <summary>
        /// Gets or sets the execution stages
        /// </summary>
        public IList<ProcessingStage<T>> ExecutionStages { get; set; } = new List<ProcessingStage<T>>();

        /// <summary>
        /// Gets or sets the total estimated duration
        /// </summary>
        public double TotalEstimatedDuration { get; set; }

        /// <summary>
        /// Gets or sets the peak memory requirement in bytes
        /// </summary>
        public long PeakMemoryRequirement { get; set; }
    }

    /// <summary>
    /// Represents a processing stage in the execution plan
    /// </summary>
    /// <typeparam name="T">Type of entities being processed</typeparam>
    public class ProcessingStage<T>
    {
        /// <summary>
        /// Gets or sets the stage number
        /// </summary>
        public int StageNumber { get; set; }

        /// <summary>
        /// Gets or sets the parallel groups to process in this stage
        /// </summary>
        public IList<IEnumerable<OptimizedEntityGroup<T>>> ParallelGroups { get; set; } = new List<IEnumerable<OptimizedEntityGroup<T>>>();

        /// <summary>
        /// Gets or sets the estimated duration for this stage
        /// </summary>
        public double EstimatedDuration { get; set; }

        /// <summary>
        /// Gets or sets the memory requirement for this stage
        /// </summary>
        public long MemoryRequirement { get; set; }
    }

    /// <summary>
    /// Results from processing a single entity group
    /// </summary>
    public class GroupProcessingResult
    {
        /// <summary>
        /// Gets or sets the entity type
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the total entities to process
        /// </summary>
        public int TotalEntities { get; set; }

        /// <summary>
        /// Gets or sets the number of processed entities
        /// </summary>
        public long ProcessedEntities { get; set; }

        /// <summary>
        /// Gets or sets the number of failed entities
        /// </summary>
        public long FailedEntities { get; set; }

        /// <summary>
        /// Gets or sets the number of processed batches
        /// </summary>
        public int ProcessedBatches { get; set; }

        /// <summary>
        /// Gets or sets the number of successful batches
        /// </summary>
        public int SuccessfulBatches { get; set; }

        /// <summary>
        /// Gets or sets the efficiency gain percentage
        /// </summary>
        public double EfficiencyGain { get; set; }

        /// <summary>
        /// Gets or sets the processing start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the processing end time
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the total processing duration
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Gets or sets the stage number this group was processed in
        /// </summary>
        public int StageNumber { get; set; }

        /// <summary>
        /// Gets or sets the stage start time
        /// </summary>
        public DateTime StageStartTime { get; set; }

        /// <summary>
        /// Gets or sets the stage end time
        /// </summary>
        public DateTime StageEndTime { get; set; }

        /// <summary>
        /// Gets or sets whether an error occurred
        /// </summary>
        public bool HasError { get; set; }

        /// <summary>
        /// Gets or sets the error message if any
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Results from intelligent bulk processing
    /// </summary>
    public class IntelligentBulkResults
    {
        /// <summary>
        /// Gets or sets the processing start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the processing end time
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the total processing duration
        /// </summary>
        public TimeSpan TotalDuration { get; set; }

        /// <summary>
        /// Gets or sets the processing strategies used
        /// </summary>
        public IList<string> ProcessingStrategies { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the results for each group
        /// </summary>
        public IList<GroupProcessingResult> GroupResults { get; set; } = new List<GroupProcessingResult>();

        /// <summary>
        /// Gets or sets the total entities processed
        /// </summary>
        public long TotalEntitiesProcessed { get; set; }

        /// <summary>
        /// Gets or sets the total batches processed
        /// </summary>
        public int TotalBatchesProcessed { get; set; }

        /// <summary>
        /// Gets or sets the overall efficiency gain percentage
        /// </summary>
        public double OverallEfficiencyGain { get; set; }

        /// <summary>
        /// Gets or sets the overall success rate percentage
        /// </summary>
        public double OverallSuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the average processing time per entity in milliseconds
        /// </summary>
        public double AverageProcessingTimePerEntity { get; set; }
    }

    /// <summary>
    /// Results from smart batching processing
    /// </summary>
    public class SmartBatchingResults
    {
        /// <summary>
        /// Gets or sets the entity type
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the total number of entities
        /// </summary>
        public int TotalEntities { get; set; }

        /// <summary>
        /// Gets or sets the number of processed entities
        /// </summary>
        public long ProcessedEntities { get; set; }

        /// <summary>
        /// Gets or sets the number of failed entities
        /// </summary>
        public long FailedEntities { get; set; }

        /// <summary>
        /// Gets or sets the number of processed batches
        /// </summary>
        public int ProcessedBatches { get; set; }

        /// <summary>
        /// Gets or sets the number of successful batches
        /// </summary>
        public int SuccessfulBatches { get; set; }

        /// <summary>
        /// Gets or sets the efficiency gain percentage
        /// </summary>
        public double EfficiencyGain { get; set; }

        /// <summary>
        /// Gets or sets the success rate percentage
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the throughput in entities per second
        /// </summary>
        public double ThroughputPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the processing start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the processing end time
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the total processing duration
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Gets or sets the total processing time across all batches
        /// </summary>
        public TimeSpan TotalProcessingTime { get; set; }

        /// <summary>
        /// Gets or sets the average processing time per batch in milliseconds
        /// </summary>
        public double AverageProcessingTimePerBatch { get; set; }
    }

    /// <summary>
    /// Represents the result of processing a batch or group
    /// </summary>
    public class ProcessingResult
    {
        /// <summary>
        /// Gets or sets the number of processed items
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// Gets or sets the number of failed items
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// Gets or sets whether the processing was successful
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Gets or sets the processing time
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>
        /// Gets or sets the throughput in items per second
        /// </summary>
        public double Throughput { get; set; }

        /// <summary>
        /// Gets or sets the error rate percentage
        /// </summary>
        public double ErrorRate { get; set; }

        /// <summary>
        /// Gets or sets the memory used in MB
        /// </summary>
        public long MemoryUsedMB { get; set; }

        /// <summary>
        /// Gets or sets the batch identifier
        /// </summary>
        public int BatchId { get; set; }

        /// <summary>
        /// Gets or sets any error message
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

} 