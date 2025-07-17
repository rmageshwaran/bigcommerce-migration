using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// TDD Tests for ProductApiService
/// Following Single Responsibility Principle - only product operations
/// </summary>
public class ProductApiServiceTests
{
    private readonly Mock<ILogger<ProductApiService>> _mockLogger;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly ProductApiService _productApiService;
    private readonly StoreConfiguration _testStoreConfig;

    public ProductApiServiceTests()
    {
        _mockLogger = new Mock<ILogger<ProductApiService>>();
        _mockHttpClient = new Mock<HttpClient>();
        _productApiService = new ProductApiService(_mockLogger.Object, _mockHttpClient.Object);
        
        _testStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store",
            AccessToken = "test-token",
            BaseUrl = "https://api.bigcommerce.com",
            ChannelId = "1"
        };
    }

    [Fact]
    public void ProductApiService_Should_Implement_IProductApiClient()
    {
        // Arrange & Act
        var service = _productApiService;

        // Assert
        service.Should().BeAssignableTo<IProductApiClient>("ProductApiService should implement IProductApiClient");
    }

    [Fact]
    public void ProductApiService_Should_Have_Single_Responsibility()
    {
        // Arrange
        var serviceType = typeof(ProductApiService);

        // Act & Assert - Check that service implements only the product interface
        serviceType.GetInterfaces().Should().Contain(typeof(IProductApiClient), 
            "ProductApiService should implement IProductApiClient interface");
            
        // Verify it has the required product methods
        serviceType.GetMethod("GetProductsAsync").Should().NotBeNull("Should have GetProductsAsync method");
        serviceType.GetMethod("CreateProductsAsync").Should().NotBeNull("Should have CreateProductsAsync method");
        serviceType.GetMethod("GetProductVariantsAsync").Should().NotBeNull("Should have GetProductVariantsAsync method");
        serviceType.GetMethod("GetProductImagesAsync").Should().NotBeNull("Should have GetProductImagesAsync method");
        serviceType.GetMethod("GetProductModifiersAsync").Should().NotBeNull("Should have GetProductModifiersAsync method");
    }

    [Fact]
    public async Task GetProductsAsync_Should_Handle_Valid_Store_Configuration()
    {
        // Arrange
        var page = 1;
        var limit = 50;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _productApiService.GetProductsAsync(_testStoreConfig, page, limit, cancellationToken);
        
        // Should not throw exception with valid configuration
        await act.Should().NotThrowAsync("Valid store configuration should not cause exceptions");
    }

    [Fact]
    public async Task CreateProductsAsync_Should_Handle_Valid_Products()
    {
        // Arrange
        var products = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "name", "Test Product" }, { "price", 10.99 } }
        };
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _productApiService.CreateProductsAsync(_testStoreConfig, products, cancellationToken);
        
        // Should not throw exception with valid products
        await act.Should().NotThrowAsync("Valid products should not cause exceptions");
    }

    [Fact]
    public async Task GetProductVariantsAsync_Should_Handle_Valid_ProductId()
    {
        // Arrange
        var productId = 123;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _productApiService.GetProductVariantsAsync(_testStoreConfig, productId, cancellationToken);
        
        // Should not throw exception with valid product ID
        await act.Should().NotThrowAsync("Valid product ID should not cause exceptions");
    }

    [Fact]
    public async Task GetProductImagesAsync_Should_Handle_Valid_ProductId()
    {
        // Arrange
        var productId = 123;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _productApiService.GetProductImagesAsync(_testStoreConfig, productId, cancellationToken);
        
        // Should not throw exception with valid product ID
        await act.Should().NotThrowAsync("Valid product ID should not cause exceptions");
    }

    [Fact]
    public async Task GetProductModifiersAsync_Should_Handle_Valid_ProductId()
    {
        // Arrange
        var productId = 123;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _productApiService.GetProductModifiersAsync(_testStoreConfig, productId, cancellationToken);
        
        // Should not throw exception with valid product ID
        await act.Should().NotThrowAsync("Valid product ID should not cause exceptions");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetProductsAsync_Should_Validate_Page_Number(int invalidPage)
    {
        // Arrange
        var limit = 50;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _productApiService.GetProductsAsync(_testStoreConfig, invalidPage, limit, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentException>("Invalid page number should throw ArgumentException");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(251)] // BigCommerce API limit is 250
    public async Task GetProductsAsync_Should_Validate_Limit(int invalidLimit)
    {
        // Arrange
        var page = 1;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _productApiService.GetProductsAsync(_testStoreConfig, page, invalidLimit, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentException>("Invalid limit should throw ArgumentException");
    }

    [Fact]
    public async Task CreateProductsAsync_Should_Validate_Products_Not_Null()
    {
        // Arrange
        List<Dictionary<string, object>>? nullProducts = null;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _productApiService.CreateProductsAsync(_testStoreConfig, nullProducts!, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentNullException>("Null products should throw ArgumentNullException");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetProductVariantsAsync_Should_Validate_ProductId(int invalidProductId)
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _productApiService.GetProductVariantsAsync(_testStoreConfig, invalidProductId, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentException>("Invalid product ID should throw ArgumentException");
    }

    [Fact]
    public async Task ProductApiService_Should_Respect_Cancellation_Token()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        var act = async () => await _productApiService.GetProductsAsync(_testStoreConfig, 1, 50, cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>("Cancelled token should throw OperationCanceledException");
    }
} 