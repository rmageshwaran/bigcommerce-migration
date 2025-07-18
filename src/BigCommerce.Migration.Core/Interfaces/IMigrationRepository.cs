using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Repository interface for migration configuration operations following Interface Segregation Principle (ISP)
/// 
/// <para><strong>Responsibility:</strong> Handles only migration CRUD operations</para>
/// <para><strong>Segregation:</strong> Extracted from IMigrationStorageService to achieve single responsibility</para>
/// <para><strong>Purpose:</strong> Manages migration configuration lifecycle and queries</para>
/// 
/// <example>
/// Usage:
/// <code>
/// var migration = await repository.CreateAsync(new MigrationEntry 
/// { 
///     SourceStoreId = "source123", 
///     TargetStoreId = "target456" 
/// });
/// </code>
/// </example>
/// </summary>
public interface IMigrationRepository
{
    /// <summary>
    /// Creates a new migration configuration entry with validation and persistence
    /// </summary>
    /// <param name="entry">Migration entry to create. Must contain valid SourceStoreId and TargetStoreId</param>
    /// <returns>Created migration entry with generated ID and timestamps</returns>
    /// <exception cref="ArgumentNullException">Thrown when entry is null</exception>
    /// <exception cref="ArgumentException">Thrown when entry has invalid store IDs</exception>
    /// <exception cref="InvalidOperationException">Thrown when migration already exists</exception>
    Task<MigrationEntry> CreateAsync(MigrationEntry entry);

    /// <summary>
    /// Retrieves a migration configuration by its unique identifier
    /// </summary>
    /// <param name="migrationId">Unique migration identifier (GUID format)</param>
    /// <returns>Migration entry if found, null if not found</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task<MigrationEntry?> GetAsync(string migrationId);

    /// <summary>
    /// Updates an existing migration configuration with optimistic concurrency control
    /// </summary>
    /// <param name="entry">Migration entry to update. Must include valid ID and ETag</param>
    /// <returns>Updated migration entry with new ETag and LastModified timestamp</returns>
    /// <exception cref="ArgumentNullException">Thrown when entry is null</exception>
    /// <exception cref="ArgumentException">Thrown when entry has invalid ID</exception>
    /// <exception cref="InvalidOperationException">Thrown when migration doesn't exist or ETag conflict</exception>
    Task<MigrationEntry> UpdateAsync(MigrationEntry entry);

    /// <summary>
    /// Retrieves a paginated list of migrations with optional filtering and sorting
    /// </summary>
    /// <param name="request">Query request with filters (status, date range), sorting, and pagination</param>
    /// <returns>Paginated result containing migrations and continuation token</returns>
    /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="ArgumentException">Thrown when request has invalid pagination parameters</exception>
    Task<MigrationListResult> GetMigrationsAsync(MigrationQueryRequest request);

    /// <summary>
    /// Permanently deletes a migration configuration and associated metadata
    /// </summary>
    /// <param name="migrationId">Unique migration identifier to delete</param>
    /// <returns>True if migration was deleted, false if migration was not found</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    /// <exception cref="InvalidOperationException">Thrown when migration is in active state</exception>
    Task<bool> DeleteAsync(string migrationId);
} 