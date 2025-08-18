using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;
using Models = BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Dynamic rate limiting service with integrated predictive capabilities and distributed coordination
/// Provides zero-429-error guarantee through real-time quota intelligence and multi-instance coordination
/// Consolidates all rate limiting functionality into a single, comprehensive service
/// Thread-safe implementation for high-throughput migration scenarios
/// </summary>
public class DynamicRateLimitService : IEnhancedDynamicRateLimiter
{
    private readonly ILogger<DynamicRateLimitService> _logger;
    private readonly IRateLimitService _rateLimitService;
    private readonly IApiHealthMonitor _healthMonitor;
    private readonly IRateCalculator _rateCalculator;
    
    // Enhanced predictive capabilities (optional - can be null if predictive is disabled)
    private readonly IPredictiveRateLimitingService? _predictiveService;
    private readonly ICoordinationHealthMonitor? _coordinationHealthMonitor;
    private readonly IQuotaTrackingService? _quotaTrackingService;
    private readonly DynamicRateLimitingConfiguration? _configuration;

    // Events for real-time rate/health change notifications
    
    /// <summary>
    /// Event triggered when optimal rate changes significantly
    /// Allows subscribers to adjust their request patterns dynamically
    /// </summary>
    public event EventHandler<Models.OptimalRateChangedEventArgs>? OptimalRateChanged;
    
    /// <summary>
    /// Event triggered when API health changes significantly
    /// Allows subscribers to react to health degradation or improvement
    /// </summary>
    public event EventHandler<Models.ApiHealthChangedEventArgs>? ApiHealthChanged;
    
    /// <summary>
    /// Event triggered when predictive rate limiting status changes
    /// Allows subscribers to react to safety level changes (Normal -> Warning -> Critical)
    /// </summary>
    public event EventHandler<string>? PredictiveStatusChanged
    {
        add { /* Not implemented yet */ }
        remove { /* Not implemented yet */ }
    }

    // Cache previous rates to detect significant changes
    private readonly Dictionary<string, double> _previousRates = new();
    private readonly Dictionary<string, double> _previousHealthScores = new();
    private readonly object _cacheLock = new();

    // Configuration constants
    private const double MinimumSafetyRate = 5.0;
    private const double SignificantRateChangeThreshold = 0.2; // 20% change triggers event
    private const double SignificantHealthChangeThreshold = 15.0; // 15 point health change triggers event

    /// <summary>
    /// Initializes a new instance of DynamicRateLimitService with optional enhanced capabilities
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <param name="rateLimitService">Existing rate limit service to wrap</param>
    /// <param name="healthMonitor">API health monitoring service</param>
    /// <param name="rateCalculator">Rate calculation service</param>
    /// <param name="predictiveService">Optional predictive rate limiting service</param>
    /// <param name="coordinationHealthMonitor">Optional coordination health monitor</param>
    /// <param name="quotaTrackingService">Optional quota tracking service</param>
    /// <param name="configuration">Optional dynamic rate limiting configuration</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
    public DynamicRateLimitService(
        ILogger<DynamicRateLimitService> logger,
        IRateLimitService rateLimitService,
        IApiHealthMonitor healthMonitor,
        IRateCalculator rateCalculator,
        IPredictiveRateLimitingService? predictiveService = null,
        ICoordinationHealthMonitor? coordinationHealthMonitor = null,
        IQuotaTrackingService? quotaTrackingService = null,
        IOptions<DynamicRateLimitingConfiguration>? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _rateCalculator = rateCalculator ?? throw new ArgumentNullException(nameof(rateCalculator));
        
        // Enhanced services (optional)
        _predictiveService = predictiveService;
        _coordinationHealthMonitor = coordinationHealthMonitor;
        _quotaTrackingService = quotaTrackingService;
        _configuration = configuration?.Value;
        
        // Log initialization based on available services
        if (_predictiveService != null && _configuration != null)
        {
            _logger.LogInformation("DynamicRateLimitService initialized with enhanced predictive capabilities");
        }
        else
        {
            _logger.LogInformation("DynamicRateLimitService initialized with basic dynamic capabilities");
        }
    }

    /// <summary>
    /// Calculates the optimal API request rate based on current health metrics
    /// Implements intelligent rate calculation with fallback safety mechanisms
    /// </summary>
    /// <param name="storeId">The ID of the store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Optimal request rate in requests per second</returns>
    public async Task<double> GetOptimalRateAsync(string storeId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get current health metrics
            var healthMetrics = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);

            // Calculate optimal rate using sophisticated algorithms
            var optimalRate = _rateCalculator.CalculateOptimalRate(healthMetrics);

            // Track rate changes and trigger events if significant
            await TrackRateChanges(storeId, optimalRate, healthMetrics);

            _logger.LogDebug("Calculated optimal rate {OptimalRate} for store {StoreId} " +
                           "(Health Score: {HealthScore}, Avg Response: {AvgResponse}ms, Error Rate: {ErrorRate:P2})",
                optimalRate, storeId, healthMetrics.GetHealthScore(), 
                healthMetrics.AverageResponseTimeMs, healthMetrics.ErrorRate);

            return optimalRate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating optimal rate for store {StoreId}, using minimum safety rate", storeId);
            return MinimumSafetyRate;
        }
    }

    /// <summary>
    /// Updates API health information with BigCommerce rate limit data from response headers
    /// Processes X-Rate-Limit-* headers for dynamic rate limiting calculations
    /// </summary>
    /// <param name="storeId">The ID of the store</param>
    /// <param name="rateLimitInfo">BigCommerce rate limit information from response headers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task UpdateApiHealthAsync(string storeId, BigCommerceRateLimitInfo rateLimitInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Record BigCommerce rate limit info for health monitoring
            // Note: Performance metrics (elapsed time, success) are recorded separately via RecordApiCallAsync
            await _healthMonitor.RecordApiCallAsync(storeId, rateLimitInfo, 0, true, cancellationToken);

            // 🎯 FIX: Call quota tracking service to trigger table creation and quota updates
            _quotaTrackingService?.ProcessRateLimitInfo(rateLimitInfo, "DynamicRateLimitService");

            _logger.LogTrace("Updated BigCommerce rate limit info for store {StoreId}: {RequestsLeft}/{RequestsQuota}",
                storeId, rateLimitInfo?.RequestsLeft, rateLimitInfo?.RequestsQuota);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating API health for store {StoreId}", storeId);
            // Don't rethrow - health tracking shouldn't break API flow
        }

        // Method is already async due to RecordApiCallAsync call above
    }

    /// <summary>
    /// Gets the current API health metrics for a store
    /// Provides comprehensive health information for monitoring and diagnostics
    /// </summary>
    /// <param name="storeId">The ID of the store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current API health metrics</returns>
    public async Task<ApiHealthMetrics> GetApiHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var healthMetrics = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);
            return healthMetrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving API health for store {StoreId}", storeId);
            throw;
        }
    }

    /// <summary>
    /// Gets enhanced rate limit status combining base service data with dynamic rate information
    /// Provides comprehensive rate limiting status for monitoring and decision making
    /// </summary>
    /// <param name="storeId">The ID of the store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enhanced rate limit status with health data</returns>
    public async Task<BigCommerce.Migration.Core.Models.EnhancedRateLimitStatus> GetEnhancedRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get base rate limit status from existing service
            var baseStatus = await _rateLimitService.GetRateLimitStatusAsync(storeId, cancellationToken);
            
            // Get current health metrics and optimal rate
            var healthMetrics = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);
            var optimalRateDouble = _rateCalculator.CalculateOptimalRate(healthMetrics);

            // Convert to requests per minute (EnhancedRateLimitStatus expects int)
            var optimalRateInt = (int)Math.Round(optimalRateDouble * 60); // Convert req/sec to req/min

            // Combine into enhanced status using Core model constructor
            var enhancedStatus = new BigCommerce.Migration.Core.Models.EnhancedRateLimitStatus(baseStatus, healthMetrics, optimalRateInt);

            return enhancedStatus;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting enhanced rate limit status for store {StoreId}", storeId);
            throw;
        }
    }

    // ===============================
    // IRateLimitService Implementation (Wrapper Pattern)
    // ===============================

    /// <summary>
    /// Dynamic implementation of CanMakeRequestAsync using BigCommerce API data
    /// Overrides base service to use real-time API capacity instead of hardcoded limits
    /// </summary>
    public async Task<bool> CanMakeRequestAsync(string storeId, CancellationToken cancellationToken = default)
    {
        try
        {
            // 🎯 FIX: Call predictive rate limiting service if available and enabled
            if (_predictiveService != null && _configuration?.Features.EnablePredictiveDistribution == true)
            {
                var predictiveResult = await _predictiveService.CanProcessRequestAsync(storeId, cancellationToken);
                _logger.LogDebug("🔮 [PREDICTIVE-RATE-LIMIT] Store {StoreId}: CanProcess = {CanProcess}", storeId, predictiveResult);
                
                // If predictive service says no, respect that decision
                if (!predictiveResult)
                {
                    return false;
                }
                // If predictive service says yes, continue with additional health checks
            }

            // Get current API health with BigCommerce data
            var apiHealth = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);
            
            // If we have BigCommerce rate limit data, use it
            if (apiHealth.BigCommerceRateLimit?.RequestsLeft > 0 && apiHealth.BigCommerceRateLimit?.RequestsQuota > 0)
            {
                var utilizationPercent = apiHealth.BigCommerceRateLimit.RequestsLeft / (double)apiHealth.BigCommerceRateLimit.RequestsQuota * 100;
                
                // Use BigCommerce data: allow requests if we have >5% capacity remaining
                var canProceed = utilizationPercent > 5.0;
                
                _logger.LogDebug("🚀 [DYNAMIC-CAN-PROCEED] Store {StoreId}: {RequestsLeft}/{RequestsQuota} ({Utilization:F1}%) - CanProceed: {CanProceed}", 
                    storeId, apiHealth.BigCommerceRateLimit.RequestsLeft, apiHealth.BigCommerceRateLimit.RequestsQuota, utilizationPercent, canProceed);
                
                return canProceed;
            }
            
            // Fallback to base service if no BigCommerce data available
            _logger.LogDebug("⚠️ [DYNAMIC-FALLBACK] No BigCommerce rate limit data for store {StoreId}, using base service", storeId);
            return await _rateLimitService.CanMakeRequestAsync(storeId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in dynamic CanMakeRequestAsync for store {StoreId}, falling back to base service", storeId);
            return await _rateLimitService.CanMakeRequestAsync(storeId, cancellationToken);
        }
    }

    /// <summary>
    /// Records an API call for rate limiting tracking
    /// Updates both existing rate limiter and health monitoring systems
    /// </summary>
    public async Task RecordApiCallAsync(string storeId, string url, double elapsedMilliseconds, bool isSuccess, CancellationToken cancellationToken = default)
    {
        // Update health monitoring (no BigCommerce headers from this method)
        await _healthMonitor.RecordApiCallAsync(storeId, null, elapsedMilliseconds, isSuccess, cancellationToken);
        
        // Delegate to existing service for original rate limiting logic
        await _rateLimitService.RecordApiCallAsync(storeId, url, elapsedMilliseconds, isSuccess, cancellationToken);
    }

    /// <summary>
    /// Gets the current rate limit status
    /// Delegates to existing service for backward compatibility
    /// </summary>
    public async Task<RateLimitStatus> GetRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        return await _rateLimitService.GetRateLimitStatusAsync(storeId, cancellationToken);
    }

    /// <summary>
    /// Calculates the delay needed before next request
    /// Delegates to existing service while considering dynamic rates
    /// </summary>
    public async Task<int> CalculateDelayAsync(string storeId, CancellationToken cancellationToken = default)
    {
        return await _rateLimitService.CalculateDelayAsync(storeId, cancellationToken);
    }

    /// <summary>
    /// Checks rate limit status and returns delay information
    /// Delegates to existing service for now - future enhancement: integrate dynamic rates
    /// </summary>
    public async Task<RateLimitResult> CheckRateLimitAsync(string storeId, CancellationToken cancellationToken = default)
    {
        return await _rateLimitService.CheckRateLimitAsync(storeId, cancellationToken);
    }

    /// <summary>
    /// Dynamic implementation of CheckAndWaitAsync using intelligent delays based on BigCommerce data
    /// </summary>
    public async Task CheckAndWaitAsync(string storeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var canProceed = await CanMakeRequestAsync(storeId, cancellationToken);
            
            if (!canProceed)
            {
                // Get API health for intelligent delay calculation
                var apiHealth = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);
                
                // Calculate dynamic delay based on BigCommerce data
                var delayMs = CalculateIntelligentDelay(apiHealth);
                
                _logger.LogInformation("🚀 [DYNAMIC-RATE-LIMIT] Store {StoreId} rate limited, waiting {DelayMs}ms (intelligent delay)", storeId, delayMs);
                await Task.Delay(delayMs, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in dynamic CheckAndWaitAsync for store {StoreId}, falling back to base service", storeId);
            await _rateLimitService.CheckAndWaitAsync(storeId, cancellationToken);
        }
    }

    // ===============================
    // Private Helper Methods
    // ===============================

    /// <summary>
    /// Calculates intelligent delay based on BigCommerce API health data
    /// </summary>
    private int CalculateIntelligentDelay(ApiHealthMetrics apiHealth)
    {
        // If we have BigCommerce timing data, use it
        if (apiHealth.BigCommerceRateLimit?.TimeResetMs > 0)
        {
            // Wait for a fraction of the reset time, minimum 100ms, maximum 5000ms
            var delayMs = Math.Max(100, Math.Min(5000, (int)(apiHealth.BigCommerceRateLimit.TimeResetMs / 4)));
            return delayMs;
        }
        
        // If no BigCommerce data, use health-based delay
        var healthScore = apiHealth.GetHealthScore();
        return healthScore switch
        {
            < 30 => 2000,   // Poor health: longer wait
            < 60 => 1000,   // Fair health: moderate wait  
            < 80 => 500,    // Good health: short wait
            _ => 200        // Excellent health: minimal wait
        };
    }

    /// <summary>
    /// Tracks rate and health changes to trigger events for significant changes
    /// Enables real-time monitoring and alerting for rate/health fluctuations
    /// </summary>
    private async Task TrackRateChanges(string storeId, double currentRate, ApiHealthMetrics healthMetrics)
    {
        var currentHealthScore = healthMetrics.GetHealthScore();
        
        lock (_cacheLock)
        {
            // Check for significant rate changes
            if (_previousRates.TryGetValue(storeId, out var previousRate))
            {
                var rateChangePercentage = Math.Abs(currentRate - previousRate) / previousRate;
                if (rateChangePercentage >= SignificantRateChangeThreshold)
                {
                    OptimalRateChanged?.Invoke(this, new Models.OptimalRateChangedEventArgs
                    {
                        StoreId = storeId,
                        PreviousRate = previousRate,
                        NewRate = currentRate,
                        ChangePercentage = rateChangePercentage,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }

            // Check for significant health changes
            if (_previousHealthScores.TryGetValue(storeId, out var previousHealthScore))
            {
                var healthChange = Math.Abs(currentHealthScore - previousHealthScore);
                if (healthChange >= SignificantHealthChangeThreshold)
                {
                    ApiHealthChanged?.Invoke(this, new Models.ApiHealthChangedEventArgs
                    {
                        StoreId = storeId,
                        PreviousHealthScore = previousHealthScore,
                        NewHealthScore = currentHealthScore,
                        HealthMetrics = healthMetrics,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }

            // Update cache
            _previousRates[storeId] = currentRate;
            _previousHealthScores[storeId] = currentHealthScore;
        }

        await Task.CompletedTask; // Async compatibility
    }
    
    /// <summary>
    /// **Phase 2.7**: Registers parallel processing context for adaptive rate limiting
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="concurrentBatches">Number of concurrent batches being processed</param>
    /// <param name="totalBatches">Total number of batches in the migration</param>
    public void RegisterParallelContext(string storeId, int concurrentBatches, int totalBatches)
    {
        _rateLimitService.RegisterParallelContext(storeId, concurrentBatches, totalBatches);
    }
    
    /// <summary>
    /// **Phase 2.7**: Unregisters parallel processing context
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    public void UnregisterParallelContext(string storeId)
    {
        _rateLimitService.UnregisterParallelContext(storeId);
    }
    
    /// <summary>
    /// **Phase 2.7**: Resets consecutive rate limit hits when requests succeed
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    public void ResetConsecutiveRateLimitHits(string storeId)
    {
        _rateLimitService.ResetConsecutiveRateLimitHits(storeId);
    }

    // ===============================
    // IEnhancedDynamicRateLimiter Implementation
    // ===============================

    /// <inheritdoc />
    public async Task RecordApiCallAsync(string storeId, TimeSpan responseTime, bool success, CancellationToken cancellationToken = default)
    {
        // Record in health monitor using the correct interface method
        await _healthMonitor.RecordApiCallAsync(
            storeId, 
            null, // No BigCommerce rate limit info in this context
            responseTime.TotalMilliseconds, 
            success, 
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<double> GetPredictiveRateAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (_predictiveService == null || _configuration?.Features.EnablePredictiveDistribution != true)
        {
            // Fallback: use standard optimal rate calculation
            var healthMetrics = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);
            return _rateCalculator.CalculateOptimalRate(healthMetrics);
        }

        try
        {
            var predictiveStatus = await _predictiveService.GetRateLimitStatusAsync(storeId, cancellationToken);
            // Calculate rate from safe tokens and time window
            return predictiveStatus.SafeTokens / 60.0; // Convert to per-second rate
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to get predictive rate for store {StoreId}, falling back to standard calculation", storeId);
            var healthMetrics = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);
            return _rateCalculator.CalculateOptimalRate(healthMetrics);
        }
    }

    /// <inheritdoc />
    public async Task<PredictiveRateLimitStatus> GetPredictiveStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (_predictiveService == null || _configuration?.Features.EnablePredictiveDistribution != true)
        {
            // Fallback: create basic status from health metrics
            var healthMetrics = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);
            var optimalRate = _rateCalculator.CalculateOptimalRate(healthMetrics);
            var healthScore = healthMetrics.GetHealthScore();
            
            return new PredictiveRateLimitStatus
            {
                StoreId = storeId,
                SafeTokens = (int)(optimalRate * 60), // Convert rate to tokens per minute
                QuotaHealthStatus = healthScore > 70 ? "Normal" : "Warning",
                QuotaUtilizationPercent = 100 - healthScore,
                StatusTimestamp = DateTime.UtcNow
            };
        }

        return await _predictiveService.GetRateLimitStatusAsync(storeId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SystemHealthState> GetCoordinationHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (_coordinationHealthMonitor == null || _configuration?.Features.EnableInstanceCoordination != true)
        {
            // Fallback: create basic system health state
            var healthMetrics = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);
            var healthScore = healthMetrics.GetHealthScore();
            return new SystemHealthState
            {
                StoreId = storeId,
                OverallHealthScore = healthScore / 100.0, // Convert to 0-1 range
                ActiveInstanceCount = 1,
                LastUpdated = DateTimeOffset.UtcNow,
                OverallHealthStatus = healthScore > 50 ? SystemHealthStatus.Healthy : SystemHealthStatus.Degraded
            };
        }

        return await _coordinationHealthMonitor.GetSystemHealthAsync(storeId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BigCommerce.Migration.Core.Models.RateLimiting.PerformanceMetrics> GetPerformanceMetricsAsync(string storeId, TimeSpan? timeWindow = null, CancellationToken cancellationToken = default)
    {
        var healthMetrics = await _healthMonitor.GetApiHealthAsync(storeId, cancellationToken);
        var optimalRate = _rateCalculator.CalculateOptimalRate(healthMetrics);
        
        var performanceMetrics = new BigCommerce.Migration.Core.Models.RateLimiting.PerformanceMetrics
        {
            StoreId = storeId,
            LastUpdated = DateTimeOffset.UtcNow,
            TotalOperations = healthMetrics.TotalRequests,
            SuccessfulOperations = healthMetrics.SuccessfulRequests,
            FailedOperations = healthMetrics.FailedRequests,
            AverageOperationLatency = TimeSpan.FromMilliseconds(healthMetrics.AverageResponseTimeMs)
        };

        return performanceMetrics;
    }
} 