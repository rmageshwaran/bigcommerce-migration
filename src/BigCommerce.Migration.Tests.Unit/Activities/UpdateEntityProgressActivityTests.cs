using BigCommerce.Migration.Activities.Activities;
using BigCommerce.Migration.Activities.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Activities;

/// <summary>
/// Unit tests for UpdateEntityProgressActivity, specifically testing the TotalCount update fix during entity completion
/// </summary>
public class UpdateEntityProgressActivityTests
{
    private readonly Mock<IProgressTracker> _mockProgressTracker;
    private readonly Mock<ILogger<UpdateEntityProgressActivity>> _mockLogger;
    private readonly UpdateEntityProgressActivity _activity;

    public UpdateEntityProgressActivityTests()
    {
        _mockProgressTracker = new Mock<IProgressTracker>();
        _mockLogger = new Mock<ILogger<UpdateEntityProgressActivity>>();
        
        _activity = new UpdateEntityProgressActivity(_mockProgressTracker.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task UpdateEntityProgressAsync_WithCompletedPhaseAndProgressiveDiscovery_ShouldUpdateTotalCount()
    {
        // Arrange
        var migrationId = "test-migration-id";
        var entityType = "options";
        
        var progressUpdate = new UpdateEntityProgressRequest
        {
            MigrationId = migrationId,
            EntityType = entityType,
            Phase = "Completed",
            TotalEntities = 0, // Progressive discovery starts with 0
            ProcessedEntities = 3,
            SuccessfulEntities = 3,
            FailedEntities = 0,
            SkippedEntities = 0,
            CancelledEntities = 0,
            Timestamp = DateTime.UtcNow
        };

        // Mock the aggregated progress that would come from ChunkIncrementEvents
        var mockAggregatedProgress = new MigrationProgress
        {
            MigrationId = migrationId,
            EntityProgress = new Dictionary<string, EntityProgress>
            {
                [entityType] = new EntityProgress
                {
                    EntityType = entityType,
                    TotalCount = 0, // Still 0 from initial discovery
                    ProcessedCount = 3,
                    SuccessCount = 3,
                    FailureCount = 0,
                    SkippedCount = 0,
                    CancelledCount = 0
                }
            }
        };

        _mockProgressTracker
            .Setup(x => x.GetLatestAggregatedProgressAsync(migrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAggregatedProgress);

        _mockProgressTracker
            .Setup(x => x.UpdateProgressAsync(migrationId, It.IsAny<ProgressUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockProgressTracker
            .Setup(x => x.CompleteEntityProcessingAsync(migrationId, entityType, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _activity.UpdateEntityProgressAsync(progressUpdate, CancellationToken.None);

        // Assert
        // Verify that UpdateProgressAsync was called twice (regular update + enhanced update)
        _mockProgressTracker.Verify(
            x => x.UpdateProgressAsync(migrationId, It.IsAny<ProgressUpdate>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Verify that the enhanced update includes TotalCount = ProcessedCount (3)
        _mockProgressTracker.Verify(
            x => x.UpdateProgressAsync(migrationId, 
                It.Is<ProgressUpdate>(update => 
                    update.TotalCount == 3 && // TotalCount should be set to ProcessedCount
                    update.ProcessedCount == 3 &&
                    update.SuccessCount == 3 &&
                    update.Phase == "Completed"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify CompleteEntityProcessingAsync was called
        _mockProgressTracker.Verify(
            x => x.CompleteEntityProcessingAsync(migrationId, entityType, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateEntityProgressAsync_WithCompletedPhaseAndRegularDiscovery_ShouldKeepOriginalTotalCount()
    {
        // Arrange
        var migrationId = "test-migration-id";
        var entityType = "products";
        
        var progressUpdate = new UpdateEntityProgressRequest
        {
            MigrationId = migrationId,
            EntityType = entityType,
            Phase = "Completed",
            TotalEntities = 5, // Regular discovery has known total
            ProcessedEntities = 5,
            SuccessfulEntities = 5,
            FailedEntities = 0,
            SkippedEntities = 0,
            CancelledEntities = 0,
            Timestamp = DateTime.UtcNow
        };

        // Mock the aggregated progress with known TotalCount
        var mockAggregatedProgress = new MigrationProgress
        {
            MigrationId = migrationId,
            EntityProgress = new Dictionary<string, EntityProgress>
            {
                [entityType] = new EntityProgress
                {
                    EntityType = entityType,
                    TotalCount = 5, // Known total from regular discovery
                    ProcessedCount = 5,
                    SuccessCount = 5,
                    FailureCount = 0,
                    SkippedCount = 0,
                    CancelledCount = 0
                }
            }
        };

        _mockProgressTracker
            .Setup(x => x.GetLatestAggregatedProgressAsync(migrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAggregatedProgress);

        _mockProgressTracker
            .Setup(x => x.UpdateProgressAsync(migrationId, It.IsAny<ProgressUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockProgressTracker
            .Setup(x => x.CompleteEntityProcessingAsync(migrationId, entityType, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _activity.UpdateEntityProgressAsync(progressUpdate, CancellationToken.None);

        // Assert
        // Verify that the enhanced update keeps original TotalCount (5)
        _mockProgressTracker.Verify(
            x => x.UpdateProgressAsync(migrationId, 
                It.Is<ProgressUpdate>(update => 
                    update.TotalCount == 5 && // TotalCount should remain 5
                    update.ProcessedCount == 5 &&
                    update.SuccessCount == 5 &&
                    update.Phase == "Completed"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateEntityProgressAsync_WithNonCompletedPhase_ShouldNotTriggerCompletionLogic()
    {
        // Arrange
        var migrationId = "test-migration-id";
        var entityType = "images";
        
        var progressUpdate = new UpdateEntityProgressRequest
        {
            MigrationId = migrationId,
            EntityType = entityType,
            Phase = "Processing", // Not completed
            TotalEntities = 0,
            ProcessedEntities = 1,
            SuccessfulEntities = 1,
            FailedEntities = 0,
            SkippedEntities = 0,
            CancelledEntities = 0,
            Timestamp = DateTime.UtcNow
        };

        _mockProgressTracker
            .Setup(x => x.UpdateProgressAsync(migrationId, It.IsAny<ProgressUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _activity.UpdateEntityProgressAsync(progressUpdate, CancellationToken.None);

        // Assert
        // Should only call UpdateProgressAsync once (regular update, no enhanced update)
        _mockProgressTracker.Verify(
            x => x.UpdateProgressAsync(migrationId, It.IsAny<ProgressUpdate>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Should not call GetLatestAggregatedProgressAsync
        _mockProgressTracker.Verify(
            x => x.GetLatestAggregatedProgressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Should not call CompleteEntityProcessingAsync
        _mockProgressTracker.Verify(
            x => x.CompleteEntityProcessingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("options", 0, 3, 3, 0, 3)] // Progressive discovery options: 0 -> 3
    [InlineData("modifiers", 0, 1, 0, 1, 1)] // Progressive discovery modifiers: 0 -> 1
    [InlineData("images", 0, 2, 2, 0, 2)] // Progressive discovery images: 0 -> 2
    [InlineData("product-variants", 12, 12, 12, 0, 12)] // Regular discovery: stays 12
    [InlineData("reviews", 0, 0, 0, 0, 0)] // No entities: stays 0
    public async Task UpdateEntityProgressAsync_VariousScenarios_ShouldHandleTotalCountCorrectly(
        string entityType,
        int initialTotalCount,
        int processedCount,
        int successCount,
        int failedCount,
        int expectedTotalCount)
    {
        // Arrange
        var migrationId = "test-migration-id";
        
        var progressUpdate = new UpdateEntityProgressRequest
        {
            MigrationId = migrationId,
            EntityType = entityType,
            Phase = "Completed",
            TotalEntities = initialTotalCount,
            ProcessedEntities = processedCount,
            SuccessfulEntities = successCount,
            FailedEntities = failedCount,
            SkippedEntities = 0,
            CancelledEntities = 0,
            Timestamp = DateTime.UtcNow
        };

        var mockAggregatedProgress = new MigrationProgress
        {
            MigrationId = migrationId,
            EntityProgress = new Dictionary<string, EntityProgress>
            {
                [entityType] = new EntityProgress
                {
                    EntityType = entityType,
                    TotalCount = initialTotalCount,
                    ProcessedCount = processedCount,
                    SuccessCount = successCount,
                    FailureCount = failedCount,
                    SkippedCount = 0,
                    CancelledCount = 0
                }
            }
        };

        _mockProgressTracker
            .Setup(x => x.GetLatestAggregatedProgressAsync(migrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAggregatedProgress);

        _mockProgressTracker
            .Setup(x => x.UpdateProgressAsync(migrationId, It.IsAny<ProgressUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockProgressTracker
            .Setup(x => x.CompleteEntityProcessingAsync(migrationId, entityType, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _activity.UpdateEntityProgressAsync(progressUpdate, CancellationToken.None);

                // Assert
        // Verify that UpdateProgressAsync was called at least twice:
        // 1. The initial progress update call
        // 2. The enhanced completion call with real aggregated data
        _mockProgressTracker.Verify(
            x => x.UpdateProgressAsync(migrationId, It.IsAny<ProgressUpdate>(), It.IsAny<CancellationToken>()), 
            Times.AtLeast(2));

        // Verify CompleteEntityProcessingAsync was called once
        _mockProgressTracker.Verify(
            x => x.CompleteEntityProcessingAsync(migrationId, entityType, It.IsAny<CancellationToken>()),
            Times.Once());
    }
}