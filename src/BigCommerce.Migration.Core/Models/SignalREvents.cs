using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// SignalR event for real-time quota updates
/// </summary>
public class QuotaUpdateEvent : ProgressEvent
{
    /// <summary>
    /// Store identifier for this quota update
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total API quota available for this store
    /// </summary>
    public int TotalQuota { get; set; }

    /// <summary>
    /// Number of API tokens remaining in the quota
    /// </summary>
    public int RemainingTokens { get; set; }

    /// <summary>
    /// Current quota utilization as a percentage
    /// </summary>
    public double UtilizationPercent { get; set; }

    /// <summary>
    /// Current health status of the quota (Healthy, Warning, Critical)
    /// </summary>
    public string HealthStatus { get; set; } = string.Empty;

    /// <summary>
    /// When the current quota window will reset
    /// </summary>
    public DateTimeOffset QuotaResetTime { get; set; }

    /// <summary>
    /// Last time the quota was updated
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Number of tokens that can be safely allocated
    /// </summary>
    public int SafeTokens { get; set; }

    /// <summary>
    /// Age of the current quota data
    /// </summary>
    public TimeSpan DataAge { get; set; }
}

/// <summary>
/// Options for creating QuotaUpdateEvent
/// </summary>
/// <summary>
/// Options for creating QuotaUpdateEvent
/// </summary>
public class QuotaUpdateOptions
{
    /// <summary>
    /// Total API quota available for this store
    /// </summary>
    public int TotalQuota { get; set; }

    /// <summary>
    /// Number of API tokens remaining in the quota
    /// </summary>
    public int RemainingTokens { get; set; }

    /// <summary>
    /// Current quota utilization as a percentage
    /// </summary>
    public double UtilizationPercent { get; set; }

    /// <summary>
    /// Current health status of the quota (Healthy, Warning, Critical)
    /// </summary>
    public string? HealthStatus { get; set; }

    /// <summary>
    /// When the current quota window will reset
    /// </summary>
    public DateTimeOffset QuotaResetTime { get; set; }

    /// <summary>
    /// Last time the quota was updated
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Number of tokens that can be safely allocated
    /// </summary>
    public int SafeTokens { get; set; }

    /// <summary>
    /// Age of the current quota data
    /// </summary>
    public TimeSpan DataAge { get; set; }
}

/// <summary>
/// SignalR event for predictive rate limiting status
/// </summary>
public class PredictiveRateLimitEvent : ProgressEvent
{
    /// <summary>
    /// Store identifier for this rate limit event
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Instance identifier that generated this event
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of tokens allocated to this instance
    /// </summary>
    public int AllocatedTokens { get; set; }

    /// <summary>
    /// Number of tokens still available for use
    /// </summary>
    public int AvailableTokens { get; set; }

    /// <summary>
    /// Number of tokens consumed by this instance
    /// </summary>
    public int TokensConsumed { get; set; }

    /// <summary>
    /// Efficiency of token allocation for this instance
    /// </summary>
    public double AllocationEfficiency { get; set; }

    /// <summary>
    /// When the current token allocation expires
    /// </summary>
    public DateTimeOffset? TokenExpiresAt { get; set; }
    
    /// <summary>
    /// Number of active instances currently coordinating
    /// </summary>
    public int ActiveInstanceCount { get; set; }

    /// <summary>
    /// Health status of instance coordination
    /// </summary>
    public string CoordinationHealthStatus { get; set; } = string.Empty;
    
    /// <summary>
    /// Current status of the circuit breaker
    /// </summary>
    public string CircuitBreakerStatus { get; set; } = string.Empty;

    /// <summary>
    /// Overall health score (0.0 to 1.0)
    /// </summary>
    public double OverallHealthScore { get; set; }

    /// <summary>
    /// Whether requests can be processed
    /// </summary>
    public bool CanProcessRequests { get; set; }
}

/// <summary>
/// Options for creating PredictiveRateLimitEvent
/// </summary>
/// <summary>
/// Options for creating PredictiveRateLimitEvent
/// </summary>
public class PredictiveRateLimitOptions
{
    /// <summary>
    /// Instance identifier that generated this event
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Number of tokens allocated to this instance
    /// </summary>
    public int AllocatedTokens { get; set; }

    /// <summary>
    /// Number of tokens still available for use
    /// </summary>
    public int AvailableTokens { get; set; }

    /// <summary>
    /// Number of tokens consumed by this instance
    /// </summary>
    public int TokensConsumed { get; set; }

    /// <summary>
    /// Efficiency of token allocation for this instance
    /// </summary>
    public double AllocationEfficiency { get; set; }

    /// <summary>
    /// When the current token allocation expires
    /// </summary>
    public DateTimeOffset? TokenExpiresAt { get; set; }

    /// <summary>
    /// Number of active instances currently coordinating
    /// </summary>
    public int ActiveInstanceCount { get; set; }

    /// <summary>
    /// Health status of instance coordination
    /// </summary>
    public string? CoordinationHealthStatus { get; set; }

    /// <summary>
    /// Current status of the circuit breaker
    /// </summary>
    public string? CircuitBreakerStatus { get; set; }

    /// <summary>
    /// Overall health score (0.0 to 1.0)
    /// </summary>
    public double OverallHealthScore { get; set; }

    /// <summary>
    /// Whether requests can be processed
    /// </summary>
    public bool CanProcessRequests { get; set; }
}

/// <summary>
/// SignalR event for system health monitoring
/// </summary>
public class SystemHealthEvent : ProgressEvent
{
    /// <summary>
    /// Store identifier for this health event
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Overall health status of the system
    /// </summary>
    public string OverallHealthStatus { get; set; } = string.Empty;

    /// <summary>
    /// Overall health score (0.0 to 1.0)
    /// </summary>
    public double OverallHealthScore { get; set; }
    
    /// <summary>
    /// Current health status of the quota
    /// </summary>
    public string QuotaHealthStatus { get; set; } = string.Empty;

    /// <summary>
    /// Efficiency of instance coordination
    /// </summary>
    public double CoordinationEfficiency { get; set; }

    /// <summary>
    /// Number of active instances
    /// </summary>
    public int ActiveInstanceCount { get; set; }
    
    /// <summary>
    /// Whether the system is in fallback mode
    /// </summary>
    public bool IsInFallbackMode { get; set; }

    /// <summary>
    /// Reason for entering fallback mode
    /// </summary>
    public string? FallbackReason { get; set; }

    /// <summary>
    /// Current health trend (Improving, Stable, Degrading)
    /// </summary>
    public string HealthTrend { get; set; } = string.Empty;

    /// <summary>
    /// List of recommended actions to improve health
    /// </summary>
    public List<string> RecommendedActions { get; set; } = new();
}

/// <summary>
/// Options for creating SystemHealthEvent
/// </summary>
/// <summary>
/// Options for creating SystemHealthEvent
/// </summary>
public class SystemHealthOptions
{
    /// <summary>
    /// Overall health status of the system
    /// </summary>
    public string? OverallHealthStatus { get; set; }

    /// <summary>
    /// Overall health score (0.0 to 1.0)
    /// </summary>
    public double OverallHealthScore { get; set; }

    /// <summary>
    /// Current health status of the quota
    /// </summary>
    public string? QuotaHealthStatus { get; set; }

    /// <summary>
    /// Efficiency of instance coordination
    /// </summary>
    public double CoordinationEfficiency { get; set; }

    /// <summary>
    /// Number of active instances
    /// </summary>
    public int ActiveInstanceCount { get; set; }

    /// <summary>
    /// Whether the system is in fallback mode
    /// </summary>
    public bool IsInFallbackMode { get; set; }

    /// <summary>
    /// Reason for entering fallback mode
    /// </summary>
    public string? FallbackReason { get; set; }

    /// <summary>
    /// Current health trend (Improving, Stable, Degrading)
    /// </summary>
    public string? HealthTrend { get; set; }

    /// <summary>
    /// List of recommended actions to improve health
    /// </summary>
    public List<string>? RecommendedActions { get; set; }
}