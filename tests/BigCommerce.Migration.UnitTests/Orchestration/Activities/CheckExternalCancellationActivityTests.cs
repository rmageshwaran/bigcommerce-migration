using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.UnitTests.Orchestration.Activities;

/// <summary>
/// Unit tests for CheckExternalCancellationActivity to validate external cancellation checking,
/// error handling, logging, and deterministic behavior across different scenarios.
/// </summary>
[Trait("Category", "DeterministicCancellation")]
[Trait("Component", "Activities")]
public class CheckExternalCancellationActivityTests
{
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ILogger<CheckExternalCancellationActivity>> _mockLogger;
    private readonly CheckExternalCancellationActivity _activity;
    private const string TestMigrationId = "test-migration-456";
    private const string TestReason = "User requested cancellation";

    public CheckExternalCancellationActivityTests()
    {
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockLogger = new Mock<ILogger<CheckExternalCancellationActivity>>();
        _activity = new CheckExternalCancellationActivity(_mockStorageService.Object, _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullStorageService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CheckExternalCancellationActivity(null!, _mockLogger.Object));

        Assert.Equal("storageService", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CheckExternalCancellationActivity(_mockStorageService.Object, null!));

        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act
        var activity = new CheckExternalCancellationActivity(_mockStorageService.Object, _mockLogger.Object);

        // Assert
        Assert.NotNull(activity);
    }

    #endregion

    #region Request Validation Tests

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithNullRequest_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _activity.CheckExternalCancellationOnceAsync(null!));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithInvalidRequest_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest
        {
            MigrationId = string.Empty // Invalid
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _activity.CheckExternalCancellationOnceAsync(request));

        Assert.Contains("Invalid request: Migration ID is required", exception.Message);
        Assert.Equal("request", exception.ParamName);
    }

    #endregion

    #region No Cancellation Found Tests

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithNoCancellationToken_ShouldReturnNotCancelled()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        
        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);

        // Act
        var result = await _activity.CheckExternalCancellationOnceAsync(request);

        // Assert
        Assert.False(result.IsCancelled);
        Assert.False(result.IsProcessed);
        Assert.Empty(result.CancellationReason);
        Assert.Null(result.CancelledAt);
        
        // Verify logging
        VerifyLogContains(LogLevel.Information, "NO CANCELLATION: No cancellation token found");
    }

    #endregion

    #region Unprocessed Cancellation Tests

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithUnprocessedCancellationToken_ShouldReturnCancelled()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        var cancelTime = DateTime.UtcNow.AddMinutes(-5);
        
        var cancellationToken = new CancellationTokenEntry
        {
            MigrationId = TestMigrationId,
            Reason = TestReason,
            RequestedAt = cancelTime,
            IsProcessed = false
        };

        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync(cancellationToken);

        // Act
        var result = await _activity.CheckExternalCancellationOnceAsync(request);

        // Assert
        Assert.True(result.IsCancelled);
        Assert.False(result.IsProcessed);
        Assert.Equal(TestReason, result.CancellationReason);
        Assert.Equal(cancelTime, result.CancelledAt);
        
        // Verify logging
        VerifyLogContains(LogLevel.Warning, "EXTERNAL CANCELLATION DETECTED");
        VerifyLogContains(LogLevel.Information, "DETERMINISTIC RESULT");
    }

    #endregion

    #region Processed Cancellation Tests

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithProcessedCancellationToken_ShouldReturnNotCancelled()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        var cancelTime = DateTime.UtcNow.AddMinutes(-10);
        
        var cancellationToken = new CancellationTokenEntry
        {
            MigrationId = TestMigrationId,
            Reason = TestReason,
            RequestedAt = cancelTime,
            IsProcessed = true,
            ProcessedAt = DateTime.UtcNow.AddMinutes(-8)
        };

        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync(cancellationToken);

        // Act
        var result = await _activity.CheckExternalCancellationOnceAsync(request);

        // Assert
        Assert.False(result.IsCancelled);
        Assert.True(result.IsProcessed);
        Assert.Equal(TestReason, result.CancellationReason);
        Assert.Equal(cancelTime, result.CancelledAt);
        
        // Verify logging
        VerifyLogContains(LogLevel.Information, "PROCESSED CANCELLATION");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithStorageException_ShouldReturnSafeDefault()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        var storageException = new InvalidOperationException("Storage connection failed");
        
        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ThrowsAsync(storageException);

        // Act
        var result = await _activity.CheckExternalCancellationOnceAsync(request);

        // Assert - Should return safe default (not cancelled) to allow migration to continue
        Assert.False(result.IsCancelled);
        Assert.False(result.IsProcessed);
        Assert.Contains("Error checking cancellation", result.CancellationReason);
        Assert.Contains("Storage connection failed", result.CancellationReason);
        
        // Verify error logging
        VerifyLogContains(LogLevel.Error, "ERROR: Failed to check external cancellation state");
    }

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithOperationCancelledException_ShouldThrow()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        
        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _activity.CheckExternalCancellationOnceAsync(request, cancellationTokenSource.Token));
        
        // Verify logging
        VerifyLogContains(LogLevel.Information, "External cancellation check was cancelled");
    }

    #endregion

    #region Determinism Tests

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithSameInput_ShouldReturnConsistentResults()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        var cancelTime = DateTime.UtcNow.AddMinutes(-3);
        
        var cancellationToken = new CancellationTokenEntry
        {
            MigrationId = TestMigrationId,
            Reason = TestReason,
            RequestedAt = cancelTime,
            IsProcessed = false
        };

        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync(cancellationToken);

        // Act - Multiple calls with same input
        var result1 = await _activity.CheckExternalCancellationOnceAsync(request);
        var result2 = await _activity.CheckExternalCancellationOnceAsync(request);
        var result3 = await _activity.CheckExternalCancellationOnceAsync(request);

        // Assert - All results should be identical
        Assert.Equal(result1.IsCancelled, result2.IsCancelled);
        Assert.Equal(result2.IsCancelled, result3.IsCancelled);
        Assert.Equal(result1.IsProcessed, result2.IsProcessed);
        Assert.Equal(result2.IsProcessed, result3.IsProcessed);
        Assert.Equal(result1.CancellationReason, result2.CancellationReason);
        Assert.Equal(result2.CancellationReason, result3.CancellationReason);
        Assert.Equal(result1.CancelledAt, result2.CancelledAt);
        Assert.Equal(result2.CancelledAt, result3.CancelledAt);
        
        // All should be cancelled
        Assert.True(result1.IsCancelled);
        Assert.True(result2.IsCancelled);
        Assert.True(result3.IsCancelled);
    }

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithDifferentMigrationIds_ShouldReturnDifferentResults()
    {
        // Arrange
        var request1 = new CheckExternalCancellationRequest { MigrationId = "migration-1" };
        var request2 = new CheckExternalCancellationRequest { MigrationId = "migration-2" };
        
        var cancellationToken1 = new CancellationTokenEntry
        {
            MigrationId = "migration-1",
            Reason = TestReason,
            RequestedAt = DateTime.UtcNow.AddMinutes(-5),
            IsProcessed = false
        };

        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync("migration-1"))
            .ReturnsAsync(cancellationToken1);
        
        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync("migration-2"))
            .ReturnsAsync((CancellationTokenEntry?)null);

        // Act
        var result1 = await _activity.CheckExternalCancellationOnceAsync(request1);
        var result2 = await _activity.CheckExternalCancellationOnceAsync(request2);

        // Assert - Results should be different
        Assert.True(result1.IsCancelled);   // migration-1 is cancelled
        Assert.False(result2.IsCancelled);  // migration-2 is not cancelled
        Assert.Equal(TestReason, result1.CancellationReason);
        Assert.Empty(result2.CancellationReason);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithNullReason_ShouldHandleGracefully()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        var cancellationToken = new CancellationTokenEntry
        {
            MigrationId = TestMigrationId,
            Reason = null!, // Null reason - explicitly marked for test
            RequestedAt = DateTime.UtcNow.AddMinutes(-2),
            IsProcessed = false
        };

        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync(cancellationToken);

        // Act
        var result = await _activity.CheckExternalCancellationOnceAsync(request);

        // Assert
        Assert.True(result.IsCancelled);
        Assert.Equal("Unknown reason", result.CancellationReason);
    }

    [Fact]
    public async Task CheckExternalCancellationOnceAsync_WithVeryLongMigrationId_ShouldWork()
    {
        // Arrange
        var longMigrationId = new string('a', 500); // Very long migration ID
        var request = new CheckExternalCancellationRequest { MigrationId = longMigrationId };
        
        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(longMigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);

        // Act
        var result = await _activity.CheckExternalCancellationOnceAsync(request);

        // Assert
        Assert.False(result.IsCancelled);
        
        // Verify the storage service was called with the correct ID
        _mockStorageService.Verify(s => s.GetCancellationTokenAsync(longMigrationId), Times.Once);
    }

    #endregion

    #region Helper Methods

    private void VerifyLogContains(LogLevel level, string message)
    {
        _mockLogger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion
} 