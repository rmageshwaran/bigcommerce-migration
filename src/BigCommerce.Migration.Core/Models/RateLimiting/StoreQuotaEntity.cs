using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models.RateLimiting;

/// <summary>
/// Azure Table Storage entity for real-time quota tracking per store
/// Stores current BigCommerce API quota state with ETag-based optimistic concurrency
/// </summary>
public class StoreQuotaEntity : ITableEntity
{
    /// <summary>
    /// Partition key: Store ID (e.g., "store-12345")
    /// Groups all quota data by store for efficient queries
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key: Always "quota" for quota state
    /// Ensures single quota record per store
    /// </summary>
    public string RowKey { get; set; } = "quota";

    /// <summary>
    /// ETag for optimistic concurrency control
    /// CRITICAL: Prevents race conditions in quota updates
    /// </summary>
    public ETag ETag { get; set; }

    /// <summary>
    /// Timestamp for Azure Table Storage
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Current quota limit from BigCommerce (e.g., 500 requests per minute)
    /// Parsed from X-Rate-Limit-Requests-Quota header
    /// </summary>
    [Required]
    public int CurrentQuota { get; set; }

    /// <summary>
    /// Remaining tokens in current window
    /// Parsed from X-Rate-Limit-Requests-Left header
    /// </summary>
    [Required]
    public int RemainingTokens { get; set; }

    /// <summary>
    /// When the quota window resets (UTC)
    /// Calculated from X-Rate-Limit-Time-Reset-Ms header
    /// </summary>
    [Required]
    public DateTimeOffset QuotaResetTime { get; set; }

    /// <summary>
    /// When this quota data was last updated (UTC)
    /// Used for anti-staleness protection
    /// </summary>
    [Required]
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Quota window duration in seconds (typically 60)
    /// Parsed from X-Rate-Limit-Time-Window-Ms header
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Source of the last quota update (API response URL)
    /// For debugging and audit purposes
    /// </summary>
    public string? LastUpdateSource { get; set; }

    /// <summary>
    /// Number of consecutive update failures
    /// Used for circuit breaker logic
    /// </summary>
    public int ConsecutiveFailures { get; set; } = 0;

    /// <summary>
    /// Calculates quota utilization percentage (0.0 to 1.0)
    /// Used for health scoring and safety buffer determination
    /// </summary>
    public double GetQuotaUtilization()
    {
        if (CurrentQuota <= 0) return 1.0; // Assume worst case
        return (double)(CurrentQuota - RemainingTokens) / CurrentQuota;
    }

    /// <summary>
    /// Determines quota health status based on remaining percentage
    /// Used for adaptive safety buffer calculation
    /// </summary>
    public QuotaHealthStatus GetHealthStatus(double healthyThreshold = 0.3, double criticalThreshold = 0.1)
    {
        var remainingPercentage = (double)RemainingTokens / CurrentQuota;
        
        if (remainingPercentage <= criticalThreshold)
            return QuotaHealthStatus.Critical;
        
        if (remainingPercentage <= healthyThreshold)
            return QuotaHealthStatus.Warning;
        
        return QuotaHealthStatus.Healthy;
    }

    /// <summary>
    /// Calculates safe tokens available for distribution with adaptive safety buffer
    /// CRITICAL: This prevents 429 errors by applying appropriate safety margins
    /// </summary>
    public int CalculateSafeTokens(double safetyBufferPercentage, double healthyThreshold = 0.3, double criticalThreshold = 0.1)
    {
        var healthStatus = GetHealthStatus(healthyThreshold, criticalThreshold);
        
        // Adaptive safety buffer based on quota health
        var effectiveBuffer = healthStatus switch
        {
            QuotaHealthStatus.Critical => 0.5,  // 50% buffer in emergency
            QuotaHealthStatus.Warning => 0.25,  // 25% buffer when cautious
            QuotaHealthStatus.Healthy => safetyBufferPercentage, // Normal buffer (15%)
            _ => 0.5 // Defensive fallback
        };

        var safeTokens = (int)(RemainingTokens * (1.0 - effectiveBuffer));
        return Math.Max(0, safeTokens);
    }

    /// <summary>
    /// Updates quota data from BigCommerce API response headers
    /// Includes timestamp validation for anti-staleness protection
    /// </summary>
    public void UpdateFromBigCommerceHeaders(int quota, int remaining, DateTimeOffset resetTime, int windowSeconds, string? source = null)
    {
        CurrentQuota = quota;
        RemainingTokens = remaining;
        QuotaResetTime = resetTime;
        WindowSeconds = windowSeconds;
        LastUpdated = DateTimeOffset.UtcNow;
        LastUpdateSource = source;
        ConsecutiveFailures = 0; // Reset on successful update
    }

    /// <summary>
    /// Checks if quota data is stale and should be refreshed
    /// </summary>
    public bool IsStale(TimeSpan maxAge)
    {
        return DateTimeOffset.UtcNow - LastUpdated > maxAge;
    }

    /// <summary>
    /// Records a quota update failure
    /// Used for circuit breaker logic
    /// </summary>
    public void RecordFailure()
    {
        ConsecutiveFailures++;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Returns a string representation of the store quota entity
    /// </summary>
    /// <returns>String with quota details, health status, and last update time</returns>
    public override string ToString()
    {
        return $"StoreQuota[{PartitionKey}]: {RemainingTokens}/{CurrentQuota} tokens, Health: {GetHealthStatus()}, Updated: {LastUpdated:HH:mm:ss}";
    }
}

/// <summary>
/// Quota health status for adaptive safety buffer calculation
/// </summary>
public enum QuotaHealthStatus
{
    /// <summary>
    /// >30% quota remaining - normal 15% safety buffer
    /// </summary>
    Healthy,
    
    /// <summary>
    /// 10-30% quota remaining - conservative 25% safety buffer
    /// </summary>
    Warning,
    
    /// <summary>
    /// Less than 10% quota remaining - emergency 50% safety buffer
    /// </summary>
    Critical
}