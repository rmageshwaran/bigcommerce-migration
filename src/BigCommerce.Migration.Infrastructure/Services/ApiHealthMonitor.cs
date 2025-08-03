using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System.Collections.Concurrent;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Service for monitoring and aggregating BigCommerce API health metrics
/// Follows Single Responsibility Principle - only handles API health data aggregation
/// Thread-safe implementation using concurrent collections for high-throughput scenarios
/// Provides real-time health data for dynamic rate limiting calculations
/// </summary>
public class ApiHealthMonitor : IApiHealthMonitor
{
    private readonly ILogger<ApiHealthMonitor> _logger;
    private readonly IDateTimeProvider _dateTimeProvider;
    
    // Thread-safe storage for API call data per store
    private readonly ConcurrentDictionary<string, List<ApiCallRecord>> _apiCallHistory;
    private readonly ConcurrentDictionary<string, BigCommerceRateLimitInfo?> _latestRateLimits;
    private readonly object _cleanupLock = new();
    
    // Configuration for time window (5 minutes default)
    private readonly TimeSpan _timeWindow = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Initializes a new instance of ApiHealthMonitor
    /// </summary>
    /// <param name="logger">Logger for service operations</param>
    /// <param name="dateTimeProvider">Provider for current date/time</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null</exception>
    public ApiHealthMonitor(ILogger<ApiHealthMonitor> logger, IDateTimeProvider dateTimeProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        
        _apiCallHistory = new ConcurrentDictionary<string, List<ApiCallRecord>>();
        _latestRateLimits = new ConcurrentDictionary<string, BigCommerceRateLimitInfo?>();
    }

    /// <summary>
    /// Records an API call for health monitoring
    /// Thread-safe method that aggregates call data for dynamic rate limiting
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="rateLimitInfo">BigCommerce rate limit information from headers (can be null)</param>
    /// <param name="responseTimeMs">Response time in milliseconds</param>
    /// <param name="isSuccess">Whether the API call was successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentException">Thrown when storeId is null or empty</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when responseTimeMs is negative</exception>
    public async Task RecordApiCallAsync(
        string storeId,
        BigCommerceRateLimitInfo? rateLimitInfo,
        double responseTimeMs,
        bool isSuccess,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        if (responseTimeMs < 0)
            throw new ArgumentOutOfRangeException(nameof(responseTimeMs), "Response time cannot be negative");

        cancellationToken.ThrowIfCancellationRequested();

        var now = _dateTimeProvider.UtcNow;
        var callRecord = new ApiCallRecord
        {
            Timestamp = now,
            ResponseTimeMs = responseTimeMs,
            IsSuccess = isSuccess
        };

        // Thread-safe addition to call history
        _apiCallHistory.AddOrUpdate(storeId,
            new List<ApiCallRecord> { callRecord },
            (key, existingList) =>
            {
                lock (existingList)
                {
                    existingList.Add(callRecord);
                    return existingList;
                }
            });

        // Update latest rate limit info if provided
        if (rateLimitInfo != null)
        {
            _latestRateLimits.AddOrUpdate(storeId, rateLimitInfo, (key, existing) => rateLimitInfo);
        }

        // Periodic cleanup of old data to prevent memory leaks
        await CleanupOldDataAsync(storeId, now);

        _logger.LogDebug("Recorded API call for store {StoreId}: {ResponseTime}ms, Success: {IsSuccess}",
            storeId, responseTimeMs, isSuccess);
    }

    /// <summary>
    /// Gets aggregated API health metrics for a store
    /// Calculates health score based on response time, error rate, and BigCommerce rate limits
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated API health metrics</returns>
    /// <exception cref="ArgumentException">Thrown when storeId is null or empty</exception>
    public async Task<ApiHealthMetrics> GetApiHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        cancellationToken.ThrowIfCancellationRequested();

        var now = _dateTimeProvider.UtcNow;
        var windowStart = now.Subtract(_timeWindow);

        // Get recent call history within time window
        var recentCalls = GetRecentCalls(storeId, windowStart);
        var latestRateLimit = _latestRateLimits.GetValueOrDefault(storeId);

        // Calculate aggregated metrics (always return metrics, even with empty data)
        var metrics = await Task.Run(() => CalculateHealthMetrics(storeId, recentCalls, latestRateLimit, now), cancellationToken);

        _logger.LogDebug("Generated health metrics for store {StoreId}: Health Score {HealthScore}, " +
                        "Avg Response Time {AvgResponseTime}ms, Error Rate {ErrorRate:P2}",
            storeId, metrics.GetHealthScore(), metrics.AverageResponseTimeMs, metrics.ErrorRate);

        return metrics;
    }

    #region Private Helper Methods

    /// <summary>
    /// Gets recent API calls within the time window for a store
    /// Thread-safe method with proper locking
    /// </summary>
    private List<ApiCallRecord> GetRecentCalls(string storeId, DateTime windowStart)
    {
        if (!_apiCallHistory.TryGetValue(storeId, out var callHistory))
            return new List<ApiCallRecord>();

        lock (callHistory)
        {
            return callHistory.Where(call => call.Timestamp >= windowStart).ToList();
        }
    }

    /// <summary>
    /// Calculates comprehensive health metrics from call history
    /// Uses existing ApiHealthMetrics structure and methods
    /// </summary>
    private ApiHealthMetrics CalculateHealthMetrics(
        string storeId,
        List<ApiCallRecord> recentCalls,
        BigCommerceRateLimitInfo? latestRateLimit,
        DateTime timestamp)
    {
        if (recentCalls.Count == 0)
        {
            return new ApiHealthMetrics
            {
                StoreId = storeId,
                AverageResponseTimeMs = 0.0,
                ErrorRate = 0.0,
                SuccessfulRequests = 0,
                FailedRequests = 0,
                BigCommerceRateLimit = latestRateLimit,
                Timestamp = timestamp
            };
        }

        // Calculate basic metrics
        var averageResponseTime = recentCalls.Average(call => call.ResponseTimeMs);
        var successCount = recentCalls.Count(call => call.IsSuccess);
        var errorCount = recentCalls.Count(call => !call.IsSuccess);
        var errorRate = (double)errorCount / recentCalls.Count;

        return new ApiHealthMetrics
        {
            StoreId = storeId,
            AverageResponseTimeMs = averageResponseTime,
            ErrorRate = errorRate,
            SuccessfulRequests = successCount,
            FailedRequests = errorCount,
            BigCommerceRateLimit = latestRateLimit,
            Timestamp = timestamp
        };
    }



    /// <summary>
    /// Cleans up old API call data to prevent memory leaks
    /// Runs periodically and removes data outside the time window
    /// </summary>
    private async Task CleanupOldDataAsync(string storeId, DateTime currentTime)
    {
        // Only cleanup periodically to avoid performance impact
        if (currentTime.Second % 30 != 0) return;

        await Task.Run(() =>
        {
            lock (_cleanupLock)
            {
                if (_apiCallHistory.TryGetValue(storeId, out var callHistory))
                {
                    var windowStart = currentTime.Subtract(_timeWindow);
                    
                    lock (callHistory)
                    {
                        // Remove old entries
                        var oldEntries = callHistory.Where(call => call.Timestamp < windowStart).ToList();
                        foreach (var entry in oldEntries)
                        {
                            callHistory.Remove(entry);
                        }
                    }
                }
            }
        });
    }

    #endregion

    #region Internal Data Models

    /// <summary>
    /// Internal record for storing individual API call data
    /// Optimized for memory efficiency and fast aggregation
    /// </summary>
    private class ApiCallRecord
    {
        public DateTime Timestamp { get; set; }
        public double ResponseTimeMs { get; set; }
        public bool IsSuccess { get; set; }
    }

    #endregion

    #region Interface Implementation Methods

    /// <summary>
    /// Clears stored health data for a specific store
    /// Useful for testing or when store configuration changes
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    public void ClearStoreData(string storeId)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            _logger.LogWarning("Attempted to clear data for null or empty store ID");
            return;
        }

        _apiCallHistory.TryRemove(storeId, out _);
        _latestRateLimits.TryRemove(storeId, out _);

        _logger.LogDebug("Cleared health data for store {StoreId}", storeId);
    }

    /// <summary>
    /// Gets the number of stores currently being monitored
    /// Useful for diagnostics and monitoring
    /// </summary>
    /// <returns>Count of stores with health data</returns>
    public int GetMonitoredStoreCount()
    {
        // Count stores that have either call history or rate limit data
        var storesWithCallHistory = _apiCallHistory.Keys.ToHashSet();
        var storesWithRateLimits = _latestRateLimits.Keys.ToHashSet();
        
        return storesWithCallHistory.Union(storesWithRateLimits).Count();
    }

    #endregion
} 