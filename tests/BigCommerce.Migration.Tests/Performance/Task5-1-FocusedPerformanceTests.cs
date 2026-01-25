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
/// Task 5.1: Focused Performance Testing for Incremental Progress System
/// 
/// This test suite validates the core performance requirements for the incremental progress system:
/// 1. Database write performance under load
/// 2. Aggregation query performance 
/// 3. Concurrent write scenarios
/// 4. Memory usage validation
/// 5. Critical <10% overhead requirement
/// 
/// These tests focus specifically on the IncrementEventsService performance
/// without dependencies on other system components that may have compilation issues.
/// </summary>
[Collection("Task 5.1 Focused Performance Tests")]
public class Task51FocusedPerformanceTests : IDisposable
{
    #region Test Infrastructure

    private readonly IIncrementEventsService _incrementEventsService;
    private readonly ILogger<Task51FocusedPerformanceTests> _logger;
    private readonly List<string> _testMigrationIds;

    public Task51FocusedPerformanceTests()
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
        
        var incrementLogger = loggerFactory.CreateLogger<IncrementEventsService>();
        _logger = loggerFactory.CreateLogger<Task51FocusedPerformanceTests>();

        _incrementEventsService = new IncrementEventsService(configuration, incrementLogger);
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
            _logger.LogInformation("✅ Cleaned up {MigrationCount} test migrations", _testMigrationIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup test data");
        }
    }

    #endregion

    #region Task 5.1.1: Database Write Performance

    [Fact]
    public async Task DatabaseWrites_ShouldMeetPerformanceTargets()
    {
        // Arrange
        var migrationId = $"db-write-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        _logger.LogInformation("🧪 TASK 5.1.1: Testing database write performance");

        // Act - Measure write performance for 100 rapid chunks
        var stopwatch = Stopwatch.StartNew();
        var writeTimes = new List<long>();

        for (int i = 1; i <= 100; i++)
        {
            var chunkStopwatch = Stopwatch.StartNew();
            
            var chunk = new ChunkIncrementEvent
            {
                MigrationId = migrationId,
                EntityType = "products",
                ChunkNumber = i,
                SuccessfulEntities = 100,
                FailedEntities = 2,
                SkippedEntities = 1,
                CancelledEntities = 0,
                Timestamp = DateTime.UtcNow
            };

            await _incrementEventsService.WriteChunkIncrementAsync(chunk);
            chunkStopwatch.Stop();
            writeTimes.Add(chunkStopwatch.ElapsedMilliseconds);
        }

        stopwatch.Stop();

        // Assert - Database write performance
        var totalTime = stopwatch.ElapsedMilliseconds;
        var averageWriteTime = writeTimes.Average();
        var maxWriteTime = writeTimes.Max();
        var minWriteTime = writeTimes.Min();

        // Performance targets
        averageWriteTime.Should().BeLessThan(100, 
            "Average database write time should be under 100ms");
        maxWriteTime.Should().BeLessThan(500, 
            "Maximum database write time should be under 500ms");
        totalTime.Should().BeLessThan(15000, 
            "100 database writes should complete within 15 seconds");

        _logger.LogInformation("✅ Database write performance: Avg={AvgMs}ms, Max={MaxMs}ms, Min={MinMs}ms, Total={TotalMs}ms", 
            averageWriteTime, maxWriteTime, minWriteTime, totalTime);
    }

    #endregion

    #region Task 5.1.2: Aggregation Performance

    [Fact]
    public async Task AggregationQueries_ShouldMeetPerformanceTargets()
    {
        // Arrange - Migration with many chunks for aggregation testing
        var migrationId = $"aggregation-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        // Create 200 chunks across multiple entity types
        var allChunks = new List<ChunkIncrementEvent>();
        allChunks.AddRange(CreateTestChunks(migrationId, "products", 100, 50));
        allChunks.AddRange(CreateTestChunks(migrationId, "categories", 50, 50));
        allChunks.AddRange(CreateTestChunks(migrationId, "brands", 30, 50));
        allChunks.AddRange(CreateTestChunks(migrationId, "variants", 20, 50));

        // Write all chunks
        foreach (var chunk in allChunks)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(chunk);
        }

        _logger.LogInformation("🧪 TASK 5.1.2: Testing aggregation performance ({ChunkCount} chunks)", allChunks.Count);

        // Act - Measure aggregation performance
        var aggregationTimes = new List<long>();

        for (int i = 0; i < 10; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var progress = await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
            stopwatch.Stop();
            
            aggregationTimes.Add(stopwatch.ElapsedMilliseconds);
            
            // Verify aggregation accuracy
            progress.Should().HaveCount(4); // 4 entity types
            progress.Values.Sum(p => p.TotalSuccessful).Should().BeGreaterThan(0);
        }

        // Assert - Aggregation performance benchmarks
        var averageAggregationTime = aggregationTimes.Average();
        var maxAggregationTime = aggregationTimes.Max();

        averageAggregationTime.Should().BeLessThan(2000, 
            "Average aggregation time should be under 2 seconds for 200 chunks");
        maxAggregationTime.Should().BeLessThan(5000, 
            "Maximum aggregation time should be under 5 seconds");

        _logger.LogInformation("✅ Aggregation performance: Avg={AvgMs}ms, Max={MaxMs}ms for {ChunkCount} chunks", 
            averageAggregationTime, maxAggregationTime, allChunks.Count);
    }

    #endregion

    #region Task 5.1.3: Concurrent Write Scenarios

    [Fact]
    public async Task ConcurrentWrites_ShouldMaintainPerformance()
    {
        // Arrange - 5 concurrent migrations
        var migrationIds = Enumerable.Range(1, 5)
            .Select(i => $"concurrent-{i}-{Guid.NewGuid():N}")
            .ToList();
        
        _testMigrationIds.AddRange(migrationIds);

        _logger.LogInformation("🧪 TASK 5.1.3: Testing concurrent write performance (5 migrations)");

        // Act - Process concurrent writes
        var stopwatch = Stopwatch.StartNew();

        var concurrentTasks = migrationIds.Select(async migrationId =>
        {
            var chunks = CreateTestChunks(migrationId, "products", 20, 100); // 20 chunks each
            
            foreach (var chunk in chunks)
            {
                await _incrementEventsService.WriteChunkIncrementAsync(chunk);
            }
            
            return await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
        });

        var results = await Task.WhenAll(concurrentTasks);
        stopwatch.Stop();

        // Assert - Concurrent performance
        var totalTime = stopwatch.ElapsedMilliseconds;
        
        // Should handle 5 concurrent migrations within 30 seconds
        totalTime.Should().BeLessThan(30000, 
            "5 concurrent migrations should complete within 30 seconds");

        // Verify all migrations completed successfully
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(progress =>
        {
            progress.Should().NotBeEmpty();
            progress.Values.Sum(p => p.TotalSuccessful).Should().BeGreaterThan(0);
        });

        _logger.LogInformation("✅ Concurrent migration performance: {TotalTime}ms for 5 concurrent migrations", totalTime);
    }

    #endregion

    #region Task 5.1.4: Memory Usage Validation

    [Fact]
    public async Task MemoryUsage_ShouldRemainWithinLimits()
    {
        // Arrange
        var migrationId = $"memory-test-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        _logger.LogInformation("🧪 TASK 5.1.4: Testing memory usage and resource consumption");

        // Measure initial memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);

        // Act - Process many chunks and measure memory growth
        var chunks = CreateTestChunks(migrationId, "products", 500, 100); // 500 chunks

        foreach (var chunk in chunks)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(chunk);
        }

        // Multiple aggregation calls to test memory usage
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

        // Assert - Memory usage limits
        memoryIncreaseMB.Should().BeLessThan(50, 
            "Memory increase should be less than 50MB for 500 chunks + 50 aggregations");

        _logger.LogInformation("✅ Memory usage: Increased by {MemoryMB:F2}MB ({MemoryBytes:N0} bytes)", 
            memoryIncreaseMB, memoryIncrease);
    }

    #endregion

    #region Task 5.1.5: Performance Overhead Validation

    [Fact]
    public async Task IncrementalProgressOverhead_ShouldBeLessThan10Percent()
    {
        // Arrange
        var migrationId = $"overhead-test-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        _logger.LogInformation("🧪 TASK 5.1.5: Validating <10% performance overhead requirement");

        // Baseline - Simulate processing without incremental progress
        var baselineStopwatch = Stopwatch.StartNew();
        for (int i = 1; i <= 50; i++)
        {
            await Task.Delay(10); // Simulate entity processing time
        }
        baselineStopwatch.Stop();
        var baselineTime = baselineStopwatch.ElapsedMilliseconds;

        // With incremental progress - Simulate processing with incremental updates
        var incrementalStopwatch = Stopwatch.StartNew();
        for (int i = 1; i <= 50; i++)
        {
            await Task.Delay(10); // Simulate entity processing time
            
            // Add incremental progress overhead
            var chunk = new ChunkIncrementEvent
            {
                MigrationId = migrationId,
                EntityType = "products",
                ChunkNumber = i,
                SuccessfulEntities = 96,
                FailedEntities = 3,
                SkippedEntities = 1,
                CancelledEntities = 0,
                Timestamp = DateTime.UtcNow
            };
            
            await _incrementEventsService.WriteChunkIncrementAsync(chunk);
        }
        incrementalStopwatch.Stop();
        var incrementalTime = incrementalStopwatch.ElapsedMilliseconds;

        // Calculate overhead percentage
        var overhead = incrementalTime - baselineTime;
        var overheadPercentage = (overhead / (double)baselineTime) * 100.0;

        // Assert - Critical requirement: <10% overhead
        overheadPercentage.Should().BeLessThan(10.0, 
            "Incremental progress overhead must be less than 10%");

        _logger.LogInformation("✅ Performance overhead validation: {OverheadPercentage:F2}% overhead ({OverheadMs}ms out of {BaselineMs}ms)", 
            overheadPercentage, overhead, baselineTime);
        
        // Additional assertion for excellent performance
        if (overheadPercentage < 5.0)
        {
            _logger.LogInformation("🎉 EXCELLENT: Overhead is under 5% - exceeds performance requirements!");
        }
    }

    #endregion

    #region Task 5.1.6: Scalability Testing

    [Fact]
    public async Task LargeScaleMigration_ShouldHandleGracefully()
    {
        // Arrange - Very large migration simulation
        var migrationId = $"large-scale-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        _logger.LogInformation("🧪 TASK 5.1.6: Testing large scale migration performance");

        // Act - Create and process chunks for large migration (sample approach)
        var sampleChunks = CreateTestChunks(migrationId, "products", 50, 500); // Sample: 50 chunks, 25k entities

        var stopwatch = Stopwatch.StartNew();
        
        // Process chunks in batches to simulate real-world scenario
        var batchSize = 10;
        for (int i = 0; i < sampleChunks.Count; i += batchSize)
        {
            var batch = sampleChunks.Skip(i).Take(batchSize);
            var batchTasks = batch.Select(chunk => _incrementEventsService.WriteChunkIncrementAsync(chunk));
            await Task.WhenAll(batchTasks);
        }
        
        // Test aggregation performance with large dataset
        var progress = await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
        stopwatch.Stop();

        // Assert - Should handle large migration efficiently
        var processingTime = stopwatch.ElapsedMilliseconds;
        
        processingTime.Should().BeLessThan(20000, 
            "Large migration sample should process within 20 seconds");

        // Verify data accuracy
        progress.Values.Sum(p => p.TotalSuccessful).Should().BeGreaterThan(20000);

        _logger.LogInformation("✅ Large scale migration performance: {ProcessingTime}ms for {EntityCount} entities", 
            processingTime, progress.Values.Sum(p => p.TotalSuccessful + p.TotalFailed + p.TotalSkipped));
    }

    #endregion

    #region Helper Methods

    private List<ChunkIncrementEvent> CreateTestChunks(string migrationId, string entityType, int chunkCount, int entitiesPerChunk)
    {
        var chunks = new List<ChunkIncrementEvent>();
        
        for (int i = 1; i <= chunkCount; i++)
        {
            chunks.Add(new ChunkIncrementEvent
            {
                MigrationId = migrationId,
                EntityType = entityType,
                ChunkNumber = i,
                SuccessfulEntities = (int)(entitiesPerChunk * 0.96), // 96% success rate
                FailedEntities = (int)(entitiesPerChunk * 0.03), // 3% failure rate
                SkippedEntities = (int)(entitiesPerChunk * 0.01), // 1% skip rate
                CancelledEntities = 0,
                Timestamp = DateTime.UtcNow.AddMinutes(-chunkCount + i) // Spread over time
            });
        }
        
        return chunks;
    }

    #endregion
}