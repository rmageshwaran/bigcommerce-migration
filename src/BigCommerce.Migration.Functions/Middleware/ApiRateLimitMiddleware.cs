using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Functions.Middleware;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace BigCommerce.Migration.Functions.Middleware;

/// <summary>
/// API rate limiting middleware to protect endpoints from abuse
/// Implements per-user rate limiting with different limits based on API key roles
/// </summary>
public class ApiRateLimitMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<ApiRateLimitMiddleware> _logger;
    private readonly IApiRateLimitService _rateLimitService;

    public ApiRateLimitMiddleware(
        ILogger<ApiRateLimitMiddleware> logger,
        IApiRateLimitService rateLimitService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpRequestData = await context.GetHttpRequestDataAsync();
        if (httpRequestData == null)
        {
            // Not an HTTP function, continue without rate limiting
            await next(context);
            return;
        }

        // Skip rate limiting for certain endpoints
        if (ShouldSkipRateLimit(httpRequestData))
        {
            await next(context);
            return;
        }

        try
        {
            // Get user context from authentication middleware
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;

            var userId = context.Items.TryGetValue("UserId", out var userIdValue) ? userIdValue?.ToString() : null;
            var userRole = context.Items.TryGetValue("UserRole", out var roleValue) ? roleValue?.ToString() : null;
            var apiKeyId = context.Items.TryGetValue("ApiKeyId", out var keyIdValue) ? keyIdValue?.ToString() : null;

            // Determine rate limit key and limits
            var rateLimitKey = GetRateLimitKey(httpRequestData, userId, apiKeyId);
            var rateLimitConfig = GetRateLimitConfiguration(httpRequestData, userRole, isAuthenticated);

            // Check rate limit
            var rateLimitResult = await _rateLimitService.CheckRateLimitAsync(rateLimitKey, rateLimitConfig);

            // Add rate limiting headers to response
            await AddRateLimitHeaders(context, rateLimitResult);

            if (!rateLimitResult.IsAllowed)
            {
                _logger.LogWarning("Rate limit exceeded for {RateLimitKey}. Limit: {Limit}, Current: {Current}", 
                    rateLimitKey, rateLimitConfig.RequestsPerMinute, rateLimitResult.CurrentRequests);

                await SetRateLimitExceededResponse(context, rateLimitResult);
                return;
            }

            // Store rate limit context for monitoring
            context.Items["RateLimitResult"] = rateLimitResult;
            context.Items["RateLimitConfig"] = rateLimitConfig;

            // Continue to next middleware/function
            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during API rate limiting for {Path}", httpRequestData.Url.AbsolutePath);
            
            // On rate limiting failure, allow the request to continue (fail open)
            await next(context);
        }
    }

    /// <summary>
    /// Determines if rate limiting should be skipped for this request
    /// </summary>
    private static bool ShouldSkipRateLimit(HttpRequestData request)
    {
        var path = request.Url.AbsolutePath.ToLowerInvariant();
        
        // Skip rate limiting for documentation and health check endpoints
        var skipPaths = new[]
        {
            "/api/health",
            "/api/docs",
            "/api/swagger",
            "/api/openapi",
            "/api/info"
        };

        return skipPaths.Any(skipPath => path.StartsWith(skipPath));
    }

    /// <summary>
    /// Gets the rate limit key for tracking requests
    /// </summary>
    private string GetRateLimitKey(HttpRequestData request, string? userId, string? apiKeyId)
    {
        // Use API key ID if available (most specific)
        if (!string.IsNullOrEmpty(apiKeyId))
        {
            return $"api_key:{apiKeyId}";
        }

        // Use user ID if available
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        // Fall back to IP address for unauthenticated requests
        var clientIp = GetClientIpAddress(request);
        return $"ip:{clientIp}";
    }

    /// <summary>
    /// Gets rate limit configuration based on endpoint and user role
    /// </summary>
    private RateLimitConfiguration GetRateLimitConfiguration(HttpRequestData request, string? userRole, bool isAuthenticated)
    {
        var path = request.Url.AbsolutePath.ToLowerInvariant();
        var method = request.Method.ToUpperInvariant();

        // Get base limits based on user role
        var baseLimits = GetRoleLimits(userRole, isAuthenticated);

        // Apply endpoint-specific modifiers
        var modifier = GetEndpointModifier(path, method);

        return new RateLimitConfiguration
        {
            RequestsPerMinute = (int)(baseLimits.RequestsPerMinute * modifier),
            RequestsPerHour = (int)(baseLimits.RequestsPerHour * modifier),
            BurstLimit = (int)(baseLimits.BurstLimit * modifier),
            WindowSizeMinutes = baseLimits.WindowSizeMinutes
        };
    }

    /// <summary>
    /// Gets base rate limits for user role
    /// </summary>
    private RateLimitConfiguration GetRoleLimits(string? userRole, bool isAuthenticated)
    {
        if (!isAuthenticated)
        {
            // Unauthenticated requests get very limited access
            return new RateLimitConfiguration
            {
                RequestsPerMinute = 10,
                RequestsPerHour = 100,
                BurstLimit = 5,
                WindowSizeMinutes = 1
            };
        }

        return userRole?.ToLowerInvariant() switch
        {
            "admin" => new RateLimitConfiguration
            {
                RequestsPerMinute = 1000,
                RequestsPerHour = 10000,
                BurstLimit = 100,
                WindowSizeMinutes = 1
            },
            "user" => new RateLimitConfiguration
            {
                RequestsPerMinute = 500,
                RequestsPerHour = 5000,
                BurstLimit = 50,
                WindowSizeMinutes = 1
            },
            "readonly" => new RateLimitConfiguration
            {
                RequestsPerMinute = 100,
                RequestsPerHour = 1000,
                BurstLimit = 20,
                WindowSizeMinutes = 1
            },
            _ => new RateLimitConfiguration
            {
                RequestsPerMinute = 100,
                RequestsPerHour = 1000,
                BurstLimit = 20,
                WindowSizeMinutes = 1
            }
        };
    }

    /// <summary>
    /// Gets endpoint-specific rate limit modifier
    /// </summary>
    private double GetEndpointModifier(string path, string method)
    {
        // More restrictive limits for write operations
        if (method == "POST" || method == "PUT" || method == "DELETE")
        {
            return 0.5; // 50% of normal limits for write operations
        }

        // Specific endpoint modifiers
        return path switch
        {
            var p when p.Contains("/migrations") && method == "POST" => 0.1, // Very limited migration starts
            var p when p.Contains("/auth/api-keys") && method == "POST" => 0.2, // Limited API key creation
            var p when p.Contains("/dashboard") => 2.0, // Higher limits for dashboard endpoints
            var p when p.Contains("/health") => 10.0, // Very high limits for health checks
            _ => 1.0 // Normal limits
        };
    }

    /// <summary>
    /// Gets client IP address from request
    /// </summary>
    private string GetClientIpAddress(HttpRequestData request)
    {
        // Try various headers for IP address
        var ipHeaders = new[] { "X-Forwarded-For", "X-Real-IP", "CF-Connecting-IP", "X-Client-IP" };
        
        foreach (var header in ipHeaders)
        {
            if (request.Headers.TryGetValues(header, out var values))
            {
                var ip = values.FirstOrDefault()?.Split(',')[0]?.Trim();
                if (!string.IsNullOrEmpty(ip) && ip != "unknown")
                {
                    return ip;
                }
            }
        }

        // Fall back to connection remote IP
        return request.Url.Host; // Simplified for Azure Functions
    }

    /// <summary>
    /// Adds rate limiting headers to the response
    /// </summary>
    private Task AddRateLimitHeaders(FunctionContext context, RateLimitResult result)
    {
        var response = context.GetHttpResponseData();
        if (response == null) return Task.CompletedTask;

        // Add standard rate limit headers
        response.Headers.Add("X-RateLimit-Limit", result.RequestsPerMinute.ToString());
        response.Headers.Add("X-RateLimit-Remaining", Math.Max(0, result.RequestsPerMinute - result.CurrentRequests).ToString());
        response.Headers.Add("X-RateLimit-Reset", result.ResetTime.ToString());
        
        if (!result.IsAllowed)
        {
            response.Headers.Add("Retry-After", result.RetryAfterSeconds.ToString());
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets rate limit exceeded response
    /// </summary>
    private async Task SetRateLimitExceededResponse(FunctionContext context, RateLimitResult result)
    {
        var response = context.GetHttpResponseData();
        if (response == null) return;

        response.StatusCode = HttpStatusCode.TooManyRequests;
        response.Headers.Add("Content-Type", "application/json");

        var errorResponse = new
        {
            error = new
            {
                code = "RATE_LIMIT_EXCEEDED",
                message = "Rate limit exceeded",
                details = $"You have exceeded the rate limit of {result.RequestsPerMinute} requests per minute. Please try again later.",
                rateLimitInfo = new
                {
                    limit = result.RequestsPerMinute,
                    current = result.CurrentRequests,
                    resetTime = result.ResetTime,
                    retryAfterSeconds = result.RetryAfterSeconds
                },
                timestamp = DateTime.UtcNow,
                documentation = "https://docs.bigcommerce-migration.com/api/rate-limiting"
            }
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}

/// <summary>
/// Service interface for API rate limiting
/// </summary>
public interface IApiRateLimitService
{
    /// <summary>
    /// Checks if a request is allowed under rate limiting rules
    /// </summary>
    Task<RateLimitResult> CheckRateLimitAsync(string key, RateLimitConfiguration config);

    /// <summary>
    /// Gets current rate limit status for a key
    /// </summary>
    Task<RateLimitStatus> GetRateLimitStatusAsync(string key);

    /// <summary>
    /// Resets rate limit for a specific key (admin operation)
    /// </summary>
    Task ResetRateLimitAsync(string key);
}

/// <summary>
/// Rate limit configuration
/// </summary>
public class RateLimitConfiguration
{
    public int RequestsPerMinute { get; set; }
    public int RequestsPerHour { get; set; }
    public int BurstLimit { get; set; }
    public int WindowSizeMinutes { get; set; }
}

/// <summary>
/// Rate limit check result
/// </summary>
public class RateLimitResult
{
    public bool IsAllowed { get; set; }
    public int RequestsPerMinute { get; set; }
    public int CurrentRequests { get; set; }
    public long ResetTime { get; set; }
    public int RetryAfterSeconds { get; set; }
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Rate limit status information
/// </summary>
public class RateLimitStatus
{
    public string Key { get; set; } = string.Empty;
    public int RequestsPerMinute { get; set; }
    public int CurrentRequests { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public TimeSpan TimeUntilReset { get; set; }
} 