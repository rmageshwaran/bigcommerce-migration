using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Comprehensive API health metrics combining internal and BigCommerce data
/// Follows Single Responsibility Principle - handles only API health data aggregation
/// Used for dynamic rate limit calculation based on multiple health factors
/// </summary>
public class ApiHealthMetrics
{
    #region Properties

    /// <summary>
    /// Store identifier for the health metrics context
    /// </summary>
    [JsonPropertyName("storeId")]
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// BigCommerce API rate limit information from response headers
    /// </summary>
    [JsonPropertyName("bigCommerceRateLimit")]
    public BigCommerceRateLimitInfo? BigCommerceRateLimit { get; set; }

    /// <summary>
    /// Average response time in milliseconds over the last measurement window
    /// </summary>
    [JsonPropertyName("averageResponseTimeMs")]
    public double AverageResponseTimeMs { get; set; }

    /// <summary>
    /// Error rate percentage (0.0 to 1.0) over the last measurement window
    /// </summary>
    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }

    /// <summary>
    /// Current requests per second being sent to the API
    /// </summary>
    [JsonPropertyName("currentRequestsPerSecond")]
    public double CurrentRequestsPerSecond { get; set; }

    /// <summary>
    /// Number of successful requests in the measurement window
    /// </summary>
    [JsonPropertyName("successfulRequests")]
    public int SuccessfulRequests { get; set; }

    /// <summary>
    /// Number of failed requests in the measurement window
    /// </summary>
    [JsonPropertyName("failedRequests")]
    public int FailedRequests { get; set; }

    /// <summary>
    /// Timestamp when these metrics were last updated
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Duration of the measurement window in minutes
    /// </summary>
    [JsonPropertyName("measurementWindowMinutes")]
    public int MeasurementWindowMinutes { get; set; } = 5;

    #endregion

    #region Constructors

    /// <summary>
    /// Default constructor - initializes with current timestamp
    /// </summary>
    public ApiHealthMetrics()
    {
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Constructor for creating health metrics with store context
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    public ApiHealthMetrics(string storeId) : this()
    {
        StoreId = storeId ?? throw new ArgumentNullException(nameof(storeId));
    }

    #endregion

    #region Business Logic Methods

    /// <summary>
    /// Gets the total number of requests in the measurement window
    /// </summary>
    public int TotalRequests => SuccessfulRequests + FailedRequests;

    /// <summary>
    /// Calculates the success rate percentage
    /// </summary>
    /// <returns>Success rate (0.0 to 1.0)</returns>
    public double GetSuccessRate()
    {
        var total = TotalRequests;
        return total > 0 ? (double)SuccessfulRequests / total : 1.0;
    }

    /// <summary>
    /// Determines if the API is in a healthy state
    /// </summary>
    /// <returns>True if API health is good</returns>
    public bool IsHealthy()
    {
        const double maxHealthyErrorRate = 0.05; // 5%
        const double maxHealthyResponseTime = 2000; // 2 seconds

        return ErrorRate <= maxHealthyErrorRate && 
               AverageResponseTimeMs <= maxHealthyResponseTime;
    }

    /// <summary>
    /// Gets the health score as a percentage (0-100)
    /// </summary>
    /// <returns>Health score where 100 is perfect health</returns>
    public double GetHealthScore()
    {
        // Start with 100% health
        double score = 100.0;

        // Deduct points for error rate (max 30 points)
        var errorPenalty = Math.Min(30, ErrorRate * 100 * 6); // 6x multiplier for error impact
        score -= errorPenalty;

        // Deduct points for response time (max 30 points)
        var maxAcceptableResponseTime = 1000.0; // 1 second
        if (AverageResponseTimeMs > maxAcceptableResponseTime)
        {
            var responsePenalty = Math.Min(30, 
                (AverageResponseTimeMs - maxAcceptableResponseTime) / maxAcceptableResponseTime * 30);
            score -= responsePenalty;
        }

        // BigCommerce rate limit health (max 40 points)
        if (BigCommerceRateLimit?.IsValid() == true)
        {
            if (BigCommerceRateLimit.IsCritical())
            {
                score -= 40; // Critical rate limit state
            }
            else
            {
                var utilizationPenalty = BigCommerceRateLimit.GetUtilizationPercentage() * 20; // Up to 20 points
                score -= utilizationPenalty;
            }
        }
        else
        {
            score -= 20; // No BigCommerce data available
        }

        return Math.Max(0, Math.Min(100, score));
    }

    /// <summary>
    /// Validates the health metrics data
    /// </summary>
    /// <returns>True if the metrics are valid</returns>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(StoreId) &&
               AverageResponseTimeMs >= 0 &&
               ErrorRate >= 0 && ErrorRate <= 1 &&
               CurrentRequestsPerSecond >= 0 &&
               SuccessfulRequests >= 0 &&
               FailedRequests >= 0 &&
               MeasurementWindowMinutes > 0;
    }

    /// <summary>
    /// Determines if the metrics data is stale
    /// </summary>
    /// <param name="maxAgeMinutes">Maximum age in minutes before considering stale</param>
    /// <returns>True if the data is stale</returns>
    public bool IsStale(int maxAgeMinutes = 10)
    {
        return DateTime.UtcNow - Timestamp > TimeSpan.FromMinutes(maxAgeMinutes);
    }

    /// <summary>
    /// Gets the recommended rate adjustment factor based on health
    /// </summary>
    /// <returns>Multiplier for adjusting rate (0.1 to 2.0)</returns>
    public double GetRecommendedRateAdjustment()
    {
        var healthScore = GetHealthScore();
        
        // Excellent health (90-100): Increase rate up to 2x
        if (healthScore >= 90)
            return 1.5 + (healthScore - 90) / 10 * 0.5; // 1.5 to 2.0
        
        // Good health (70-89): Slight increase
        if (healthScore >= 70)
            return 1.0 + (healthScore - 70) / 20 * 0.5; // 1.0 to 1.5
        
        // Fair health (50-69): No change
        if (healthScore >= 50)
            return 1.0;
        
        // Poor health (30-49): Reduce rate
        if (healthScore >= 30)
            return 0.5 + (healthScore - 30) / 20 * 0.5; // 0.5 to 1.0
        
        // Critical health (0-29): Severely reduce rate
        return 0.1 + healthScore / 30 * 0.4; // 0.1 to 0.5
    }

    #endregion

    #region String Representation

    /// <summary>
    /// Returns a string representation of the API health metrics
    /// </summary>
    public override string ToString()
    {
        var healthScore = GetHealthScore();
        var successRate = GetSuccessRate() * 100;
        
        return $"API Health [Store: {StoreId}, " +
               $"Score: {healthScore:F1}%, " +
               $"Success Rate: {successRate:F1}%, " +
               $"Avg Response: {AverageResponseTimeMs:F0}ms, " +
               $"Error Rate: {ErrorRate * 100:F1}%]";
    }

    #endregion
} 