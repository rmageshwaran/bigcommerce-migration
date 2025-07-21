using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Repository interface for entity ID mapping operations following Interface Segregation Principle (ISP)
/// 
/// <para><strong>Responsibility:</strong> Handles only entity ID mapping operations</para>
/// <para><strong>Segregation:</strong> Extracted from IMigrationStorageService to achieve single responsibility</para>
/// <para><strong>Purpose:</strong> Manages source-to-destination entity ID mappings for BigCommerce migrations</para>
/// 
/// <example>
/// Usage:
/// <code>
/// var mapping = await repository.CreateAsync(new EntityMapping 
/// { 
///     MigrationId = "migration-123",
///     EntityType = "product",
///     SourceId = "source-456",
///     DestinationId = "dest-789"
/// });
/// </code>
/// </example>
/// </summary>
public interface IEntityMappingRepository
{
    /// <summary>
    /// Creates a new entity ID mapping with validation and duplicate prevention
    /// </summary>
    /// <param name="mapping">Entity mapping to create. Must contain valid MigrationId, EntityType, and SourceId</param>
    /// <returns>Created mapping with generated ID and timestamps</returns>
    /// <exception cref="ArgumentNullException">Thrown when mapping is null</exception>
    /// <exception cref="ArgumentException">Thrown when mapping has invalid or missing required fields</exception>
    /// <exception cref="InvalidOperationException">Thrown when mapping already exists for the source entity</exception>
    Task<EntityMapping> CreateAsync(EntityMapping mapping);

    /// <summary>
    /// Retrieves an entity mapping by source entity identifier within a specific migration context
    /// </summary>
    /// <param name="migrationId">Unique migration identifier (GUID format)</param>
    /// <param name="entityType">Entity type identifier (e.g., "product", "category", "brand", "customer")</param>
    /// <param name="sourceId">Source entity identifier from the origin store</param>
    /// <returns>Entity mapping if found, null if no mapping exists</returns>
    /// <exception cref="ArgumentException">Thrown when any parameter is null, empty, or invalid format</exception>
    Task<EntityMapping?> GetAsync(string migrationId, string entityType, string sourceId);

    /// <summary>
    /// Retrieves all entity mappings for a migration with optional entity type filtering
    /// </summary>
    /// <param name="migrationId">Unique migration identifier to query mappings for</param>
    /// <param name="entityType">Optional entity type filter. If null, returns all entity types</param>
    /// <returns>List of entity mappings. Empty list if no mappings found</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task<List<EntityMapping>> GetAllAsync(string migrationId, string? entityType = null);

    /// <summary>
    /// Creates multiple entity mappings in a single atomic operation for performance optimization
    /// </summary>
    /// <param name="mappings">List of entity mappings to create. Maximum 1000 mappings per batch</param>
    /// <returns>List of created mappings with generated IDs and timestamps</returns>
    /// <exception cref="ArgumentNullException">Thrown when mappings is null</exception>
    /// <exception cref="ArgumentException">Thrown when mappings list is empty or exceeds maximum batch size</exception>
    /// <exception cref="InvalidOperationException">Thrown when any mapping in the batch is invalid or duplicated</exception>
    Task<List<EntityMapping>> CreateBatchAsync(List<EntityMapping> mappings);

    /// <summary>
    /// Updates an existing entity mapping with optimistic concurrency control
    /// </summary>
    /// <param name="mapping">Entity mapping to update. Must include valid ID and ETag</param>
    /// <returns>Updated mapping with new ETag and LastModified timestamp</returns>
    /// <exception cref="ArgumentNullException">Thrown when mapping is null</exception>
    /// <exception cref="ArgumentException">Thrown when mapping has invalid ID or required fields</exception>
    /// <exception cref="InvalidOperationException">Thrown when mapping doesn't exist or ETag conflict</exception>
    Task<EntityMapping> UpdateAsync(EntityMapping mapping);
} 