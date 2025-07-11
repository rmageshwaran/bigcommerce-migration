using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Configuration settings for OpenSearch connection and indexing
/// Contains connection parameters, timeouts, and indexing preferences
/// </summary>
public class OpenSearchConfiguration
{
    /// <summary>
    /// OpenSearch endpoint URL
    /// </summary>
    [Required]
    public string? Endpoint { get; set; }

    /// <summary>
    /// Username for authentication (optional)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for authentication (optional)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Default index name for migration logs
    /// </summary>
    public string DefaultIndex { get; set; } = "bigcommerce-migration";

    /// <summary>
    /// Connection timeout for OpenSearch client
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Request timeout for OpenSearch operations
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum number of retries for failed operations
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Enable debug mode for detailed logging
    /// </summary>
    public bool EnableDebugMode { get; set; } = false;

    /// <summary>
    /// Validates if the endpoint is a valid URL
    /// </summary>
    /// <returns>True if endpoint is valid, false otherwise</returns>
    public bool IsValidEndpoint()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
            return false;

        return Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && 
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Checks if credentials are provided
    /// </summary>
    /// <returns>True if both username and password are provided, false otherwise</returns>
    public bool HasCredentials()
    {
        return !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
    }

    /// <summary>
    /// Gets a connection string representation (with masked password for security)
    /// </summary>
    /// <returns>Connection string with masked credentials</returns>
    public string GetConnectionString()
    {
        var connectionString = $"Endpoint: {Endpoint}";
        
        if (HasCredentials())
        {
            connectionString += $", Username: {Username}, Password: ***";
        }
        
        connectionString += $", DefaultIndex: {DefaultIndex}";
        connectionString += $", ConnectionTimeout: {ConnectionTimeout}";
        connectionString += $", RequestTimeout: {RequestTimeout}";
        connectionString += $", MaxRetries: {MaxRetries}";
        
        return connectionString;
    }
} 