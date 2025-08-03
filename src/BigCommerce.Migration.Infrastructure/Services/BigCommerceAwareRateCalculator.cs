using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Sophisticated rate calculator that considers BigCommerce API health and rate limits
/// Follows Single Responsibility Principle - only handles rate calculation logic
/// Implements exponential smoothing and adaptive algorithms for optimal performance
/// Thread-safe implementation for concurrent use in high-throughput scenarios
/// </summary>
public class BigCommerceAwareRateCalculator : IRateCalculator
{
    private readonly ILogger<BigCommerceAwareRateCalculator> _logger;
    
    // Rate calculation constants
    private const int MIN_RATE = 5;   // Minimum safe rate (req/sec)
    private const int MAX_RATE = 50;  // Maximum allowed rate (req/sec)
    private const int BASE_RATE = 12; // Current static rate baseline
    
    // Algorithm parameters
    private const double SMOOTHING_FACTOR = 0.6; // Exponential smoothing alpha - increased for more responsive changes
    private const double HEALTH_WEIGHT = 0.4;    // Health score influence (40%)
    private const double RESPONSE_WEIGHT = 0.3;  // Response time influence (30%)
    private const double BC_LIMIT_WEIGHT = 0.3;  // BigCommerce limit influence (30%)
    
    // Internal state for exponential smoothing
    private readonly object _stateLock = new object();
    private double _lastCalculatedRate = BASE_RATE;
    private DateTime _lastCalculationTime = DateTime.UtcNow;
    private bool _hasHistory = false;

    /// <summary>
    /// Initializes a new instance of BigCommerceAwareRateCalculator
    /// </summary>
    /// <param name="logger">Logger for diagnostic information and debugging</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null</exception>
    public BigCommerceAwareRateCalculator(ILogger<BigCommerceAwareRateCalculator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calculates optimal rate using sophisticated multi-factor algorithm
    /// Combines health metrics, BigCommerce limits, and exponential smoothing
    /// </summary>
    public double CalculateOptimalRate(ApiHealthMetrics? healthMetrics)
    {
        try
        {
            // Safety check - return minimum rate for null input
            if (healthMetrics == null)
            {
                _logger.LogWarning("Health metrics is null, returning minimum safe rate: {MinRate}", MIN_RATE);
                return MIN_RATE;
            }

            lock (_stateLock)
            {
                // Calculate raw rate based on multiple factors
                var rawRate = CalculateRawRate(healthMetrics);
                
                // Apply exponential smoothing for stability
                var smoothedRate = ApplyExponentialSmoothing(rawRate);
                
                // Apply time-based adjustments
                var adjustedRate = ApplyTimeBasedAdjustments(smoothedRate);
                
                // Ensure bounds and convert to integer
                var finalRate = Math.Max(MIN_RATE, Math.Min(MAX_RATE, (int)Math.Round(adjustedRate)));
                
                // Update internal state
                _lastCalculatedRate = finalRate;
                _lastCalculationTime = DateTime.UtcNow;
                _hasHistory = true;

                _logger.LogDebug("Rate calculation: Raw={Raw}, Smoothed={Smoothed}, Final={Final} for store {StoreId}",
                    rawRate, smoothedRate, finalRate, healthMetrics.StoreId);

                return finalRate;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating optimal rate for store {StoreId}, returning minimum safe rate", 
                healthMetrics?.StoreId ?? "unknown");
            return MIN_RATE;
        }
    }

    /// <summary>
    /// Calculates raw rate based on health factors before smoothing
    /// </summary>
    private double CalculateRawRate(ApiHealthMetrics healthMetrics)
    {
        // Get health score (0-100, but typically 20-100 range)
        var healthScore = Math.Max(0, Math.Min(100, healthMetrics.GetHealthScore()));
        
        // Special case: Very low health scores (≤10) should return minimum rate immediately
        if (healthScore <= 10)
        {
            _logger.LogWarning("Health score is critically low ({HealthScore}), returning minimum rate for safety", healthScore);
            return MIN_RATE;
        }
        
        // Check for critical BigCommerce constraints that should override other factors
        if (healthMetrics.BigCommerceRateLimit?.IsCritical() == true)
        {
            _logger.LogWarning("BigCommerce API in critical state, applying minimum rate");
            return MIN_RATE;
        }
        
        // Enhanced health-based factor calculation
        // Scale health score 20-100 to factor 0.0-1.0 for better distribution
        var normalizedHealth = Math.Max(0, (healthScore - 20.0) / 80.0); // 20-100 -> 0-1
        
        // Apply selective scaling: surgical precision for different health levels
        // Multi-tier piecewise function for optimal rate distribution
        double healthFactor;
        if (normalizedHealth < 0.20)  // Very poor health (critical scenarios only)
        {
            // Critical health: extremely aggressive penalty (power 8.0)
            healthFactor = Math.Pow(normalizedHealth, 8.0); 
        }
        else if (normalizedHealth < 0.40)  // Poor health (wider range but very aggressive)
        {
            // Poor health: extremely aggressive penalty (power 6.0) - increased from 5.0
            healthFactor = Math.Pow(normalizedHealth, 6.0); 
        }
        else if (normalizedHealth < 0.65)
        {
            // Average health: more aggressive penalty
            healthFactor = Math.Pow(normalizedHealth, 2.5);
        }
        else if (normalizedHealth < 0.85)
        {
            // Good health: moderate scaling  
            healthFactor = 0.5 + (normalizedHealth - 0.65) * 1.5; // Scale from 0.5 to 0.8
        }
        else
        {
            // Excellent health: aggressive boost for recovery scenarios
            healthFactor = 0.8 + (normalizedHealth - 0.85) * 1.33; // Scale from 0.8 to 1.0 rapidly
        }
        
        // Calculate response time factor (inverted - faster is better)
        var responseTimeFactor = CalculateResponseTimeFactor(healthMetrics.AverageResponseTimeMs);
        
        // Calculate error rate factor (inverted - lower errors are better)
        var errorRateFactor = Math.Max(0.0, 1.0 - Math.Min(1.0, healthMetrics.ErrorRate));
        
        // Calculate BigCommerce rate limit factor
        var bcLimitFactor = CalculateBigCommerceLimitFactor(healthMetrics.BigCommerceRateLimit);
        
        // Use weighted average with much higher weight on health factor for primary scaling
        var combinedFactor = (healthFactor * 0.70) + 
                           (responseTimeFactor * 0.15) + 
                           (errorRateFactor * 0.08) + 
                           (bcLimitFactor * 0.07);
        
        // Apply additional boost for excellent scenarios
        if (healthScore >= 95)
        {
            combinedFactor = Math.Min(1.0, combinedFactor * 1.5); // 50% boost for near-perfect health
            _logger.LogTrace("Applied near-perfect health boost for score {HealthScore}", healthScore);
        }
        else if (healthScore >= 85)
        {
            combinedFactor = Math.Min(1.0, combinedFactor * 1.4); // 40% boost for excellent health
            _logger.LogTrace("Applied excellent health boost for score {HealthScore}", healthScore);
        }
        else if (healthScore >= 70)  // Lowered threshold from 75 to 70
        {
            combinedFactor = Math.Min(1.0, combinedFactor * 1.25); // 25% boost for good health
            _logger.LogTrace("Applied good health boost for score {HealthScore}", healthScore);
        }
        
        // Scale to rate range with emphasis on utilizing the full range
        var rawRate = MIN_RATE + (combinedFactor * (MAX_RATE - MIN_RATE));
        
        _logger.LogTrace("Rate calculation - Health: {Health} (factor: {HealthFactor:F2}), ResponseTime: {RT:F2}, Error: {Error:F2}, BC: {BC:F2}, Combined: {Combined:F2}, Raw Rate: {RawRate:F1}",
            healthScore, healthFactor, responseTimeFactor, errorRateFactor, bcLimitFactor, combinedFactor, rawRate);
        
        return rawRate;
    }

    /// <summary>
    /// Calculates response time factor with adaptive thresholds
    /// </summary>
    private double CalculateResponseTimeFactor(double avgResponseTimeMs)
    {
        // Handle edge cases
        if (avgResponseTimeMs <= 0) return 1.0; // Perfect if no/invalid data
        if (avgResponseTimeMs >= 5000) return 0.0; // Very poor for 5+ seconds
        
        // Adaptive thresholds
        const double EXCELLENT_THRESHOLD = 100;  // < 100ms = excellent (1.0)
        const double GOOD_THRESHOLD = 300;       // < 300ms = good (0.8)
        const double FAIR_THRESHOLD = 800;       // < 800ms = fair (0.5)
        const double POOR_THRESHOLD = 2000;      // < 2000ms = poor (0.2)
        
        if (avgResponseTimeMs <= EXCELLENT_THRESHOLD) return 1.0;
        if (avgResponseTimeMs <= GOOD_THRESHOLD) return 0.8;
        if (avgResponseTimeMs <= FAIR_THRESHOLD) return 0.5;
        if (avgResponseTimeMs <= POOR_THRESHOLD) return 0.2;
        
        // Linear degradation for very poor response times
        return Math.Max(0.0, 0.2 - ((avgResponseTimeMs - POOR_THRESHOLD) / (5000 - POOR_THRESHOLD)) * 0.2);
    }

    /// <summary>
    /// Calculates BigCommerce rate limit factor based on remaining capacity
    /// </summary>
    private double CalculateBigCommerceLimitFactor(BigCommerceRateLimitInfo? rateLimitInfo)
    {
        // No BigCommerce data - use moderate factor
        if (rateLimitInfo == null || !rateLimitInfo.IsValid())
        {
            return 0.7; // Moderate assumption when no data
        }
        
        // Calculate remaining capacity percentage
        var utilizationPercentage = rateLimitInfo.GetUtilizationPercentage();
        var remainingCapacity = 1.0 - utilizationPercentage;
        
        // Apply extremely aggressive scaling based on utilization levels
        double capacityFactor;
        if (utilizationPercentage >= 0.975)
        {
            capacityFactor = 0.02; // Extremely aggressive limitation when >97.5% used
        }
        else if (utilizationPercentage >= 0.95)
        {
            capacityFactor = 0.05; // Very aggressive limitation when >95% used
        }
        else if (utilizationPercentage >= 0.90)
        {
            capacityFactor = 0.10; // Aggressive limitation when >90% used
        }
        else if (utilizationPercentage >= 0.80)
        {
            capacityFactor = 0.25; // Moderate limitation when >80% used
        }
        else if (utilizationPercentage >= 0.70)
        {
            capacityFactor = 0.5;  // Light limitation when >70% used
        }
        else
        {
            capacityFactor = Math.Max(0.8, remainingCapacity); // Good capacity available
        }
        
        // Calculate effective rate based on time until reset
        var effectiveRate = rateLimitInfo.GetEffectiveRateLimit();
        var rateFactor = Math.Min(1.0, effectiveRate / MAX_RATE); // Scale to our max rate
        
        // Combine factors with heavier weight on capacity constraints
        var combinedFactor = (capacityFactor * 0.8) + (rateFactor * 0.2);
        
        _logger.LogTrace("BC Rate Limit Factor - Utilization: {Util:P2}, Remaining: {Remaining:P2}, Effective Rate: {EffRate:F2}, Capacity Factor: {CapFactor:F2}, Final: {Factor:F2}",
            utilizationPercentage, remainingCapacity, effectiveRate, capacityFactor, combinedFactor);
        
        return Math.Max(0.05, Math.Min(1.0, combinedFactor)); // Allow minimum 5% factor
    }

    /// <summary>
    /// Applies exponential smoothing for stable rate transitions
    /// </summary>
    private double ApplyExponentialSmoothing(double currentRate)
    {
        if (!_hasHistory)
        {
            return currentRate; // No smoothing for first calculation
        }
        
        // Exponential smoothing: S[t] = α * X[t] + (1 - α) * S[t-1]
        var smoothedRate = (SMOOTHING_FACTOR * currentRate) + ((1 - SMOOTHING_FACTOR) * _lastCalculatedRate);
        
        // Apply reaction speed adjustments for significant changes
        var changePercentage = Math.Abs(currentRate - _lastCalculatedRate) / _lastCalculatedRate;
        
        if (changePercentage > 0.5) // Significant change (>50%)
        {
            // React faster to large changes by increasing smoothing factor
            var adaptiveAlpha = Math.Min(0.7, SMOOTHING_FACTOR + (changePercentage * 0.4));
            smoothedRate = (adaptiveAlpha * currentRate) + ((1 - adaptiveAlpha) * _lastCalculatedRate);
            
            _logger.LogDebug("Large rate change detected ({Change:P2}), using adaptive smoothing factor: {Alpha:F2}",
                changePercentage, adaptiveAlpha);
        }
        
        return smoothedRate;
    }

    /// <summary>
    /// Applies time-based adjustments for peak hours and system load
    /// </summary>
    private double ApplyTimeBasedAdjustments(double baseRate)
    {
        var now = DateTime.UtcNow;
        var hour = now.Hour;
        
        // Apply conservative adjustments during typical peak hours (9 AM - 5 PM UTC)
        // This helps reduce load during high-traffic periods
        if (hour >= 9 && hour <= 17)
        {
            var peakAdjustment = 0.9; // 10% reduction during peak hours
            baseRate *= peakAdjustment;
            
            _logger.LogTrace("Applied peak hours adjustment: {Adjustment:F2} at hour {Hour}", peakAdjustment, hour);
        }
        
        // Apply slight boost during off-peak hours (midnight - 6 AM UTC)
        else if (hour >= 0 && hour <= 6)
        {
            var offPeakBoost = 1.1; // 10% increase during off-peak
            baseRate *= offPeakBoost;
            
            _logger.LogTrace("Applied off-peak boost: {Boost:F2} at hour {Hour}", offPeakBoost, hour);
        }
        
        return baseRate;
    }

    /// <summary>
    /// Gets the minimum allowed rate limit for safety
    /// </summary>
    public double GetMinimumRate() => MIN_RATE;

    /// <summary>
    /// Gets the maximum allowed rate limit
    /// </summary>
    public double GetMaximumRate() => MAX_RATE;

    /// <summary>
    /// Resets the internal state of rate calculation algorithms
    /// </summary>
    public void Reset()
    {
        lock (_stateLock)
        {
            _lastCalculatedRate = BASE_RATE;
            _lastCalculationTime = DateTime.UtcNow;
            _hasHistory = false;
            
            _logger.LogInformation("Rate calculator state reset to baseline: {BaseRate}", BASE_RATE);
        }
    }
} 