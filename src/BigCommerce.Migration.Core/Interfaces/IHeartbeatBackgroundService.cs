namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for heartbeat background service management
/// Provides automatic heartbeat coordination for active stores
/// </summary>
public interface IHeartbeatBackgroundService
{
    /// <summary>
    /// Registers a store for automatic heartbeat management
    /// </summary>
    /// <param name="storeId">Store identifier to register</param>
    void RegisterStore(string storeId);

    /// <summary>
    /// Unregisters a store from automatic heartbeat management
    /// </summary>
    /// <param name="storeId">Store identifier to unregister</param>
    void UnregisterStore(string storeId);

    /// <summary>
    /// Gets the list of actively managed stores
    /// </summary>
    /// <returns>List of store IDs being managed</returns>
    List<string> GetActiveStores();
}