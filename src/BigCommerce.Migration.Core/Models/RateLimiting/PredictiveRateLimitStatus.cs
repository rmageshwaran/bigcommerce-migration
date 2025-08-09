namespace BigCommerce.Migration.Core.Models.RateLimiting;

/// <summary>
/// Comprehensive status information for predictive rate limiting system
/// Provides complete visibility into quota tracking, instance coordination, and token consensus
/// </summary>
public class PredictiveRateLimitStatus
{
    /// <summary>
    /// Store identifier for this rate limit status
    /// </summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// Instance identifier that generated this status
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when this status was generated
    /// </summary>
    public DateTimeOffset StatusTimestamp { get; set; }

    /// <summary>
    /// Total API quota available for this store
    /// </summary>
    public int TotalQuota { get; set; }

    /// <summary>
    /// Number of API tokens remaining in the quota
    /// </summary>
    public int RemainingTokens { get; set; }

    /// <summary>
    /// Number of tokens that can be safely allocated based on health status
    /// </summary>
    public int SafeTokens { get; set; }

    /// <summary>
    /// Current quota utilization as a percentage
    /// </summary>
    public double QuotaUtilizationPercent { get; set; }

    /// <summary>
    /// Current health status of the quota (Healthy, Warning, Critical)
    /// </summary>
    public string QuotaHealthStatus { get; set; } = string.Empty;

    /// <summary>
    /// When the current quota window will reset
    /// </summary>
    public DateTimeOffset QuotaResetTime { get; set; }

    /// <summary>
    /// Age of the current quota data
    /// </summary>
    public TimeSpan DataAge { get; set; }

    /// <summary>
    /// Number of active instances currently coordinating
    /// </summary>
    public int ActiveInstanceCount { get; set; }

    /// <summary>
    /// Health status of instance coordination (Healthy, Warning, Critical)
    /// </summary>
    public string CoordinationHealthStatus { get; set; } = string.Empty;

    /// <summary>
    /// Efficiency of instance coordination as a percentage
    /// </summary>
    public double CoordinationEfficiency { get; set; }

    /// <summary>
    /// Number of tokens allocated to this instance
    /// </summary>
    public int AllocatedTokens { get; set; }

    /// <summary>
    /// Number of tokens still available for use by this instance
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
    /// Total number of tokens allocated across all instances
    /// </summary>
    public int TotalAllocatedTokens { get; set; }

    /// <summary>
    /// Health status of token consensus (Healthy, Warning, Critical)
    /// </summary>
    public string ConsensusHealthStatus { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the circuit breaker (Closed, HalfOpen, Open)
    /// </summary>
    public string CircuitBreakerStatus { get; set; } = string.Empty;

    /// <summary>
    /// Number of consecutive failures that triggered circuit breaker
    /// </summary>
    public int CircuitBreakerFailureCount { get; set; }

    /// <summary>
    /// Whether predictive rate limiting is enabled
    /// </summary>
    public bool IsPredictiveEnabled { get; set; }

    /// <summary>
    /// Whether multi-instance coordination is enabled
    /// </summary>
    public bool IsCoordinationEnabled { get; set; }

    /// <summary>
    /// Whether real-time quota tracking is enabled
    /// </summary>
    public bool IsQuotaTrackingEnabled { get; set; }

    /// <summary>
    /// Calculates overall system health score (0.0 to 1.0)
    /// </summary>
    public double GetOverallHealthScore()
    {
        var scores = new List<double>();

        // Quota health (40% weight)
        var quotaScore = QuotaHealthStatus.ToLowerInvariant() switch
        {
            "healthy" => 1.0,
            "warning" => 0.6,
            "critical" => 0.2,
            _ => 0.5
        };
        scores.Add(quotaScore * 0.4);

        // Coordination health (30% weight)
        var coordinationScore = CoordinationHealthStatus.ToLowerInvariant() switch
        {
            "healthy" => 1.0,
            "warning" => 0.6,
            "critical" => 0.2,
            _ => 0.5
        };
        scores.Add(coordinationScore * 0.3);

        // Consensus health (20% weight)
        var consensusScore = ConsensusHealthStatus.ToLowerInvariant() switch
        {
            "healthy" => 1.0,
            "warning" => 0.6,
            "critical" => 0.2,
            _ => 0.5
        };
        scores.Add(consensusScore * 0.2);

        // Circuit breaker health (10% weight)
        var circuitScore = CircuitBreakerStatus.ToLowerInvariant() switch
        {
            "closed" => 1.0,
            "halfopen" => 0.5,
            "open" => 0.1,
            _ => 0.5
        };
        scores.Add(circuitScore * 0.1);

        return Math.Max(0.0, Math.Min(1.0, scores.Sum()));
    }

    /// <summary>
    /// Gets overall system health status based on health score
    /// </summary>
    public string GetOverallHealthStatus()
    {
        var score = GetOverallHealthScore();
        
        return score switch
        {
            >= 0.8 => "Healthy",
            >= 0.5 => "Warning",
            _ => "Critical"
        };
    }

    /// <summary>
    /// Checks if the system can safely process requests
    /// </summary>
    public bool CanProcessRequests()
    {
        // Must have available tokens and circuit breaker must not be open
        return AvailableTokens > 0 && 
               CircuitBreakerStatus.ToLowerInvariant() != "open" &&
               QuotaHealthStatus.ToLowerInvariant() != "critical";
    }

    /// <summary>
    /// Gets token efficiency percentage for this instance
    /// </summary>
    public double GetTokenEfficiencyPercent()
    {
        if (AllocatedTokens <= 0) return 0.0;
        return (double)TokensConsumed / AllocatedTokens * 100;
    }

    /// <summary>
    /// Gets time until token allocation expires
    /// </summary>
    public TimeSpan GetTimeUntilTokenExpiry()
    {
        if (!TokenExpiresAt.HasValue) return TimeSpan.Zero;
        return TokenExpiresAt.Value - DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Checks if token allocation is about to expire (within 30 seconds)
    /// </summary>
    public bool IsTokenAllocationExpiringSoon()
    {
        var timeUntilExpiry = GetTimeUntilTokenExpiry();
        return timeUntilExpiry > TimeSpan.Zero && timeUntilExpiry <= TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Gets quota utilization level description
    /// </summary>
    public string GetQuotaUtilizationLevel()
    {
        return QuotaUtilizationPercent switch
        {
            >= 95 => "Critical",
            >= 80 => "High",
            >= 60 => "Moderate",
            >= 30 => "Low",
            _ => "Minimal"
        };
    }

    /// <summary>
    /// Gets recommended actions based on current status
    /// </summary>
    public List<string> GetRecommendedActions()
    {
        var actions = new List<string>();

        // Quota-based recommendations
        if (QuotaUtilizationPercent >= 95)
        {
            actions.Add("URGENT: Consider reducing request rate - quota critically high");
        }
        else if (QuotaUtilizationPercent >= 80)
        {
            actions.Add("WARNING: Monitor quota usage closely");
        }

        // Token allocation recommendations
        if (AvailableTokens <= 0)
        {
            actions.Add("No available tokens - requests will be throttled");
        }
        else if (AvailableTokens <= 5)
        {
            actions.Add("Low token count - consider requesting more tokens");
        }

        // Circuit breaker recommendations
        if (CircuitBreakerStatus.ToLowerInvariant() == "open")
        {
            actions.Add("Circuit breaker open - requests being rejected for protection");
        }
        else if (CircuitBreakerFailureCount >= 3)
        {
            actions.Add("High failure count - monitor for potential issues");
        }

        // Coordination recommendations
        if (ActiveInstanceCount <= 1 && IsCoordinationEnabled)
        {
            actions.Add("Single instance detected - coordination benefits limited");
        }
        else if (CoordinationEfficiency < 50 && ActiveInstanceCount > 1)
        {
            actions.Add("Low coordination efficiency - review load balancing");
        }

        // Token expiry recommendations
        if (IsTokenAllocationExpiringSoon())
        {
            actions.Add("Token allocation expiring soon - renewal may be needed");
        }

        // Data freshness recommendations
        if (DataAge > TimeSpan.FromMinutes(5))
        {
            actions.Add("Quota data is stale - consider refreshing");
        }

        return actions;
    }

    /// <summary>
    /// Returns a summary string of the current status
    /// </summary>
    public override string ToString()
    {
        return $"PredictiveRateLimit[{StoreId}]: " +
               $"Health={GetOverallHealthStatus()}, " +
               $"Quota={RemainingTokens}/{TotalQuota} ({QuotaUtilizationPercent:F1}%), " +
               $"Tokens={AvailableTokens}/{AllocatedTokens}, " +
               $"Instances={ActiveInstanceCount}, " +
               $"Circuit={CircuitBreakerStatus}";
    }
}