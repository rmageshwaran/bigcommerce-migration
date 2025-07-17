using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.UnitTests.TestHelpers;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for BigCommerceApiClient delegation pattern refactoring
/// Tests that BigCommerceApiClient properly delegates HTTP concerns to IApiRequestHandler
/// Validates Single Responsibility Principle - business logic separation from HTTP handling
/// </summary>
public class BigCommerceApiClientDelegationTests : IDisposable
{
    private readonly Mock<IApiRequestHandler> _apiRequestHandlerMock;
    private readonly Mock<ILogger<BigCommerceApiClient>> _loggerMock;
    private readonly BigCommerceApiClient _apiClient;
    private readonly StoreConfiguration _testStoreConfig;

    public BigCommerceApiClientDelegationTests()
    {
        _apiRequestHandlerMock = new Mock<IApiRequestHandler>();
        _loggerMock = new Mock<ILogger<BigCommerceApiClient>>();
        _apiClient = new BigCommerceApiClient(_apiRequestHandlerMock.Object, _loggerMock.Object);
        _testStoreConfig = TestDataFactory.CreateSourceStoreConfiguration();
    }

    [Fact]
    public async Task GetCategoryTreesAsync_ShouldDelegateToApiRequestHandler_WithCorrectApiRequest()
    {
        // Arrange
        var expectedTrees = TestDataFactory.CreateMockCategoryTrees("1", "tree-123");
        var responseData = new { data = expectedTrees };
        var expectedUrl = $"{_testStoreConfig.GetApiBaseUrl()}/catalog/trees?channel_id:in={_testStoreConfig.ChannelId}";

        _apiRequestHandlerMock
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["data"] = JsonSerializer.SerializeToElement(expectedTrees) });

        // Act
        var result = await _apiClient.GetCategoryTreesAsync(_testStoreConfig);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);

        // Verify delegation to ApiRequestHandler
        _apiRequestHandlerMock.Verify(
            x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCategoriesAsync_ShouldDelegateToApiRequestHandler_WithCorrectParameters()
    {
        // Arrange
        var categoryTreeId = "tree-123";
        var expectedCategories = TestDataFactory.CreateMockCategories(2);
        var responseData = new { data = expectedCategories };
        var expectedUrl = $"{_testStoreConfig.GetApiBaseUrl()}/catalog/trees/{categoryTreeId}/categories";

        _apiRequestHandlerMock
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["data"] = JsonSerializer.SerializeToElement(expectedCategories) });

        // Act
        var result = await _apiClient.GetCategoriesAsync(_testStoreConfig, categoryTreeId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        // Verify delegation to ApiRequestHandler
        _apiRequestHandlerMock.Verify(
            x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateCategoriesAsync_ShouldDelegateToApiRequestHandler_WithPostRequest()
    {
        // Arrange
        var categoryTreeId = "tree-123";
        var categoriesToCreate = TestDataFactory.CreateMockCategories(2);
        var expectedResponse = TestDataFactory.CreateMockCategories(2);
        var expectedUrl = $"{_testStoreConfig.GetApiBaseUrl()}/catalog/trees/categories";
        var expectedContent = JsonSerializer.Serialize(categoriesToCreate);

        _apiRequestHandlerMock
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Post &&
                    req.StoreConfiguration == _testStoreConfig &&
                    req.Content == expectedContent
                ), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["data"] = JsonSerializer.SerializeToElement(expectedResponse) });

        // Act
        var result = await _apiClient.CreateCategoriesAsync(_testStoreConfig, categoryTreeId, categoriesToCreate);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        // Verify delegation to ApiRequestHandler
        _apiRequestHandlerMock.Verify(
            x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Post &&
                    req.StoreConfiguration == _testStoreConfig &&
                    req.Content == expectedContent
                ), 
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductsAsync_ShouldDelegateToApiRequestHandler_WithPaginationParameters()
    {
        // Arrange
        var page = 1;
        var limit = 50;
        var expectedProducts = TestDataFactory.CreateMockProducts(3);
        var expectedUrl = $"{_testStoreConfig.GetApiBaseUrl()}/catalog/products?page={page}&limit={limit}&channel_id={_testStoreConfig.ChannelId}";

        _apiRequestHandlerMock
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["data"] = JsonSerializer.SerializeToElement(expectedProducts) });

        // Act
        var result = await _apiClient.GetProductsAsync(_testStoreConfig, page, limit);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);

        // Verify delegation to ApiRequestHandler
        _apiRequestHandlerMock.Verify(
            x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateProductsAsync_ShouldDelegateToApiRequestHandler_WithProductData()
    {
        // Arrange
        var productsToCreate = TestDataFactory.CreateMockProducts(2);
        var expectedResponse = TestDataFactory.CreateMockProducts(2);
        var expectedUrl = $"{_testStoreConfig.GetApiBaseUrl()}/catalog/products?channel_id={_testStoreConfig.ChannelId}";
        var expectedContent = JsonSerializer.Serialize(productsToCreate);

        _apiRequestHandlerMock
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Post &&
                    req.StoreConfiguration == _testStoreConfig &&
                    req.Content == expectedContent
                ), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["data"] = JsonSerializer.SerializeToElement(expectedResponse) });

        // Act
        var result = await _apiClient.CreateProductsAsync(_testStoreConfig, productsToCreate);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        // Verify delegation to ApiRequestHandler
        _apiRequestHandlerMock.Verify(
            x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Post &&
                    req.StoreConfiguration == _testStoreConfig &&
                    req.Content == expectedContent
                ), 
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IsHealthyAsync_ShouldDelegateToApiRequestHandler_WithV2ApiEndpoint()
    {
        // Arrange
        var expectedUrl = $"{_testStoreConfig.GetApiBaseUrl("v2")}/store";

        _apiRequestHandlerMock
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["status"] = "healthy" });

        // Act
        var result = await _apiClient.IsHealthyAsync(_testStoreConfig);

        // Assert
        result.Should().BeTrue();

        // Verify delegation to ApiRequestHandler
        _apiRequestHandlerMock.Verify(
            x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IsHealthyAsync_ShouldReturnFalse_WhenApiRequestHandlerThrowsException()
    {
        // Arrange
        var expectedUrl = $"{_testStoreConfig.GetApiBaseUrl("v2")}/store";

        _apiRequestHandlerMock
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(), 
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API unavailable"));

        // Act
        var result = await _apiClient.IsHealthyAsync(_testStoreConfig);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldOnlyRequire_ApiRequestHandlerAndLogger()
    {
        // Arrange & Act
        var client = new BigCommerceApiClient(_apiRequestHandlerMock.Object, _loggerMock.Object);

        // Assert
        client.Should().NotBeNull();
        // Verify that BigCommerceApiClient no longer depends on HttpClient, BigCommerceConfiguration, or IOpenSearchService
        // This validates SRP - HTTP concerns are delegated to IApiRequestHandler
    }

    [Fact]
    public void Constructor_WithNullApiRequestHandler_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new BigCommerceApiClient(null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new BigCommerceApiClient(_apiRequestHandlerMock.Object, null!));
    }

    [Fact]
    public async Task GetPaginatedEntitiesAsync_ShouldDelegateToApiRequestHandler_WithCorrectEntityUrl()
    {
        // Arrange
        var entityType = "products";
        var paginationRequest = new BigCommercePaginationRequest { Page = 1, Limit = 50 };
        var expectedUrl = $"{_testStoreConfig.GetApiBaseUrl()}/catalog/{entityType}?page=1&limit=50&channel_id={_testStoreConfig.ChannelId}";
        var mockData = TestDataFactory.CreateMockProducts(2);

        var mockResponse = new Dictionary<string, object>
        {
            ["data"] = JsonSerializer.SerializeToElement(mockData),
            ["meta"] = JsonSerializer.SerializeToElement(new 
            { 
                pagination = new 
                { 
                    total = 100, 
                    count = 2, 
                    per_page = 50, 
                    current_page = 1, 
                    total_pages = 2 
                } 
            })
        };

        _apiRequestHandlerMock
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _apiClient.GetPaginatedEntitiesAsync(_testStoreConfig, entityType, paginationRequest, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().HaveCount(2);
        result.CurrentPage.Should().Be(1);
        result.TotalItems.Should().Be(100);

        // Verify delegation to ApiRequestHandler
        _apiRequestHandlerMock.Verify(
            x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(req => 
                    req.Url == expectedUrl &&
                    req.Method == HttpMethod.Get &&
                    req.StoreConfiguration == _testStoreConfig
                ), 
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    protected virtual void Dispose(bool disposing)
    {
        // No resources to dispose in this test class
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
} 