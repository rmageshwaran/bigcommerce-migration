using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for distributed quota tracking with real-time BigCommerce API intelligence
/// Provides atomic quota updates with ETag-based conflict resolution
/// </summary>
public interface IDistributedQuotaTracker
{
    /// <summary>
    /// Gets the current quota state for a store
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current quota entity or null if not found</returns>
    Task<StoreQuotaEntity?> GetCurrentQuotaAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates quota from BigCommerce API response headers with anti-staleness protection
    /// Uses ETag-based atomic operations to prevent race conditions
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="quota">Current quota limit</param>
    /// <param name="remaining">Remaining tokens</param>
    /// <param name="resetTime">When quota resets</param>
    /// <param name="windowSeconds">Quota window duration</param>
    /// <param name="source">Source of the update (for audit)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if update was successful, false if rejected due to staleness</returns>
    Task<bool> UpdateQuotaFromHeadersAsync(
        string storeId, 
        int quota, 
        int remaining, 
        DateTimeOffset resetTime, 
        int windowSeconds = 60,
        string? source = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets quota health metrics for monitoring and dashboard display
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Quota health information</returns>
    Task<QuotaHealthMetrics> GetQuotaHealthAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a quota refresh by clearing cached data
    /// Used when quota data might be stale or corrupted
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RefreshQuotaAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if quota data is fresh enough for reliable decisions
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="maxAge">Maximum acceptable age</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if quota data is fresh</returns>
    Task<bool> IsQuotaFreshAsync(string storeId, TimeSpan maxAge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates quota from BigCommerce API response info
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="rateLimitInfo">BigCommerce rate limit information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if quota was updated successfully</returns>
    Task<bool> UpdateQuotaFromBigCommerceInfoAsync(string storeId, BigCommerceRateLimitInfo rateLimitInfo, CancellationToken cancellationToken = default);
}

