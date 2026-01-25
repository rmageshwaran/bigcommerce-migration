namespace BigCommerce.Migration.Core.Models.RateLimiting;

/// <summary>
/// Performance metrics for coordination health monitoring
/// Tracks operation latency, conflict rates, and system performance indicators
/// </summary>
public class PerformanceMetrics
{
    /// <summary>
    /// Store identifier for which these performance metrics apply
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Timestamp of the last update to these performance metrics
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    // Operation metrics
    /// <summary>
    /// Total number of operations performed
    /// </summary>
    public long TotalOperations { get; set; } = 0;
    
    /// <summary>
    /// Number of operations that completed successfully
    /// </summary>
    public long SuccessfulOperations { get; set; } = 0;
    
    /// <summary>
    /// Number of operations that failed
    /// </summary>
    public long FailedOperations { get; set; } = 0;
    
    /// <summary>
    /// Success rate of operations (0.0 to 1.0)
    /// </summary>
    public double SuccessRate => TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations : 0.0;

    // Latency metrics
    /// <summary>
    /// Average latency across all operations
    /// </summary>
    public TimeSpan AverageOperationLatency { get; set; } = TimeSpan.Zero;
    
    /// <summary>
    /// Maximum latency observed for any operation
    /// </summary>
    public TimeSpan MaxOperationLatency { get; set; } = TimeSpan.Zero;
    
    /// <summary>
    /// Minimum latency observed for any operation
    /// </summary>
    public TimeSpan MinOperationLatency { get; set; } = TimeSpan.MaxValue;

    // Conflict metrics
    /// <summary>
    /// Total number of ETag conflicts encountered during operations
    /// </summary>
    public long TotalETagConflicts { get; set; } = 0;
    
    /// <summary>
    /// Rate of ETag conflicts relative to total operations (0.0 to 1.0)
    /// </summary>
    public double ETagConflictRate => TotalOperations > 0 ? (double)TotalETagConflicts / TotalOperations : 0.0;

    // Throughput metrics
    /// <summary>
    /// Current operations per second throughput
    /// </summary>
    public double OperationsPerSecond { get; set; } = 0.0;
    
    /// <summary>
    /// Operations per minute calculated from operations per second
    /// </summary>
    public double OperationsPerMinute => OperationsPerSecond * 60;

    // Resource utilization
    /// <summary>
    /// Average memory usage during operations (in MB)
    /// </summary>
    public double AverageMemoryUsage { get; set; } = 0.0;
    
    /// <summary>
    /// Average CPU usage during operations (0.0 to 100.0)
    /// </summary>
    public double AverageCpuUsage { get; set; } = 0.0;

    // Historical tracking
    private readonly Queue<OperationSample> _operationSamples = new();
    private readonly Queue<LatencySample> _latencySamples = new();
    private readonly object _samplesLock = new object();

    /// <summary>
    /// Records an operation for metrics tracking
    /// </summary>
    public void RecordOperation(CoordinationOperation operation, bool success, TimeSpan? latency = null)
    {
        lock (_samplesLock)
        {
            TotalOperations++;
            if (success)
                SuccessfulOperations++;
            else
                FailedOperations++;

            // Record operation sample
            _operationSamples.Enqueue(new OperationSample
            {
                Timestamp = DateTimeOffset.UtcNow,
                Operation = operation,
                Success = success,
                Latency = latency ?? TimeSpan.Zero
            });

            // Keep only last 1000 samples
            while (_operationSamples.Count > 1000)
            {
                _operationSamples.Dequeue();
            }

            // Update latency if provided
            if (latency.HasValue)
            {
                RecordLatency(latency.Value);
            }

            // Update throughput
            UpdateThroughputMetrics();
            
            LastUpdated = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Records operation latency
    /// </summary>
    public void RecordLatency(TimeSpan latency)
    {
        lock (_samplesLock)
        {
            _latencySamples.Enqueue(new LatencySample
            {
                Timestamp = DateTimeOffset.UtcNow,
                Latency = latency
            });

            // Keep only last 500 latency samples
            while (_latencySamples.Count > 500)
            {
                _latencySamples.Dequeue();
            }

            // Update latency metrics
            if (latency > MaxOperationLatency)
                MaxOperationLatency = latency;

            if (latency < MinOperationLatency)
                MinOperationLatency = latency;

            // Calculate average latency from recent samples
            var recentSamples = _latencySamples.TakeLast(100).ToList();
            if (recentSamples.Count > 0)
            {
                AverageOperationLatency = TimeSpan.FromTicks((long)recentSamples.Average(s => s.Latency.Ticks));
            }
        }
    }

    /// <summary>
    /// Records ETag conflict for tracking
    /// </summary>
    public void RecordETagConflict()
    {
        TotalETagConflicts++;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates metrics from system health state
    /// </summary>
    public void UpdateFromHealthState(SystemHealthState healthState)
    {
        // Update conflict metrics from health state
        if (healthState.TotalETagConflicts > TotalETagConflicts)
        {
            TotalETagConflicts = healthState.TotalETagConflicts;
        }

        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets operation breakdown by type
    /// </summary>
    public Dictionary<CoordinationOperation, long> GetOperationBreakdown()
    {
        lock (_samplesLock)
        {
            return _operationSamples
                .GroupBy(s => s.Operation)
                .ToDictionary(g => g.Key, g => (long)g.Count());
        }
    }

    /// <summary>
    /// Gets recent performance trend
    /// </summary>
    public PerformanceTrend GetPerformanceTrend()
    {
        lock (_samplesLock)
        {
            var recent = _operationSamples.TakeLast(50).ToList();
            if (recent.Count < 10)
                return PerformanceTrend.Stable;

            var recentSuccess = recent.TakeLast(25).Count(s => s.Success);
            var olderSuccess = recent.Take(25).Count(s => s.Success);

            var recentRate = recent.Count > 0 ? (double)recentSuccess / Math.Min(25, recent.Count) : 0.0;
            var olderRate = recent.Count > 25 ? (double)olderSuccess / 25 : recentRate;

            var improvement = recentRate - olderRate;

            return improvement switch
            {
                > 0.1 => PerformanceTrend.Improving,
                < -0.1 => PerformanceTrend.Degrading,
                _ => PerformanceTrend.Stable
            };
        }
    }

    /// <summary>
    /// Gets latency percentiles
    /// </summary>
    public LatencyPercentiles GetLatencyPercentiles()
    {
        lock (_samplesLock)
        {
            var latencies = _latencySamples.Select(s => s.Latency.TotalMilliseconds).OrderBy(l => l).ToList();
            
            if (latencies.Count == 0)
            {
                return new LatencyPercentiles();
            }

            return new LatencyPercentiles
            {
                P50 = TimeSpan.FromMilliseconds(GetPercentile(latencies, 0.5)),
                P90 = TimeSpan.FromMilliseconds(GetPercentile(latencies, 0.9)),
                P95 = TimeSpan.FromMilliseconds(GetPercentile(latencies, 0.95)),
                P99 = TimeSpan.FromMilliseconds(GetPercentile(latencies, 0.99))
            };
        }
    }

    /// <summary>
    /// Checks if performance is degraded
    /// </summary>
    public bool IsPerformanceDegraded()
    {
        return SuccessRate < 0.9 || 
               AverageOperationLatency > TimeSpan.FromSeconds(2) ||
               ETagConflictRate > 0.1;
    }

    /// <summary>
    /// Gets performance recommendations
    /// </summary>
    public List<string> GetPerformanceRecommendations()
    {
        var recommendations = new List<string>();

        if (SuccessRate < 0.9)
        {
            recommendations.Add($"Low success rate ({SuccessRate:P1}) - investigate error patterns");
        }

        if (AverageOperationLatency > TimeSpan.FromSeconds(2))
        {
            recommendations.Add($"High latency ({AverageOperationLatency.TotalMilliseconds:F0}ms) - check system load");
        }

        if (ETagConflictRate > 0.1)
        {
            recommendations.Add($"High conflict rate ({ETagConflictRate:P1}) - consider reducing concurrency");
        }

        if (OperationsPerSecond > 100)
        {
            recommendations.Add("High operation rate - monitor for throttling");
        }

        var trend = GetPerformanceTrend();
        if (trend == PerformanceTrend.Degrading)
        {
            recommendations.Add("Performance trending downward - investigate recent changes");
        }

        return recommendations;
    }

    /// <summary>
    /// Creates a clone of current metrics
    /// </summary>
    public PerformanceMetrics Clone()
    {
        lock (_samplesLock)
        {
            return new PerformanceMetrics
            {
                StoreId = StoreId,
                LastUpdated = LastUpdated,
                TotalOperations = TotalOperations,
                SuccessfulOperations = SuccessfulOperations,
                FailedOperations = FailedOperations,
                AverageOperationLatency = AverageOperationLatency,
                MaxOperationLatency = MaxOperationLatency,
                MinOperationLatency = MinOperationLatency,
                TotalETagConflicts = TotalETagConflicts,
                OperationsPerSecond = OperationsPerSecond,
                AverageMemoryUsage = AverageMemoryUsage,
                AverageCpuUsage = AverageCpuUsage
            };
        }
    }

    /// <summary>
    /// Updates throughput metrics based on recent samples
    /// </summary>
    private void UpdateThroughputMetrics()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-10);
        var recentOperations = _operationSamples.Where(s => s.Timestamp >= cutoff).Count();
        OperationsPerSecond = recentOperations / 10.0;
    }

    /// <summary>
    /// Calculates percentile from sorted list
    /// </summary>
    private double GetPercentile(List<double> sortedList, double percentile)
    {
        if (sortedList.Count == 0) return 0.0;
        
        var index = (int)Math.Ceiling(sortedList.Count * percentile) - 1;
        index = Math.Max(0, Math.Min(sortedList.Count - 1, index));
        
        return sortedList[index];
    }

    /// <summary>
    /// Returns a string representation of the performance metrics
    /// </summary>
    /// <returns>String representation with key performance indicators</returns>
    public override string ToString()
    {
        return $"PerformanceMetrics[{StoreId}]: {SuccessfulOperations}/{TotalOperations} ({SuccessRate:P1}), " +
               $"Latency: {AverageOperationLatency.TotalMilliseconds:F0}ms, " +
               $"Conflicts: {ETagConflictRate:P1}, " +
               $"Rate: {OperationsPerSecond:F1}/s";
    }
}

/// <summary>
/// Operation sample for historical tracking
/// </summary>
public class OperationSample
{
    /// <summary>
    /// Timestamp when the operation was recorded
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
    
    /// <summary>
    /// Type of coordination operation performed
    /// </summary>
    public CoordinationOperation Operation { get; set; }
    
    /// <summary>
    /// Whether the operation completed successfully
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Duration of the operation
    /// </summary>
    public TimeSpan Latency { get; set; }
}

/// <summary>
/// Latency sample for performance tracking
/// </summary>
public class LatencySample
{
    /// <summary>
    /// Timestamp when the latency was recorded
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
    
    /// <summary>
    /// Latency measurement for the operation
    /// </summary>
    public TimeSpan Latency { get; set; }
}

/// <summary>
/// Latency percentiles for detailed performance analysis
/// </summary>
public class LatencyPercentiles
{
    /// <summary>
    /// 50th percentile latency (median)
    /// </summary>
    public TimeSpan P50 { get; set; } = TimeSpan.Zero;
    
    /// <summary>
    /// 90th percentile latency
    /// </summary>
    public TimeSpan P90 { get; set; } = TimeSpan.Zero;
    
    /// <summary>
    /// 95th percentile latency
    /// </summary>
    public TimeSpan P95 { get; set; } = TimeSpan.Zero;
    
    /// <summary>
    /// 99th percentile latency
    /// </summary>
    public TimeSpan P99 { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Returns a string representation of the latency percentiles
    /// </summary>
    /// <returns>String representation with percentile values</returns>
    public override string ToString()
    {
        return $"Latency Percentiles: P50={P50.TotalMilliseconds:F0}ms, " +
               $"P90={P90.TotalMilliseconds:F0}ms, " +
               $"P95={P95.TotalMilliseconds:F0}ms, " +
               $"P99={P99.TotalMilliseconds:F0}ms";
    }
}

/// <summary>
/// Performance trend enumeration
/// </summary>
public enum PerformanceTrend
{
    /// <summary>
    /// Performance is improving over time
    /// </summary>
    Improving,
    
    /// <summary>
    /// Performance is stable and consistent
    /// </summary>
    Stable,
    
    /// <summary>
    /// Performance is degrading over time
    /// </summary>
    Degrading
}