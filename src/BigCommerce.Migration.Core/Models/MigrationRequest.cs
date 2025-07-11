namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Represents a migration request with multi-storefront support
/// Uses request-based store credentials instead of hardcoded configuration
/// </summary>
public class MigrationRequest
{
    /// <summary>
    /// Source BigCommerce store configuration including credentials and channel ID
    /// </summary>
    public StoreConfiguration? SourceStore { get; set; }

    /// <summary>
    /// Destination BigCommerce store configuration including credentials and channel ID
    /// </summary>
    public StoreConfiguration? DestinationStore { get; set; }

    /// <summary>
    /// List of entity types to migrate (e.g., "Categories", "Products", "Customers")
    /// </summary>
    public List<string> Entities { get; set; } = new();

    /// <summary>
    /// Optional migration configuration settings
    /// </summary>
    public MigrationSettings? Settings { get; set; }

    /// <summary>
    /// Validates that the migration request has all required information
    /// </summary>
    /// <returns>True if request is valid, false otherwise</returns>
    public bool IsValid()
    {
        return SourceStore != null && SourceStore.IsValid() &&
               DestinationStore != null && DestinationStore.IsValid() &&
               Entities.Any();
    }

    /// <summary>
    /// Gets a summary of the migration request for logging
    /// </summary>
    /// <returns>Migration request summary</returns>
    public string GetSummary()
    {
        var entityList = string.Join(", ", Entities);
        return $"Migration: {SourceStore?.StoreId}[{SourceStore?.ChannelId}] -> {DestinationStore?.StoreId}[{DestinationStore?.ChannelId}], Entities: {entityList}";
    }
}

/// <summary>
/// Optional settings for migration configuration
/// </summary>
public class MigrationSettings
{
    /// <summary>
    /// Maximum API calls per second (default: 12 for BigCommerce compliance)
    /// </summary>
    public int MaxApiCallsPerSecond { get; set; } = 12;

    /// <summary>
    /// Enable adaptive batch sizing based on performance
    /// </summary>
    public bool EnableAdaptiveBatching { get; set; } = true;

    /// <summary>
    /// Log level for migration operations
    /// </summary>
    public string LogLevel { get; set; } = "INFO";

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of retries for failed requests
    /// </summary>
    public int MaxRetries { get; set; } = 3;
} 