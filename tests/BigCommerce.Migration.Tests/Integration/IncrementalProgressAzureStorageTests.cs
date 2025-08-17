using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Integration tests specifically for Azure Table Storage incremental progress functionality
/// Tests the IncrementEventsService with real Azure Storage (Azurite for local testing)
/// 
/// Prerequisites:
/// - Azurite storage emulator running locally
/// - Connection string: "UseDevelopmentStorage=true"
/// 
/// Test Coverage:
/// 1. ChunkIncrementEvent creation and storage
/// 2. Query operations and aggregation
/// 3. Batch operations
/// 4. Error handling and resilience
/// 5. Performance under load
/// </summary>
[Collection("Azure Storage Tests")]
public class IncrementalProgressAzureStorageTests : IDisposable
{
    #region Test Infrastructure

    private readonly IIncrementEventsService _incrementEventsService;
    private readonly ILogger<IncrementEventsService> _logger;
    private readonly string _testMigrationId;
    private readonly List<string> _testMigrationIds;

    public IncrementalProgressAzureStorageTests()
    {
        _testMigrationId = $"test-migration-{Guid.NewGuid():N}";
        _testMigrationIds = new List<string> { _testMigrationId };

        // Configure for Azurite local testing
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true"
            }!)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<IncrementEventsService>();

        _incrementEventsService = new IncrementEventsService(configuration, _logger);
    }

    public void Dispose()
    {
        // Cleanup test data
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
            // Log but don't fail tests due to cleanup issues
            _logger.LogWarning(ex, "Failed to cleanup test data");
        }
    }

    #endregion

    #region Azure Storage Integration Tests

    [Fact]
    public async Task WriteChunkIncrementAsync_ShouldPersistToAzureStorage()
    {
        // Arrange
        var incrementEvent = CreateTestChunkIncrementEvent(chunkNumber: 1);

        // Act
        await _incrementEventsService.WriteChunkIncrementAsync(incrementEvent);

        // Assert - Verify data was persisted
        var storedEvents = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId);
        
        storedEvents.Should().HaveCount(1);
        var storedEvent = storedEvents[0];
        
        storedEvent.MigrationId.Should().Be(_testMigrationId);
        storedEvent.EntityType.Should().Be("products");
        storedEvent.ChunkNumber.Should().Be(1);
        storedEvent.SuccessfulEntities.Should().Be(100);
        storedEvent.FailedEntities.Should().Be(5);
        storedEvent.SkippedEntities.Should().Be(3);
        storedEvent.CancelledEntities.Should().Be(0);
        storedEvent.SourceStore.Should().Be("source-store-123");
        storedEvent.DestinationStore.Should().Be("dest-store-456");
        storedEvent.RowKey.Should().NotBeNullOrEmpty();
        storedEvent.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task WriteBatchChunkIncrementsAsync_ShouldHandleMultipleEvents()
    {
        // Arrange
        var batchEvents = new List<ChunkIncrementEvent>
        {
            CreateTestChunkIncrementEvent(chunkNumber: 1, startIndex: 0),
            CreateTestChunkIncrementEvent(chunkNumber: 2, startIndex: 250),
            CreateTestChunkIncrementEvent(chunkNumber: 3, startIndex: 500)
        };

        // Act
        await _incrementEventsService.WriteBatchChunkIncrementsAsync(batchEvents);

        // Assert
        var storedEvents = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId);
        
        storedEvents.Should().HaveCount(3);
        storedEvents.Should().OnlyContain(e => e.MigrationId == _testMigrationId);
        
        var chunkNumbers = storedEvents.Select(e => e.ChunkNumber).OrderBy(n => n).ToArray();
        chunkNumbers.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task GetChunkIncrementsAsync_ByEntityType_ShouldFilterCorrectly()
    {
        // Arrange
        var productEvents = new[]
        {
            CreateTestChunkIncrementEvent(entityType: "products", chunkNumber: 1),
            CreateTestChunkIncrementEvent(entityType: "products", chunkNumber: 2)
        };
        
        var categoryEvents = new[]
        {
            CreateTestChunkIncrementEvent(entityType: "categories", chunkNumber: 1)
        };

        foreach (var evt in productEvents.Concat(categoryEvents))
        {
            await _incrementEventsService.WriteChunkIncrementAsync(evt);
        }

        // Act
        var productIncrements = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId, "products");
        var categoryIncrements = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId, "categories");

        // Assert
        productIncrements.Should().HaveCount(2);
        productIncrements.Should().OnlyContain(e => e.EntityType == "products");
        
        categoryIncrements.Should().HaveCount(1);
        categoryIncrements.Should().OnlyContain(e => e.EntityType == "categories");
    }

    [Fact]
    public async Task GetAggregatedProgressAsync_ShouldCalculateCorrectTotals()
    {
        // Arrange
        var chunkEvents = new[]
        {
            CreateTestChunkIncrementEvent(chunkNumber: 1, successful: 100, failed: 5, skipped: 2),
            CreateTestChunkIncrementEvent(chunkNumber: 2, successful: 95, failed: 8, skipped: 1),
            CreateTestChunkIncrementEvent(chunkNumber: 3, successful: 88, failed: 12, skipped: 4)
        };

        foreach (var evt in chunkEvents)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(evt);
        }

        // Act
        var aggregatedProgress = await _incrementEventsService.GetAggregatedProgressAsync(_testMigrationId);

        // Assert
        aggregatedProgress.Should().ContainKey("products");
        
        var productProgress = aggregatedProgress["products"];
        productProgress.TotalChunks.Should().Be(3);
        productProgress.TotalSuccessful.Should().Be(283); // 100 + 95 + 88
        productProgress.TotalFailed.Should().Be(25); // 5 + 8 + 12
        productProgress.TotalSkipped.Should().Be(7); // 2 + 1 + 4
        productProgress.TotalProcessed.Should().Be(315); // 283 + 25 + 7
        productProgress.SuccessRate.Should().BeApproximately(89.84, 0.01); // 283/315 * 100
    }

    [Fact]
    public async Task GetEntityProgressSummaryAsync_ShouldReturnCorrectSummary()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddMinutes(-10);
        var endTime = DateTime.UtcNow.AddMinutes(-5);
        
        var chunkEvents = new[]
        {
            CreateTestChunkIncrementEvent(chunkNumber: 1, processingStart: startTime, processingEnd: startTime.AddMinutes(2)),
            CreateTestChunkIncrementEvent(chunkNumber: 2, processingStart: startTime.AddMinutes(3), processingEnd: endTime)
        };

        foreach (var evt in chunkEvents)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(evt);
        }

        // Act
        var summary = await _incrementEventsService.GetEntityProgressSummaryAsync(_testMigrationId, "products");

        // Assert
        summary.Should().NotBeNull();
        summary.EntityType.Should().Be("products");
        summary.TotalChunks.Should().Be(2);
        summary.TotalSuccessful.Should().Be(200); // 100 * 2
        summary.FirstChunkStartTime.Should().BeCloseTo(startTime, TimeSpan.FromSeconds(30));
        summary.LastChunkEndTime.Should().BeCloseTo(endTime, TimeSpan.FromSeconds(30));
        summary.TotalProcessingTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task IsHealthyAsync_WithValidConnection_ShouldReturnTrue()
    {
        // Act
        var isHealthy = await _incrementEventsService.IsHealthyAsync();

        // Assert
        isHealthy.Should().BeTrue("Service should be healthy with valid Azurite connection");
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnAccurateStatistics()
    {
        // Arrange
        var chunkEvents = new[]
        {
            CreateTestChunkIncrementEvent(chunkNumber: 1, successful: 100, failed: 5),
            CreateTestChunkIncrementEvent(chunkNumber: 2, successful: 95, failed: 10)
        };

        foreach (var evt in chunkEvents)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(evt);
        }

        // Act
        var statistics = await _incrementEventsService.GetStatisticsAsync(_testMigrationId);

        // Assert
        statistics.Should().NotBeNull();
        statistics.MigrationId.Should().Be(_testMigrationId);
        statistics.TotalEvents.Should().Be(2);
        statistics.TotalSuccessfulEntities.Should().Be(195); // 100 + 95
        statistics.TotalFailedEntities.Should().Be(15); // 5 + 10
        statistics.UniqueMigrations.Should().Be(1);
        statistics.UniqueEntityTypes.Should().Be(1);
        statistics.CalculatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task DeleteMigrationIncrementsAsync_ShouldRemoveAllEvents()
    {
        // Arrange
        var chunkEvents = new[]
        {
            CreateTestChunkIncrementEvent(chunkNumber: 1),
            CreateTestChunkIncrementEvent(chunkNumber: 2),
            CreateTestChunkIncrementEvent(chunkNumber: 3)
        };

        foreach (var evt in chunkEvents)
        {
            await _incrementEventsService.WriteChunkIncrementAsync(evt);
        }

        // Verify events exist
        var eventsBefore = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId);
        eventsBefore.Should().HaveCount(3);

        // Act
        var deletedCount = await _incrementEventsService.DeleteMigrationIncrementsAsync(_testMigrationId);

        // Assert
        deletedCount.Should().Be(3);
        
        var eventsAfter = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId);
        eventsAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentWrites_ShouldHandleCorrectly()
    {
        // Arrange
        var concurrentTasks = new List<Task>();
        var chunkCount = 10;

        // Act - Write multiple chunks concurrently
        for (int i = 1; i <= chunkCount; i++)
        {
            var chunkNumber = i;
            var task = Task.Run(async () =>
            {
                var incrementEvent = CreateTestChunkIncrementEvent(chunkNumber: chunkNumber);
                await _incrementEventsService.WriteChunkIncrementAsync(incrementEvent);
            });
            concurrentTasks.Add(task);
        }

        await Task.WhenAll(concurrentTasks);

        // Assert
        var storedEvents = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId);
        
        storedEvents.Should().HaveCount(chunkCount);
        storedEvents.Should().OnlyContain(e => e.MigrationId == _testMigrationId);
        
        var chunkNumbers = storedEvents.Select(e => e.ChunkNumber).OrderBy(n => n).ToArray();
        chunkNumbers.Should().BeEquivalentTo(Enumerable.Range(1, chunkCount));
    }

    [Fact]
    public async Task InvalidEvent_ShouldBeHandledGracefully()
    {
        // Arrange
        var invalidEvent = new ChunkIncrementEvent
        {
            // Missing required fields to trigger validation failure
            MigrationId = "", // Invalid - empty
            EntityType = "products",
            ChunkNumber = -1, // Invalid - negative
            SourceStore = "", // Invalid - empty
            DestinationStore = "" // Invalid - empty
        };

        // Act & Assert - Should not throw, but should handle gracefully
        await _incrementEventsService.WriteChunkIncrementAsync(invalidEvent);
        
        // Verify no invalid data was stored
        var storedEvents = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId);
        storedEvents.Should().BeEmpty();
    }

    #endregion

    #region Helper Methods

    private ChunkIncrementEvent CreateTestChunkIncrementEvent(
        string entityType = "products",
        int chunkNumber = 1,
        int startIndex = 0,
        int successful = 100,
        int failed = 5,
        int skipped = 3,
        int cancelled = 0,
        DateTime? processingStart = null,
        DateTime? processingEnd = null)
    {
        var start = processingStart ?? DateTime.UtcNow.AddMinutes(-5);
        var end = processingEnd ?? DateTime.UtcNow.AddMinutes(-2);

        return ChunkIncrementEvent.Create(
            migrationId: _testMigrationId,
            entityType: entityType,
            chunkNumber: chunkNumber,
            chunkStartIndex: startIndex,
            chunkSize: successful + failed + skipped + cancelled,
            successfulEntities: successful,
            failedEntities: failed,
            skippedEntities: skipped,
            cancelledEntities: cancelled,
            processingStartTime: start,
            processingEndTime: end,
            sourceStore: "source-store-123",
            destinationStore: "dest-store-456",
            errors: failed > 0 ? new[] { "Test error message" } : null);
    }

    #endregion
}

/// <summary>
/// Collection definition for Azure Storage integration tests
/// </summary>
[CollectionDefinition("Azure Storage Tests")]
public class AzureStorageTestCollection : ICollectionFixture<AzureStorageTestCollection>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}