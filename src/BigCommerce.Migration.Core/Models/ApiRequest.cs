namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Model representing an HTTP API request configuration
/// Contains all necessary information for making authenticated API calls
/// </summary>
public class ApiRequest
{
    /// <summary>
    /// The complete URL for the API request
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method for the request (GET, POST, PUT, DELETE, etc.)
    /// </summary>
    public HttpMethod Method { get; set; } = HttpMethod.Get;

    /// <summary>
    /// Store configuration containing authentication information
    /// </summary>
    public StoreConfiguration StoreConfiguration { get; set; } = new();

    /// <summary>
    /// Request content for POST/PUT requests (JSON string)
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Custom headers to include with the request
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Content type for the request (defaults to application/json)
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Timeout for the request (optional, uses default if not specified)
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Whether to retry the request on failure
    /// </summary>
    public bool EnableRetry { get; set; } = false; // 🚨 DISABLED: No retries to avoid rate limit issues

    /// <summary>
    /// Maximum number of retry attempts  
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 0; // 🚨 DISABLED: No retries

    /// <summary>
    /// Validates that the API request has all required information
    /// </summary>
    /// <returns>True if request is valid, false otherwise</returns>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(Url) &&
               Method != null &&
               StoreConfiguration?.IsValid() == true;
    }

    /// <summary>
    /// Creates a simple GET request
    /// </summary>
    /// <param name="url">Request URL</param>
    /// <param name="storeConfig">Store configuration</param>
    /// <returns>Configured API request</returns>
    public static ApiRequest CreateGet(string url, StoreConfiguration storeConfig)
    {
        return new ApiRequest
        {
            Url = url,
            Method = HttpMethod.Get,
            StoreConfiguration = storeConfig
        };
    }

    /// <summary>
    /// Creates a POST request with JSON content
    /// </summary>
    /// <param name="url">Request URL</param>
    /// <param name="content">JSON content</param>
    /// <param name="storeConfig">Store configuration</param>
    /// <returns>Configured API request</returns>
    public static ApiRequest CreatePost(string url, string content, StoreConfiguration storeConfig)
    {
        return new ApiRequest
        {
            Url = url,
            Method = HttpMethod.Post,
            Content = content,
            StoreConfiguration = storeConfig,
            ContentType = "application/json"
        };
    }

    /// <summary>
    /// Creates a PUT request with JSON content
    /// </summary>
    /// <param name="url">Request URL</param>
    /// <param name="content">JSON content</param>
    /// <param name="storeConfig">Store configuration</param>
    /// <returns>Configured API request</returns>
    public static ApiRequest CreatePut(string url, string content, StoreConfiguration storeConfig)
    {
        return new ApiRequest
        {
            Url = url,
            Method = HttpMethod.Put,
            Content = content,
            StoreConfiguration = storeConfig,
            ContentType = "application/json"
        };
    }
} 