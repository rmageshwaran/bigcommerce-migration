namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Performance and operational metrics for the RowNumber service
/// Provides insights into counter service performance, concurrency, and health
/// 
/// Used for:
/// - Performance monitoring and optimization
/// - Concurrency conflict analysis
/// - Service health assessment
/// - Capacity planning and scaling decisions
/// </summary>
public class RowNumberServiceMetrics
{
    #region General Metrics

    /// <summary>
    /// Migration ID scope for these metrics (null for global metrics)
    /// </summary>
    public string? MigrationId { get; set; }

    /// <summary>
    /// Entity type scope for these metrics (null for all entity types)
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// When these metrics were collected
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Time period covered by these metrics
    /// </summary>
    public TimeSpan MetricsPeriod { get; set; }

    #endregion

    #region Performance Metrics

    /// <summary>
    /// Total number of RowNumber allocation requests processed
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Total number of RowNumbers allocated (sum of all individual and range allocations)
    /// </summary>
    public long TotalRowNumbersAllocated { get; set; }

    /// <summary>
    /// Average request latency in milliseconds
    /// </summary>
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// 95th percentile request latency in milliseconds
    /// </summary>
    public double P95LatencyMs { get; set; }

    /// <summary>
    /// Maximum request latency in milliseconds
    /// </summary>
    public double MaxLatencyMs { get; set; }

    /// <summary>
    /// Requests per second throughput
    /// </summary>
    public double RequestsPerSecond { get; set; }

    /// <summary>
    /// RowNumbers allocated per second throughput
    /// </summary>
    public double RowNumbersPerSecond { get; set; }

    #endregion

    #region Concurrency Metrics

    /// <summary>
    /// Total number of concurrency conflicts (ETag failures)
    /// </summary>
    public long ConcurrencyConflicts { get; set; }

    /// <summary>
    /// Percentage of requests that experienced concurrency conflicts
    /// </summary>
    public double ConflictRate => TotalRequests > 0 ? (ConcurrencyConflicts / (double)TotalRequests) * 100.0 : 0.0;

    /// <summary>
    /// Average number of retry attempts per request
    /// </summary>
    public double AverageRetryAttempts { get; set; }

    /// <summary>
    /// Maximum number of retry attempts for any single request
    /// </summary>
    public int MaxRetryAttempts { get; set; }

    /// <summary>
    /// Number of requests that failed after all retries
    /// </summary>
    public long FailedRequestsAfterRetries { get; set; }

    #endregion

    #region Range Allocation Metrics

    /// <summary>
    /// Number of range allocation requests (vs individual requests)
    /// </summary>
    public long RangeAllocationRequests { get; set; }

    /// <summary>
    /// Average range size for batch allocations
    /// </summary>
    public double AverageRangeSize { get; set; }

    /// <summary>
    /// Largest single range allocation
    /// </summary>
    public int MaxRangeSize { get; set; }

    /// <summary>
    /// Percentage of RowNumbers allocated via range requests
    /// </summary>
    public double RangeAllocationPercentage => TotalRowNumbersAllocated > 0 
        ? ((double)(TotalRowNumbersAllocated - (TotalRequests - RangeAllocationRequests)) / TotalRowNumbersAllocated) * 100.0 
        : 0.0;

    #endregion

    #region Health Metrics

    /// <summary>
    /// Overall service health status
    /// </summary>
    public RowNumberServiceHealth HealthStatus { get; set; } = RowNumberServiceHealth.Healthy;

    /// <summary>
    /// Number of active counters being managed
    /// </summary>
    public int ActiveCounters { get; set; }

    /// <summary>
    /// Azure Table Storage connection health
    /// </summary>
    public bool StorageConnectionHealthy { get; set; } = true;

    /// <summary>
    /// Any health issues or warnings
    /// </summary>
    public List<string> HealthIssues { get; set; } = new List<string>();

    #endregion

    #region Efficiency Metrics

    /// <summary>
    /// Cache hit rate for counter queries (if caching is implemented)
    /// </summary>
    public double CacheHitRate { get; set; }

    /// <summary>
    /// Average Azure Table Storage RU consumption per request
    /// </summary>
    public double AverageRUPerRequest { get; set; }

    /// <summary>
    /// Estimated cost per 1000 RowNumber allocations (in USD)
    /// </summary>
    public double EstimatedCostPer1000Allocations { get; set; }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Determines if the service performance is within acceptable thresholds
    /// </summary>
    /// <returns>True if performance is acceptable</returns>
    public bool IsPerformanceAcceptable()
    {
        return AverageLatencyMs < 100.0 &&  // < 100ms average
               P95LatencyMs < 200.0 &&      // < 200ms P95
               ConflictRate < 10.0 &&       // < 10% conflict rate
               RequestsPerSecond > 50.0 &&  // > 50 RPS throughput
               FailedRequestsAfterRetries == 0;  // No failed requests
    }

    /// <summary>
    /// Gets a summary of performance health
    /// </summary>
    /// <returns>Performance health summary</returns>
    public string GetPerformanceSummary()
    {
        var status = IsPerformanceAcceptable() ? "✅ GOOD" : "⚠️ DEGRADED";
        return $"{status} - Avg: {AverageLatencyMs:F1}ms, P95: {P95LatencyMs:F1}ms, " +
               $"Throughput: {RequestsPerSecond:F0} RPS, Conflicts: {ConflictRate:F1}%";
    }

    /// <summary>
    /// Gets efficiency metrics summary
    /// </summary>
    /// <returns>Efficiency summary</returns>
    public string GetEfficiencySummary()
    {
        return $"Cache Hit Rate: {CacheHitRate:F1}%, Avg RU/Request: {AverageRUPerRequest:F2}, " +
               $"Range Allocation: {RangeAllocationPercentage:F1}%";
    }

    #endregion
}

/// <summary>
/// Health status enumeration for RowNumber service
/// </summary>
public enum RowNumberServiceHealth
{
    /// <summary>
    /// Service is operating normally
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// Service is operational but with performance degradation
    /// </summary>
    Degraded = 1,

    /// <summary>
    /// Service has significant issues but is still functional
    /// </summary>
    Unhealthy = 2,

    /// <summary>
    /// Service is unavailable or critically impaired
    /// </summary>
    Critical = 3
}
