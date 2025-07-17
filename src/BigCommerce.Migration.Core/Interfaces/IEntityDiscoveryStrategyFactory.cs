using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Factory interface for selecting appropriate entity discovery strategies
/// Follows Strategy Pattern by encapsulating strategy selection logic
/// Supports Single Responsibility Principle by separating strategy selection from discovery logic
/// </summary>
public interface IEntityDiscoveryStrategyFactory
{
    /// <summary>
    /// Gets the appropriate entity discovery strategy based on store configuration and entity type
    /// </summary>
    /// <param name="storeConfig">Store configuration to determine API version and capabilities</param>
    /// <param name="entityType">Type of entity being discovered (products, categories, brands, etc.)</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Strategy instance appropriate for the given context</returns>
    /// <exception cref="NotSupportedException">Thrown when no strategy supports the detected API version</exception>
    Task<IEntityDiscoveryStrategy> GetStrategyAsync(
        StoreConfiguration storeConfig, 
        string entityType, 
        CancellationToken cancellationToken = default);
} 