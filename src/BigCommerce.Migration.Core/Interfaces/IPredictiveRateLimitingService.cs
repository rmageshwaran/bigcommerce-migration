using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// High-level predictive rate limiting service interface
/// Provides the main entry point for zero-429-error rate limiting with circuit breaker protection
/// </summary>
public interface IPredictiveRateLimitingService
{
    /// <summary>
    /// Determines if a request can be processed based on predictive rate limiting
    /// Implements token consumption, circuit breaker logic, and coordination awareness
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if request can proceed, false if it should be throttled</returns>
    Task<bool> CanProcessRequestAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comprehensive rate limiting status for monitoring and debugging
    /// Includes quota health, coordination status, token allocation, and circuit breaker state
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete rate limiting status information</returns>
    Task<PredictiveRateLimitStatus> GetRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a manual quota refresh for a store
    /// Forces fresh data fetch and cleans up expired allocations
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task TriggerQuotaRefreshAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers emergency scale-back when quota is critically low
    /// Applies maximum safety buffer and opens circuit breaker temporarily
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task TriggerEmergencyScaleBackAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if predictive rate limiting is enabled for a store
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <returns>True if predictive rate limiting is active</returns>
    bool IsPredictiveEnabled(string storeId);
}