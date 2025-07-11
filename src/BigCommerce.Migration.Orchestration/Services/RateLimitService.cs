using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using System.Collections.Concurrent;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Service for managing rate limiting across BigCommerce API calls
/// Implements BigCommerce's 12 requests per second limit with proper cancellation support
/// </summary>
public class RateLimitService : IRateLimitService
{
    private readonly ILogger<RateLimitService> _logger;
    private readonly ConcurrentDictionary<string, StoreRateLimit> _storeLimits;
    private readonly object _lock = new object();
    
    /// <summary>
    /// Initializes a new instance of the RateLimitService
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public RateLimitService(ILogger<RateLimitService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storeLimits = new ConcurrentDictionary<string, StoreRateLimit>();
    }
    
    /// <inheritdoc />
    public async Task<bool> CanMakeRequestAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        _logger.LogDebug("Checking if request can be made for store: {StoreId}", storeId);
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                return storeLimit.RequestTimes.Count < 12;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CanMakeRequestAsync was cancelled for store {StoreId}", storeId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task RecordApiCallAsync(string storeId, string endpoint, double responseTime, bool isSuccessful, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        if (string.IsNullOrEmpty(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty", nameof(endpoint));
        
        _logger.LogDebug("Recording API call for store {StoreId}, endpoint {Endpoint}, time {ResponseTime}ms, success {IsSuccessful}", 
            storeId, endpoint, responseTime, isSuccessful);
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                storeLimit.RequestTimes.Add(DateTime.UtcNow);
                storeLimit.LastCallTime = DateTime.UtcNow;
            }
            
            await Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RecordApiCallAsync was cancelled for store {StoreId}", storeId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task<RateLimitStatus> GetRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        _logger.LogDebug("Getting rate limit status for store: {StoreId}", storeId);
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                var requestsInWindow = storeLimit.RequestTimes.Count;
                var isLimited = requestsInWindow >= 12;
                
                return new RateLimitStatus
                {
                    StoreId = storeId,
                    RequestsPerMinute = requestsInWindow,
                    RequestsRemaining = Math.Max(0, 12 - requestsInWindow),
                    WindowResetTime = DateTime.UtcNow.AddMinutes(1),
                    IsLimited = isLimited,
                    RecommendedDelayMs = isLimited ? CalculateDelayMs(storeLimit) : 0
                };
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("GetRateLimitStatusAsync was cancelled for store {StoreId}", storeId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task<int> CalculateDelayAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        _logger.LogDebug("Calculating delay for store: {StoreId}", storeId);
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                return CalculateDelayMs(storeLimit);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CalculateDelayAsync was cancelled for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<RateLimitResult> CheckRateLimitAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        _logger.LogDebug("Checking rate limit for store: {StoreId}", storeId);
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                var requestsInWindow = storeLimit.RequestTimes.Count;
                var canProceed = requestsInWindow < 12;
                var delayMs = canProceed ? 0 : CalculateDelayMs(storeLimit);
                
                return new RateLimitResult
                {
                    CanProceed = canProceed,
                    DelayMs = delayMs,
                    CurrentRequestCount = requestsInWindow,
                    RequestLimit = 12,
                    WindowResetTime = TimeSpan.FromMinutes(1)
                };
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CheckRateLimitAsync was cancelled for store {StoreId}", storeId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task CheckAndWaitAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        _logger.LogDebug("Checking and waiting for rate limit for store: {StoreId}", storeId);
        
        try
        {
            var result = await CheckRateLimitAsync(storeId, cancellationToken);
            
            if (!result.CanProceed && result.DelayMs > 0)
            {
                _logger.LogInformation("Rate limit hit for store {StoreId}, waiting {DelayMs}ms", storeId, result.DelayMs);
                await Task.Delay(result.DelayMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CheckAndWaitAsync was cancelled for store {StoreId}", storeId);
            throw;
        }
    }
    
    /// <summary>
    /// Gets or creates a store rate limit tracker
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <returns>Store rate limit tracker</returns>
    private StoreRateLimit GetOrCreateStoreLimit(string storeId)
    {
        return _storeLimits.GetOrAdd(storeId, _ => new StoreRateLimit
        {
            StoreId = storeId,
            RequestTimes = new List<DateTime>(),
            LastCallTime = DateTime.UtcNow
        });
    }
    
    /// <summary>
    /// Cleans up expired requests from the rate limit window
    /// </summary>
    /// <param name="storeLimit">Store rate limit tracker</param>
    private void CleanupExpiredRequests(StoreRateLimit storeLimit)
    {
        var cutoffTime = DateTime.UtcNow.AddMinutes(-1);
        storeLimit.RequestTimes.RemoveAll(time => time < cutoffTime);
    }
    
    /// <summary>
    /// Calculates the delay needed before the next request
    /// </summary>
    /// <param name="storeLimit">Store rate limit tracker</param>
    /// <returns>Delay in milliseconds</returns>
    private int CalculateDelayMs(StoreRateLimit storeLimit)
    {
        if (storeLimit.RequestTimes.Count < 12)
            return 0;
        
        var oldestRequest = storeLimit.RequestTimes.Min();
        var timeUntilExpiry = oldestRequest.AddMinutes(1) - DateTime.UtcNow;
        
        return Math.Max(0, (int)timeUntilExpiry.TotalMilliseconds + 100); // Add 100ms buffer
    }
}

/// <summary>
/// Internal class to track rate limiting per store
/// </summary>
internal class StoreRateLimit
{
    public string StoreId { get; set; } = string.Empty;
    public List<DateTime> RequestTimes { get; set; } = new List<DateTime>();
    public DateTime LastCallTime { get; set; }
} 