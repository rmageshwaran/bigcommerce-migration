using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System;

namespace BigCommerce.Migration.OrchestrationTests.Activities;

/// <summary>
/// Unit tests for BroadcastMigrationEventActivity following TDD principles
/// Tests cover all SignalR broadcasting activities for real-time dashboard integration
/// </summary>
public class BroadcastMigrationEventActivityTests
{
    private readonly Mock<ILogger<BroadcastMigrationEventActivity>> _mockLogger;
    private readonly Mock<IMigrationSignalRService> _mockSignalRService;
    private readonly BroadcastMigrationEventActivity _activity;
    private readonly string _testMigrationId = "test-migration-456";
    private readonly string _testEntityType = "products";

    public BroadcastMigrationEventActivityTests()
    {
        _mockLogger = new Mock<ILogger<BroadcastMigrationEventActivity>>();
        _mockSignalRService = new Mock<IMigrationSignalRService>();
        _activity = new BroadcastMigrationEventActivity(_mockLogger.Object, _mockSignalRService.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new BroadcastMigrationEventActivity(null!, _mockSignalRService.Object));
    }

    [Fact]
    public void Constructor_WithNullSignalRService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new BroadcastMigrationEventActivity(_mockLogger.Object, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var activity = new BroadcastMigrationEventActivity(_mockLogger.Object, _mockSignalRService.Object);

        // Assert
        Assert.NotNull(activity);
    }

    #endregion

    #region Migration Lifecycle Broadcasting Tests

    [Fact]
    public async Task BroadcastMigrationStartedAsync_WithValidRequest_CallsSignalRServiceAndReturnsTrue()
    {
        // Arrange
        var request = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new { Status = "Started", TotalEntities = 500 }
        };
        var cancellationToken = CancellationToken.None;

        _mockSignalRService
            .Setup(s => s.BroadcastMigrationStartedAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BroadcastMigrationStartedAsync(request, cancellationToken);

        // Assert
        Assert.True(result);
        _mockSignalRService.Verify(s => s.BroadcastMigrationStartedAsync(
            _testMigrationId,
            request.Data,
            cancellationToken), Times.Once);
    }

    [Fact]
    public async Task BroadcastMigrationCompletedAsync_WithValidRequest_CallsSignalRServiceAndReturnsTrue()
    {
        // Arrange
        var request = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new { Status = "Completed", ProcessedEntities = 500, Duration = "2h 15m" }
        };
        var cancellationToken = CancellationToken.None;

        _mockSignalRService
            .Setup(s => s.BroadcastMigrationCompletedAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BroadcastMigrationCompletedAsync(request, cancellationToken);

        // Assert
        Assert.True(result);
        _mockSignalRService.Verify(s => s.BroadcastMigrationCompletedAsync(
            _testMigrationId,
            request.Data,
            cancellationToken), Times.Once);
    }

    [Fact]
    public async Task BroadcastMigrationFailedAsync_WithValidRequest_CallsSignalRServiceAndReturnsTrue()
    {
        // Arrange
        var request = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new { Status = "Failed", ErrorMessage = "API connection failed", FailedAt = DateTime.UtcNow }
        };
        var cancellationToken = CancellationToken.None;

        _mockSignalRService
            .Setup(s => s.BroadcastMigrationFailedAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BroadcastMigrationFailedAsync(request, cancellationToken);

        // Assert
        Assert.True(result);
        _mockSignalRService.Verify(s => s.BroadcastMigrationFailedAsync(
            _testMigrationId,
            request.Data,
            cancellationToken), Times.Once);
    }

    [Fact]
    public async Task BroadcastMigrationCancelledAsync_WithValidRequest_CallsSignalRServiceAndReturnsTrue()
    {
        // Arrange
        var request = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new { Status = "Cancelled", Reason = "User requested", CancelledBy = "admin@example.com" }
        };
        var cancellationToken = CancellationToken.None;

        _mockSignalRService
            .Setup(s => s.BroadcastMigrationCancelledAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BroadcastMigrationCancelledAsync(request, cancellationToken);

        // Assert
        Assert.True(result);
        _mockSignalRService.Verify(s => s.BroadcastMigrationCancelledAsync(
            _testMigrationId,
            request.Data,
            cancellationToken), Times.Once);
    }

    #endregion

    #region Entity Phase Broadcasting Tests

    [Fact]
    public async Task BroadcastEntityPhaseStartedAsync_WithValidRequest_CallsSignalRServiceAndReturnsTrue()
    {
        // Arrange
        var request = new BroadcastEntityEventRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            Data = new { TotalCount = 1500, EstimatedDuration = "45m" }
        };
        var cancellationToken = CancellationToken.None;

        _mockSignalRService
            .Setup(s => s.BroadcastEntityPhaseStartAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BroadcastEntityPhaseStartedAsync(request, cancellationToken);

        // Assert
        Assert.True(result);
        _mockSignalRService.Verify(s => s.BroadcastEntityPhaseStartAsync(
            _testMigrationId,
            _testEntityType,
            request.Data,
            cancellationToken), Times.Once);
    }

    [Fact]
    public async Task BroadcastEntityPhaseCompletedAsync_WithValidRequest_CallsSignalRServiceAndReturnsTrue()
    {
        // Arrange
        var request = new BroadcastEntityEventRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            Data = new 
            { 
                ProcessedCount = 1500, 
                SuccessCount = 1485, 
                FailedCount = 15,
                ProcessingTime = "42m 30s"
            }
        };
        var cancellationToken = CancellationToken.None;

        _mockSignalRService
            .Setup(s => s.BroadcastEntityPhaseCompletedAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BroadcastEntityPhaseCompletedAsync(request, cancellationToken);

        // Assert
        Assert.True(result);
        _mockSignalRService.Verify(s => s.BroadcastEntityPhaseCompletedAsync(
            _testMigrationId,
            _testEntityType,
            request.Data,
            cancellationToken), Times.Once);
    }

    #endregion

    #region Status Update Broadcasting Tests

    [Fact]
    public async Task BroadcastMigrationStatusUpdateAsync_WithValidRequest_CallsSignalRServiceAndReturnsTrue()
    {
        // Arrange
        var request = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new 
            { 
                Status = "Processing",
                CurrentEntity = "products",
                OverallProgress = 45.5,
                EntitiesRemaining = 3,
                EstimatedCompletion = DateTime.UtcNow.AddHours(2)
            }
        };
        var cancellationToken = CancellationToken.None;

        _mockSignalRService
            .Setup(s => s.BroadcastStatusUpdateAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BroadcastMigrationStatusUpdateAsync(request, cancellationToken);

        // Assert
        Assert.True(result);
        _mockSignalRService.Verify(s => s.BroadcastStatusUpdateAsync(
            _testMigrationId,
            request.Data,
            cancellationToken), Times.Once);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task BroadcastMigrationStartedAsync_WhenSignalRServiceThrows_LogsErrorAndReturnsFalse()
    {
        // Arrange
        var request = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new { Status = "Started" }
        };
        var expectedException = new InvalidOperationException("SignalR connection failed");

        _mockSignalRService
            .Setup(s => s.BroadcastMigrationStartedAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act
        var result = await _activity.BroadcastMigrationStartedAsync(request);

        // Assert
        Assert.False(result); // Should return false on error, not throw
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to broadcast migration started event")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastEntityPhaseStartedAsync_WhenSignalRServiceThrows_LogsErrorAndReturnsFalse()
    {
        // Arrange
        var request = new BroadcastEntityEventRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            Data = new { TotalCount = 100 }
        };
        var expectedException = new TimeoutException("SignalR timeout");

        _mockSignalRService
            .Setup(s => s.BroadcastEntityPhaseStartAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act
        var result = await _activity.BroadcastEntityPhaseStartedAsync(request);

        // Assert
        Assert.False(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to broadcast entity phase started event") 
                    && v.ToString()!.Contains(_testMigrationId) 
                    && v.ToString()!.Contains(_testEntityType)),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastMigrationCompletedAsync_WhenSignalRServiceThrows_LogsErrorAndReturnsFalse()
    {
        // Arrange
        var request = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new { Status = "Completed" }
        };
        var expectedException = new UnauthorizedAccessException("Authentication failed");

        _mockSignalRService
            .Setup(s => s.BroadcastMigrationCompletedAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act
        var result = await _activity.BroadcastMigrationCompletedAsync(request);

        // Assert
        Assert.False(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to broadcast migration completed event")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Request Model Tests

    [Fact]
    public async Task BroadcastMigrationStartedAsync_WithNullRequest_ThrowsException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<NullReferenceException>(
            () => _activity.BroadcastMigrationStartedAsync(null!));
    }

    [Fact]
    public async Task BroadcastEntityPhaseStartedAsync_WithNullRequest_ThrowsException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<NullReferenceException>(
            () => _activity.BroadcastEntityPhaseStartedAsync(null!));
    }

    [Fact]
    public async Task BroadcastMigrationStartedAsync_WithEmptyMigrationId_PassesToSignalRService()
    {
        // Arrange
        var request = new BroadcastMigrationEventRequest
        {
            MigrationId = string.Empty,
            Data = new { Status = "Started" }
        };

        _mockSignalRService
            .Setup(s => s.BroadcastMigrationStartedAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BroadcastMigrationStartedAsync(request);

        // Assert
        Assert.True(result);
        _mockSignalRService.Verify(s => s.BroadcastMigrationStartedAsync(
            string.Empty,
            request.Data,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Cancellation Token Tests

    [Fact]
    public async Task BroadcastMigrationStartedAsync_PassesCancellationTokenToSignalRService()
    {
        // Arrange
        var request = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new { Status = "Started" }
        };
        var cancellationToken = new CancellationTokenSource().Token;
        CancellationToken capturedToken = default;

        _mockSignalRService
            .Setup(s => s.BroadcastMigrationStartedAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((id, data, token) => capturedToken = token)
            .Returns(Task.CompletedTask);

        // Act
        await _activity.BroadcastMigrationStartedAsync(request, cancellationToken);

        // Assert
        Assert.Equal(cancellationToken, capturedToken);
    }

    [Fact]
    public async Task BroadcastEntityPhaseStartedAsync_PassesCancellationTokenToSignalRService()
    {
        // Arrange
        var request = new BroadcastEntityEventRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            Data = new { TotalCount = 100 }
        };
        var cancellationToken = new CancellationTokenSource().Token;
        CancellationToken capturedToken = default;

        _mockSignalRService
            .Setup(s => s.BroadcastEntityPhaseStartAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, object, CancellationToken>((id, entityType, data, token) => capturedToken = token)
            .Returns(Task.CompletedTask);

        // Act
        await _activity.BroadcastEntityPhaseStartedAsync(request, cancellationToken);

        // Assert
        Assert.Equal(cancellationToken, capturedToken);
    }

    #endregion

    #region Integration Tests with Multiple Activities

    [Fact]
    public async Task BroadcastMigrationLifecycle_AllActivities_CallSignalRServiceInSequence()
    {
        // Arrange
        var startRequest = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new { Status = "Started" }
        };
        var completedRequest = new BroadcastMigrationEventRequest
        {
            MigrationId = _testMigrationId,
            Data = new { Status = "Completed" }
        };

        _mockSignalRService.Setup(s => s.BroadcastMigrationStartedAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockSignalRService.Setup(s => s.BroadcastMigrationCompletedAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var startResult = await _activity.BroadcastMigrationStartedAsync(startRequest);
        var completedResult = await _activity.BroadcastMigrationCompletedAsync(completedRequest);

        // Assert
        Assert.True(startResult);
        Assert.True(completedResult);
        _mockSignalRService.Verify(s => s.BroadcastMigrationStartedAsync(
            _testMigrationId, startRequest.Data, It.IsAny<CancellationToken>()), Times.Once);
        _mockSignalRService.Verify(s => s.BroadcastMigrationCompletedAsync(
            _testMigrationId, completedRequest.Data, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
} 