using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Functions.Services;
using BigCommerce.Migration.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System;

namespace BigCommerce.Migration.UnitTests.Functions;

/// <summary>
/// Unit tests for NoOpSignalRService following TDD principles
/// Tests ensure all enhanced broadcasting methods work correctly when SignalR is disabled
/// </summary>
public class NoOpSignalRServiceTests
{
    private readonly Mock<ILogger<NoOpSignalRService>> _mockLogger;
    private readonly NoOpSignalRService _noOpService;
    private readonly string _testMigrationId = "test-migration-noop";
    private readonly string _testEntityType = "categories";

    public NoOpSignalRServiceTests()
    {
        _mockLogger = new Mock<ILogger<NoOpSignalRService>>();
        _noOpService = new NoOpSignalRService(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // Arrange & Act
        var service = new NoOpSignalRService(null!);

        // Assert
        Assert.NotNull(service);
        // Note: Constructor doesn't validate logger parameter
        // NullReferenceException will occur when methods are called
    }

    [Fact]
    public void Constructor_WithValidLogger_CreatesInstance()
    {
        // Arrange & Act
        var service = new NoOpSignalRService(_mockLogger.Object);

        // Assert
        Assert.NotNull(service);
        Assert.IsAssignableFrom<IMigrationSignalRService>(service);
    }

    #endregion

    #region Enhanced Migration Lifecycle Broadcasting Tests

    [Fact]
    public async Task BroadcastMigrationStartedAsync_CompletesSuccessfully()
    {
        // Arrange
        var migrationData = new { Status = "Started", SourceStore = "test-store" };
        var cancellationToken = CancellationToken.None;

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastMigrationStartedAsync(_testMigrationId, migrationData, cancellationToken);

        // Verify debug logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SignalR not configured") 
                    && v.ToString()!.Contains("Migration started for migration") 
                    && v.ToString()!.Contains(_testMigrationId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastMigrationCompletedAsync_CompletesSuccessfully()
    {
        // Arrange
        var completionData = new { Status = "Completed", Duration = "2h 15m" };
        var cancellationToken = CancellationToken.None;

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastMigrationCompletedAsync(_testMigrationId, completionData, cancellationToken);

        // Verify debug logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SignalR not configured") 
                    && v.ToString()!.Contains("Migration completed for migration") 
                    && v.ToString()!.Contains(_testMigrationId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastMigrationFailedAsync_CompletesSuccessfully()
    {
        // Arrange
        var errorData = new { Error = "Test error", ErrorCode = 500 };
        var cancellationToken = CancellationToken.None;

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastMigrationFailedAsync(_testMigrationId, errorData, cancellationToken);

        // Verify debug logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SignalR not configured") 
                    && v.ToString()!.Contains("Migration failed for migration") 
                    && v.ToString()!.Contains(_testMigrationId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastMigrationCancelledAsync_CompletesSuccessfully()
    {
        // Arrange
        var cancellationData = new { Reason = "User requested", CancelledBy = "admin" };
        var cancellationToken = CancellationToken.None;

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastMigrationCancelledAsync(_testMigrationId, cancellationData, cancellationToken);

        // Verify debug logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SignalR not configured") 
                    && v.ToString()!.Contains("Migration cancelled for migration") 
                    && v.ToString()!.Contains(_testMigrationId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Enhanced Entity Phase Broadcasting Tests

    [Fact]
    public async Task BroadcastEntityPhaseStartAsync_CompletesSuccessfully()
    {
        // Arrange
        var phaseData = new { TotalCount = 150, EstimatedTime = "45m" };
        var cancellationToken = CancellationToken.None;

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastEntityPhaseStartAsync(_testMigrationId, _testEntityType, phaseData, cancellationToken);

        // Verify debug logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SignalR not configured") 
                    && v.ToString()!.Contains("Entity phase start for migration") 
                    && v.ToString()!.Contains(_testMigrationId)
                    && v.ToString()!.Contains(_testEntityType)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastEntityPhaseCompletedAsync_CompletesSuccessfully()
    {
        // Arrange
        var completionData = new { ProcessedCount = 150, SuccessCount = 145, FailedCount = 5 };
        var cancellationToken = CancellationToken.None;

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastEntityPhaseCompletedAsync(_testMigrationId, _testEntityType, completionData, cancellationToken);

        // Verify debug logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SignalR not configured") 
                    && v.ToString()!.Contains("Entity phase completed for migration") 
                    && v.ToString()!.Contains(_testMigrationId)
                    && v.ToString()!.Contains(_testEntityType)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Enhanced Error and Alert Broadcasting Tests

    [Fact]
    public async Task BroadcastErrorNotificationAsync_CompletesSuccessfully()
    {
        // Arrange
        var errorData = new 
        { 
            EntityId = "123",
            ErrorMessage = "Duplicate category error",
            HttpStatusCode = 422,
            RequestPayloadBlobUrl = "https://blob.example.com/request.gz"
        };
        var cancellationToken = CancellationToken.None;

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastErrorNotificationAsync(_testMigrationId, errorData, cancellationToken);

        // Verify debug logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SignalR not configured") 
                    && v.ToString()!.Contains("Error notification for migration") 
                    && v.ToString()!.Contains(_testMigrationId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastSystemAlertAsync_CompletesSuccessfully()
    {
        // Arrange
        var alertType = "RateLimitWarning";
        var alertData = new { Message = "Approaching rate limits", Severity = "Warning" };
        var cancellationToken = CancellationToken.None;

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastSystemAlertAsync(alertType, alertData, cancellationToken);

        // Verify debug logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SignalR not configured") 
                    && v.ToString()!.Contains("System alert for type") 
                    && v.ToString()!.Contains(alertType)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Existing Functionality Tests (Regression)

    [Fact]
    public async Task BroadcastProgressUpdateAsync_CompletesSuccessfully()
    {
        // Arrange
        var migrationId = "test-migration-001";
        var progress = new MigrationProgress { MigrationId = migrationId };

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastProgressUpdateAsync(migrationId, progress);
    }

    [Fact]
    public async Task BroadcastStatusUpdateAsync_CompletesSuccessfully()
    {
        // Arrange
        var migrationId = "test-migration-001";
        var status = new { Status = "processing" };

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastStatusUpdateAsync(migrationId, status);
    }

    [Fact]
    public async Task BroadcastSystemHealthAsync_CompletesSuccessfully()
    {
        // Arrange
        var healthData = new { Status = "healthy" };

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastSystemHealthAsync(healthData);
    }

    #endregion

    #region Cancellation Token Tests

    [Fact]
    public async Task BroadcastMigrationStartedAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var migrationData = new { Status = "Started" };
        var cancellationToken = new CancellationToken();

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastMigrationStartedAsync(_testMigrationId, migrationData, cancellationToken);
    }

    [Fact]
    public async Task BroadcastEntityPhaseStartAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var phaseData = new { TotalCount = 100 };
        var cancellationToken = new CancellationToken();

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastEntityPhaseStartAsync(_testMigrationId, _testEntityType, phaseData, cancellationToken);
    }

    [Fact]
    public async Task BroadcastSystemAlertAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var alertType = "TestAlert";
        var alertData = new { Message = "Test alert" };
        var cancellationToken = new CancellationToken();

        // Act & Assert - Should complete without throwing
        await _noOpService.BroadcastSystemAlertAsync(alertType, alertData, cancellationToken);
    }

    #endregion

    #region Stress Test - All Enhanced Methods

    [Fact]
    public async Task AllEnhancedMethods_CompleteConcurrently_WithoutThrowing()
    {
        // Arrange
        var migrationData = new { Status = "Started" };
        var completionData = new { Status = "Completed" };
        var errorData = new { Error = "Test error" };
        var cancellationData = new { Reason = "User requested" };
        var phaseData = new { TotalCount = 100 };
        var completedPhaseData = new { ProcessedCount = 100 };
        var errorNotificationData = new { EntityId = "123", ErrorMessage = "Test error" };
        var alertType = "TestAlert";
        var alertData = new { Message = "Test alert" };

        // Act & Assert - All methods should complete without throwing, even concurrently
        var tasks = new[]
        {
            _noOpService.BroadcastMigrationStartedAsync(_testMigrationId, migrationData),
            _noOpService.BroadcastMigrationCompletedAsync(_testMigrationId, completionData),
            _noOpService.BroadcastMigrationFailedAsync(_testMigrationId, errorData),
            _noOpService.BroadcastMigrationCancelledAsync(_testMigrationId, cancellationData),
            _noOpService.BroadcastEntityPhaseStartAsync(_testMigrationId, _testEntityType, phaseData),
            _noOpService.BroadcastEntityPhaseCompletedAsync(_testMigrationId, _testEntityType, completedPhaseData),
            _noOpService.BroadcastErrorNotificationAsync(_testMigrationId, errorNotificationData),
            _noOpService.BroadcastSystemAlertAsync(alertType, alertData)
        };

        await Task.WhenAll(tasks);

        // Verify all debug logs were written
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SignalR not configured")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(8)); // 8 enhanced methods called
    }

    #endregion
} 