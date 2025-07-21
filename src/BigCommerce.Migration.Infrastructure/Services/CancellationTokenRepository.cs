using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

#pragma warning disable CS1998 // Async method lacks 'await' operators (in-memory storage)

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Repository implementation for cancellation token operations
/// Follows Interface Segregation Principle - handles only cancellation token management
/// Uses in-memory storage for prototype/development (can be replaced with persistent storage)
/// </summary>
public class CancellationTokenRepository : ICancellationTokenRepository
{
    private readonly ILogger<CancellationTokenRepository> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenEntry> _tokens;

    /// <summary>
    /// Initializes a new instance of the CancellationTokenRepository
    /// </summary>
    /// <param name="logger">Logger instance for tracking operations</param>
    public CancellationTokenRepository(ILogger<CancellationTokenRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tokens = new ConcurrentDictionary<string, CancellationTokenEntry>();
    }

    /// <summary>
    /// Creates a new cancellation token for a migration to initiate graceful shutdown
    /// </summary>
    public async Task<CancellationTokenEntry> CreateAsync(string migrationId, string reason)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("MigrationId cannot be null or empty", nameof(migrationId));

        if (string.IsNullOrEmpty(reason))
            throw new ArgumentException("Reason cannot be null or empty", nameof(reason));

        if (reason.Length > 500)
            throw new ArgumentException("Reason cannot exceed 500 characters", nameof(reason));

        // Check if cancellation token already exists
        if (_tokens.ContainsKey(migrationId))
        {
            throw new InvalidOperationException($"Cancellation token already exists for migration {migrationId}");
        }

        var token = new CancellationTokenEntry
        {
            MigrationId = migrationId,
            Reason = reason,
            RequestedBy = "System", // Could be enhanced to accept user context
            RequestedAt = DateTime.UtcNow,
            IsProcessed = false,
            Status = "Active"
        };

        // Store in-memory (replace with persistent storage)
        if (!_tokens.TryAdd(migrationId, token))
        {
            throw new InvalidOperationException($"Failed to create cancellation token for migration {migrationId}");
        }

        _logger.LogInformation("Created cancellation token for migration {MigrationId}: {Reason}", 
            migrationId, reason);

        return token;
    }

    /// <summary>
    /// Retrieves an existing cancellation token for a specific migration
    /// </summary>
    public async Task<CancellationTokenEntry?> GetAsync(string migrationId)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("MigrationId cannot be null or empty", nameof(migrationId));

        _tokens.TryGetValue(migrationId, out var token);

        if (token != null)
        {
            _logger.LogDebug("Retrieved cancellation token for migration {MigrationId}", migrationId);
        }
        else
        {
            _logger.LogDebug("Cancellation token not found for migration {MigrationId}", migrationId);
        }

        return token;
    }

    /// <summary>
    /// Updates an existing cancellation token with status changes or additional metadata
    /// </summary>
    public async Task<CancellationTokenEntry> UpdateAsync(CancellationTokenEntry token)
    {
        if (token == null)
            throw new ArgumentNullException(nameof(token));

        if (string.IsNullOrEmpty(token.MigrationId))
            throw new ArgumentException("MigrationId is required", nameof(token));

        if (!_tokens.ContainsKey(token.MigrationId))
        {
            throw new InvalidOperationException($"Cancellation token for migration {token.MigrationId} does not exist");
        }

        // Mark as processed if status indicates completion
        if (token.Status.Equals("Processed", StringComparison.OrdinalIgnoreCase) ||
            token.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
        {
            token.IsProcessed = true;
            token.ProcessedAt = DateTime.UtcNow;
        }

        // Update in-memory storage (replace with persistent storage)
        _tokens[token.MigrationId] = token;

        _logger.LogInformation("Updated cancellation token for migration {MigrationId}: status={Status}, processed={IsProcessed}", 
            token.MigrationId, token.Status, token.IsProcessed);

        return token;
    }

    /// <summary>
    /// Permanently deletes a cancellation token when migration is completed or reset
    /// </summary>
    public async Task<bool> DeleteAsync(string migrationId)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("MigrationId cannot be null or empty", nameof(migrationId));

        var removed = _tokens.TryRemove(migrationId, out var deletedToken);

        if (removed && deletedToken != null)
        {
            _logger.LogInformation("Deleted cancellation token for migration {MigrationId}", migrationId);
        }
        else
        {
            _logger.LogDebug("Cancellation token not found for deletion: migration {MigrationId}", migrationId);
        }

        return removed;
    }
} 