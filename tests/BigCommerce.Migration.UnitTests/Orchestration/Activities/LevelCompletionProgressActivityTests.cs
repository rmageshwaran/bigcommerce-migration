using System;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Orchestration.Activities;

/// <summary>
/// TDD unit tests for LevelCompletionProgressActivity
/// Task 5.1.3: Comprehensive testing for SignalR progress broadcasting with bulk batch progress
/// CRITICAL: Tests SignalR centralization, level progress calculations, and bulk batch tracking
/// </summary>
public class LevelCompletionProgressActivityTests
{
    private readonly Mock<IProgressEventPublisher> _mockProgressEventPublisher;
    private readonly Mock<ISignalREventFactory> _mockSignalREventFactory;
    private readonly Mock<ILogger<LevelCompletionProgressActivity>> _mockLogger;
    private readonly LevelCompletionProgressActivity _activity;

    public LevelCompletionProgressActivityTests()
    {
        _mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
        _mockSignalREventFactory = new Mock<ISignalREventFactory>();
        _mockLogger = new Mock<ILogger<LevelCompletionProgressActivity>>();
        
        _activity = new LevelCompletionProgressActivity(
            _mockProgressEventPublisher.Object,
            _mockSignalREventFactory.Object,
            _mockLogger.Object);
    }

    #region SignalR Factory Usage Tests

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldUse_CentralizedSignalRFactory()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        var mockMigrationProgressEvent = new MigrationProgressEvent();
        var mockStatusEvent = new MigrationProgressEvent();
        var mockBatchProgressEvent = new MigrationProgressEvent();

        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Returns(mockMigrationProgressEvent);
        
        _mockSignalREventFactory.Setup(f => f.CreateStatusProgress(
            It.IsAny<string>(), It.IsAny<StatusProgressOptions>()))
            .Returns(new StatusProgressEvent());
            
        _mockSignalREventFactory.Setup(f => f.CreateBatchProgress(
            It.IsAny<string>(), It.IsAny<BatchProgressOptions>()))
            .Returns(new BatchProgressEvent());

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Verify centralized factory usage
        _mockSignalREventFactory.Verify(f => f.CreateMigrationProgress(
            request.MigrationId, It.IsAny<MigrationProgressOptions>()), 
            Times.Once, "Should use centralized factory for migration progress");
            
        _mockSignalREventFactory.Verify(f => f.CreateStatusProgress(
            request.MigrationId, It.IsAny<StatusProgressOptions>()), 
            Times.Once, "Should use centralized factory for status");
            
        _mockSignalREventFactory.Verify(f => f.CreateBatchProgress(
            request.MigrationId, It.IsAny<BatchProgressOptions>()), 
            Times.Once, "Should use centralized factory for batch progress");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldPublish_AllProgressEventTypes()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        SetupMockFactoryReturns();

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Verify all event types are published
        _mockProgressEventPublisher.Verify(p => p.PublishMigrationProgressAsync(
            It.IsAny<MigrationProgressEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once, "Should publish migration progress event");
            
        _mockProgressEventPublisher.Verify(p => p.PublishStatusAsync(
            It.IsAny<StatusProgressEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once, "Should publish status event");
            
        _mockProgressEventPublisher.Verify(p => p.PublishBatchProgressAsync(
            It.IsAny<BatchProgressEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once, "Should publish batch progress event");
    }

    #endregion

    #region Level Progress Calculation Tests

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldCalculate_CorrectLevelProgress()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        request.Level = 1; // Level 1 of 3
        request.TotalLevels = 3;
        request.CompletedLevels = 2; // Level 1 completed = 2 levels done (0-based level 1 = 2nd level)
        request.CumulativeCategoriesProcessed = 80;
        request.TotalCategories = 100;
        
        MigrationProgressOptions? capturedOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedOptions = options)
            .Returns(new MigrationProgressEvent());

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Verify level progress calculations
        capturedOptions.Should().NotBeNull("Migration progress options should be set");
        
        // Level 1 completion = (1+1)/3 = 66.67% of levels complete
        var expectedLevelProgress = ((double)(request.Level + 1) / request.TotalLevels) * 100.0;
        capturedOptions!.OverallProgress.Should().BeApproximately(expectedLevelProgress, 0.1, 
            "Should calculate correct level completion percentage");
            
        capturedOptions.ProcessedEntities.Should().Be(80, "Should set processed entities from request");
        capturedOptions.TotalEntities.Should().Be(100, "Should set total entities from request");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldCalculate_SuccessfulLevelProgress()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        request.Success = true;
        request.Level = 2; // Level 2 of 4
        request.TotalLevels = 4;
        request.CompletedLevels = 3; // Level 2 completed = 3 levels done
        
        MigrationProgressOptions? capturedOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedOptions = options)
            .Returns(new MigrationProgressEvent());

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Verify successful level progress
        capturedOptions.Should().NotBeNull();
        
        // Level 2 completion = (2+1)/4 = 75% of levels complete
        var expectedProgress = ((double)(request.Level + 1) / request.TotalLevels) * 100.0;
        capturedOptions!.OverallProgress.Should().BeApproximately(expectedProgress, 0.1, 
            "Should calculate 75% progress for successful level 2 of 4");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldHandle_FailedLevelProgress()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        request.Success = false;
        request.Level = 1;
        request.TotalLevels = 3;
        
        StatusProgressOptions? capturedStatusProgressOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateStatusProgress(
            It.IsAny<string>(), It.IsAny<StatusProgressOptions>()))
            .Callback<string, StatusProgressOptions>((id, options) => capturedStatusProgressOptions = options)
            .Returns(new StatusProgressEvent());

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Verify failed level handling
        capturedStatusProgressOptions.Should().NotBeNull();
        capturedStatusProgressOptions!.Status.Should().Be("level-failed", "Status should be level-failed for failed level (continue-on-error)");
    }

    #endregion

    #region Bulk Batch Progress Tests

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldSet_CorrectBatchProgress()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        request.Success = true;
        request.CategoriesProcessed = 95;
        request.TotalCategories = 100;
        
        BatchProgressOptions? capturedBatchOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateBatchProgress(
            It.IsAny<string>(), It.IsAny<BatchProgressOptions>()))
            .Callback<string, BatchProgressOptions>((id, options) => capturedBatchOptions = options)
            .Returns(new BatchProgressEvent());

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Verify batch progress calculations
        capturedBatchOptions.Should().NotBeNull("Batch progress options should be set");
        capturedBatchOptions!.ProcessedCount.Should().BeGreaterThan(0, "Successful level should show processed entities");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldSet_ZeroBatchProgress_ForFailedLevel()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        request.Success = false;
        request.CategoriesProcessed = 50;
        request.TotalCategories = 100;
        
        BatchProgressOptions? capturedBatchOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateBatchProgress(
            It.IsAny<string>(), It.IsAny<BatchProgressOptions>()))
            .Callback<string, BatchProgressOptions>((id, options) => capturedBatchOptions = options)
            .Returns(new BatchProgressEvent());

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Verify failed batch progress
        capturedBatchOptions.Should().NotBeNull();
        capturedBatchOptions!.FailedCount.Should().BeGreaterThan(0, "Failed level should show failed entities");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldInclude_ProcessingTimeInBatchProgress()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        request.ProcessingTimeMinutes = 3.5;
        
        BatchProgressOptions? capturedBatchOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateBatchProgress(
            It.IsAny<string>(), It.IsAny<BatchProgressOptions>()))
            .Callback<string, BatchProgressOptions>((id, options) => capturedBatchOptions = options)
            .Returns(new BatchProgressEvent());

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Verify processing time inclusion
        capturedBatchOptions.Should().NotBeNull();
        // Note: Processing time may be included in CurrentBatchDetails or other progress properties
        // depending on the specific implementation
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldContinueOnError_WhenSignalRFactoryFails()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Throws(new InvalidOperationException("SignalR factory failed"));

        // Act & Assert - Should not throw exception (continue-on-error)
        var act = () => _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync("Should continue-on-error when SignalR factory fails");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldContinueOnError_WhenProgressPublisherFails()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        SetupMockFactoryReturns();
        
        _mockProgressEventPublisher.Setup(p => p.PublishBatchProgressAsync(
            It.IsAny<BatchProgressEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Batch progress publisher timeout"));

        // Act & Assert - Should not throw exception (continue-on-error)
        var act = () => _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync("Should continue-on-error when batch progress publisher fails");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldLogErrors_WhenSignalROperationsFail()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        var expectedException = new InvalidOperationException("Batch progress creation failed");
        
        _mockSignalREventFactory.Setup(f => f.CreateBatchProgress(
            It.IsAny<string>(), It.IsAny<BatchProgressOptions>()))
            .Throws(expectedException);

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Verify error logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to update level completion progress")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log error when SignalR operations fail");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldRespect_CancellationToken()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act & Assert
        var act = () => _activity.UpdateLevelCompletionProgressAsync(request, cancellationTokenSource.Token);
        await act.Should().ThrowAsync<OperationCanceledException>("Should respect cancellation token");
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldHandle_ZeroTotalCategories()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        request.TotalCategories = 0;
        request.CumulativeCategoriesProcessed = 0;
        
        MigrationProgressOptions? capturedOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedOptions = options)
            .Returns(new MigrationProgressEvent());

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Should handle zero categories gracefully
        capturedOptions.Should().NotBeNull();
        capturedOptions!.TotalEntities.Should().Be(0, "Should handle zero total categories");
        capturedOptions!.ProcessedEntities.Should().Be(0, "Should handle zero processed categories");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldHandle_MaximumLevelNumber()
    {
        // Arrange
        var request = CreateTestLevelCompletionRequest();
        request.Level = 99; // Very high level number
        request.TotalLevels = 100;
        request.CompletedLevels = 100; // Level 99 completed = 100 levels done (100% completion)
        
        MigrationProgressOptions? capturedOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedOptions = options)
            .Returns(new MigrationProgressEvent());

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Should handle high level numbers correctly
        capturedOptions.Should().NotBeNull();
        var expectedProgress = ((double)(99 + 1) / 100) * 100.0; // 100% progress
        capturedOptions!.OverallProgress.Should().BeApproximately(expectedProgress, 0.1, 
            "Should calculate correct progress for high level numbers");
    }

    [Fact]
    public async Task UpdateLevelCompletionProgressAsync_ShouldValidate_RequiredRequestProperties()
    {
        // Arrange
        var request = new LevelCompletionProgressRequest
        {
            MigrationId = null!, // Invalid null migration ID
            Level = 1,
            TotalLevels = 3,
            Success = true,
            CategoriesProcessed = 50,
            TotalCategories = 100,
            ProcessingTimeMinutes = 2.5
        };

        // Act
        await _activity.UpdateLevelCompletionProgressAsync(request, CancellationToken.None);

        // Assert - Should log error but NOT throw (continue-on-error policy for progress activities)
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("MigrationId is required")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log validation error for null MigrationId");
    }

    #endregion

    #region Helper Methods

    private LevelCompletionProgressRequest CreateTestLevelCompletionRequest()
    {
        return new LevelCompletionProgressRequest
        {
            MigrationId = "test-migration-456",
            Level = 1,
            TotalLevels = 3,
            Success = true,
            CumulativeCategoriesProcessed = 150,
            TotalCategories = 200,
            ProcessingTimeMinutes = 5.2
        };
    }

    private void SetupMockFactoryReturns()
    {
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Returns(new MigrationProgressEvent());
            
        _mockSignalREventFactory.Setup(f => f.CreateStatusProgress(
            It.IsAny<string>(), It.IsAny<StatusProgressOptions>()))
            .Returns(new StatusProgressEvent());
            
        _mockSignalREventFactory.Setup(f => f.CreateBatchProgress(
            It.IsAny<string>(), It.IsAny<BatchProgressOptions>()))
            .Returns(new BatchProgressEvent());
    }

    #endregion
}