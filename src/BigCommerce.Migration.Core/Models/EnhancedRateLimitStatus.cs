using System.Text.Json.Serialization;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Enhanced rate limit status extending base RateLimitStatus with dynamic rate limiting data
/// Follows Open/Closed Principle - extends base functionality without modification
/// Provides comprehensive rate limiting information for dynamic adjustments
/// </summary>
public class EnhancedRateLimitStatus : RateLimitStatus
{
    #region Additional Properties

    /// <summary>
    /// Current optimal rate calculated dynamically based on API health
    /// </summary>
    [JsonPropertyName("optimalRate")]
    public int OptimalRate { get; set; }

    /// <summary>
    /// Health score of the API (0-100)
    /// </summary>
    [JsonPropertyName("healthScore")]
    public double HealthScore { get; set; }

    /// <summary>
    /// Rate adjustment factor applied to base rate
    /// </summary>
    [JsonPropertyName("rateAdjustmentFactor")]
    public double RateAdjustmentFactor { get; set; } = 1.0;

    /// <summary>
    /// BigCommerce API rate limit information if available
    /// </summary>
    [JsonPropertyName("bigCommerceRateLimit")]
    public BigCommerceRateLimitInfo? BigCommerceRateLimit { get; set; }

    /// <summary>
    /// Indicates if dynamic rate limiting is currently active
    /// </summary>
    [JsonPropertyName("isDynamicRateLimitingActive")]
    public bool IsDynamicRateLimitingActive { get; set; }

    /// <summary>
    /// Reason for the current rate limit adjustment
    /// </summary>
    [JsonPropertyName("adjustmentReason")]
    public string AdjustmentReason { get; set; } = string.Empty;

    /// <summary>
    /// Average response time in milliseconds
    /// </summary>
    [JsonPropertyName("averageResponseTimeMs")]
    public double AverageResponseTimeMs { get; set; }

    /// <summary>
    /// Current error rate (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }

    /// <summary>
    /// Timestamp when the enhanced status was calculated
    /// </summary>
    [JsonPropertyName("enhancedTimestamp")]
    public DateTime EnhancedTimestamp { get; set; }

    #endregion

    #region Constructors

    /// <summary>
    /// Default constructor - initializes with current timestamp
    /// </summary>
    public EnhancedRateLimitStatus()
    {
        EnhancedTimestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Constructor from base RateLimitStatus
    /// </summary>
    /// <param name="baseStatus">Base rate limit status</param>
    public EnhancedRateLimitStatus(RateLimitStatus baseStatus) : this()
    {
        if (baseStatus == null) throw new ArgumentNullException(nameof(baseStatus));

        // Copy all base properties
        StoreId = baseStatus.StoreId;
        RequestsPerMinute = baseStatus.RequestsPerMinute;
        RequestsRemaining = baseStatus.RequestsRemaining;
        WindowResetTime = baseStatus.WindowResetTime;
        IsLimited = baseStatus.IsLimited;
        RecommendedDelayMs = baseStatus.RecommendedDelayMs;
    }

    /// <summary>
    /// Constructor with dynamic rate limiting data
    /// </summary>
    /// <param name="baseStatus">Base rate limit status</param>
    /// <param name="apiHealth">API health metrics</param>
    /// <param name="optimalRate">Calculated optimal rate</param>
    public EnhancedRateLimitStatus(
        RateLimitStatus baseStatus, 
        ApiHealthMetrics apiHealth, 
        int optimalRate) : this(baseStatus)
    {
        if (apiHealth == null) throw new ArgumentNullException(nameof(apiHealth));

        OptimalRate = optimalRate;
        HealthScore = apiHealth.GetHealthScore();
        RateAdjustmentFactor = apiHealth.GetRecommendedRateAdjustment();
        BigCommerceRateLimit = apiHealth.BigCommerceRateLimit;
        AverageResponseTimeMs = apiHealth.AverageResponseTimeMs;
        ErrorRate = apiHealth.ErrorRate;
        IsDynamicRateLimitingActive = true;
        AdjustmentReason = GenerateAdjustmentReason(apiHealth);
    }

    #endregion

    #region Business Logic Methods

    /// <summary>
    /// Determines if the current rate should be increased
    /// </summary>
    /// <returns>True if rate can be safely increased</returns>
    public bool CanIncreaseRate()
    {
        return HealthScore >= 70 && 
               ErrorRate <= 0.02 && 
               AverageResponseTimeMs <= 1500 &&
               !IsAtBigCommerceLimit();
    }

    /// <summary>
    /// Determines if the current rate should be decreased
    /// </summary>
    /// <returns>True if rate should be decreased</returns>
    public bool ShouldDecreaseRate()
    {
        return HealthScore < 50 || 
               ErrorRate > 0.05 || 
               AverageResponseTimeMs > 3000 ||
               IsAtBigCommerceLimit();
    }

    /// <summary>
    /// Checks if we're approaching BigCommerce rate limits
    /// </summary>
    /// <returns>True if at or near BigCommerce limits</returns>
    public bool IsAtBigCommerceLimit()
    {
        return BigCommerceRateLimit?.IsCritical() == true ||
               BigCommerceRateLimit?.GetUtilizationPercentage() >= 0.9;
    }

    /// <summary>
    /// Gets the effective delay considering both base and dynamic factors
    /// </summary>
    /// <returns>Effective delay in milliseconds</returns>
    public int GetEffectiveDelayMs()
    {
        if (!IsDynamicRateLimitingActive)
            return RecommendedDelayMs;

        // If BigCommerce provides specific timing, use it
        if (BigCommerceRateLimit?.TimeResetMs > 0)
        {
            var bcDelay = (int)Math.Min(BigCommerceRateLimit.TimeResetMs, 60000); // Max 1 minute
            return Math.Max(RecommendedDelayMs, bcDelay);
        }

        // Apply health-based adjustment to base delay
        var healthAdjustedDelay = RecommendedDelayMs * (2.0 - RateAdjustmentFactor);
        return (int)Math.Max(0, Math.Min(healthAdjustedDelay, 60000)); // Cap at 1 minute
    }

    /// <summary>
    /// Gets the optimal requests per second rate
    /// </summary>
    /// <returns>Optimal rate in requests per second</returns>
    public double GetOptimalRequestsPerSecond()
    {
        return OptimalRate / 60.0; // Convert from per-minute to per-second
    }

    /// <summary>
    /// Validates the enhanced rate limit status
    /// </summary>
    /// <returns>True if the status is valid</returns>
    public bool IsValid()
    {
        return base.StoreId != null && 
               OptimalRate >= 0 && 
               HealthScore >= 0 && HealthScore <= 100 &&
               RateAdjustmentFactor > 0 &&
               ErrorRate >= 0 && ErrorRate <= 1;
    }

    /// <summary>
    /// Gets performance insights based on current metrics
    /// </summary>
    /// <returns>Array of insight messages</returns>
    public string[] GetPerformanceInsights()
    {
        var insights = new List<string>();

        if (HealthScore >= 90)
            insights.Add("Excellent API health - consider increasing rate");
        else if (HealthScore >= 70)
            insights.Add("Good API health - current rate is optimal");
        else if (HealthScore >= 50)
            insights.Add("Fair API health - monitor closely");
        else
            insights.Add("Poor API health - consider reducing rate");

        if (ErrorRate > 0.05)
            insights.Add($"High error rate ({ErrorRate * 100:F1}%) - investigate API issues");

        if (AverageResponseTimeMs > 2000)
            insights.Add($"Slow response times ({AverageResponseTimeMs:F0}ms) - API may be overloaded");

        if (IsAtBigCommerceLimit())
            insights.Add("Approaching BigCommerce rate limits - reduce request rate");

        if (BigCommerceRateLimit?.IsValid() != true)
            insights.Add("BigCommerce rate limit data unavailable - using estimated limits");

        return insights.ToArray();
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Generates human-readable reason for rate adjustment
    /// </summary>
    private string GenerateAdjustmentReason(ApiHealthMetrics apiHealth)
    {
        if (apiHealth.GetHealthScore() >= 90)
            return "Excellent API health allows increased rate";
        
        if (apiHealth.ErrorRate > 0.05)
            return $"High error rate ({apiHealth.ErrorRate * 100:F1}%) requires reduced rate";
        
        if (apiHealth.AverageResponseTimeMs > 2000)
            return $"Slow response times ({apiHealth.AverageResponseTimeMs:F0}ms) requires reduced rate";
        
        if (apiHealth.BigCommerceRateLimit?.IsCritical() == true)
            return "BigCommerce rate limit critical - rate reduced";
        
        if (apiHealth.GetHealthScore() < 50)
            return "Poor API health requires reduced rate";
        
        return "Rate optimized based on current API health";
    }

    #endregion

    #region String Representation

    /// <summary>
    /// Returns a comprehensive string representation of the enhanced rate limit status
    /// </summary>
    public override string ToString()
    {
        var baseInfo = base.ToString();
        var dynamicInfo = IsDynamicRateLimitingActive 
            ? $", Optimal Rate: {OptimalRate}/min, Health: {HealthScore:F1}%, Factor: {RateAdjustmentFactor:F2}x"
            : ", Dynamic Rate Limiting: Inactive";
        
        return baseInfo + dynamicInfo;
    }

    #endregion
} 