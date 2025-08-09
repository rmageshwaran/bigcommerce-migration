namespace BigCommerce.Migration.Core.Models.RateLimiting;

/// <summary>
/// Quota health metrics for distributed quota tracking
/// </summary>
public class QuotaHealthMetrics
{
    /// <summary>
    /// Store identifier for which these quota metrics apply
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Current health status of the quota (Healthy, Warning, Critical)
    /// </summary>
    public QuotaHealthStatus HealthStatus { get; set; } = QuotaHealthStatus.Critical;
    
    /// <summary>
    /// Percentage of quota currently utilized (0.0 to 100.0)
    /// </summary>
    public double QuotaUtilizationPercent { get; set; }
    
    /// <summary>
    /// Number of tokens that can be safely consumed without hitting limits
    /// </summary>
    public int SafeTokens { get; set; }
    
    /// <summary>
    /// Total quota limit for the current time window
    /// </summary>
    public int TotalQuota { get; set; }
    
    /// <summary>
    /// Number of tokens remaining in the current quota window
    /// </summary>
    public int RemainingTokens { get; set; }
    
    /// <summary>
    /// Time when the current quota window will reset
    /// </summary>
    public DateTimeOffset QuotaResetTime { get; set; }
    
    /// <summary>
    /// Age of the quota data since last update from BigCommerce API
    /// </summary>
    public TimeSpan DataAge { get; set; }
    
    /// <summary>
    /// Number of consecutive failures in quota tracking operations
    /// </summary>
    public int ConsecutiveFailures { get; set; }
}

/// <summary>
/// Coordination health metrics for instance coordination
/// </summary>
public class CoordinationHealthMetrics
{
    /// <summary>
    /// Store identifier for which these coordination metrics apply
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of active application instances currently coordinating
    /// </summary>
    public int ActiveInstanceCount { get; set; }
    
    /// <summary>
    /// Total number of requests processed across all instances
    /// </summary>
    public long TotalRequestsProcessed { get; set; }
    
    /// <summary>
    /// Average load factor across all active instances (0.0 to 1.0)
    /// </summary>
    public double AverageLoadFactor { get; set; }
    
    /// <summary>
    /// Overall health status of the coordination system
    /// </summary>
    public string HealthStatus { get; set; } = string.Empty;
    
    /// <summary>
    /// Time since the last heartbeat was received from any instance
    /// </summary>
    public TimeSpan LastHeartbeatAge { get; set; }
    
    /// <summary>
    /// Efficiency score of inter-instance coordination (0.0 to 1.0)
    /// </summary>
    public double CoordinationEfficiency { get; set; }
}

/// <summary>
/// Consensus health metrics for distributed token allocation
/// </summary>
public class ConsensusHealthMetrics
{
    /// <summary>
    /// Store identifier for which these consensus metrics apply
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of currently active token allocations across instances
    /// </summary>
    public int ActiveAllocations { get; set; }
    
    /// <summary>
    /// Number of token allocations that have expired and need cleanup
    /// </summary>
    public int ExpiredAllocations { get; set; }
    
    /// <summary>
    /// Total number of tokens allocated across all instances
    /// </summary>
    public int TotalAllocatedTokens { get; set; }
    
    /// <summary>
    /// Total number of tokens available for consumption across instances
    /// </summary>
    public int TotalAvailableTokens { get; set; }
    
    /// <summary>
    /// Total number of tokens actually consumed by all instances
    /// </summary>
    public int TotalConsumedTokens { get; set; }
    
    /// <summary>
    /// Average efficiency of token allocation across instances (0.0 to 1.0)
    /// </summary>
    public double AverageAllocationEfficiency { get; set; }
    
    /// <summary>
    /// Total number of ETag conflicts encountered during consensus operations
    /// </summary>
    public int TotalETagConflicts { get; set; }
    
    /// <summary>
    /// Overall health score of the consensus system (0.0 to 1.0)
    /// </summary>
    public double HealthScore { get; set; }
    
    /// <summary>
    /// Current health status of the consensus system
    /// </summary>
    public string HealthStatus { get; set; } = string.Empty;
}

/// <summary>
/// Monitoring statistics for predictive rate limiting monitoring
/// </summary>
public class MonitoringStatistics
{
    /// <summary>
    /// Store identifier for which these monitoring statistics apply
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether monitoring is currently active for this store
    /// </summary>
    public bool IsActive { get; set; }
    
    /// <summary>
    /// Time when monitoring was started for this store
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }
    
    /// <summary>
    /// Total number of SignalR events published for this store
    /// </summary>
    public long TotalEventsPublished { get; set; }
    
    /// <summary>
    /// Time of the last SignalR event published for this store
    /// </summary>
    public DateTimeOffset? LastEventPublished { get; set; }
    
    /// <summary>
    /// Breakdown of event counts by event type
    /// </summary>
    public Dictionary<string, long> EventBreakdown { get; set; } = new();

    /// <summary>
    /// Gets the total duration that monitoring has been active
    /// </summary>
    /// <returns>Duration since monitoring started, or null if not started</returns>
    public TimeSpan? GetMonitoringDuration()
    {
        return StartedAt.HasValue ? DateTimeOffset.UtcNow - StartedAt.Value : null;
    }

    /// <summary>
    /// Calculates the average number of events published per minute
    /// </summary>
    /// <returns>Events per minute rate, or 0.0 if no monitoring duration</returns>
    public double GetEventsPerMinute()
    {
        var duration = GetMonitoringDuration();
        if (!duration.HasValue || duration.Value.TotalMinutes <= 0)
            return 0.0;

        return TotalEventsPublished / duration.Value.TotalMinutes;
    }
}