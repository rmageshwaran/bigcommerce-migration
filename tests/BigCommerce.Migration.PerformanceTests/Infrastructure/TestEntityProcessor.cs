namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// Test implementation of entity processor that simulates realistic BigCommerce migration workflows
/// Demonstrates different memory usage patterns for performance analysis
/// </summary>
public class TestEntityProcessor : ITestEntityProcessor
{
    private readonly Random _random = new();
    private readonly IEntityMappingService _mappingService;

    public TestEntityProcessor()
    {
        _mappingService = new TestEntityMappingService();
    }

    /// <summary>
    /// Standard entity processing approach - loads all data into memory at once
    /// Simulates current migration approach that may have memory issues
    /// </summary>
    public async Task ProcessEntitiesAsync(List<Dictionary<string, object>> entities)
    {
        // Simulate memory-intensive processing by creating intermediate collections
        var processedEntities = new List<Dictionary<string, object>>();
        var mappingCache = new Dictionary<string, string>();
        var transformedData = new List<object>();

        foreach (var entity in entities)
        {
            // Simulate entity processing steps that allocate memory
            var enrichedEntity = await EnrichEntityAsync(entity).ConfigureAwait(false);
            var mappedEntity = await ApplyMappingsAsync(enrichedEntity, mappingCache).ConfigureAwait(false);
            var transformedEntity = await TransformEntityAsync(mappedEntity).ConfigureAwait(false);
            
            // Keep all processed data in memory (memory-intensive approach)
            processedEntities.Add(transformedEntity);
            transformedData.Add(CreateAuxiliaryData(transformedEntity));
            
            // Simulate minimal processing delay for faster tests
            await Task.Delay(1).ConfigureAwait(false);
        }

        // Simulate final processing that keeps everything in memory
        await FinalizeProcessingAsync(processedEntities, transformedData).ConfigureAwait(false);
    }

    /// <summary>
    /// Streaming entity processing approach - processes one entity at a time
    /// Memory-efficient approach that minimizes memory usage
    /// </summary>
    public async Task ProcessEntitiesStreamingAsync(List<Dictionary<string, object>> entities)
    {
        // Process entities one at a time without keeping all in memory
        foreach (var entity in entities)
        {
            // Process and immediately dispose
            var enrichedEntity = await EnrichEntityAsync(entity).ConfigureAwait(false);
            var mappedEntity = await ApplyStreamingMappingAsync(enrichedEntity).ConfigureAwait(false);
            var transformedEntity = await TransformEntityAsync(mappedEntity).ConfigureAwait(false);
            
            // Immediately process and release memory
            await ProcessSingleEntityAsync(transformedEntity).ConfigureAwait(false);
            
            // Explicitly help GC by nulling references in streaming approach
            entity.Clear();
            
            // Simulate minimal streaming delay for faster tests  
            await Task.Delay(1).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Batch entity processing approach - processes entities in configurable batches
    /// Balances memory efficiency with processing performance
    /// </summary>
    public async Task ProcessEntitiesBatchAsync(List<Dictionary<string, object>> entities, int batchSize)
    {
        var batches = entities
            .Select((entity, index) => new { entity, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.entity).ToList())
            .ToList();

        foreach (var batch in batches)
        {
            // Process batch in memory-controlled chunks
            var batchResults = new List<Dictionary<string, object>>(batch.Count);
            
            foreach (var entity in batch)
            {
                var enrichedEntity = await EnrichEntityAsync(entity).ConfigureAwait(false);
                var mappedEntity = await ApplyBatchMappingAsync(enrichedEntity).ConfigureAwait(false);
                var transformedEntity = await TransformEntityAsync(mappedEntity).ConfigureAwait(false);
                
                batchResults.Add(transformedEntity);
            }
            
            // Process entire batch and then release memory
            await ProcessBatchAsync(batchResults).ConfigureAwait(false);
            
            // Clear batch memory
            batchResults.Clear();
            batch.Clear();
            
            // Force garbage collection between batches to measure memory patterns
            if (batches.Count > 5) // Only for larger batch sets
            {
                GC.Collect(0, GCCollectionMode.Optimized);
            }
            
            await Task.Delay(2).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Entity processing with garbage collection analysis hooks
    /// Includes explicit GC triggers and memory measurements for analysis
    /// </summary>
    public async Task ProcessEntitiesWithGCAnalysisAsync(List<Dictionary<string, object>> entities)
    {
        var gcTriggerInterval = Math.Max(100, entities.Count / 10); // Trigger GC every ~10% of entities
        
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            
            // Process entity with memory tracking
            var enrichedEntity = await EnrichEntityAsync(entity).ConfigureAwait(false);
            var mappedEntity = await ApplyMappingsAsync(enrichedEntity, new Dictionary<string, string>()).ConfigureAwait(false);
            var transformedEntity = await TransformEntityAsync(mappedEntity).ConfigureAwait(false);
            
            // Create additional memory pressure for analysis
            var auxData = CreateLargeAuxiliaryData(transformedEntity);
            
            await ProcessSingleEntityAsync(transformedEntity).ConfigureAwait(false);
            
            // Trigger GC at intervals for analysis
            if (i % gcTriggerInterval == 0 && i > 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            
            // Clear temporary data
            auxData = null;
            entity.Clear();
            
            await Task.Delay(1).ConfigureAwait(false);
        }
    }

    #region Helper Methods

    /// <summary>
    /// Simulates entity enrichment with additional data
    /// </summary>
    private async Task<Dictionary<string, object>> EnrichEntityAsync(Dictionary<string, object> entity)
    {
        await Task.Delay(1).ConfigureAwait(false);
        
        var enriched = new Dictionary<string, object>(entity);
        
        // Add enrichment data that consumes memory
        enriched["enriched_timestamp"] = DateTime.UtcNow;
        enriched["enriched_metadata"] = GenerateMetadata(entity);
        enriched["enriched_tags"] = GenerateTags();
        enriched["enriched_analytics"] = GenerateAnalyticsData();
        
        return enriched;
    }

    /// <summary>
    /// Applies entity mappings with caching (memory-intensive)
    /// </summary>
    private async Task<Dictionary<string, object>> ApplyMappingsAsync(Dictionary<string, object> entity, Dictionary<string, string> cache)
    {
        var entityType = entity.GetValueOrDefault("entity_type", "unknown")?.ToString() ?? "unknown";
        var sourceId = entity.GetValueOrDefault("id", "0")?.ToString() ?? "0";
        
        // Use caching approach (builds up memory over time)
        if (!cache.ContainsKey(sourceId))
        {
            var destinationId = await _mappingService.GetDestinationEntityIdAsync(entityType, sourceId).ConfigureAwait(false);
            cache[sourceId] = destinationId ?? $"mapped-{sourceId}";
        }
        
        var mapped = new Dictionary<string, object>(entity)
        {
            ["destination_id"] = cache[sourceId],
            ["mapping_timestamp"] = DateTime.UtcNow
        };
        
        return mapped;
    }

    /// <summary>
    /// Applies mappings with streaming approach (memory-efficient)
    /// </summary>
    private async Task<Dictionary<string, object>> ApplyStreamingMappingAsync(Dictionary<string, object> entity)
    {
        var entityType = entity.GetValueOrDefault("entity_type", "unknown")?.ToString() ?? "unknown";
        var sourceId = entity.GetValueOrDefault("id", "0")?.ToString() ?? "0";
        
        // Direct lookup without caching (streaming approach)
        var destinationId = await _mappingService.GetDestinationEntityIdAsync(entityType, sourceId).ConfigureAwait(false);
        
        var mapped = new Dictionary<string, object>(entity)
        {
            ["destination_id"] = destinationId ?? $"mapped-{sourceId}",
            ["mapping_timestamp"] = DateTime.UtcNow
        };
        
        return mapped;
    }

    /// <summary>
    /// Applies mappings with batch optimization
    /// </summary>
    private async Task<Dictionary<string, object>> ApplyBatchMappingAsync(Dictionary<string, object> entity)
    {
        // For batch processing, we'd collect IDs and do batch lookup
        // For simulation, just do individual lookup with minimal overhead
        return await ApplyStreamingMappingAsync(entity).ConfigureAwait(false);
    }

    /// <summary>
    /// Transforms entity data
    /// </summary>
    private async Task<Dictionary<string, object>> TransformEntityAsync(Dictionary<string, object> entity)
    {
        await Task.Delay(1).ConfigureAwait(false);
        
        var transformed = new Dictionary<string, object>(entity)
        {
            ["transformed_timestamp"] = DateTime.UtcNow,
            ["transformation_id"] = Guid.NewGuid().ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            ["transformed_data"] = GenerateTransformationData(entity)
        };
        
        return transformed;
    }

    /// <summary>
    /// Creates auxiliary data for memory pressure simulation
    /// </summary>
    private object CreateAuxiliaryData(Dictionary<string, object> entity)
    {
        return new
        {
            EntityId = entity.GetValueOrDefault("id", "0")?.ToString(),
            ProcessingMetrics = Enumerable.Range(1, 10).Select(i => new { Metric = $"metric_{i}", Value = _random.NextDouble() }).ToList(),
            Timestamps = Enumerable.Range(1, 5).Select(i => DateTime.UtcNow.AddMilliseconds(-i)).ToList(),
            LargeData = new string('x', 1024) // 1KB of data per entity
        };
    }

    /// <summary>
    /// Creates large auxiliary data for GC analysis
    /// </summary>
    private object CreateLargeAuxiliaryData(Dictionary<string, object> entity)
    {
        return new
        {
            EntityId = entity.GetValueOrDefault("id", "0")?.ToString(),
            LargeBuffer = new byte[8192], // 8KB buffer
            StringData = new string('y', 4096), // 4KB string
            CollectionData = Enumerable.Range(1, 100).Select(i => $"item_{i}_{DateTime.UtcNow.Ticks}").ToList()
        };
    }

    /// <summary>
    /// Generates metadata for entity enrichment
    /// </summary>
    private Dictionary<string, object> GenerateMetadata(Dictionary<string, object> entity)
    {
        return new Dictionary<string, object>
        {
            ["source_system"] = "test",
            ["migration_batch"] = _random.Next(1, 100),
            ["processing_version"] = "1.0.0",
            ["entity_hash"] = entity.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["metadata_size"] = _random.Next(100, 1000)
        };
    }

    /// <summary>
    /// Generates tags data
    /// </summary>
    private List<string> GenerateTags()
    {
        var tagCount = _random.Next(2, 8);
        return Enumerable.Range(1, tagCount)
            .Select(i => $"tag_{i}_{_random.Next(1000, 9999)}")
            .ToList();
    }

    /// <summary>
    /// Generates analytics data
    /// </summary>
    private Dictionary<string, double> GenerateAnalyticsData()
    {
        return new Dictionary<string, double>
        {
            ["processing_score"] = _random.NextDouble() * 100,
            ["quality_metric"] = _random.NextDouble(),
            ["complexity_factor"] = _random.NextDouble() * 10,
            ["memory_footprint"] = _random.NextDouble() * 1024
        };
    }

    /// <summary>
    /// Generates transformation data
    /// </summary>
    private object GenerateTransformationData(Dictionary<string, object> entity)
    {
        return new
        {
            OriginalKeys = entity.Keys.Count,
            TransformationRules = _random.Next(5, 15),
            DataQuality = _random.NextDouble(),
            TransformationLog = $"Transformed at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}"
        };
    }

    /// <summary>
    /// Finalizes processing with memory-intensive operations
    /// </summary>
    private async Task FinalizeProcessingAsync(List<Dictionary<string, object>> processedEntities, List<object> transformedData)
    {
        await Task.Delay(10).ConfigureAwait(false);
        
        // Simulate memory-intensive finalization
        var summary = new
        {
            TotalEntities = processedEntities.Count,
            ProcessingMetrics = transformedData.Count,
            FinalizedAt = DateTime.UtcNow,
            LargeReport = new string('z', processedEntities.Count * 10) // Proportional to entity count
        };
        
        GC.KeepAlive(summary);
    }

    /// <summary>
    /// Processes a single entity (streaming approach)
    /// </summary>
    private async Task ProcessSingleEntityAsync(Dictionary<string, object> entity)
    {
        await Task.Delay(1).ConfigureAwait(false);
        
        // Simulate individual entity processing without retaining memory
        var result = entity.GetValueOrDefault("destination_id", "unknown");
        GC.KeepAlive(result);
    }

    /// <summary>
    /// Processes a batch of entities
    /// </summary>
    private async Task ProcessBatchAsync(List<Dictionary<string, object>> batch)
    {
        await Task.Delay(5).ConfigureAwait(false);
        
        // Simulate batch processing
        var batchSummary = new
        {
            BatchSize = batch.Count,
            ProcessedAt = DateTime.UtcNow,
            BatchId = Guid.NewGuid().ToString("D", System.Globalization.CultureInfo.InvariantCulture)
        };
        
        GC.KeepAlive(batchSummary);
    }

    #endregion
} 