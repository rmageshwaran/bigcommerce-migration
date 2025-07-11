using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using BigCommerce.Migration.Functions.Middleware;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace BigCommerce.Migration.Functions.Functions;

/// <summary>
/// HTTP functions for managing and monitoring API rate limits
/// </summary>
public class RateLimitManagementFunctions
{
    private readonly ILogger<RateLimitManagementFunctions> _logger;
    private readonly IApiRateLimitService _rateLimitService;

    public RateLimitManagementFunctions(
        ILogger<RateLimitManagementFunctions> logger,
        IApiRateLimitService rateLimitService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
    }

    /// <summary>
    /// Gets current rate limit status for authenticated user
    /// </summary>
    [Function("GetRateLimitStatus")]
    [OpenApiOperation(operationId: "GetRateLimitStatus", tags: new[] { "Rate Limiting" },
        Summary = "Get current rate limit status",
        Description = "Returns the current rate limit status for the authenticated user, including usage statistics and remaining quota.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Rate limit status retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(object),
        Summary = "Authentication required")]
    public async Task<HttpResponseData> GetRateLimitStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "rate-limit/status")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            // Get user context from middleware
            var context = req.FunctionContext;
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;

            if (!isAuthenticated)
            {
                response.StatusCode = HttpStatusCode.Unauthorized;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "UNAUTHORIZED",
                        message = "Authentication required to view rate limit status"
                    }
                }));
                return response;
            }

            var userId = context.Items.TryGetValue("UserId", out var userIdValue) ? userIdValue?.ToString() : null;
            var apiKeyId = context.Items.TryGetValue("ApiKeyId", out var keyIdValue) ? keyIdValue?.ToString() : null;
            var userRole = context.Items.TryGetValue("UserRole", out var roleValue) ? roleValue?.ToString() : null;

            // Get rate limit key (same logic as middleware)
            var rateLimitKey = GetRateLimitKey(req, userId, apiKeyId);
            
            var status = await _rateLimitService.GetRateLimitStatusAsync(rateLimitKey);

            var result = new
            {
                key = rateLimitKey,
                userRole = userRole,
                status = new
                {
                    currentRequests = status.CurrentRequests,
                    windowStart = status.WindowStart,
                    windowEnd = status.WindowEnd,
                    timeUntilReset = status.TimeUntilReset.ToString(@"hh\:mm\:ss")
                },
                limits = GetRoleLimits(userRole, isAuthenticated)
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rate limit status");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "INTERNAL_ERROR",
                    message = "An error occurred while retrieving rate limit status"
                }
            }));
            return response;
        }
    }

    /// <summary>
    /// Resets rate limit for a specific user (Admin only)
    /// </summary>
    [Function("ResetRateLimit")]
    [OpenApiOperation(operationId: "ResetRateLimit", tags: new[] { "Rate Limiting" },
        Summary = "Reset rate limit for a specific key",
        Description = "Resets the rate limit counter for a specific user or API key. This endpoint requires admin privileges.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(RateLimitResetRequest), Required = true,
        Description = "Rate limit reset request containing the key to reset")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Rate limit reset successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object),
        Summary = "Admin role required")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object),
        Summary = "Invalid request")]
    public async Task<HttpResponseData> ResetRateLimitAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "rate-limit/reset")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            // Get user context from middleware
            var context = req.FunctionContext;
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;
            var userRole = context.Items.TryGetValue("UserRole", out var roleValue) ? roleValue?.ToString() : null;

            if (!isAuthenticated || userRole?.ToLowerInvariant() != "admin")
            {
                response.StatusCode = HttpStatusCode.Forbidden;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "FORBIDDEN",
                        message = "Admin role required to reset rate limits"
                    }
                }));
                return response;
            }

            // Parse request body
            var body = await req.ReadAsStringAsync();
            var request = JsonSerializer.Deserialize<RateLimitResetRequest>(body ?? "{}");

            if (string.IsNullOrEmpty(request?.Key))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "INVALID_REQUEST",
                        message = "Rate limit key is required"
                    }
                }));
                return response;
            }

            await _rateLimitService.ResetRateLimitAsync(request.Key);

            _logger.LogInformation("Rate limit reset for key {Key} by admin user", request.Key);

            var result = new
            {
                success = true,
                message = $"Rate limit reset successfully for key: {request.Key}",
                timestamp = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting rate limit");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "INTERNAL_ERROR",
                    message = "An error occurred while resetting rate limit"
                }
            }));
            return response;
        }
    }

    /// <summary>
    /// Gets current rate limit configuration and policies
    /// </summary>
    [Function("GetRateLimitPolicies")]
    [OpenApiOperation(operationId: "GetRateLimitPolicies", tags: new[] { "Rate Limiting" },
        Summary = "Get rate limit policies",
        Description = "Returns the current rate limiting policies and configurations for all user roles and endpoints.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Rate limit policies retrieved successfully")]
    public async Task<HttpResponseData> GetRateLimitPoliciesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "rate-limit/policies")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            var policies = new
            {
                roles = new
                {
                    admin = GetRoleLimits("admin", true),
                    user = GetRoleLimits("user", true),
                    readOnly = GetRoleLimits("readonly", true),
                    unauthenticated = GetRoleLimits(null, false)
                },
                endpointModifiers = new
                {
                    readOperations = new { modifier = 1.0, description = "Normal rate limits for GET requests" },
                    writeOperations = new { modifier = 0.5, description = "50% of normal limits for POST/PUT/DELETE" },
                    migrationStart = new { modifier = 0.1, description = "Very restrictive for migration start operations" },
                    apiKeyCreation = new { modifier = 0.2, description = "Limited API key creation" },
                    dashboardEndpoints = new { modifier = 2.0, description = "Higher limits for dashboard operations" },
                    healthChecks = new { modifier = 10.0, description = "Very high limits for health checks" }
                },
                headers = new
                {
                    rateLimitHeaders = new[]
                    {
                        "X-RateLimit-Limit: Maximum requests per minute",
                        "X-RateLimit-Remaining: Remaining requests in current window",
                        "X-RateLimit-Reset: Unix timestamp when rate limit resets",
                        "Retry-After: Seconds to wait before retrying (when rate limited)"
                    }
                },
                documentation = "https://docs.bigcommerce-migration.com/api/rate-limiting"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(policies, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rate limit policies");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "INTERNAL_ERROR",
                    message = "An error occurred while retrieving rate limit policies"
                }
            }));
            return response;
        }
    }

    /// <summary>
    /// Test endpoint to check rate limiting behavior
    /// </summary>
    [Function("TestRateLimit")]
    [OpenApiOperation(operationId: "TestRateLimit", tags: new[] { "Rate Limiting" },
        Summary = "Test rate limiting behavior",
        Description = "Test endpoint to check rate limiting behavior and view current rate limit context information.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Rate limit test completed successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.TooManyRequests, contentType: "application/json", bodyType: typeof(object),
        Summary = "Rate limit exceeded")]
    public async Task<HttpResponseData> TestRateLimitAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "rate-limit/test")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            // Get rate limit context from middleware
            var context = req.FunctionContext;
            var rateLimitResult = context.Items.TryGetValue("RateLimitResult", out var resultValue) ? 
                                 resultValue as RateLimitResult : null;
            var rateLimitConfig = context.Items.TryGetValue("RateLimitConfig", out var configValue) ? 
                                 configValue as RateLimitConfiguration : null;

            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;
            var userRole = context.Items.TryGetValue("UserRole", out var roleValue) ? roleValue?.ToString() : null;

            var result = new
            {
                message = "Rate limit test successful",
                timestamp = DateTime.UtcNow,
                authentication = new
                {
                    isAuthenticated = isAuthenticated,
                    userRole = userRole
                },
                rateLimitInfo = rateLimitResult != null ? new
                {
                    key = rateLimitResult.Key,
                    isAllowed = rateLimitResult.IsAllowed,
                    requestsPerMinute = rateLimitResult.RequestsPerMinute,
                    currentRequests = rateLimitResult.CurrentRequests,
                    resetTime = rateLimitResult.ResetTime
                } : null,
                rateLimitConfig = rateLimitConfig != null ? new
                {
                    requestsPerMinute = rateLimitConfig.RequestsPerMinute,
                    requestsPerHour = rateLimitConfig.RequestsPerHour,
                    burstLimit = rateLimitConfig.BurstLimit,
                    windowSizeMinutes = rateLimitConfig.WindowSizeMinutes
                } : null
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rate limit test");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "INTERNAL_ERROR",
                    message = "An error occurred during rate limit test"
                }
            }));
            return response;
        }
    }

    /// <summary>
    /// Helper method to get rate limit key (same logic as middleware)
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
    /// Helper method to get client IP address
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
    /// Helper method to get role limits (same logic as middleware)
    /// </summary>
    private object GetRoleLimits(string? userRole, bool isAuthenticated)
    {
        if (!isAuthenticated)
        {
            return new
            {
                requestsPerMinute = 10,
                requestsPerHour = 100,
                burstLimit = 5,
                windowSizeMinutes = 1,
                description = "Unauthenticated user limits"
            };
        }

        return userRole?.ToLowerInvariant() switch
        {
            "admin" => new
            {
                requestsPerMinute = 1000,
                requestsPerHour = 10000,
                burstLimit = 100,
                windowSizeMinutes = 1,
                description = "Administrator limits"
            },
            "user" => new
            {
                requestsPerMinute = 500,
                requestsPerHour = 5000,
                burstLimit = 50,
                windowSizeMinutes = 1,
                description = "Standard user limits"
            },
            "readonly" => new
            {
                requestsPerMinute = 100,
                requestsPerHour = 1000,
                burstLimit = 20,
                windowSizeMinutes = 1,
                description = "Read-only user limits"
            },
            _ => new
            {
                requestsPerMinute = 100,
                requestsPerHour = 1000,
                burstLimit = 20,
                windowSizeMinutes = 1,
                description = "Default user limits"
            }
        };
    }
}

/// <summary>
/// Request model for rate limit reset
/// </summary>
public class RateLimitResetRequest
{
    public string Key { get; set; } = string.Empty;
} 