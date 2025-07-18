using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.PerformanceTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Baselines;

/// <summary>
/// TDD RED PHASE: Entity Mapping Performance Baseline Tests
/// These tests measure the critical performance difference between individual vs batch lookups
/// Tests will initially fail because we need to implement test infrastructure
/// </summary>
public class EntityMappingPerformanceTests
{
    private readonly ITestOutputHelper _output;
    private readonly PerformanceBenchmark _benchmark;

    public EntityMappingPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _benchmark = new PerformanceBenchmark();
    }

    [Fact]
    public async Task Individual_Entity_Mapping_Lookup_Performance_Baseline()
    {
        // ARRANGE
        var mappingService = CreateTestEntityMappingService();
        var sourceEntityIds = GenerateSourceEntityIds(50); // 50 individual lookups
        
        // ACT - Measure individual lookup performance (current approach)
        var result = await _benchmark.MeasureAsync(async () =>
        {
            foreach (var sourceId in sourceEntityIds)
            {
                var destinationId = await mappingService.GetDestinationEntityIdAsync("products", sourceId).ConfigureAwait(false);
                GC.KeepAlive(destinationId); // Prevent optimization
            }
        }).ConfigureAwait(false);
        
        // ASSERT - Document current performance baseline
        Assert.True(result.ElapsedMilliseconds > 0);
        var averageTimePerLookup = result.ElapsedMilliseconds / sourceEntityIds.Count;
        
        _output.WriteLine("=== INDIVIDUAL ENTITY MAPPING BASELINE ===");
        _output.WriteLine($"Total Lookups: {sourceEntityIds.Count}");
        _output.WriteLine($"Total Time: {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Average Time per Lookup: {averageTimePerLookup:F1}ms");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
        _output.WriteLine($"Lookups per Second: {sourceEntityIds.Count / (result.ElapsedMilliseconds / 1000.0):F1}");
        
        // Store baseline for comparison
        await SavePerformanceBaseline("individual_entity_mapping", result, sourceEntityIds.Count).ConfigureAwait(false);
    }

    [Fact]
    public async Task Batch_Entity_Mapping_Lookup_Performance()
    {
        // ARRANGE
        var mappingService = CreateTestEntityMappingService();
        var sourceEntityIds = GenerateSourceEntityIds(50); // Same 50 lookups, but batched
        var batchSize = 25; // Batch into groups of 25
        
        // ACT - Measure batch lookup performance (optimized approach)
        var result = await _benchmark.MeasureAsync(async () =>
        {
            var batches = sourceEntityIds
                .Select((id, index) => new { id, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.id).ToList())
                .ToList();
            
            foreach (var batch in batches)
            {
                var destinationIds = await mappingService.GetDestinationEntityIdsBatchAsync("products", batch).ConfigureAwait(false);
                GC.KeepAlive(destinationIds); // Prevent optimization
            }
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(result.ElapsedMilliseconds > 0);
        var averageTimePerBatch = result.ElapsedMilliseconds / (sourceEntityIds.Count / batchSize);
        var averageTimePerLookup = result.ElapsedMilliseconds / sourceEntityIds.Count;
        
        _output.WriteLine("=== BATCH ENTITY MAPPING PERFORMANCE ===");
        _output.WriteLine($"Total Lookups: {sourceEntityIds.Count}");
        _output.WriteLine($"Batch Size: {batchSize}");
        _output.WriteLine($"Number of Batches: {sourceEntityIds.Count / batchSize}");
        _output.WriteLine($"Total Time: {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Average Time per Batch: {averageTimePerBatch:F1}ms");
        _output.WriteLine($"Average Time per Lookup: {averageTimePerLookup:F1}ms");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
        _output.WriteLine($"Batch Lookups per Second: {sourceEntityIds.Count / (result.ElapsedMilliseconds / 1000.0):F1}");
        
        await SavePerformanceBaseline("batch_entity_mapping", result, sourceEntityIds.Count).ConfigureAwait(false);
    }

    [Fact]
    public async Task Entity_Mapping_Performance_Comparison_Individual_vs_Batch()
    {
        // ARRANGE
        var mappingService = CreateTestEntityMappingService();
        var sourceEntityIds = GenerateSourceEntityIds(100); // More data for clearer comparison
        var batchSize = 25;
        
        // ACT - Measure both approaches with identical data
        var individualResult = await _benchmark.MeasureAsync(async () =>
        {
            foreach (var sourceId in sourceEntityIds)
            {
                var destinationId = await mappingService.GetDestinationEntityIdAsync("products", sourceId).ConfigureAwait(false);
                GC.KeepAlive(destinationId);
            }
        }).ConfigureAwait(false);
        
        var batchResult = await _benchmark.MeasureAsync(async () =>
        {
            var batches = sourceEntityIds
                .Select((id, index) => new { id, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.id).ToList())
                .ToList();
            
            foreach (var batch in batches)
            {
                var destinationIds = await mappingService.GetDestinationEntityIdsBatchAsync("products", batch).ConfigureAwait(false);
                GC.KeepAlive(destinationIds);
            }
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(individualResult.ElapsedMilliseconds > 0);
        Assert.True(batchResult.ElapsedMilliseconds > 0);
        
        var improvement = batchResult.GetImprovementOver(individualResult);
        var speedMultiplier = individualResult.ElapsedMilliseconds / batchResult.ElapsedMilliseconds;
        
        _output.WriteLine("=== INDIVIDUAL vs BATCH MAPPING COMPARISON ===");
        _output.WriteLine($"Total Entity Lookups: {sourceEntityIds.Count}");
        _output.WriteLine($"Individual Approach Time: {individualResult.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Batch Approach Time: {batchResult.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Performance Improvement: {improvement}");
        _output.WriteLine($"Speed Multiplier: {speedMultiplier:F1}x faster");
        _output.WriteLine($"Individual Memory: {individualResult.MemoryUsed:N0} bytes");
        _output.WriteLine($"Batch Memory: {batchResult.MemoryUsed:N0} bytes");
        
        // Document which approach is faster
        if (batchResult.IsFasterThan(individualResult))
        {
            _output.WriteLine("✅ Batch approach is significantly faster");
            _output.WriteLine($"📈 Improvement Target: Should achieve 5x+ improvement (currently {speedMultiplier:F1}x)");
        }
        else
        {
            _output.WriteLine("⚠️ Individual approach is faster (unexpected - needs investigation)");
        }
        
        // Verify batch approach achieves significant improvement
        Assert.True(speedMultiplier >= 2.0, $"Batch approach should be at least 2x faster, but was only {speedMultiplier:F1}x");
    }

    [Fact]
    public async Task Large_Scale_Entity_Mapping_Memory_Efficiency()
    {
        // ARRANGE
        var mappingService = CreateTestEntityMappingService();
        var sourceEntityIds = GenerateSourceEntityIds(1000); // Large dataset simulation
        var batchSize = 50;
        
        // ACT - Measure memory efficiency with large dataset
        var result = await _benchmark.MeasureAsync(async () =>
        {
            var batches = sourceEntityIds
                .Select((id, index) => new { id, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.id).ToList())
                .ToList();
            
            foreach (var batch in batches)
            {
                var destinationIds = await mappingService.GetDestinationEntityIdsBatchAsync("products", batch).ConfigureAwait(false);
                
                // Simulate processing the results
                foreach (var mapping in destinationIds)
                {
                    GC.KeepAlive(mapping);
                }
                
                // Clear references to measure memory usage patterns
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(result.ElapsedMilliseconds > 0);
        var memoryPerLookup = result.MemoryUsed / (double)sourceEntityIds.Count;
        var throughput = sourceEntityIds.Count / (result.ElapsedMilliseconds / 1000.0);
        
        _output.WriteLine("=== LARGE SCALE MAPPING MEMORY EFFICIENCY ===");
        _output.WriteLine($"Total Lookups: {sourceEntityIds.Count:N0}");
        _output.WriteLine($"Batch Size: {batchSize}");
        _output.WriteLine($"Total Time: {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
        _output.WriteLine($"Memory per Lookup: {memoryPerLookup:F1} bytes/lookup");
        _output.WriteLine($"Throughput: {throughput:F1} lookups/second");
        _output.WriteLine($"Estimated 10M Lookups Memory: {(memoryPerLookup * 10_000_000) / (1024 * 1024):F1} MB");
        
        // Verify memory efficiency for large scale
        var estimatedMemoryFor10M = memoryPerLookup * 10_000_000;
        var maxAllowedMemoryBytes = 500 * 1024 * 1024; // 500MB limit for Azure Functions
        
        Assert.True(estimatedMemoryFor10M < maxAllowedMemoryBytes, 
            $"Memory usage should be efficient for large scale. Estimated 10M lookups would use {estimatedMemoryFor10M / (1024 * 1024):F1}MB, exceeding {maxAllowedMemoryBytes / (1024 * 1024)}MB limit");
        
        await SavePerformanceBaseline("large_scale_mapping_memory", result, sourceEntityIds.Count).ConfigureAwait(false);
    }

    [Fact]
    public async Task Entity_Mapping_Different_Batch_Sizes_Performance()
    {
        // ARRANGE
        var mappingService = CreateTestEntityMappingService();
        var sourceEntityIds = GenerateSourceEntityIds(200);
        var batchSizes = new[] { 10, 25, 50, 100 }; // Test different batch sizes
        
        _output.WriteLine("=== BATCH SIZE OPTIMIZATION ANALYSIS ===");
        
        var bestPerformance = double.MaxValue;
        var optimalBatchSize = 0;
        
        foreach (var batchSize in batchSizes)
        {
            // ACT - Measure performance for each batch size
            var result = await _benchmark.MeasureAsync(async () =>
            {
                var batches = sourceEntityIds
                    .Select((id, index) => new { id, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(x => x.id).ToList())
                    .ToList();
                
                foreach (var batch in batches)
                {
                    var destinationIds = await mappingService.GetDestinationEntityIdsBatchAsync("products", batch).ConfigureAwait(false);
                    GC.KeepAlive(destinationIds);
                }
            }).ConfigureAwait(false);
            
            var averageTimePerLookup = result.ElapsedMilliseconds / sourceEntityIds.Count;
            var throughput = sourceEntityIds.Count / (result.ElapsedMilliseconds / 1000.0);
            
            _output.WriteLine($"Batch Size {batchSize:D3}: {result.ElapsedMilliseconds:F1}ms total, {averageTimePerLookup:F2}ms/lookup, {throughput:F1} lookups/sec");
            
            if (result.ElapsedMilliseconds < bestPerformance)
            {
                bestPerformance = result.ElapsedMilliseconds;
                optimalBatchSize = batchSize;
            }
        }
        
        // ASSERT
        Assert.True(optimalBatchSize > 0, "Should identify an optimal batch size");
        _output.WriteLine($"✅ Optimal Batch Size: {optimalBatchSize} (fastest performance)");
        
        // Store results for future optimization
        await SavePerformanceBaseline($"batch_size_optimization_optimal_{optimalBatchSize}", 
            new PerformanceResult 
            { 
                ElapsedMilliseconds = bestPerformance,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMilliseconds(bestPerformance),
                MemoryBefore = 0,
                MemoryAfter = 0
            }, sourceEntityIds.Count).ConfigureAwait(false);
    }

    [Fact]
    public async Task Entity_Mapping_Cache_vs_NoCache_Performance()
    {
        // ARRANGE
        var mappingService = CreateTestEntityMappingService();
        var sourceEntityIds = GenerateSourceEntityIds(100);
        
        // ACT - Test performance with and without caching (if supported)
        var noCacheResult = await _benchmark.MeasureAsync(async () =>
        {
            foreach (var sourceId in sourceEntityIds)
            {
                var destinationId = await mappingService.GetDestinationEntityIdAsync("products", sourceId).ConfigureAwait(false);
                GC.KeepAlive(destinationId);
            }
        }).ConfigureAwait(false);
        
        // Simulate second pass (would benefit from caching)
        var cachedResult = await _benchmark.MeasureAsync(async () =>
        {
            foreach (var sourceId in sourceEntityIds)
            {
                var destinationId = await mappingService.GetDestinationEntityIdAsync("products", sourceId).ConfigureAwait(false);
                GC.KeepAlive(destinationId);
            }
        }).ConfigureAwait(false);
        
        // ASSERT
        var improvement = cachedResult.GetImprovementOver(noCacheResult);
        
        _output.WriteLine("=== CACHING IMPACT ANALYSIS ===");
        _output.WriteLine($"First Pass (No Cache): {noCacheResult.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Second Pass (Potential Cache): {cachedResult.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Cache Improvement: {improvement}");
        
        if (cachedResult.IsFasterThan(noCacheResult))
        {
            _output.WriteLine("✅ Caching shows performance benefit");
        }
        else
        {
            _output.WriteLine("ℹ️ No significant caching benefit (as expected for table storage)");
        }
        
        await SavePerformanceBaseline("caching_analysis", noCacheResult, sourceEntityIds.Count).ConfigureAwait(false);
    }

    #region Helper Methods (GREEN phase implementation)

    /// <summary>
    /// Creates a test entity mapping service for performance measurement
    /// </summary>
    private IEntityMappingService CreateTestEntityMappingService()
    {
        return new TestEntityMappingService();
    }

    /// <summary>
    /// Generates realistic source entity IDs for testing
    /// </summary>
    private List<string> GenerateSourceEntityIds(int count)
    {
        var sourceIds = new List<string>();
        var random = new Random(42); // Fixed seed for consistent results
        
        for (int i = 0; i < count; i++)
        {
            // Generate IDs that exist in our test mapping service
            var entityId = random.Next(1, 10001); // Within range of test data
            sourceIds.Add($"source-product-{entityId}");
        }
        
        return sourceIds;
    }

    /// <summary>
    /// Saves performance baseline with entity count for future comparison
    /// Enhanced to include entity count and throughput metrics
    /// </summary>
    private async Task SavePerformanceBaseline(string testName, PerformanceResult result, int entityCount)
    {
        // Simulate async save operation
        await Task.Delay(1).ConfigureAwait(false);
        
        var throughput = entityCount / (result.ElapsedMilliseconds / 1000.0);
        var avgTimePerEntity = result.ElapsedMilliseconds / entityCount;
        
        _output.WriteLine($"=== BASELINE SAVED: {testName} ===");
        _output.WriteLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        _output.WriteLine($"Entity Count: {entityCount:N0}");
        _output.WriteLine($"Total Execution Time: {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Average Time per Entity: {avgTimePerEntity:F2}ms");
        _output.WriteLine($"Throughput: {throughput:F1} entities/second");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
        _output.WriteLine($"Memory per Entity: {result.MemoryUsed / (double)entityCount:F1} bytes/entity");
        _output.WriteLine($"Start: {result.StartTime:HH:mm:ss.fff}");
        _output.WriteLine($"End: {result.EndTime:HH:mm:ss.fff}");
        _output.WriteLine("=====================================");
        
        // Future enhancement: Save to structured format for historical comparison
        // var baselineData = new 
        // {
        //     TestName = testName,
        //     Timestamp = DateTime.UtcNow,
        //     EntityCount = entityCount,
        //     Performance = result,
        //     Throughput = throughput,
        //     AvgTimePerEntity = avgTimePerEntity
        // };
        // await File.WriteAllTextAsync($"baselines/{testName}.json", JsonSerializer.Serialize(baselineData));
    }

    #endregion
} 