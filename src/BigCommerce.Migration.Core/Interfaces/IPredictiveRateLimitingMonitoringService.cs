

using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for real-time monitoring of predictive rate limiting with SignalR integration
/// Provides comprehensive visibility into quota tracking, instance coordination, and system health
/// </summary>
public interface IPredictiveRateLimitingMonitoringService
{
    /// <summary>
    /// Starts real-time monitoring for a store
    /// Begins publishing SignalR events for all predictive rate limiting components
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartMonitoringAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops real-time monitoring for a store
    /// Ceases publishing SignalR events for the specified store
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopMonitoringAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes real-time quota update event
    /// Includes quota utilization, health status, and token availability
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishQuotaUpdateAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes real-time predictive status event
    /// Includes token allocation, instance coordination, and circuit breaker status
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishPredictiveStatusAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes real-time system health event
    /// Includes overall health score, coordination efficiency, and recommended actions
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishSystemHealthAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes all status events for comprehensive monitoring
    /// Combines quota, predictive, and health status in one operation
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAllStatusAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets list of all stores currently being monitored
    /// </summary>
    /// <returns>List of store IDs with active monitoring</returns>
    List<string> GetActiveStores();

    /// <summary>
    /// Gets monitoring statistics for a store
    /// Includes event counts, publishing rates, and monitoring duration
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <returns>Monitoring statistics</returns>
    MonitoringStatistics GetMonitoringStatistics(string storeId);
}