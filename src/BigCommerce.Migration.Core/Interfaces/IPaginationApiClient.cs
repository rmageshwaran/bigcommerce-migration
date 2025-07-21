using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for BigCommerce Pagination API operations
/// Follows Interface Segregation Principle - only pagination-related methods
/// </summary>
public interface IPaginationApiClient
{
    /// <summary>
    /// Gets a paginated response for any entity type with automatic API version handling
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="entityType">Type of entity to fetch</param>
    /// <param name="paginationRequest">Pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated response with data and meta information</returns>
    Task<BigCommercePaginatedResponse<Dictionary<string, object>>> GetPaginatedEntitiesAsync(
        StoreConfiguration storeConfig, 
        string entityType, 
        BigCommercePaginationRequest paginationRequest, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all entities of a specific type using pagination (streaming approach)
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="entityType">Type of entity to fetch</param>
    /// <param name="paginationRequest">Base pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of entity pages</returns>
    IAsyncEnumerable<BigCommercePaginatedResponse<Dictionary<string, object>>> GetAllEntitiesPaginatedAsync(
        StoreConfiguration storeConfig,
        string entityType,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken);
} 