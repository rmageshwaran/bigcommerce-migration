using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// **PHASE 2: Enhanced Parallel Processing Interface**
/// 
/// Coordinates parallel batch processing while respecting dynamic rate limits
/// and preserving real-time progress tracking through SignalR.
/// 
/// **Key Capabilities:**
/// - Process multiple batches concurrently with controlled semaphore-based throttling
/// - Integrate with Phase 1 dynamic rate limiting (IDynamicRateLimiter) 
/// - Maintain thread-safe progress updates for SignalR real-time monitoring
/// - Preserve Durable Functions determinism requirements
/// - Adaptive concurrency based on API health and system performance
/// 
/// **Target Performance:**
/// - 12.5x total throughput improvement (720 → 9,000 req/hour)
/// - Optimal resource utilization (70% CPU target)
/// - Zero message loss or duplication during parallel processing
/// </summary>
public interface IEnhancedParallelProcessor
{
    #region Parallel Batch Coordination

    /// <summary>
    /// Processes multiple batches in parallel while respecting dynamic rate limits
    /// 
    /// **Core Functionality:**
    /// - Coordinates Task.WhenAll execution for optimal parallel performance
    /// - Applies semaphore-based concurrency control based on system health
    /// - Integrates with IDynamicRateLimiter for per-batch rate respect
    /// - Maintains thread-safe progress aggregation for SignalR updates
    /// 
    /// **Determinism Compliance:**
    /// - Uses deterministic batch ordering for Durable Functions compatibility
    /// - Preserves replay-safe progress tracking patterns
    /// - Maintains exact entity count accuracy across parallel execution
    /// </summary>
    /// <param name="batches">Collection of batches to process in parallel</param>
    /// <param name="batchProcessor">Function to process individual batches</param>
    /// <param name="parallelConfig">Configuration for parallel processing behavior</param>
    /// <param name="progressCallback">Thread-safe callback for real-time progress updates</param>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown</param>
    /// <returns>Aggregated results from all parallel batch processing</returns>
    Task<ParallelProcessingResult> ProcessBatchesInParallelAsync<TBatch>(
        IReadOnlyList<TBatch> batches,
        Func<TBatch, CancellationToken, Task<BatchProcessingResult>> batchProcessor,
        ParallelProcessingConfiguration parallelConfig,
        IProgress<BatchProgressUpdate>? progressCallback = null,
        CancellationToken cancellationToken = default)
        where TBatch : class;

    #endregion

    #region Concurrency Management

    /// <summary>
    /// Calculates optimal concurrency level for parallel batch processing
    /// 
    /// Integrates with Phase 1 dynamic rate limiting to determine:
    /// - Maximum safe concurrent batches based on current API health
    /// - System resource availability (CPU, memory, connection pool)
    /// - BigCommerce rate limit headroom across all parallel requests
    /// - Historical performance patterns for adaptive optimization
    /// </summary>
    /// <param name="storeId">Store identifier for rate limit context</param>
    /// <param name="entityType">Type of entities being processed</param>
    /// <param name="totalBatches">Total number of batches to process</param>
    /// <param name="totalEntityCount">Total number of entities (for adaptive concurrency scaling, -1 if unknown)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Optimal concurrency level (recommended parallel batch count)</returns>
    Task<OptimalConcurrencyResult> CalculateOptimalConcurrencyAsync(
        string storeId,
        string entityType,
        int totalBatches,
        int totalEntityCount = -1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Monitors and adjusts concurrency during parallel execution
    /// 
    /// **Adaptive Behavior:**
    /// - Increases concurrency when API health improves
    /// - Decreases concurrency when rate limits approach
    /// - Responds to system resource pressure (CPU, memory)
    /// - Maintains optimal throughput without causing 429 errors
    /// </summary>
    /// <param name="currentConcurrency">Current number of concurrent batches</param>
    /// <param name="performanceMetrics">Real-time system and API performance data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Adjusted concurrency recommendation</returns>
    Task<ConcurrencyAdjustmentResult> AdjustConcurrencyAsync(
        int currentConcurrency,
        ParallelPerformanceMetrics performanceMetrics,
        CancellationToken cancellationToken = default);

    #endregion

    #region Progress Tracking & SignalR Integration

    /// <summary>
    /// Creates a thread-safe progress aggregator for parallel batch processing
    /// 
    /// **Thread Safety Features:**
    /// - Concurrent updates from multiple batch processors
    /// - Atomic progress calculations (total processed, failed counts)
    /// - Rate-limited SignalR updates to prevent UI flooding
    /// - Deterministic progress ordering for Durable Functions replay
    /// 
    /// **SignalR Integration:**
    /// - Real-time batch completion notifications
    /// - Aggregated progress percentage updates
    /// - Error reporting with batch-specific context
    /// - Throughput metrics for performance monitoring
    /// </summary>
    /// <param name="migrationId">Migration identifier for progress context</param>
    /// <param name="entityType">Entity type being processed</param>
    /// <param name="totalBatches">Total number of batches in the migration</param>
    /// <param name="progressEventPublisher">Progress event publisher for SignalR real-time updates</param>
    /// <returns>Thread-safe progress aggregator instance</returns>
    IParallelProgressAggregator CreateProgressAggregator(
        string migrationId,
        string entityType,
        int totalBatches,
        IProgressEventPublisher? progressEventPublisher = null);

    #endregion

    #region Health & Performance Monitoring

    /// <summary>
    /// Monitors parallel processing health and performance metrics
    /// 
    /// **Monitoring Capabilities:**
    /// - Concurrent batch execution performance
    /// - Rate limit compliance across parallel streams
    /// - System resource utilization during parallel processing
    /// - Error rates and retry patterns per batch
    /// - SignalR update delivery success rates
    /// 
    /// **Integration Points:**
    /// - Phase 1 dynamic rate limiting health data
    /// - Azure Application Insights performance counters
    /// - Custom parallel processing metrics for optimization
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comprehensive parallel processing health metrics</returns>
    Task<ParallelProcessingHealthMetrics> GetProcessingHealthAsync(
        string storeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Event triggered when parallel processing performance changes significantly
    /// 
    /// **Use Cases:**
    /// - Automatic concurrency adjustments based on performance trends
    /// - Alert generation when parallel processing degrades
    /// - Performance data collection for machine learning optimization
    /// - Integration with monitoring dashboards and alerting systems
    /// </summary>
    event EventHandler<ParallelPerformanceChangedEventArgs>? ParallelPerformanceChanged;

    #endregion
} 