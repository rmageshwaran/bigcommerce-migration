using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Orchestration.Services;

/// <summary>
/// Unit tests for ProgressTracker persistence functionality
/// These tests focus specifically on the entity progress persistence feature I added
/// Following TDD principles: Red-Green-Refactor
/// </summary>
    public class ProgressTrackerPersistenceTests
    {
        private readonly Mock<ILogger<ProgressTracker>> _mockLogger;
        private readonly Mock<IProgressEventPublisher> _mockProgressEventPublisher;
        private readonly Mock<IMigrationStorageService> _mockStorageService;
        private readonly ProgressTracker _progressTracker;
        private readonly string _testMigrationId = "test-migration-123";

        public ProgressTrackerPersistenceTests()
        {
            _mockLogger = new Mock<ILogger<ProgressTracker>>();
            _mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
            _mockStorageService = new Mock<IMigrationStorageService>();
            
            _progressTracker = new ProgressTracker(
                _mockLogger.Object, 
                _mockProgressEventPublisher.Object,
                _mockStorageService.Object);
        }

    #region TDD Test 1: Entity Progress Should Persist When Updated

    [Fact]
    public async Task UpdateProgressAsync_WithEntityProgress_ShouldPersistToStorage()
    {
        // Arrange
        var update = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "categories",
            Phase = "processing",
            ProcessedCount = 50,
            SuccessCount = 45,
            FailureCount = 5,
            Timestamp = DateTime.UtcNow
        };

        var migrationEntry = new MigrationEntry
        {
            Id = _testMigrationId,
            Status = MigrationStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _mockStorageService
            .Setup(x => x.GetMigrationAsync(_testMigrationId))
            .ReturnsAsync(migrationEntry);

        _mockStorageService
            .Setup(x => x.UpdateMigrationAsync(It.IsAny<MigrationEntry>()))
            .ReturnsAsync((MigrationEntry entry) => entry);

        _mockStorageService
            .Setup(x => x.CreateOrUpdateEntityProgressAsync(It.IsAny<EntityProgressEntry>()))
            .ReturnsAsync((EntityProgressEntry entry) => entry);

        // Act - First start entity processing, then update progress (this is the actual usage pattern)
        await _progressTracker.StartEntityProcessingAsync(_testMigrationId, "categories", 100);
        await _progressTracker.UpdateProgressAsync(_testMigrationId, update);

        // Assert
        _mockStorageService.Verify(
            x => x.CreateOrUpdateEntityProgressAsync(It.Is<EntityProgressEntry>(entry =>
                entry.MigrationId == _testMigrationId &&
                entry.EntityType == "categories" &&
                entry.ProcessedCount == 50 &&
                entry.SuccessCount == 45 &&
                entry.FailureCount == 5)),
            Times.Once);
    }

    #endregion

    #region TDD Test 2: Multiple Entity Types Should All Persist

    [Fact]
    public async Task UpdateProgressAsync_WithMultipleEntityTypes_ShouldPersistAllTypes()
    {
        // Arrange
        var migrationEntry = CreateTestMigrationEntry();
        SetupMockStorageService(migrationEntry);

        // Start two different entity types
        await _progressTracker.StartEntityProcessingAsync(_testMigrationId, "categories", 100);
        await _progressTracker.StartEntityProcessingAsync(_testMigrationId, "products", 200);

        // Update progress for both
        var categoriesUpdate = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "categories",
            ProcessedCount = 50,
            SuccessCount = 48,
            FailureCount = 2
        };

        var productsUpdate = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "products",
            ProcessedCount = 75,
            SuccessCount = 70,
            FailureCount = 5
        };

        // Act
        await _progressTracker.UpdateProgressAsync(_testMigrationId, categoriesUpdate);
        await _progressTracker.UpdateProgressAsync(_testMigrationId, productsUpdate);

        // Assert
        _mockStorageService.Verify(
            x => x.CreateOrUpdateEntityProgressAsync(It.Is<EntityProgressEntry>(entry =>
                entry.EntityType == "categories")),
            Times.AtLeastOnce);

        _mockStorageService.Verify(
            x => x.CreateOrUpdateEntityProgressAsync(It.Is<EntityProgressEntry>(entry =>
                entry.EntityType == "products")),
            Times.AtLeastOnce);
    }

    #endregion

    #region TDD Test 3: GetProgressAsync Should Reconstruct From Storage When Cache Empty

    [Fact]
    public async Task GetProgressAsync_WhenCacheEmpty_ShouldReconstructFromStorage()
    {
        // Arrange
        var migrationEntry = CreateTestMigrationEntry();
        var entityProgressEntries = new List<EntityProgressEntry>
        {
            new EntityProgressEntry
            {
                MigrationId = _testMigrationId,
                EntityType = "categories",
                TotalCount = 100,
                ProcessedCount = 50,
                SuccessCount = 45,
                FailureCount = 5,
                ProgressPercentage = 50.0,
                Status = "processing"
            },
            new EntityProgressEntry
            {
                MigrationId = _testMigrationId,
                EntityType = "products",
                TotalCount = 200,
                ProcessedCount = 100,
                SuccessCount = 95,
                FailureCount = 5,
                ProgressPercentage = 50.0,
                Status = "processing"
            }
        };

        _mockStorageService
            .Setup(x => x.GetMigrationAsync(_testMigrationId))
            .ReturnsAsync(migrationEntry);

        _mockStorageService
            .Setup(x => x.GetEntityProgressAsync(_testMigrationId, null))
            .ReturnsAsync(entityProgressEntries);

        // Act
        var progress = await _progressTracker.GetProgressAsync(_testMigrationId);

        // Assert
        progress.Should().NotBeNull();
        progress.EntityProgress.Should().HaveCount(2);
        progress.EntityProgress.Should().ContainKey("categories");
        progress.EntityProgress.Should().ContainKey("products");
        
        var categoriesProgress = progress.EntityProgress["categories"];
        categoriesProgress.TotalCount.Should().Be(100);
        categoriesProgress.ProcessedCount.Should().Be(50);
        categoriesProgress.SuccessCount.Should().Be(45);
        categoriesProgress.FailureCount.Should().Be(5);
    }

    #endregion

    #region TDD Test 4: Storage Failures Should Not Break Progress Updates

    [Fact]
    public async Task UpdateProgressAsync_WhenStorageFails_ShouldContinueWithoutException()
    {
        // Arrange
        var update = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "categories",
            ProcessedCount = 50
        };

        var migrationEntry = CreateTestMigrationEntry();
        _mockStorageService
            .Setup(x => x.GetMigrationAsync(_testMigrationId))
            .ReturnsAsync(migrationEntry);

        _mockStorageService
            .Setup(x => x.UpdateMigrationAsync(It.IsAny<MigrationEntry>()))
            .ReturnsAsync(migrationEntry);

        // Start entity processing to set up entity progress
        await _progressTracker.StartEntityProcessingAsync(_testMigrationId, "categories", 100);

        // Simulate storage failure for entity progress
        _mockStorageService
            .Setup(x => x.CreateOrUpdateEntityProgressAsync(It.IsAny<EntityProgressEntry>()))
            .ThrowsAsync(new InvalidOperationException("Storage connection failed"));

        // Act & Assert - Should not throw exception
        var exception = await Record.ExceptionAsync(() => 
            _progressTracker.UpdateProgressAsync(_testMigrationId, update));

        exception.Should().BeNull();

        // Verify warning was logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to persist entity progress")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region TDD Test 5: Entity Progress Timestamps Should Be Accurate

    [Fact]
    public async Task UpdateProgressAsync_ShouldSetAccurateTimestamps()
    {
        // Arrange
        var beforeUpdate = DateTime.UtcNow;
        var update = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "categories",
            ProcessedCount = 50
        };

        var migrationEntry = CreateTestMigrationEntry();
        SetupMockStorageService(migrationEntry);

        // Start entity processing to set up entity progress
        await _progressTracker.StartEntityProcessingAsync(_testMigrationId, "categories", 100);

        EntityProgressEntry? capturedEntry = null;
        _mockStorageService
            .Setup(x => x.CreateOrUpdateEntityProgressAsync(It.IsAny<EntityProgressEntry>()))
            .Callback<EntityProgressEntry>(entry => capturedEntry = entry)
            .ReturnsAsync((EntityProgressEntry entry) => entry);

        // Act
        await _progressTracker.UpdateProgressAsync(_testMigrationId, update);
        var afterUpdate = DateTime.UtcNow;

        // Assert
        capturedEntry.Should().NotBeNull();
        capturedEntry!.CreatedAt.Should().BeOnOrAfter(beforeUpdate);
        capturedEntry.CreatedAt.Should().BeOnOrBefore(afterUpdate);
        capturedEntry.UpdatedAt.Should().BeOnOrAfter(beforeUpdate);
        capturedEntry.UpdatedAt.Should().BeOnOrBefore(afterUpdate);
    }

    #endregion

    #region TDD Test 6: Progress Percentage Should Be Calculated Correctly

    [Fact]
    public async Task UpdateProgressAsync_ShouldCalculateCorrectProgressPercentage()
    {
        // Arrange
        var migrationEntry = CreateTestMigrationEntry();
        SetupMockStorageService(migrationEntry);

        await _progressTracker.StartEntityProcessingAsync(_testMigrationId, "categories", 100);

        var update = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "categories",
            ProcessedCount = 75,
            SuccessCount = 70,
            FailureCount = 5
        };

        EntityProgressEntry? capturedEntry = null;
        _mockStorageService
            .Setup(x => x.CreateOrUpdateEntityProgressAsync(It.IsAny<EntityProgressEntry>()))
            .Callback<EntityProgressEntry>(entry => capturedEntry = entry)
            .ReturnsAsync((EntityProgressEntry entry) => entry);

        // Act
        await _progressTracker.UpdateProgressAsync(_testMigrationId, update);

        // Assert
        capturedEntry.Should().NotBeNull();
        capturedEntry!.TotalCount.Should().Be(100);
        capturedEntry.ProcessedCount.Should().Be(75);
        capturedEntry.ProgressPercentage.Should().Be(75.0);
    }

    #endregion

    #region Helper Methods

    private MigrationEntry CreateTestMigrationEntry()
    {
        return new MigrationEntry
        {
            Id = _testMigrationId,
            Status = MigrationStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TotalEntities = 0,
            ProcessedEntities = 0,
            FailedEntities = 0
        };
    }

    private void SetupMockStorageService(MigrationEntry migrationEntry)
    {
        _mockStorageService
            .Setup(x => x.GetMigrationAsync(_testMigrationId))
            .ReturnsAsync(migrationEntry);

        _mockStorageService
            .Setup(x => x.UpdateMigrationAsync(It.IsAny<MigrationEntry>()))
            .ReturnsAsync((MigrationEntry entry) => entry);

        _mockStorageService
            .Setup(x => x.CreateOrUpdateEntityProgressAsync(It.IsAny<EntityProgressEntry>()))
            .ReturnsAsync((EntityProgressEntry entry) => entry);
    }

    #endregion
} 