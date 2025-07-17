using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Services;

/// <summary>
/// Tests for EntityTransformService to verify strategy pattern delegation and transformation orchestration
/// </summary>
public class EntityTransformServiceTests
{
    private readonly Mock<IEntityTransformStrategyFactory> _mockStrategyFactory;
    private readonly Mock<ILogger<EntityTransformService>> _mockLogger;
    private readonly Mock<IEntityTransformStrategy> _mockStrategy;
    private readonly EntityTransformService _service;
    private readonly StoreConfiguration _sourceStore;
    private readonly StoreConfiguration _destinationStore;

    public EntityTransformServiceTests()
    {
        _mockStrategyFactory = new Mock<IEntityTransformStrategyFactory>();
        _mockLogger = new Mock<ILogger<EntityTransformService>>();
        _mockStrategy = new Mock<IEntityTransformStrategy>();
        
        _service = new EntityTransformService(_mockStrategyFactory.Object, _mockLogger.Object);
        
        _sourceStore = new StoreConfiguration
        {
            StoreId = "source-store",
            AccessToken = "source-token",
            ChannelId = "1"
        };
        
        _destinationStore = new StoreConfiguration
        {
            StoreId = "dest-store",
            AccessToken = "dest-token",
            ChannelId = "2"
        };

        // Setup factory to return our mock strategy
        _mockStrategyFactory.Setup(x => x.GetStrategy(It.IsAny<string>()))
            .Returns(_mockStrategy.Object);
    }

    [Fact]
    public async Task TransformEntityAsync_WhenEntityProvided_ShouldDelegateToStrategy()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["id"] = "123", // This should be removed by common cleanup
            ["name"] = "Test Entity",
            ["date_created"] = "2023-01-01", // This should be removed by common cleanup
            ["custom_field"] = "value"
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = _sourceStore,
            DestinationStore = _destinationStore,
            CategoryTreeContext = new CategoryTreeContext { DestinationCategoryTreeId = "1" }
        };

        var expectedResult = new Dictionary<string, object>
        {
            ["name"] = "Test Entity",
            ["custom_field"] = "value",
            ["transformed"] = true
        };

        _mockStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                request.MigrationId,
                request.SourceStore,
                request.DestinationStore,
                request.CategoryTreeContext,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.TransformEntityAsync(sourceEntity, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result);
        
        _mockStrategyFactory.Verify(x => x.GetStrategy("categories"), Times.Once);
        _mockStrategy.Verify(x => x.TransformEntityAsync(
            It.Is<Dictionary<string, object>>(d => 
                !d.ContainsKey("id") && 
                !d.ContainsKey("date_created") && 
                d.ContainsKey("name") && 
                d.ContainsKey("custom_field")),
            request.MigrationId,
            request.SourceStore,
            request.DestinationStore,
            request.CategoryTreeContext,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransformEntityAsync_ShouldRemoveSystemFields()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["id"] = "source-123",
            ["name"] = "Test Entity",
            ["date_created"] = "2023-01-01",
            ["date_modified"] = "2023-01-02",
            ["_links"] = new { self = "http://example.com" },
            ["_meta"] = new { total = 1 },
            ["_original_entity_id"] = "original-123"
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            SourceStore = _sourceStore,
            DestinationStore = _destinationStore
        };

        _mockStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["name"] = "Test Entity" });

        // Act
        await _service.TransformEntityAsync(sourceEntity, request);

        // Assert - Verify system fields were removed before passing to strategy
        _mockStrategy.Verify(x => x.TransformEntityAsync(
            It.Is<Dictionary<string, object>>(d => 
                !d.ContainsKey("id") &&
                !d.ContainsKey("date_created") &&
                !d.ContainsKey("date_modified") &&
                !d.ContainsKey("_links") &&
                !d.ContainsKey("_meta") &&
                !d.ContainsKey("_original_entity_id") &&
                d.ContainsKey("name")),
            It.IsAny<string>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<CategoryTreeContext?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransformEntityAsync_WhenNullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = _sourceStore,
            DestinationStore = _destinationStore
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.TransformEntityAsync(null!, request));
        
        // Strategy should not be called
        _mockStrategyFactory.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Never);
        _mockStrategy.Verify(x => x.TransformEntityAsync(
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<CategoryTreeContext?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TransformEntityAsync_WhenNullRequest_ShouldThrowArgumentNullException()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["name"] = "Test Entity"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.TransformEntityAsync(sourceEntity, null!));
        
        // Strategy should not be called
        _mockStrategyFactory.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Never);
        _mockStrategy.Verify(x => x.TransformEntityAsync(
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<StoreConfiguration>(),
            It.IsAny<CategoryTreeContext?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TransformEntityAsync_WhenStrategyThrows_ShouldPropagateException()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["name"] = "Test Entity"
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = _sourceStore,
            DestinationStore = _destinationStore
        };

        var expectedException = new InvalidOperationException("Strategy failed");
        _mockStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.TransformEntityAsync(sourceEntity, request));
        
        Assert.Equal("Entity transformation failed for categories", exception.Message);
        Assert.Equal(expectedException, exception.InnerException);
        
        _mockStrategyFactory.Verify(x => x.GetStrategy("categories"), Times.Once);
        _mockStrategy.Verify(x => x.TransformEntityAsync(
            It.IsAny<Dictionary<string, object>>(),
            request.MigrationId,
            request.SourceStore,
            request.DestinationStore,
            request.CategoryTreeContext,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransformEntityAsync_ShouldPassCorrectEntityTypeToFactory()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["name"] = "Test Product"
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            SourceStore = _sourceStore,
            DestinationStore = _destinationStore
        };

        _mockStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Act
        await _service.TransformEntityAsync(sourceEntity, request);

        // Assert
        _mockStrategyFactory.Verify(x => x.GetStrategy("products"), Times.Once);
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("products")]
    [InlineData("brands")]
    [InlineData("variants")]
    [InlineData("images")]
    [InlineData("modifiers")]
    public async Task TransformEntityAsync_ShouldSupportAllEntityTypes(string entityType)
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["name"] = "Test Entity"
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = entityType,
            SourceStore = _sourceStore,
            DestinationStore = _destinationStore
        };

        var expectedResult = new Dictionary<string, object>
        {
            ["name"] = "Test Entity",
            ["transformed"] = true
        };

        _mockStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.TransformEntityAsync(sourceEntity, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedResult, result);
        
        _mockStrategyFactory.Verify(x => x.GetStrategy(entityType), Times.Once);
        _mockStrategy.Verify(x => x.TransformEntityAsync(
            It.IsAny<Dictionary<string, object>>(),
            request.MigrationId,
            request.SourceStore,
            request.DestinationStore,
            request.CategoryTreeContext,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransformEntityAsync_ShouldNotModifyOriginalEntity()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["id"] = "123",
            ["name"] = "Test Entity"
        };

        var originalEntity = new Dictionary<string, object>(sourceEntity);

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = _sourceStore,
            DestinationStore = _destinationStore
        };

        _mockStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Act
        await _service.TransformEntityAsync(sourceEntity, request);

        // Assert - Original entity should remain unchanged
        Assert.Equal(originalEntity, sourceEntity);
        Assert.True(sourceEntity.ContainsKey("id")); // Should still have system fields
        Assert.True(sourceEntity.ContainsKey("name"));
    }
} 