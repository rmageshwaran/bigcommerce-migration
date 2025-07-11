using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Global BigCommerce API configuration settings
/// Does not contain store-specific credentials (those come from MigrationRequest)
/// </summary>
public class BigCommerceConfiguration
{
    /// <summary>
    /// Default BigCommerce API base URL
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.bigcommerce.com";

    /// <summary>
    /// Default request timeout for API calls
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default maximum number of retries for failed requests
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Default rate limit (requests per second) for BigCommerce API compliance
    /// </summary>
    public int RateLimitRequestsPerSecond { get; set; } = 12;

    /// <summary>
    /// Enable debug logging for API requests
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// User agent string for API requests
    /// </summary>
    public string UserAgent { get; set; } = "BigCommerce-Migration-System/1.0";

    /// <summary>
    /// Validates the global configuration settings
    /// </summary>
    /// <returns>True if configuration is valid, false otherwise</returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(BaseUrl) &&
               Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               RequestTimeout.TotalSeconds > 0 &&
               MaxRetries >= 0 &&
               RateLimitRequestsPerSecond > 0;
    }

    /// <summary>
    /// Gets the API URL for a specific store
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <returns>Formatted API URL</returns>
    public string GetApiUrl(string storeId)
    {
        return $"{BaseUrl.TrimEnd('/')}/stores/{storeId}/v3";
    }

    /// <summary>
    /// Gets default HTTP headers for API requests
    /// </summary>
    /// <returns>Dictionary of default headers</returns>
    public Dictionary<string, string> GetDefaultHeaders()
    {
        return new Dictionary<string, string>
        {
            ["Accept"] = "application/json",
            ["User-Agent"] = UserAgent
        };
    }
} 