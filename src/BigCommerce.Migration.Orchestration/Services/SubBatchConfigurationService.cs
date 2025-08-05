using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Implementation of ISubBatchConfigurationService that loads configurations from appsettings.json
/// Provides optimized sub-batch settings for each entity type to balance performance and rate limiting
/// </summary>
public class SubBatchConfigurationService : ISubBatchConfigurationService
{
    private readonly ILogger<SubBatchConfigurationService> _logger;
    private readonly Dictionary<string, SubBatchConfiguration> _configurations;

    public SubBatchConfigurationService(IConfiguration configuration, ILogger<SubBatchConfigurationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configurations = LoadConfigurations(configuration);
        
        _logger.LogInformation("🔧 [SUB-BATCH] Loaded {ConfigCount} sub-batch configurations", _configurations.Count);
    }

    public SubBatchConfiguration GetConfiguration(string entityType)
    {
        if (string.IsNullOrEmpty(entityType))
        {
            _logger.LogWarning("⚠️ [SUB-BATCH] Empty entity type provided, using default configuration");
            return GetDefaultConfiguration();
        }

        var normalizedEntityType = entityType.ToLowerInvariant();
        
        if (_configurations.TryGetValue(normalizedEntityType, out var config))
        {
            _logger.LogDebug("🎯 [SUB-BATCH] Found configuration for {EntityType}: " +
                           "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, DelayMs={DelayMs}",
                entityType, config.SubBatchSize, config.MaxConcurrency, config.SubBatchDelayMs);
            return config;
        }

        _logger.LogWarning("⚠️ [SUB-BATCH] No configuration found for entity type '{EntityType}', using default", entityType);
        return GetDefaultConfiguration();
    }

    public Dictionary<string, SubBatchConfiguration> GetAllConfigurations()
    {
        return new Dictionary<string, SubBatchConfiguration>(_configurations);
    }

    /// <summary>
    /// Loads sub-batch configurations from appsettings.json
    /// </summary>
    private Dictionary<string, SubBatchConfiguration> LoadConfigurations(IConfiguration configuration)
    {
        var configurations = new Dictionary<string, SubBatchConfiguration>();

        try
        {
            // Load configurations from appsettings.json ParallelProcessing section
            var parallelProcessingSection = configuration.GetSection("ParallelProcessing");
            var subBatchConfigsSection = parallelProcessingSection.GetSection("subBatchConfigurations");

            if (subBatchConfigsSection.Exists())
            {
                // Load each entity-specific configuration
                foreach (var entitySection in subBatchConfigsSection.GetChildren())
                {
                    var entityType = entitySection.Key.ToLowerInvariant();
                    var config = new SubBatchConfiguration
                    {
                        EntityType = entityType,
                        PageSize = entitySection.GetValue<int>("pageSize", 50),
                        SubBatchSize = entitySection.GetValue<int>("subBatchSize", 5),
                        MaxConcurrency = entitySection.GetValue<int>("maxConcurrency", 5),
                        EnableSubBatching = entitySection.GetValue<bool>("enableSubBatching", true),
                        SubBatchDelayMs = entitySection.GetValue<int>("subBatchDelayMs", 0),
                        ProcessSubBatchesSequentially = entitySection.GetValue<bool>("processSubBatchesSequentially", true)
                    };

                    configurations[entityType] = config;
                    _logger.LogDebug("📋 [SUB-BATCH] Loaded configuration for {EntityType}: " +
                                   "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}",
                        entityType, config.SubBatchSize, config.MaxConcurrency);
                }
            }

            // Load default configuration
            var defaultSection = parallelProcessingSection.GetSection("defaultSubBatchConfiguration");
            if (defaultSection.Exists())
            {
                var defaultConfig = new SubBatchConfiguration
                {
                    EntityType = "default",
                    PageSize = defaultSection.GetValue<int>("pageSize", 50),
                    SubBatchSize = defaultSection.GetValue<int>("subBatchSize", 5),
                    MaxConcurrency = defaultSection.GetValue<int>("maxConcurrency", 5),
                    EnableSubBatching = defaultSection.GetValue<bool>("enableSubBatching", true),
                    SubBatchDelayMs = defaultSection.GetValue<int>("subBatchDelayMs", 0),
                    ProcessSubBatchesSequentially = defaultSection.GetValue<bool>("processSubBatchesSequentially", true)
                };

                configurations["default"] = defaultConfig;
            }

            _logger.LogInformation("✅ [SUB-BATCH] Successfully loaded {ConfigCount} configurations from appsettings.json", 
                configurations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [SUB-BATCH] Failed to load configurations from appsettings.json, using defaults");
            
            // Fallback to hard-coded defaults if configuration loading fails
            configurations = SubBatchConfiguration.GetDefaultConfigurations();
        }

        return configurations;
    }

    /// <summary>
    /// Gets the default sub-batch configuration when no specific configuration is found
    /// </summary>
    private SubBatchConfiguration GetDefaultConfiguration()
    {
        if (_configurations.TryGetValue("default", out var defaultConfig))
        {
            return defaultConfig;
        }

        // Ultimate fallback configuration
        return new SubBatchConfiguration
        {
            EntityType = "fallback",
            PageSize = 50,
            SubBatchSize = 5,
            MaxConcurrency = 5,
            EnableSubBatching = true,
            SubBatchDelayMs = 0,
            ProcessSubBatchesSequentially = true
        };
    }
}