using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Implementation of IEntityMappingService for creating and managing entity mappings
/// </summary>
public class EntityMappingService : IEntityMappingService
{
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly ILogger<EntityMappingService> _logger;

    public EntityMappingService(IMigrationStorageService migrationStorageService, ILogger<EntityMappingService> logger)
    {
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public EntityMapping CreateEntityMapping(
        Dictionary<string, object> sourceEntity, 
        Dictionary<string, object> destinationEntity, 
        BatchProcessingRequest request)
    {
        if (sourceEntity == null)
            throw new ArgumentNullException(nameof(sourceEntity));
        
        if (destinationEntity == null)
            throw new ArgumentNullException(nameof(destinationEntity));
        
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var sourceId = ExtractId(sourceEntity, request.EntityType);
            var destinationId = ExtractId(destinationEntity, request.EntityType);

            var mapping = new EntityMapping
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                SourceId = sourceId,
                DestinationId = destinationId,
                SourceStoreId = request.SourceStore.StoreId!,
                DestinationStoreId = request.DestinationStore.StoreId!,
                Status = "completed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _logger.LogDebug("Created entity mapping for {EntityType} {SourceId} -> {DestinationId} in migration {MigrationId}", 
                request.EntityType, sourceId, destinationId, request.MigrationId);

            return mapping;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create entity mapping for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            throw new InvalidOperationException($"Failed to create entity mapping for {request.EntityType}", ex);
        }
    }

    public async Task StoreEntityMappingsAsync(
        List<EntityMapping> mappings, 
        CancellationToken cancellationToken)
    {
        if (mappings == null || !mappings.Any())
        {
            _logger.LogWarning("No entity mappings provided for storage");
            return;
        }

        try
        {
            _logger.LogDebug("Storing {Count} entity mappings for migration {MigrationId}", 
                mappings.Count, mappings.First().MigrationId);
            
            await _migrationStorageService.StoreEntityMappingsAsync(mappings);
            
            _logger.LogInformation("Successfully stored {Count} entity mappings for migration {MigrationId}", 
                mappings.Count, mappings.First().MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store {Count} entity mappings for migration {MigrationId}", 
                mappings.Count, mappings.First().MigrationId);
            throw new InvalidOperationException("Failed to store entity mappings", ex);
        }
    }

    public async Task StoreEntityMappingAsync(
        EntityMapping mapping, 
        CancellationToken cancellationToken)
    {
        if (mapping == null)
            throw new ArgumentNullException(nameof(mapping));

        try
        {
            _logger.LogDebug("Storing entity mapping for {EntityType} {SourceId} -> {DestinationId} in migration {MigrationId}", 
                mapping.EntityType, mapping.SourceId, mapping.DestinationId, mapping.MigrationId);
            
            await _migrationStorageService.StoreEntityMappingsAsync(new List<EntityMapping> { mapping });
            
            _logger.LogDebug("Successfully stored entity mapping for {EntityType} {SourceId} -> {DestinationId} in migration {MigrationId}", 
                mapping.EntityType, mapping.SourceId, mapping.DestinationId, mapping.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store entity mapping for {EntityType} {SourceId} -> {DestinationId} in migration {MigrationId}", 
                mapping.EntityType, mapping.SourceId, mapping.DestinationId, mapping.MigrationId);
            throw new InvalidOperationException("Failed to store entity mapping", ex);
        }
    }

    public async Task<List<EntityMapping>> GetEntityMappingsAsync(
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));

        try
        {
            _logger.LogDebug("Getting entity mappings for migration {MigrationId}, entity type {EntityType}", 
                migrationId, entityType);
            
            // This would need to be implemented in the storage service
            // For now, return empty list as placeholder
            var mappings = new List<EntityMapping>();
            
            _logger.LogDebug("Retrieved {Count} entity mappings for migration {MigrationId}, entity type {EntityType}", 
                mappings.Count, migrationId, entityType);
            
            return mappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get entity mappings for migration {MigrationId}, entity type {EntityType}", 
                migrationId, entityType);
            throw new InvalidOperationException($"Failed to get entity mappings for {entityType}", ex);
        }
    }

    public async Task<string?> GetDestinationIdAsync(
        string migrationId, 
        string entityType, 
        string sourceId, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Source ID cannot be null or empty", nameof(sourceId));

        try
        {
            _logger.LogDebug("Getting destination ID for migration {MigrationId}, entity type {EntityType}, source ID {SourceId}", 
                migrationId, entityType, sourceId);
            
            // Retrieve the entity mapping from storage
            var mapping = await _migrationStorageService.GetEntityMappingAsync(migrationId, entityType, sourceId);
            
            if (mapping != null && !string.IsNullOrEmpty(mapping.DestinationId))
            {
                _logger.LogDebug("Found destination ID {DestinationId} for migration {MigrationId}, entity type {EntityType}, source ID {SourceId}", 
                    mapping.DestinationId, migrationId, entityType, sourceId);
                return mapping.DestinationId;
            }
            else
            {
                _logger.LogDebug("No destination ID found for migration {MigrationId}, entity type {EntityType}, source ID {SourceId}", 
                    migrationId, entityType, sourceId);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get destination ID for migration {MigrationId}, entity type {EntityType}, source ID {SourceId}", 
                migrationId, entityType, sourceId);
            throw new InvalidOperationException($"Failed to get destination ID for {entityType}", ex);
        }
    }

    public async Task<Dictionary<string, string>> GetSourceToDestinationMappingAsync(
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));

        try
        {
            _logger.LogDebug("Getting source to destination mapping for migration {MigrationId}, entity type {EntityType}", 
                migrationId, entityType);
            
            var mappings = await GetEntityMappingsAsync(migrationId, entityType, cancellationToken);
            var mappingDict = mappings.ToDictionary(m => m.SourceId, m => m.DestinationId);
            
            _logger.LogDebug("Retrieved {Count} source to destination mappings for migration {MigrationId}, entity type {EntityType}", 
                mappingDict.Count, migrationId, entityType);
            
            return mappingDict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get source to destination mapping for migration {MigrationId}, entity type {EntityType}", 
                migrationId, entityType);
            throw new InvalidOperationException($"Failed to get source to destination mapping for {entityType}", ex);
        }
    }

    public async Task<bool> MappingExistsAsync(
        string migrationId, 
        string entityType, 
        string sourceId, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Source ID cannot be null or empty", nameof(sourceId));

        try
        {
            var destinationId = await GetDestinationIdAsync(migrationId, entityType, sourceId, cancellationToken);
            var exists = !string.IsNullOrWhiteSpace(destinationId);
            
            _logger.LogDebug("Mapping exists check for migration {MigrationId}, entity type {EntityType}, source ID {SourceId}: {Exists}", 
                migrationId, entityType, sourceId, exists);
            
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if mapping exists for migration {MigrationId}, entity type {EntityType}, source ID {SourceId}", 
                migrationId, entityType, sourceId);
            throw new InvalidOperationException($"Failed to check if mapping exists for {entityType}", ex);
        }
    }

    private static string ExtractId(Dictionary<string, object> entity, string entityType)
    {
        if (entity == null || entity.Count == 0)
            throw new ArgumentException("Entity cannot be null or empty");

        // Define entity-specific ID field names
        var idFieldMappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["categories"] = new[] { "category_id", "id", "Id", "ID" },
            ["products"] = new[] { "product_id", "id", "Id", "ID" },
            ["brands"] = new[] { "brand_id", "id", "Id", "ID" },
            ["customers"] = new[] { "customer_id", "id", "Id", "ID" },
            ["orders"] = new[] { "order_id", "id", "Id", "ID" },
            ["order_products"] = new[] { "order_product_id", "id", "Id", "ID" },
            ["order_statuses"] = new[] { "order_status_id", "id", "Id", "ID" },
            ["shipping_zones"] = new[] { "shipping_zone_id", "id", "Id", "ID" },
            ["shipping_methods"] = new[] { "shipping_method_id", "id", "Id", "ID" },
            ["payment_methods"] = new[] { "payment_method_id", "id", "Id", "ID" },
            ["tax_classes"] = new[] { "tax_class_id", "id", "Id", "ID" },
            ["coupons"] = new[] { "coupon_id", "id", "Id", "ID" },
            ["redirects"] = new[] { "redirect_id", "id", "Id", "ID" },
            ["wishlist"] = new[] { "wishlist_id", "id", "Id", "ID" },
            ["gift_certificates"] = new[] { "gift_certificate_id", "id", "Id", "ID" },
            ["pages"] = new[] { "page_id", "id", "Id", "ID" },
            ["blog_posts"] = new[] { "blog_post_id", "id", "Id", "ID" },
            ["blog_tags"] = new[] { "blog_tag_id", "id", "Id", "ID" },
            ["banner"] = new[] { "banner_id", "id", "Id", "ID" },
            ["newsletter_subscribers"] = new[] { "subscriber_id", "id", "Id", "ID" }
        };

        var possibleIdFields = idFieldMappings.TryGetValue(entityType.ToLowerInvariant(), out var fields)
            ? fields 
            : new[] { "id", "Id", "ID" };

        foreach (var field in possibleIdFields)
        {
            if (entity.TryGetValue(field, out var value) && value != null)
            {
                var id = value.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }
        }

        throw new InvalidOperationException($"Entity is missing ID field for entity type '{entityType}'. Available keys: {string.Join(", ", entity.Keys)}");
    }
} 