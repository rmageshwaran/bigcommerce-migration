using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Functions.Middleware;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;

namespace BigCommerce.Migration.Functions.Functions;

/// <summary>
/// API key management functions for testing authentication
/// </summary>
public class ApiKeyManagementFunctions
{
    private readonly ILogger<ApiKeyManagementFunctions> _logger;
    private readonly IApiKeyService _apiKeyService;

    public ApiKeyManagementFunctions(
        ILogger<ApiKeyManagementFunctions> logger,
        IApiKeyService apiKeyService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiKeyService = apiKeyService ?? throw new ArgumentNullException(nameof(apiKeyService));
    }

    /// <summary>
    /// Test endpoint to verify authentication is working
    /// </summary>
    [Function("GetAuthStatus")]
    public async Task<HttpResponseData> GetAuthStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "auth/status")] HttpRequestData req,
        FunctionContext context)
    {
        _logger.LogInformation("Testing authentication status");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        // Get user context from authentication middleware
        var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                             isAuthenticatedValue is bool authenticated && authenticated;

        var statusData = new
        {
            authenticated = isAuthenticated,
            userId = context.Items.TryGetValue("UserId", out var userId) ? userId?.ToString() : null,
            role = context.Items.TryGetValue("UserRole", out var role) ? role?.ToString() : null,
            apiKeyId = context.Items.TryGetValue("ApiKeyId", out var apiKeyId) ? apiKeyId?.ToString() : null,
            rateLimit = context.Items.TryGetValue("RateLimit", out var rateLimit) ? rateLimit?.ToString() : null,
            timestamp = DateTime.UtcNow,
            message = isAuthenticated ? "Authentication successful" : "Not authenticated"
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(statusData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        return response;
    }

    /// <summary>
    /// Create a new API key (Admin only)
    /// </summary>
    [Function("CreateApiKey")]
    public async Task<HttpResponseData> CreateApiKey(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "auth/api-keys")] HttpRequestData req,
        FunctionContext context)
    {
        _logger.LogInformation("Creating new API key");

        try
        {
            // Check if user is authenticated and has admin role
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;

            if (!isAuthenticated)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Authentication required");
            }

            var userRole = context.Items.TryGetValue("UserRole", out var roleValue) ? roleValue?.ToString() : null;
            if (userRole != ApiKeyRole.Admin.ToString())
            {
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "Admin role required");
            }

            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var createRequest = JsonSerializer.Deserialize<CreateApiKeyRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (createRequest == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid request body");
            }

            // Create API key
            var apiKey = await _apiKeyService.CreateApiKeyAsync(
                createRequest.UserId,
                createRequest.Name,
                createRequest.Role,
                createRequest.ExpirationDays.HasValue ? TimeSpan.FromDays(createRequest.ExpirationDays.Value) : null);

            var response = req.CreateResponse(HttpStatusCode.Created);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new
            {
                id = apiKey.Id,
                name = apiKey.Name,
                userId = apiKey.UserId,
                role = apiKey.Role.ToString(),
                rateLimit = apiKey.RateLimit,
                apiKey = apiKey.KeyHash, // This contains the actual key, only returned once
                createdAt = apiKey.CreatedAt,
                expiresAt = apiKey.ExpiresAt,
                message = "API key created successfully. Store this key securely - it will not be shown again."
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating API key");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Failed to create API key");
        }
    }

    /// <summary>
    /// Get user's API keys
    /// </summary>
    [Function("GetUserApiKeys")]
    public async Task<HttpResponseData> GetUserApiKeys(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "auth/api-keys")] HttpRequestData req,
        FunctionContext context)
    {
        _logger.LogInformation("Getting user API keys");

        try
        {
            // Check if user is authenticated
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;

            if (!isAuthenticated)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Authentication required");
            }

            var userId = context.Items.TryGetValue("UserId", out var userIdValue) ? userIdValue?.ToString() : null;
            if (string.IsNullOrEmpty(userId))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "User ID not found");
            }

            // Get user's API keys
            var apiKeys = await _apiKeyService.GetUserApiKeysAsync(userId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new
            {
                apiKeys = apiKeys.Select(key => new
                {
                    id = key.Id,
                    name = key.Name,
                    role = key.Role.ToString(),
                    rateLimit = key.RateLimit,
                    createdAt = key.CreatedAt,
                    expiresAt = key.ExpiresAt,
                    lastUsedAt = key.LastUsedAt,
                    isActive = key.IsActive
                }),
                totalCount = apiKeys.Count
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user API keys");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Failed to get API keys");
        }
    }

    /// <summary>
    /// Revoke an API key
    /// </summary>
    [Function("RevokeApiKey")]
    public async Task<HttpResponseData> RevokeApiKey(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "auth/api-keys/{apiKeyId}")] HttpRequestData req,
        string apiKeyId,
        FunctionContext context)
    {
        _logger.LogInformation("Revoking API key: {ApiKeyId}", apiKeyId);

        try
        {
            // Check if user is authenticated
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;

            if (!isAuthenticated)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Authentication required");
            }

            // Revoke API key
            var success = await _apiKeyService.RevokeApiKeyAsync(apiKeyId);

            if (!success)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "API key not found");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new
            {
                message = "API key revoked successfully",
                apiKeyId = apiKeyId,
                revokedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking API key: {ApiKeyId}", apiKeyId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Failed to revoke API key");
        }
    }

    /// <summary>
    /// Creates an error response
    /// </summary>
    private static async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string message)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");

        var errorResponse = new
        {
            error = new
            {
                code = statusCode.ToString(),
                message = message,
                timestamp = DateTime.UtcNow
            }
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        return response;
    }
}

/// <summary>
/// Request model for creating API keys
/// </summary>
public class CreateApiKeyRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ApiKeyRole Role { get; set; } = ApiKeyRole.User;
    public int? ExpirationDays { get; set; }
} 