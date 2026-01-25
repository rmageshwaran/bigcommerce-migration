using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Service for integrating quota tracking with existing API request processing
/// Provides fire-and-forget quota updates to not impact API request performance
/// </summary>
public class QuotaTrackingService : IQuotaTrackingService
{
    private readonly IDistributedQuotaTracker _quotaTracker;
    private readonly ILogger<QuotaTrackingService> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the QuotaTrackingService
    /// </summary>
    public QuotaTrackingService(
        IDistributedQuotaTracker quotaTracker,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<QuotaTrackingService> logger)
    {
        _quotaTracker = quotaTracker ?? throw new ArgumentNullException(nameof(quotaTracker));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes BigCommerce rate limit information asynchronously
    /// Fire-and-forget operation to not block API requests
    /// </summary>
    public void ProcessRateLimitInfo(BigCommerceRateLimitInfo rateLimitInfo, string? source = null)
    {
        if (!_configuration.Features.EnableQuotaTracking || 
            !_configuration.Features.EnablePredictiveDistribution)
        {
            // Quota tracking disabled - skip processing
            return;
        }

        if (rateLimitInfo == null || !rateLimitInfo.IsValid())
        {
            _logger.LogDebug("Invalid rate limit info provided - skipping quota tracking");
            return;
        }

        // Fire-and-forget: Don't block API request processing
        _ = Task.Run(async () => await ProcessRateLimitInfoAsync(rateLimitInfo, source));
    }

    /// <summary>
    /// Internal async processing of rate limit information
    /// </summary>
    private async Task ProcessRateLimitInfoAsync(BigCommerceRateLimitInfo rateLimitInfo, string? source)
    {
        try
        {
            var success = await _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(rateLimitInfo.StoreId, rateLimitInfo);
            
            if (success)
            {
                _logger.LogDebug("Successfully processed quota update for store {StoreId}: {Remaining}/{Total} tokens",
                    rateLimitInfo.StoreId, rateLimitInfo.RequestsLeft, rateLimitInfo.RequestsQuota);
            }
            else
            {
                _logger.LogDebug("Quota update was throttled or rejected for store {StoreId}", rateLimitInfo.StoreId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process quota tracking for store {StoreId}", rateLimitInfo.StoreId);
            // Don't throw - this is fire-and-forget
        }
    }

    /// <summary>
    /// Checks if quota tracking is enabled and healthy
    /// </summary>
    public bool IsQuotaTrackingEnabled()
    {
        return _configuration.Features.EnableQuotaTracking && 
               _configuration.Features.EnablePredictiveDistribution;
    }

    /// <summary>
    /// Gets quota health for a store (convenience method)
    /// </summary>
    public async Task<QuotaHealthMetrics> GetQuotaHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (!IsQuotaTrackingEnabled())
        {
            return new QuotaHealthMetrics
            {
                StoreId = storeId,
                HealthStatus = QuotaHealthStatus.Critical,
                QuotaUtilizationPercent = 0,
                SafeTokens = 0,
                TotalQuota = 0,
                RemainingTokens = 0,
                DataAge = TimeSpan.MaxValue,
                ConsecutiveFailures = 0
            };
        }

        return await _quotaTracker.GetQuotaHealthAsync(storeId, cancellationToken);
    }
}