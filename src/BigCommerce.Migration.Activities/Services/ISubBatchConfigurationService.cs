using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Service for retrieving sub-batch configuration for different entity types
/// Provides entity-specific configurations for optimal parallel processing
/// </summary>
public interface ISubBatchConfigurationService
{
    /// <summary>
    /// Gets the sub-batch configuration for the specified entity type
    /// </summary>
    /// <param name="entityType">The entity type (e.g., "brands", "products", "categories")</param>
    /// <returns>Sub-batch configuration with optimal settings for the entity type</returns>
    SubBatchConfiguration GetConfiguration(string entityType);

    /// <summary>
    /// Gets all available sub-batch configurations
    /// </summary>
    /// <returns>Dictionary of entity type to configuration mappings</returns>
    Dictionary<string, SubBatchConfiguration> GetAllConfigurations();
}