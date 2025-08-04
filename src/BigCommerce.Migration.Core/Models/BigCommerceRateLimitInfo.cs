using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Data model for BigCommerce API rate limit information from response headers
/// Follows Single Responsibility Principle - handles only BigCommerce rate limit data
/// Captures X-Rate-Limit-* headers for dynamic rate limit calculation
/// </summary>
public class BigCommerceRateLimitInfo : IEquatable<BigCommerceRateLimitInfo>
{
    #region Properties

    /// <summary>
    /// Store identifier for the rate limit context
    /// </summary>
    [JsonPropertyName("storeId")]
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// Number of requests remaining in the current window
    /// Maps to X-Rate-Limit-Requests-Left header
    /// </summary>
    [JsonPropertyName("requestsLeft")]
    public int RequestsLeft { get; set; }

    /// <summary>
    /// Total quota of requests allowed in the time window
    /// Maps to X-Rate-Limit-Requests-Quota header
    /// </summary>
    [JsonPropertyName("requestsQuota")]
    public int RequestsQuota { get; set; }

    /// <summary>
    /// Time in milliseconds until the rate limit resets
    /// Maps to X-Rate-Limit-Time-Reset-Ms header
    /// </summary>
    [JsonPropertyName("timeResetMs")]
    public long TimeResetMs { get; set; }

    /// <summary>
    /// Time window in milliseconds for the rate limit
    /// Maps to X-Rate-Limit-Time-Window-Ms header
    /// </summary>
    [JsonPropertyName("timeWindowMs")]
    public long TimeWindowMs { get; set; }

    /// <summary>
    /// Timestamp when this rate limit information was captured
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    #endregion

    #region Constructors

    /// <summary>
    /// Default constructor - initializes with current timestamp
    /// </summary>
    public BigCommerceRateLimitInfo()
    {
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Parameterized constructor for creating rate limit info with all values
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="requestsLeft">Requests remaining in window</param>
    /// <param name="requestsQuota">Total request quota for window</param>
    /// <param name="timeResetMs">Time until reset in milliseconds</param>
    /// <param name="timeWindowMs">Time window in milliseconds</param>
    public BigCommerceRateLimitInfo(
        string storeId,
        int requestsLeft,
        int requestsQuota,
        long timeResetMs,
        long timeWindowMs)
    {
        StoreId = storeId ?? throw new ArgumentNullException(nameof(storeId));
        RequestsLeft = requestsLeft;
        RequestsQuota = requestsQuota;
        TimeResetMs = timeResetMs;
        TimeWindowMs = timeWindowMs;
        Timestamp = DateTime.UtcNow;
    }

    #endregion

    #region Business Logic Methods

    /// <summary>
    /// Calculates the utilization percentage of the rate limit
    /// </summary>
    /// <returns>Utilization percentage (0.0 to 1.0)</returns>
    public double GetUtilizationPercentage()
    {
        if (RequestsQuota <= 0)
            return 0.0;

        var usedRequests = RequestsQuota - RequestsLeft;
        return (double)usedRequests / RequestsQuota;
    }

    /// <summary>
    /// Determines if the rate limit is in a critical state (&lt; 5% capacity remaining)
    /// </summary>
    /// <returns>True if capacity is critically low</returns>
    public bool IsCritical()
    {
        if (RequestsQuota <= 0)
            return false;

        const double criticalThreshold = 0.05; // 5%
        var remainingPercentage = (double)RequestsLeft / RequestsQuota;
        return remainingPercentage < criticalThreshold;
    }

    /// <summary>
    /// Gets the time until the rate limit resets
    /// </summary>
    /// <returns>TimeSpan until reset</returns>
    public TimeSpan GetTimeUntilReset()
    {
        return TimeSpan.FromMilliseconds(TimeResetMs);
    }

    /// <summary>
    /// Validates the rate limit information
    /// </summary>
    /// <returns>True if the rate limit info is valid</returns>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(StoreId) && RequestsQuota > 0;
    }

    /// <summary>
    /// Gets the effective rate limit considering current capacity
    /// </summary>
    /// <returns>Suggested requests per second based on remaining capacity</returns>
    public double GetEffectiveRateLimit()
    {
        if (TimeResetMs <= 0 || RequestsLeft <= 0)
            return 0.0;

        var resetTimeSeconds = TimeResetMs / 1000.0;
        return RequestsLeft / resetTimeSeconds;
    }

    #endregion

    #region IEquatable<BigCommerceRateLimitInfo> Implementation

    /// <summary>
    /// Determines whether the specified BigCommerceRateLimitInfo is equal to the current object
    /// </summary>
    public bool Equals(BigCommerceRateLimitInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return StoreId == other.StoreId &&
               RequestsLeft == other.RequestsLeft &&
               RequestsQuota == other.RequestsQuota &&
               TimeResetMs == other.TimeResetMs &&
               TimeWindowMs == other.TimeWindowMs;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object
    /// </summary>
    public override bool Equals(object? obj)
    {
        return Equals(obj as BigCommerceRateLimitInfo);
    }

    /// <summary>
    /// Returns a hash code for the current object
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(StoreId, RequestsLeft, RequestsQuota, TimeResetMs, TimeWindowMs);
    }

    /// <summary>
    /// Equality operator
    /// </summary>
    public static bool operator ==(BigCommerceRateLimitInfo? left, BigCommerceRateLimitInfo? right)
    {
        return Equals(left, right);
    }

    /// <summary>
    /// Inequality operator
    /// </summary>
    public static bool operator !=(BigCommerceRateLimitInfo? left, BigCommerceRateLimitInfo? right)
    {
        return !(left == right);
    }

    #endregion

    #region String Representation

    /// <summary>
    /// Returns a string representation of the rate limit information
    /// </summary>
    public override string ToString()
    {
        var utilizationPercent = GetUtilizationPercentage() * 100;
        var resetTime = GetTimeUntilReset();
        
        return $"BigCommerce Rate Limit [Store: {StoreId}, " +
               $"Requests: {RequestsLeft}/{RequestsQuota} " +
               $"({utilizationPercent:F1}% used), " +
               $"Reset in: {resetTime.TotalSeconds:F0}s]";
    }

    #endregion
} 