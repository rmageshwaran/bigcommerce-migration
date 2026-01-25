using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BigCommerce.Migration.Task51Performance;

/// <summary>
/// Task 5.1: Performance Testing Validation Demo
/// 
/// Standalone performance validation program to verify that the incremental progress system
/// meets the critical performance requirements without test compilation dependencies.
/// 
/// Key Performance Requirements:
/// 1. Database write performance < 100ms average
/// 2. Aggregation queries < 2 seconds for large datasets
/// 3. Concurrent operations maintain performance
/// 4. Memory usage stays within reasonable limits
/// 5. Critical requirement: <10% overhead
/// </summary>
class Program
{
    private static IIncrementEventsService? _incrementEventsService;
    private static ILogger<Program>? _logger;
    private static readonly List<string> _testMigrationIds = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("🧪 Task 5.1: Performance Testing Validation");
        Console.WriteLine("============================================");

        // Initialize services
        InitializeServices();

        try
        {
            // Run all performance tests
            await RunDatabaseWritePerformanceTest();
            await RunAggregationPerformanceTest();
            await RunConcurrentWriteTest();
            await RunMemoryUsageTest();
            await RunOverheadValidationTest();
            await RunScalabilityTest();

            Console.WriteLine("\n🎉 All Task 5.1 Performance Tests Completed Successfully!");
            Console.WriteLine("✅ System meets all performance requirements for production deployment.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Performance test failed: {ex.Message}");
            Environment.Exit(1);
        }
        finally
        {
            await CleanupTestData();
        }
    }

    private static void InitializeServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true"
            }!)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        var incrementLogger = loggerFactory.CreateLogger<IncrementEventsService>();
        _logger = loggerFactory.CreateLogger<Program>();

        _incrementEventsService = new IncrementEventsService(configuration, incrementLogger);
        
        Console.WriteLine("✅ Services initialized successfully");
    }

    #region Task 5.1.1: Database Write Performance

    private static async Task RunDatabaseWritePerformanceTest()
    {
        Console.WriteLine("\n🧪 Task 5.1.1: Database Write Performance Test");
        
        var migrationId = $"db-write-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        var stopwatch = Stopwatch.StartNew();
        var writeTimes = new List<long>();

        // Test 100 rapid database writes
        for (int i = 1; i <= 100; i++)
        {
            var chunkStopwatch = Stopwatch.StartNew();
            
            var startTime = DateTime.UtcNow.AddSeconds(-1);
            var endTime = DateTime.UtcNow;
            
            var chunk = ChunkIncrementEvent.Create(
                migrationId: migrationId,
                entityType: "products",
                chunkNumber: i,
                chunkStartIndex: (i - 1) * 100,
                chunkSize: 100,
                successfulEntities: 100,
                failedEntities: 2,
                skippedEntities: 1,
                cancelledEntities: 0,
                processingStartTime: startTime,
                processingEndTime: endTime,
                sourceStore: "test-source-store",
                destinationStore: "test-dest-store",
                errors: null
            );

            await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
            chunkStopwatch.Stop();
            writeTimes.Add(chunkStopwatch.ElapsedMilliseconds);

            if (i % 20 == 0)
            {
                Console.WriteLine($"   📝 Completed {i}/100 writes...");
            }
        }

        stopwatch.Stop();

        // Analyze performance
        var totalTime = stopwatch.ElapsedMilliseconds;
        var averageWriteTime = writeTimes.Average();
        var maxWriteTime = writeTimes.Max();
        var minWriteTime = writeTimes.Min();

        Console.WriteLine($"   📊 Results:");
        Console.WriteLine($"      Total Time: {totalTime}ms");
        Console.WriteLine($"      Average Write: {averageWriteTime:F2}ms");
        Console.WriteLine($"      Max Write: {maxWriteTime}ms");
        Console.WriteLine($"      Min Write: {minWriteTime}ms");

        // Validate performance targets
        if (averageWriteTime >= 100)
            throw new Exception($"❌ FAIL: Average write time {averageWriteTime:F2}ms exceeds 100ms target");

        if (maxWriteTime >= 500)
            throw new Exception($"❌ FAIL: Max write time {maxWriteTime}ms exceeds 500ms target");

        if (totalTime >= 15000)
            throw new Exception($"❌ FAIL: Total time {totalTime}ms exceeds 15s target");

        Console.WriteLine("   ✅ PASS: Database write performance meets all targets");
    }

    #endregion

    #region Task 5.1.2: Aggregation Performance

    private static async Task RunAggregationPerformanceTest()
    {
        Console.WriteLine("\n🧪 Task 5.1.2: Aggregation Performance Test");
        
        var migrationId = $"aggregation-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        // Create 200 chunks across multiple entity types
        var allChunks = new List<ChunkIncrementEvent>();
        allChunks.AddRange(CreateTestChunks(migrationId, "products", 100, 50));
        allChunks.AddRange(CreateTestChunks(migrationId, "categories", 50, 50));
        allChunks.AddRange(CreateTestChunks(migrationId, "brands", 30, 50));
        allChunks.AddRange(CreateTestChunks(migrationId, "variants", 20, 50));

        Console.WriteLine($"   📝 Writing {allChunks.Count} chunks...");

        // Write all chunks
        foreach (var chunk in allChunks)
        {
            await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
        }

        Console.WriteLine("   📊 Testing aggregation query performance...");

        // Test aggregation performance
        var aggregationTimes = new List<long>();

        for (int i = 0; i < 10; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var progress = await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
            stopwatch.Stop();
            
            aggregationTimes.Add(stopwatch.ElapsedMilliseconds);
            
            // Verify aggregation accuracy
            if (progress.Count != 4)
                throw new Exception($"❌ FAIL: Expected 4 entity types, got {progress.Count}");

            if (progress.Values.Sum(p => p.TotalSuccessful) == 0)
                throw new Exception("❌ FAIL: No successful entities found in aggregation");
        }

        var averageAggregationTime = aggregationTimes.Average();
        var maxAggregationTime = aggregationTimes.Max();

        Console.WriteLine($"   📊 Results:");
        Console.WriteLine($"      Average Aggregation: {averageAggregationTime:F2}ms");
        Console.WriteLine($"      Max Aggregation: {maxAggregationTime}ms");
        Console.WriteLine($"      Chunks Processed: {allChunks.Count}");

        // Validate performance targets
        if (averageAggregationTime >= 2000)
            throw new Exception($"❌ FAIL: Average aggregation time {averageAggregationTime:F2}ms exceeds 2s target");

        if (maxAggregationTime >= 5000)
            throw new Exception($"❌ FAIL: Max aggregation time {maxAggregationTime}ms exceeds 5s target");

        Console.WriteLine("   ✅ PASS: Aggregation performance meets all targets");
    }

    #endregion

    #region Task 5.1.3: Concurrent Write Performance

    private static async Task RunConcurrentWriteTest()
    {
        Console.WriteLine("\n🧪 Task 5.1.3: Concurrent Write Performance Test");
        
        var migrationIds = Enumerable.Range(1, 5)
            .Select(i => $"concurrent-{i}-{Guid.NewGuid():N}")
            .ToList();
        
        _testMigrationIds.AddRange(migrationIds);

        Console.WriteLine("   📝 Running 5 concurrent migrations...");

        var stopwatch = Stopwatch.StartNew();

        var concurrentTasks = migrationIds.Select(async migrationId =>
        {
            var chunks = CreateTestChunks(migrationId, "products", 20, 100); // 20 chunks each
            
            foreach (var chunk in chunks)
            {
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
            }
            
            return await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
        });

        var results = await Task.WhenAll(concurrentTasks);
        stopwatch.Stop();

        var totalTime = stopwatch.ElapsedMilliseconds;
        
        Console.WriteLine($"   📊 Results:");
        Console.WriteLine($"      Total Time: {totalTime}ms");
        Console.WriteLine($"      Migrations: {results.Length}");

        // Validate performance and results
        if (totalTime >= 30000)
            throw new Exception($"❌ FAIL: Concurrent migrations took {totalTime}ms, exceeds 30s target");

        if (results.Length != 5)
            throw new Exception($"❌ FAIL: Expected 5 results, got {results.Length}");

        foreach (var progress in results)
        {
            if (progress.Values.Sum(p => p.TotalSuccessful) == 0)
                throw new Exception("❌ FAIL: Concurrent migration has no successful entities");
        }

        Console.WriteLine("   ✅ PASS: Concurrent write performance meets all targets");
    }

    #endregion

    #region Task 5.1.4: Memory Usage Test

    private static async Task RunMemoryUsageTest()
    {
        Console.WriteLine("\n🧪 Task 5.1.4: Memory Usage Test");
        
        var migrationId = $"memory-test-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        // Measure initial memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);
        Console.WriteLine($"   📊 Initial Memory: {initialMemory / (1024.0 * 1024.0):F2}MB");

        // Process many chunks
        var chunks = CreateTestChunks(migrationId, "products", 500, 100); // 500 chunks

        Console.WriteLine("   📝 Processing 500 chunks...");
        foreach (var chunk in chunks)
        {
            await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
        }

        Console.WriteLine("   📝 Running 50 aggregation queries...");
        for (int i = 0; i < 50; i++)
        {
            await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
        }

        // Measure final memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;
        var memoryIncreaseMB = memoryIncrease / (1024.0 * 1024.0);

        Console.WriteLine($"   📊 Results:");
        Console.WriteLine($"      Final Memory: {finalMemory / (1024.0 * 1024.0):F2}MB");
        Console.WriteLine($"      Memory Increase: {memoryIncreaseMB:F2}MB");

        // Validate memory usage
        if (memoryIncreaseMB >= 50)
            throw new Exception($"❌ FAIL: Memory increase {memoryIncreaseMB:F2}MB exceeds 50MB target");

        Console.WriteLine("   ✅ PASS: Memory usage within acceptable limits");
    }

    #endregion

    #region Task 5.1.5: Performance Overhead Validation

    private static async Task RunOverheadValidationTest()
    {
        Console.WriteLine("\n🧪 Task 5.1.5: Performance Overhead Validation (Critical <10% Requirement)");
        
        var migrationId = $"overhead-test-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        Console.WriteLine("   📝 Measuring baseline performance (no incremental progress)...");
        
        // Baseline - Simulate processing without incremental progress
        var baselineStopwatch = Stopwatch.StartNew();
        for (int i = 1; i <= 50; i++)
        {
            await Task.Delay(10); // Simulate entity processing time
        }
        baselineStopwatch.Stop();
        var baselineTime = baselineStopwatch.ElapsedMilliseconds;

        Console.WriteLine("   📝 Measuring performance with incremental progress...");

        // With incremental progress - Simulate processing with incremental updates
        var incrementalStopwatch = Stopwatch.StartNew();
        for (int i = 1; i <= 50; i++)
        {
            await Task.Delay(10); // Simulate entity processing time
            
            // Add incremental progress overhead
            var startTime = DateTime.UtcNow.AddSeconds(-1);
            var endTime = DateTime.UtcNow;
            
            var chunk = ChunkIncrementEvent.Create(
                migrationId: migrationId,
                entityType: "products",
                chunkNumber: i,
                chunkStartIndex: (i - 1) * 100,
                chunkSize: 100,
                successfulEntities: 96,
                failedEntities: 3,
                skippedEntities: 1,
                cancelledEntities: 0,
                processingStartTime: startTime,
                processingEndTime: endTime,
                sourceStore: "test-source-store",
                destinationStore: "test-dest-store",
                errors: null
            );
            
            await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
        }
        incrementalStopwatch.Stop();
        var incrementalTime = incrementalStopwatch.ElapsedMilliseconds;

        // Calculate overhead percentage
        var overhead = incrementalTime - baselineTime;
        var overheadPercentage = (overhead / (double)baselineTime) * 100.0;

        Console.WriteLine($"   📊 Results:");
        Console.WriteLine($"      Baseline Time: {baselineTime}ms");
        Console.WriteLine($"      Incremental Time: {incrementalTime}ms");
        Console.WriteLine($"      Overhead: {overhead}ms ({overheadPercentage:F2}%)");

        // Critical validation - <10% overhead requirement
        if (overheadPercentage >= 10.0)
            throw new Exception($"❌ CRITICAL FAIL: Performance overhead {overheadPercentage:F2}% exceeds 10% requirement!");

        if (overheadPercentage < 5.0)
        {
            Console.WriteLine("   🎉 EXCELLENT: Overhead is under 5% - exceeds performance requirements!");
        }

        Console.WriteLine("   ✅ PASS: Performance overhead meets critical <10% requirement");
    }

    #endregion

    #region Task 5.1.6: Scalability Test

    private static async Task RunScalabilityTest()
    {
        Console.WriteLine("\n🧪 Task 5.1.6: Scalability Test");
        
        var migrationId = $"scalability-test-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        Console.WriteLine("   📝 Simulating large scale migration (50 chunks, 25k entities)...");

        var chunks = CreateTestChunks(migrationId, "products", 50, 500); // 50 chunks, 25k entities

        var stopwatch = Stopwatch.StartNew();
        
        // Process chunks in batches to simulate real-world scenario
        var batchSize = 10;
        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize);
            var batchTasks = batch.Select(chunk => _incrementEventsService!.WriteChunkIncrementAsync(chunk));
            await Task.WhenAll(batchTasks);
            
            Console.WriteLine($"   📝 Processed batch {(i / batchSize) + 1}/{(chunks.Count + batchSize - 1) / batchSize}");
        }
        
        Console.WriteLine("   📝 Testing aggregation with large dataset...");
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        stopwatch.Stop();

        var processingTime = stopwatch.ElapsedMilliseconds;
        var totalEntities = progress.Values.Sum(p => p.TotalProcessed);
        
        Console.WriteLine($"   📊 Results:");
        Console.WriteLine($"      Processing Time: {processingTime}ms");
        Console.WriteLine($"      Total Entities: {totalEntities:N0}");
        Console.WriteLine($"      Throughput: {totalEntities / (processingTime / 1000.0):F0} entities/second");

        // Validate scalability
        if (processingTime >= 20000)
            throw new Exception($"❌ FAIL: Large scale processing took {processingTime}ms, exceeds 20s target");

        if (totalEntities < 20000)
            throw new Exception($"❌ FAIL: Expected >20k entities, got {totalEntities}");

        Console.WriteLine("   ✅ PASS: Scalability test meets performance targets");
    }

    #endregion

    #region Helper Methods

    private static List<ChunkIncrementEvent> CreateTestChunks(string migrationId, string entityType, int chunkCount, int entitiesPerChunk)
    {
        var chunks = new List<ChunkIncrementEvent>();
        
        for (int i = 1; i <= chunkCount; i++)
        {
            var startTime = DateTime.UtcNow.AddMinutes(-chunkCount + i);
            var endTime = startTime.AddSeconds(30); // 30 second processing time
            
            chunks.Add(ChunkIncrementEvent.Create(
                migrationId: migrationId,
                entityType: entityType,
                chunkNumber: i,
                chunkStartIndex: (i - 1) * entitiesPerChunk,
                chunkSize: entitiesPerChunk,
                successfulEntities: (int)(entitiesPerChunk * 0.96), // 96% success rate
                failedEntities: (int)(entitiesPerChunk * 0.03), // 3% failure rate
                skippedEntities: (int)(entitiesPerChunk * 0.01), // 1% skip rate
                cancelledEntities: 0,
                processingStartTime: startTime,
                processingEndTime: endTime,
                sourceStore: "test-source-store",
                destinationStore: "test-dest-store",
                errors: null
            ));
        }
        
        return chunks;
    }

    private static async Task CleanupTestData()
    {
        if (_incrementEventsService == null) return;

        try
        {
            Console.WriteLine("\n🧹 Cleaning up test data...");
            foreach (var migrationId in _testMigrationIds)
            {
                await _incrementEventsService.DeleteMigrationIncrementsAsync(migrationId);
            }
            Console.WriteLine($"✅ Cleaned up {_testMigrationIds.Count} test migrations");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Warning: Failed to cleanup test data: {ex.Message}");
        }
    }

    #endregion
}