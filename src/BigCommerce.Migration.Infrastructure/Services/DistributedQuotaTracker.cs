using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;
using BigCommerce.Migration.Core.Services;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Distributed quota tracker with real-time BigCommerce API intelligence
/// Implements atomic quota updates with ETag-based conflict resolution and anti-staleness protection
/// </summary>
public class DistributedQuotaTracker : IDistributedQuotaTracker
{
    private readonly IRateLimitingTableStorageFactory _tableStorageFactory;
    private readonly ISignalREventFactory _signalREventFactory;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ILogger<DistributedQuotaTracker> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;
    
    // Throttling: Max 1 quota update per 5 seconds per store
    private readonly Dictionary<string, DateTimeOffset> _lastUpdateTimes = new();
    private readonly object _throttleLock = new object();

    /// <summary>
    /// Initializes a new instance of the DistributedQuotaTracker
    /// </summary>
    public DistributedQuotaTracker(
        IRateLimitingTableStorageFactory tableStorageFactory,
        ISignalREventFactory signalREventFactory,
        IProgressEventPublisher progressEventPublisher,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<DistributedQuotaTracker> logger)
    {
        _tableStorageFactory = tableStorageFactory ?? throw new ArgumentNullException(nameof(tableStorageFactory));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("Initialized DistributedQuotaTracker with anti-staleness protection and ETag conflict resolution");
    }

    /// <inheritdoc />
    public async Task<StoreQuotaEntity?> GetCurrentQuotaAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetQuotaTableClientAsync(cancellationToken);
            
            var response = await tableClient.GetEntityIfExistsAsync<StoreQuotaEntity>(
                storeId, "quota", cancellationToken: cancellationToken);

            if (!response.HasValue)
            {
                _logger.LogDebug("No quota data found for store {StoreId}", storeId);
                return null;
            }

            var quota = response.Value;
            if (quota != null)
            {
                _logger.LogDebug("Retrieved quota for store {StoreId}: {Remaining}/{Total} tokens, Health: {Health}",
                    storeId, quota.RemainingTokens, quota.CurrentQuota, quota.GetHealthStatus());
            }

            return quota;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current quota for store {StoreId}. Returning null to avoid breaking application functionality.", storeId);
            // Return null instead of throwing - let the caller handle the missing quota gracefully
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateQuotaFromHeadersAsync(
        string storeId, 
        int quota, 
        int remaining, 
        DateTimeOffset resetTime, 
        int windowSeconds = 60,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        if (quota <= 0 || remaining < 0)
        {
            _logger.LogWarning("Invalid quota data for store {StoreId}: {Remaining}/{Quota}", storeId, remaining, quota);
            return false;
        }

        // Throttling: Prevent excessive Table Storage operations
        if (ShouldThrottleUpdate(storeId))
        {
            _logger.LogDebug("Throttling quota update for store {StoreId} (too frequent)", storeId);
            return false;
        }

        const int maxRetries = 5;
        var baseDelay = 50; // Base delay in milliseconds
        var random = new Random();

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var tableClient = await _tableStorageFactory.GetQuotaTableClientAsync(cancellationToken);
                var updateTime = DateTimeOffset.UtcNow;

                // Get existing quota entity for ETag-based update
                var existingResponse = await tableClient.GetEntityIfExistsAsync<StoreQuotaEntity>(
                    storeId, "quota", cancellationToken: cancellationToken);

                StoreQuotaEntity quotaEntity;
                bool isNewEntity = false;

                if (existingResponse.HasValue)
                {
                    quotaEntity = existingResponse.Value ?? new StoreQuotaEntity
                    {
                        PartitionKey = storeId,
                        RowKey = "quota"
                    };
                    
                    // CRITICAL: Anti-staleness protection - reject updates older than existing data
                    if (quotaEntity != null && updateTime <= quotaEntity.LastUpdated)
                    {
                        _logger.LogDebug("Rejecting stale quota update for store {StoreId}: {UpdateTime} <= {ExistingTime}",
                            storeId, updateTime, quotaEntity.LastUpdated);
                        return false;
                    }

                    if (quotaEntity == null)
                    {
                        _logger.LogWarning("Null quota entity found for store {StoreId}", storeId);
                        return false;
                    }
                }
                else
                {
                    // Create new quota entity
                    quotaEntity = new StoreQuotaEntity
                    {
                        PartitionKey = storeId,
                        RowKey = "quota"
                    };
                    isNewEntity = true;
                }

                // Update quota data
                quotaEntity.UpdateFromBigCommerceHeaders(quota, remaining, resetTime, windowSeconds, source);

                // Atomic operation: Create or update with ETag
                if (isNewEntity)
                {
                    await tableClient.AddEntityAsync(quotaEntity, cancellationToken);
                    _logger.LogInformation("Created new quota for store {StoreId}: {Remaining}/{Quota} tokens",
                        storeId, remaining, quota);
                }
                else
                {
                    await tableClient.UpdateEntityAsync(quotaEntity, quotaEntity.ETag, cancellationToken: cancellationToken);
                    _logger.LogDebug("Updated quota for store {StoreId}: {Remaining}/{Quota} tokens, Health: {Health}",
                        storeId, remaining, quota, quotaEntity.GetHealthStatus());
                }

                // Update throttling tracker
                UpdateThrottleTracker(storeId);

                        // Real-time SignalR notification for dashboard visibility
        _ = Task.Run(async () => await PublishQuotaUpdateEventAsync(storeId, quotaEntity));
        // Fire-and-forget to not block quota updates

                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
            {
                // Another instance updated quota simultaneously - retry with exponential backoff + jitter
                var jitter = random.Next(0, 50); // Add randomness to prevent thundering herd
                var delay = (int)(baseDelay * Math.Pow(2, retry)) + jitter;
                
                _logger.LogDebug("ETag conflict updating quota for store {StoreId}, retry {Retry}/{Max} (delay: {Delay}ms)",
                    storeId, retry + 1, maxRetries, delay);

                if (retry < maxRetries - 1) // Don't delay on the last attempt
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update quota for store {StoreId} on attempt {Retry}/{Max}",
                    storeId, retry + 1, maxRetries);
                
                if (retry == maxRetries - 1) // Last attempt
                {
                    throw;
                }
            }
        }

        _logger.LogWarning("Failed to update quota for store {StoreId} after {Retries} retries due to ETag conflicts",
            storeId, maxRetries);
        return false;
    }

    /// <summary>
    /// Updates quota from existing BigCommerceRateLimitInfo (integration with existing ApiRequestHandler)
    /// </summary>
    /// <param name="rateLimitInfo">Parsed BigCommerce rate limit information</param>
    /// <param name="source">Source of the update (for audit)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if update was successful</returns>
    public async Task<bool> UpdateQuotaFromBigCommerceInfoAsync(
        BigCommerceRateLimitInfo rateLimitInfo,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        if (rateLimitInfo == null || !rateLimitInfo.IsValid())
        {
            _logger.LogWarning("Invalid BigCommerceRateLimitInfo provided for quota update");
            return false;
        }

        // Convert to our standard format
        var resetTime = DateTimeOffset.UtcNow.AddMilliseconds(rateLimitInfo.TimeResetMs);
        var windowSeconds = (int)(rateLimitInfo.TimeWindowMs / 1000);

        return await UpdateQuotaFromHeadersAsync(
            rateLimitInfo.StoreId,
            rateLimitInfo.RequestsQuota,
            rateLimitInfo.RequestsLeft,
            resetTime,
            windowSeconds,
            source,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<QuotaHealthMetrics> GetQuotaHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var quota = await GetCurrentQuotaAsync(storeId, cancellationToken);
            
            if (quota == null)
            {
                return new QuotaHealthMetrics
                {
                    StoreId = storeId,
                    HealthStatus = QuotaHealthStatus.Critical,
                    QuotaUtilizationPercent = 1.0,
                    SafeTokens = 0,
                    TotalQuota = 0,
                    RemainingTokens = 0,
                    QuotaResetTime = DateTimeOffset.UtcNow,
                    DataAge = TimeSpan.MaxValue,
                    ConsecutiveFailures = int.MaxValue
                };
            }

            var dataAge = DateTimeOffset.UtcNow - quota.LastUpdated;
            var safeTokens = quota.CalculateSafeTokens(
                _configuration.Predictive.SafetyBufferPercentage,
                _configuration.Predictive.HealthyQuotaThreshold,
                _configuration.Predictive.CriticalQuotaThreshold);

            return new QuotaHealthMetrics
            {
                StoreId = storeId,
                HealthStatus = quota.GetHealthStatus(
                    _configuration.Predictive.HealthyQuotaThreshold,
                    _configuration.Predictive.CriticalQuotaThreshold),
                QuotaUtilizationPercent = quota.GetQuotaUtilization(),
                SafeTokens = safeTokens,
                TotalQuota = quota.CurrentQuota,
                RemainingTokens = quota.RemainingTokens,
                QuotaResetTime = quota.QuotaResetTime,
                DataAge = dataAge,
                ConsecutiveFailures = quota.ConsecutiveFailures
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get quota health for store {StoreId}. Returning critical health status to avoid breaking application functionality.", storeId);
            
            // Return a critical health status instead of throwing
            // This ensures the application continues to function even if quota health check fails
            return new QuotaHealthMetrics
            {
                StoreId = storeId,
                HealthStatus = QuotaHealthStatus.Critical,
                QuotaUtilizationPercent = 1.0, // Assume worst case
                SafeTokens = 1, // Minimal safe allocation to keep things moving
                TotalQuota = 100, // Conservative estimate
                RemainingTokens = 1,
                QuotaResetTime = DateTimeOffset.UtcNow.AddHours(1), // Assume 1-hour reset
                DataAge = TimeSpan.MaxValue,
                ConsecutiveFailures = int.MaxValue
            };
        }
    }

    /// <inheritdoc />
    public async Task RefreshQuotaAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetQuotaTableClientAsync(cancellationToken);
            
            // Delete existing quota to force refresh
            try
            {
                await tableClient.DeleteEntityAsync(storeId, "quota", ETag.All, cancellationToken);
                _logger.LogInformation("Refreshed quota data for store {StoreId} (deleted stale data)", storeId);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Entity doesn't exist - that's fine
                _logger.LogDebug("No existing quota to refresh for store {StoreId}", storeId);
            }

            // Clear throttling for this store to allow immediate update
            lock (_throttleLock)
            {
                _lastUpdateTimes.Remove(storeId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh quota for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsQuotaFreshAsync(string storeId, TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        try
        {
            var quota = await GetCurrentQuotaAsync(storeId, cancellationToken);
            return quota != null && !quota.IsStale(maxAge);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check quota freshness for store {StoreId}", storeId);
            return false;
        }
    }

    /// <summary>
    /// Checks if quota update should be throttled to prevent excessive Table Storage operations
    /// </summary>
    private bool ShouldThrottleUpdate(string storeId)
    {
        lock (_throttleLock)
        {
            if (_lastUpdateTimes.TryGetValue(storeId, out var lastUpdate))
            {
                var timeSinceLastUpdate = DateTimeOffset.UtcNow - lastUpdate;
                return timeSinceLastUpdate < TimeSpan.FromSeconds(5); // 5-second throttle
            }
            
            return false; // No previous update - don't throttle
        }
    }

    /// <summary>
    /// Updates the throttling tracker for a store
    /// </summary>
    private void UpdateThrottleTracker(string storeId)
    {
        lock (_throttleLock)
        {
            _lastUpdateTimes[storeId] = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Publishes quota update event for real-time dashboard visibility
    /// </summary>
    private async Task PublishQuotaUpdateEventAsync(string storeId, StoreQuotaEntity quota)
    {
        try
        {
            var healthStatus = quota.GetHealthStatus(
                _configuration.Predictive.HealthyQuotaThreshold,
                _configuration.Predictive.CriticalQuotaThreshold);

            var quotaEvent = _signalREventFactory.CreateQuotaUpdate(storeId, new QuotaUpdateOptions
            {
                TotalQuota = quota.CurrentQuota,
                RemainingTokens = quota.RemainingTokens,
                UtilizationPercent = quota.GetQuotaUtilization(),
                HealthStatus = healthStatus.ToString(),
                SafeTokens = quota.CalculateSafeTokens(
                    _configuration.Predictive.SafetyBufferPercentage,
                    _configuration.Predictive.HealthyQuotaThreshold,
                    _configuration.Predictive.CriticalQuotaThreshold),
                QuotaResetTime = quota.QuotaResetTime,
                LastUpdated = quota.LastUpdated
            });

            await _progressEventPublisher.PublishAsync(quotaEvent);

            _logger.LogDebug("Published quota update event for store {StoreId}: {Remaining}/{Total} tokens ({Utilization:P1}), Health: {Health}",
                storeId, quota.RemainingTokens, quota.CurrentQuota, quota.GetQuotaUtilization(), healthStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish quota update event for store {StoreId}", storeId);
            // Don't throw - this is not critical for quota tracking functionality
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateQuotaFromBigCommerceInfoAsync(string storeId, BigCommerceRateLimitInfo rateLimitInfo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        if (rateLimitInfo == null)
            throw new ArgumentNullException(nameof(rateLimitInfo));

        try
        {
            // Extract quota information from BigCommerce rate limit info
            var quota = rateLimitInfo.RequestsQuota;
            var remaining = rateLimitInfo.RequestsLeft;
            var resetTime = DateTimeOffset.UtcNow.AddMilliseconds(rateLimitInfo.TimeResetMs);
            var windowSeconds = (int)(rateLimitInfo.TimeWindowMs / 1000);

            return await UpdateQuotaFromHeadersAsync(storeId, quota, remaining, resetTime, windowSeconds, "BigCommerce API", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update quota from BigCommerce info for store {StoreId}", storeId);
            return false;
        }
    }
}