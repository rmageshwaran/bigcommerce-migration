using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Adaptive concurrency controller that adjusts concurrency levels based on system performance
    /// Monitors latency, memory pressure, CPU usage, and error rates to optimize processing
    /// </summary>
    public interface IAdaptiveConcurrencyController
    {
        /// <summary>
        /// Get the optimal concurrency level based on current system performance
        /// </summary>
        /// <param name="entityType">Type of entity being processed</param>
        /// <param name="currentPerformance">Current performance metrics</param>
        /// <returns>Recommended concurrency level</returns>
        Task<int> GetOptimalConcurrencyAsync(string entityType, SystemPerformanceMetrics currentPerformance);

        /// <summary>
        /// Record performance metrics for a completed operation
        /// </summary>
        /// <param name="entityType">Type of entity processed</param>
        /// <param name="concurrencyLevel">Concurrency level used</param>
        /// <param name="latency">Processing latency</param>
        /// <param name="memoryUsage">Memory usage during processing</param>
        /// <param name="success">Whether the operation was successful</param>
        Task RecordPerformanceAsync(string entityType, int concurrencyLevel, TimeSpan latency, long memoryUsage, bool success);

        /// <summary>
        /// Get performance history for analysis
        /// </summary>
        /// <param name="entityType">Type of entity</param>
        /// <param name="timeRange">Time range for history</param>
        /// <returns>Performance metrics history</returns>
        Task<List<PerformanceSnapshot>> GetPerformanceHistoryAsync(string entityType, TimeSpan timeRange);
    }

    /// <summary>
    /// System performance metrics for concurrency adjustment
    /// </summary>
    public class SystemPerformanceMetrics
    {
        /// <summary>
        /// Average latency in milliseconds for recent operations
        /// </summary>
        public double AverageLatencyMs { get; set; }
        
        /// <summary>
        /// Memory pressure as a percentage (0.0 to 1.0)
        /// </summary>
        public double MemoryPressure { get; set; }
        
        /// <summary>
        /// CPU usage as a percentage (0.0 to 1.0)
        /// </summary>
        public double CpuUsage { get; set; }
        
        /// <summary>
        /// Error rate as a percentage (0.0 to 1.0)
        /// </summary>
        public double ErrorRate { get; set; }
        
        /// <summary>
        /// Current concurrency level being used
        /// </summary>
        public int CurrentConcurrency { get; set; }
        
        /// <summary>
        /// Timestamp when these metrics were captured
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Performance snapshot for historical analysis
    /// </summary>
    public class PerformanceSnapshot
    {
        /// <summary>
        /// Type of entity being processed
        /// </summary>
        public string EntityType { get; set; } = string.Empty;
        
        /// <summary>
        /// Concurrency level used for this operation
        /// </summary>
        public int ConcurrencyLevel { get; set; }
        
        /// <summary>
        /// Processing latency in milliseconds
        /// </summary>
        public double LatencyMs { get; set; }
        
        /// <summary>
        /// Memory usage in bytes
        /// </summary>
        public long MemoryUsage { get; set; }
        
        /// <summary>
        /// Whether the operation was successful
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// Timestamp of this performance snapshot
        /// </summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Adaptive concurrency controller implementation
    /// </summary>
    public class AdaptiveConcurrencyController : IAdaptiveConcurrencyController
    {
        private readonly ILogger<AdaptiveConcurrencyController> _logger;
        private readonly ConcurrentDictionary<string, int> _currentConcurrency = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<PerformanceSnapshot>> _performanceHistory = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastAdjustment = new();

        // Configuration constants
        private const int MIN_CONCURRENCY = 2;
        private const int MAX_CONCURRENCY = 50;
        private const double TARGET_LATENCY_MS = 1500;
        private const double MAX_ERROR_RATE = 0.05; // 5%
        private const double HIGH_MEMORY_PRESSURE = 0.8; // 80%
        private const double HIGH_CPU_USAGE = 0.9; // 90%
        private const int ADJUSTMENT_COOLDOWN_SECONDS = 30; // Wait 30 seconds between adjustments
        private const int PERFORMANCE_HISTORY_LIMIT = 1000; // Keep last 1000 records per entity type

        /// <summary>
        /// Initializes a new instance of the AdaptiveConcurrencyController
        /// </summary>
        /// <param name="logger">Logger instance for diagnostics</param>
        public AdaptiveConcurrencyController(ILogger<AdaptiveConcurrencyController> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the optimal concurrency level based on current system performance
        /// </summary>
        /// <param name="entityType">Type of entity being processed</param>
        /// <param name="currentPerformance">Current system performance metrics</param>
        /// <returns>Optimal concurrency level for the given entity type</returns>
        public async Task<int> GetOptimalConcurrencyAsync(string entityType, SystemPerformanceMetrics currentPerformance)
        {
            if (string.IsNullOrEmpty(entityType))
                throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));

            await Task.CompletedTask; // Make method async for interface compliance

            var currentConcurrency = _currentConcurrency.GetOrAdd(entityType, GetDefaultConcurrency(entityType));
            var lastAdjustment = _lastAdjustment.GetValueOrDefault(entityType, DateTime.MinValue);

            // Check if we're within cooldown period
            if (DateTime.UtcNow - lastAdjustment < TimeSpan.FromSeconds(ADJUSTMENT_COOLDOWN_SECONDS))
            {
                _logger.LogDebug("Concurrency adjustment for {EntityType} is in cooldown period", entityType);
                return currentConcurrency;
            }

            var newConcurrency = CalculateOptimalConcurrency(entityType, currentConcurrency, currentPerformance);

            if (newConcurrency != currentConcurrency)
            {
                _currentConcurrency[entityType] = newConcurrency;
                _lastAdjustment[entityType] = DateTime.UtcNow;

                _logger.LogInformation("Adjusted concurrency for {EntityType} from {OldConcurrency} to {NewConcurrency} " +
                                     "based on performance metrics (Latency: {Latency:F2}ms, Memory: {Memory:P}, Error Rate: {ErrorRate:P})",
                    entityType, currentConcurrency, newConcurrency, 
                    currentPerformance.AverageLatencyMs, currentPerformance.MemoryPressure, currentPerformance.ErrorRate);
            }

            return newConcurrency;
        }

        /// <summary>
        /// Records performance metrics for a completed operation.
        /// </summary>
        /// <param name="entityType">Type of entity processed</param>
        /// <param name="concurrencyLevel">Concurrency level used</param>
        /// <param name="latency">Processing latency</param>
        /// <param name="memoryUsage">Memory usage during processing</param>
        /// <param name="success">Whether the operation was successful</param>
        public async Task RecordPerformanceAsync(string entityType, int concurrencyLevel, TimeSpan latency, long memoryUsage, bool success)
        {
            if (string.IsNullOrEmpty(entityType))
                throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));

            await Task.CompletedTask; // Make method async for interface compliance

            var snapshot = new PerformanceSnapshot
            {
                EntityType = entityType,
                ConcurrencyLevel = concurrencyLevel,
                LatencyMs = latency.TotalMilliseconds,
                MemoryUsage = memoryUsage,
                Success = success,
                Timestamp = DateTime.UtcNow
            };

            var history = _performanceHistory.GetOrAdd(entityType, _ => new ConcurrentQueue<PerformanceSnapshot>());
            history.Enqueue(snapshot);

            // Maintain history size limit
            while (history.Count > PERFORMANCE_HISTORY_LIMIT && history.TryDequeue(out _))
            {
                // Remove oldest entries
            }

            _logger.LogDebug("Recorded performance for {EntityType}: Concurrency={Concurrency}, Latency={Latency:F2}ms, Success={Success}",
                entityType, concurrencyLevel, latency.TotalMilliseconds, success);
        }

        /// <summary>
        /// Gets performance history for analysis.
        /// </summary>
        /// <param name="entityType">Type of entity</param>
        /// <param name="timeRange">Time range for history</param>
        /// <returns>Performance metrics history</returns>
        public async Task<List<PerformanceSnapshot>> GetPerformanceHistoryAsync(string entityType, TimeSpan timeRange)
        {
            if (string.IsNullOrEmpty(entityType))
                throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));

            await Task.CompletedTask; // Make method async for interface compliance

            var cutoffTime = DateTime.UtcNow - timeRange;
            var history = _performanceHistory.GetValueOrDefault(entityType, new ConcurrentQueue<PerformanceSnapshot>());

            return history.Where(snapshot => snapshot.Timestamp >= cutoffTime)
                         .OrderBy(snapshot => snapshot.Timestamp)
                         .ToList();
        }

        private int CalculateOptimalConcurrency(string entityType, int currentConcurrency, SystemPerformanceMetrics performance)
        {
            var newConcurrency = currentConcurrency;

            // Get recent performance history for trend analysis
            var recentHistory = GetRecentPerformanceHistory(entityType, TimeSpan.FromMinutes(5));
            var errorRate = CalculateErrorRate(recentHistory);
            var avgLatency = CalculateAverageLatency(recentHistory);

            // Decision matrix for concurrency adjustment
            var shouldDecrease = ShouldDecreaseConcurrency(performance, errorRate, avgLatency);
            var shouldIncrease = ShouldIncreaseConcurrency(performance, errorRate, avgLatency, currentConcurrency);

            if (shouldDecrease)
            {
                newConcurrency = Math.Max(MIN_CONCURRENCY, currentConcurrency - CalculateDecreaseAmount(performance));
                _logger.LogDebug("Decreasing concurrency for {EntityType}: Latency={Latency:F2}ms, Error Rate={ErrorRate:P}, Memory={Memory:P}",
                    entityType, performance.AverageLatencyMs, errorRate, performance.MemoryPressure);
            }
            else if (shouldIncrease)
            {
                newConcurrency = Math.Min(MAX_CONCURRENCY, currentConcurrency + CalculateIncreaseAmount(performance));
                _logger.LogDebug("Increasing concurrency for {EntityType}: Good performance metrics detected",
                    entityType);
            }

            return newConcurrency;
        }

        private bool ShouldDecreaseConcurrency(SystemPerformanceMetrics performance, double errorRate, double avgLatency)
        {
            return performance.AverageLatencyMs > TARGET_LATENCY_MS ||
                   errorRate > MAX_ERROR_RATE ||
                   performance.MemoryPressure > HIGH_MEMORY_PRESSURE ||
                   performance.CpuUsage > HIGH_CPU_USAGE ||
                   avgLatency > TARGET_LATENCY_MS;
        }

        private bool ShouldIncreaseConcurrency(SystemPerformanceMetrics performance, double errorRate, double avgLatency, int currentConcurrency)
        {
            return currentConcurrency < MAX_CONCURRENCY &&
                   performance.AverageLatencyMs < TARGET_LATENCY_MS * 0.5 &&
                   errorRate < MAX_ERROR_RATE * 0.5 &&
                   performance.MemoryPressure < HIGH_MEMORY_PRESSURE * 0.6 &&
                   performance.CpuUsage < HIGH_CPU_USAGE * 0.7 &&
                   avgLatency < TARGET_LATENCY_MS * 0.5;
        }

        private int CalculateDecreaseAmount(SystemPerformanceMetrics performance)
        {
            // More aggressive decrease for worse performance
            if (performance.AverageLatencyMs > TARGET_LATENCY_MS * 2 || performance.ErrorRate > MAX_ERROR_RATE * 2)
                return 3;
            if (performance.AverageLatencyMs > TARGET_LATENCY_MS * 1.5 || performance.ErrorRate > MAX_ERROR_RATE * 1.5)
                return 2;
            return 1;
        }

        private int CalculateIncreaseAmount(SystemPerformanceMetrics performance)
        {
            // Conservative increase - only add 1-2 at a time
            if (performance.AverageLatencyMs < TARGET_LATENCY_MS * 0.25 && 
                performance.MemoryPressure < HIGH_MEMORY_PRESSURE * 0.4)
                return 2;
            return 1;
        }

        private List<PerformanceSnapshot> GetRecentPerformanceHistory(string entityType, TimeSpan timeRange)
        {
            var cutoffTime = DateTime.UtcNow - timeRange;
            var history = _performanceHistory.GetValueOrDefault(entityType, new ConcurrentQueue<PerformanceSnapshot>());

            return history.Where(snapshot => snapshot.Timestamp >= cutoffTime).ToList();
        }

        private double CalculateErrorRate(List<PerformanceSnapshot> snapshots)
        {
            if (!snapshots.Any()) return 0.0;

            var totalOperations = snapshots.Count;
            var failedOperations = snapshots.Count(s => !s.Success);

            return (double)failedOperations / totalOperations;
        }

        private double CalculateAverageLatency(List<PerformanceSnapshot> snapshots)
        {
            if (!snapshots.Any()) return 0.0;

            return snapshots.Average(s => s.LatencyMs);
        }

        private int GetDefaultConcurrency(string entityType)
        {
            return entityType.ToLowerInvariant() switch
            {
                "categories" => 4,
                "products" => 6,
                "brands" => 8,
                "variants" => 5,
                "images" => 3, // Images tend to be more resource intensive
                "modifiers" => 7,
                _ => 5 // Default fallback
            };
        }
    }
} 