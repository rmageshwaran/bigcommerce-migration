using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for entity discovery strategies
/// Follows Strategy Pattern - different implementations for different API versions and entity types
/// Supports Single Responsibility Principle by separating discovery logic into focused strategies
/// </summary>
public interface IEntityDiscoveryStrategy
{
    /// <summary>
    /// Gets the BigCommerce API version this strategy supports
    /// </summary>
    BigCommerceApiVersion SupportedApiVersion { get; }

    /// <summary>
    /// Discovers entities using the specific strategy implementation
    /// </summary>
    /// <param name="request">Entity discovery request with source store and entity configuration</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Discovery result with entities, metadata, and strategy-specific optimizations</returns>
    Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default);
} 