using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Enhanced dynamic rate limiting service that integrates predictive rate limiting with existing functionality
/// Provides zero-429-error guarantee through distributed coordination and real-time quota intelligence
/// </summary>
public class EnhancedDynamicRateLimitService : IDynamicRateLimiter
{
    private readonly IDynamicRateLimiter _baseDynamicRateLimiter;
    private readonly IPredictiveRateLimitingService _predictiveService;
    private readonly ICoordinationHealthMonitor _healthMonitor;
    private readonly IQuotaTrackingService _quotaTrackingService;
    private readonly ILogger<EnhancedDynamicRateLimitService> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;

    // Performance tracking
    private readonly Dictionary<string, RequestMetrics> _requestMetrics = new();
    private readonly object _metricsLock = new object();

    /// <summary>
    /// Event raised when the optimal request rate changes
    /// </summary>
    public event EventHandler<OptimalRateChangedEventArgs>? OptimalRateChanged;

    /// <summary>
    /// Event raised when the API health status changes
    /// </summary>
    public event EventHandler<ApiHealthChangedEventArgs>? ApiHealthChanged;

    /// <summary>
    /// Event raised when the predictive rate limiting status changes
    /// </summary>
    public event EventHandler<PredictiveStatusChangedEventArgs>? PredictiveStatusChanged;

    /// <summary>
    /// Initializes a new instance of the EnhancedDynamicRateLimitService
    /// </summary>
    public EnhancedDynamicRateLimitService(
        IDynamicRateLimiter baseDynamicRateLimiter,
        IPredictiveRateLimitingService predictiveService,
        ICoordinationHealthMonitor healthMonitor,
        IQuotaTrackingService quotaTrackingService,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<EnhancedDynamicRateLimitService> logger)
    {
        _baseDynamicRateLimiter = baseDynamicRateLimiter ?? throw new ArgumentNullException(nameof(baseDynamicRateLimiter));
        _predictiveService = predictiveService ?? throw new ArgumentNullException(nameof(predictiveService));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _quotaTrackingService = quotaTrackingService ?? throw new ArgumentNullException(nameof(quotaTrackingService));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Forward events from base service
        _baseDynamicRateLimiter.OptimalRateChanged += (sender, args) => OptimalRateChanged?.Invoke(sender, args);
        _baseDynamicRateLimiter.ApiHealthChanged += (sender, args) => ApiHealthChanged?.Invoke(sender, args);

        _logger.LogInformation("Initialized EnhancedDynamicRateLimitService with predictive capabilities");
    }

    /// <inheritdoc />
    public async Task<double> GetOptimalRateAsync(string storeId, CancellationToken cancellationToken = default)
    {
        // If predictive rate limiting is disabled, use base service
        if (!_configuration.Features.EnablePredictiveDistribution)
        {
            return await _baseDynamicRateLimiter.GetOptimalRateAsync(storeId, cancellationToken);
        }

        try
        {
            // Get predictive rate limit status
            var predictiveStatus = await _predictiveService.GetRateLimitStatusAsync(storeId, cancellationToken);
            
            // Calculate optimal rate based on predictive intelligence
            var optimalRate = CalculatePredictiveOptimalRate(predictiveStatus);
            
            _logger.LogDebug("Calculated predictive optimal rate {OptimalRate} for store {StoreId} " +
                             "(Predictive: {CanProcess}, Available: {AvailableTokens}, Health: {Health})",
                optimalRate, storeId, predictiveStatus.CanProcessRequests(), 
                predictiveStatus.AvailableTokens, predictiveStatus.GetOverallHealthStatus());

            // Raise event to notify subscribers of predictive status change
            PredictiveStatusChanged?.Invoke(this, new PredictiveStatusChangedEventArgs
            {
                StoreId = storeId,
                Status = predictiveStatus,
                Timestamp = DateTimeOffset.UtcNow
            });

            return optimalRate;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate predictive optimal rate for store {StoreId}, falling back to base service", storeId);
            return await _baseDynamicRateLimiter.GetOptimalRateAsync(storeId, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> CanMakeRequestAsync(string storeId, CancellationToken cancellationToken = default)
    {
        // Always check coordination health first
        var systemHealthy = await _healthMonitor.IsSystemHealthyAsync(storeId, cancellationToken);
        if (!systemHealthy)
        {
            _logger.LogDebug("System unhealthy for store {StoreId} - denying request", storeId);
            return false;
        }

        // If predictive rate limiting is enabled, use it for decision
        if (_configuration.Features.EnablePredictiveDistribution)
        {
            try
            {
                var canProcess = await _predictiveService.CanProcessRequestAsync(storeId, cancellationToken);
                
                if (canProcess)
                {
                    // Record successful authorization
                    RecordRequestDecision(storeId, true, "predictive-allowed");
                    return true;
                }
                else
                {
                    // Predictive service denied - check if we should fallback to base service
                    if (await ShouldFallbackToBaseService(storeId, cancellationToken))
                    {
                        var baseDecision = await _baseDynamicRateLimiter.CanMakeRequestAsync(storeId, cancellationToken);
                        RecordRequestDecision(storeId, baseDecision, "fallback-to-base");
                        return baseDecision;
                    }
                    else
                    {
                        RecordRequestDecision(storeId, false, "predictive-denied");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Predictive rate limiting failed for store {StoreId}, falling back to base service", storeId);
                var baseDecision = await _baseDynamicRateLimiter.CanMakeRequestAsync(storeId, cancellationToken);
                RecordRequestDecision(storeId, baseDecision, "exception-fallback");
                return baseDecision;
            }
        }
        else
        {
            // Predictive disabled - use base service
            var baseDecision = await _baseDynamicRateLimiter.CanMakeRequestAsync(storeId, cancellationToken);
            RecordRequestDecision(storeId, baseDecision, "base-service");
            return baseDecision;
        }
    }

    /// <inheritdoc />
    public async Task UpdateApiHealthAsync(string storeId, BigCommerceRateLimitInfo rateLimitInfo, CancellationToken cancellationToken = default)
    {
        // Update base service
        await _baseDynamicRateLimiter.UpdateApiHealthAsync(storeId, rateLimitInfo, cancellationToken);

        // Update predictive quota tracking if enabled
        if (_configuration.Features.EnableQuotaTracking && rateLimitInfo != null)
        {
            _quotaTrackingService.ProcessRateLimitInfo(rateLimitInfo, "api-response");
        }
    }

    /// <inheritdoc />
    public async Task<ApiHealthMetrics> GetApiHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        return await _baseDynamicRateLimiter.GetApiHealthAsync(storeId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BigCommerce.Migration.Core.Models.EnhancedRateLimitStatus> GetEnhancedRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        // Get base enhanced status
        var baseStatus = await _baseDynamicRateLimiter.GetEnhancedRateLimitStatusAsync(storeId, cancellationToken);
        
        // If predictive is disabled, return base status with indication
        if (!_configuration.Features.EnablePredictiveDistribution)
        {
            baseStatus.AdjustmentReason += " (Predictive rate limiting disabled)";
            return baseStatus;
        }

        try
        {
            // Enhance with predictive data
            var predictiveStatus = await _predictiveService.GetRateLimitStatusAsync(storeId, cancellationToken);
            var systemHealth = await _healthMonitor.GetSystemHealthAsync(storeId, cancellationToken);
            
            // Override base status with predictive intelligence
            if (predictiveStatus.AvailableTokens > 0)
            {
                baseStatus.OptimalRate = Math.Max(baseStatus.OptimalRate, predictiveStatus.AvailableTokens);
                baseStatus.AdjustmentReason = "Rate optimized using predictive token allocation";
                baseStatus.RateAdjustmentFactor = predictiveStatus.GetOverallHealthScore();
            }
            
            if (systemHealth.OverallHealthScore < 0.7)
            {
                baseStatus.HealthScore = systemHealth.OverallHealthScore * 100;
                baseStatus.AdjustmentReason += $" (System health: {systemHealth.OverallHealthStatus})";
            }

            // Raise event to notify subscribers of predictive status change
            PredictiveStatusChanged?.Invoke(this, new PredictiveStatusChangedEventArgs
            {
                StoreId = storeId,
                Status = predictiveStatus,
                Timestamp = DateTimeOffset.UtcNow
            });
            
            return baseStatus;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enhance status with predictive data for store {StoreId}", storeId);
            baseStatus.AdjustmentReason += " (Predictive data unavailable)";
            return baseStatus;
        }
    }

    /// <inheritdoc />
    public async Task RecordApiCallAsync(string storeId, string url, double elapsedMilliseconds, bool isSuccess, CancellationToken cancellationToken = default)
    {
        // Record in base service
        await _baseDynamicRateLimiter.RecordApiCallAsync(storeId, url, elapsedMilliseconds, isSuccess, cancellationToken);

        // Record in request metrics
        RecordApiCallMetrics(storeId, elapsedMilliseconds, isSuccess);

        // Update coordination health if enabled
        if (_configuration.Features.EnableInstanceCoordination)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var canExecute = await _healthMonitor.CanExecuteOperationAsync(storeId, CoordinationOperation.HealthCheck, cancellationToken);
                    if (canExecute)
                    {
                        await _healthMonitor.GetSystemHealthAsync(storeId, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Failed to update coordination health for store {StoreId}", storeId);
                }
            });
        }
    }

    /// <inheritdoc />
    public async Task<RateLimitStatus> GetRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        return await _baseDynamicRateLimiter.GetRateLimitStatusAsync(storeId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CalculateDelayAsync(string storeId, CancellationToken cancellationToken = default)
    {
        // If predictive is enabled and we're in fallback mode, use intelligent delays
        if (_configuration.Features.EnablePredictiveDistribution)
        {
            try
            {
                var systemHealth = await _healthMonitor.GetSystemHealthAsync(storeId, cancellationToken);
                if (systemHealth.IsInFallbackMode)
                {
                    return CalculateIntelligentFallbackDelay(systemHealth);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to get system health for delay calculation, using base service");
            }
        }

        return await _baseDynamicRateLimiter.CalculateDelayAsync(storeId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RateLimitResult> CheckRateLimitAsync(string storeId, CancellationToken cancellationToken = default)
    {
        return await _baseDynamicRateLimiter.CheckRateLimitAsync(storeId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CheckAndWaitAsync(string storeId, CancellationToken cancellationToken = default)
    {
        var canProceed = await CanMakeRequestAsync(storeId, cancellationToken);
        
        if (!canProceed)
        {
            var delay = await CalculateDelayAsync(storeId, cancellationToken);
            _logger.LogInformation("Enhanced rate limiting delay for store {StoreId}: {DelayMs}ms", storeId, delay);
            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void RegisterParallelContext(string storeId, int concurrentBatches, int totalBatches)
    {
        _baseDynamicRateLimiter.RegisterParallelContext(storeId, concurrentBatches, totalBatches);
    }

    /// <inheritdoc />
    public void UnregisterParallelContext(string storeId)
    {
        _baseDynamicRateLimiter.UnregisterParallelContext(storeId);
    }

    /// <inheritdoc />
    public void ResetConsecutiveRateLimitHits(string storeId)
    {
        _baseDynamicRateLimiter.ResetConsecutiveRateLimitHits(storeId);
    }

    /// <summary>
    /// Gets comprehensive predictive rate limiting status for monitoring
    /// </summary>
    public async Task<PredictiveRateLimitStatus> GetPredictiveStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (!_configuration.Features.EnablePredictiveDistribution)
        {
            throw new InvalidOperationException("Predictive rate limiting is disabled");
        }

        return await _predictiveService.GetRateLimitStatusAsync(storeId, cancellationToken);
    }

    /// <summary>
    /// Triggers manual quota refresh for predictive system
    /// </summary>
    public async Task TriggerQuotaRefreshAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (!_configuration.Features.EnablePredictiveDistribution)
        {
            _logger.LogWarning("Quota refresh requested but predictive rate limiting is disabled for store {StoreId}", storeId);
            return;
        }

        await _predictiveService.TriggerQuotaRefreshAsync(storeId, cancellationToken);
        _logger.LogInformation("Manual quota refresh triggered for store {StoreId}", storeId);
    }

    /// <summary>
    /// Triggers emergency scale-back for critical quota situations
    /// </summary>
    public async Task TriggerEmergencyScaleBackAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (!_configuration.Features.EnablePredictiveDistribution)
        {
            _logger.LogWarning("Emergency scale-back requested but predictive rate limiting is disabled for store {StoreId}", storeId);
            return;
        }

        await _predictiveService.TriggerEmergencyScaleBackAsync(storeId, cancellationToken);
        _logger.LogWarning("Emergency scale-back triggered for store {StoreId}", storeId);
    }

    /// <summary>
    /// Calculates optimal rate based on predictive status
    /// </summary>
    private double CalculatePredictiveOptimalRate(PredictiveRateLimitStatus predictiveStatus)
    {
        // Base rate calculation
        var baseRate = 5.0; // Conservative default
        
        // Adjust based on available tokens
        if (predictiveStatus.AvailableTokens > 50)
            baseRate = 20.0; // High availability
        else if (predictiveStatus.AvailableTokens > 20)
            baseRate = 15.0; // Moderate availability
        else if (predictiveStatus.AvailableTokens > 5)
            baseRate = 10.0; // Low availability
        else if (predictiveStatus.AvailableTokens > 0)
            baseRate = 5.0;  // Very low availability
        else
            baseRate = 1.0;  // Minimal rate when no tokens

        // Adjust based on system health
        var healthMultiplier = predictiveStatus.GetOverallHealthScore();
        baseRate *= healthMultiplier;

        // Adjust based on coordination efficiency
        if (predictiveStatus.IsCoordinationEnabled && predictiveStatus.CoordinationEfficiency < 50)
        {
            baseRate *= 0.7; // Reduce rate if coordination is poor
        }

        // Circuit breaker influence
        if (predictiveStatus.CircuitBreakerStatus.ToLowerInvariant() == "open")
        {
            baseRate = 0.1; // Nearly stop when circuit is open
        }
        else if (predictiveStatus.CircuitBreakerStatus.ToLowerInvariant() == "halfopen")
        {
            baseRate *= 0.5; // Reduce rate in half-open state
        }

        return Math.Max(0.1, Math.Min(50.0, baseRate)); // Constrain between 0.1 and 50 req/sec
    }

    /// <summary>
    /// Determines if we should fallback to base service
    /// </summary>
    private async Task<bool> ShouldFallbackToBaseService(string storeId, CancellationToken cancellationToken)
    {
        try
        {
            var systemHealth = await _healthMonitor.GetSystemHealthAsync(storeId, cancellationToken);
            
            // Fallback if system is in critical state or fallback mode
            return systemHealth.OverallHealthStatus == SystemHealthStatus.Critical ||
                   systemHealth.IsInFallbackMode;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to check system health for fallback decision - defaulting to no fallback");
            return false;
        }
    }

    /// <summary>
    /// Calculates intelligent delay during fallback mode
    /// </summary>
    private int CalculateIntelligentFallbackDelay(SystemHealthState systemHealth)
    {
        var baseDelay = systemHealth.FallbackReason switch
        {
            FallbackReason.SplitBrainDetected => 5000,      // 5 seconds for split-brain
            FallbackReason.CoordinationFailure => 3000,     // 3 seconds for coordination issues
            FallbackReason.HighConflictRate => 2000,        // 2 seconds for high conflicts
            FallbackReason.PerformanceDegradation => 1500,  // 1.5 seconds for performance issues
            FallbackReason.QuotaDataStale => 1000,          // 1 second for stale data
            _ => 2000 // Default 2 seconds
        };

        // Add jitter to prevent thundering herd
        var jitter = Random.Shared.Next(-500, 500);
        return Math.Max(500, baseDelay + jitter);
    }

    /// <summary>
    /// Records request decision metrics
    /// </summary>
    private void RecordRequestDecision(string storeId, bool allowed, string reason)
    {
        lock (_metricsLock)
        {
            if (!_requestMetrics.TryGetValue(storeId, out var metrics))
            {
                metrics = new RequestMetrics { StoreId = storeId };
                _requestMetrics[storeId] = metrics;
            }

            metrics.RecordDecision(allowed, reason);
        }
    }

    /// <summary>
    /// Records API call metrics
    /// </summary>
    private void RecordApiCallMetrics(string storeId, double elapsedMs, bool isSuccess)
    {
        lock (_metricsLock)
        {
            if (!_requestMetrics.TryGetValue(storeId, out var metrics))
            {
                metrics = new RequestMetrics { StoreId = storeId };
                _requestMetrics[storeId] = metrics;
            }

            metrics.RecordApiCall(elapsedMs, isSuccess);
        }
    }

    /// <summary>
    /// Gets request metrics for a store
    /// </summary>
    private RequestMetrics GetRequestMetrics(string storeId)
    {
        lock (_metricsLock)
        {
            return _requestMetrics.TryGetValue(storeId, out var metrics) 
                ? metrics.Clone() 
                : new RequestMetrics { StoreId = storeId };
        }
    }
}



/// <summary>
/// Request metrics for monitoring enhanced rate limiting
/// </summary>
/// <summary>
/// Metrics for tracking request decisions and performance
/// </summary>
public class RequestMetrics
{
    /// <summary>
    /// Store identifier for these metrics
    /// </summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// Total number of requests processed
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Number of requests that were allowed
    /// </summary>
    public long AllowedRequests { get; set; }

    /// <summary>
    /// Number of requests that were denied
    /// </summary>
    public long DeniedRequests { get; set; }

    /// <summary>
    /// Reasons for request decisions and their counts
    /// </summary>
    public Dictionary<string, long> DecisionReasons { get; set; } = new();

    /// <summary>
    /// Average latency in milliseconds for API calls
    /// </summary>
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// Success rate of API calls (0.0 to 1.0)
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// Last time these metrics were updated
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Records a request decision with its reason
    /// </summary>
    /// <param name="allowed">Whether the request was allowed</param>
    /// <param name="reason">Reason for the decision</param>
    public void RecordDecision(bool allowed, string reason)
    {
        TotalRequests++;
        if (allowed)
            AllowedRequests++;
        else
            DeniedRequests++;

        if (!DecisionReasons.ContainsKey(reason))
            DecisionReasons[reason] = 0;
        DecisionReasons[reason]++;

        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records an API call with its latency and success status using exponential moving averages
    /// </summary>
    /// <param name="elapsedMs">Elapsed time in milliseconds</param>
    /// <param name="isSuccess">Whether the call was successful</param>
    public void RecordApiCall(double elapsedMs, bool isSuccess)
    {
        // Update rolling average latency (simple exponential moving average)
        AverageLatencyMs = AverageLatencyMs == 0 ? elapsedMs : (AverageLatencyMs * 0.9) + (elapsedMs * 0.1);
        
        // Update rolling success rate
        var newSuccessRate = isSuccess ? 1.0 : 0.0;
        SuccessRate = SuccessRate == 0 ? newSuccessRate : (SuccessRate * 0.9) + (newSuccessRate * 0.1);
        
        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Creates a deep copy of these metrics
    /// </summary>
    /// <returns>New RequestMetrics instance with copied values</returns>
    public RequestMetrics Clone()
    {
        return new RequestMetrics
        {
            StoreId = StoreId,
            TotalRequests = TotalRequests,
            AllowedRequests = AllowedRequests,
            DeniedRequests = DeniedRequests,
            DecisionReasons = new Dictionary<string, long>(DecisionReasons),
            AverageLatencyMs = AverageLatencyMs,
            SuccessRate = SuccessRate,
            LastUpdated = LastUpdated
        };
    }
}

/// <summary>
/// Event args for predictive status changes
/// </summary>
/// <summary>
/// Event arguments for predictive rate limiting status changes
/// </summary>
public class PredictiveStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// Store identifier for this status change
    /// </summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// Current predictive rate limiting status
    /// </summary>
    public PredictiveRateLimitStatus Status { get; set; } = null!;

    /// <summary>
    /// When this status change occurred
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}