namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// Test implementation of IEntityMappingService that simulates realistic Azure Table Storage performance
/// Demonstrates the performance difference between individual vs batch lookups
/// </summary>
public class TestEntityMappingService : IEntityMappingService
{
    private readonly Random _random = new();
    private readonly Dictionary<string, Dictionary<string, string>> _entityMappings;

    /// <summary>
    /// Initializes the test service with pre-populated mappings
    /// </summary>
    public TestEntityMappingService()
    {
        _entityMappings = new Dictionary<string, Dictionary<string, string>>();
        InitializeTestMappings();
    }

    /// <summary>
    /// Simulates individual Azure Table Storage lookup
    /// Each lookup takes 2-5ms (realistic Azure Table Storage latency)
    /// </summary>
    public async Task<string?> GetDestinationEntityIdAsync(string entityType, string sourceEntityId)
    {
        // Simulate Azure Table Storage individual query latency (2-5ms typical)
        var latency = _random.Next(2, 6);
        await Task.Delay(latency).ConfigureAwait(false);
        
        // Look up the mapping
        if (_entityMappings.TryGetValue(entityType, out var typeMappings) &&
            typeMappings.TryGetValue(sourceEntityId, out var destinationId))
        {
            return destinationId;
        }
        
        return null; // Not found
    }

    /// <summary>
    /// Simulates batch Azure Table Storage lookup
    /// Batch operations are much more efficient: ~5ms for entire batch vs 2-5ms per individual
    /// </summary>
    public async Task<Dictionary<string, string?>> GetDestinationEntityIdsBatchAsync(string entityType, List<string> sourceEntityIds)
    {
        // Simulate Azure Table Storage batch query latency
        // Batch queries are much more efficient - fixed ~5ms overhead + small per-item cost
        var batchBaseLatency = 5; // Base batch operation overhead
        var perItemLatency = Math.Max(1, sourceEntityIds.Count / 20); // Very small per-item cost in batch
        var totalLatency = batchBaseLatency + perItemLatency;
        
        await Task.Delay(totalLatency).ConfigureAwait(false);
        
        var results = new Dictionary<string, string?>();
        
        if (_entityMappings.TryGetValue(entityType, out var typeMappings))
        {
            foreach (var sourceId in sourceEntityIds)
            {
                results[sourceId] = typeMappings.TryGetValue(sourceId, out var destinationId) ? destinationId : null;
            }
        }
        else
        {
            // Entity type not found - return nulls for all
            foreach (var sourceId in sourceEntityIds)
            {
                results[sourceId] = null;
            }
        }
        
        return results;
    }

    /// <summary>
    /// Simulates getting all mappings (cache loading scenario)
    /// Large result set - takes longer but loads everything at once
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllMappingsAsync(string entityType)
    {
        // Simulate loading all mappings (larger operation - 20-50ms depending on size)
        if (_entityMappings.TryGetValue(entityType, out var typeMappings))
        {
            var estimatedLatency = Math.Max(20, typeMappings.Count / 100); // Scales with data size
            await Task.Delay(estimatedLatency).ConfigureAwait(false);
            
            // Return non-null mappings only
            return typeMappings.Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                              .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        
        await Task.Delay(5).ConfigureAwait(false); // Minimal delay for empty result
        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Simulates existence check (lightweight query)
    /// Faster than full lookup but still requires individual query
    /// </summary>
    public async Task<bool> MappingExistsAsync(string entityType, string sourceEntityId)
    {
        // Existence checks are slightly faster than full lookups (1-3ms)
        var latency = _random.Next(1, 4);
        await Task.Delay(latency).ConfigureAwait(false);
        
        return _entityMappings.TryGetValue(entityType, out var typeMappings) &&
               typeMappings.ContainsKey(sourceEntityId);
    }

    /// <summary>
    /// Initializes test mappings to simulate a realistic migration scenario
    /// </summary>
    private void InitializeTestMappings()
    {
        // Initialize mappings for different entity types
        _entityMappings["products"] = GenerateEntityMappings("product", 10000, "dest-prod-");
        _entityMappings["categories"] = GenerateEntityMappings("category", 500, "dest-cat-");
        _entityMappings["brands"] = GenerateEntityMappings("brand", 100, "dest-brand-");
        _entityMappings["variants"] = GenerateEntityMappings("variant", 25000, "dest-var-");
        _entityMappings["images"] = GenerateEntityMappings("image", 50000, "dest-img-");
        _entityMappings["modifiers"] = GenerateEntityMappings("modifier", 5000, "dest-mod-");
    }

    /// <summary>
    /// Generates entity mappings for testing
    /// </summary>
    private Dictionary<string, string> GenerateEntityMappings(string entityPrefix, int count, string destinationPrefix)
    {
        var mappings = new Dictionary<string, string>();
        
        for (int i = 1; i <= count; i++)
        {
            var sourceId = $"source-{entityPrefix}-{i}";
            var destinationId = $"{destinationPrefix}{i}";
            mappings[sourceId] = destinationId;
        }
        
        return mappings;
    }

    /// <summary>
    /// Gets mapping statistics for analysis
    /// </summary>
    public Dictionary<string, int> GetMappingStatistics()
    {
        return _entityMappings.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Count
        );
    }
} 