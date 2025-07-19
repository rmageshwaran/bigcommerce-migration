using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Interface for parallel entity processing with controlled concurrency and memory management
    /// </summary>
    public interface IParallelEntityProcessor
    {
        /// <summary>
        /// Process entities in parallel with controlled concurrency without return values
        /// </summary>
        /// <typeparam name="T">Type of entity to process</typeparam>
        /// <param name="entities">Collection of entities to process</param>
        /// <param name="maxConcurrency">Maximum number of concurrent operations</param>
        /// <param name="processor">Function to process each entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the completion of all processing</returns>
        Task ProcessEntitiesAsync<T>(
            IEnumerable<T> entities,
            int maxConcurrency,
            Func<T, CancellationToken, Task> processor,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Process entities in parallel with controlled concurrency and collect results
        /// </summary>
        /// <typeparam name="T">Type of entity to process</typeparam>
        /// <typeparam name="TResult">Type of result from processing</typeparam>
        /// <param name="entities">Collection of entities to process</param>
        /// <param name="maxConcurrency">Maximum number of concurrent operations</param>
        /// <param name="processor">Function to process each entity and return a result</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task containing list of processing results</returns>
        Task<List<TResult>> ProcessEntitiesAsync<T, TResult>(
            IEnumerable<T> entities,
            int maxConcurrency,
            Func<T, CancellationToken, Task<TResult>> processor,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Process entities in parallel with adaptive concurrency based on system performance
        /// </summary>
        /// <typeparam name="T">Type of entity to process</typeparam>
        /// <param name="entities">Collection of entities to process</param>
        /// <param name="initialConcurrency">Initial concurrency level</param>
        /// <param name="processor">Function to process each entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the completion of all processing</returns>
        Task ProcessEntitiesWithAdaptiveConcurrencyAsync<T>(
            IEnumerable<T> entities,
            int initialConcurrency,
            Func<T, CancellationToken, Task> processor,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get current performance metrics for parallel processing
        /// </summary>
        /// <returns>Performance metrics including throughput, concurrency, and resource usage</returns>
        Task<ParallelProcessingMetrics> GetPerformanceMetricsAsync();
    }

    /// <summary>
    /// Performance metrics for parallel processing operations
    /// </summary>
    public class ParallelProcessingMetrics
    {
        /// <summary>
        /// Current concurrent operations count
        /// </summary>
        public int CurrentConcurrency { get; set; }

        /// <summary>
        /// Maximum concurrency level configured
        /// </summary>
        public int MaxConcurrency { get; set; }

        /// <summary>
        /// Average processing time per entity in milliseconds
        /// </summary>
        public double AverageProcessingTimeMs { get; set; }

        /// <summary>
        /// Throughput in entities processed per second
        /// </summary>
        public double ThroughputPerSecond { get; set; }

        /// <summary>
        /// Total entities processed in current session
        /// </summary>
        public long TotalEntitiesProcessed { get; set; }

        /// <summary>
        /// Total errors encountered during processing
        /// </summary>
        public long TotalErrors { get; set; }

        /// <summary>
        /// Current memory usage in MB
        /// </summary>
        public long MemoryUsageMB { get; set; }

        /// <summary>
        /// When these metrics were captured
        /// </summary>
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    }
} 