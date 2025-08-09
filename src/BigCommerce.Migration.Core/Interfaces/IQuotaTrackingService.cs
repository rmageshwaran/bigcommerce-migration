using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for quota tracking service integration
/// Provides fire-and-forget quota updates for existing API request handlers
/// </summary>
public interface IQuotaTrackingService
{
    /// <summary>
    /// Processes BigCommerce rate limit information asynchronously (fire-and-forget)
    /// Does not block API request processing
    /// </summary>
    /// <param name="rateLimitInfo">Parsed BigCommerce rate limit information</param>
    /// <param name="source">Source of the update (for audit)</param>
    void ProcessRateLimitInfo(BigCommerceRateLimitInfo rateLimitInfo, string? source = null);

    /// <summary>
    /// Checks if quota tracking is enabled and active
    /// </summary>
    /// <returns>True if quota tracking is enabled</returns>
    bool IsQuotaTrackingEnabled();

    /// <summary>
    /// Gets quota health metrics for a store
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Quota health information</returns>
    Task<QuotaHealthMetrics> GetQuotaHealthAsync(string storeId, CancellationToken cancellationToken = default);
}