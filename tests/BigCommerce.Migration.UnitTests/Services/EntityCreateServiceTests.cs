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
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly Mock<ILogger<EntityCreateService>> _mockLogger;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly EntityCreateService _service;
    private readonly StoreConfiguration _testStoreConfig;

    public EntityCreateServiceTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _mockLogger = new Mock<ILogger<EntityCreateService>>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _service = new EntityCreateService(_mockApiClient.Object, _mockLogger.Object, _mockErrorHandlingService.Object);
        
        _testStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store",
            AccessToken = "test-token",
            ChannelId = "1"
        };
    }

    [Fact]
    public async Task CreateCategoriesAsync_WhenBatchSucceeds_ShouldReturnCreatedCategories()
    {
        // Arrange
        var categories = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Category1", ["id"] = "1" },
            new() { ["name"] = "Category2", ["id"] = "2" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = _testStoreConfig,
            CategoryTreeContext = new CategoryTreeContext { DestinationCategoryTreeId = "1" }
        };

        var createdCategories = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "101", ["name"] = "Category1" },
            new() { ["id"] = "102", ["name"] = "Category2" }
        };

        _mockApiClient.Setup(x => x.CreateCategoriesAsync(_testStoreConfig, "1", categories, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdCategories);

        // Act
        var result = await _service.CreateCategoriesAsync(categories, request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        _mockApiClient.Verify(x => x.CreateCategoriesAsync(_testStoreConfig, "1", categories, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateCategoriesAsync_WhenBatchFails_ShouldFallbackToIndividualCreation()
    {
        // Arrange
        var categories = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Category1", ["id"] = "1" },
            new() { ["name"] = "Category2", ["id"] = "2" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = _testStoreConfig,
            CategoryTreeContext = new CategoryTreeContext { DestinationCategoryTreeId = "1" }
        };

        var createdCategory1 = new List<Dictionary<string, object>> { new() { ["id"] = "101", ["name"] = "Category1" } };
        var createdCategory2 = new List<Dictionary<string, object>> { new() { ["id"] = "102", ["name"] = "Category2" } };

        // Batch creation fails
        _mockApiClient.Setup(x => x.CreateCategoriesAsync(_testStoreConfig, "1", categories, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Batch creation failed"));

        // Individual creation succeeds for both
        _mockApiClient.Setup(x => x.CreateCategoriesAsync(_testStoreConfig, "1", It.Is<List<Dictionary<string, object>>>(l => l.Count == 1 && l[0]["name"].ToString() == "Category1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdCategory1);
        _mockApiClient.Setup(x => x.CreateCategoriesAsync(_testStoreConfig, "1", It.Is<List<Dictionary<string, object>>>(l => l.Count == 1 && l[0]["name"].ToString() == "Category2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdCategory2);

        // Act
        var result = await _service.CreateCategoriesAsync(categories, request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        _mockApiClient.Verify(x => x.CreateCategoriesAsync(_testStoreConfig, "1", categories, It.IsAny<CancellationToken>()), Times.Once);
        _mockApiClient.Verify(x => x.CreateCategoriesAsync(_testStoreConfig, "1", It.Is<List<Dictionary<string, object>>>(l => l.Count == 1), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateCategoriesAsync_WhenBatchFailsAndSomeIndividualFail_ShouldReturnSuccessfulOnes()
    {
        // Arrange
        var categories = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Category1", ["id"] = "1" },
            new() { ["name"] = "Category2", ["id"] = "2" },
            new() { ["name"] = "Category3", ["id"] = "3" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = _testStoreConfig,
            CategoryTreeContext = new CategoryTreeContext { DestinationCategoryTreeId = "1" }
        };

        var createdCategory1 = new List<Dictionary<string, object>> { new() { ["id"] = "101", ["name"] = "Category1" } };
        var createdCategory3 = new List<Dictionary<string, object>> { new() { ["id"] = "103", ["name"] = "Category3" } };

        // Batch creation fails
        _mockApiClient.Setup(x => x.CreateCategoriesAsync(_testStoreConfig, "1", categories, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Batch creation failed"));

        // Individual creation: Category1 succeeds, Category2 fails, Category3 succeeds
        _mockApiClient.Setup(x => x.CreateCategoriesAsync(_testStoreConfig, "1", It.Is<List<Dictionary<string, object>>>(l => l.Count == 1 && l[0]["name"].ToString() == "Category1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdCategory1);
        _mockApiClient.Setup(x => x.CreateCategoriesAsync(_testStoreConfig, "1", It.Is<List<Dictionary<string, object>>>(l => l.Count == 1 && l[0]["name"].ToString() == "Category2"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Category2 creation failed"));
        _mockApiClient.Setup(x => x.CreateCategoriesAsync(_testStoreConfig, "1", It.Is<List<Dictionary<string, object>>>(l => l.Count == 1 && l[0]["name"].ToString() == "Category3"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdCategory3);

        // Act
        var result = await _service.CreateCategoriesAsync(categories, request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // Only Category1 and Category3 should be created
        _mockApiClient.Verify(x => x.CreateCategoriesAsync(_testStoreConfig, "1", categories, It.IsAny<CancellationToken>()), Times.Once);
        _mockApiClient.Verify(x => x.CreateCategoriesAsync(_testStoreConfig, "1", It.Is<List<Dictionary<string, object>>>(l => l.Count == 1), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task CreateProductsAsync_WhenBatchSucceeds_ShouldReturnCreatedProducts()
    {
        // Arrange
        var products = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Product1", ["id"] = "1" },
            new() { ["name"] = "Product2", ["id"] = "2" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            DestinationStore = _testStoreConfig
        };

        var createdProducts = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "101", ["name"] = "Product1" },
            new() { ["id"] = "102", ["name"] = "Product2" }
        };

        _mockApiClient.Setup(x => x.CreateProductsAsync(_testStoreConfig, products, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdProducts);

        // Act
        var result = await _service.CreateProductsAsync(products, request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        _mockApiClient.Verify(x => x.CreateProductsAsync(_testStoreConfig, products, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateProductsAsync_WhenBatchFails_ShouldFallbackToIndividualCreation()
    {
        // Arrange
        var products = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Product1", ["id"] = "1" },
            new() { ["name"] = "Product2", ["id"] = "2" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            DestinationStore = _testStoreConfig
        };

        var createdProduct1 = new List<Dictionary<string, object>> { new() { ["id"] = "101", ["name"] = "Product1" } };
        var createdProduct2 = new List<Dictionary<string, object>> { new() { ["id"] = "102", ["name"] = "Product2" } };

        // Batch creation fails
        _mockApiClient.Setup(x => x.CreateProductsAsync(_testStoreConfig, products, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Batch creation failed"));

        // Individual creation succeeds for both
        _mockApiClient.Setup(x => x.CreateProductsAsync(_testStoreConfig, It.Is<List<Dictionary<string, object>>>(l => l.Count == 1 && l[0]["name"].ToString() == "Product1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdProduct1);
        _mockApiClient.Setup(x => x.CreateProductsAsync(_testStoreConfig, It.Is<List<Dictionary<string, object>>>(l => l.Count == 1 && l[0]["name"].ToString() == "Product2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdProduct2);

        // Act
        var result = await _service.CreateProductsAsync(products, request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        _mockApiClient.Verify(x => x.CreateProductsAsync(_testStoreConfig, products, It.IsAny<CancellationToken>()), Times.Once);
        _mockApiClient.Verify(x => x.CreateProductsAsync(_testStoreConfig, It.Is<List<Dictionary<string, object>>>(l => l.Count == 1), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateCategoriesAsync_WhenMissingCategoryTreeId_ShouldThrowException()
    {
        // Arrange
        var categories = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Category1", ["id"] = "1" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = _testStoreConfig,
            CategoryTreeContext = new CategoryTreeContext { DestinationCategoryTreeId = null }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateCategoriesAsync(categories, request, CancellationToken.None));

        Assert.Contains("Destination category tree ID is required", exception.Message);
    }

    [Fact]
    public async Task CreateCategoriesAsync_WhenInvalidStoreConfig_ShouldThrowException()
    {
        // Arrange
        var categories = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Category1", ["id"] = "1" }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            DestinationStore = null!,
            CategoryTreeContext = new CategoryTreeContext { DestinationCategoryTreeId = "1" }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateCategoriesAsync(categories, request, CancellationToken.None));

        Assert.Contains("Invalid destination store configuration", exception.Message);
    }
} 