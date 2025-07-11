using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using System.Text.Json;

namespace BigCommerce.Migration.Functions.Middleware;

/// <summary>
/// Middleware for API key authentication in Azure Functions
/// Validates API keys and sets user context for subsequent processing
/// </summary>
public class ApiKeyAuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly IApiKeyService _apiKeyService;

    public ApiKeyAuthenticationMiddleware(
        ILogger<ApiKeyAuthenticationMiddleware> logger,
        IApiKeyService apiKeyService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiKeyService = apiKeyService ?? throw new ArgumentNullException(nameof(apiKeyService));
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        // Check if this is an HTTP trigger function
        var httpRequestData = await context.GetHttpRequestDataAsync();
        if (httpRequestData == null)
        {
            // Not an HTTP function, continue without authentication
            await next(context);
            return;
        }

        // Skip authentication for certain endpoints
        if (ShouldSkipAuthentication(httpRequestData))
        {
            await next(context);
            return;
        }

        try
        {
            // Extract API key from headers
            var apiKey = ExtractApiKey(httpRequestData);
            
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("API key missing in request to {Path}", httpRequestData.Url.AbsolutePath);
                await SetUnauthorizedResponse(context, "API key is required");
                return;
            }

            // Validate API key
            var apiKeyValidation = await _apiKeyService.ValidateApiKeyAsync(apiKey);
            
            if (!apiKeyValidation.IsValid)
            {
                _logger.LogWarning("Invalid API key used for request to {Path}", httpRequestData.Url.AbsolutePath);
                await SetUnauthorizedResponse(context, "Invalid API key");
                return;
            }

            // Set user context for downstream processing
            SetUserContext(context, apiKeyValidation);

            // Log successful authentication
            _logger.LogDebug("API key authentication successful for user {UserId}", apiKeyValidation.UserId);

            // Continue to next middleware/function
            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during API key authentication");
            await SetInternalServerErrorResponse(context, "Authentication failed");
        }
    }

    /// <summary>
    /// Determines if authentication should be skipped for this request
    /// </summary>
    private static bool ShouldSkipAuthentication(HttpRequestData request)
    {
        var path = request.Url.AbsolutePath.ToLowerInvariant();
        
        // Skip authentication for health checks and documentation
        var skipPaths = new[]
        {
            "/api/health",
            "/api/docs",
            "/api/swagger",
            "/api/openapi",
            "/api/migrations",  // Skip for local development testing
            "/api/debug"        // Skip for debug endpoints
        };

        return skipPaths.Any(skipPath => path.StartsWith(skipPath));
    }

    /// <summary>
    /// Extracts API key from request headers
    /// </summary>
    private static string? ExtractApiKey(HttpRequestData request)
    {
        // Try X-API-Key header first
        if (request.Headers.TryGetValues("X-API-Key", out var apiKeyValues))
        {
            return apiKeyValues.FirstOrDefault();
        }

        // Try Authorization header with ApiKey scheme
        if (request.Headers.TryGetValues("Authorization", out var authValues))
        {
            var authValue = authValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(authValue) && authValue.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            {
                return authValue.Substring(7); // Remove "ApiKey " prefix
            }
        }

        // Try query parameter as fallback (less secure)
        var query = request.Url.Query;
        if (!string.IsNullOrEmpty(query))
        {
            var queryParams = QueryHelpers.ParseQuery(query);
            if (queryParams.TryGetValue("api_key", out StringValues apiKeyValue))
            {
                return apiKeyValue.FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>
    /// Sets user context in the function context for downstream use
    /// </summary>
    private static void SetUserContext(FunctionContext context, ApiKeyValidationResult validationResult)
    {
        context.Items["UserId"] = validationResult.UserId;
        context.Items["UserRole"] = validationResult.Role;
        context.Items["ApiKeyId"] = validationResult.ApiKeyId;
        context.Items["RateLimit"] = validationResult.RateLimit;
        context.Items["IsAuthenticated"] = true;
    }

    /// <summary>
    /// Sets unauthorized response
    /// </summary>
    private static async Task SetUnauthorizedResponse(FunctionContext context, string message)
    {
        var response = context.GetHttpResponseData();
        if (response != null)
        {
            response.StatusCode = HttpStatusCode.Unauthorized;
            response.Headers.Add("Content-Type", "application/json");
            
            var errorResponse = new
            {
                error = new
                {
                    code = "UNAUTHORIZED",
                    message = message,
                    timestamp = DateTime.UtcNow,
                    documentation = "https://docs.bigcommerce-migration.com/authentication"
                }
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }

    /// <summary>
    /// Sets internal server error response
    /// </summary>
    private static async Task SetInternalServerErrorResponse(FunctionContext context, string message)
    {
        var response = context.GetHttpResponseData();
        if (response != null)
        {
            response.StatusCode = HttpStatusCode.InternalServerError;
            response.Headers.Add("Content-Type", "application/json");
            
            var errorResponse = new
            {
                error = new
                {
                    code = "INTERNAL_ERROR",
                    message = message,
                    timestamp = DateTime.UtcNow
                }
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }
}

/// <summary>
/// Service interface for API key management and validation
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// Validates an API key and returns user information
    /// </summary>
    Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey);

    /// <summary>
    /// Creates a new API key for a user
    /// </summary>
    Task<ApiKey> CreateApiKeyAsync(string userId, string name, ApiKeyRole role, TimeSpan? expiration = null);

    /// <summary>
    /// Revokes an API key
    /// </summary>
    Task<bool> RevokeApiKeyAsync(string apiKeyId);

    /// <summary>
    /// Gets API keys for a user
    /// </summary>
    Task<List<ApiKey>> GetUserApiKeysAsync(string userId);
}

/// <summary>
/// Result of API key validation
/// </summary>
public class ApiKeyValidationResult
{
    public bool IsValid { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ApiKeyId { get; set; } = string.Empty;
    public ApiKeyRole Role { get; set; }
    public int RateLimit { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// API key model
/// </summary>
public class ApiKey
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public ApiKeyRole Role { get; set; }
    public int RateLimit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// API key roles with different permissions
/// </summary>
public enum ApiKeyRole
{
    /// <summary>
    /// Read-only access to migration status and logs
    /// </summary>
    ReadOnly = 1,

    /// <summary>
    /// Can start and cancel migrations, read status
    /// </summary>
    User = 2,

    /// <summary>
    /// Full access including user management and system configuration
    /// </summary>
    Admin = 3
} 