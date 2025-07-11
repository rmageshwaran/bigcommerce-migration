using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.UnitTests.TestHelpers;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Interfaces;

/// <summary>
/// Unit tests for IBigCommerceApiClient interface contracts
/// Tests method signatures and basic mock behavior with request-based architecture
/// </summary>
public class IBigCommerceApiClientTests
{
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly StoreConfiguration _testStoreConfig;

    public IBigCommerceApiClientTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _testStoreConfig = TestDataFactory.CreateSourceStoreConfiguration();
    }

    [Fact]
    public async Task GetCategoryTreesAsync_WithValidStoreConfig_ShouldReturnTrees()
    {
        // Arrange
        var expectedTrees = TestDataFactory.CreateMockCategoryTrees("1", "tree-123");
        _mockApiClient
            .Setup(x => x.GetCategoryTreesAsync(_testStoreConfig, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedTrees);

        // Act
        var result = await _mockApiClient.Object.GetCategoryTreesAsync(_testStoreConfig);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedTrees);
        _mockApiClient.Verify(x => x.GetCategoryTreesAsync(_testStoreConfig, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCategoriesAsync_WithValidParameters_ShouldReturnCategories()
    {
        // Arrange
        var categoryTreeId = "tree-123";
        var expectedCategories = TestDataFactory.CreateMockCategories(3);
        _mockApiClient
            .Setup(x => x.GetCategoriesAsync(_testStoreConfig, categoryTreeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCategories);

        // Act
        var result = await _mockApiClient.Object.GetCategoriesAsync(_testStoreConfig, categoryTreeId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedCategories);
        _mockApiClient.Verify(x => x.GetCategoriesAsync(_testStoreConfig, categoryTreeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetProductsAsync_WithPagination_ShouldReturnProducts()
    {
        // Arrange
        var page = 1;
        var limit = 50;
        var expectedProducts = TestDataFactory.CreateMockProducts(5);
        _mockApiClient
            .Setup(x => x.GetProductsAsync(_testStoreConfig, page, limit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedProducts);

        // Act
        var result = await _mockApiClient.Object.GetProductsAsync(_testStoreConfig, page, limit);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedProducts);
        _mockApiClient.Verify(x => x.GetProductsAsync(_testStoreConfig, page, limit, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateProductsAsync_WithValidProducts_ShouldReturnCreatedProducts()
    {
        // Arrange
        var productsToCreate = TestDataFactory.CreateMockProducts(2);
        var expectedCreatedProducts = TestDataFactory.CreateMockProducts(2);
        _mockApiClient
            .Setup(x => x.CreateProductsAsync(_testStoreConfig, productsToCreate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCreatedProducts);

        // Act
        var result = await _mockApiClient.Object.CreateProductsAsync(_testStoreConfig, productsToCreate);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedCreatedProducts);
        _mockApiClient.Verify(x => x.CreateProductsAsync(_testStoreConfig, productsToCreate, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsHealthyAsync_WithValidStoreConfig_ShouldReturnHealthStatus()
    {
        // Arrange
        var expectedHealth = true;
        _mockApiClient
            .Setup(x => x.IsHealthyAsync(_testStoreConfig, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedHealth);

        // Act
        var result = await _mockApiClient.Object.IsHealthyAsync(_testStoreConfig);

        // Assert
        result.Should().Be(expectedHealth);
        _mockApiClient.Verify(x => x.IsHealthyAsync(_testStoreConfig, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateCategoriesAsync_WithValidCategories_ShouldReturnCreatedCategories()
    {
        // Arrange
        var categoryTreeId = "tree-123";
        var categoriesToCreate = TestDataFactory.CreateMockCategories(2);
        var expectedCreatedCategories = TestDataFactory.CreateMockCategories(2);
        _mockApiClient
            .Setup(x => x.CreateCategoriesAsync(_testStoreConfig, categoryTreeId, categoriesToCreate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCreatedCategories);

        // Act
        var result = await _mockApiClient.Object.CreateCategoriesAsync(_testStoreConfig, categoryTreeId, categoriesToCreate);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedCreatedCategories);
        _mockApiClient.Verify(x => x.CreateCategoriesAsync(_testStoreConfig, categoryTreeId, categoriesToCreate, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCategoryTreesAsync_WithCancellation_ShouldPassCancellationToken()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);
        _mockApiClient
            .Setup(x => x.GetCategoryTreesAsync(_testStoreConfig, cancellationToken))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _mockApiClient.Object.GetCategoryTreesAsync(_testStoreConfig, cancellationToken));
    }
} 