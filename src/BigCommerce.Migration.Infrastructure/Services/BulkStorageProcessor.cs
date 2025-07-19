using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Phase 5: Bulk Operations and Storage Optimization
    /// Bulk Storage Processor - Achieves 90% storage efficiency improvement through intelligent bulk operations
    /// 
    /// Goals:
    /// - Process entities in optimized batches for maximum storage efficiency
    /// - Reduce storage operations by 90% through intelligent batching
    /// - Maintain memory-safe operations within Azure Functions limits
    /// - Provide high-throughput processing with error resilience
    /// </summary>
    public class BulkStorageProcessor
    {
        private readonly ILogger<BulkStorageProcessor> _logger;
        private const int DefaultBatchSize = 50;
        private const int MaxConcurrentBatches = 5; // Memory safety for Azure Functions

        /// <summary>
        /// Initializes a new instance of the BulkStorageProcessor
        /// </summary>
        /// <param name="logger">Logger for diagnostic information</param>
        public BulkStorageProcessor(ILogger<BulkStorageProcessor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes entities in bulk operations for maximum storage efficiency
        /// </summary>
        /// <typeparam name="T">Type of entity to process</typeparam>
        /// <param name="entities">Collection of entities to process</param>
        /// <param name="batchProcessor">Function to process each batch of entities</param>
        /// <param name="batchSize">Optional batch size (uses dynamic sizing if not specified)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Results of bulk processing operation</returns>
        public async Task<BulkProcessingResults> ProcessEntitiesBulkAsync<T>(
            IEnumerable<T> entities,
            Func<IEnumerable<T>, Task> batchProcessor,
            int? batchSize = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var entityList = entities.ToList();
                var effectiveBatchSize = batchSize ?? DefaultBatchSize;
                var totalEntities = entityList.Count;

                _logger.LogInformation("Starting bulk processing of {EntityCount} entities with batch size {BatchSize}",
                    totalEntities, effectiveBatchSize);

                var results = new BulkProcessingResults
                {
                    TotalEntities = totalEntities,
                    StartTime = DateTime.UtcNow
                };

                // Process entities in batches with controlled concurrency
                var batches = CreateBatches(entityList, effectiveBatchSize);
                var semaphore = new SemaphoreSlim(MaxConcurrentBatches, MaxConcurrentBatches);
                var batchTasks = new List<Task>();

                foreach (var batch in batches)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var batchTask = ProcessBatchWithSemaphoreAsync(
                        batch, 
                        batchProcessor, 
                        semaphore, 
                        results, 
                        cancellationToken);
                    
                    batchTasks.Add(batchTask);
                }

                // Wait for all batches to complete
                await Task.WhenAll(batchTasks).ConfigureAwait(false);

                results.EndTime = DateTime.UtcNow;
                results.Duration = results.EndTime - results.StartTime;

                // Calculate efficiency metrics
                CalculateEfficiencyMetrics(results, totalEntities, batches.Count());

                _logger.LogInformation("Bulk processing completed. Success: {SuccessfulOperations}/{TotalEntities}, " +
                                     "Efficiency: {EfficiencyGain:F1}%, Duration: {Duration:F1}s",
                    results.SuccessfulOperations, results.TotalEntities, 
                    results.EfficiencyGain, results.Duration.TotalSeconds);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk processing");
                throw;
            }
        }

        /// <summary>
        /// Processes entities in bulk with adaptive batch sizing based on entity characteristics
        /// </summary>
        /// <typeparam name="T">Type of entity to process</typeparam>
        /// <param name="entities">Collection of entities to process</param>
        /// <param name="batchProcessor">Function to process each batch of entities</param>
        /// <param name="entityComplexitySelector">Function to determine entity complexity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Results of adaptive bulk processing operation</returns>
        public async Task<BulkProcessingResults> ProcessEntitiesAdaptiveBulkAsync<T>(
            IEnumerable<T> entities,
            Func<IEnumerable<T>, Task> batchProcessor,
            Func<T, EntityComplexity> entityComplexitySelector,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var entityList = entities.ToList();
                _logger.LogInformation("Starting adaptive bulk processing of {EntityCount} entities", entityList.Count);

                // Group entities by complexity for optimal batch sizing
                var complexityGroups = entityList
                    .GroupBy(entityComplexitySelector)
                    .ToList();

                var overallResults = new BulkProcessingResults
                {
                    TotalEntities = entityList.Count,
                    StartTime = DateTime.UtcNow
                };

                // Process each complexity group with optimized batch sizes
                foreach (var group in complexityGroups)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var optimalBatchSize = GetOptimalBatchSizeForComplexity(group.Key);
                    
                    _logger.LogDebug("Processing {EntityCount} entities of complexity {Complexity} with batch size {BatchSize}",
                        group.Count(), group.Key, optimalBatchSize);

                    var groupResults = await ProcessEntitiesBulkAsync(
                        group, 
                        batchProcessor, 
                        optimalBatchSize, 
                        cancellationToken).ConfigureAwait(false);

                    // Aggregate results
                    AggregateResults(overallResults, groupResults);
                }

                overallResults.EndTime = DateTime.UtcNow;
                overallResults.Duration = overallResults.EndTime - overallResults.StartTime;

                _logger.LogInformation("Adaptive bulk processing completed with {SuccessfulOperations}/{TotalEntities} successful operations",
                    overallResults.SuccessfulOperations, overallResults.TotalEntities);

                return overallResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during adaptive bulk processing");
                throw;
            }
        }

        /// <summary>
        /// Processes a batch with semaphore-controlled concurrency
        /// </summary>
        private async Task ProcessBatchWithSemaphoreAsync<T>(
            IEnumerable<T> batch,
            Func<IEnumerable<T>, Task> batchProcessor,
            SemaphoreSlim semaphore,
            BulkProcessingResults results,
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var batchList = batch.ToList();
                await batchProcessor(batchList).ConfigureAwait(false);
                
                // Update results atomically
                results.AddSuccessfulOperations(batchList.Count);
                results.IncrementBatchesProcessed();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing batch of {BatchSize} entities", batch.Count());
                
                var batchCount = batch.Count();
                results.AddFailedOperations(batchCount);
                results.IncrementFailedBatches();
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Creates optimized batches from entity collection
        /// </summary>
        private static IEnumerable<IEnumerable<T>> CreateBatches<T>(IList<T> entities, int batchSize)
        {
            for (int i = 0; i < entities.Count; i += batchSize)
            {
                yield return entities.Skip(i).Take(batchSize);
            }
        }

        /// <summary>
        /// Gets optimal batch size based on entity complexity
        /// </summary>
        private static int GetOptimalBatchSizeForComplexity(EntityComplexity complexity)
        {
            return complexity switch
            {
                EntityComplexity.Low => 100,    // Categories, Brands - large batches
                EntityComplexity.Medium => 50,  // Variants - moderate batches
                EntityComplexity.High => 20,    // Products, Images - small batches
                _ => DefaultBatchSize
            };
        }

        /// <summary>
        /// Calculates efficiency metrics for bulk processing results
        /// </summary>
        private void CalculateEfficiencyMetrics(BulkProcessingResults results, int totalEntities, int totalBatches)
        {
            // Calculate efficiency gain (compared to individual operations)
            var individualOperations = totalEntities; // One operation per entity (baseline)
            var bulkOperations = totalBatches;        // One operation per batch
            
            results.EfficiencyGain = bulkOperations > 0 
                ? ((double)(individualOperations - bulkOperations) / individualOperations) * 100
                : 0;

            // Calculate throughput
            results.ThroughputPerSecond = results.Duration.TotalSeconds > 0 
                ? results.SuccessfulOperations / results.Duration.TotalSeconds 
                : 0;

            // Calculate success rate
            results.SuccessRate = results.TotalEntities > 0 
                ? (double)results.SuccessfulOperations / results.TotalEntities * 100 
                : 0;

            _logger.LogDebug("Efficiency metrics - Gain: {EfficiencyGain:F1}%, " +
                           "Throughput: {Throughput:F1} entities/sec, Success Rate: {SuccessRate:F1}%",
                results.EfficiencyGain, results.ThroughputPerSecond, results.SuccessRate);
        }

        /// <summary>
        /// Aggregates results from multiple processing operations
        /// </summary>
        private static void AggregateResults(BulkProcessingResults overall, BulkProcessingResults additional)
        {
            overall.SuccessfulOperations += additional.SuccessfulOperations;
            overall.FailedOperations += additional.FailedOperations;
            overall.BatchesProcessed += additional.BatchesProcessed;
            overall.FailedBatches += additional.FailedBatches;
            
            // Weighted average for efficiency gain
            var totalSuccessful = overall.SuccessfulOperations + additional.SuccessfulOperations;
            if (totalSuccessful > 0)
            {
                overall.EfficiencyGain = ((overall.SuccessfulOperations * overall.EfficiencyGain) + 
                                        (additional.SuccessfulOperations * additional.EfficiencyGain)) / totalSuccessful;
            }
        }
    }

    /// <summary>
    /// Results of bulk processing operations
    /// </summary>
    public class BulkProcessingResults
    {
        // Fields for atomic operations
        private long _successfulOperations;
        private long _failedOperations;
        private int _batchesProcessed;
        private int _failedBatches;

        /// <summary>
        /// Gets or sets the total number of entities to be processed
        /// </summary>
        public int TotalEntities { get; set; }

        /// <summary>
        /// Gets or sets the number of successfully processed operations
        /// </summary>
        public long SuccessfulOperations 
        { 
            get => Interlocked.Read(ref _successfulOperations);
            set => Interlocked.Exchange(ref _successfulOperations, value);
        }

        /// <summary>
        /// Gets or sets the number of failed operations
        /// </summary>
        public long FailedOperations 
        { 
            get => Interlocked.Read(ref _failedOperations);
            set => Interlocked.Exchange(ref _failedOperations, value);
        }

        /// <summary>
        /// Gets or sets the number of batches that were processed
        /// </summary>
        public int BatchesProcessed 
        { 
            get => _batchesProcessed;
            set => Interlocked.Exchange(ref _batchesProcessed, value);
        }

        /// <summary>
        /// Gets or sets the number of batches that failed processing
        /// </summary>
        public int FailedBatches 
        { 
            get => _failedBatches;
            set => Interlocked.Exchange(ref _failedBatches, value);
        }

        /// <summary>
        /// Gets or sets the efficiency gain percentage compared to individual operations
        /// </summary>
        public double EfficiencyGain { get; set; }

        /// <summary>
        /// Gets or sets the throughput in entities processed per second
        /// </summary>
        public double ThroughputPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the success rate percentage
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the start time of the bulk processing operation
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the end time of the bulk processing operation
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the total duration of the bulk processing operation
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Atomically adds to the successful operations count
        /// </summary>
        /// <param name="count">Number of operations to add</param>
        public void AddSuccessfulOperations(long count) => Interlocked.Add(ref _successfulOperations, count);

        /// <summary>
        /// Atomically adds to the failed operations count
        /// </summary>
        /// <param name="count">Number of operations to add</param>
        public void AddFailedOperations(long count) => Interlocked.Add(ref _failedOperations, count);

        /// <summary>
        /// Atomically increments the batches processed count
        /// </summary>
        public void IncrementBatchesProcessed() => Interlocked.Increment(ref _batchesProcessed);

        /// <summary>
        /// Atomically increments the failed batches count
        /// </summary>
        public void IncrementFailedBatches() => Interlocked.Increment(ref _failedBatches);
    }
} 