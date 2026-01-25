using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Service responsible for creating and managing entity mappings
/// Single Responsibility: Entity mapping management only
/// </summary>
public interface IEntityMappingService
{
    /// <summary>
    /// Creates a mapping between source and destination entities
    /// </summary>
    /// <param name="sourceEntity">Source entity data</param>
    /// <param name="destinationEntity">Destination entity data</param>
    /// <param name="request">Batch processing request</param>
    /// <returns>Entity mapping record</returns>
    EntityMapping CreateEntityMapping(
        Dictionary<string, object> sourceEntity, 
        Dictionary<string, object> destinationEntity, 
        BatchProcessingRequest request);

    /// <summary>
    /// Stores multiple entity mappings
    /// </summary>
    /// <param name="mappings">List of entity mappings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StoreEntityMappingsAsync(
        List<EntityMapping> mappings, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores a single entity mapping
    /// </summary>
    /// <param name="mapping">Entity mapping</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StoreEntityMappingAsync(
        EntityMapping mapping, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves entity mappings for a migration and entity type
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entity mappings</returns>
    Task<List<EntityMapping>> GetEntityMappingsAsync(
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the destination ID for a source entity
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="sourceId">Source entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Destination entity ID or null if not found</returns>
    Task<string?> GetDestinationIdAsync(
        string migrationId, 
        string entityType, 
        string sourceId, 
        CancellationToken cancellationToken);
} 