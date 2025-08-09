namespace BigCommerce.Migration.Core.Models.RateLimiting;

/// <summary>
/// Comprehensive system health state for distributed rate limiting coordination
/// Tracks health across quota tracking, instance coordination, and token consensus
/// </summary>
public class SystemHealthState
{
    /// <summary>
    /// Store identifier for this health state
    /// </summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// Last time this health state was updated
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// Overall health status of the rate limiting system
    /// </summary>
    public SystemHealthStatus OverallHealthStatus { get; set; } = SystemHealthStatus.Unknown;

    /// <summary>
    /// Overall health score (0.0 to 1.0) calculated from all metrics
    /// </summary>
    public double OverallHealthScore { get; set; } = 0.0;
    
    /// <summary>
    /// Current health status of the quota tracking system
    /// </summary>
    public QuotaHealthStatus QuotaHealthStatus { get; set; } = QuotaHealthStatus.Critical;

    /// <summary>
    /// Age of the current quota data
    /// </summary>
    public TimeSpan QuotaDataAge { get; set; } = TimeSpan.MaxValue;

    /// <summary>
    /// Current quota utilization as a percentage
    /// </summary>
    public double QuotaUtilizationPercent { get; set; } = 0.0;

    /// <summary>
    /// Number of tokens that can be safely allocated based on health status
    /// </summary>
    public int SafeTokens { get; set; } = 0;
    
    /// <summary>
    /// Number of active instances currently coordinating
    /// </summary>
    public int ActiveInstanceCount { get; set; } = 0;

    /// <summary>
    /// Efficiency of instance coordination as a percentage
    /// </summary>
    public double CoordinationEfficiency { get; set; } = 0.0;

    /// <summary>
    /// Number of consecutive coordination failures
    /// </summary>
    public int CoordinationFailureCount { get; set; } = 0;

    /// <summary>
    /// Age of the most recent instance heartbeat
    /// </summary>
    public TimeSpan LastHeartbeatAge { get; set; } = TimeSpan.MaxValue;
    
    /// <summary>
    /// Total number of tokens allocated across all instances
    /// </summary>
    public int TotalAllocatedTokens { get; set; } = 0;

    /// <summary>
    /// Total number of tokens still available across all instances
    /// </summary>
    public int TotalAvailableTokens { get; set; } = 0;

    /// <summary>
    /// Average token allocation efficiency across all instances
    /// </summary>
    public double AverageAllocationEfficiency { get; set; } = 0.0;

    /// <summary>
    /// Total number of ETag conflicts during token allocation
    /// </summary>
    public int TotalETagConflicts { get; set; } = 0;
    
    /// <summary>
    /// Whether the system is currently in fallback mode
    /// </summary>
    public bool IsInFallbackMode { get; set; } = false;

    /// <summary>
    /// Reason why fallback mode was triggered
    /// </summary>
    public FallbackReason? FallbackReason { get; set; }

    /// <summary>
    /// When fallback mode was triggered
    /// </summary>
    public DateTimeOffset? FallbackTriggeredAt { get; set; }

    /// <summary>
    /// Number of times fallback mode has been triggered
    /// </summary>
    public int FallbackCount { get; set; } = 0;
    
    // Health history for trend analysis
    private readonly Queue<HealthSnapshot> _healthHistory = new();
    private readonly object _historyLock = new object();
    
    /// <summary>
    /// Updates health state from subsystem health metrics
    /// </summary>
    public void UpdateHealth(QuotaHealthMetrics quotaHealth, CoordinationHealthMetrics coordinationHealth, ConsensusHealthMetrics consensusHealth)
    {
        LastUpdated = DateTimeOffset.UtcNow;
        
        // Update quota health
        QuotaHealthStatus = quotaHealth.HealthStatus;
        QuotaDataAge = quotaHealth.DataAge;
        QuotaUtilizationPercent = quotaHealth.QuotaUtilizationPercent;
        SafeTokens = quotaHealth.SafeTokens;
        
        // Update coordination health
        ActiveInstanceCount = coordinationHealth.ActiveInstanceCount;
        CoordinationEfficiency = coordinationHealth.CoordinationEfficiency;
        LastHeartbeatAge = coordinationHealth.LastHeartbeatAge;
        
        // Update consensus health
        TotalAllocatedTokens = consensusHealth.TotalAllocatedTokens;
        TotalAvailableTokens = consensusHealth.TotalAvailableTokens;
        AverageAllocationEfficiency = consensusHealth.AverageAllocationEfficiency;
        TotalETagConflicts = consensusHealth.TotalETagConflicts;
        
        // Calculate overall health
        CalculateOverallHealth();
        
        // Add to health history
        AddHealthSnapshot();
    }
    
    /// <summary>
    /// Enters fallback mode with specified reason
    /// </summary>
    public void EnterFallbackMode(FallbackReason reason)
    {
        if (!IsInFallbackMode)
        {
            IsInFallbackMode = true;
            FallbackReason = reason;
            FallbackTriggeredAt = DateTimeOffset.UtcNow;
            FallbackCount++;
            OverallHealthStatus = SystemHealthStatus.Critical;
        }
    }
    
    /// <summary>
    /// Exits fallback mode
    /// </summary>
    public void ExitFallbackMode()
    {
        if (IsInFallbackMode)
        {
            IsInFallbackMode = false;
            FallbackReason = null;
            FallbackTriggeredAt = null;
            
            // Recalculate health without fallback influence
            CalculateOverallHealth();
        }
    }
    
    /// <summary>
    /// Gets health trend over time
    /// </summary>
    public HealthTrend GetHealthTrend()
    {
        lock (_historyLock)
        {
            if (_healthHistory.Count < 2)
                return HealthTrend.Stable;
            
            var recent = _healthHistory.TakeLast(5).ToList();
            var scores = recent.Select(h => h.HealthScore).ToList();
            
            if (scores.Count < 2)
                return HealthTrend.Stable;
            
            var trend = scores.Last() - scores.First();
            
            return trend switch
            {
                > 0.1 => HealthTrend.Improving,
                < -0.1 => HealthTrend.Degrading,
                _ => HealthTrend.Stable
            };
        }
    }
    
    /// <summary>
    /// Gets time since last fallback
    /// </summary>
    public TimeSpan? GetTimeSinceLastFallback()
    {
        return FallbackTriggeredAt.HasValue ? DateTimeOffset.UtcNow - FallbackTriggeredAt.Value : null;
    }
    
    /// <summary>
    /// Checks if system is stable (no recent fallbacks)
    /// </summary>
    public bool IsSystemStable()
    {
        var timeSinceFallback = GetTimeSinceLastFallback();
        return !IsInFallbackMode && 
               (timeSinceFallback == null || timeSinceFallback > TimeSpan.FromMinutes(5)) &&
               OverallHealthStatus != SystemHealthStatus.Critical;
    }
    
    /// <summary>
    /// Gets recommended actions based on current health state
    /// </summary>
    public List<string> GetRecommendedActions()
    {
        var actions = new List<string>();
        
        if (IsInFallbackMode)
        {
            actions.Add($"System in fallback mode due to: {FallbackReason}");
            actions.Add("Monitor system stability before attempting recovery");
        }
        
        if (QuotaDataAge > TimeSpan.FromMinutes(5))
        {
            actions.Add("Quota data is stale - trigger refresh");
        }
        
        if (ActiveInstanceCount <= 1 && CoordinationEfficiency < 0.5)
        {
            actions.Add("Low coordination efficiency - check instance connectivity");
        }
        
        if (TotalETagConflicts > 100)
        {
            actions.Add("High ETag conflict rate - consider reducing concurrency");
        }
        
        if (SafeTokens <= 0)
        {
            actions.Add("No safe tokens available - review quota utilization");
        }
        
        var trend = GetHealthTrend();
        if (trend == HealthTrend.Degrading)
        {
            actions.Add("Health trend degrading - investigate system performance");
        }
        
        return actions;
    }
    
    /// <summary>
    /// Calculates overall health score and status
    /// </summary>
    private void CalculateOverallHealth()
    {
        if (IsInFallbackMode)
        {
            OverallHealthStatus = SystemHealthStatus.Critical;
            OverallHealthScore = 0.1;
            return;
        }
        
        var scores = new List<double>();
        
        // Quota health (40% weight)
        var quotaScore = QuotaHealthStatus switch
        {
            QuotaHealthStatus.Healthy => 1.0,
            QuotaHealthStatus.Warning => 0.6,
            QuotaHealthStatus.Critical => 0.2,
            _ => 0.5
        };
        scores.Add(quotaScore * 0.4);
        
        // Coordination health (30% weight)
        var coordinationScore = ActiveInstanceCount > 0 ? CoordinationEfficiency : 0.0;
        if (LastHeartbeatAge > TimeSpan.FromMinutes(2))
            coordinationScore *= 0.5; // Penalize stale heartbeats
        scores.Add(coordinationScore * 0.3);
        
        // Consensus health (20% weight)
        var consensusScore = AverageAllocationEfficiency;
        if (TotalETagConflicts > 50)
            consensusScore *= 0.7; // Penalize high conflict rates
        scores.Add(consensusScore * 0.2);
        
        // Data freshness (10% weight)
        var freshnessScore = QuotaDataAge < TimeSpan.FromMinutes(1) ? 1.0 :
                           QuotaDataAge < TimeSpan.FromMinutes(5) ? 0.8 : 0.3;
        scores.Add(freshnessScore * 0.1);
        
        OverallHealthScore = Math.Max(0.0, Math.Min(1.0, scores.Sum()));
        
        OverallHealthStatus = OverallHealthScore switch
        {
            >= 0.8 => SystemHealthStatus.Healthy,
            >= 0.5 => SystemHealthStatus.Warning,
            >= 0.2 => SystemHealthStatus.Degraded,
            _ => SystemHealthStatus.Critical
        };
    }
    
    /// <summary>
    /// Adds current health snapshot to history
    /// </summary>
    private void AddHealthSnapshot()
    {
        lock (_historyLock)
        {
            _healthHistory.Enqueue(new HealthSnapshot
            {
                Timestamp = DateTimeOffset.UtcNow,
                HealthScore = OverallHealthScore,
                HealthStatus = OverallHealthStatus
            });
            
            // Keep only last 20 snapshots
            while (_healthHistory.Count > 20)
            {
                _healthHistory.Dequeue();
            }
        }
    }
    
    /// <summary>
    /// Returns a string representation of the system health state
    /// </summary>
    /// <returns>String with health status, trend, instance count, and fallback info</returns>
    public override string ToString()
    {
        var trend = GetHealthTrend();
        var fallbackInfo = IsInFallbackMode ? $" [FALLBACK: {FallbackReason}]" : "";
        
        return $"SystemHealth[{StoreId}]: {OverallHealthStatus} ({OverallHealthScore:F2}), " +
               $"Trend: {trend}, Instances: {ActiveInstanceCount}, " +
               $"Safe Tokens: {SafeTokens}{fallbackInfo}";
    }
}

/// <summary>
/// Health snapshot for trend analysis
/// </summary>
/// <summary>
/// Health snapshot for trend analysis
/// </summary>
public class HealthSnapshot
{
    /// <summary>
    /// When this snapshot was taken
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Overall health score at snapshot time
    /// </summary>
    public double HealthScore { get; set; }

    /// <summary>
    /// System health status at snapshot time
    /// </summary>
    public SystemHealthStatus HealthStatus { get; set; }
}

/// <summary>
/// System health status enumeration
/// </summary>
/// <summary>
/// System health status enumeration
/// </summary>
public enum SystemHealthStatus
{
    /// <summary>
    /// Health status has not been determined yet
    /// </summary>
    Unknown,

    /// <summary>
    /// System is operating normally with good performance
    /// </summary>
    Healthy,

    /// <summary>
    /// System is operating but showing signs of stress
    /// </summary>
    Warning,

    /// <summary>
    /// System performance is significantly impacted
    /// </summary>
    Degraded,

    /// <summary>
    /// System is in a critical state requiring immediate attention
    /// </summary>
    Critical
}

/// <summary>
/// Health trend enumeration
/// </summary>
/// <summary>
/// Health trend enumeration
/// </summary>
public enum HealthTrend
{
    /// <summary>
    /// System health is improving over time
    /// </summary>
    Improving,

    /// <summary>
    /// System health is stable and consistent
    /// </summary>
    Stable,

    /// <summary>
    /// System health is degrading over time
    /// </summary>
    Degrading
}

/// <summary>
/// Fallback reason enumeration
/// </summary>
/// <summary>
/// Fallback reason enumeration
/// </summary>
public enum FallbackReason
{
    /// <summary>
    /// Multiple instances have inconsistent state
    /// </summary>
    SplitBrainDetected,

    /// <summary>
    /// Instance coordination has failed
    /// </summary>
    CoordinationFailure,

    /// <summary>
    /// Quota data is too old to be reliable
    /// </summary>
    QuotaDataStale,

    /// <summary>
    /// Too many ETag conflicts during token allocation
    /// </summary>
    HighConflictRate,

    /// <summary>
    /// System performance has degraded significantly
    /// </summary>
    PerformanceDegradation
}