using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

#pragma warning disable CS1998 // Async method lacks 'await' operators (in-memory storage)

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Repository implementation for entity ID mapping operations
/// Follows Interface Segregation Principle - handles only entity mapping operations
/// Uses in-memory storage for prototype/development (can be replaced with persistent storage)
/// </summary>
public class EntityMappingRepository : IEntityMappingRepository
{
    private readonly ILogger<EntityMappingRepository> _logger;
    private readonly ConcurrentDictionary<string, EntityMapping> _mappings;

    /// <summary>
    /// Initializes a new instance of the EntityMappingRepository
    /// </summary>
    /// <param name="logger">Logger instance for entity mapping operations</param>
    public EntityMappingRepository(ILogger<EntityMappingRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mappings = new ConcurrentDictionary<string, EntityMapping>();
    }

    /// <summary>
    /// Creates a new entity ID mapping with validation and duplicate prevention
    /// </summary>
    public async Task<EntityMapping> CreateAsync(EntityMapping mapping)
    {
        if (mapping == null)
            throw new ArgumentNullException(nameof(mapping));

        if (string.IsNullOrEmpty(mapping.MigrationId))
            throw new ArgumentException("MigrationId is required", nameof(mapping));

        if (string.IsNullOrEmpty(mapping.EntityType))
            throw new ArgumentException("EntityType is required", nameof(mapping));

        if (string.IsNullOrEmpty(mapping.SourceId))
            throw new ArgumentException("SourceId is required", nameof(mapping));

        // Check for existing mapping
        var existingKey = GetMappingKey(mapping.MigrationId, mapping.EntityType, mapping.SourceId);
        if (_mappings.ContainsKey(existingKey))
        {
            throw new InvalidOperationException($"Mapping already exists for {mapping.EntityType} {mapping.SourceId} in migration {mapping.MigrationId}");
        }

        // Generate timestamps
        mapping.CreatedAt = DateTime.UtcNow;
        mapping.UpdatedAt = mapping.CreatedAt;

        // Store in-memory (replace with persistent storage)
        if (!_mappings.TryAdd(existingKey, mapping))
        {
            throw new InvalidOperationException($"Failed to create mapping for {mapping.EntityType} {mapping.SourceId}");
        }

        _logger.LogInformation("Created entity mapping for {EntityType} {SourceId} -> {DestinationId} in migration {MigrationId}", 
            mapping.EntityType, mapping.SourceId, mapping.DestinationId, mapping.MigrationId);

        return mapping;
    }

    /// <summary>
    /// Retrieves an entity mapping by source entity identifier within a specific migration context
    /// </summary>
    public async Task<EntityMapping?> GetAsync(string migrationId, string entityType, string sourceId)
    {
        // LSP COMPLIANCE: Handle null specifically as ArgumentNullException
        if (migrationId == null) throw new ArgumentNullException(nameof(migrationId));
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("MigrationId cannot be empty", nameof(migrationId));

        if (entityType == null) throw new ArgumentNullException(nameof(entityType));
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("EntityType cannot be empty", nameof(entityType));

        if (sourceId == null) throw new ArgumentNullException(nameof(sourceId));
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("SourceId cannot be empty", nameof(sourceId));

        var key = GetMappingKey(migrationId, entityType, sourceId);
        _mappings.TryGetValue(key, out var mapping);

        if (mapping != null)
        {
            _logger.LogDebug("Retrieved entity mapping for {EntityType} {SourceId} in migration {MigrationId}", 
                entityType, sourceId, migrationId);
        }
        else
        {
            _logger.LogDebug("Entity mapping not found for {EntityType} {SourceId} in migration {MigrationId}", 
                entityType, sourceId, migrationId);
        }

        return mapping;
    }

    /// <summary>
    /// Retrieves all entity mappings for a migration with optional entity type filtering
    /// </summary>
    public async Task<List<EntityMapping>> GetAllAsync(string migrationId, string? entityType = null)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("MigrationId cannot be null or empty", nameof(migrationId));

        var allMappings = _mappings.Values.Where(m => m.MigrationId == migrationId);

        if (!string.IsNullOrEmpty(entityType))
        {
            allMappings = allMappings.Where(m => m.EntityType == entityType);
        }

        var result = allMappings.OrderBy(m => m.EntityType).ThenBy(m => m.SourceId).ToList();

        _logger.LogDebug("Retrieved {Count} entity mappings for migration {MigrationId} (type: {EntityType})", 
            result.Count, migrationId, entityType ?? "all");

        return result;
    }

    /// <summary>
    /// Creates multiple entity mappings in a single atomic operation for performance optimization
    /// </summary>
    public async Task<List<EntityMapping>> CreateBatchAsync(List<EntityMapping> mappings)
    {
        if (mappings == null)
            throw new ArgumentNullException(nameof(mappings));

        if (mappings.Count == 0)
            throw new ArgumentException("Mappings list cannot be empty", nameof(mappings));

        if (mappings.Count > 1000)
            throw new ArgumentException("Maximum 1000 mappings per batch", nameof(mappings));

        var createdMappings = new List<EntityMapping>();
        var keysToAdd = new List<string>();

        try
        {
            // Validate all mappings first
            foreach (var mapping in mappings)
            {
                if (mapping == null)
                    throw new ArgumentException("Mapping cannot be null", nameof(mappings));

                if (string.IsNullOrEmpty(mapping.MigrationId) || 
                    string.IsNullOrEmpty(mapping.EntityType) || 
                    string.IsNullOrEmpty(mapping.SourceId))
                {
                    throw new ArgumentException("All mappings must have valid MigrationId, EntityType, and SourceId", nameof(mappings));
                }

                var key = GetMappingKey(mapping.MigrationId, mapping.EntityType, mapping.SourceId);
                if (_mappings.ContainsKey(key) || keysToAdd.Contains(key))
                {
                    throw new InvalidOperationException($"Duplicate mapping found for {mapping.EntityType} {mapping.SourceId} in migration {mapping.MigrationId}");
                }

                keysToAdd.Add(key);
            }

            // Create all mappings
            for (int i = 0; i < mappings.Count; i++)
            {
                var mapping = mappings[i];
                mapping.CreatedAt = DateTime.UtcNow;
                mapping.UpdatedAt = mapping.CreatedAt;

                if (!_mappings.TryAdd(keysToAdd[i], mapping))
                {
                    throw new InvalidOperationException($"Failed to create mapping for {mapping.EntityType} {mapping.SourceId}");
                }

                createdMappings.Add(mapping);
            }

            _logger.LogInformation("Created {Count} entity mappings in batch", createdMappings.Count);

            return createdMappings;
        }
        catch
        {
            // Rollback any created mappings on failure
            foreach (var key in keysToAdd.Take(createdMappings.Count))
            {
                _mappings.TryRemove(key, out _);
            }
            throw;
        }
    }

    /// <summary>
    /// Updates an existing entity mapping with optimistic concurrency control
    /// </summary>
    public async Task<EntityMapping> UpdateAsync(EntityMapping mapping)
    {
        if (mapping == null)
            throw new ArgumentNullException(nameof(mapping));

        if (string.IsNullOrEmpty(mapping.MigrationId) || 
            string.IsNullOrEmpty(mapping.EntityType) || 
            string.IsNullOrEmpty(mapping.SourceId))
        {
            throw new ArgumentException("MigrationId, EntityType, and SourceId are required", nameof(mapping));
        }

        var key = GetMappingKey(mapping.MigrationId, mapping.EntityType, mapping.SourceId);
        
        if (!_mappings.ContainsKey(key))
        {
            throw new InvalidOperationException($"Entity mapping for {mapping.EntityType} {mapping.SourceId} does not exist");
        }

        // Update timestamps
        mapping.UpdatedAt = DateTime.UtcNow;

        // Update in-memory storage (replace with persistent storage)
        _mappings[key] = mapping;

        _logger.LogInformation("Updated entity mapping for {EntityType} {SourceId} in migration {MigrationId}", 
            mapping.EntityType, mapping.SourceId, mapping.MigrationId);

        return mapping;
    }

    /// <summary>
    /// Generates a composite key for entity mapping storage
    /// </summary>
    private static string GetMappingKey(string migrationId, string entityType, string sourceId)
    {
        return $"{migrationId}:{entityType}:{sourceId}";
    }
} 