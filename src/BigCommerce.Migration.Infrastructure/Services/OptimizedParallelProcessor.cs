using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// High-performance parallel entity processor with controlled concurrency and memory management
    /// Target: 3-5x processing speed improvement over sequential processing
    /// Rate limiting is handled at higher levels where store context is available
    /// </summary>
    public class OptimizedParallelProcessor : IParallelEntityProcessor
    {
        private readonly ILogger<OptimizedParallelProcessor> _logger;

        // Performance tracking
        private long _totalEntitiesProcessed = 0;
        private long _totalErrors = 0;
        private readonly ConcurrentQueue<long> _processingTimes = new();
        private readonly Stopwatch _sessionStopwatch = Stopwatch.StartNew();
        private int _currentConcurrency = 0;
        private int _maxConcurrency = 0;

        /// <summary>
        /// Initializes a new instance of the OptimizedParallelProcessor
        /// </summary>
        /// <param name="logger">Logger instance for diagnostics and performance tracking</param>
        public OptimizedParallelProcessor(ILogger<OptimizedParallelProcessor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Process entities in parallel with controlled concurrency without return values
        /// </summary>
        /// <typeparam name="T">Type of entity to process</typeparam>
        /// <param name="entities">Collection of entities to process</param>
        /// <param name="maxConcurrency">Maximum number of concurrent operations</param>
        /// <param name="processor">Function to process each entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the completion of all processing</returns>
        public async Task ProcessEntitiesAsync<T>(
            IEnumerable<T> entities,
            int maxConcurrency,
            Func<T, CancellationToken, Task> processor,
            CancellationToken cancellationToken = default)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            if (processor == null) throw new ArgumentNullException(nameof(processor));
            if (maxConcurrency <= 0) throw new ArgumentException("Max concurrency must be greater than 0", nameof(maxConcurrency));

            var entityList = entities.ToList();
            if (!entityList.Any()) return;

            _maxConcurrency = maxConcurrency;
            _logger.LogInformation("Starting parallel processing of {EntityCount} entities with max concurrency {MaxConcurrency}",
                entityList.Count, maxConcurrency);

            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var processingStopwatch = Stopwatch.StartNew();

            var tasks = entityList.Select(async entity =>
            {
                await semaphore.WaitAsync(cancellationToken);
                Interlocked.Increment(ref _currentConcurrency);

                try
                {
                    var entityStopwatch = Stopwatch.StartNew();
                    
                    // Process the entity (rate limiting handled at higher levels)
                    await processor(entity, cancellationToken);
                    
                    entityStopwatch.Stop();
                    _processingTimes.Enqueue(entityStopwatch.ElapsedMilliseconds);
                    Interlocked.Increment(ref _totalEntitiesProcessed);
                }
                catch (OperationCanceledException)
                {
                    // Expected during cancellation, don't log as error
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _totalErrors);
                    _logger.LogError(ex, "Error processing entity in parallel processor");
                    throw;
                }
                finally
                {
                    Interlocked.Decrement(ref _currentConcurrency);
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            processingStopwatch.Stop();
            
            _logger.LogInformation("Completed parallel processing: {EntityCount} entities in {ElapsedMs}ms, " +
                                 "Average: {AvgMs}ms per entity, Errors: {ErrorCount}",
                entityList.Count, processingStopwatch.ElapsedMilliseconds,
                entityList.Count > 0 ? processingStopwatch.ElapsedMilliseconds / entityList.Count : 0,
                _totalErrors);
        }

        /// <summary>
        /// Process entities in parallel with controlled concurrency and collect results
        /// </summary>
        /// <typeparam name="T">Type of entity to process</typeparam>
        /// <typeparam name="TResult">Type of result to collect</typeparam>
        /// <param name="entities">Collection of entities to process</param>
        /// <param name="maxConcurrency">Maximum number of concurrent operations</param>
        /// <param name="processor">Function to process each entity and return a result</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the completion of all processing, returning a list of results</returns>
        public async Task<List<TResult>> ProcessEntitiesAsync<T, TResult>(
            IEnumerable<T> entities,
            int maxConcurrency,
            Func<T, CancellationToken, Task<TResult>> processor,
            CancellationToken cancellationToken = default)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            if (processor == null) throw new ArgumentNullException(nameof(processor));
            if (maxConcurrency <= 0) throw new ArgumentException("Max concurrency must be greater than 0", nameof(maxConcurrency));

            var entityList = entities.ToList();
            if (!entityList.Any()) return new List<TResult>();

            _maxConcurrency = maxConcurrency;

            _logger.LogInformation("Starting parallel processing of {EntityCount} entities with results collection, max concurrency {MaxConcurrency}",
                entityList.Count, maxConcurrency);

            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var results = new ConcurrentBag<TResult>();
            var processingStopwatch = Stopwatch.StartNew();

            var tasks = entityList.Select(async entity =>
            {
                await semaphore.WaitAsync(cancellationToken);
                Interlocked.Increment(ref _currentConcurrency);

                try
                {
                    var entityStopwatch = Stopwatch.StartNew();
                    
                    // Process the entity and collect result (rate limiting handled at higher levels)
                    var result = await processor(entity, cancellationToken);
                    results.Add(result);
                    
                    entityStopwatch.Stop();
                    _processingTimes.Enqueue(entityStopwatch.ElapsedMilliseconds);
                    Interlocked.Increment(ref _totalEntitiesProcessed);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _totalErrors);
                    _logger.LogError(ex, "Error processing entity in parallel with results");
                    throw;
                }
                finally
                {
                    Interlocked.Decrement(ref _currentConcurrency);
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            processingStopwatch.Stop();
            var resultList = results.ToList();
            
            _logger.LogInformation("Completed parallel processing with results: {EntityCount} entities in {ElapsedMs}ms, " +
                                 "Collected {ResultCount} results, Average: {AvgMs}ms per entity, Errors: {ErrorCount}",
                entityList.Count, processingStopwatch.ElapsedMilliseconds, resultList.Count,
                entityList.Count > 0 ? processingStopwatch.ElapsedMilliseconds / entityList.Count : 0,
                _totalErrors);

            return resultList;
        }

        /// <summary>
        /// Process entities with adaptive concurrency based on performance
        /// </summary>
        /// <typeparam name="T">Type of entity to process</typeparam>
        /// <param name="entities">Collection of entities to process</param>
        /// <param name="initialConcurrency">Initial number of concurrent operations</param>
        /// <param name="processor">Function to process each entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the completion of all processing</returns>
        public async Task ProcessEntitiesWithAdaptiveConcurrencyAsync<T>(
            IEnumerable<T> entities,
            int initialConcurrency,
            Func<T, CancellationToken, Task> processor,
            CancellationToken cancellationToken = default)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            if (processor == null) throw new ArgumentNullException(nameof(processor));
            if (initialConcurrency <= 0) throw new ArgumentException("Initial concurrency must be greater than 0", nameof(initialConcurrency));

            var entityList = entities.ToList();
            if (!entityList.Any()) return;

            _logger.LogInformation("Starting adaptive parallel processing of {EntityCount} entities with initial concurrency {InitialConcurrency}",
                entityList.Count, initialConcurrency);

            var currentConcurrency = initialConcurrency;
            var batchSize = Math.Min(50, entityList.Count / 4); // Process in batches to allow adaptation
            if (batchSize < 10) batchSize = entityList.Count; // For small datasets, process all at once

            for (int i = 0; i < entityList.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = entityList.Skip(i).Take(batchSize);
                var batchStopwatch = Stopwatch.StartNew();

                // Process current batch
                await ProcessEntitiesAsync(batch, currentConcurrency, processor, cancellationToken);

                batchStopwatch.Stop();

                // Adaptive concurrency adjustment based on performance
                if (batchStopwatch.ElapsedMilliseconds > 0 && i + batchSize < entityList.Count)
                {
                    var avgTimePerEntity = (double)batchStopwatch.ElapsedMilliseconds / batchSize;
                    
                    if (avgTimePerEntity < 50) // Fast processing - can increase concurrency
                    {
                        currentConcurrency = Math.Min(currentConcurrency + 2, initialConcurrency * 2);
                        _logger.LogDebug("Increased concurrency to {Concurrency} due to fast processing ({AvgTime:F2}ms per entity)",
                            currentConcurrency, avgTimePerEntity);
                    }
                    else if (avgTimePerEntity > 200) // Slow processing - decrease concurrency
                    {
                        currentConcurrency = Math.Max(currentConcurrency - 1, 1);
                        _logger.LogDebug("Decreased concurrency to {Concurrency} due to slow processing ({AvgTime:F2}ms per entity)",
                            currentConcurrency, avgTimePerEntity);
                    }
                }
            }

            _logger.LogInformation("Adaptive parallel processing completed. Final concurrency: {FinalConcurrency}",
                currentConcurrency);
        }

        /// <summary>
        /// Get performance metrics for the current session
        /// </summary>
        /// <returns>A ParallelProcessingMetrics object containing performance data</returns>
        public async Task<ParallelProcessingMetrics> GetPerformanceMetricsAsync()
        {
            await Task.CompletedTask; // Make it async for interface compliance

            var processingTimesList = new List<long>();
            while (_processingTimes.TryDequeue(out var time))
            {
                processingTimesList.Add(time);
            }

            var avgProcessingTime = processingTimesList.Any() ? processingTimesList.Average() : 0;
            var totalSessionTimeSeconds = _sessionStopwatch.Elapsed.TotalSeconds;
            var throughput = totalSessionTimeSeconds > 0 ? _totalEntitiesProcessed / totalSessionTimeSeconds : 0;

            // Get current memory usage
            var memoryUsageBytes = GC.GetTotalMemory(forceFullCollection: false);
            var memoryUsageMB = memoryUsageBytes / (1024 * 1024);

            var metrics = new ParallelProcessingMetrics
            {
                CurrentConcurrency = _currentConcurrency,
                MaxConcurrency = _maxConcurrency,
                AverageProcessingTimeMs = avgProcessingTime,
                ThroughputPerSecond = throughput,
                TotalEntitiesProcessed = _totalEntitiesProcessed,
                TotalErrors = _totalErrors,
                MemoryUsageMB = memoryUsageMB,
                CapturedAt = DateTime.UtcNow
            };

            return metrics;
        }
    }
} 