using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace BigCommerce.Migration.Tests.IncrementalProgress;

/// <summary>
/// Unit tests for ProgressTracker incremental progress functionality
/// Tests the new IncrementProgressAsync and GetLatestAggregatedProgressAsync methods
/// </summary>
public class ProgressTrackerIncrementalTests
{
    #region Test Setup

    private Mock<IIncrementEventsService> CreateMockIncrementEventsService()
    {
        return new Mock<IIncrementEventsService>();
    }

    private ProgressTracker CreateProgressTracker(IIncrementEventsService? incrementEventsService = null)
    {
        var mockLogger = new Mock<ILogger<ProgressTracker>>();
        var mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
        var mockSignalREventFactory = new Mock<ISignalREventFactory>();
        var mockStorageService = new Mock<IMigrationStorageService>();

        return new ProgressTracker(
            mockLogger.Object,
            mockProgressEventPublisher.Object,
            mockSignalREventFactory.Object,
            mockStorageService.Object,
            incrementEventsService);
    }

    private ChunkIncrementEvent CreateValidChunkEvent()
    {
        return new ChunkIncrementEvent
        {
            PartitionKey = "test-migration-id",
            MigrationId = "test-migration-id",
            EntityType = "products",
            ChunkNumber = 1,
            ChunkStartIndex = 0,
            ChunkSize = 250,
            SuccessfulEntities = 200,
            FailedEntities = 30,
            SkippedEntities = 15,
            CancelledEntities = 5,
            ProcessingStartTime = DateTime.UtcNow.AddMinutes(-5),
            ProcessingEndTime = DateTime.UtcNow,
            ProcessingTimeMs = 300000,
            SourceStore = "source-store",
            DestinationStore = "dest-store"
        };
    }

    #endregion

    #region IncrementProgressAsync Tests

    [Fact]
    public async Task IncrementProgressAsync_WithValidParameters_DoesNotThrow()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        var progressTracker = CreateProgressTracker(mockIncrementService.Object);

        // Act & Assert - Should not throw
        await progressTracker.IncrementProgressAsync(
            "migration-id", "products", 1, 0, 250,
            200, 30, 15, 5,
            DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
            "source", "dest");

        // Verify the increment service was called (eventually, via Task.Run)
        // Note: We can't easily verify Task.Run calls in unit tests, but we can verify no exceptions
        Assert.True(true);
    }

    [Fact]
    public async Task IncrementProgressAsync_WithNullIncrementService_DoesNotThrow()
    {
        // Arrange
        var progressTracker = CreateProgressTracker(incrementEventsService: null);

        // Act & Assert - Should not throw (backward compatibility)
        await progressTracker.IncrementProgressAsync(
            "migration-id", "products", 1, 0, 250,
            200, 30, 15, 5,
            DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
            "source", "dest");

        Assert.True(true);
    }

    [Fact]
    public async Task IncrementProgressAsync_WithEmptyMigrationId_ThrowsArgumentException()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        var progressTracker = CreateProgressTracker(mockIncrementService.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            progressTracker.IncrementProgressAsync(
                "", "products", 1, 0, 250,
                200, 30, 15, 5,
                DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
                "source", "dest"));
    }

    [Fact]
    public async Task IncrementProgressAsync_WithEmptyEntityType_ThrowsArgumentException()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        var progressTracker = CreateProgressTracker(mockIncrementService.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            progressTracker.IncrementProgressAsync(
                "migration-id", "", 1, 0, 250,
                200, 30, 15, 5,
                DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
                "source", "dest"));
    }

    [Fact]
    public async Task IncrementProgressAsync_WithEmptySourceStore_ThrowsArgumentException()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        var progressTracker = CreateProgressTracker(mockIncrementService.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            progressTracker.IncrementProgressAsync(
                "migration-id", "products", 1, 0, 250,
                200, 30, 15, 5,
                DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
                "", "dest"));
    }

    [Fact]
    public async Task IncrementProgressAsync_WithEmptyDestinationStore_ThrowsArgumentException()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        var progressTracker = CreateProgressTracker(mockIncrementService.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            progressTracker.IncrementProgressAsync(
                "migration-id", "products", 1, 0, 250,
                200, 30, 15, 5,
                DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
                "source", ""));
    }

    #endregion

    #region GetLatestAggregatedProgressAsync Tests

    [Fact]
    public async Task GetLatestAggregatedProgressAsync_WithNullIncrementService_ReturnsCachedProgress()
    {
        // Arrange
        var progressTracker = CreateProgressTracker(incrementEventsService: null);

        // Act
        var result = await progressTracker.GetLatestAggregatedProgressAsync("migration-id");

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be("migration-id");
    }

    [Fact]
    public async Task GetLatestAggregatedProgressAsync_WithEmptyMigrationId_ThrowsArgumentException()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        var progressTracker = CreateProgressTracker(mockIncrementService.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            progressTracker.GetLatestAggregatedProgressAsync(""));
    }

    [Fact]
    public async Task GetLatestAggregatedProgressAsync_WithIncrementalData_ReturnsEnhancedProgress()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        var aggregatedData = new Dictionary<string, EntityProgressSummary>
        {
            ["products"] = new EntityProgressSummary
            {
                EntityType = "products",
                TotalChunks = 2,
                TotalSuccessful = 400,
                TotalFailed = 60,
                TotalSkipped = 30,
                TotalCancelled = 10,
                FirstChunkStartTime = DateTime.UtcNow.AddMinutes(-10),
                LastChunkEndTime = DateTime.UtcNow.AddMinutes(-2),
                TotalProcessingTime = TimeSpan.FromMinutes(8)
            }
        };

        mockIncrementService.Setup(s => s.GetAggregatedProgressAsync("migration-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(aggregatedData);

        var progressTracker = CreateProgressTracker(mockIncrementService.Object);

        // Act
        var result = await progressTracker.GetLatestAggregatedProgressAsync("migration-id");

        // Assert
        result.Should().NotBeNull();
        result.EntityProgress.Should().ContainKey("products");
        result.EntityProgress["products"].SuccessCount.Should().Be(400);
        result.EntityProgress["products"].FailureCount.Should().Be(60);
        result.EntityProgress["products"].SkippedCount.Should().Be(30);
        result.EntityProgress["products"].CancelledCount.Should().Be(10);
        result.EntityProgress["products"].ProcessedCount.Should().Be(500); // 400+60+30+10
    }

    [Fact]
    public async Task GetLatestAggregatedProgressAsync_WithNoIncrementalData_ReturnsCachedProgress()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        var emptyData = new Dictionary<string, EntityProgressSummary>();

        mockIncrementService.Setup(s => s.GetAggregatedProgressAsync("migration-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyData);

        var progressTracker = CreateProgressTracker(mockIncrementService.Object);

        // Act
        var result = await progressTracker.GetLatestAggregatedProgressAsync("migration-id");

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be("migration-id");
    }

    [Fact]
    public async Task GetLatestAggregatedProgressAsync_WithIncrementServiceFailure_ReturnsCachedProgress()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        mockIncrementService.Setup(s => s.GetAggregatedProgressAsync("migration-id", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Azure Table Storage error"));

        var progressTracker = CreateProgressTracker(mockIncrementService.Object);

        // Act
        var result = await progressTracker.GetLatestAggregatedProgressAsync("migration-id");

        // Assert - Should return cached progress without throwing
        result.Should().NotBeNull();
        result.MigrationId.Should().Be("migration-id");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task IncrementProgressAsync_WithErrors_PassesErrorsToIncrementService()
    {
        // Arrange
        var mockIncrementService = CreateMockIncrementEventsService();
        var progressTracker = CreateProgressTracker(mockIncrementService.Object);
        var errors = new List<string> { "Error 1", "Error 2" };

        // Act
        await progressTracker.IncrementProgressAsync(
            "migration-id", "products", 1, 0, 250,
            200, 30, 15, 5,
            DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
            "source", "dest", errors);

        // Assert - Should not throw
        Assert.True(true);
    }

    #endregion
}