using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// High-level manager for instance coordination across multiple stores
/// Provides simplified interface for rate limiting integration with automatic lifecycle management
/// </summary>
public interface IInstanceCoordinationManager
{
    /// <summary>
    /// Ensures a store is registered for instance coordination (idempotent)
    /// Automatically registers with heartbeat management
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task EnsureStoreRegistrationAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of active instances for a store
    /// Returns 1 if coordination is disabled (conservative fallback)
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of active instances</returns>
    Task<int> GetActiveInstanceCountAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the IDs of all active instances for a store
    /// Returns current instance only if coordination is disabled
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active instance IDs</returns>
    Task<List<string>> GetActiveInstanceIdsAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets coordination health metrics for a store
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Coordination health information</returns>
    Task<CoordinationHealthMetrics> GetCoordinationHealthAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a request was processed by this instance (fire-and-forget)
    /// Used for load balancing metrics
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordRequestProcessedAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the load factor for this instance (fire-and-forget)
    /// Used for coordination efficiency metrics
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="loadFactor">Current load factor (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateLoadFactorAsync(string storeId, double loadFactor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current instance identifier
    /// </summary>
    /// <returns>Instance ID</returns>
    string GetCurrentInstanceId();

    /// <summary>
    /// Checks if instance coordination is enabled
    /// </summary>
    /// <returns>True if coordination is active</returns>
    bool IsCoordinationEnabled();

    /// <summary>
    /// Gets the list of stores registered for coordination
    /// </summary>
    /// <returns>List of registered store IDs</returns>
    List<string> GetRegisteredStores();

    /// <summary>
    /// Unregisters a store from instance coordination
    /// Removes from heartbeat management and coordinator
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnregisterStoreAsync(string storeId, CancellationToken cancellationToken = default);
}