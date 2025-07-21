using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

#pragma warning disable CS1998 // Async method lacks 'await' operators (in-memory storage)

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Repository implementation for migration configuration operations
/// Follows Interface Segregation Principle - handles only migration CRUD operations
/// Uses in-memory storage for prototype/development (can be replaced with persistent storage)
/// </summary>
public class MigrationRepository : IMigrationRepository
{
    private readonly ILogger<MigrationRepository> _logger;
    private readonly ConcurrentDictionary<string, MigrationEntry> _migrations;

    /// <summary>
    /// Initializes a new instance of the MigrationRepository
    /// </summary>
    /// <param name="logger">Logger instance for migration operations</param>
    public MigrationRepository(ILogger<MigrationRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _migrations = new ConcurrentDictionary<string, MigrationEntry>();
    }

    /// <summary>
    /// Creates a new migration configuration entry with validation and persistence
    /// </summary>
    public Task<MigrationEntry> CreateAsync(MigrationEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (string.IsNullOrEmpty(entry.SourceStoreId))
            throw new ArgumentException("SourceStoreId is required", nameof(entry));

        if (string.IsNullOrEmpty(entry.DestinationStoreId))
            throw new ArgumentException("DestinationStoreId is required", nameof(entry));

        // Generate ID and timestamps
        entry.Id = Guid.NewGuid().ToString();
        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = entry.CreatedAt;

        // Store in-memory (replace with persistent storage)
        if (!_migrations.TryAdd(entry.Id, entry))
        {
            throw new InvalidOperationException($"Migration with ID {entry.Id} already exists");
        }

        _logger.LogInformation("Created migration {MigrationId} from {SourceStore} to {DestinationStore}", 
            entry.Id, entry.SourceStoreId, entry.DestinationStoreId);

        return Task.FromResult(entry);
    }

    /// <summary>
    /// Retrieves a migration configuration by its unique identifier
    /// </summary>
    public async Task<MigrationEntry?> GetAsync(string migrationId)
    {
        // LSP COMPLIANCE: Handle null specifically as ArgumentNullException
        if (migrationId == null) throw new ArgumentNullException(nameof(migrationId));
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("MigrationId cannot be empty", nameof(migrationId));

        _migrations.TryGetValue(migrationId, out var migration);
        
        if (migration != null)
        {
            _logger.LogDebug("Retrieved migration {MigrationId}", migrationId);
        }
        else
        {
            _logger.LogDebug("Migration {MigrationId} not found", migrationId);
        }

        return migration;
    }

    /// <summary>
    /// Updates an existing migration configuration with optimistic concurrency control
    /// </summary>
    public async Task<MigrationEntry> UpdateAsync(MigrationEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (string.IsNullOrEmpty(entry.Id))
            throw new ArgumentException("Entry ID is required for updates", nameof(entry));

        if (!_migrations.ContainsKey(entry.Id))
            throw new InvalidOperationException($"Migration {entry.Id} does not exist");

        // Update timestamps
        entry.UpdatedAt = DateTime.UtcNow;

        // Update in-memory storage (replace with persistent storage)
        _migrations[entry.Id] = entry;

        _logger.LogInformation("Updated migration {MigrationId}", entry.Id);

        return entry;
    }

    /// <summary>
    /// Retrieves a paginated list of migrations with optional filtering and sorting
    /// </summary>
    public async Task<MigrationListResult> GetMigrationsAsync(MigrationQueryRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var allMigrations = _migrations.Values.ToList();

        // Apply filtering (basic implementation for prototype)
        var filteredMigrations = allMigrations.AsEnumerable();

        if (!string.IsNullOrEmpty(request.Status))
        {
            if (Enum.TryParse<MigrationStatus>(request.Status, true, out var statusEnum))
            {
                filteredMigrations = filteredMigrations.Where(m => m.Status == statusEnum);
            }
        }

        if (!string.IsNullOrEmpty(request.MigrationId))
        {
            filteredMigrations = filteredMigrations.Where(m => m.Id.Equals(request.MigrationId, StringComparison.OrdinalIgnoreCase));
        }

        // Apply sorting
        filteredMigrations = filteredMigrations.OrderByDescending(m => m.CreatedAt);

        // Apply pagination
        var skip = (request.Page - 1) * request.PageSize;
        var paginatedMigrations = filteredMigrations.Skip(skip).Take(request.PageSize).ToList();

        var result = new MigrationListResult
        {
            Migrations = paginatedMigrations,
            TotalCount = allMigrations.Count,
            CurrentPage = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling((double)allMigrations.Count / request.PageSize),
            HasMorePages = allMigrations.Count > skip + paginatedMigrations.Count
        };

        _logger.LogDebug("Retrieved {Count} migrations (page {Page})", paginatedMigrations.Count, request.Page);

        return result;
    }

    /// <summary>
    /// Permanently deletes a migration configuration and associated metadata
    /// </summary>
    public async Task<bool> DeleteAsync(string migrationId)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("MigrationId cannot be null or empty", nameof(migrationId));

        var removed = _migrations.TryRemove(migrationId, out var deletedMigration);

        if (removed && deletedMigration != null)
        {
            _logger.LogInformation("Deleted migration {MigrationId}", migrationId);
        }
        else
        {
            _logger.LogDebug("Migration {MigrationId} not found for deletion", migrationId);
        }

        return removed;
    }
} 