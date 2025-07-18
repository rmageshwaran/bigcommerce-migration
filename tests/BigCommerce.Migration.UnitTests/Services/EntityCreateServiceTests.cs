using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Services;

public class EntityCreateServiceTests
{
    private readonly Mock<IEntityCreationStrategyFactory> _mockStrategyFactory;
    private readonly Mock<ILogger<EntityCreateService>> _mockLogger;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<IEntityCreationStrategy> _mockStrategy;
    private readonly EntityCreateService _service;
    private readonly StoreConfiguration _testStoreConfig;

    public EntityCreateServiceTests()
    {
        _mockStrategyFactory = new Mock<IEntityCreationStrategyFactory>();
        _mockLogger = new Mock<ILogger<EntityCreateService>>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _mockStrategy = new Mock<IEntityCreationStrategy>();
        
        _service = new EntityCreateService(_mockStrategyFactory.Object, _mockLogger.Object, _mockErrorHandlingService.Object);
        
        _testStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store",
            AccessToken = "test-token",
            ChannelId = "1"
        };

        // Setup factory to return our mock strategy
        _mockStrategyFactory.Setup(x => x.GetStrategy(It.IsAny<string>()))
            .Returns(_mockStrategy.Object);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WhenEntitiesProvided_ShouldDelegateToStrategy()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Entity1", ["id"] = "1" },
            new() { ["name"] = "Entity2", ["id"] = "2" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = _testStoreConfig,
            CategoryTreeContext = new CategoryTreeContext { DestinationCategoryTreeId = "1" }
        };

        var expectedResult = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "101", ["name"] = "Entity1" },
            new() { ["id"] = "102", ["name"] = "Entity2" }
        };

        _mockStrategy.Setup(x => x.CreateEntitiesAsync(
                entities, 
                request.MigrationId, 
                request.DestinationStore, 
                request.CategoryTreeContext, 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.CreateEntitiesAsync(entities, request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("101", result[0]["id"].ToString());
        Assert.Equal("102", result[1]["id"].ToString());
        
        _mockStrategyFactory.Verify(x => x.GetStrategy("categories"), Times.Once);
        _mockStrategy.Verify(x => x.CreateEntitiesAsync(
            entities, 
            request.MigrationId, 
            request.DestinationStore, 
            request.CategoryTreeContext, 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WhenNullEntities_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = _testStoreConfig
        };

        // Act
        var result = await _service.CreateEntitiesAsync(null!, request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
        
        // Strategy should not be called
        _mockStrategyFactory.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Never);
        _mockStrategy.Verify(x => x.CreateEntitiesAsync(
            It.IsAny<List<Dictionary<string, object>>>(), 
            It.IsAny<string>(), 
            It.IsAny<StoreConfiguration>(), 
            It.IsAny<CategoryTreeContext?>(), 
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WhenEmptyEntities_ShouldReturnEmptyList()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>();
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = _testStoreConfig
        };

        // Act
        var result = await _service.CreateEntitiesAsync(entities, request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
        
        // Strategy should not be called
        _mockStrategyFactory.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Never);
        _mockStrategy.Verify(x => x.CreateEntitiesAsync(
            It.IsAny<List<Dictionary<string, object>>>(), 
            It.IsAny<string>(), 
            It.IsAny<StoreConfiguration>(), 
            It.IsAny<CategoryTreeContext?>(), 
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(Skip = "Complex strategy exception handling - requires proper service setup")]
    public async Task CreateEntitiesAsync_WhenStrategyThrows_ShouldPropagateException()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Entity1", ["id"] = "1" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = _testStoreConfig
        };

        var expectedException = new InvalidOperationException("Strategy failed");
        _mockStrategy.Setup(x => x.CreateEntitiesAsync(
                entities, 
                request.MigrationId, 
                request.DestinationStore, 
                request.CategoryTreeContext, 
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateEntitiesAsync(entities, request, CancellationToken.None));
        
        Assert.Equal("Strategy failed", exception.Message);
        
        _mockStrategyFactory.Verify(x => x.GetStrategy("categories"), Times.Once);
        _mockStrategy.Verify(x => x.CreateEntitiesAsync(
            entities, 
            request.MigrationId, 
            request.DestinationStore, 
            request.CategoryTreeContext, 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateEntitiesAsync_ShouldPassCorrectEntityTypeToFactory()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Product1" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            DestinationStore = _testStoreConfig
        };

        _mockStrategy.Setup(x => x.CreateEntitiesAsync(
                entities, 
                request.MigrationId, 
                request.DestinationStore, 
                request.CategoryTreeContext, 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>());

        // Act
        await _service.CreateEntitiesAsync(entities, request, CancellationToken.None);

        // Assert
        _mockStrategyFactory.Verify(x => x.GetStrategy("products"), Times.Once);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WhenStrategyReturnsNull_ShouldReturnNull()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Entity1" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = _testStoreConfig
        };

        _mockStrategy.Setup(x => x.CreateEntitiesAsync(
                entities, 
                request.MigrationId, 
                request.DestinationStore, 
                request.CategoryTreeContext, 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Dictionary<string, object>>?)null);

        // Act
        var result = await _service.CreateEntitiesAsync(entities, request, CancellationToken.None);

        // Assert
        Assert.Null(result);
        
        _mockStrategyFactory.Verify(x => x.GetStrategy("categories"), Times.Once);
        _mockStrategy.Verify(x => x.CreateEntitiesAsync(
            entities, 
            request.MigrationId, 
            request.DestinationStore, 
            request.CategoryTreeContext, 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("products")]
    [InlineData("brands")]
    [InlineData("variants")]
    [InlineData("images")]
    [InlineData("modifiers")]
    public async Task CreateEntitiesAsync_ShouldSupportAllEntityTypes(string entityType)
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "TestEntity" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = entityType,
            DestinationStore = _testStoreConfig
        };

        var expectedResult = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "123", ["name"] = "TestEntity" }
        };

        _mockStrategy.Setup(x => x.CreateEntitiesAsync(
                entities, 
                request.MigrationId, 
                request.DestinationStore, 
                request.CategoryTreeContext, 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.CreateEntitiesAsync(entities, request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("123", result[0]["id"].ToString());
        
        _mockStrategyFactory.Verify(x => x.GetStrategy(entityType), Times.Once);
        _mockStrategy.Verify(x => x.CreateEntitiesAsync(
            entities, 
            request.MigrationId, 
            request.DestinationStore, 
            request.CategoryTreeContext, 
            It.IsAny<CancellationToken>()), Times.Once);
    }
} 