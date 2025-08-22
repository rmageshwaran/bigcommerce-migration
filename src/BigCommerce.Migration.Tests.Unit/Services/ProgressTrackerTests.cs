using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Services;

/// <summary>
/// Unit tests for ProgressTracker, specifically testing the TotalCount update fix for progressive discovery
/// </summary>
public class ProgressTrackerTests
{
    private readonly Mock<ILogger<ProgressTracker>> _mockLogger;
    private readonly Mock<IProgressEventPublisher> _mockProgressEventPublisher;
    private readonly Mock<ISignalREventFactory> _mockSignalREventFactory;
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<IIncrementEventsService> _mockIncrementEventsService;
    private readonly ProgressTracker _progressTracker;

    public ProgressTrackerTests()
    {
        _mockLogger = new Mock<ILogger<ProgressTracker>>();
        _mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
        _mockSignalREventFactory = new Mock<ISignalREventFactory>();
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockIncrementEventsService = new Mock<IIncrementEventsService>();
        
        _progressTracker = new ProgressTracker(
            _mockLogger.Object,
            _mockProgressEventPublisher.Object,
            _mockSignalREventFactory.Object,
            _mockStorageService.Object,
            _mockIncrementEventsService.Object);
    }

    [Fact]
    public async Task UpdateProgressAsync_WithTotalCountUpdate_ShouldUpdateEntityTotalCount()
    {
        // Arrange
        var migrationId = "test-migration-id";
        var entityType = "options";
        
        // Start with TotalCount = 0 (progressive discovery)
        await _progressTracker.StartEntityProcessingAsync(migrationId, entityType, 0, CancellationToken.None);
        
        // Create update with TotalCount = 3 (completion update)
        var progressUpdate = new ProgressUpdate
        {
            MigrationId = migrationId,
            EntityType = entityType,
            Phase = "Completed",
            TotalCount = 3, // This is the fix - updating TotalCount from 0 to actual processed count
            ProcessedCount = 3,
            SuccessCount = 3,
            FailureCount = 0,
            SkippedCount = 0,
            CancelledCount = 0,
            Timestamp = DateTime.UtcNow
        };

        // Act
        await _progressTracker.UpdateProgressAsync(migrationId, progressUpdate, CancellationToken.None);
        
        // Get the updated progress
        var progress = await _progressTracker.GetProgressAsync(migrationId, CancellationToken.None);

        // Assert
        Assert.NotNull(progress);
        Assert.True(progress.EntityProgress.ContainsKey(entityType));
        
        var entityProgress = progress.EntityProgress[entityType];
        Assert.Equal(3, entityProgress.TotalCount); // Should be updated from 0 to 3
        Assert.Equal(3, entityProgress.ProcessedCount);
        Assert.Equal(3, entityProgress.SuccessCount);
        Assert.Equal(0, entityProgress.FailureCount);
    }

    [Fact]
    public async Task UpdateProgressAsync_WithoutTotalCountUpdate_ShouldKeepOriginalTotalCount()
    {
        // Arrange
        var migrationId = "test-migration-id";
        var entityType = "products";
        
        // Start with TotalCount = 5 (regular discovery)
        await _progressTracker.StartEntityProcessingAsync(migrationId, entityType, 5, CancellationToken.None);
        
        // Create update without TotalCount (regular progress update)
        var progressUpdate = new ProgressUpdate
        {
            MigrationId = migrationId,
            EntityType = entityType,
            Phase = "Processing",
            TotalCount = 0, // No TotalCount update (0 means no change)
            ProcessedCount = 2,
            SuccessCount = 2,
            FailureCount = 0,
            SkippedCount = 0,
            CancelledCount = 0,
            Timestamp = DateTime.UtcNow
        };

        // Act
        await _progressTracker.UpdateProgressAsync(migrationId, progressUpdate, CancellationToken.None);
        
        // Get the updated progress
        var progress = await _progressTracker.GetProgressAsync(migrationId, CancellationToken.None);

        // Assert
        Assert.NotNull(progress);
        Assert.True(progress.EntityProgress.ContainsKey(entityType));
        
        var entityProgress = progress.EntityProgress[entityType];
        Assert.Equal(5, entityProgress.TotalCount); // Should remain original value
        Assert.Equal(2, entityProgress.ProcessedCount);
        Assert.Equal(2, entityProgress.SuccessCount);
    }

    [Fact]
    public async Task UpdateProgressAsync_ProgressiveDiscoveryScenario_ShouldUpdateAllComponentEntities()
    {
        // Arrange - Simulates the exact scenario from migration 9df507cc-bca6-459b-8556-241e324bc11a
        var migrationId = "9df507cc-bca6-459b-8556-241e324bc11a";
        var componentEntities = new[]
        {
            ("options", 3, 3, 0),    // 3 processed, 3 success, 0 failed
            ("modifiers", 1, 0, 1),  // 1 processed, 0 success, 1 failed  
            ("images", 2, 2, 0),     // 2 processed, 2 success, 0 failed
            ("variants", 12, 12, 0) // 12 processed, 12 success, 0 failed
        };

        // Start all entities with TotalCount = 0 (progressive discovery)
        foreach (var (entityType, _, _, _) in componentEntities)
        {
            await _progressTracker.StartEntityProcessingAsync(migrationId, entityType, 0, CancellationToken.None);
        }

        // Act - Update each entity with completion data
        foreach (var (entityType, processed, success, failed) in componentEntities)
        {
            var progressUpdate = new ProgressUpdate
            {
                MigrationId = migrationId,
                EntityType = entityType,
                Phase = "Completed",
                TotalCount = processed, // Set TotalCount to actual processed count
                ProcessedCount = processed,
                SuccessCount = success,
                FailureCount = failed,
                SkippedCount = 0,
                CancelledCount = 0,
                Timestamp = DateTime.UtcNow
            };

            await _progressTracker.UpdateProgressAsync(migrationId, progressUpdate, CancellationToken.None);
        }

        // Assert - Get the final progress
        var progress = await _progressTracker.GetProgressAsync(migrationId, CancellationToken.None);
        Assert.NotNull(progress);

        // Verify each entity has correct TotalCount
        foreach (var (entityType, processed, success, failed) in componentEntities)
        {
            Assert.True(progress.EntityProgress.ContainsKey(entityType), $"Missing entity: {entityType}");
            
            var entityProgress = progress.EntityProgress[entityType];
            Assert.Equal(processed, entityProgress.TotalCount); // TotalCount should match processed count
            Assert.Equal(processed, entityProgress.ProcessedCount);
            Assert.Equal(success, entityProgress.SuccessCount);
            Assert.Equal(failed, entityProgress.FailureCount);
            
            // Verify progress percentage calculation is correct
            var expectedPercentage = processed > 0 ? 100.0 : 0.0; // Should be 100% since all entities are completed
            Assert.Equal(expectedPercentage, entityProgress.ProgressPercentage);
        }
    }

    [Fact]
    public async Task UpdateProgressAsync_WithZeroTotalCountAndZeroProcessed_ShouldNotUpdateTotalCount()
    {
        // Arrange
        var migrationId = "test-migration-id";
        var entityType = "reviews"; // Often has 0 entities
        
        // Start with TotalCount = 0 (progressive discovery)
        await _progressTracker.StartEntityProcessingAsync(migrationId, entityType, 0, CancellationToken.None);
        
        // Create update with TotalCount = 0 and ProcessedCount = 0 (no entities found)
        var progressUpdate = new ProgressUpdate
        {
            MigrationId = migrationId,
            EntityType = entityType,
            Phase = "Completed",
            TotalCount = 0, // No entities processed, so TotalCount remains 0
            ProcessedCount = 0,
            SuccessCount = 0,
            FailureCount = 0,
            SkippedCount = 0,
            CancelledCount = 0,
            Timestamp = DateTime.UtcNow
        };

        // Act
        await _progressTracker.UpdateProgressAsync(migrationId, progressUpdate, CancellationToken.None);
        
        // Get the updated progress
        var progress = await _progressTracker.GetProgressAsync(migrationId, CancellationToken.None);

        // Assert
        Assert.NotNull(progress);
        Assert.True(progress.EntityProgress.ContainsKey(entityType));
        
        var entityProgress = progress.EntityProgress[entityType];
        Assert.Equal(0, entityProgress.TotalCount); // Should remain 0
        Assert.Equal(0, entityProgress.ProcessedCount);
        Assert.Equal(0, entityProgress.SuccessCount);
        Assert.Equal(0.0, entityProgress.ProgressPercentage); // Should be 0%
    }

    [Theory]
    [InlineData(0, 5, 5)] // Progressive discovery: 0 -> 5
    [InlineData(0, 3, 3)] // Progressive discovery: 0 -> 3
    [InlineData(0, 1, 1)] // Progressive discovery: 0 -> 1
    [InlineData(10, 10, 10)] // Regular discovery: stays 10
    [InlineData(5, 0, 5)] // No update: stays 5 (0 means no change)
    public async Task UpdateProgressAsync_TotalCountScenarios_ShouldHandleCorrectly(
        int initialTotal, 
        int updateTotal, 
        int expectedFinalTotal)
    {
        // Arrange
        var migrationId = "test-migration-id";
        var entityType = "test-entity";
        
        await _progressTracker.StartEntityProcessingAsync(migrationId, entityType, initialTotal, CancellationToken.None);
        
        var progressUpdate = new ProgressUpdate
        {
            MigrationId = migrationId,
            EntityType = entityType,
            Phase = "Completed",
            TotalCount = updateTotal,
            ProcessedCount = updateTotal == 0 ? 0 : updateTotal,
            SuccessCount = updateTotal == 0 ? 0 : updateTotal,
            FailureCount = 0,
            SkippedCount = 0,
            CancelledCount = 0,
            Timestamp = DateTime.UtcNow
        };

        // Act
        await _progressTracker.UpdateProgressAsync(migrationId, progressUpdate, CancellationToken.None);
        
        // Get the updated progress
        var progress = await _progressTracker.GetProgressAsync(migrationId, CancellationToken.None);

        // Assert
        Assert.NotNull(progress);
        Assert.True(progress.EntityProgress.ContainsKey(entityType));
        
        var entityProgress = progress.EntityProgress[entityType];
        Assert.Equal(expectedFinalTotal, entityProgress.TotalCount);
    }
}