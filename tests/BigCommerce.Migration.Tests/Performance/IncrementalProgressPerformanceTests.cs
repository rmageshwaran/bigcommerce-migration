using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Tests.Performance;

/// <summary>
/// Performance and load tests for the incremental progress system
/// Tests system behavior under high load and concurrent access patterns
/// 
/// Test Scenarios:
/// 1. High-throughput chunk processing simulation
/// 2. Concurrent chunk writes from multiple migration instances
/// 3. Large-scale aggregation performance
/// 4. Memory usage and resource cleanup
/// 5. Azure Table Storage performance limits
/// </summary>
[Collection("Performance Tests")]
public class IncrementalProgressPerformanceTests : IDisposable
{
    #region Test Infrastructure

    private readonly IIncrementEventsService _incrementEventsService;
    private readonly ILogger<IncrementEventsService> _logger;
    private readonly List<string> _testMigrationIds;

    public IncrementalProgressPerformanceTests()
    {
        _testMigrationIds = new List<string>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true"
            }!)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<IncrementEventsService>();

        _incrementEventsService = new IncrementEventsService(configuration, _logger);
    }

    public void Dispose()
    {
        CleanupTestDataAsync().GetAwaiter().GetResult();
    }

    private async Task CleanupTestDataAsync()
    {
        try
        {
            foreach (var migrationId in _testMigrationIds)
            {
                await _incrementEventsService.DeleteMigrationIncrementsAsync(migrationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup test data");
        }
    }

    private string CreateTestMigrationId()
    {
        var migrationId = $"perf-test-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        return migrationId;
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task HighThroughputChunkWrites_ShouldMaintainPerformance()
    {
        // Arrange
        var migrationId = CreateTestMigrationId();
        var chunkCount = 100; // Simulate 100 chunks (25,000 entities at 250 per chunk)
        var stopwatch = Stopwatch.StartNew();

        // Act - Write chunks sequentially (simulating real migration flow)
        for (int i = 1; i <= chunkCount; i++)
        {
            var incrementEvent = CreateTestChunkIncrementEvent(migrationId, chunkNumber: i);
            await _incrementEventsService.WriteChunkIncrementAsync(incrementEvent);

            // Log progress every 20 chunks
            if (i % 20 == 0)
            {
                var elapsed = stopwatch.Elapsed;
                var avgTimePerChunk = elapsed.TotalMilliseconds / i;
                _logger.LogInformation("Processed {ChunkCount}/{TotalChunks} chunks. Avg: {AvgTime:F1}ms per chunk",
                    i, chunkCount, avgTimePerChunk);
            }
        }

        stopwatch.Stop();

        // Assert - Performance requirements
        var totalTime = stopwatch.Elapsed;
        var averageTimePerChunk = totalTime.TotalMilliseconds / chunkCount;

        _logger.LogInformation("High throughput test completed: {TotalTime:F1}s total, {AvgTime:F1}ms per chunk",
            totalTime.TotalSeconds, averageTimePerChunk);

        // Performance assertions
        averageTimePerChunk.Should().BeLessThan(500, "Each chunk increment should complete within 500ms");
        totalTime.Should().BeLessThan(TimeSpan.FromMinutes(2), "100 chunks should complete within 2 minutes");

        // Verify data integrity
        var storedEvents = await _incrementEventsService.GetChunkIncrementsAsync(migrationId);
        storedEvents.Should().HaveCount(chunkCount);
    }

    [Fact]
    public async Task ConcurrentMultiMigrationWrites_ShouldHandleLoad()
    {
        // Arrange
        var migrationCount = 5;
        var chunksPerMigration = 20;
        var migrationIds = Enumerable.Range(1, migrationCount).Select(_ => CreateTestMigrationId()).ToArray();
        
        var stopwatch = Stopwatch.StartNew();

        // Act - Simulate multiple concurrent migrations
        var migrationTasks = migrationIds.Select(async migrationId =>
        {
            var chunkTasks = Enumerable.Range(1, chunksPerMigration).Select(async chunkNumber =>
            {
                var incrementEvent = CreateTestChunkIncrementEvent(migrationId, chunkNumber: chunkNumber);
                await _incrementEventsService.WriteChunkIncrementAsync(incrementEvent);
            });

            await Task.WhenAll(chunkTasks);
        });

        await Task.WhenAll(migrationTasks);
        stopwatch.Stop();

        // Assert
        var totalOperations = migrationCount * chunksPerMigration;
        var totalTime = stopwatch.Elapsed;
        var operationsPerSecond = totalOperations / totalTime.TotalSeconds;

        _logger.LogInformation("Concurrent multi-migration test: {TotalOps} operations in {TotalTime:F1}s = {OpsPerSec:F1} ops/sec",
            totalOperations, totalTime.TotalSeconds, operationsPerSecond);

        operationsPerSecond.Should().BeGreaterThan(10, "Should handle at least 10 operations per second");
        totalTime.Should().BeLessThan(TimeSpan.FromMinutes(3), "Should complete within 3 minutes");

        // Verify data integrity for each migration
        foreach (var migrationId in migrationIds)
        {
            var events = await _incrementEventsService.GetChunkIncrementsAsync(migrationId);
            events.Should().HaveCount(chunksPerMigration, $"Migration {migrationId} should have all chunks");
        }
    }

    [Fact]
    public async Task LargeScaleAggregation_ShouldPerformWithinLimits()
    {
        // Arrange
        var migrationId = CreateTestMigrationId();
        var chunkCount = 200; // Large migration simulation
        var entityTypes = new[] { "products", "categories", "brands", "variants" };

        // Create chunks across multiple entity types
        var writeStopwatch = Stopwatch.StartNew();
        var writeTasks = new List<Task>();

        foreach (var entityType in entityTypes)
        {
            for (int i = 1; i <= chunkCount / entityTypes.Length; i++)
            {
                var task = Task.Run(async () =>
                {
                    var incrementEvent = CreateTestChunkIncrementEvent(
                        migrationId, entityType: entityType, chunkNumber: i);
                    await _incrementEventsService.WriteChunkIncrementAsync(incrementEvent);
                });
                writeTasks.Add(task);
            }
        }

        await Task.WhenAll(writeTasks);
        writeStopwatch.Stop();

        // Act - Test aggregation performance
        var aggregationStopwatch = Stopwatch.StartNew();
        var aggregatedProgress = await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
        aggregationStopwatch.Stop();

        // Assert
        var aggregationTime = aggregationStopwatch.ElapsedMilliseconds;
        _logger.LogInformation("Large scale aggregation: {ChunkCount} chunks across {EntityTypes} entity types, " +
                             "aggregated in {AggTime}ms",
            chunkCount, entityTypes.Length, aggregationTime);

        aggregationTime.Should().BeLessThan(5000, "Aggregation should complete within 5 seconds");
        
        aggregatedProgress.Should().HaveCount(entityTypes.Length);
        foreach (var entityType in entityTypes)
        {
            aggregatedProgress.Should().ContainKey(entityType);
            aggregatedProgress[entityType].TotalChunks.Should().Be(chunkCount / entityTypes.Length);
        }
    }

    [Fact]
    public async Task BatchWritePerformance_ShouldOutperformIndividualWrites()
    {
        // Arrange
        var migrationId = CreateTestMigrationId();
        var chunkCount = 50;

        // Test 1: Individual writes
        var individualEvents = Enumerable.Range(1, chunkCount)
            .Select(i => CreateTestChunkIncrementEvent(migrationId, chunkNumber: i))
            .ToList();

        var individualStopwatch = Stopwatch.StartNew();
        foreach (var evt in individualEvents)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(evt);
        }
        individualStopwatch.Stop();

        // Cleanup for batch test
        await _incrementEventsService.DeleteMigrationIncrementsAsync(migrationId);

        // Test 2: Batch write
        var batchEvents = Enumerable.Range(1, chunkCount)
            .Select(i => CreateTestChunkIncrementEvent(migrationId, chunkNumber: i + 1000)) // Different chunk numbers
            .ToList();

        var batchStopwatch = Stopwatch.StartNew();
        await _incrementEventsService.WriteBatchChunkIncrementsAsync(batchEvents);
        batchStopwatch.Stop();

        // Assert
        var individualTime = individualStopwatch.ElapsedMilliseconds;
        var batchTime = batchStopwatch.ElapsedMilliseconds;
        var performanceImprovement = (double)individualTime / batchTime;

        _logger.LogInformation("Write performance comparison: Individual={IndividualTime}ms, Batch={BatchTime}ms, " +
                             "Improvement={Improvement:F1}x",
            individualTime, batchTime, performanceImprovement);

        batchTime.Should().BeLessThan(individualTime, "Batch writes should be faster than individual writes");
        performanceImprovement.Should().BeGreaterThan(1.5, "Batch writes should be at least 1.5x faster");

        // Verify both datasets were written correctly
        var allEvents = await _incrementEventsService.GetChunkIncrementsAsync(migrationId);
        allEvents.Should().HaveCount(chunkCount * 2); // Both individual and batch writes
    }

    [Fact]
    public async Task MemoryUsage_ShouldRemainStable_UnderLoad()
    {
        // Arrange
        var migrationId = CreateTestMigrationId();
        var iterations = 10;
        var chunksPerIteration = 20;

        var initialMemory = GC.GetTotalMemory(true);
        _logger.LogInformation("Initial memory usage: {MemoryMB:F1} MB", initialMemory / 1024.0 / 1024.0);

        // Act - Multiple iterations of chunk processing
        for (int iteration = 1; iteration <= iterations; iteration++)
        {
            var chunkTasks = Enumerable.Range(1, chunksPerIteration).Select(async chunkNumber =>
            {
                var incrementEvent = CreateTestChunkIncrementEvent(
                    migrationId, chunkNumber: (iteration - 1) * chunksPerIteration + chunkNumber);
                await _incrementEventsService.WriteChunkIncrementAsync(incrementEvent);
            });

            await Task.WhenAll(chunkTasks);

            // Force garbage collection and measure memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var currentMemory = GC.GetTotalMemory(false);
            var memoryIncrease = currentMemory - initialMemory;
            
            _logger.LogInformation("Iteration {Iteration}: Memory usage: {MemoryMB:F1} MB " +
                                 "(+{IncreaseMB:F1} MB from initial)",
                iteration, currentMemory / 1024.0 / 1024.0, memoryIncrease / 1024.0 / 1024.0);

            // Assert memory doesn't grow excessively
            memoryIncrease.Should().BeLessThan(50 * 1024 * 1024, // 50 MB
                $"Memory increase should be reasonable after {iteration} iterations");
        }

        // Final verification
        var totalEvents = await _incrementEventsService.GetChunkIncrementsAsync(migrationId);
        totalEvents.Should().HaveCount(iterations * chunksPerIteration);
    }

    [Fact]
    public async Task ErrorRecovery_ShouldMaintainPerformance()
    {
        // Arrange
        var migrationId = CreateTestMigrationId();
        var totalChunks = 50;
        var errorFrequency = 10; // Every 10th chunk will have an error

        var stopwatch = Stopwatch.StartNew();
        var successCount = 0;
        var errorCount = 0;

        // Act - Mix successful and error-prone operations
        for (int i = 1; i <= totalChunks; i++)
        {
            try
            {
                ChunkIncrementEvent incrementEvent;
                
                if (i % errorFrequency == 0)
                {
                    // Create invalid event to trigger error handling
                    incrementEvent = new ChunkIncrementEvent
                    {
                        MigrationId = "", // Invalid - will trigger validation error
                        EntityType = "products",
                        ChunkNumber = i
                    };
                    errorCount++;
                }
                else
                {
                    incrementEvent = CreateTestChunkIncrementEvent(migrationId, chunkNumber: i);
                    successCount++;
                }

                await _incrementEventsService.WriteChunkIncrementAsync(incrementEvent);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Expected error for chunk {ChunkNumber}: {Error}", i, ex.Message);
            }
        }

        stopwatch.Stop();

        // Assert
        var averageTimePerOperation = stopwatch.ElapsedMilliseconds / (double)totalChunks;
        
        _logger.LogInformation("Error recovery test: {SuccessCount} successful, {ErrorCount} errors, " +
                             "{AvgTime:F1}ms per operation",
            successCount, errorCount, averageTimePerOperation);

        averageTimePerOperation.Should().BeLessThan(1000, "Error handling shouldn't significantly impact performance");

        // Verify only successful events were stored
        var storedEvents = await _incrementEventsService.GetChunkIncrementsAsync(migrationId);
        storedEvents.Should().HaveCount(successCount);
    }

    #endregion

    #region Helper Methods

    private ChunkIncrementEvent CreateTestChunkIncrementEvent(
        string migrationId,
        string entityType = "products",
        int chunkNumber = 1,
        int successful = 100,
        int failed = 5)
    {
        return ChunkIncrementEvent.Create(
            migrationId: migrationId,
            entityType: entityType,
            chunkNumber: chunkNumber,
            chunkStartIndex: (chunkNumber - 1) * 250,
            chunkSize: successful + failed,
            successfulEntities: successful,
            failedEntities: failed,
            skippedEntities: 0,
            cancelledEntities: 0,
            processingStartTime: DateTime.UtcNow.AddMinutes(-2),
            processingEndTime: DateTime.UtcNow,
            sourceStore: "perf-source-store",
            destinationStore: "perf-dest-store");
    }

    #endregion
}

/// <summary>
/// Collection definition for performance tests
/// </summary>
[CollectionDefinition("Performance Tests")]
public class PerformanceTestCollection : ICollectionFixture<PerformanceTestCollection>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}