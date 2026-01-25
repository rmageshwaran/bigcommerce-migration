namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Event arguments for optimal rate change notifications
/// Provides comprehensive information about rate changes for real-time monitoring
/// Used by dynamic rate limiting system to notify consumers of significant rate adjustments
/// </summary>
public class OptimalRateChangedEventArgs : EventArgs
{
    /// <summary>
    /// The ID of the store for which the rate changed
    /// </summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// The previous optimal rate in requests per second
    /// </summary>
    public double PreviousRate { get; set; }

    /// <summary>
    /// The new optimal rate in requests per second
    /// </summary>
    public double NewRate { get; set; }

    /// <summary>
    /// The percentage change in rate (0.0 to 1.0, where 0.2 = 20% change)
    /// </summary>
    public double ChangePercentage { get; set; }

    /// <summary>
    /// Timestamp when the rate change was detected
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets a human-readable description of the rate change
    /// </summary>
    public string Description => 
        $"Rate changed from {PreviousRate:F1} to {NewRate:F1} req/sec ({ChangePercentage:P1} change) for store {StoreId}";

    /// <summary>
    /// Indicates if this represents a rate increase
    /// </summary>
    public bool IsIncrease => NewRate > PreviousRate;

    /// <summary>
    /// Indicates if this represents a significant change (>= 20%)
    /// </summary>
    public bool IsSignificantChange => ChangePercentage >= 0.2;
}

/// <summary>
/// Event arguments for API health change notifications
/// Provides comprehensive information about health changes for real-time monitoring
/// Used by dynamic rate limiting system to notify consumers of significant health fluctuations
/// </summary>
public class ApiHealthChangedEventArgs : EventArgs
{
    /// <summary>
    /// The ID of the store for which the health changed
    /// </summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// The previous health score (0-100)
    /// </summary>
    public double PreviousHealthScore { get; set; }

    /// <summary>
    /// The new health score (0-100)
    /// </summary>
    public double NewHealthScore { get; set; }

    /// <summary>
    /// The complete health metrics providing detailed context
    /// </summary>
    public ApiHealthMetrics? HealthMetrics { get; set; }

    /// <summary>
    /// Timestamp when the health change was detected
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets a human-readable description of the health change
    /// </summary>
    public string Description => 
        $"Health score changed from {PreviousHealthScore:F1} to {NewHealthScore:F1} for store {StoreId}";

    /// <summary>
    /// Indicates if this represents a health improvement
    /// </summary>
    public bool IsImprovement => NewHealthScore > PreviousHealthScore;

    /// <summary>
    /// Indicates if this represents a significant change (>= 15 points)
    /// </summary>
    public bool IsSignificantChange => Math.Abs(NewHealthScore - PreviousHealthScore) >= 15.0;

    /// <summary>
    /// Gets the magnitude of the health change
    /// </summary>
    public double HealthChangeMagnitude => Math.Abs(NewHealthScore - PreviousHealthScore);

    /// <summary>
    /// Indicates if the new health score represents a critical state (&lt; 30)
    /// </summary>
    public bool IsCriticalHealth => NewHealthScore < 30.0;

    /// <summary>
    /// Indicates if the new health score represents excellent health (&gt; 85)
    /// </summary>
    public bool IsExcellentHealth => NewHealthScore > 85.0;
} 