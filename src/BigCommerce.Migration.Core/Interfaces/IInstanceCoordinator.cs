using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for instance coordination and heartbeat management in distributed environments
/// Provides real-time instance discovery and health tracking
/// </summary>
public interface IInstanceCoordinator
{
    /// <summary>
    /// Unique identifier for this instance
    /// </summary>
    string InstanceId { get; }

    /// <summary>
    /// Register this instance in the coordination system
    /// Should be called on application startup
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RegisterInstanceAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send heartbeat to indicate this instance is alive and healthy
    /// Should be called periodically (recommended: every 30-60 seconds)
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendHeartbeatAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of all active instance IDs for a store
    /// Used for distributed coordination decisions
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="timeoutPeriod">How long to consider an instance active (null = use default)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active instance IDs</returns>
    Task<List<string>> GetActiveInstanceIdsAsync(string storeId, TimeSpan? timeoutPeriod = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of active instances for a store
    /// More efficient than getting full list when only count is needed
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="timeoutPeriod">How long to consider an instance active (null = use default)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of active instances</returns>
    Task<int> GetActiveInstanceCountAsync(string storeId, TimeSpan? timeoutPeriod = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed information about all active instances
    /// Includes heartbeat status, load metrics, and instance metadata
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="timeoutPeriod">How long to consider an instance active (null = use default)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active instance details</returns>
    Task<List<InstanceCoordinationEntity>> GetActiveInstancesAsync(string storeId, TimeSpan? timeoutPeriod = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove stale instance records that haven't sent heartbeats
    /// Should be called periodically to maintain data hygiene
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="timeoutPeriod">How long to wait before considering instance stale (null = use default)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of stale instances removed</returns>
    Task<int> CleanupStaleInstancesAsync(string storeId, TimeSpan? timeoutPeriod = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregister this instance from the coordination system
    /// Should be called on graceful shutdown
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnregisterInstanceAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record that this instance processed a request
    /// Used for load balancing and performance metrics
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordRequestProcessedAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update this instance's current load factor
    /// Used for intelligent load distribution
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="loadFactor">Current load factor (0.0 = idle, 1.0 = fully loaded)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateLoadFactorAsync(string storeId, double loadFactor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get comprehensive coordination health metrics
    /// Includes instance counts, heartbeat status, and coordination efficiency
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed coordination health metrics</returns>
    Task<CoordinationHealthMetrics> GetCoordinationHealthAsync(string storeId, CancellationToken cancellationToken = default);
}