using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for coordination health monitoring with advanced reliability patterns
/// Provides circuit breaker functionality, thundering herd prevention, and split-brain detection
/// </summary>
public interface ICoordinationHealthMonitor
{
    /// <summary>
    /// Gets comprehensive system health state for a store
    /// Aggregates health from quota tracking, instance coordination, and token consensus
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete system health state</returns>
    Task<SystemHealthState> GetSystemHealthAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the system is healthy for a store (quick health check)
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if system is healthy</returns>
    Task<bool> IsSystemHealthyAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if a coordination operation can be executed safely
    /// Implements thundering herd protection and health-based authorization
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="operation">Coordination operation to authorize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if operation can be executed safely</returns>
    Task<bool> CanExecuteOperationAsync(string storeId, CoordinationOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers conservative fallback mode for system protection
    /// Implements circuit breaker behavior and conservative operation mode
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="reason">Reason for triggering fallback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task TriggerConservativeFallbackAsync(string storeId, FallbackReason reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to recover the system from fallback mode
    /// Validates system health and exits fallback mode if appropriate
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if recovery was successful</returns>
    Task<bool> AttemptSystemRecoveryAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets performance metrics for monitoring and optimization
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <returns>Performance metrics</returns>
    PerformanceMetrics GetPerformanceMetrics(string storeId);

    /// <summary>
    /// Gets list of all active stores being monitored
    /// </summary>
    /// <returns>List of store IDs</returns>
    List<string> GetActiveStores();
}