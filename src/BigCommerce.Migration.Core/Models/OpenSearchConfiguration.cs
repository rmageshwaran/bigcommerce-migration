using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Sort order enumeration for OpenSearch queries
/// </summary>
public enum SortOrder
{
    /// <summary>
    /// Ascending sort order
    /// </summary>
    Ascending = 0,
    
    /// <summary>
    /// Descending sort order
    /// </summary>
    Descending = 1
}

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
    /// Validates if authentication credentials are provided
    /// </summary>
    /// <returns>True if both username and password are provided</returns>
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

/// <summary>
/// Structured query request model for optimized OpenSearch queries
/// </summary>
public class OpenSearchQuery
{
    /// <summary>
    /// Start date for the search range
    /// </summary>
    public DateTime FromDate { get; set; } = DateTime.UtcNow.AddDays(-1);

    /// <summary>
    /// End date for the search range
    /// </summary>
    public DateTime ToDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Log level filter (Debug, Information, Warning, Error, Critical)
    /// </summary>
    public string? Level { get; set; }

    /// <summary>
    /// Migration ID for filtering
    /// </summary>
    public string? MigrationId { get; set; }

    /// <summary>
    /// Entity type for filtering (Categories, Products, etc.)
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Free text search term
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Number of results to return (default: 50, max: 1000)
    /// </summary>
    public int Size { get; set; } = 50;

    /// <summary>
    /// Number of results to skip for pagination
    /// </summary>
    public int From { get; set; } = 0;

    /// <summary>
    /// Field to sort by (default: timestamp)
    /// </summary>
    public string SortField { get; set; } = "timestamp";

    /// <summary>
    /// Sort order (asc/desc, default: desc)
    /// </summary>
    public SortOrder SortOrder { get; set; } = SortOrder.Descending;

    /// <summary>
    /// Specific fields to include in the response (for performance optimization)
    /// </summary>
    public string[]? IncludeFields { get; set; }

    /// <summary>
    /// Enable query result highlighting
    /// </summary>
    public bool EnableHighlighting { get; set; } = false;

    /// <summary>
    /// Validates the query parameters
    /// </summary>
    public bool IsValid()
    {
        return FromDate <= ToDate && 
               Size > 0 && Size <= 1000 && 
               From >= 0;
    }

    /// <summary>
    /// Gets the current page number (1-based)
    /// </summary>
    public int CurrentPage => (From / Size) + 1;
} 