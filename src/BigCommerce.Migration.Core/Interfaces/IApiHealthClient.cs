using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for BigCommerce API Health and Version operations
/// Follows Interface Segregation Principle - only health and utility methods
/// </summary>
public interface IApiHealthClient
{
    /// <summary>
    /// Checks if the BigCommerce API is healthy for a specific store
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if API is healthy, false otherwise</returns>
    Task<bool> IsHealthyAsync(
        StoreConfiguration storeConfig, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the BigCommerce API version for a store
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>API version (V2 or V3)</returns>
    Task<BigCommerceApiVersion> DetectApiVersionAsync(
        StoreConfiguration storeConfig, 
        CancellationToken cancellationToken);
} 