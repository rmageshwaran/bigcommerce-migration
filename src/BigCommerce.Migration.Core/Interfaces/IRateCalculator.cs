using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for calculating optimal API request rates based on health metrics
/// Follows Interface Segregation Principle - focused on rate calculation only
/// Follows Dependency Inversion Principle - enables testable and flexible implementations
/// Supports dynamic rate calculation algorithms for BigCommerce API optimization
/// </summary>
public interface IRateCalculator
{
    /// <summary>
    /// Calculates the optimal API request rate based on current health metrics
    /// Implements sophisticated algorithms including exponential smoothing and adaptive thresholds
    /// </summary>
    /// <param name="healthMetrics">Current API health metrics including BigCommerce rate limit data</param>
    /// <returns>Optimal requests per second (bounded between 5-50)</returns>
    /// <exception cref="ArgumentException">Thrown when health metrics contain invalid data</exception>
    double CalculateOptimalRate(ApiHealthMetrics? healthMetrics);

    /// <summary>
    /// Gets the minimum allowed rate limit for safety
    /// </summary>
    /// <returns>Minimum rate in requests per second</returns>
    double GetMinimumRate();

    /// <summary>
    /// Gets the maximum allowed rate limit
    /// </summary>
    /// <returns>Maximum rate in requests per second</returns>
    double GetMaximumRate();

    /// <summary>
    /// Resets the internal state of rate calculation algorithms
    /// Useful for testing or when switching between different stores
    /// </summary>
    void Reset();
} 