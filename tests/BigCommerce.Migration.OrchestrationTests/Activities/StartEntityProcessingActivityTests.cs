using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Activities;

public class StartEntityProcessingActivityTests
{
    private readonly Mock<IProgressTracker> _mockProgressTracker;
    private readonly Mock<ILogger<StartEntityProcessingActivity>> _mockLogger;
    private readonly StartEntityProcessingActivity _activity;

    public StartEntityProcessingActivityTests()
    {
        _mockProgressTracker = new Mock<IProgressTracker>();
        _mockLogger = new Mock<ILogger<StartEntityProcessingActivity>>();
        _activity = new StartEntityProcessingActivity(_mockProgressTracker.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task StartEntityProcessingAsync_WithValidRequest_ShouldCallProgressTracker()
    {
        // Arrange
        var request = new StartEntityProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            TotalCount = 10
        };

        _mockProgressTracker.Setup(x => x.StartEntityProcessingAsync(
            request.MigrationId, 
            request.EntityType, 
            request.TotalCount, 
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _activity.StartEntityProcessingAsync(request, CancellationToken.None);

        // Assert
        _mockProgressTracker.Verify(x => x.StartEntityProcessingAsync(
            request.MigrationId, 
            request.EntityType, 
            request.TotalCount, 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartEntityProcessingAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var request = new StartEntityProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            TotalCount = 10
        };

        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _activity.StartEntityProcessingAsync(request, cancellationToken));
    }

    [Fact]
    public async Task StartEntityProcessingAsync_WhenProgressTrackerThrows_ShouldRethrow()
    {
        // Arrange
        var request = new StartEntityProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            TotalCount = 10
        };

        var expectedException = new InvalidOperationException("Test exception");
        _mockProgressTracker.Setup(x => x.StartEntityProcessingAsync(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<int>(), 
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _activity.StartEntityProcessingAsync(request, CancellationToken.None));
        
        Assert.Same(expectedException, exception);
    }
} 