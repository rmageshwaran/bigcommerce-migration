using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Infrastructure.Services;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// TDD unit tests for LiveCancellationManager
/// Tests define the expected behavior - implementation will follow (TDD)
/// Validates multi-level cancellation management with real-time capabilities
/// </summary>
public class LiveCancellationManagerTests
{
    private readonly Mock<ICancellationTokenRepository> _mockRepository;
    private readonly Mock<ISignalREventFactory> _mockSignalREventFactory;
    private readonly Mock<IProgressEventPublisher> _mockProgressEventPublisher;
    private readonly Mock<ILogger<LiveCancellationManager>> _mockLogger;
    private readonly LiveCancellationManager _service;

    public LiveCancellationManagerTests()
    {
        _mockRepository = new Mock<ICancellationTokenRepository>();
        _mockSignalREventFactory = new Mock<ISignalREventFactory>();
        _mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
        _mockLogger = new Mock<ILogger<LiveCancellationManager>>();
        
        _service = new LiveCancellationManager(
            _mockRepository.Object,
            _mockSignalREventFactory.Object,
            _mockProgressEventPublisher.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDependencies_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new LiveCancellationManager(
            _mockRepository.Object,
            _mockSignalREventFactory.Object,
            _mockProgressEventPublisher.Object,
            _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullRepository_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var act = () => new LiveCancellationManager(
            null!,
            _mockSignalREventFactory.Object,
            _mockProgressEventPublisher.Object,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cancellationTokenRepository");
    }

    [Fact]
    public void Constructor_WithNullSignalREventFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var act = () => new LiveCancellationManager(
            _mockRepository.Object,
            null!,
            _mockProgressEventPublisher.Object,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("signalREventFactory");
    }

    #endregion

    #region CancelAsync Tests

    [Fact]
    public async Task CancelAsync_WithValidMigrationRequest_ShouldReturnSuccessResult()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.Migration;
        var reason = "User requested cancellation";

        var expectedEntry = new EnhancedCancellationTokenEntry
        {
            MigrationId = migrationId,
            Scope = scope,
            Reason = reason,
            RequestedAt = DateTime.UtcNow,
            RequestedBy = "system",
            IsActive = true
        };

        var basicCancellationEntry = new CancellationTokenEntry
        {
            MigrationId = migrationId,
            Reason = reason,
            RequestedAt = DateTime.UtcNow,
            RequestedBy = "system",
            Status = "Active"
        };

        _mockRepository.Setup(r => r.CreateAsync(migrationId, reason))
            .ReturnsAsync(basicCancellationEntry);

        // Act
        var result = await _service.CancelAsync(migrationId, scope, reason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.MigrationId.Should().Be(migrationId);
        result.Scope.Should().Be(scope);
        result.Message.Should().Contain("successfully");

        _mockRepository.Verify(r => r.CreateAsync(migrationId, reason), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_WithEntityTypeScope_ShouldIncludeEntityType()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.EntityType;
        var reason = "Entity processing timeout";
        var entityType = "categories";

        var expectedEntry = new EnhancedCancellationTokenEntry
        {
            MigrationId = migrationId,
            Scope = scope,
            EntityType = entityType,
            Reason = reason,
            RequestedAt = DateTime.UtcNow,
            IsActive = true
        };

        var basicCancellationEntry = new CancellationTokenEntry
        {
            MigrationId = migrationId,
            Reason = reason,
            RequestedAt = DateTime.UtcNow,
            RequestedBy = "system",
            Status = "Active"
        };

        _mockRepository.Setup(r => r.CreateAsync(migrationId, reason))
            .ReturnsAsync(basicCancellationEntry);

        // Act
        var result = await _service.CancelAsync(migrationId, scope, reason, entityType);

        // Assert
        result.Success.Should().BeTrue();
        _mockRepository.Verify(r => r.CreateAsync(migrationId, reason), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_WithRepositoryException_ShouldReturnFailureResult()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.Migration;
        var reason = "Test cancellation";

        _mockRepository.Setup(r => r.CreateAsync(migrationId, reason))
            .ThrowsAsync(new InvalidOperationException("Cancellation already exists"));

        // Act
        var result = await _service.CancelAsync(migrationId, scope, reason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorDetails.Should().Contain("Cancellation already exists");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CancelAsync_WithInvalidMigrationId_ShouldThrowArgumentException(string? migrationId)
    {
        // Arrange
        var scope = CancellationScope.Migration;
        var reason = "Test cancellation";

        // Act & Assert
        var act = async () => await _service.CancelAsync(migrationId!, scope, reason);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("migrationId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CancelAsync_WithInvalidReason_ShouldThrowArgumentException(string? reason)
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.Migration;

        // Act & Assert
        var act = async () => await _service.CancelAsync(migrationId, scope, reason!);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("reason");
    }

    #endregion

    #region IsCancelledAsync Tests

    [Fact]
    public async Task IsCancelledAsync_WithActiveCancellation_ShouldReturnTrue()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.Migration;

        var existingCancellation = new CancellationTokenEntry
        {
            MigrationId = migrationId,
            Reason = "Test reason",
            IsProcessed = false
        };

        _mockRepository.Setup(r => r.GetAsync(migrationId))
            .ReturnsAsync(existingCancellation);

        // Act
        var result = await _service.IsCancelledAsync(migrationId, scope);

        // Assert
        result.Should().BeTrue();
        _mockRepository.Verify(r => r.GetAsync(migrationId), Times.Once);
    }

    [Fact]
    public async Task IsCancelledAsync_WithNoCancellation_ShouldReturnFalse()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.EntityType;

        _mockRepository.Setup(r => r.GetAsync(migrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);

        // Act
        var result = await _service.IsCancelledAsync(migrationId, scope);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCancelledAsync_WithEntityTypeScope_ShouldCheckEntitySpecificCancellation()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.EntityType;
        var entityType = "products";

        _mockRepository.Setup(r => r.IsActiveCancellationAsync(migrationId, scope, entityType, null, null))
            .ReturnsAsync(true);

        // Act
        var result = await _service.IsCancelledAsync(migrationId, scope, entityType);

        // Assert
        result.Should().BeTrue();
        _mockRepository.Verify(r => r.IsActiveCancellationAsync(migrationId, scope, entityType, null, null), Times.Once);
    }

    [Fact]
    public async Task IsCancelledAsync_WithRepositoryException_ShouldReturnFalse()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.Migration;

        _mockRepository.Setup(r => r.IsActiveCancellationAsync(migrationId, scope, null, null, null))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _service.IsCancelledAsync(migrationId, scope);

        // Assert
        result.Should().BeFalse(); // Continue-on-error policy
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error checking cancellation status")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetCancellationStatusAsync Tests

    [Fact]
    public async Task GetCancellationStatusAsync_WithActiveCancellations_ShouldReturnCorrectStatus()
    {
        // Arrange
        var migrationId = "migration-123";
        var activeCancellations = new List<EnhancedCancellationTokenEntry>
        {
            new() { MigrationId = migrationId, Scope = CancellationScope.EntityType, IsActive = true, RequestedAt = DateTime.UtcNow.AddMinutes(-5) },
            new() { MigrationId = migrationId, Scope = CancellationScope.Batch, IsActive = true, RequestedAt = DateTime.UtcNow.AddMinutes(-2) }
        };

        _mockRepository.Setup(r => r.GetActiveByMigrationAsync(migrationId))
            .ReturnsAsync(activeCancellations);

        // Act
        var result = await _service.GetCancellationStatusAsync(migrationId);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(migrationId);
        result.HasActiveCancellation.Should().BeTrue();
        result.ActiveScopes.Should().Contain(CancellationScope.EntityType);
        result.ActiveScopes.Should().Contain(CancellationScope.Batch);
        result.ActiveScopes.Should().HaveCount(2);
        result.TotalCancellationRequests.Should().Be(2);
        result.LastCancellationAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(-2), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetCancellationStatusAsync_WithNoCancellations_ShouldReturnInactiveStatus()
    {
        // Arrange
        var migrationId = "migration-123";
        var activeCancellations = new List<EnhancedCancellationTokenEntry>();

        _mockRepository.Setup(r => r.GetActiveByMigrationAsync(migrationId))
            .ReturnsAsync(activeCancellations);

        // Act
        var result = await _service.GetCancellationStatusAsync(migrationId);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(migrationId);
        result.HasActiveCancellation.Should().BeFalse();
        result.ActiveScopes.Should().BeEmpty();
        result.TotalCancellationRequests.Should().Be(0);
        result.LastCancellationAt.Should().BeNull();
    }

    #endregion

    #region PropagateToAllInstancesAsync Tests

    [Fact]
    public async Task PropagateToAllInstancesAsync_WithValidRequest_ShouldSendSignalREvent()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.EntityType;

        var expectedEvent = new CancellationProgressEvent
        {
            MigrationId = migrationId,
            Scope = scope,
            Status = "propagating",
            Timestamp = DateTime.UtcNow
        };

        _mockSignalREventFactory.Setup(f => f.CreateCancellationProgress(migrationId, It.IsAny<CancellationProgressOptions>()))
            .Returns(expectedEvent);

        // Act
        await _service.PropagateToAllInstancesAsync(migrationId, scope, null, null, null);

        // Assert
        _mockSignalREventFactory.Verify(f => f.CreateCancellationProgress(migrationId, It.IsAny<CancellationProgressOptions>()), Times.Once);
        _mockProgressEventPublisher.Verify(p => p.PublishAsync(expectedEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PropagateToAllInstancesAsync_WithSignalRException_ShouldLogErrorAndContinue()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.Migration;

        _mockSignalREventFactory.Setup(f => f.CreateCancellationProgress(migrationId, It.IsAny<CancellationProgressOptions>()))
            .Throws(new Exception("SignalR connection failed"));

        // Act & Assert - Should not throw (continue-on-error policy)
        var act = async () => await _service.PropagateToAllInstancesAsync(migrationId, scope, null, null, null);
        await act.Should().NotThrowAsync();

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error propagating cancellation")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task IsCancelledAsync_PerformanceRequirement_ShouldCompleteWithin50Ms()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.Migration;

        _mockRepository.Setup(r => r.IsActiveCancellationAsync(migrationId, scope, null, null, null))
            .Returns(Task.FromResult(true)); // Fast response for performance test

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = await _service.IsCancelledAsync(migrationId, scope);

        // Assert
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50, "Cancellation check should complete within 50ms");
        result.Should().BeTrue();
    }

    #endregion
}