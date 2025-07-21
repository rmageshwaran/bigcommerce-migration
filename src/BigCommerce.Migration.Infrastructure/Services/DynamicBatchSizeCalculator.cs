using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Phase 5: Bulk Operations and Storage Optimization
    /// Dynamic Batch Size Calculator - Optimizes batch sizes based on entity complexity
    /// 
    /// Goals:
    /// - Calculate optimal batch sizes for different entity types and complexities
    /// - Achieve 90% storage operation efficiency improvement
    /// - Provide adaptive batch sizing based on performance feedback
    /// - Maintain memory-safe operations within Azure Functions limits
    /// </summary>
    public class DynamicBatchSizeCalculator
    {
        private readonly ILogger<DynamicBatchSizeCalculator> _logger;
        private readonly ConcurrentDictionary<string, BatchSizeMetrics> _performanceHistory;
        private const int MaxBatchSize = 200; // Azure Functions memory safety limit
        private const int MinBatchSize = 5;   // Minimum efficiency threshold

        /// <summary>
        /// Initializes a new instance of the DynamicBatchSizeCalculator
        /// </summary>
        /// <param name="logger">Logger for diagnostic information</param>
        public DynamicBatchSizeCalculator(ILogger<DynamicBatchSizeCalculator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _performanceHistory = new ConcurrentDictionary<string, BatchSizeMetrics>();
        }

        /// <summary>
        /// Calculates the optimal batch size for an entity type based on its complexity and base size
        /// </summary>
        /// <param name="entityType">Type of entity (Products, Categories, etc.)</param>
        /// <param name="complexity">Entity complexity level</param>
        /// <param name="baseEntitySize">Base size of individual entity in bytes</param>
        /// <returns>Optimal batch size for maximum efficiency</returns>
        public async Task<int> CalculateOptimalBatchSizeAsync(
            string entityType, 
            EntityComplexity complexity, 
            int baseEntitySize)
        {
            try
            {
                _logger.LogDebug("Calculating optimal batch size for {EntityType} with complexity {Complexity}", 
                    entityType, complexity);

                // Calculate base batch size using entity complexity and size
                var baseBatchSize = CalculateBaseBatchSize(complexity, baseEntitySize);
                
                // Apply performance feedback if available
                var adjustedBatchSize = await ApplyPerformanceFeedbackAsync(entityType, baseBatchSize).ConfigureAwait(false);
                
                // Ensure batch size is within safe limits
                var optimalBatchSize = Math.Max(MinBatchSize, Math.Min(MaxBatchSize, adjustedBatchSize));

                _logger.LogInformation("Calculated optimal batch size {BatchSize} for {EntityType}", 
                    optimalBatchSize, entityType);

                return optimalBatchSize;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating optimal batch size for {EntityType}", entityType);
                return GetFallbackBatchSize(complexity);
            }
        }

        /// <summary>
        /// Provides adaptive batch size recommendations based on performance feedback
        /// </summary>
        /// <param name="entityType">Type of entity being processed</param>
        /// <param name="currentBatchSize">Current batch size being used</param>
        /// <param name="performanceResult">Performance feedback from processing</param>
        /// <returns>Adaptive batch size recommendation</returns>
        public async Task<AdaptiveBatchSizeRecommendation> GetAdaptiveBatchSizeRecommendationAsync(
            string entityType,
            int currentBatchSize,
            BatchProcessingResult performanceResult)
        {
            try
            {
                await Task.CompletedTask.ConfigureAwait(false); // Satisfy async requirement
                _logger.LogDebug("Getting adaptive batch size recommendation for {EntityType}", entityType);

                // Update performance history
                UpdatePerformanceHistory(entityType, currentBatchSize, performanceResult);

                // Analyze performance trends
                var recommendation = AnalyzePerformanceAndRecommend(entityType, currentBatchSize, performanceResult);

                _logger.LogInformation("Recommended batch size {BatchSize} for {EntityType} (previous: {PreviousBatchSize})",
                    recommendation.RecommendedBatchSize, entityType, currentBatchSize);

                return recommendation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating adaptive batch size recommendation for {EntityType}", entityType);
                return new AdaptiveBatchSizeRecommendation
                {
                    RecommendedBatchSize = currentBatchSize,
                    Confidence = 0.5,
                    Reason = "Error occurred, maintaining current batch size"
                };
            }
        }

        /// <summary>
        /// Calculates base batch size based on entity complexity and size
        /// </summary>
        private int CalculateBaseBatchSize(EntityComplexity complexity, int baseEntitySize)
        {
            // Memory-safe batch sizing algorithm
            const long maxBatchMemory = 20 * 1024 * 1024; // 20MB max per batch (Azure Functions safe)

            // Calculate maximum entities per batch based on memory constraints
            var maxEntitiesPerBatch = (int)(maxBatchMemory / Math.Max(baseEntitySize, 1024)); // Min 1KB per entity

            // Apply complexity-based adjustments
            var complexityMultiplier = complexity switch
            {
                EntityComplexity.Low => 1.0,     // Categories, Brands - larger batches
                EntityComplexity.Medium => 0.6,  // Variants - moderate batches  
                EntityComplexity.High => 0.3,    // Products, Images - smaller batches
                _ => 0.5
            };

            var baseBatchSize = (int)(maxEntitiesPerBatch * complexityMultiplier);

            // Apply minimum efficiency thresholds
            return Math.Max(MinBatchSize, Math.Min(MaxBatchSize, baseBatchSize));
        }

        /// <summary>
        /// Applies performance feedback to adjust batch size
        /// </summary>
        private Task<int> ApplyPerformanceFeedbackAsync(string entityType, int baseBatchSize)
        {
            if (!_performanceHistory.TryGetValue(entityType, out var metrics))
            {
                return Task.FromResult(baseBatchSize); // No history available
            }

            // Analyze historical performance and adjust
            var performanceRatio = metrics.AverageThroughput / Math.Max(metrics.AverageErrorRate + 0.01, 0.01);
            
            if (performanceRatio > 1.5) // Good performance, can increase batch size
            {
                return Task.FromResult(Math.Min(MaxBatchSize, (int)(baseBatchSize * 1.2)));
            }
            else if (performanceRatio < 0.8) // Poor performance, decrease batch size
            {
                return Task.FromResult(Math.Max(MinBatchSize, (int)(baseBatchSize * 0.8)));
            }

            return Task.FromResult(baseBatchSize); // Maintain current size
        }

        /// <summary>
        /// Updates performance history with new processing results
        /// </summary>
        private void UpdatePerformanceHistory(string entityType, int batchSize, BatchProcessingResult result)
        {
            _performanceHistory.AddOrUpdate(entityType,
                new BatchSizeMetrics
                {
                    EntityType = entityType,
                    SampleCount = 1,
                    AverageBatchSize = batchSize,
                    AverageThroughput = result.Throughput,
                    AverageErrorRate = result.ErrorRate,
                    AverageMemoryUsage = result.MemoryUsage,
                    LastUpdated = DateTime.UtcNow
                },
                (key, existing) => new BatchSizeMetrics
                {
                    EntityType = entityType,
                    SampleCount = existing.SampleCount + 1,
                    AverageBatchSize = (existing.AverageBatchSize * existing.SampleCount + batchSize) / (existing.SampleCount + 1),
                    AverageThroughput = (existing.AverageThroughput * existing.SampleCount + result.Throughput) / (existing.SampleCount + 1),
                    AverageErrorRate = (existing.AverageErrorRate * existing.SampleCount + result.ErrorRate) / (existing.SampleCount + 1),
                    AverageMemoryUsage = (existing.AverageMemoryUsage * existing.SampleCount + result.MemoryUsage) / (existing.SampleCount + 1),
                    LastUpdated = DateTime.UtcNow
                });
        }

        /// <summary>
        /// Analyzes performance and generates batch size recommendation
        /// </summary>
        private AdaptiveBatchSizeRecommendation AnalyzePerformanceAndRecommend(
            string entityType, 
            int currentBatchSize, 
            BatchProcessingResult result)
        {
            var recommendation = new AdaptiveBatchSizeRecommendation
            {
                RecommendedBatchSize = currentBatchSize,
                Confidence = 0.8,
                Reason = "Maintaining current batch size"
            };

            // Performance-based adjustments
            if (result.ErrorRate > 5.0) // High error rate
            {
                recommendation.RecommendedBatchSize = Math.Max(MinBatchSize, (int)(currentBatchSize * 0.7));
                recommendation.Reason = "Reducing batch size due to high error rate";
                recommendation.Confidence = 0.9;
            }
            else if (result.Throughput > 0 && result.ErrorRate < 1.0 && result.MemoryUsage < 50 * 1024 * 1024) // Good performance
            {
                recommendation.RecommendedBatchSize = Math.Min(MaxBatchSize, (int)(currentBatchSize * 1.1));
                recommendation.Reason = "Increasing batch size due to good performance";
                recommendation.Confidence = 0.8;
            }
            else if (result.MemoryUsage > 80 * 1024 * 1024) // High memory usage
            {
                recommendation.RecommendedBatchSize = Math.Max(MinBatchSize, (int)(currentBatchSize * 0.8));
                recommendation.Reason = "Reducing batch size due to high memory usage";
                recommendation.Confidence = 0.9;
            }

            return recommendation;
        }

        /// <summary>
        /// Gets fallback batch size for error scenarios
        /// </summary>
        private static int GetFallbackBatchSize(EntityComplexity complexity)
        {
            return complexity switch
            {
                EntityComplexity.Low => 50,    // Conservative but efficient for simple entities
                EntityComplexity.Medium => 25, // Moderate for medium complexity
                EntityComplexity.High => 10,   // Small for complex entities
                _ => 20
            };
        }
    }

    /// <summary>
    /// Performance metrics for batch size optimization
    /// </summary>
    public class BatchSizeMetrics
    {
        /// <summary>
        /// Gets or sets the entity type
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Gets or sets the number of samples collected
        /// </summary>
        public int SampleCount { get; set; }

        /// <summary>
        /// Gets or sets the average batch size
        /// </summary>
        public double AverageBatchSize { get; set; }

        /// <summary>
        /// Gets or sets the average throughput
        /// </summary>
        public double AverageThroughput { get; set; }

        /// <summary>
        /// Gets or sets the average error rate
        /// </summary>
        public double AverageErrorRate { get; set; }

        /// <summary>
        /// Gets or sets the average memory usage
        /// </summary>
        public long AverageMemoryUsage { get; set; }

        /// <summary>
        /// Gets or sets the last updated timestamp
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Adaptive batch size recommendation
    /// </summary>
    public class AdaptiveBatchSizeRecommendation
    {
        /// <summary>
        /// Gets or sets the recommended batch size
        /// </summary>
        public int RecommendedBatchSize { get; set; }

        /// <summary>
        /// Gets or sets the confidence level of the recommendation
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Gets or sets the reason for the recommendation
        /// </summary>
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// Batch processing performance result
    /// </summary>
    public class BatchProcessingResult
    {
        /// <summary>
        /// Gets or sets the processing time in milliseconds
        /// </summary>
        public double ProcessingTime { get; set; }

        /// <summary>
        /// Gets or sets the memory usage in bytes
        /// </summary>
        public long MemoryUsage { get; set; }

        /// <summary>
        /// Gets or sets the error rate percentage
        /// </summary>
        public double ErrorRate { get; set; }

        /// <summary>
        /// Gets or sets the throughput in entities per second
        /// </summary>
        public double Throughput { get; set; }
    }

    /// <summary>
    /// Entity complexity levels for batch sizing
    /// </summary>
    public enum EntityComplexity
    {
        /// <summary>
        /// Low complexity entities like Categories and Brands
        /// </summary>
        Low = 0,

        /// <summary>
        /// Medium complexity entities like Variants
        /// </summary>
        Medium = 1,

        /// <summary>
        /// High complexity entities like Products and Images with nested data
        /// </summary>
        High = 2
    }
} 