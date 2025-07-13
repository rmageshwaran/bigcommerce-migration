using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.UnitTests.TestHelpers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for BigCommerceApiClient service implementation
/// Tests HTTP client interactions and API request handling with new request-based architecture
/// </summary>
public class BigCommerceApiClientTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly Mock<ILogger<BigCommerceApiClient>> _loggerMock;
    private readonly Mock<IOpenSearchService> _openSearchServiceMock;
    private readonly HttpClient _httpClient;
    private readonly BigCommerceConfiguration _globalConfig;
    private readonly BigCommerceApiClient _apiClient;
    private readonly StoreConfiguration _testStoreConfig;

    public BigCommerceApiClientTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _loggerMock = new Mock<ILogger<BigCommerceApiClient>>();
        _openSearchServiceMock = new Mock<IOpenSearchService>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _globalConfig = TestDataFactory.CreateBigCommerceConfiguration();
        _apiClient = new BigCommerceApiClient(_globalConfig, _httpClient, _openSearchServiceMock.Object, _loggerMock.Object);
        _testStoreConfig = TestDataFactory.CreateSourceStoreConfiguration();
    }

    [Fact]
    public async Task GetCategoryTreesAsync_WithValidStoreConfig_ShouldReturnCategoryTrees()
    {
        // Arrange
        var expectedTrees = TestDataFactory.CreateMockCategoryTrees("1", "tree-123");
        var responseJson = JsonSerializer.Serialize(new { data = expectedTrees });
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        // Act
        var result = await _apiClient.GetCategoryTreesAsync(_testStoreConfig);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        
        // Verify HTTP request was made with correct parameters
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Get &&
                req.RequestUri!.ToString().Contains($"stores/{_testStoreConfig.StoreId}/v3/catalog/trees")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetCategoriesAsync_WithValidParameters_ShouldReturnCategories()
    {
        // Arrange
        var categoryTreeId = "tree-123";
        var expectedCategories = TestDataFactory.CreateMockCategories(2);
        var responseJson = JsonSerializer.Serialize(new { data = expectedCategories });
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        // Act
        var result = await _apiClient.GetCategoriesAsync(_testStoreConfig, categoryTreeId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        
        // Verify HTTP request was made with correct parameters
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Get &&
                req.RequestUri!.ToString().Contains($"stores/{_testStoreConfig.StoreId}/v3/catalog/trees/{categoryTreeId}/categories")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetProductsAsync_WithPagination_ShouldReturnProducts()
    {
        // Arrange
        var page = 1;
        var limit = 50;
        var expectedProducts = TestDataFactory.CreateMockProducts(3);
        var responseJson = JsonSerializer.Serialize(new { data = expectedProducts });
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        // Act
        var result = await _apiClient.GetProductsAsync(_testStoreConfig, page, limit);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        
        // Verify HTTP request was made with correct parameters
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Get &&
                req.RequestUri!.ToString().Contains($"stores/{_testStoreConfig.StoreId}/v3/catalog/products") &&
                req.RequestUri.ToString().Contains($"page={page}") &&
                req.RequestUri.ToString().Contains($"limit={limit}")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task CreateProductsAsync_WithValidProducts_ShouldReturnCreatedProducts()
    {
        // Arrange
        var productsToCreate = TestDataFactory.CreateMockProducts(2);
        var expectedResponse = TestDataFactory.CreateMockProducts(2);
        var responseJson = JsonSerializer.Serialize(new { data = expectedResponse });
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        // Act
        var result = await _apiClient.CreateProductsAsync(_testStoreConfig, productsToCreate);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        
        // Verify HTTP request was made with correct parameters
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains($"stores/{_testStoreConfig.StoreId}/v3/catalog/products")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task CreateCategoriesAsync_WithValidCategories_ShouldReturnCreatedCategories()
    {
        // Arrange
        var categoryTreeId = "tree-123";
        var categoriesToCreate = TestDataFactory.CreateMockCategories(2);
        var expectedResponse = TestDataFactory.CreateMockCategories(2);
        var responseJson = JsonSerializer.Serialize(new { data = expectedResponse });
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        // Act
        var result = await _apiClient.CreateCategoriesAsync(_testStoreConfig, categoryTreeId, categoriesToCreate);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        
        // Verify HTTP request was made with correct parameters
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains($"stores/{_testStoreConfig.StoreId}/v3/catalog/trees/categories")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task IsHealthyAsync_WithValidStoreConfig_ShouldReturnTrue()
    {
        // Arrange
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\": {\"status\": \"healthy\"}}", System.Text.Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        // Act
        var result = await _apiClient.IsHealthyAsync(_testStoreConfig);

        // Assert
        result.Should().BeTrue();
        
        // Verify HTTP request was made with correct parameters
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Get &&
                req.RequestUri!.ToString().Contains($"stores/{_testStoreConfig.StoreId}/v2/store")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task IsHealthyAsync_WithUnhealthyResponse_ShouldReturnFalse()
    {
        // Arrange
        var responseMessage = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        // Act
        var result = await _apiClient.IsHealthyAsync(_testStoreConfig);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetCategoryTreesAsync_WithHttpException_ShouldThrowException()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => 
            _apiClient.GetCategoryTreesAsync(_testStoreConfig));
    }

    [Fact]
    public async Task GetCategoriesAsync_WithCancellation_ShouldCancelRequest()
    {
        // Arrange
        var categoryTreeId = "tree-123";
        var cancellationToken = new CancellationToken(true);

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => 
            _apiClient.GetCategoriesAsync(_testStoreConfig, categoryTreeId, cancellationToken));
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        var client = new BigCommerceApiClient(_globalConfig, _httpClient, _openSearchServiceMock.Object, _loggerMock.Object);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new BigCommerceApiClient(_globalConfig, null!, _openSearchServiceMock.Object, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new BigCommerceApiClient(null!, _httpClient, _openSearchServiceMock.Object, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new BigCommerceApiClient(_globalConfig, _httpClient, _openSearchServiceMock.Object, null!));
    }

    [Fact]
    public async Task GetProductsAsync_WithInvalidStoreConfig_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidStoreConfig = new StoreConfiguration
        {
            StoreId = "", // Invalid
            AccessToken = "token",
            ChannelId = "1"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _apiClient.GetProductsAsync(invalidStoreConfig, 1, 50));
    }

    [Fact]
    public async Task CreateProductsAsync_WithEmptyProductsList_ShouldReturnEmptyList()
    {
        // Arrange
        var emptyProducts = new List<Dictionary<string, object>>();
        var responseJson = JsonSerializer.Serialize(new { data = emptyProducts });
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        // Act
        var result = await _apiClient.CreateProductsAsync(_testStoreConfig, emptyProducts);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
} 