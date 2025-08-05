#nullable disable
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity to get entity-specific configuration values for orchestrator use
/// </summary>
public class GetEntityConfigurationActivity
{
    private readonly ISubBatchConfigurationService _configService;
    private readonly ILogger<GetEntityConfigurationActivity> _logger;

    public GetEntityConfigurationActivity(
        ISubBatchConfigurationService configService,
        ILogger<GetEntityConfigurationActivity> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Get configuration for a specific entity type
    /// </summary>
    [Function("GetEntityConfigurationActivity")]
    public EntityConfiguration GetEntityConfiguration([ActivityTrigger] string entityType)
    {
        _logger.LogError("🎯🎯🎯 [CONFIG-ACTIVITY-DEBUG] ===== ACTIVITY CALLED for entityType: '{EntityType}' =====", entityType);
        
        // 🚨 FIX: Handle Durable Functions JSON serialization issue
        // When string parameters are passed to activities, they can get double-serialized
        // "brands" becomes "\"brands\"" - we need to clean this up
        var cleanEntityType = entityType;
        if (entityType.StartsWith("\"") && entityType.EndsWith("\""))
        {
            cleanEntityType = entityType.Trim('"');
            _logger.LogError("🔧 [CONFIG-ACTIVITY-DEBUG] FIXED serialization issue: '{OriginalEntityType}' → '{CleanEntityType}'", 
                entityType, cleanEntityType);
        }
        
        try
        {
            _logger.LogError("📋 [CONFIG-ACTIVITY-DEBUG] Calling _configService.GetConfiguration('{EntityType}')...", cleanEntityType);
            var config = _configService.GetConfiguration(cleanEntityType);
            
            _logger.LogError("📋 [CONFIG-ACTIVITY-DEBUG] SubBatchConfiguration received from service: " +
                           "EntityType={EntityType}, ChunkSize={ChunkSize}, FetchBatchSize={FetchBatchSize}, PageSize={PageSize}, " +
                           "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, ProcessSubBatchesSequentially={ProcessSubBatchesSequentially}",
                           config.EntityType, config.ChunkSize, config.FetchBatchSize, config.PageSize,
                           config.SubBatchSize, config.MaxConcurrency, config.ProcessSubBatchesSequentially);
            
            var entityConfig = new EntityConfiguration
            {
                EntityType = cleanEntityType, // ✅ Use cleaned entity type
                ChunkSize = config.ChunkSize,
                FetchBatchSize = config.FetchBatchSize,
                PageSize = config.PageSize,
                SubBatchSize = config.SubBatchSize,
                MaxConcurrency = config.MaxConcurrency,
                ProcessSubBatchesSequentially = config.ProcessSubBatchesSequentially
            };

            _logger.LogError("🚀 [CONFIG-ACTIVITY-DEBUG] FINAL EntityConfiguration to return: " +
                           "EntityType={EntityType}, ChunkSize={ChunkSize}, FetchBatchSize={FetchBatchSize}, PageSize={PageSize}, " +
                           "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, ProcessSubBatchesSequentially={ProcessSubBatchesSequentially}",
                           entityConfig.EntityType, entityConfig.ChunkSize, entityConfig.FetchBatchSize, entityConfig.PageSize,
                           entityConfig.SubBatchSize, entityConfig.MaxConcurrency, entityConfig.ProcessSubBatchesSequentially);

            _logger.LogError("🎯🎯🎯 [CONFIG-ACTIVITY-DEBUG] ===== ACTIVITY COMPLETED for '{EntityType}' =====", cleanEntityType);

            return entityConfig;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get configuration for entity type: {EntityType}", cleanEntityType);
            
            // Return safe defaults
            return new EntityConfiguration
            {
                EntityType = cleanEntityType, // ✅ Use cleaned entity type
                ChunkSize = 50,
                FetchBatchSize = cleanEntityType.Equals("brands", StringComparison.OrdinalIgnoreCase) ? 50 : 250,
                PageSize = 50,
                SubBatchSize = 5,
                MaxConcurrency = 5,
                ProcessSubBatchesSequentially = true
            };
        }
    }
}