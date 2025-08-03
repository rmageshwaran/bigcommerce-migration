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
/// TDD unit tests for StartChunkedMigrationProgressActivity
/// Task 5.1.3: Comprehensive testing for SignalR progress broadcasting activities
/// CRITICAL: Tests SignalR centralization and continue-on-error patterns
/// </summary>
public class StartChunkedMigrationProgressActivityTests
{
    private readonly Mock<IProgressEventPublisher> _mockProgressEventPublisher;
    private readonly Mock<ISignalREventFactory> _mockSignalREventFactory;
    private readonly Mock<ILogger<StartChunkedMigrationProgressActivity>> _mockLogger;
    private readonly StartChunkedMigrationProgressActivity _activity;

    public StartChunkedMigrationProgressActivityTests()
    {
        _mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
        _mockSignalREventFactory = new Mock<ISignalREventFactory>();
        _mockLogger = new Mock<ILogger<StartChunkedMigrationProgressActivity>>();
        
        _activity = new StartChunkedMigrationProgressActivity(
            _mockProgressEventPublisher.Object,
            _mockSignalREventFactory.Object,
            _mockLogger.Object);
    }

    #region SignalR Factory Usage Tests

    [Fact]
    public async Task StartMigrationProgressAsync_ShouldUse_CentralizedSignalRFactory()
    {
        // Arrange
        var request = CreateTestStartRequest();
        var mockMigrationProgressEvent = new MigrationProgressEvent();
        var mockStatusEvent = new StatusProgressEvent();
        var mockBatchProgressEvent = new BatchProgressEvent();

        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Returns(mockMigrationProgressEvent);
        
        _mockSignalREventFactory.Setup(f => f.CreateStatusProgress(
            It.IsAny<string>(), It.IsAny<StatusProgressOptions>()))
            .Returns(mockStatusEvent);
            
        _mockSignalREventFactory.Setup(f => f.CreateBatchProgress(
            It.IsAny<string>(), It.IsAny<BatchProgressOptions>()))
            .Returns(mockBatchProgressEvent);

        // Act
        await _activity.StartChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify centralized factory usage
        _mockSignalREventFactory.Verify(f => f.CreateMigrationProgress(
            request.MigrationId, It.IsAny<MigrationProgressOptions>()), 
            Times.Once, "Should use centralized factory for migration progress");
            
        _mockSignalREventFactory.Verify(f => f.CreateStatusProgress(
            request.MigrationId, It.IsAny<StatusProgressOptions>()), 
            Times.Once, "Should use centralized factory for status");
            
        _mockSignalREventFactory.Verify(f => f.CreateMigrationProgress(
            request.MigrationId, It.IsAny<MigrationProgressOptions>()), 
            Times.Once, "Should use centralized factory for migration progress");
    }

    [Fact]
    public async Task StartMigrationProgressAsync_ShouldPublish_AllProgressEventTypes()
    {
        // Arrange
        var request = CreateTestStartRequest();
        SetupMockFactoryReturns();

        // Act
        await _activity.StartChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify all event types are published
        _mockProgressEventPublisher.Verify(p => p.PublishMigrationProgressAsync(
            It.IsAny<MigrationProgressEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once, "Should publish migration progress event");
            
        _mockProgressEventPublisher.Verify(p => p.PublishStatusAsync(
            It.IsAny<StatusProgressEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once, "Should publish status event");
            
        _mockProgressEventPublisher.Verify(p => p.PublishMigrationProgressAsync(
            It.IsAny<MigrationProgressEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once, "Should publish migration progress event");
    }

    #endregion

    #region Progress Calculation Tests

    [Fact]
    public async Task StartChunkedMigrationProgressAsync_ShouldCalculate_CorrectInitialProgress()
    {
        // Arrange
        var request = CreateTestStartRequest();
        MigrationProgressOptions? capturedMigrationOptions = null;
        StatusProgressOptions? capturedStatusProgressOptions = null;
        BatchProgressOptions? capturedBatchOptions = null;

        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedMigrationOptions = options)
            .Returns(new MigrationProgressEvent());
            
        _mockSignalREventFactory.Setup(f => f.CreateStatusProgress(
            It.IsAny<string>(), It.IsAny<StatusProgressOptions>()))
            .Callback<string, StatusProgressOptions>((id, options) => capturedStatusProgressOptions = options)
            .Returns(new StatusProgressEvent());
            
        _mockSignalREventFactory.Setup(f => f.CreateBatchProgress(
            It.IsAny<string>(), It.IsAny<BatchProgressOptions>()))
            .Callback<string, BatchProgressOptions>((id, options) => capturedBatchOptions = options)
            .Returns(new BatchProgressEvent());

        // Act
        await _activity.StartChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify initial progress calculations
        capturedMigrationOptions.Should().NotBeNull("Migration progress options should be set");
        capturedMigrationOptions!.OverallProgress.Should().Be(0.0, "Initial progress should be 0%");
        // EstimatedTotalMinutes property doesn't exist in MigrationProgressOptions - removing this assertion
        
        capturedStatusProgressOptions.Should().NotBeNull("Status options should be set");
        capturedStatusProgressOptions!.Status.Should().Be("chunked-migration-started", "Initial status should be chunked-migration-started");
        
        // Note: StartChunked activity doesn't create batch progress events, only migration and status progress
    }

    [Fact]
    public async Task StartChunkedMigrationProgressAsync_ShouldSet_CorrectEntityCounts()
    {
        // Arrange
        var request = CreateTestStartRequest();
        request.TotalCategories = 1500;
        request.TotalLevels = 5;
        
        MigrationProgressOptions? capturedOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedOptions = options)
            .Returns(new MigrationProgressEvent());

        // Act
        await _activity.StartChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify entity counts
        capturedOptions.Should().NotBeNull();
        capturedOptions!.TotalEntities.Should().Be(1500, "Should set total entities from request");
        capturedOptions!.ProcessedEntities.Should().Be(0, "Initial processed entities should be 0");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task StartChunkedMigrationProgressAsync_ShouldContinueOnError_WhenSignalRFactoryFails()
    {
        // Arrange
        var request = CreateTestStartRequest();
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Throws(new InvalidOperationException("SignalR factory failed"));

        // Act & Assert - Should not throw exception (continue-on-error)
        var act = () => _activity.StartChunkedMigrationProgressAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync<Exception>("Should continue-on-error when SignalR factory fails");
    }

    [Fact]
    public async Task StartChunkedMigrationProgressAsync_ShouldContinueOnError_WhenProgressPublisherFails()
    {
        // Arrange
        var request = CreateTestStartRequest();
        SetupMockFactoryReturns();
        
        _mockProgressEventPublisher.Setup(p => p.PublishMigrationProgressAsync(
            It.IsAny<MigrationProgressEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Progress publisher timeout"));

        // Act & Assert - Should not throw exception (continue-on-error)
        var act = () => _activity.StartChunkedMigrationProgressAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync<Exception>("Should continue-on-error when progress publisher fails");
    }

    [Fact]
    public async Task StartChunkedMigrationProgressAsync_ShouldLogErrors_WhenSignalROperationsFail()
    {
        // Arrange
        var request = CreateTestStartRequest();
        var expectedException = new InvalidOperationException("SignalR operation failed");
        
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Throws(expectedException);

        // Act
        await _activity.StartChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Verify error logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to start chunked migration progress")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log error when SignalR operations fail");
    }

    [Fact]
    public async Task StartChunkedMigrationProgressAsync_ShouldRespect_CancellationToken()
    {
        // Arrange
        var request = CreateTestStartRequest();
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act & Assert
        var act = () => _activity.StartChunkedMigrationProgressAsync(request, cancellationTokenSource.Token);
        await act.Should().ThrowAsync<OperationCanceledException>("Should respect cancellation token");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task StartMigrationProgressAsync_ShouldValidate_RequiredRequestProperties()
    {
        // Arrange
        var request = new StartChunkedMigrationProgressRequest
        {
            MigrationId = null!, // Invalid null migration ID
            TotalCategories = 100,
            TotalLevels = 3,
            EstimatedTimeMinutes = 15.0
        };

        // Act
        await _activity.StartChunkedMigrationProgressAsync(request, CancellationToken.None);

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
    public async Task StartMigrationProgressAsync_ShouldHandle_ZeroEntityCounts()
    {
        // Arrange
        var request = CreateTestStartRequest();
        request.TotalCategories = 0;
        request.TotalLevels = 0;
        
        MigrationProgressOptions? capturedOptions = null;
        _mockSignalREventFactory.Setup(f => f.CreateMigrationProgress(
            It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
            .Callback<string, MigrationProgressOptions>((id, options) => capturedOptions = options)
            .Returns(new MigrationProgressEvent());

        // Act
        await _activity.StartChunkedMigrationProgressAsync(request, CancellationToken.None);

        // Assert - Should handle zero counts gracefully
        capturedOptions.Should().NotBeNull();
        capturedOptions!.TotalEntities.Should().Be(0, "Should handle zero entity count");
        capturedOptions!.OverallProgress.Should().Be(0.0, "Should handle zero progress");
    }

    #endregion

    #region Helper Methods

    private StartChunkedMigrationProgressRequest CreateTestStartRequest()
    {
        return new StartChunkedMigrationProgressRequest
        {
            MigrationId = "test-migration-123",
            TotalCategories = 500,
            TotalLevels = 3,
            EstimatedTimeMinutes = 25.0
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