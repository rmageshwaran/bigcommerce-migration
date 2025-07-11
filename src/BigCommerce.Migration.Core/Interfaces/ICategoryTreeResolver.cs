using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for resolving category tree IDs for BigCommerce multi-storefront migrations
/// Now uses request-based credentials from MigrationRequest
/// </summary>
public interface ICategoryTreeResolver
{
    /// <summary>
    /// Resolves category tree IDs for source and destination channels from a migration request
    /// </summary>
    /// <param name="migrationRequest">Migration request containing source and destination store configurations</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>CategoryTreeContext with resolved tree IDs, or null if resolution failed</returns>
    Task<CategoryTreeContext?> ResolveCategoryTreeAsync(MigrationRequest migrationRequest, CancellationToken cancellationToken = default);
} 