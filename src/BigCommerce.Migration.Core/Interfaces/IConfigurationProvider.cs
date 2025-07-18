using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Abstraction for configuration access to enforce Dependency Inversion Principle
/// Provides strongly-typed configuration access without direct dependency on Microsoft.Extensions.Configuration
/// </summary>
public interface IMigrationConfigurationProvider
{
    /// <summary>
    /// Gets a connection string by name
    /// </summary>
    /// <param name="name">Connection string name</param>
    /// <returns>Connection string value or null if not found</returns>
    string? GetConnectionString(string name);
    
    /// <summary>
    /// Gets BigCommerce configuration settings
    /// </summary>
    /// <returns>BigCommerce configuration</returns>
    BigCommerceConfiguration GetBigCommerceConfiguration();
    
    /// <summary>
    /// Gets OpenSearch configuration settings
    /// </summary>
    /// <returns>OpenSearch configuration</returns>
    OpenSearchConfiguration GetOpenSearchConfiguration();
    
    /// <summary>
    /// Gets a configuration value by key with fallback to default
    /// </summary>
    /// <param name="key">Configuration key</param>
    /// <param name="defaultValue">Default value if key not found</param>
    /// <returns>Configuration value or default</returns>
    string GetConfigurationValue(string key, string defaultValue);
    
    /// <summary>
    /// Gets a boolean configuration value by key with fallback to default
    /// </summary>
    /// <param name="key">Configuration key</param>
    /// <param name="defaultValue">Default value if key not found or invalid</param>
    /// <returns>Boolean configuration value or default</returns>
    bool GetBooleanValue(string key, bool defaultValue);
    
    /// <summary>
    /// Gets an integer configuration value by key with fallback to default
    /// </summary>
    /// <param name="key">Configuration key</param>
    /// <param name="defaultValue">Default value if key not found or invalid</param>
    /// <returns>Integer configuration value or default</returns>
    int GetIntegerValue(string key, int defaultValue);
    
    /// <summary>
    /// Gets a TimeSpan configuration value by key with fallback to default
    /// </summary>
    /// <param name="key">Configuration key</param>
    /// <param name="defaultValue">Default value if key not found or invalid</param>
    /// <returns>TimeSpan configuration value or default</returns>
    TimeSpan GetTimeSpanValue(string key, TimeSpan defaultValue);
} 