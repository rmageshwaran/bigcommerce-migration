using BigCommerce.Migration.Core.Extensions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Functions.Middleware;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace BigCommerce.Migration.Functions.Services;

/// <summary>
/// Service for managing API keys and authentication
/// Uses Azure Table Storage for persistence
/// </summary>
public class ApiKeyService : IApiKeyService
{
    private readonly ILogger<ApiKeyService> _logger;
    private readonly IMigrationStorageService _storageService;
    private readonly IOpenSearchService _openSearchService;

    public ApiKeyService(
        ILogger<ApiKeyService> logger,
        IMigrationStorageService storageService,
        IOpenSearchService openSearchService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
    }

    /// <summary>
    /// Validates an API key and returns user information
    /// </summary>
    public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "API key is required"
                };
            }

            // Hash the provided API key to match stored hash
            var hashedApiKey = HashApiKey(apiKey);

            // Retrieve API key from storage
            var storedApiKey = await GetApiKeyByHashAsync(hashedApiKey);

            if (storedApiKey == null)
            {
                _logger.LogWarning("API key not found in storage");
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Invalid API key"
                };
            }

            // Check if API key is active
            if (!storedApiKey.IsActive)
            {
                _logger.LogWarning("Inactive API key used: {ApiKeyId}", storedApiKey.Id);
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "API key is inactive"
                };
            }

            // Check if API key has expired
            if (storedApiKey.ExpiresAt.HasValue && storedApiKey.ExpiresAt.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired API key used: {ApiKeyId}", storedApiKey.Id);
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "API key has expired"
                };
            }

            // Update last used timestamp
            await UpdateLastUsedAsync(storedApiKey.Id);

            return new ApiKeyValidationResult
            {
                IsValid = true,
                UserId = storedApiKey.UserId,
                ApiKeyId = storedApiKey.Id,
                Role = storedApiKey.Role,
                RateLimit = storedApiKey.RateLimit,
                ExpiresAt = storedApiKey.ExpiresAt
            };
        }
        catch (Exception ex)
        {
            // Use standardized error logging with OpenSearch integration
            await _openSearchService.LogStructuredErrorAsync(
                _logger,
                ex,
                component: nameof(ApiKeyService),
                operationContext: "ValidateApiKey",
                entityId: apiKey?.Substring(0, Math.Min(8, apiKey?.Length ?? 0)) + "***" // Partial key for tracking
            );
            
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorMessage = "Validation failed"
            };
        }
    }

    /// <summary>
    /// Creates a new API key for a user
    /// </summary>
    public async Task<ApiKey> CreateApiKeyAsync(string userId, string name, ApiKeyRole role, TimeSpan? expiration = null)
    {
        try
        {
            // Generate a new API key
            var apiKey = GenerateApiKey();
            var hashedApiKey = HashApiKey(apiKey);

            var newApiKey = new ApiKey
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Name = name,
                KeyHash = hashedApiKey,
                Role = role,
                RateLimit = GetDefaultRateLimit(role),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null,
                IsActive = true
            };

            // Store in Azure Table Storage
            await StoreApiKeyAsync(newApiKey);

            _logger.LogInformation("Created new API key for user {UserId} with role {Role}", userId, role);

            // Return the API key with the actual key value (only time it's returned)
            return new ApiKey
            {
                Id = newApiKey.Id,
                UserId = newApiKey.UserId,
                Name = newApiKey.Name,
                KeyHash = apiKey, // Return actual key, not hash
                Role = newApiKey.Role,
                RateLimit = newApiKey.RateLimit,
                CreatedAt = newApiKey.CreatedAt,
                ExpiresAt = newApiKey.ExpiresAt,
                IsActive = newApiKey.IsActive
            };
        }
        catch (Exception ex)
        {
            // Use standardized error logging with OpenSearch integration
            await _openSearchService.LogStructuredErrorAsync(
                _logger,
                ex,
                component: nameof(ApiKeyService),
                operationContext: "CreateApiKey",
                entityId: userId
            );
            
            // Infrastructure exception - should throw to stop the operation
            throw;
        }
    }

    /// <summary>
    /// Revokes an API key
    /// </summary>
    public async Task<bool> RevokeApiKeyAsync(string apiKeyId)
    {
        try
        {
            var apiKey = await GetApiKeyByIdAsync(apiKeyId);
            if (apiKey == null)
            {
                _logger.LogWarning("API key not found for revocation: {ApiKeyId}", apiKeyId);
                return false;
            }

            apiKey.IsActive = false;
            await UpdateApiKeyAsync(apiKey);

            _logger.LogInformation("Revoked API key: {ApiKeyId}", apiKeyId);
            return true;
        }
        catch (Exception ex)
        {
            // Use standardized error logging with OpenSearch integration
            await _openSearchService.LogStructuredErrorAsync(
                _logger,
                ex,
                component: nameof(ApiKeyService),
                operationContext: "RevokeApiKey",
                entityId: apiKeyId
            );
            
            return false;
        }
    }

    /// <summary>
    /// Gets API keys for a user
    /// </summary>
    public async Task<List<ApiKey>> GetUserApiKeysAsync(string userId)
    {
        try
        {
            var apiKeys = await GetApiKeysByUserIdAsync(userId);
            
            // Remove sensitive key hash information
            return apiKeys.Select(key => new ApiKey
            {
                Id = key.Id,
                UserId = key.UserId,
                Name = key.Name,
                KeyHash = "***", // Don't return actual hash
                Role = key.Role,
                RateLimit = key.RateLimit,
                CreatedAt = key.CreatedAt,
                ExpiresAt = key.ExpiresAt,
                LastUsedAt = key.LastUsedAt,
                IsActive = key.IsActive
            }).ToList();
        }
        catch (Exception ex)
        {
            // Use standardized error logging with OpenSearch integration
            await _openSearchService.LogStructuredErrorAsync(
                _logger,
                ex,
                component: nameof(ApiKeyService),
                operationContext: "GetApiKeysForUser",
                entityId: userId
            );
            
            // Infrastructure exception - should throw to stop the operation
            throw;
        }
    }

    /// <summary>
    /// Generates a new API key
    /// </summary>
    private static string GenerateApiKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-").TrimEnd('=');
    }

    /// <summary>
    /// Hashes an API key for storage
    /// </summary>
    private static string HashApiKey(string apiKey)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(hashedBytes);
    }

    /// <summary>
    /// Gets default rate limit based on role
    /// </summary>
    private static int GetDefaultRateLimit(ApiKeyRole role)
    {
        return role switch
        {
            ApiKeyRole.ReadOnly => 100,  // 100 requests per minute
            ApiKeyRole.User => 500,      // 500 requests per minute
            ApiKeyRole.Admin => 1000,    // 1000 requests per minute
            _ => 100
        };
    }

    // Storage operations (implementations will use Azure Table Storage)
    private async Task<ApiKey?> GetApiKeyByHashAsync(string hashedApiKey)
    {
        // This would query Azure Table Storage
        // For now, implementing a simple in-memory store for development
        
        // TODO: Implement actual Azure Table Storage queries
        // This is a placeholder implementation
        await Task.Delay(1); // Simulate async call
        
        // Development-only: Return a sample API key for testing
        if (hashedApiKey == HashApiKey("dev-test-key"))
        {
            return new ApiKey
            {
                Id = "dev-test-key-id",
                UserId = "dev-user",
                Name = "Development Test Key",
                KeyHash = hashedApiKey,
                Role = ApiKeyRole.Admin,
                RateLimit = 1000,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                ExpiresAt = DateTime.UtcNow.AddDays(365),
                IsActive = true
            };
        }

        return null;
    }

    private async Task<ApiKey?> GetApiKeyByIdAsync(string apiKeyId)
    {
        // TODO: Implement actual Azure Table Storage query
        await Task.Delay(1); // Simulate async call
        
        if (apiKeyId == "dev-test-key-id")
        {
            return new ApiKey
            {
                Id = "dev-test-key-id",
                UserId = "dev-user",
                Name = "Development Test Key",
                KeyHash = HashApiKey("dev-test-key"),
                Role = ApiKeyRole.Admin,
                RateLimit = 1000,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                ExpiresAt = DateTime.UtcNow.AddDays(365),
                IsActive = true
            };
        }

        return null;
    }

    private async Task<List<ApiKey>> GetApiKeysByUserIdAsync(string userId)
    {
        // TODO: Implement actual Azure Table Storage query
        await Task.Delay(1); // Simulate async call
        
        if (userId == "dev-user")
        {
            return new List<ApiKey>
            {
                new ApiKey
                {
                    Id = "dev-test-key-id",
                    UserId = "dev-user",
                    Name = "Development Test Key",
                    KeyHash = HashApiKey("dev-test-key"),
                    Role = ApiKeyRole.Admin,
                    RateLimit = 1000,
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    ExpiresAt = DateTime.UtcNow.AddDays(365),
                    IsActive = true
                }
            };
        }

        return new List<ApiKey>();
    }

    private async Task StoreApiKeyAsync(ApiKey apiKey)
    {
        // TODO: Implement actual Azure Table Storage storage
        await Task.Delay(1); // Simulate async call
        _logger.LogInformation("Stored API key: {ApiKeyId}", apiKey.Id);
    }

    private async Task UpdateApiKeyAsync(ApiKey apiKey)
    {
        // TODO: Implement actual Azure Table Storage update
        await Task.Delay(1); // Simulate async call
        _logger.LogInformation("Updated API key: {ApiKeyId}", apiKey.Id);
    }

    private async Task UpdateLastUsedAsync(string apiKeyId)
    {
        // TODO: Implement actual Azure Table Storage update
        await Task.Delay(1); // Simulate async call
        _logger.LogDebug("Updated last used timestamp for API key: {ApiKeyId}", apiKeyId);
    }
} 