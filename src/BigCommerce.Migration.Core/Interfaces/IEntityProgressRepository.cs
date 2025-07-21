using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Repository interface for entity progress operations following ISP
/// Separated from IMigrationStorageService for better adherence to SOLID principles
/// </summary>
public interface IEntityProgressRepository
{
    /// <summary>
    /// Creates or updates an entity progress entry with optimized upsert operation
    /// </summary>
    /// <param name="progressEntry">Entity progress entry to create or update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created or updated progress entry</returns>
    Task<EntityProgressEntry> UpsertEntityProgressAsync(EntityProgressEntry progressEntry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk upserts multiple entity progress entries for performance optimization
    /// </summary>
    /// <param name="progressEntries">List of entity progress entries to upsert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of upserted progress entries</returns>
    Task<List<EntityProgressEntry>> BulkUpsertEntityProgressAsync(List<EntityProgressEntry> progressEntries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets entity progress entries for a migration with caching support
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="useCache">Whether to use cached results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entity progress entries</returns>
    Task<List<EntityProgressEntry>> GetEntityProgressAsync(string migrationId, bool useCache = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific entity progress entry by type with caching
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="useCache">Whether to use cached results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity progress entry or null if not found</returns>
    Task<EntityProgressEntry?> GetEntityProgressByTypeAsync(string migrationId, string entityType, bool useCache = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes entity progress entries for a migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Optional entity type filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of deleted entries</returns>
    Task<int> DeleteEntityProgressAsync(string migrationId, string? entityType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears cache for entity progress data
    /// </summary>
    /// <param name="migrationId">Optional migration ID filter</param>
    Task ClearCacheAsync(string? migrationId = null);
} 