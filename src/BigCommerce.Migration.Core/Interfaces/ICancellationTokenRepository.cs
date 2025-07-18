using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Repository interface for cancellation token operations following Interface Segregation Principle (ISP)
/// 
/// <para><strong>Responsibility:</strong> Handles only cancellation token management</para>
/// <para><strong>Segregation:</strong> Extracted from IMigrationStorageService to achieve single responsibility</para>
/// <para><strong>Purpose:</strong> Manages migration cancellation tokens for graceful shutdown coordination</para>
/// 
/// <example>
/// Usage:
/// <code>
/// var token = await repository.CreateAsync("migration-123", "User requested cancellation");
/// var existing = await repository.GetAsync("migration-123");
/// if (existing?.IsCancelled == true) {
///     // Handle cancellation logic
/// }
/// </code>
/// </example>
/// </summary>
public interface ICancellationTokenRepository
{
    /// <summary>
    /// Creates a new cancellation token for a migration to initiate graceful shutdown
    /// </summary>
    /// <param name="migrationId">Unique migration identifier (GUID format)</param>
    /// <param name="reason">Human-readable cancellation reason for audit purposes</param>
    /// <returns>Created cancellation token with unique ID and timestamps</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    /// <exception cref="ArgumentException">Thrown when reason is null, empty, or exceeds maximum length</exception>
    /// <exception cref="InvalidOperationException">Thrown when cancellation token already exists for the migration</exception>
    Task<CancellationTokenEntry> CreateAsync(string migrationId, string reason);

    /// <summary>
    /// Retrieves an existing cancellation token for a specific migration
    /// </summary>
    /// <param name="migrationId">Unique migration identifier to query cancellation token for</param>
    /// <returns>Cancellation token if found, null if no cancellation token exists</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task<CancellationTokenEntry?> GetAsync(string migrationId);

    /// <summary>
    /// Updates an existing cancellation token with status changes or additional metadata
    /// </summary>
    /// <param name="token">Cancellation token to update. Must include valid ID and ETag</param>
    /// <returns>Updated cancellation token with new ETag and LastModified timestamp</returns>
    /// <exception cref="ArgumentNullException">Thrown when token is null</exception>
    /// <exception cref="ArgumentException">Thrown when token has invalid ID or required fields</exception>
    /// <exception cref="InvalidOperationException">Thrown when token doesn't exist or ETag conflict</exception>
    Task<CancellationTokenEntry> UpdateAsync(CancellationTokenEntry token);

    /// <summary>
    /// Permanently deletes a cancellation token when migration is completed or reset
    /// </summary>
    /// <param name="migrationId">Unique migration identifier to delete cancellation token for</param>
    /// <returns>True if cancellation token was deleted, false if token was not found</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task<bool> DeleteAsync(string migrationId);
} 