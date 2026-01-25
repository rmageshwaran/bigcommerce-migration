using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Enhanced dynamic rate limiter interface with predictive capabilities and distributed coordination
/// Extends IDynamicRateLimiter with advanced features for zero-429-error guarantee
/// Provides predictive quota management, multi-instance coordination, and real-time adaptation
/// </summary>
public interface IEnhancedDynamicRateLimiter : IDynamicRateLimiter
{
    #region Enhanced Predictive Methods

    /// <summary>
    /// Records an API call result for predictive analysis and rate optimization
    /// Updates internal metrics used for future rate calculations and coordination
    /// </summary>
    /// <param name="storeId">Store identifier for tracking per-store performance</param>
    /// <param name="responseTime">Time taken for the API call to complete</param>
    /// <param name="success">Whether the API call was successful (not 429 or other error)</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Task representing the async operation</returns>
    Task RecordApiCallAsync(string storeId, TimeSpan responseTime, bool success, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets predictive rate recommendation based on current state and historical data
    /// Provides recommended rate with predictive analysis
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Recommended rate per second based on predictive analysis</returns>
    Task<double> GetPredictiveRateAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets predictive rate limiting status with safety information
    /// Provides current state and recommendations for quota management
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Predictive rate limiting status</returns>
    Task<PredictiveRateLimitStatus> GetPredictiveStatusAsync(string storeId, CancellationToken cancellationToken = default);

    #endregion

    #region Enhanced Events

    /// <summary>
    /// Event triggered when predictive rate limiting status changes
    /// Allows subscribers to react to safety level changes (Normal -> Warning -> Critical)
    /// </summary>
    event EventHandler<string>? PredictiveStatusChanged;

    #endregion

    #region Coordination and Performance

    /// <summary>
    /// Gets system health state including coordination metrics
    /// Shows how well instances are coordinating their API usage
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>System health state with coordination metrics</returns>
    Task<SystemHealthState> GetCoordinationHealthAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets performance metrics for rate limiting analysis
    /// Provides analysis of rate limiting effectiveness and optimization opportunities
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="timeWindow">Time window for metrics calculation (default: last hour)</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Performance metrics with analysis</returns>
    Task<PerformanceMetrics> GetPerformanceMetricsAsync(string storeId, TimeSpan? timeWindow = null, CancellationToken cancellationToken = default);

    #endregion
}