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
using Moq;

namespace BigCommerce.Migration.Tests.Performance;

/// <summary>
/// Task 5.1: Performance Testing Validation
/// 
/// Comprehensive performance tests to validate that the incremental progress system
/// meets the critical <10% overhead requirement and performs well under various loads.
/// 
/// Test Coverage:
/// 1. Small Migration Performance (< 1,000 entities)
/// 2. Medium Migration Performance (1,000 - 10,000 entities)  
/// 3. Large Migration Performance (10,000+ entities)
/// 4. Database write frequency and impact measurement
/// 5. Concurrent migration scenarios
/// 6. Aggregation performance benchmarks
/// 7. Memory usage and resource consumption
/// 8. Edge case performance (very large migrations)
/// </summary>
[Collection("Task 5.1 Performance Tests")]
public class Task51PerformanceValidation : IDisposable
{
    #region Test Infrastructure

    private readonly IIncrementEventsService _incrementEventsService;
    private readonly ILogger<Task51PerformanceValidation> _logger;
    private readonly List<string> _testMigrationIds;

    public Task51PerformanceValidation()
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
        _logger = loggerFactory.CreateLogger<Task51PerformanceValidation>();

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

    #region Task 5.1.1: Performance Test Scenarios

    [Fact]
    public async Task SmallMigration_ShouldMeetPerformanceRequirements()
    {
        // Arrange - Small migration (500 entities)
        var migrationId = $"small-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        var chunks = CreateTestChunks(migrationId, "products", 2, 250); // 2 chunks, 250 entities each

        _logger.LogInformation("🧪 TASK 5.1.1: Testing small migration performance (500 entities)");

        // Act - Measure processing time with incremental progress
        var stopwatch = Stopwatch.StartNew();
        
        foreach (var chunk in chunks)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(chunk);
        }
        
        var incrementalProgress = await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
        stopwatch.Stop();

        // Assert - Performance requirements
        var processingTime = stopwatch.ElapsedMilliseconds;
        
        // Should complete within 2 seconds for small migration
        processingTime.Should().BeLessThan(2000, 
            "Small migration incremental progress should complete within 2 seconds");

        // Verify data accuracy
        incrementalProgress.SuccessfulEntities.Should().Be(480); // 240 + 240
        incrementalProgress.FailedEntities.Should().Be(16); // 8 + 8
        incrementalProgress.SkippedEntities.Should().Be(4); // 2 + 2

        _logger.LogInformation("✅ Small migration performance: {ProcessingTime}ms for 500 entities", processingTime);
    }

    [Fact]
    public async Task MediumMigration_ShouldMeetPerformanceRequirements()
    {
        // Arrange - Medium migration (5,000 entities)
        var migrationId = $"medium-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        var chunks = CreateTestChunks(migrationId, "products", 10, 500); // 10 chunks, 500 entities each

        _logger.LogInformation("🧪 TASK 5.1.1: Testing medium migration performance (5,000 entities)");

        // Act - Measure processing time with incremental progress
        var stopwatch = Stopwatch.StartNew();
        
        foreach (var chunk in chunks)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(chunk);
        }
        
        var incrementalProgress = await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
        stopwatch.Stop();

        // Assert - Performance requirements
        var processingTime = stopwatch.ElapsedMilliseconds;
        
        // Should complete within 10 seconds for medium migration
        processingTime.Should().BeLessThan(10000, 
            "Medium migration incremental progress should complete within 10 seconds");

        // Verify data accuracy
        incrementalProgress.SuccessfulEntities.Should().Be(4800); // 10 chunks * 480 each
        incrementalProgress.ProcessedEntities.Should().Be(5000);

        _logger.LogInformation("✅ Medium migration performance: {ProcessingTime}ms for 5,000 entities", processingTime);
    }

    [Fact]
    public async Task LargeMigration_ShouldMeetPerformanceRequirements()
    {
        // Arrange - Large migration (25,000 entities)
        var migrationId = $"large-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        var chunks = CreateTestChunks(migrationId, "products", 50, 500); // 50 chunks, 500 entities each

        _logger.LogInformation("🧪 TASK 5.1.1: Testing large migration performance (25,000 entities)");

        // Act - Measure processing time with incremental progress
        var stopwatch = Stopwatch.StartNew();
        
        // Process chunks in batches to simulate real-world scenario
        var batchSize = 10;
        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize);
            var batchTasks = batch.Select(chunk => _incrementEventsService.WriteChunkIncrementAsync(chunk));
            await Task.WhenAll(batchTasks);
        }
        
        var incrementalProgress = await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
        stopwatch.Stop();

        // Assert - Performance requirements
        var processingTime = stopwatch.ElapsedMilliseconds;
        
        // Should complete within 30 seconds for large migration
        processingTime.Should().BeLessThan(30000, 
            "Large migration incremental progress should complete within 30 seconds");

        // Verify data accuracy
        incrementalProgress.SuccessfulEntities.Should().Be(24000); // 50 chunks * 480 each
        incrementalProgress.ProcessedEntities.Should().Be(25000);

        _logger.LogInformation("✅ Large migration performance: {ProcessingTime}ms for 25,000 entities", processingTime);
    }

    #endregion

    #region Task 5.1.2: Database Write Frequency and Impact

    [Fact]
    public async Task DatabaseWriteFrequency_ShouldMeetPerformanceTargets()
    {
        // Arrange
        var migrationId = $"db-write-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        _logger.LogInformation("🧪 TASK 5.1.2: Testing database write frequency and impact");

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

    #region Task 5.1.3: Concurrent Migration Scenarios

    [Fact]
    public async Task ConcurrentMigrations_ShouldMaintainPerformance()
    {
        // Arrange - 5 concurrent migrations
        var migrationIds = Enumerable.Range(1, 5)
            .Select(i => $"concurrent-{i}-{Guid.NewGuid():N}")
            .ToList();
        
        _testMigrationIds.AddRange(migrationIds);

        _logger.LogInformation("🧪 TASK 5.1.3: Testing concurrent migration performance (5 migrations)");

        // Act - Process concurrent migrations
        var stopwatch = Stopwatch.StartNew();

        var concurrentTasks = migrationIds.Select(async migrationId =>
        {
            var chunks = CreateTestChunks(migrationId, "products", 10, 100); // 10 chunks each
            
            foreach (var chunk in chunks)
            {
                await _incrementEventsService.WriteChunkIncrementAsync(chunk);
            }
            
            return await _progressTracker.GetLatestAggregatedProgressAsync(migrationId);
        });

        var results = await Task.WhenAll(concurrentTasks);
        stopwatch.Stop();

        // Assert - Concurrent performance
        var totalTime = stopwatch.ElapsedMilliseconds;
        
        // Should handle 5 concurrent migrations within 20 seconds
        totalTime.Should().BeLessThan(20000, 
            "5 concurrent migrations should complete within 20 seconds");

        // Verify all migrations completed successfully
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(progress =>
        {
            progress.SuccessfulEntities.Should().Be(960); // 10 chunks * 96 successful each
            progress.ProcessedEntities.Should().Be(1000); // 10 chunks * 100 each
        });

        _logger.LogInformation("✅ Concurrent migration performance: {TotalTime}ms for 5 concurrent migrations", totalTime);
    }

    #endregion

    #region Task 5.1.4: Aggregation Performance Benchmarks

    [Fact]
    public async Task AggregationPerformance_ShouldMeetBenchmarks()
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

        _logger.LogInformation("🧪 TASK 5.1.4: Testing aggregation performance ({ChunkCount} chunks)", allChunks.Count);

        // Act - Measure aggregation performance
        var aggregationTimes = new List<long>();

        for (int i = 0; i < 10; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var progress = await _incrementEventsService.GetAggregatedProgressAsync(migrationId);
            stopwatch.Stop();
            
            aggregationTimes.Add(stopwatch.ElapsedMilliseconds);
            
            // Verify aggregation accuracy
            progress.EntityProgress.Should().HaveCount(4); // 4 entity types
            progress.SuccessfulEntities.Should().BeGreaterThan(0);
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

    #region Task 5.1.5: Memory Usage and Resource Consumption

    [Fact]
    public async Task MemoryUsage_ShouldRemainWithinLimits()
    {
        // Arrange
        var migrationId = $"memory-test-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        _logger.LogInformation("🧪 TASK 5.1.5: Testing memory usage and resource consumption");

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
            await _progressTracker.GetLatestAggregatedProgressAsync(migrationId);
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

    #region Task 5.1.6: Edge Case Performance

    [Fact]
    public async Task VeryLargeMigration_ShouldHandleGracefully()
    {
        // Arrange - Very large migration (100,000 entities)
        var migrationId = $"very-large-perf-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        _logger.LogInformation("🧪 TASK 5.1.6: Testing very large migration performance (100,000 entities)");

        // Act - Create chunks for very large migration (but don't process all to save time)
        var sampleChunks = CreateTestChunks(migrationId, "products", 20, 500); // Sample: 20 chunks

        var stopwatch = Stopwatch.StartNew();
        
        // Process sample chunks
        foreach (var chunk in sampleChunks)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(chunk);
        }
        
        // Test aggregation performance with sample data
        var progress = await _progressTracker.GetLatestAggregatedProgressAsync(migrationId);
        stopwatch.Stop();

        // Assert - Should handle large migration samples efficiently
        var processingTime = stopwatch.ElapsedMilliseconds;
        
        processingTime.Should().BeLessThan(15000, 
            "Very large migration sample should process within 15 seconds");

        // Extrapolate performance for full migration
        var estimatedFullMigrationTime = (processingTime / 20.0) * 200; // 200 chunks for 100k entities
        estimatedFullMigrationTime.Should().BeLessThan(150000, // 2.5 minutes
            "Full very large migration should complete within 2.5 minutes");

        _logger.LogInformation("✅ Very large migration performance: {ProcessingTime}ms for sample, estimated {EstimatedMs}ms for full", 
            processingTime, estimatedFullMigrationTime);
    }

    #endregion

    #region Task 5.1.7: Performance Overhead Validation

    [Fact]
    public async Task IncrementalProgressOverhead_ShouldBeLessThan10Percent()
    {
        // Arrange
        var migrationId = $"overhead-test-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);

        _logger.LogInformation("🧪 TASK 5.1.7: Validating <10% performance overhead requirement");

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