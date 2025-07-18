using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Concrete implementation of IMigrationConfigurationProvider that wraps Microsoft.Extensions.Configuration
/// Enforces Dependency Inversion Principle by providing abstraction over configuration access
/// </summary>
public class MigrationConfigurationProvider : IMigrationConfigurationProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MigrationConfigurationProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the MigrationConfigurationProvider
    /// </summary>
    /// <param name="configuration">Microsoft configuration service</param>
    /// <param name="logger">Logger instance</param>
    public MigrationConfigurationProvider(IConfiguration configuration, ILogger<MigrationConfigurationProvider> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string? GetConnectionString(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Connection string name cannot be null or empty", nameof(name));

        var connectionString = _configuration.GetConnectionString(name);
        _logger.LogDebug("Retrieved connection string for {Name}: {Found}", name, connectionString != null ? "Found" : "Not Found");
        return connectionString;
    }

    /// <inheritdoc />
    public BigCommerceConfiguration GetBigCommerceConfiguration()
    {
        _logger.LogDebug("Loading BigCommerce configuration");
        
        var config = new BigCommerceConfiguration();
        var section = _configuration.GetSection("BigCommerce");
        
        if (section.Exists())
        {
            section.Bind(config);
        }
        else
        {
            _logger.LogWarning("BigCommerce configuration section not found, using defaults");
            // Set defaults
            config.BaseUrl = "https://api.bigcommerce.com";
            config.RequestTimeout = TimeSpan.FromSeconds(30);
            config.MaxRetries = 0;
            config.RateLimitRequestsPerSecond = 12;
            config.EnableDebugLogging = false;
            config.UserAgent = "BigCommerce-Migration-System/1.0";
        }

        _logger.LogDebug("BigCommerce configuration loaded: BaseUrl={BaseUrl}, Timeout={Timeout}s", 
            config.BaseUrl, config.RequestTimeout.TotalSeconds);

        return config;
    }

    /// <inheritdoc />
    public OpenSearchConfiguration GetOpenSearchConfiguration()
    {
        _logger.LogDebug("Loading OpenSearch configuration");
        
        var config = new OpenSearchConfiguration();
        var section = _configuration.GetSection("OpenSearch");
        
        if (section.Exists())
        {
            section.Bind(config);
        }
        else
        {
            _logger.LogWarning("OpenSearch configuration section not found, using defaults");
            // Set defaults
            config.Endpoint = "https://localhost:9200";
            config.DefaultIndex = "bigcommerce-migration";
            config.ConnectionTimeout = TimeSpan.FromSeconds(30);
            config.RequestTimeout = TimeSpan.FromSeconds(60);
            config.MaxRetries = 3;
            config.EnableDebugMode = false;
        }

        _logger.LogDebug("OpenSearch configuration loaded: Endpoint={Endpoint}, Index={Index}", 
            config.Endpoint, config.DefaultIndex);

        return config;
    }

    /// <inheritdoc />
    public string GetConfigurationValue(string key, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Configuration key cannot be null or empty", nameof(key));

        var value = _configuration[key] ?? defaultValue;
        _logger.LogDebug("Retrieved configuration value for {Key}: {HasValue}", key, value != defaultValue ? "Custom" : "Default");
        return value;
    }

    /// <inheritdoc />
    public bool GetBooleanValue(string key, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Configuration key cannot be null or empty", nameof(key));

        var stringValue = _configuration[key];
        if (string.IsNullOrWhiteSpace(stringValue))
        {
            _logger.LogDebug("Boolean configuration key {Key} not found, using default: {DefaultValue}", key, defaultValue);
            return defaultValue;
        }

        if (bool.TryParse(stringValue, out var result))
        {
            _logger.LogDebug("Boolean configuration key {Key} parsed: {Value}", key, result);
            return result;
        }

        _logger.LogWarning("Boolean configuration key {Key} has invalid value '{StringValue}', using default: {DefaultValue}", 
            key, stringValue, defaultValue);
        return defaultValue;
    }

    /// <inheritdoc />
    public int GetIntegerValue(string key, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Configuration key cannot be null or empty", nameof(key));

        var stringValue = _configuration[key];
        if (string.IsNullOrWhiteSpace(stringValue))
        {
            _logger.LogDebug("Integer configuration key {Key} not found, using default: {DefaultValue}", key, defaultValue);
            return defaultValue;
        }

        if (int.TryParse(stringValue, out var result))
        {
            _logger.LogDebug("Integer configuration key {Key} parsed: {Value}", key, result);
            return result;
        }

        _logger.LogWarning("Integer configuration key {Key} has invalid value '{StringValue}', using default: {DefaultValue}", 
            key, stringValue, defaultValue);
        return defaultValue;
    }

    /// <inheritdoc />
    public TimeSpan GetTimeSpanValue(string key, TimeSpan defaultValue)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Configuration key cannot be null or empty", nameof(key));

        var stringValue = _configuration[key];
        if (string.IsNullOrWhiteSpace(stringValue))
        {
            _logger.LogDebug("TimeSpan configuration key {Key} not found, using default: {DefaultValue}", key, defaultValue);
            return defaultValue;
        }

        if (TimeSpan.TryParse(stringValue, out var result))
        {
            _logger.LogDebug("TimeSpan configuration key {Key} parsed: {Value}", key, result);
            return result;
        }

        _logger.LogWarning("TimeSpan configuration key {Key} has invalid value '{StringValue}', using default: {DefaultValue}", 
            key, stringValue, defaultValue);
        return defaultValue;
    }
} 