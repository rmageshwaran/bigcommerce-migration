using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using BigCommerce.Migration.Functions.Middleware;

namespace BigCommerce.Migration.Functions.Services;

/// <summary>
/// API rate limiting service using sliding window algorithm
/// Uses in-memory storage suitable for single-instance deployments
/// For multi-instance deployments, consider using Redis or similar distributed cache
/// </summary>
public class ApiRateLimitService : IApiRateLimitService
{
    private readonly ILogger<ApiRateLimitService> _logger;
    private readonly ConcurrentDictionary<string, RateLimitWindow> _windows = new();
    private readonly Timer _cleanupTimer;

    public ApiRateLimitService(ILogger<ApiRateLimitService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Start cleanup timer to remove expired windows every 5 minutes
        _cleanupTimer = new Timer(CleanupExpiredWindows, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public Task<RateLimitResult> CheckRateLimitAsync(string key, RateLimitConfiguration config)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var now = DateTime.UtcNow;
        var window = _windows.GetOrAdd(key, k => new RateLimitWindow(k, now));

        lock (window)
        {
            // Clean up old requests outside the window
            var windowStart = now.AddMinutes(-config.WindowSizeMinutes);
            window.CleanupOldRequests(windowStart);

            // Check if we're within limits
            var currentRequests = window.RequestCount;
            var isAllowed = currentRequests < config.RequestsPerMinute;

            // If allowed, record the request
            if (isAllowed)
            {
                window.AddRequest(now);
                currentRequests++;
            }

            // Calculate reset time (next window start)
            var resetTime = window.WindowStart.AddMinutes(config.WindowSizeMinutes);
            var retryAfterSeconds = (int)Math.Max(0, (resetTime - now).TotalSeconds);

            var result = new RateLimitResult
            {
                IsAllowed = isAllowed,
                RequestsPerMinute = config.RequestsPerMinute,
                CurrentRequests = currentRequests,
                ResetTime = new DateTimeOffset(resetTime).ToUnixTimeSeconds(),
                RetryAfterSeconds = retryAfterSeconds,
                Key = key
            };

            if (!isAllowed)
            {
                _logger.LogWarning("Rate limit exceeded for key {Key}. Current: {Current}, Limit: {Limit}", 
                    key, currentRequests, config.RequestsPerMinute);
            }

            return Task.FromResult(result);
        }
    }

    public Task<RateLimitStatus> GetRateLimitStatusAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (!_windows.TryGetValue(key, out var window))
        {
            var defaultStatus = new RateLimitStatus
            {
                Key = key,
                RequestsPerMinute = 0,
                CurrentRequests = 0,
                WindowStart = DateTime.UtcNow,
                WindowEnd = DateTime.UtcNow.AddMinutes(1),
                TimeUntilReset = TimeSpan.FromMinutes(1)
            };
            return Task.FromResult(defaultStatus);
        }

        lock (window)
        {
            var now = DateTime.UtcNow;
            var windowEnd = window.WindowStart.AddMinutes(1); // Default to 1 minute window
            var timeUntilReset = windowEnd > now ? windowEnd - now : TimeSpan.Zero;

            var status = new RateLimitStatus
            {
                Key = key,
                RequestsPerMinute = 0, // We don't store the original limit in the window
                CurrentRequests = window.RequestCount,
                WindowStart = window.WindowStart,
                WindowEnd = windowEnd,
                TimeUntilReset = timeUntilReset
            };

            return Task.FromResult(status);
        }
    }

    public Task ResetRateLimitAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (_windows.TryRemove(key, out var window))
        {
            _logger.LogInformation("Rate limit reset for key {Key}", key);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Cleanup expired windows to prevent memory leaks
    /// </summary>
    private void CleanupExpiredWindows(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var expiredKeys = new List<string>();

            foreach (var kvp in _windows)
            {
                var window = kvp.Value;
                lock (window)
                {
                    // Remove windows that haven't been used in the last hour
                    if (now - window.LastAccess > TimeSpan.FromHours(1))
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }
            }

            foreach (var key in expiredKeys)
            {
                _windows.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} expired rate limit windows", expiredKeys.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rate limit window cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

/// <summary>
/// Represents a sliding window for rate limiting
/// </summary>
internal class RateLimitWindow
{
    private readonly List<DateTime> _requests = new();
    
    public string Key { get; }
    public DateTime WindowStart { get; private set; }
    public DateTime LastAccess { get; private set; }
    public int RequestCount => _requests.Count;

    public RateLimitWindow(string key, DateTime windowStart)
    {
        Key = key;
        WindowStart = windowStart;
        LastAccess = windowStart;
    }

    public void AddRequest(DateTime timestamp)
    {
        _requests.Add(timestamp);
        LastAccess = timestamp;
    }

    public void CleanupOldRequests(DateTime windowStart)
    {
        // Remove requests older than the window start
        var cutoffIndex = _requests.FindIndex(r => r >= windowStart);
        if (cutoffIndex > 0)
        {
            _requests.RemoveRange(0, cutoffIndex);
        }
        else if (cutoffIndex == -1)
        {
            // All requests are old, clear the list
            _requests.Clear();
        }

        // Update window start if we have requests
        if (_requests.Count > 0)
        {
            WindowStart = windowStart;
        }
        else
        {
            WindowStart = DateTime.UtcNow;
        }

        LastAccess = DateTime.UtcNow;
    }
} 