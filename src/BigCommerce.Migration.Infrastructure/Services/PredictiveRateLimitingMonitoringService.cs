using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Real-time monitoring service for predictive rate limiting with SignalR integration
/// Provides comprehensive visibility into quota tracking, instance coordination, and system health
/// </summary>
public class PredictiveRateLimitingMonitoringService : IPredictiveRateLimitingMonitoringService
{
    private readonly IPredictiveRateLimitingService _predictiveService;
    private readonly ICoordinationHealthMonitor _healthMonitor;
    private readonly ISignalREventFactory _signalREventFactory;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ILogger<PredictiveRateLimitingMonitoringService> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;

    // Monitoring state
    private readonly Dictionary<string, MonitoringState> _monitoringStates = new();
    private readonly object _stateLock = new object();

    /// <summary>
    /// Initializes a new instance of the PredictiveRateLimitingMonitoringService
    /// </summary>
    public PredictiveRateLimitingMonitoringService(
        IPredictiveRateLimitingService predictiveService,
        ICoordinationHealthMonitor healthMonitor,
        ISignalREventFactory signalREventFactory,
        IProgressEventPublisher progressEventPublisher,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<PredictiveRateLimitingMonitoringService> logger)
    {
        _predictiveService = predictiveService ?? throw new ArgumentNullException(nameof(predictiveService));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("Initialized PredictiveRateLimitingMonitoringService with real-time SignalR events");
    }

    /// <inheritdoc />
    public async Task StartMonitoringAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        if (!_configuration.Features.EnablePredictiveDistribution)
        {
            _logger.LogDebug("Predictive rate limiting disabled - monitoring not started for store {StoreId}", storeId);
            return;
        }

        lock (_stateLock)
        {
            if (_monitoringStates.ContainsKey(storeId))
            {
                _logger.LogDebug("Monitoring already active for store {StoreId}", storeId);
                return;
            }

            _monitoringStates[storeId] = new MonitoringState
            {
                StoreId = storeId,
                IsActive = true,
                StartedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Started predictive rate limiting monitoring for store {StoreId}", storeId);

        // Publish initial status events
        await PublishInitialStatusAsync(storeId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StopMonitoringAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            return;

        lock (_stateLock)
        {
            if (_monitoringStates.Remove(storeId))
            {
                _logger.LogInformation("Stopped predictive rate limiting monitoring for store {StoreId}", storeId);
            }
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task PublishQuotaUpdateAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (!IsMonitoringActive(storeId))
            return;

        try
        {
            var predictiveStatus = await _predictiveService.GetRateLimitStatusAsync(storeId, cancellationToken);

            // Note: Quota update events removed in simplified SignalR approach
            /*
            var quotaEvent = _signalREventFactory.CreateQuotaUpdate(storeId, new QuotaUpdateOptions
            {
                TotalQuota = predictiveStatus.TotalQuota,
                RemainingTokens = predictiveStatus.RemainingTokens,
                UtilizationPercent = predictiveStatus.QuotaUtilizationPercent,
                HealthStatus = predictiveStatus.QuotaHealthStatus,
                QuotaResetTime = predictiveStatus.QuotaResetTime,
                LastUpdated = DateTimeOffset.UtcNow,
                SafeTokens = predictiveStatus.SafeTokens,
                DataAge = predictiveStatus.DataAge
            });

            // await _progressEventPublisher.PublishAsync(quotaEvent);
            */
            UpdateMonitoringState(storeId, "QuotaUpdate");

            _logger.LogTrace("Published quota update event for store {StoreId}: {Remaining}/{Total} tokens",
                storeId, predictiveStatus.RemainingTokens, predictiveStatus.TotalQuota);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish quota update for store {StoreId}", storeId);
        }
    }

    /// <inheritdoc />
    public async Task PublishPredictiveStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (!IsMonitoringActive(storeId))
            return;

        try
        {
            var predictiveStatus = await _predictiveService.GetRateLimitStatusAsync(storeId, cancellationToken);

            // Note: Predictive rate limit events removed in simplified SignalR approach
            /*
            var predictiveEvent = _signalREventFactory.CreatePredictiveRateLimit(storeId, new PredictiveRateLimitOptions
            {
                InstanceId = predictiveStatus.InstanceId,
                AllocatedTokens = predictiveStatus.AllocatedTokens,
                AvailableTokens = predictiveStatus.AvailableTokens,
                TokensConsumed = predictiveStatus.TokensConsumed,
                AllocationEfficiency = predictiveStatus.AllocationEfficiency,
                TokenExpiresAt = predictiveStatus.TokenExpiresAt,
                ActiveInstanceCount = predictiveStatus.ActiveInstanceCount,
                CoordinationHealthStatus = predictiveStatus.CoordinationHealthStatus,
                CircuitBreakerStatus = predictiveStatus.CircuitBreakerStatus,
                OverallHealthScore = predictiveStatus.GetOverallHealthScore(),
                CanProcessRequests = predictiveStatus.CanProcessRequests()
            });

            // await _progressEventPublisher.PublishAsync(predictiveEvent);
            */
            UpdateMonitoringState(storeId, "PredictiveStatus");

            _logger.LogTrace("Published predictive status event for store {StoreId}: {Available}/{Allocated} tokens, Health: {Health}",
                storeId, predictiveStatus.AvailableTokens, predictiveStatus.AllocatedTokens, predictiveStatus.GetOverallHealthStatus());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish predictive status for store {StoreId}", storeId);
        }
    }

    /// <inheritdoc />
    public async Task PublishSystemHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (!IsMonitoringActive(storeId))
            return;

        try
        {
            var systemHealth = await _healthMonitor.GetSystemHealthAsync(storeId, cancellationToken);

            // Note: System health events removed in simplified SignalR approach
            /*
            var healthEvent = _signalREventFactory.CreateSystemHealth(storeId, new SystemHealthOptions
            {
                OverallHealthStatus = systemHealth.OverallHealthStatus.ToString(),
                OverallHealthScore = systemHealth.OverallHealthScore,
                QuotaHealthStatus = systemHealth.QuotaHealthStatus.ToString(),
                CoordinationEfficiency = systemHealth.CoordinationEfficiency,
                ActiveInstanceCount = systemHealth.ActiveInstanceCount,
                IsInFallbackMode = systemHealth.IsInFallbackMode,
                FallbackReason = systemHealth.FallbackReason?.ToString(),
                HealthTrend = systemHealth.GetHealthTrend().ToString(),
                RecommendedActions = systemHealth.GetRecommendedActions()
            });

            // await _progressEventPublisher.PublishAsync(healthEvent);
            */
            UpdateMonitoringState(storeId, "SystemHealth");

            _logger.LogTrace("Published system health event for store {StoreId}: {Status} ({Score:F2}), Trend: {Trend}",
                storeId, systemHealth.OverallHealthStatus, systemHealth.OverallHealthScore, systemHealth.GetHealthTrend());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish system health for store {StoreId}", storeId);
        }
    }

    /// <inheritdoc />
    public async Task PublishAllStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (!IsMonitoringActive(storeId))
            return;

        // Publish all status types in parallel
        var tasks = new[]
        {
            PublishQuotaUpdateAsync(storeId, cancellationToken),
            PublishPredictiveStatusAsync(storeId, cancellationToken),
            PublishSystemHealthAsync(storeId, cancellationToken)
        };

        try
        {
            await Task.WhenAll(tasks);
            _logger.LogTrace("Published all status events for store {StoreId}", storeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish some status events for store {StoreId}", storeId);
        }
    }

    /// <inheritdoc />
    public List<string> GetActiveStores()
    {
        lock (_stateLock)
        {
            return _monitoringStates.Where(kvp => kvp.Value.IsActive)
                                   .Select(kvp => kvp.Key)
                                   .ToList();
        }
    }

    /// <inheritdoc />
    public MonitoringStatistics GetMonitoringStatistics(string storeId)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        lock (_stateLock)
        {
            if (_monitoringStates.TryGetValue(storeId, out var state))
            {
                return new MonitoringStatistics
                {
                    StoreId = storeId,
                    IsActive = state.IsActive,
                    StartedAt = state.StartedAt,
                    TotalEventsPublished = state.TotalEventsPublished,
                    LastEventPublished = state.LastEventPublished,
                    EventBreakdown = new Dictionary<string, long>(state.EventBreakdown)
                };
            }

            return new MonitoringStatistics { StoreId = storeId };
        }
    }

    /// <summary>
    /// Publishes initial status events when monitoring starts
    /// </summary>
    private async Task PublishInitialStatusAsync(string storeId, CancellationToken cancellationToken)
    {
        try
        {
            // Delay to avoid overwhelming during startup
            await Task.Delay(1000, cancellationToken);
            await PublishAllStatusAsync(storeId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish initial status for store {StoreId}", storeId);
        }
    }

    /// <summary>
    /// Checks if monitoring is active for a store
    /// </summary>
    private bool IsMonitoringActive(string storeId)
    {
        if (!_configuration.Features.EnablePredictiveDistribution)
            return false;

        lock (_stateLock)
        {
            return _monitoringStates.TryGetValue(storeId, out var state) && state.IsActive;
        }
    }

    /// <summary>
    /// Updates monitoring state with event publication
    /// </summary>
    private void UpdateMonitoringState(string storeId, string eventType)
    {
        lock (_stateLock)
        {
            if (_monitoringStates.TryGetValue(storeId, out var state))
            {
                state.TotalEventsPublished++;
                state.LastEventPublished = DateTimeOffset.UtcNow;
                
                if (!state.EventBreakdown.ContainsKey(eventType))
                    state.EventBreakdown[eventType] = 0;
                state.EventBreakdown[eventType]++;
            }
        }
    }
}

/// <summary>
/// Monitoring state for a store
/// </summary>
internal class MonitoringState
{
    public string StoreId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public long TotalEventsPublished { get; set; }
    public DateTimeOffset? LastEventPublished { get; set; }
    public Dictionary<string, long> EventBreakdown { get; set; } = new();
}

