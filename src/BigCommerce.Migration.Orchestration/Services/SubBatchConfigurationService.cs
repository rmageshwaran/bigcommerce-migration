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
        _logger.LogError("🔍🔍🔍 [CONFIG-SERVICE-DEBUG] REQUEST for entityType='{EntityType}'. Available configs: [{AvailableConfigs}]", 
            entityType, string.Join(", ", _configurations.Keys));

        if (string.IsNullOrEmpty(entityType))
        {
            _logger.LogError("🚨 [CONFIG-SERVICE-DEBUG] Empty entity type provided, using default configuration");
            var defaultConfig = GetDefaultConfiguration();
            _logger.LogError("🔧 [CONFIG-SERVICE-DEBUG] DEFAULT config returned: " +
                           "ChunkSize={ChunkSize}, FetchBatchSize={FetchBatchSize}, PageSize={PageSize}, " +
                           "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, ProcessSubBatchesSequentially={ProcessSubBatchesSequentially}",
                           defaultConfig.ChunkSize, defaultConfig.FetchBatchSize, defaultConfig.PageSize, 
                           defaultConfig.SubBatchSize, defaultConfig.MaxConcurrency, defaultConfig.ProcessSubBatchesSequentially);
            return defaultConfig;
        }

        var normalizedEntityType = entityType.ToLowerInvariant();
        _logger.LogError("🔍 [CONFIG-SERVICE-DEBUG] Normalized entityType: '{OriginalEntityType}' → '{NormalizedEntityType}'", 
            entityType, normalizedEntityType);
        
        if (_configurations.TryGetValue(normalizedEntityType, out var config))
        {
            _logger.LogError("✅ [CONFIG-SERVICE-DEBUG] FOUND specific config for '{EntityType}': " +
                           "ChunkSize={ChunkSize}, FetchBatchSize={FetchBatchSize}, PageSize={PageSize}, " +
                           "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, ProcessSubBatchesSequentially={ProcessSubBatchesSequentially}",
                           entityType, config.ChunkSize, config.FetchBatchSize, config.PageSize, 
                           config.SubBatchSize, config.MaxConcurrency, config.ProcessSubBatchesSequentially);
            return config;
        }

        _logger.LogError("⚠️ [CONFIG-SERVICE-DEBUG] NO CONFIG FOUND for '{EntityType}', using default", entityType);
        var fallbackConfig = GetDefaultConfiguration();
        _logger.LogError("🔧 [CONFIG-SERVICE-DEBUG] FALLBACK config for '{EntityType}': " +
                       "ChunkSize={ChunkSize}, FetchBatchSize={FetchBatchSize}, PageSize={PageSize}, " +
                       "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, ProcessSubBatchesSequentially={ProcessSubBatchesSequentially}",
                       entityType, fallbackConfig.ChunkSize, fallbackConfig.FetchBatchSize, fallbackConfig.PageSize, 
                       fallbackConfig.SubBatchSize, fallbackConfig.MaxConcurrency, fallbackConfig.ProcessSubBatchesSequentially);
        return fallbackConfig;
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
                        ChunkSize = entitySection.GetValue<int>("chunkSize", 50),
                        FetchBatchSize = entitySection.GetValue<int>("fetchBatchSize", entityType == "brands" ? 50 : 250),
                        SubBatchSize = entitySection.GetValue<int>("subBatchSize", 5),
                        MaxConcurrency = entitySection.GetValue<int>("maxConcurrency", 5),
                        EnableSubBatching = entitySection.GetValue<bool>("enableSubBatching", true),
                        SubBatchDelayMs = entitySection.GetValue<int>("subBatchDelayMs", 0),
                        ProcessSubBatchesSequentially = entitySection.GetValue<bool>("processSubBatchesSequentially", true),
                        Include = entitySection.GetValue<string>("include"), // ✅ Enhanced Product Migration: Load include parameter
                        EnableParallelSubEntities = entitySection.GetValue<bool>("enableParallelSubEntities", false) // ✅ Phase 2: Parallel sub-entity processing
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
                    ChunkSize = defaultSection.GetValue<int>("chunkSize", 50),
                    FetchBatchSize = defaultSection.GetValue<int>("fetchBatchSize", 250),
                    SubBatchSize = defaultSection.GetValue<int>("subBatchSize", 5),
                    MaxConcurrency = defaultSection.GetValue<int>("maxConcurrency", 5),
                    EnableSubBatching = defaultSection.GetValue<bool>("enableSubBatching", true),
                    SubBatchDelayMs = defaultSection.GetValue<int>("subBatchDelayMs", 0),
                    ProcessSubBatchesSequentially = defaultSection.GetValue<bool>("processSubBatchesSequentially", true)
                };

                configurations["default"] = defaultConfig;
                
                _logger.LogError("📋 [CONFIG-SERVICE-DEBUG] Loaded DEFAULT configuration: " +
                               "ChunkSize={ChunkSize}, FetchBatchSize={FetchBatchSize}, PageSize={PageSize}, " +
                               "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, ProcessSubBatchesSequentially={ProcessSubBatchesSequentially}",
                               defaultConfig.ChunkSize, defaultConfig.FetchBatchSize, defaultConfig.PageSize, 
                               defaultConfig.SubBatchSize, defaultConfig.MaxConcurrency, defaultConfig.ProcessSubBatchesSequentially);
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
            _logger.LogError("🔧 [CONFIG-SERVICE-DEBUG] Returning DEFAULT configuration from loaded configs");
            return defaultConfig;
        }

        // Ultimate fallback configuration
        _logger.LogError("🚨 [CONFIG-SERVICE-DEBUG] Creating ULTIMATE FALLBACK configuration");
        var fallbackConfig = new SubBatchConfiguration
        {
            EntityType = "fallback",
            PageSize = 50,
            ChunkSize = 50,
            FetchBatchSize = 250,
            SubBatchSize = 5,
            MaxConcurrency = 5,
            EnableSubBatching = true,
            SubBatchDelayMs = 0,
            ProcessSubBatchesSequentially = true
        };
        
        _logger.LogError("🔧 [CONFIG-SERVICE-DEBUG] ULTIMATE FALLBACK config: " +
                       "ChunkSize={ChunkSize}, FetchBatchSize={FetchBatchSize}, PageSize={PageSize}, " +
                       "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, ProcessSubBatchesSequentially={ProcessSubBatchesSequentially}",
                       fallbackConfig.ChunkSize, fallbackConfig.FetchBatchSize, fallbackConfig.PageSize, 
                       fallbackConfig.SubBatchSize, fallbackConfig.MaxConcurrency, fallbackConfig.ProcessSubBatchesSequentially);
        
        return fallbackConfig;
    }
}