using Azure.Data.Tables;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Factory interface for creating rate limiting Table Storage clients
/// Follows existing MigrationStorageService patterns for consistency
/// </summary>
public interface IRateLimitingTableStorageFactory
{
    /// <summary>
    /// Gets a table client for quota tracking with auto-creation
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Table client for quota operations</returns>
    Task<TableClient> GetQuotaTableClientAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a table client for instance coordination with auto-creation
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Table client for instance operations</returns>
    Task<TableClient> GetInstanceTableClientAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a table client for token allocation with auto-creation
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Table client for token operations</returns>
    Task<TableClient> GetTokenTableClientAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if all rate limiting tables exist
    /// Used for health checks and validation
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if all tables exist</returns>
    Task<bool> AllTablesExistAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates all rate limiting tables if they don't exist
    /// Called during initialization
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task EnsureTablesExistAsync(CancellationToken cancellationToken = default);
}