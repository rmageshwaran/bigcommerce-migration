using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for monitoring and aggregating BigCommerce API health metrics
/// Follows Interface Segregation Principle - focused on health monitoring only
/// Enables testability through dependency injection and mocking
/// Provides real-time health data for dynamic rate limiting calculations
/// </summary>
public interface IApiHealthMonitor
{
    /// <summary>
    /// Records an API call for health monitoring
    /// Thread-safe method that aggregates call data for dynamic rate limiting
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="rateLimitInfo">BigCommerce rate limit information from headers (can be null)</param>
    /// <param name="responseTimeMs">Response time in milliseconds</param>
    /// <param name="isSuccess">Whether the API call was successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordApiCallAsync(
        string storeId,
        BigCommerceRateLimitInfo? rateLimitInfo,
        double responseTimeMs,
        bool isSuccess,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comprehensive API health metrics for the specified store
    /// Combines internal metrics with BigCommerce rate limit data
    /// Used for dynamic rate limiting calculations and monitoring dashboards
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>API health metrics for the store (returns default metrics if no data available)</returns>
    Task<ApiHealthMetrics> GetApiHealthAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears stored health data for a specific store
    /// Useful for testing or when store configuration changes
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    void ClearStoreData(string storeId);

    /// <summary>
    /// Gets the number of stores currently being monitored
    /// Useful for diagnostics and monitoring
    /// </summary>
    /// <returns>Count of stores with health data</returns>
    int GetMonitoredStoreCount();
} 