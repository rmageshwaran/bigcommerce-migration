using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service for storing hierarchical option mappings in the OptionsMappingData field
/// Required for Phase 3 variant migration to lookup option and option_value ID mappings
/// </summary>
public interface IHierarchicalOptionMappingService
{
    /// <summary>
    /// Stores hierarchical option mapping in the product's OptionsMappingData field
    /// Updates the product's entity mapping with option and option_value ID mappings
    /// </summary>
    /// <param name="sourceOption">Source option data</param>
    /// <param name="createdOption">Created option data from BigCommerce</param>
    /// <param name="sourceOptionId">Source option ID</param>
    /// <param name="destinationOptionId">Destination option ID</param>
    /// <param name="sourceProductId">Source product ID</param>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StoreHierarchicalOptionMappingAsync(
        Dictionary<string, object> sourceOption,
        Dictionary<string, object> createdOption,
        string sourceOptionId,
        string destinationOptionId,
        string sourceProductId,
        string migrationId,
        CancellationToken cancellationToken = default);
}