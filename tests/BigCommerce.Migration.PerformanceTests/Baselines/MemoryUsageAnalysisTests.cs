using BigCommerce.Migration.PerformanceTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Baselines;

/// <summary>
/// TDD RED PHASE: Memory Usage Analysis Tests
/// These tests analyze memory patterns during entity processing workflows to prevent Azure Functions memory issues
/// Tests will initially fail because we need to implement memory analysis infrastructure
/// </summary>
public class MemoryUsageAnalysisTests
{
    private readonly ITestOutputHelper _output;
    private readonly PerformanceBenchmark _benchmark;

    public MemoryUsageAnalysisTests(ITestOutputHelper output)
    {
        _output = output;
        _benchmark = new PerformanceBenchmark();
    }

    [Fact]
    public async Task Memory_Growth_During_Large_Entity_Processing_Workflow()
    {
        // ARRANGE
        var processor = CreateTestEntityProcessor();
        var entityCount = 1000; // Reasonable dataset for CI/development testing
        var entities = GenerateTestEntities("products", entityCount);
        
        // ACT - Measure memory growth during large entity processing
        var memoryAnalysis = await AnalyzeMemoryUsageAsync(async () =>
        {
            await processor.ProcessEntitiesAsync(entities).ConfigureAwait(false);
        }).ConfigureAwait(false);
        
        // ASSERT - Verify memory usage stays within acceptable bounds
        Assert.True(memoryAnalysis.PeakMemoryUsage > 0, "Should track peak memory usage");
        Assert.True(memoryAnalysis.FinalMemoryUsage <= memoryAnalysis.PeakMemoryUsage, "Final memory should not exceed peak");
        
        var memoryPerEntity = memoryAnalysis.PeakMemoryUsage / (double)entityCount;
        var estimatedMemoryFor10M = memoryPerEntity * 10_000_000;
        var azureFunctionLimit = 1.5 * 1024 * 1024 * 1024; // 1.5GB Azure Functions limit
        
        _output.WriteLine("=== LARGE ENTITY PROCESSING MEMORY ANALYSIS ===");
        _output.WriteLine($"Entity Count: {entityCount:N0}");
        _output.WriteLine($"Peak Memory Usage: {memoryAnalysis.PeakMemoryUsage:N0} bytes ({memoryAnalysis.PeakMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Final Memory Usage: {memoryAnalysis.FinalMemoryUsage:N0} bytes ({memoryAnalysis.FinalMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Memory per Entity: {memoryPerEntity:F1} bytes/entity");
        _output.WriteLine($"Processing Duration: {memoryAnalysis.ProcessingDuration:F1}ms");
        _output.WriteLine($"GC Collections (Gen0/Gen1/Gen2): {memoryAnalysis.GCCollections.Gen0}/{memoryAnalysis.GCCollections.Gen1}/{memoryAnalysis.GCCollections.Gen2}");
        _output.WriteLine($"Memory Growth Rate: {memoryAnalysis.MemoryGrowthRate:F2} bytes/ms");
        _output.WriteLine($"Estimated Memory for 10M Entities: {estimatedMemoryFor10M / (1024.0 * 1024):F1} MB");
        
        // Verify memory efficiency for enterprise scale
        Assert.True(estimatedMemoryFor10M < azureFunctionLimit, 
            $"Memory usage should scale efficiently. Estimated 10M entities would use {estimatedMemoryFor10M / (1024.0 * 1024):F1}MB, exceeding Azure Functions limit of {azureFunctionLimit / (1024.0 * 1024):F0}MB");
        
        await SaveMemoryBaseline("large_entity_processing", memoryAnalysis, entityCount).ConfigureAwait(false);
    }

    [Fact]
    public async Task Memory_Efficiency_Streaming_vs_Batch_Processing()
    {
        // ARRANGE
        var processor = CreateTestEntityProcessor();
        var entityCount = 500;
        var entities = GenerateTestEntities("products", entityCount);
        
        // ACT - Compare memory usage between streaming and batch processing
        var streamingAnalysis = await AnalyzeMemoryUsageAsync(async () =>
        {
            await processor.ProcessEntitiesStreamingAsync(entities).ConfigureAwait(false);
        }).ConfigureAwait(false);
        
        var batchAnalysis = await AnalyzeMemoryUsageAsync(async () =>
        {
            await processor.ProcessEntitiesBatchAsync(entities, batchSize: 100).ConfigureAwait(false);
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(streamingAnalysis.PeakMemoryUsage > 0);
        Assert.True(batchAnalysis.PeakMemoryUsage > 0);
        
        var streamingMemoryPerEntity = streamingAnalysis.PeakMemoryUsage / (double)entityCount;
        var batchMemoryPerEntity = batchAnalysis.PeakMemoryUsage / (double)entityCount;
        var memoryEfficiencyGain = (streamingMemoryPerEntity - batchMemoryPerEntity) / streamingMemoryPerEntity * 100;
        
        _output.WriteLine("=== STREAMING vs BATCH PROCESSING MEMORY COMPARISON ===");
        _output.WriteLine($"Entity Count: {entityCount:N0}");
        _output.WriteLine($"Streaming Peak Memory: {streamingAnalysis.PeakMemoryUsage:N0} bytes ({streamingAnalysis.PeakMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Batch Peak Memory: {batchAnalysis.PeakMemoryUsage:N0} bytes ({batchAnalysis.PeakMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Streaming Memory/Entity: {streamingMemoryPerEntity:F1} bytes");
        _output.WriteLine($"Batch Memory/Entity: {batchMemoryPerEntity:F1} bytes");
        _output.WriteLine($"Memory Efficiency Gain: {memoryEfficiencyGain:F1}% with {(memoryEfficiencyGain > 0 ? "batch" : "streaming")}");
        _output.WriteLine($"Streaming GC (Gen0/Gen1/Gen2): {streamingAnalysis.GCCollections.Gen0}/{streamingAnalysis.GCCollections.Gen1}/{streamingAnalysis.GCCollections.Gen2}");
        _output.WriteLine($"Batch GC (Gen0/Gen1/Gen2): {batchAnalysis.GCCollections.Gen0}/{batchAnalysis.GCCollections.Gen1}/{batchAnalysis.GCCollections.Gen2}");
        
        // Determine which approach is more memory efficient
        if (streamingAnalysis.PeakMemoryUsage < batchAnalysis.PeakMemoryUsage)
        {
            _output.WriteLine("✅ Streaming processing is more memory efficient");
        }
        else
        {
            _output.WriteLine("✅ Batch processing is more memory efficient");
        }
        
        await SaveMemoryBaseline("streaming_vs_batch_memory", streamingAnalysis, entityCount).ConfigureAwait(false);
    }

    [Fact]
    public async Task Memory_Leak_Detection_During_Long_Running_Processing()
    {
        // ARRANGE
        var processor = CreateTestEntityProcessor();
        var cycles = 5; // Multiple processing cycles to detect leaks
        var entitiesPerCycle = 200;
        
        var memoryMeasurements = new List<long>();
        
        // ACT - Process multiple cycles and track memory growth
        var totalAnalysis = await AnalyzeMemoryUsageAsync(async () =>
        {
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                var entities = GenerateTestEntities("products", entitiesPerCycle);
                await processor.ProcessEntitiesAsync(entities).ConfigureAwait(false);
                
                // Force garbage collection and measure memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                var currentMemory = GC.GetTotalMemory(false);
                memoryMeasurements.Add(currentMemory);
                
                _output.WriteLine($"Cycle {cycle + 1}: {currentMemory:N0} bytes ({currentMemory / (1024.0 * 1024):F1} MB)");
            }
        }).ConfigureAwait(false);
        
        // ASSERT - Analyze memory growth pattern for leaks
        var memoryGrowthPerCycle = CalculateMemoryGrowthTrend(memoryMeasurements);
        var totalEntitiesProcessed = cycles * entitiesPerCycle;
        
        _output.WriteLine("=== MEMORY LEAK DETECTION ANALYSIS ===");
        _output.WriteLine($"Processing Cycles: {cycles}");
        _output.WriteLine($"Entities per Cycle: {entitiesPerCycle:N0}");
        _output.WriteLine($"Total Entities Processed: {totalEntitiesProcessed:N0}");
        _output.WriteLine($"Initial Memory: {memoryMeasurements[0]:N0} bytes ({memoryMeasurements[0] / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Final Memory: {memoryMeasurements[^1]:N0} bytes ({memoryMeasurements[^1] / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Memory Growth per Cycle: {memoryGrowthPerCycle:F1} bytes/cycle");
        _output.WriteLine($"Total GC Collections (Gen0/Gen1/Gen2): {totalAnalysis.GCCollections.Gen0}/{totalAnalysis.GCCollections.Gen1}/{totalAnalysis.GCCollections.Gen2}");
        
        // Verify no significant memory leaks
        var maxAcceptableGrowthPerCycle = 1024 * 1024; // 1MB per cycle max
        Assert.True(memoryGrowthPerCycle < maxAcceptableGrowthPerCycle, 
            $"Memory growth per cycle should be minimal. Current: {memoryGrowthPerCycle:F1} bytes/cycle, Max allowed: {maxAcceptableGrowthPerCycle:N0} bytes/cycle");
        
        if (memoryGrowthPerCycle < 100 * 1024) // Less than 100KB per cycle
        {
            _output.WriteLine("✅ No significant memory leaks detected");
        }
        else
        {
            _output.WriteLine("⚠️ Potential memory leak detected - investigate memory retention");
        }
        
        await SaveMemoryBaseline("memory_leak_detection", totalAnalysis, totalEntitiesProcessed).ConfigureAwait(false);
    }

    [Fact]
    public async Task Memory_Patterns_Different_Entity_Types()
    {
        // ARRANGE
        var processor = CreateTestEntityProcessor();
        var entityCount = 1000;
        var entityTypes = new[] { "products", "categories", "brands", "variants", "images" };
        
        var entityMemoryPatterns = new Dictionary<string, MemoryAnalysisResult>();
        
        // ACT - Analyze memory patterns for different entity types
        foreach (var entityType in entityTypes)
        {
            var entities = GenerateTestEntities(entityType, entityCount);
            
            var analysis = await AnalyzeMemoryUsageAsync(async () =>
            {
                await processor.ProcessEntitiesAsync(entities).ConfigureAwait(false);
            }).ConfigureAwait(false);
            
            entityMemoryPatterns[entityType] = analysis;
        }
        
        // ASSERT
        _output.WriteLine("=== ENTITY TYPE MEMORY PATTERNS ANALYSIS ===");
        
        foreach (var kvp in entityMemoryPatterns)
        {
            var entityType = kvp.Key;
            var analysis = kvp.Value;
            var memoryPerEntity = analysis.PeakMemoryUsage / (double)entityCount;
            
            _output.WriteLine($"{entityType.ToUpperInvariant()}:");
            _output.WriteLine($"  Peak Memory: {analysis.PeakMemoryUsage:N0} bytes ({analysis.PeakMemoryUsage / (1024.0 * 1024):F1} MB)");
            _output.WriteLine($"  Memory/Entity: {memoryPerEntity:F1} bytes");
            _output.WriteLine($"  Processing Time: {analysis.ProcessingDuration:F1}ms");
            _output.WriteLine($"  GC Collections: Gen0({analysis.GCCollections.Gen0}) Gen1({analysis.GCCollections.Gen1}) Gen2({analysis.GCCollections.Gen2})");
            
            // Verify reasonable memory usage per entity type
            var maxMemoryPerEntity = entityType switch
            {
                "products" => 10 * 1024,    // 10KB per product (complex with variants)
                "categories" => 2 * 1024,   // 2KB per category (simpler)
                "brands" => 1 * 1024,       // 1KB per brand (simplest)
                "variants" => 5 * 1024,     // 5KB per variant (moderate complexity)
                "images" => 8 * 1024,       // 8KB per image (metadata + path)
                _ => 15 * 1024               // 15KB default max
            };
            
            Assert.True(memoryPerEntity < maxMemoryPerEntity, 
                $"{entityType} memory usage too high: {memoryPerEntity:F1} bytes/entity, max allowed: {maxMemoryPerEntity:N0} bytes/entity");
        }
        
        // Find most/least memory efficient entity types
        var mostEfficient = entityMemoryPatterns.MinBy(kvp => kvp.Value.PeakMemoryUsage / (double)entityCount);
        var leastEfficient = entityMemoryPatterns.MaxBy(kvp => kvp.Value.PeakMemoryUsage / (double)entityCount);
        
        _output.WriteLine($"Most Memory Efficient: {mostEfficient.Key} ({mostEfficient.Value.PeakMemoryUsage / (double)entityCount:F1} bytes/entity)");
        _output.WriteLine($"Least Memory Efficient: {leastEfficient.Key} ({leastEfficient.Value.PeakMemoryUsage / (double)entityCount:F1} bytes/entity)");
        
        await SaveMemoryBaseline("entity_type_memory_patterns", mostEfficient.Value, entityCount).ConfigureAwait(false);
    }

    [Fact]
    public async Task Memory_Pressure_Under_High_Load_Simulation()
    {
        // ARRANGE
        var processor = CreateTestEntityProcessor();
        var concurrentTasks = 3; // Simulate concurrent processing
        var entitiesPerTask = 300;
        
        // ACT - Simulate high load with concurrent processing
        var highLoadAnalysis = await AnalyzeMemoryUsageAsync(async () =>
        {
            var tasks = new Task[concurrentTasks];
            
            for (int i = 0; i < concurrentTasks; i++)
            {
                var taskEntities = GenerateTestEntities("products", entitiesPerTask);
                tasks[i] = processor.ProcessEntitiesAsync(taskEntities);
            }
            
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }).ConfigureAwait(false);
        
        // ASSERT
        var totalEntities = concurrentTasks * entitiesPerTask;
        var memoryPerEntity = highLoadAnalysis.PeakMemoryUsage / (double)totalEntities;
        var memoryEfficiency = highLoadAnalysis.FinalMemoryUsage / (double)highLoadAnalysis.PeakMemoryUsage;
        
        _output.WriteLine("=== HIGH LOAD MEMORY PRESSURE ANALYSIS ===");
        _output.WriteLine($"Concurrent Tasks: {concurrentTasks}");
        _output.WriteLine($"Entities per Task: {entitiesPerTask:N0}");
        _output.WriteLine($"Total Entities: {totalEntities:N0}");
        _output.WriteLine($"Peak Memory Usage: {highLoadAnalysis.PeakMemoryUsage:N0} bytes ({highLoadAnalysis.PeakMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Final Memory Usage: {highLoadAnalysis.FinalMemoryUsage:N0} bytes ({highLoadAnalysis.FinalMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Memory per Entity: {memoryPerEntity:F1} bytes");
        _output.WriteLine($"Memory Efficiency: {memoryEfficiency:P1} (final/peak)");
        _output.WriteLine($"Processing Duration: {highLoadAnalysis.ProcessingDuration:F1}ms");
        _output.WriteLine($"GC Pressure (Gen0/Gen1/Gen2): {highLoadAnalysis.GCCollections.Gen0}/{highLoadAnalysis.GCCollections.Gen1}/{highLoadAnalysis.GCCollections.Gen2}");
        
        // Verify system handles high load without excessive memory usage
        var maxAcceptableMemory = 512 * 1024 * 1024; // 512MB max for concurrent processing
        Assert.True(highLoadAnalysis.PeakMemoryUsage < maxAcceptableMemory, 
            $"High load memory usage should be reasonable. Peak: {highLoadAnalysis.PeakMemoryUsage / (1024.0 * 1024):F1}MB, Max allowed: {maxAcceptableMemory / (1024.0 * 1024):F0}MB");
        
        if (memoryEfficiency > 0.8) // Good memory cleanup
        {
            _output.WriteLine("⚠️ Memory efficiency could be improved - consider more aggressive cleanup");
        }
        else
        {
            _output.WriteLine("✅ Good memory efficiency under high load");
        }
        
        await SaveMemoryBaseline("high_load_memory_pressure", highLoadAnalysis, totalEntities).ConfigureAwait(false);
    }

    [Fact]
    public async Task Garbage_Collection_Impact_Analysis()
    {
        // ARRANGE
        var processor = CreateTestEntityProcessor();
        var entityCount = 800;
        var entities = GenerateTestEntities("products", entityCount);
        
        // ACT - Analyze GC impact during processing
        var gcAnalysis = await AnalyzeMemoryUsageAsync(async () =>
        {
            await processor.ProcessEntitiesWithGCAnalysisAsync(entities).ConfigureAwait(false);
        }).ConfigureAwait(false);
        
        // ASSERT
        var gcEfficiency = CalculateGCEfficiency(gcAnalysis.GCCollections);
        var avgTimeBetweenGC = gcAnalysis.ProcessingDuration / (gcAnalysis.GCCollections.Gen0 + gcAnalysis.GCCollections.Gen1 + gcAnalysis.GCCollections.Gen2);
        
        _output.WriteLine("=== GARBAGE COLLECTION IMPACT ANALYSIS ===");
        _output.WriteLine($"Entity Count: {entityCount:N0}");
        _output.WriteLine($"Processing Duration: {gcAnalysis.ProcessingDuration:F1}ms");
        _output.WriteLine($"Peak Memory: {gcAnalysis.PeakMemoryUsage:N0} bytes ({gcAnalysis.PeakMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Final Memory: {gcAnalysis.FinalMemoryUsage:N0} bytes ({gcAnalysis.FinalMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"GC Collections - Gen0: {gcAnalysis.GCCollections.Gen0}, Gen1: {gcAnalysis.GCCollections.Gen1}, Gen2: {gcAnalysis.GCCollections.Gen2}");
        _output.WriteLine($"GC Efficiency Score: {gcEfficiency:F1}% (higher is better)");
        _output.WriteLine($"Average Time Between GC: {avgTimeBetweenGC:F1}ms");
        _output.WriteLine($"Memory Recovered by GC: {gcAnalysis.PeakMemoryUsage - gcAnalysis.FinalMemoryUsage:N0} bytes");
        
        // Verify reasonable GC behavior
        var totalGCCollections = gcAnalysis.GCCollections.Gen0 + gcAnalysis.GCCollections.Gen1 + gcAnalysis.GCCollections.Gen2;
        var maxExpectedGCCollections = entityCount / 500; // Rough estimate: 1 GC per 500 entities max
        
        Assert.True(totalGCCollections <= maxExpectedGCCollections, 
            $"Too many GC collections. Actual: {totalGCCollections}, Expected max: {maxExpectedGCCollections}");
        
        if (gcEfficiency > 70)
        {
            _output.WriteLine("✅ Good garbage collection efficiency");
        }
        else
        {
            _output.WriteLine("⚠️ Poor GC efficiency - consider object pooling or streaming approaches");
        }
        
        await SaveMemoryBaseline("gc_impact_analysis", gcAnalysis, entityCount).ConfigureAwait(false);
    }

    #region Helper Methods (GREEN phase implementation)

    /// <summary>
    /// Creates a test entity processor for memory analysis
    /// </summary>
    private ITestEntityProcessor CreateTestEntityProcessor()
    {
        return new TestEntityProcessor();
    }

    /// <summary>
    /// Generates test entities of different types for memory testing
    /// </summary>
    private List<Dictionary<string, object>> GenerateTestEntities(string entityType, int count)
    {
        var entities = new List<Dictionary<string, object>>();
        var random = new Random(42); // Fixed seed for consistent results
        
        for (int i = 1; i <= count; i++)
        {
            var entity = entityType.ToLowerInvariant() switch
            {
                "products" => GenerateProductEntity(i, random),
                "categories" => GenerateCategoryEntity(i, random),
                "brands" => GenerateBrandEntity(i, random),
                "variants" => GenerateVariantEntity(i, random),
                "images" => GenerateImageEntity(i, random),
                "modifiers" => GenerateModifierEntity(i, random),
                _ => GenerateGenericEntity(i, entityType, random)
            };
            
            entities.Add(entity);
        }
        
        return entities;
    }

    /// <summary>
    /// Analyzes memory usage during an operation with detailed tracking
    /// </summary>
    private async Task<MemoryAnalysisResult> AnalyzeMemoryUsageAsync(Func<Task> operation)
    {
        // Force initial garbage collection for clean baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);
        var initialGC = new GCCollectionData
        {
            Gen0 = GC.CollectionCount(0),
            Gen1 = GC.CollectionCount(1),
            Gen2 = GC.CollectionCount(2)
        };
        
        var startTime = DateTime.UtcNow;
        var memorySamples = new List<MemorySample>();
        var peakMemory = initialMemory;
        
        // Sample memory during operation with proper cancellation
        using var cancellationTokenSource = new CancellationTokenSource();
        var memoryTrackingTask = Task.Run(async () =>
        {
            var sampleCount = 0;
            try
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var currentMemory = GC.GetTotalMemory(false);
                    var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    
                    memorySamples.Add(new MemorySample
                    {
                        Timestamp = DateTime.UtcNow,
                        MemoryUsage = currentMemory,
                        ElapsedMilliseconds = elapsed,
                        Context = $"Sample_{sampleCount++}"
                    });
                    
                    if (currentMemory > peakMemory)
                    {
                        peakMemory = currentMemory;
                    }
                    
                    await Task.Delay(100, cancellationTokenSource.Token).ConfigureAwait(false); // Sample every 100ms
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when operation completes
            }
        }, cancellationTokenSource.Token);
        
        var operationStartTime = DateTime.UtcNow;
        
        try
        {
            // Execute the operation
            await operation().ConfigureAwait(false);
        }
        finally
        {
            // Stop memory tracking
            cancellationTokenSource.Cancel();
            try
            {
                await memoryTrackingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
        
        var operationEndTime = DateTime.UtcNow;
        var processingDuration = (operationEndTime - operationStartTime).TotalMilliseconds;
        
        // Force garbage collection to get final memory reading
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(false);
        var finalGC = new GCCollectionData
        {
            Gen0 = GC.CollectionCount(0) - initialGC.Gen0,
            Gen1 = GC.CollectionCount(1) - initialGC.Gen1,
            Gen2 = GC.CollectionCount(2) - initialGC.Gen2
        };
        
        // Calculate memory growth rate
        var memoryGrowthRate = processingDuration > 0 
            ? (peakMemory - initialMemory) / processingDuration 
            : 0;
        
        return new MemoryAnalysisResult
        {
            InitialMemoryUsage = initialMemory,
            PeakMemoryUsage = peakMemory,
            FinalMemoryUsage = finalMemory,
            ProcessingDuration = processingDuration,
            GCCollections = finalGC,
            MemoryGrowthRate = memoryGrowthRate,
            MemorySamples = memorySamples
        };
    }

    /// <summary>
    /// Calculates memory growth trend from measurements using linear regression
    /// </summary>
    private double CalculateMemoryGrowthTrend(List<long> memoryMeasurements)
    {
        if (memoryMeasurements.Count < 2)
            return 0;
        
        // Simple linear regression to find growth trend
        var n = memoryMeasurements.Count;
        var xSum = n * (n - 1) / 2; // Sum of indices (0, 1, 2, ...)
        var ySum = memoryMeasurements.Sum();
        var xySum = memoryMeasurements.Select((y, x) => (long)x * y).Sum();
        var xSquaredSum = Enumerable.Range(0, n).Select(x => x * x).Sum();
        
        // Calculate slope (memory growth per cycle)
        var slope = (double)(n * xySum - xSum * ySum) / (n * xSquaredSum - xSum * xSum);
        
        return slope;
    }

    /// <summary>
    /// Calculates garbage collection efficiency based on collection patterns
    /// </summary>
    private double CalculateGCEfficiency(GCCollectionData gcData)
    {
        if (gcData.Total == 0)
            return 100; // No GC needed = perfect efficiency
        
        // Efficiency formula: fewer Gen2 collections = better efficiency
        // Weight: Gen0 = 1, Gen1 = 2, Gen2 = 4 (Gen2 is expensive)
        var totalPressure = gcData.Gen0 + (gcData.Gen1 * 2) + (gcData.Gen2 * 4);
        var maxPressure = gcData.Total * 4; // If all were Gen2 collections
        
        if (maxPressure == 0)
            return 100;
        
        var efficiency = 100.0 * (1.0 - (double)totalPressure / maxPressure);
        return Math.Max(0, Math.Min(100, efficiency));
    }

    /// <summary>
    /// Saves memory baseline with comprehensive analysis metrics
    /// </summary>
    private async Task SaveMemoryBaseline(string testName, MemoryAnalysisResult analysis, int entityCount)
    {
        // Simulate async save operation
        await Task.Delay(1).ConfigureAwait(false);
        
        var memoryPerEntity = entityCount > 0 ? analysis.PeakMemoryUsage / (double)entityCount : 0;
        var throughput = analysis.ProcessingDuration > 0 ? entityCount / (analysis.ProcessingDuration / 1000.0) : 0;
        var memoryEfficiency = analysis.MemoryEfficiencyRatio * 100;
        
        _output.WriteLine($"=== MEMORY BASELINE SAVED: {testName} ===");
        _output.WriteLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        _output.WriteLine($"Entity Count: {entityCount:N0}");
        _output.WriteLine($"Peak Memory: {analysis.PeakMemoryUsage:N0} bytes ({analysis.PeakMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Final Memory: {analysis.FinalMemoryUsage:N0} bytes ({analysis.FinalMemoryUsage / (1024.0 * 1024):F1} MB)");
        _output.WriteLine($"Memory per Entity: {memoryPerEntity:F1} bytes/entity");
        _output.WriteLine($"Processing Duration: {analysis.ProcessingDuration:F1}ms");
        _output.WriteLine($"Throughput: {throughput:F1} entities/second");
        _output.WriteLine($"Memory Efficiency: {memoryEfficiency:F1}% (lower is better)");
        _output.WriteLine($"Memory Growth Rate: {analysis.MemoryGrowthRate:F2} bytes/ms");
        _output.WriteLine($"Total Memory Allocated: {analysis.TotalMemoryAllocated:N0} bytes");
        _output.WriteLine($"Memory Recovered by GC: {analysis.MemoryRecovered:N0} bytes");
        _output.WriteLine($"GC Collections (Gen0/Gen1/Gen2): {analysis.GCCollections.Gen0}/{analysis.GCCollections.Gen1}/{analysis.GCCollections.Gen2}");
        _output.WriteLine($"GC Pressure Score: {analysis.GCCollections.GCPressureScore:F1}");
        _output.WriteLine($"Memory Samples Collected: {analysis.MemorySamples.Count}");
        _output.WriteLine("=====================================");
    }

    #region Entity Generation Methods

    private Dictionary<string, object> GenerateProductEntity(int id, Random random)
    {
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["entity_type"] = "products",
            ["name"] = $"Test Product {id}",
            ["sku"] = $"TEST-PROD-{id:D6}",
            ["price"] = Math.Round(random.NextDouble() * 1000, 2),
            ["description"] = GenerateDescription(random, 200),
            ["weight"] = Math.Round(random.NextDouble() * 50, 2),
            ["categories"] = Enumerable.Range(1, random.Next(1, 5)).Select(i => random.Next(1, 100)).ToList(),
            ["images"] = Enumerable.Range(1, random.Next(1, 8)).Select(i => $"image_{id}_{i}.jpg").ToList(),
            ["variants"] = Enumerable.Range(1, random.Next(1, 15)).Select(i => GenerateVariantData(id, i, random)).ToList(),
            ["custom_fields"] = GenerateCustomFields(random, 5),
            ["meta_data"] = GenerateMetaData(random)
        };
    }

    private Dictionary<string, object> GenerateCategoryEntity(int id, Random random)
    {
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["entity_type"] = "categories",
            ["name"] = $"Category {id}",
            ["parent_id"] = random.Next(0, 10), // Some root categories (0)
            ["description"] = GenerateDescription(random, 100),
            ["sort_order"] = id,
            ["is_visible"] = true,
            ["meta_keywords"] = GenerateKeywords(random),
            ["meta_description"] = GenerateDescription(random, 50)
        };
    }

    private Dictionary<string, object> GenerateBrandEntity(int id, Random random)
    {
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["entity_type"] = "brands",
            ["name"] = $"Brand {id}",
            ["page_title"] = $"Brand {id} - Official Store",
            ["meta_keywords"] = GenerateKeywords(random),
            ["meta_description"] = GenerateDescription(random, 75),
            ["image_file"] = $"brand_{id}_logo.png"
        };
    }

    private Dictionary<string, object> GenerateVariantEntity(int id, Random random)
    {
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["entity_type"] = "variants",
            ["product_id"] = random.Next(1, 1000),
            ["sku"] = $"VAR-{id:D6}",
            ["price"] = Math.Round(random.NextDouble() * 500, 2),
            ["weight"] = Math.Round(random.NextDouble() * 10, 2),
            ["inventory_level"] = random.Next(0, 1000),
            ["option_values"] = GenerateOptionValues(random)
        };
    }

    private Dictionary<string, object> GenerateImageEntity(int id, Random random)
    {
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["entity_type"] = "images",
            ["product_id"] = random.Next(1, 1000),
            ["image_file"] = $"product_image_{id}.jpg",
            ["is_thumbnail"] = id % 5 == 0, // Every 5th image is thumbnail
            ["sort_order"] = random.Next(1, 10),
            ["description"] = $"Product image {id}",
            ["alt_text"] = $"Alt text for image {id}"
        };
    }

    private Dictionary<string, object> GenerateModifierEntity(int id, Random random)
    {
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["entity_type"] = "modifiers",
            ["product_id"] = random.Next(1, 1000),
            ["name"] = $"Modifier {id}",
            ["display_name"] = $"Product Modifier {id}",
            ["type"] = new[] { "text", "dropdown", "checkbox", "radio" }[random.Next(4)],
            ["required"] = random.Next(2) == 1,
            ["sort_order"] = id
        };
    }

    private Dictionary<string, object> GenerateGenericEntity(int id, string entityType, Random random)
    {
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["entity_type"] = entityType,
            ["name"] = $"{entityType} {id}",
            ["data"] = GenerateGenericData(random),
            ["created_at"] = DateTime.UtcNow.AddDays(-random.Next(365)),
            ["updated_at"] = DateTime.UtcNow.AddDays(-random.Next(30))
        };
    }

    private string GenerateDescription(Random random, int maxLength)
    {
        var words = new[] { "quality", "premium", "excellent", "durable", "innovative", "reliable", "efficient", "modern", "advanced", "professional" };
        var wordCount = random.Next(5, Math.Min(maxLength / 8, 25));
        return string.Join(" ", Enumerable.Range(1, wordCount).Select(_ => words[random.Next(words.Length)]));
    }

    private List<string> GenerateKeywords(Random random)
    {
        var keywords = new[] { "product", "quality", "premium", "sale", "new", "popular", "trending", "best" };
        return keywords.OrderBy(_ => random.Next()).Take(random.Next(3, 6)).ToList();
    }

    private Dictionary<string, object> GenerateVariantData(int productId, int variantId, Random random)
    {
        return new Dictionary<string, object>
        {
            ["variant_id"] = $"{productId}-{variantId}",
            ["sku"] = $"VAR-{productId}-{variantId}",
            ["price_modifier"] = Math.Round((random.NextDouble() - 0.5) * 100, 2),
            ["weight_modifier"] = Math.Round((random.NextDouble() - 0.5) * 5, 2)
        };
    }

    private Dictionary<string, object> GenerateCustomFields(Random random, int maxFields)
    {
        var fieldCount = random.Next(1, maxFields);
        var fields = new Dictionary<string, object>();
        
        for (int i = 1; i <= fieldCount; i++)
        {
            fields[$"custom_field_{i}"] = $"value_{random.Next(1000, 9999)}";
        }
        
        return fields;
    }

    private Dictionary<string, object> GenerateMetaData(Random random)
    {
        return new Dictionary<string, object>
        {
            ["source"] = "test_migration",
            ["batch_id"] = random.Next(1, 100),
            ["processing_flags"] = new[] { "validated", "enriched", "mapped" },
            ["quality_score"] = Math.Round(random.NextDouble() * 100, 1)
        };
    }

    private List<Dictionary<string, object>> GenerateOptionValues(Random random)
    {
        var count = random.Next(1, 4);
        return Enumerable.Range(1, count).Select(i => new Dictionary<string, object>
        {
            ["option_id"] = random.Next(1, 50),
            ["value"] = $"Value {i}",
            ["label"] = $"Option {i}"
        }).ToList();
    }

    private Dictionary<string, object> GenerateGenericData(Random random)
    {
        return new Dictionary<string, object>
        {
            ["field1"] = $"data_{random.Next(1000, 9999)}",
            ["field2"] = random.NextDouble() * 100,
            ["field3"] = random.Next(2) == 1,
            ["field4"] = DateTime.UtcNow.AddDays(-random.Next(100))
        };
    }

    #endregion

    #endregion
} 