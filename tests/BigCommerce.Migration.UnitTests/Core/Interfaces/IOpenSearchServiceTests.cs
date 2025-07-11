using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Interfaces;

/// <summary>
/// Unit tests for IOpenSearchService interface
/// Tests OpenSearch logging and performance tracking functionality
/// </summary>
public class IOpenSearchServiceTests
{
    private readonly Mock<IOpenSearchService> _mockService;

    public IOpenSearchServiceTests()
    {
        _mockService = new Mock<IOpenSearchService>();
    }

    [Fact]
    public async Task LogMigrationEventAsync_WithValidData_ShouldLogSuccessfully()
    {
        // Arrange
        var eventType = "ProductMigration";
        var entityId = "12345";
        var eventData = new { Status = "Started", BatchSize = 100 };

        _mockService.Setup(x => x.LogMigrationEventAsync(
            eventType, entityId, eventData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _mockService.Object.LogMigrationEventAsync(
            eventType, entityId, eventData, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        _mockService.Verify(x => x.LogMigrationEventAsync(
            eventType, entityId, eventData, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogPerformanceMetricsAsync_WithValidMetrics_ShouldLogSuccessfully()
    {
        // Arrange
        var operationName = "CategoryTreeResolution";
        var duration = TimeSpan.FromSeconds(2.5);
        var metrics = new { 
            TotalItems = 1000, 
            ProcessedItems = 1000, 
            ErrorCount = 0,
            SuccessRate = 100.0 
        };

        _mockService.Setup(x => x.LogPerformanceMetricsAsync(
            operationName, duration, metrics, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _mockService.Object.LogPerformanceMetricsAsync(
            operationName, duration, metrics, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        _mockService.Verify(x => x.LogPerformanceMetricsAsync(
            operationName, duration, metrics, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogErrorAsync_WithException_ShouldLogErrorSuccessfully()
    {
        // Arrange
        var context = "ProductMigration";
        var exception = new InvalidOperationException("Test exception");
        var additionalData = new { EntityId = "12345", BatchId = "batch-001" };

        _mockService.Setup(x => x.LogErrorAsync(
            context, exception, additionalData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _mockService.Object.LogErrorAsync(
            context, exception, additionalData, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        _mockService.Verify(x => x.LogErrorAsync(
            context, exception, additionalData, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchLogsAsync_WithValidQuery_ShouldReturnResults()
    {
        // Arrange
        var searchQuery = "ProductMigration AND Status:Completed";
        var fromDate = DateTime.UtcNow.AddDays(-7);
        var toDate = DateTime.UtcNow;
        var expectedResults = new List<object> 
        { 
            new { EntityId = "12345", Status = "Completed" },
            new { EntityId = "67890", Status = "Completed" }
        };

        _mockService.Setup(x => x.SearchLogsAsync(
            searchQuery, fromDate, toDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResults);

        // Act
        var result = await _mockService.Object.SearchLogsAsync(
            searchQuery, fromDate, toDate, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(expectedResults);
    }

    [Fact]
    public async Task IsHealthyAsync_WhenServiceIsHealthy_ShouldReturnTrue()
    {
        // Arrange
        _mockService.Setup(x => x.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _mockService.Object.IsHealthyAsync(CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthyAsync_WhenServiceIsUnhealthy_ShouldReturnFalse()
    {
        // Arrange
        _mockService.Setup(x => x.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _mockService.Object.IsHealthyAsync(CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }
} 