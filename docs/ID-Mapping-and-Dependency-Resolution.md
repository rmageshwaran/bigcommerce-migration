# BigCommerce Migration System - ID Mapping and Dependency Resolution

## Overview

This document provides detailed implementation specifications for entity ID mapping and dependency resolution in the BigCommerce migration system. It covers how source entity IDs are mapped to destination entity IDs and how entity dependencies are resolved during migration processing.

## Table of Contents
1. [Entity ID Mapping Implementation](#entity-id-mapping-implementation)
2. [Dependency Resolution System](#dependency-resolution-system)
3. [Dependencies First Approach](#dependencies-first-approach)
4. [Implementation Examples](#implementation-examples)
5. [Error Handling and Recovery](#error-handling-and-recovery)
6. [Performance Optimization](#performance-optimization)

## Entity ID Mapping Implementation

### EntityMappings Table Structure

The `EntityMappings` table in Azure Table Storage stores the bidirectional mapping between source and destination entity IDs:

```csharp
public class EntityMappingEntity : TableEntity
{
    // PartitionKey: "{migrationId}:{entityType}" (e.g., "migration-123:Products")
    // RowKey: "{sourceEntityId}" (e.g., "source-product-456")
    
    public string MigrationId { get; set; }
    public string EntityType { get; set; }
    public string SourceEntityId { get; set; }
    public string DestinationEntityId { get; set; }
    public string SourceEntityName { get; set; }
    public string DestinationEntityName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Status { get; set; } // "Success", "Failed", "Pending"
    public string ParentEntityType { get; set; }
    public string ParentSourceId { get; set; }
    public string ParentDestinationId { get; set; }
    public string EntityMetadata { get; set; } // JSON serialized metadata
}
```

### ID Mapping Service Implementation

```csharp
public class EntityMappingService
{
    private readonly ITableStorageService _tableStorage;
    private readonly ILogger<EntityMappingService> _logger;
    
    public async Task<string> GetDestinationId(string migrationId, string entityType, string sourceId)
    {
        var partitionKey = $"{migrationId}:{entityType}";
        var mapping = await _tableStorage.GetAsync<EntityMappingEntity>(partitionKey, sourceId);
        
        return mapping?.DestinationEntityId;
    }
    
    public async Task<Dictionary<string, string>> GetBatchMappings(string migrationId, 
        string entityType, IEnumerable<string> sourceIds)
    {
        var partitionKey = $"{migrationId}:{entityType}";
        var mappings = await _tableStorage.GetBatchAsync<EntityMappingEntity>(
            partitionKey, sourceIds);
        
        return mappings.ToDictionary(m => m.SourceEntityId, m => m.DestinationEntityId);
    }
    
    public async Task StoreMapping(string migrationId, string entityType, 
        string sourceId, string destinationId, string entityName, 
        string parentEntityType = null, string parentSourceId = null)
    {
        var mapping = new EntityMappingEntity
        {
            PartitionKey = $"{migrationId}:{entityType}",
            RowKey = sourceId,
            MigrationId = migrationId,
            EntityType = entityType,
            SourceEntityId = sourceId,
            DestinationEntityId = destinationId,
            SourceEntityName = entityName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Status = "Success",
            ParentEntityType = parentEntityType,
            ParentSourceId = parentSourceId
        };
        
        // Get parent destination ID if applicable
        if (!string.IsNullOrEmpty(parentEntityType) && !string.IsNullOrEmpty(parentSourceId))
        {
            mapping.ParentDestinationId = await GetDestinationId(migrationId, parentEntityType, parentSourceId);
        }
        
        await _tableStorage.InsertOrReplaceAsync("EntityMappings", mapping);
    }
    
    public async Task<bool> IsMappingExists(string migrationId, string entityType, string sourceId)
    {
        var partitionKey = $"{migrationId}:{entityType}";
        var mapping = await _tableStorage.GetAsync<EntityMappingEntity>(partitionKey, sourceId);
        return mapping != null && mapping.Status == "Success";
    }
}
```

## Dependency Resolution System

### DependencyGraph Table Structure

```csharp
public class DependencyGraphEntity : TableEntity
{
    // PartitionKey: "Dependencies"
    // RowKey: "{entityType}" (e.g., "Customers")
    
    public string EntityType { get; set; }
    public string[] DependsOn { get; set; } // ["CustomerGroups", "Stores"]
    public int Priority { get; set; }
    public string ProcessingPhase { get; set; }
    public bool AllowParallelProcessing { get; set; }
    public string ApiEndpoint { get; set; }
    public string[] ConflictsWith { get; set; } // Entities that cannot be processed in parallel
}
```

### Dependency Resolution Service

```csharp
public class DependencyResolver
{
    private readonly ITableStorageService _tableStorage;
    private readonly ILogger<DependencyResolver> _logger;
    
    public async Task<List<ProcessingPhase>> GetProcessingOrder(string[] requestedEntities)
    {
        var dependencyGraph = await LoadDependencyGraph();
        var phases = new List<ProcessingPhase>();
        var processedEntities = new HashSet<string>();
        var remainingEntities = requestedEntities.ToList();
        
        int phaseNumber = 1;
        
        while (remainingEntities.Any())
        {
            var readyEntities = remainingEntities.Where(entity => 
                CanProcess(entity, dependencyGraph, processedEntities)).ToList();
            
            if (!readyEntities.Any())
            {
                var cyclicDependencies = FindCyclicDependencies(remainingEntities, dependencyGraph);
                throw new InvalidOperationException(
                    $"Circular dependency detected: {string.Join(" -> ", cyclicDependencies)}");
            }
            
            // Group entities by parallel processing capability
            var parallelGroups = GroupEntitiesForParallelProcessing(readyEntities, dependencyGraph);
            
            phases.Add(new ProcessingPhase
            {
                PhaseNumber = phaseNumber++,
                ParallelGroups = parallelGroups,
                EstimatedDuration = CalculateEstimatedDuration(readyEntities)
            });
            
            processedEntities.UnionWith(readyEntities);
            remainingEntities.RemoveAll(e => readyEntities.Contains(e));
        }
        
        return phases;
    }
    
    private async Task<Dictionary<string, DependencyGraphEntity>> LoadDependencyGraph()
    {
        var entities = await _tableStorage.GetAllAsync<DependencyGraphEntity>("Dependencies");
        return entities.ToDictionary(e => e.EntityType, e => e);
    }
    
    private bool CanProcess(string entityType, Dictionary<string, DependencyGraphEntity> dependencies, 
        HashSet<string> processedEntities)
    {
        var entity = dependencies.GetValueOrDefault(entityType);
        if (entity?.DependsOn == null) return true;
        
        return entity.DependsOn.All(dep => processedEntities.Contains(dep));
    }
    
    private List<ParallelGroup> GroupEntitiesForParallelProcessing(
        List<string> entities, Dictionary<string, DependencyGraphEntity> dependencies)
    {
        var groups = new List<ParallelGroup>();
        var remainingEntities = entities.ToList();
        
        while (remainingEntities.Any())
        {
            var group = new ParallelGroup();
            var entityToProcess = remainingEntities.First();
            
            group.Entities.Add(entityToProcess);
            remainingEntities.Remove(entityToProcess);
            
            // Find entities that can be processed in parallel
            var parallelEntities = remainingEntities.Where(e => 
                CanProcessInParallel(entityToProcess, e, dependencies)).ToList();
            
            group.Entities.AddRange(parallelEntities);
            remainingEntities.RemoveAll(e => parallelEntities.Contains(e));
            
            groups.Add(group);
        }
        
        return groups;
    }
    
    private bool CanProcessInParallel(string entity1, string entity2, 
        Dictionary<string, DependencyGraphEntity> dependencies)
    {
        var config1 = dependencies.GetValueOrDefault(entity1);
        var config2 = dependencies.GetValueOrDefault(entity2);
        
        if (config1?.AllowParallelProcessing == false || config2?.AllowParallelProcessing == false)
            return false;
        
        if (config1?.ConflictsWith?.Contains(entity2) == true ||
            config2?.ConflictsWith?.Contains(entity1) == true)
            return false;
        
        return true;
    }
}

public class ProcessingPhase
{
    public int PhaseNumber { get; set; }
    public List<ParallelGroup> ParallelGroups { get; set; } = new();
    public TimeSpan EstimatedDuration { get; set; }
}

public class ParallelGroup
{
    public List<string> Entities { get; set; } = new();
    public int MaxConcurrency { get; set; } = 1;
}
```

## Dependencies First Approach

### Customer Groups Example Implementation

```csharp
public class CustomerMigrationOrchestrator
{
    private readonly IBigCommerceApiService _sourceApi;
    private readonly IBigCommerceApiService _destinationApi;
    private readonly EntityMappingService _mappingService;
    private readonly ILogger<CustomerMigrationOrchestrator> _logger;
    
    public async Task ProcessCustomerMigration(string migrationId)
    {
        try
        {
            // Phase 1: Create Customer Groups First
            await ProcessCustomerGroups(migrationId);
            
            // Phase 2: Create Customers with Mapped Group IDs
            await ProcessCustomers(migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process customer migration {MigrationId}", migrationId);
            throw;
        }
    }
    
    private async Task ProcessCustomerGroups(string migrationId)
    {
        var customerGroups = await _sourceApi.GetCustomerGroups();
        var batchSize = 25; // Configure based on entity configuration
        
        foreach (var batch in customerGroups.Batch(batchSize))
        {
            var tasks = batch.Select(async group =>
            {
                try
                {
                    var destinationGroup = await _destinationApi.CreateCustomerGroup(group);
                    
                    await _mappingService.StoreMapping(migrationId, "CustomerGroups", 
                        group.Id, destinationGroup.Id, group.Name);
                    
                    return new { Success = true, Group = group };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create customer group {GroupId}", group.Id);
                    return new { Success = false, Group = group };
                }
            });
            
            var results = await Task.WhenAll(tasks);
            
            // Log batch results
            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);
            
            _logger.LogInformation(
                "Customer groups batch completed. Success: {Success}, Failed: {Failed}", 
                successCount, failureCount);
        }
    }
    
    private async Task ProcessCustomers(string migrationId)
    {
        var customers = await _sourceApi.GetCustomers();
        var batchSize = 10; // Configure based on entity configuration
        
        foreach (var batch in customers.Batch(batchSize))
        {
            var tasks = batch.Select(async customer =>
            {
                try
                {
                    // Transform customer with mapped dependencies
                    var transformedCustomer = await TransformCustomerWithMappedIds(customer, migrationId);
                    
                    var destinationCustomer = await _destinationApi.CreateCustomer(transformedCustomer);
                    
                    await _mappingService.StoreMapping(migrationId, "Customers", 
                        customer.Id, destinationCustomer.Id, customer.Email);
                    
                    return new { Success = true, Customer = customer };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create customer {CustomerId}", customer.Id);
                    return new { Success = false, Customer = customer };
                }
            });
            
            await Task.WhenAll(tasks);
        }
    }
    
    private async Task<Customer> TransformCustomerWithMappedIds(Customer customer, string migrationId)
    {
        var transformedCustomer = customer.Clone();
        
        // Map Customer Group ID
        if (!string.IsNullOrEmpty(customer.CustomerGroupId))
        {
            var mappedGroupId = await _mappingService.GetDestinationId(
                migrationId, "CustomerGroups", customer.CustomerGroupId);
            
            if (mappedGroupId != null)
            {
                transformedCustomer.CustomerGroupId = mappedGroupId;
            }
            else
            {
                _logger.LogWarning("Customer group mapping not found for {GroupId}", customer.CustomerGroupId);
                // Could either skip this customer or create a default group
            }
        }
        
        // Map other dependencies (e.g., Store IDs, Address IDs)
        transformedCustomer = await MapAdditionalDependencies(transformedCustomer, migrationId);
        
        return transformedCustomer;
    }
}
```

### Default Dependency Configuration

```json
{
  "Dependencies": [
    {
      "EntityType": "Stores",
      "DependsOn": [],
      "Priority": 1,
      "ProcessingPhase": "Infrastructure",
      "AllowParallelProcessing": true,
      "ConflictsWith": []
    },
    {
      "EntityType": "CustomerGroups",
      "DependsOn": ["Stores"],
      "Priority": 2,
      "ProcessingPhase": "Dependencies",
      "AllowParallelProcessing": true,
      "ConflictsWith": []
    },
    {
      "EntityType": "Categories",
      "DependsOn": ["Stores"],
      "Priority": 2,
      "ProcessingPhase": "Dependencies",
      "AllowParallelProcessing": true,
      "ConflictsWith": []
    },
    {
      "EntityType": "Brands",
      "DependsOn": ["Stores"],
      "Priority": 2,
      "ProcessingPhase": "Dependencies",
      "AllowParallelProcessing": true,
      "ConflictsWith": []
    },
    {
      "EntityType": "Products",
      "DependsOn": ["Categories", "Brands"],
      "Priority": 3,
      "ProcessingPhase": "MainEntities",
      "AllowParallelProcessing": true,
      "ConflictsWith": []
    },
    {
      "EntityType": "Customers",
      "DependsOn": ["CustomerGroups", "Stores"],
      "Priority": 3,
      "ProcessingPhase": "MainEntities",
      "AllowParallelProcessing": true,
      "ConflictsWith": []
    },
    {
      "EntityType": "Variants",
      "DependsOn": ["Products"],
      "Priority": 4,
      "ProcessingPhase": "Components",
      "AllowParallelProcessing": true,
      "ConflictsWith": []
    },
    {
      "EntityType": "Images",
      "DependsOn": ["Products"],
      "Priority": 4,
      "ProcessingPhase": "Components",
      "AllowParallelProcessing": false,
      "ConflictsWith": ["Variants"]
    },
    {
      "EntityType": "Orders",
      "DependsOn": ["Customers", "Products"],
      "Priority": 5,
      "ProcessingPhase": "TransactionalData",
      "AllowParallelProcessing": true,
      "ConflictsWith": []
    }
  ]
}
```

## Error Handling and Recovery

### Dependency Processing Error Handling

```csharp
public class DependencyProcessingService
{
    public async Task ProcessDependency(string entityType, string migrationId, 
        EntityConfiguration config)
    {
        try
        {
            var entities = await _sourceApi.GetEntities(entityType);
            var processedCount = 0;
            var failedCount = 0;
            
            foreach (var batch in entities.Batch(config.BatchSize))
            {
                var batchResults = await ProcessBatch(batch, entityType, migrationId);
                
                processedCount += batchResults.Count(r => r.Success);
                failedCount += batchResults.Count(r => !r.Success);
                
                // Continue processing even if some entities fail
                await LogBatchResults(migrationId, entityType, batchResults);
            }
            
            _logger.LogInformation(
                "Dependency processing completed for {EntityType}. Processed: {Processed}, Failed: {Failed}",
                entityType, processedCount, failedCount);
        }
        catch (Exception ex)
        {
            // Critical error - this dependency cannot be processed
            await _logger.LogError(migrationId, new MigrationError
            {
                EntityType = entityType,
                ErrorMessage = $"Failed to process dependency: {ex.Message}",
                ErrorLevel = "Critical",
                StackTrace = ex.StackTrace
            });
            
            throw new DependencyProcessingException($"Critical failure processing {entityType}", ex);
        }
    }
    
    private async Task<List<ProcessingResult>> ProcessBatch(IEnumerable<object> batch, 
        string entityType, string migrationId)
    {
        var tasks = batch.Select(async entity =>
        {
            try
            {
                var destinationEntity = await _destinationApi.CreateEntity(entityType, entity);
                
                await _mappingService.StoreMapping(migrationId, entityType, 
                    entity.Id, destinationEntity.Id, entity.Name);
                
                return new ProcessingResult { Success = true, Entity = entity };
            }
            catch (Exception ex)
            {
                await _logger.LogError(migrationId, new MigrationError
                {
                    EntityType = entityType,
                    EntityId = entity.Id,
                    EntityName = entity.Name,
                    ErrorMessage = ex.Message,
                    ErrorLevel = "Error",
                    StackTrace = ex.StackTrace
                });
                
                return new ProcessingResult { Success = false, Entity = entity, Error = ex };
            }
        });
        
        return (await Task.WhenAll(tasks)).ToList();
    }
}

public class ProcessingResult
{
    public bool Success { get; set; }
    public object Entity { get; set; }
    public Exception Error { get; set; }
}
```

### Resume Capability with Mapping Validation

```csharp
public class MigrationResumeService
{
    public async Task<bool> CanResumeMigration(string migrationId, string entityType)
    {
        // Check if any mappings exist for this entity type
        var existingMappings = await _tableStorage.QueryAsync<EntityMappingEntity>(
            $"PartitionKey eq '{migrationId}:{entityType}'");
        
        return existingMappings.Any();
    }
    
    public async Task<List<string>> GetProcessedEntityIds(string migrationId, string entityType)
    {
        var mappings = await _tableStorage.QueryAsync<EntityMappingEntity>(
            $"PartitionKey eq '{migrationId}:{entityType}' and Status eq 'Success'");
        
        return mappings.Select(m => m.SourceEntityId).ToList();
    }
    
    public async Task ValidateMappingIntegrity(string migrationId)
    {
        var dependencyGraph = await _dependencyResolver.LoadDependencyGraph();
        var issues = new List<string>();
        
        foreach (var entityType in dependencyGraph.Keys)
        {
            var dependencies = dependencyGraph[entityType].DependsOn;
            
            if (dependencies?.Any() == true)
            {
                var entityMappings = await GetProcessedEntityIds(migrationId, entityType);
                
                foreach (var dependency in dependencies)
                {
                    var dependencyMappings = await GetProcessedEntityIds(migrationId, dependency);
                    
                    // Check if any entity references a dependency that doesn't exist
                    var orphanedEntities = await FindOrphanedEntities(migrationId, entityType, dependency);
                    
                    if (orphanedEntities.Any())
                    {
                        issues.Add($"Entity type {entityType} has {orphanedEntities.Count} orphaned references to {dependency}");
                    }
                }
            }
        }
        
        if (issues.Any())
        {
            throw new MappingIntegrityException($"Mapping integrity issues found: {string.Join(", ", issues)}");
        }
    }
}
```

## Performance Optimization

### Batch Mapping Operations

```csharp
public class OptimizedMappingService
{
    public async Task StoreBatchMappings(string migrationId, string entityType, 
        Dictionary<string, string> mappings)
    {
        var entities = mappings.Select(kvp => new EntityMappingEntity
        {
            PartitionKey = $"{migrationId}:{entityType}",
            RowKey = kvp.Key,
            MigrationId = migrationId,
            EntityType = entityType,
            SourceEntityId = kvp.Key,
            DestinationEntityId = kvp.Value,
            CreatedAt = DateTime.UtcNow,
            Status = "Success"
        }).ToList();
        
        await _tableStorage.BatchInsertAsync("EntityMappings", entities);
    }
    
    public async Task<Dictionary<string, string>> GetBatchMappingsWithFallback(
        string migrationId, string entityType, IEnumerable<string> sourceIds)
    {
        var mappings = await GetBatchMappings(migrationId, entityType, sourceIds);
        var missingIds = sourceIds.Except(mappings.Keys).ToList();
        
        if (missingIds.Any())
        {
            _logger.LogWarning("Missing mappings for {EntityType}: {MissingIds}", 
                entityType, string.Join(", ", missingIds));
        }
        
        return mappings;
    }
}
```

### Caching Strategy

```csharp
public class CachedMappingService
{
    private readonly IMemoryCache _cache;
    private readonly EntityMappingService _mappingService;
    
    public async Task<string> GetDestinationIdWithCache(string migrationId, 
        string entityType, string sourceId)
    {
        var cacheKey = $"mapping:{migrationId}:{entityType}:{sourceId}";
        
        if (_cache.TryGetValue(cacheKey, out string cachedId))
        {
            return cachedId;
        }
        
        var destinationId = await _mappingService.GetDestinationId(migrationId, entityType, sourceId);
        
        if (destinationId != null)
        {
            _cache.Set(cacheKey, destinationId, TimeSpan.FromMinutes(30));
        }
        
        return destinationId;
    }
}
```

## Summary

This implementation provides:

1. **Robust ID Mapping**: Complete bidirectional mapping with metadata and error tracking
2. **Flexible Dependency Resolution**: Graph-based dependency resolution with parallel processing optimization
3. **Dependencies First Approach**: Ensures all dependencies are created before dependent entities
4. **Error Resilience**: Comprehensive error handling with continue-on-failure semantics
5. **Performance Optimization**: Batch operations and caching for high-throughput scenarios
6. **Resume Capability**: Full state preservation for migration resume scenarios

The system ensures data integrity while providing optimal performance for large-scale BigCommerce migrations. 