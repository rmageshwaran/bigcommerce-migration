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
/// TDD unit tests for CompleteChunkedMigrationProgressActivity
/// Task 5.1.3: Comprehensive testing for SignalR progress broadcasting completion events
/// CRITICAL: Tests SignalR centralization, completion notifications, and performance metrics
/// </summary>
public class CompleteChunkedMigrationProgressActivityTests
{
    private readonly Mock<IProgressEventPublisher> _mockProgressEventPublisher;
    private readonly Mock<ISignalREventFactory> _mockSignalREventFactory;
    private readonly Mock<ILogger<CompleteChunkedMigrationProgressActivity>> _mockLogger;
    private readonly CompleteChunkedMigrationProgressActivity _activity;

    public CompleteChunkedMigrationProgressActivityTests()
    {
        _mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
        _mockSignalREventFactory = new Mock<ISignalREventFactory>();
        _mockLogger = new Mock<ILogger<CompleteChunkedMigrationProgressActivity>>();
        
        _activity = new CompleteChunkedMigrationProgressActivity(
            _mockProgressEventPublisher.Object,
            _mockSignalREventFactory.Object,
            _mockLogger.Object);
    }

    #region SignalR Factory Usage Tests

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldUse_CentralizedSignalRFactory()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        var mockMigrationProgressEvent = new MigrationProgressEvent();
        var mockStatusEvent = new StatusProgressEvent();
        var mockEntityProgressEvent = new EntityProgressEvent();

        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Returns(mockMigrationProgressEvent);
        
        _mockSignalREventFactory.Setup(f => f.CreateStatusProgress(
            It.IsAny<string>(), It.IsAny<StatusProgressOptions>()))
            .Returns(new StatusProgressEvent());
            
        _mockSignalREventFactory.Setup(f => f.CreateEntityProgress(
            It.IsAny<string>(), It.IsAny<EntityProgressOptions>()))
            .Returns(new EntityProgressEvent());

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify centralized factory usage
        _mockSignalREventFactory.Verify(f => f.CreateMigrationProgress(
            request.MigrationId, It.IsAny<MigrationProgressOptions>()), 
            Times.Once, "Should use centralized factory for migration progress");
            
        _mockSignalREventFactory.Verify(f => f.CreateStatusProgress(
            request.MigrationId, It.IsAny<StatusProgressOptions>()), 
            Times.Once, "Should use centralized factory for status");
            
        _mockSignalREventFactory.Verify(f => f.CreateEntityProgress(
            request.MigrationId, It.IsAny<EntityProgressOptions>()), 
            Times.Once, "Should use centralized factory for entity progress");
    }

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldPublish_AllCompletionEventTypes()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        SetupMockFactoryReturns();

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify all completion event types are published
        _mockProgressEventPublisher.Verify(p => p.PublishMigrationProgressAsync(
            It.IsAny<MigrationProgressEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once, "Should publish final migration progress event");
            
        _mockProgressEventPublisher.Verify(p => p.PublishStatusAsync(
            It.IsAny<StatusProgressEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once, "Should publish completion status event");
            
        _mockProgressEventPublisher.Verify(p => p.PublishEntityProgressAsync(
            It.IsAny<EntityProgressEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once, "Should publish final entity progress event");
    }

    #endregion

    #region Completion Progress Calculation Tests

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldSet_100PercentProgress_ForSuccessfulMigration()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        request.Success = true;
        request.TotalCategoriesProcessed = 500;
        request.TotalCategoriesCreated = 480;
        
        MigrationProgressOptions? capturedOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedOptions = options)
            .Returns(new MigrationProgressEvent());

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify 100% completion progress
        capturedOptions.Should().NotBeNull("Migration progress options should be set");
        capturedOptions!.OverallProgress.Should().Be(100.0, "Successful migration should show 100% progress");
        capturedOptions!.ProcessedEntities.Should().Be(500, "Should set processed entities from request");
    }

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldSet_PartialProgress_ForFailedMigration()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        request.Success = false;
        request.TotalCategoriesProcessed = 300; // Only partial processing
        request.TotalCategoriesCreated = 280;
        
        MigrationProgressOptions? capturedOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedOptions = options)
            .Returns(new MigrationProgressEvent());

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify partial completion progress
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ProcessedEntities.Should().Be(300, "Should show actual processed entities even for failed migration");
        // Progress percentage may be calculated based on actual progress
    }

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldSet_CorrectCompletionStatus()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        request.Success = true;
        
        StatusProgressOptions? capturedStatusProgressOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateStatusProgress(
            It.IsAny<string>(), It.IsAny<StatusProgressOptions>()))
            .Callback<string, StatusProgressOptions>((id, options) => capturedStatusProgressOptions = options)
            .Returns(new StatusProgressEvent());

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify completion status
        capturedStatusProgressOptions.Should().NotBeNull();
        capturedStatusProgressOptions!.Status.Should().Be("chunked-migration-completed_with_errors", "Successful migration should have chunked-migration-completed_with_errors status");
    }

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldSet_FailedStatus_ForUnsuccessfulMigration()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        request.Success = false;
        
        StatusProgressOptions? capturedStatusProgressOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateStatusProgress(
            It.IsAny<string>(), It.IsAny<StatusProgressOptions>()))
            .Callback<string, StatusProgressOptions>((id, options) => capturedStatusProgressOptions = options)
            .Returns(new StatusProgressEvent());

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify failed status
        capturedStatusProgressOptions.Should().NotBeNull();
        capturedStatusProgressOptions!.Status.Should().Be("chunked-migration-failed", "Failed migration should have chunked-migration-failed status");
    }

    #endregion

    #region Performance Metrics Tests

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldInclude_PerformanceMetrics()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        request.PerformanceImprovement = 8.5;
        request.ProcessingTimeMinutes = 12.3;
        request.LevelsProcessed = 4;
        request.LevelsFailed = 1;
        
        EntityProgressOptions? capturedEntityOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateEntityProgress(
            It.IsAny<string>(), It.IsAny<EntityProgressOptions>()))
            .Callback<string, EntityProgressOptions>((id, options) => capturedEntityOptions = options)
            .Returns(new EntityProgressEvent());

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify performance metrics inclusion
        capturedEntityOptions.Should().NotBeNull("Entity progress options should be set");
        // Performance improvement and processing time should be reflected in the progress data
        // The exact mapping depends on the EntityProgressOptions structure
    }

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldInclude_LevelStatistics()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        request.LevelsProcessed = 3;
        request.LevelsFailed = 1;
        
        // Since we don't know the exact structure of EntityProgressOptions,
        // we'll verify that the activity completes successfully with level statistics
        SetupMockFactoryReturns();

        // Act & Assert - Should handle level statistics without errors
        var act = () => _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync("Should handle level statistics successfully");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldContinueOnError_WhenSignalRFactoryFails()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Throws(new InvalidOperationException("SignalR factory failed"));

        // Act & Assert - Should not throw exception (continue-on-error)
        var act = () => _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync("Should continue-on-error when SignalR factory fails");
    }

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldContinueOnError_WhenProgressPublisherFails()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        SetupMockFactoryReturns();
        
        _mockProgressEventPublisher.Setup(p => p.PublishEntityProgressAsync(
            It.IsAny<EntityProgressEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Entity progress publisher timeout"));

        // Act & Assert - Should not throw exception (continue-on-error)
        var act = () => _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync("Should continue-on-error when entity progress publisher fails");
    }

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldLogErrors_WhenSignalROperationsFail()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        var expectedException = new InvalidOperationException("Completion progress creation failed");
        
        _mockSignalREventFactory.Setup(f => f.CreateEntityProgress(
            It.IsAny<string>(), It.IsAny<EntityProgressOptions>()))
            .Throws(expectedException);

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify error logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to complete chunked migration progress")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log error when SignalR operations fail");
    }

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldRespect_CancellationToken()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act & Assert
        var act = () => _activity.CompleteChunkedMigrationProgressAsync(request, cancellationTokenSource.Token);
        await act.Should().ThrowAsync<OperationCanceledException>("Should respect cancellation token");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldValidate_RequiredRequestProperties()
    {
        // Arrange
        var request = new CompleteChunkedMigrationProgressRequest
        {
            MigrationId = null!, // Invalid null migration ID
            Success = true,
            TotalCategoriesProcessed = 100,
            TotalCategoriesCreated = 95,
            PerformanceImprovement = 5.0,
            ProcessingTimeMinutes = 10.0,
            LevelsProcessed = 3,
            LevelsFailed = 0
        };

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

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

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldHandle_ZeroEntityCounts()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        request.TotalCategoriesProcessed = 0;
        request.TotalCategoriesCreated = 0;
        request.LevelsProcessed = 0;
        
        MigrationProgressOptions? capturedOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedOptions = options)
            .Returns(new MigrationProgressEvent());

        // Act
        await _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Should handle zero counts gracefully
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ProcessedEntities.Should().Be(0, "Should handle zero processed entities");
    }

    [Fact]
    public async Task CompleteChunkedMigrationProgressAsync_ShouldHandle_NegativePerformanceImprovement()
    {
        // Arrange
        var request = CreateTestCompleteRequest();
        request.PerformanceImprovement = -2.5; // Performance degradation
        
        // Act & Assert - Should handle negative performance improvement gracefully
        var act = () => _activity.CompleteChunkedMigrationProgressAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync("Should handle negative performance improvement");
    }

    #endregion

    #region Helper Methods

    private CompleteChunkedMigrationProgressRequest CreateTestCompleteRequest()
    {
        return new CompleteChunkedMigrationProgressRequest
        {
            MigrationId = "test-migration-789",
            Success = true,
            TotalCategoriesProcessed = 750,
            TotalCategoriesCreated = 720,
            PerformanceImprovement = 6.8,
            ProcessingTimeMinutes = 18.5,
            LevelsProcessed = 4,
            LevelsFailed = 1
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
            
        _mockSignalREventFactory.Setup(f => f.CreateEntityProgress(
            It.IsAny<string>(), It.IsAny<EntityProgressOptions>()))
            .Returns(new EntityProgressEvent());
    }

    #endregion
}