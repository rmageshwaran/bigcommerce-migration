namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Comprehensive configuration for dynamic rate limiting system
/// Supports feature flags, rate calculation parameters, health monitoring settings, and safety configurations
/// Follows configuration best practices with sensible defaults and validation
/// </summary>
public class DynamicRateLimitingConfiguration
{
    /// <summary>
    /// Feature flags for controlling dynamic rate limiting behavior
    /// </summary>
    public FeatureFlags Features { get; set; } = new();

    /// <summary>
    /// Rate calculation algorithm settings
    /// </summary>
    public RateCalculationSettings RateCalculation { get; set; } = new();

    /// <summary>
    /// API health monitoring configuration
    /// </summary>
    public HealthMonitoringSettings HealthMonitoring { get; set; } = new();

    /// <summary>
    /// Event triggering and notification settings
    /// </summary>
    public EventSettings Events { get; set; } = new();

    /// <summary>
    /// Safety and fallback behavior configuration
    /// </summary>
    public SafetySettings Safety { get; set; } = new();

    /// <summary>
    /// Predictive distributed rate limiting configuration
    /// NEW: Enhanced multi-instance coordination and quota tracking
    /// </summary>
    public PredictiveSettings Predictive { get; set; } = new();

    /// <summary>
    /// Validates the configuration for correctness and consistency
    /// </summary>
    /// <returns>List of validation errors, empty if valid</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        // Validate rate calculation settings
        if (RateCalculation.MinimumRate <= 0)
            errors.Add("MinimumRate must be greater than 0");

        if (RateCalculation.MaximumRate <= RateCalculation.MinimumRate)
            errors.Add("MaximumRate must be greater than MinimumRate");

        if (RateCalculation.MinimumRate > 50 || RateCalculation.MaximumRate > 200)
            errors.Add("Rate limits seem unreasonably high - consider API provider limits");

        // Validate health monitoring settings
        if (HealthMonitoring.MonitoringWindowMinutes <= 0)
            errors.Add("MonitoringWindowMinutes must be greater than 0");

        if (HealthMonitoring.MonitoringWindowMinutes > 60)
            errors.Add("MonitoringWindowMinutes should not exceed 60 for responsive rate adjustments");

        // Validate event settings
        if (Events.SignificantRateChangeThreshold < 0 || Events.SignificantRateChangeThreshold > 1)
            errors.Add("SignificantRateChangeThreshold must be between 0 and 1");

        if (Events.SignificantHealthChangeThreshold < 0 || Events.SignificantHealthChangeThreshold > 100)
            errors.Add("SignificantHealthChangeThreshold must be between 0 and 100");

        // Validate safety settings
        if (Safety.FallbackRateRequestsPerSecond <= 0)
            errors.Add("FallbackRateRequestsPerSecond must be greater than 0");

        return errors;
    }
}

/// <summary>
/// Feature flags for controlling dynamic rate limiting functionality
/// Enables gradual rollout and easy rollback of features
/// </summary>
public class FeatureFlags
{
    /// <summary>
    /// Master switch for dynamic rate limiting
    /// When false, system falls back to static rate limiting
    /// </summary>
    public bool EnableDynamicRateLimiting { get; set; } = true;

    /// <summary>
    /// Enable BigCommerce API health monitoring
    /// When false, only internal metrics are used
    /// </summary>
    public bool EnableApiHealthMonitoring { get; set; } = true;

    /// <summary>
    /// Enable real-time rate and health change events
    /// When false, events are not triggered
    /// </summary>
    public bool EnableRealTimeEvents { get; set; } = true;

    /// <summary>
    /// Enable sophisticated rate calculation algorithms
    /// When false, uses simplified linear calculation
    /// </summary>
    public bool EnableAdvancedRateCalculation { get; set; } = true;

    /// <summary>
    /// Enable automatic rate adjustments based on BigCommerce headers
    /// When false, only internal metrics influence rate calculation
    /// </summary>
    public bool EnableBigCommerceRateLimitIntegration { get; set; } = true;

    /// <summary>
    /// Enable comprehensive logging of rate limiting decisions
    /// When false, only critical events are logged
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// NEW: Enable predictive distributed rate limiting with multi-instance coordination
    /// When false, uses existing in-memory rate limiting approach
    /// </summary>
    public bool EnablePredictiveDistribution { get; set; } = false;

    /// <summary>
    /// NEW: Enable multi-instance coordination via Azure Table Storage
    /// Requires EnablePredictiveDistribution to be true
    /// </summary>
    public bool EnableInstanceCoordination { get; set; } = true;

    /// <summary>
    /// NEW: Enable real-time BigCommerce quota tracking from API response headers
    /// When false, uses static rate assumptions
    /// </summary>
    public bool EnableQuotaTracking { get; set; } = true;
}

/// <summary>
/// Configuration for rate calculation algorithms and parameters
/// Controls how optimal rates are computed based on health metrics
/// </summary>
public class RateCalculationSettings
{
    /// <summary>
    /// Minimum allowed rate in requests per second
    /// Safety floor to prevent system starvation
    /// </summary>
    public double MinimumRate { get; set; } = 5.0;

    /// <summary>
    /// Maximum allowed rate in requests per second
    /// Safety ceiling to respect API provider limits
    /// </summary>
    public double MaximumRate { get; set; } = 50.0;

    /// <summary>
    /// Default rate when no health data is available
    /// Used during system startup or health monitoring failure
    /// </summary>
    public double DefaultRate { get; set; } = 12.0;

    /// <summary>
    /// Weight given to response time factor in rate calculation (0.0 to 1.0)
    /// Higher values make the system more sensitive to response time changes
    /// </summary>
    public double ResponseTimeWeight { get; set; } = 0.2;

    /// <summary>
    /// Weight given to error rate factor in rate calculation (0.0 to 1.0)
    /// Higher values make the system more sensitive to error rate changes
    /// </summary>
    public double ErrorRateWeight { get; set; } = 0.12;

    /// <summary>
    /// Weight given to BigCommerce rate limit factor in rate calculation (0.0 to 1.0)
    /// Higher values make the system more responsive to API provider signals
    /// </summary>
    public double BigCommerceRateLimitWeight { get; set; } = 0.12;

    /// <summary>
    /// Weight given to overall health factor in rate calculation (0.0 to 1.0)
    /// This is the primary scaling factor for rate adjustments
    /// </summary>
    public double HealthFactorWeight { get; set; } = 0.56;

    /// <summary>
    /// Health score threshold below which aggressive penalties are applied
    /// </summary>
    public double CriticalHealthThreshold { get; set; } = 25.0;

    /// <summary>
    /// Health score threshold above which performance bonuses are applied
    /// </summary>
    public double ExcellentHealthThreshold { get; set; } = 85.0;

    /// <summary>
    /// Rate boost multiplier for excellent health conditions
    /// </summary>
    public double ExcellentHealthBoostMultiplier { get; set; } = 1.3;

    /// <summary>
    /// Validates that all weights sum to approximately 1.0
    /// </summary>
    public bool AreWeightsValid()
    {
        var totalWeight = ResponseTimeWeight + ErrorRateWeight + BigCommerceRateLimitWeight + HealthFactorWeight;
        return Math.Abs(totalWeight - 1.0) < 0.01; // Allow small floating point variations
    }
}

/// <summary>
/// Configuration for API health monitoring behavior
/// Controls how health metrics are collected, aggregated, and stored
/// </summary>
public class HealthMonitoringSettings
{
    /// <summary>
    /// Time window in minutes for health metric aggregation
    /// Shorter windows provide faster response, longer windows provide stability
    /// </summary>
    public int MonitoringWindowMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of API call records to store per store
    /// Prevents unbounded memory growth during high-throughput scenarios
    /// </summary>
    public int MaxCallHistorySize { get; set; } = 1000;

    /// <summary>
    /// Response time threshold in milliseconds above which calls are considered slow
    /// Used for health scoring calculations
    /// </summary>
    public double SlowResponseThresholdMs { get; set; } = 1000.0;

    /// <summary>
    /// Response time threshold in milliseconds above which calls are considered critical
    /// Used for aggressive health penalty calculations
    /// </summary>
    public double CriticalResponseThresholdMs { get; set; } = 3000.0;

    /// <summary>
    /// Error rate threshold above which health penalties are applied
    /// Expressed as a fraction (0.0 to 1.0, where 0.1 = 10%)
    /// </summary>
    public double ErrorRateThreshold { get; set; } = 0.05; // 5%

    /// <summary>
    /// Interval in seconds between health metric cleanup operations
    /// Removes old data outside the monitoring window
    /// </summary>
    public int CleanupIntervalSeconds { get; set; } = 300; // 5 minutes

    /// <summary>
    /// Enable automatic purging of stale health data
    /// When false, data cleanup must be triggered manually
    /// </summary>
    public bool EnableAutomaticCleanup { get; set; } = true;
}

/// <summary>
/// Configuration for event triggering and notifications
/// Controls when and how rate/health change events are fired
/// </summary>
public class EventSettings
{
    /// <summary>
    /// Rate change percentage threshold that triggers OptimalRateChanged events
    /// Expressed as a fraction (0.0 to 1.0, where 0.2 = 20% change)
    /// </summary>
    public double SignificantRateChangeThreshold { get; set; } = 0.2; // 20%

    /// <summary>
    /// Health score change threshold (in points) that triggers ApiHealthChanged events
    /// Health scores range from 0 to 100
    /// </summary>
    public double SignificantHealthChangeThreshold { get; set; } = 15.0; // 15 points

    /// <summary>
    /// Maximum frequency of event firing in seconds
    /// Prevents event spam during rapid fluctuations
    /// </summary>
    public int EventThrottleIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Enable event batching to reduce notification overhead
    /// When true, multiple small changes are combined into larger events
    /// </summary>
    public bool EnableEventBatching { get; set; } = false;

    /// <summary>
    /// Include detailed health metrics in ApiHealthChanged events
    /// When true, events contain full ApiHealthMetrics objects
    /// </summary>
    public bool IncludeDetailedHealthInEvents { get; set; } = true;
}

/// <summary>
/// Safety and fallback configuration
/// Ensures system reliability when dynamic rate limiting encounters issues
/// </summary>
public class SafetySettings
{
    /// <summary>
    /// Fallback rate used when dynamic calculation fails
    /// Should be conservative to prevent API abuse
    /// </summary>
    public double FallbackRateRequestsPerSecond { get; set; } = 8.0;

    /// <summary>
    /// Maximum allowed consecutive failures before falling back to static rate limiting
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>
    /// Time in minutes to wait before retrying dynamic rate limiting after fallback
    /// </summary>
    public int FallbackRetryDelayMinutes { get; set; } = 10;

    /// <summary>
    /// Enable automatic recovery from fallback mode
    /// When false, manual intervention is required to restore dynamic rate limiting
    /// </summary>
    public bool EnableAutomaticRecovery { get; set; } = true;

    /// <summary>
    /// Enable circuit breaker pattern for health monitoring failures
    /// When true, repeated health monitoring failures trigger temporary bypass
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = true;

    /// <summary>
    /// Health monitoring timeout in milliseconds
    /// Prevents health checks from blocking rate limiting decisions
    /// </summary>
    public int HealthMonitoringTimeoutMs { get; set; } = 1000; // 1 second

    /// <summary>
    /// Enable graceful degradation when BigCommerce rate limit headers are missing
    /// When true, continues with internal metrics only
    /// </summary>
    public bool EnableGracefulDegradation { get; set; } = true;
}

/// <summary>
/// NEW: Configuration for predictive distributed rate limiting
/// Controls multi-instance coordination, quota tracking, and consensus algorithms
/// </summary>
public class PredictiveSettings
{
    /// <summary>
    /// Safety buffer percentage for quota allocation (0.0 to 1.0)
    /// 0.15 = 15% safety margin for normal operations
    /// </summary>
    public double SafetyBufferPercentage { get; set; } = 0.15;

    /// <summary>
    /// Quota utilization threshold above which system is considered healthy (0.0 to 1.0)
    /// Above this threshold, normal 15% safety buffer is used
    /// </summary>
    public double HealthyQuotaThreshold { get; set; } = 0.3;

    /// <summary>
    /// Quota utilization threshold below which system is considered critical (0.0 to 1.0)
    /// Below this threshold, emergency 50% safety buffer is used
    /// </summary>
    public double CriticalQuotaThreshold { get; set; } = 0.1;

    /// <summary>
    /// Token expiry time in seconds for preventing token hoarding
    /// Adaptive: 15-45 seconds based on migration velocity
    /// </summary>
    public int TokenExpirySeconds { get; set; } = 30;

    /// <summary>
    /// Instance timeout in seconds for phantom cleanup
    /// Instances not seen within this time are considered dead
    /// </summary>
    public int InstanceTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Heartbeat interval in seconds for instance discovery
    /// How often instances send "I'm alive" signals
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum ETag conflict retry attempts
    /// Prevents infinite retry loops in high-contention scenarios
    /// </summary>
    public int MaxETagRetries { get; set; } = 5;

    /// <summary>
    /// Coordination health check interval in seconds
    /// How often to validate distributed coordination health
    /// </summary>
    public int CoordinationHealthCheckSeconds { get; set; } = 30;

    /// <summary>
    /// Table Storage configuration for predictive rate limiting
    /// </summary>
    public TableStorageSettings TableStorage { get; set; } = new();
}

/// <summary>
/// NEW: Table Storage configuration for predictive rate limiting
/// Defines table names and connection settings
/// </summary>
public class TableStorageSettings
{
    /// <summary>
    /// Table name for real-time quota state per store
    /// Stores current quota, remaining tokens, and reset times
    /// </summary>
    public string QuotaTableName { get; set; } = "RateLimitQuotas";

    /// <summary>
    /// Table name for instance coordination and heartbeats
    /// Tracks active instances per store for token distribution
    /// </summary>
    public string InstanceTableName { get; set; } = "RateLimitInstances";

    /// <summary>
    /// Table name for distributed token allocation
    /// Manages atomic token reservations across instances
    /// </summary>
    public string TokenTableName { get; set; } = "RateLimitTokens";

    /// <summary>
    /// Automatically create tables if they don't exist
    /// Follows existing MigrationStorageService patterns
    /// </summary>
    public bool AutoCreateTables { get; set; } = true;
} 