using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for dynamic rate limiting extending base IRateLimitService
/// Follows Interface Segregation Principle - adds dynamic capabilities without breaking existing contracts
/// Follows Dependency Inversion Principle - depends on abstractions not concretions
/// Supports adaptive rate limiting based on API health and BigCommerce rate limit headers
/// </summary>
public interface IDynamicRateLimiter : IRateLimitService
{
    #region Dynamic Rate Calculation

    /// <summary>
    /// Gets the optimal rate limit based on current API health metrics
    /// Calculates adaptive rate between 5-50 requests per second based on:
    /// - BigCommerce API response headers (X-Rate-Limit-*)
    /// - Internal response time metrics  
    /// - Error rate analysis
    /// - System performance indicators
    /// </summary>
    /// <param name="storeId">Store identifier for context-specific rate calculation</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Optimal requests per second (5-50 range)</returns>
    Task<double> GetOptimalRateAsync(string storeId, CancellationToken cancellationToken = default);

    #endregion

    #region API Health Management

    /// <summary>
    /// Updates API health metrics with BigCommerce rate limit information from response headers
    /// Processes X-Rate-Limit-Requests-Left, X-Rate-Limit-Requests-Quota, 
    /// X-Rate-Limit-Time-Reset-Ms, X-Rate-Limit-Time-Window-Ms headers
    /// </summary>
    /// <param name="storeId">Store identifier for tracking per-store health</param>
    /// <param name="rateLimitInfo">BigCommerce rate limit data extracted from response headers</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Task representing the async operation</returns>
    Task UpdateApiHealthAsync(string storeId, BigCommerceRateLimitInfo rateLimitInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comprehensive API health metrics for the specified store
    /// Combines internal metrics (response time, error rate) with BigCommerce data
    /// Used for dynamic rate limit calculation and monitoring dashboards
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Comprehensive API health metrics</returns>
    Task<ApiHealthMetrics> GetApiHealthAsync(string storeId, CancellationToken cancellationToken = default);

    #endregion

    #region Enhanced Rate Limit Status

    /// <summary>
    /// Gets enhanced rate limit status with dynamic calculations
    /// Extends base RateLimitStatus with optimal rates, health scores, and performance insights
    /// Provides actionable information for rate adjustment decisions
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Enhanced rate limit status with dynamic information</returns>
    Task<EnhancedRateLimitStatus> GetEnhancedRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default);

    #endregion

    #region Health Monitoring Events

    /// <summary>
    /// Event triggered when API health changes significantly
    /// Allows subscribers to react to health degradation or improvement
    /// Useful for alerting and automatic scaling decisions
    /// </summary>
    event EventHandler<ApiHealthChangedEventArgs>? ApiHealthChanged;

    /// <summary>
    /// Event triggered when optimal rate changes
    /// Allows subscribers to adjust their request patterns dynamically
    /// Critical for real-time rate optimization
    /// </summary>
    event EventHandler<OptimalRateChangedEventArgs>? OptimalRateChanged;

    #endregion
}

 