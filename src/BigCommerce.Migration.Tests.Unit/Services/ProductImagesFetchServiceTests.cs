using System.Text.Json;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Services;

/// <summary>
/// Unit tests for ProductImagesFetchService.
/// Tests the streaming image fetching functionality, pagination, error handling, and cancellation.
/// </summary>
public class ProductImagesFetchServiceTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<ICancellationStore> _mockCancellationStore;
    private readonly Mock<ILogger<ProductImagesFetchService>> _mockLogger;
    private readonly ProductImagesFetchService _service;
    private readonly StoreConfiguration _validStore;
    private const string ValidProductId = "123";
    private const string ValidMigrationId = "migration-456";

    public ProductImagesFetchServiceTests()
    {
        _mockApiRequestHandler = new Mock<IApiRequestHandler>();
        _mockCancellationStore = new Mock<ICancellationStore>();
        _mockLogger = new Mock<ILogger<ProductImagesFetchService>>();

        _service = new ProductImagesFetchService(
            _mockApiRequestHandler.Object,
            _mockCancellationStore.Object,
            _mockLogger.Object);

        _validStore = new StoreConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com",
            AccessToken = "test-token",
            StoreId = "test-store-id",
            ChannelId = "1"
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullApiRequestHandler_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ProductImagesFetchService(null!, _mockCancellationStore.Object, _mockLogger.Object));
        
        Assert.Equal("apiRequestHandler", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullCancellationStore_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ProductImagesFetchService(_mockApiRequestHandler.Object, null!, _mockLogger.Object));
        
        Assert.Equal("cancellationStore", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ProductImagesFetchService(_mockApiRequestHandler.Object, _mockCancellationStore.Object, null!));
        
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region FetchProductImagesStreamingAsync Tests

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WithValidInputs_ShouldReturnImageCount()
    {
        // Arrange
        var callbackInvoked = false;
        var capturedImages = new List<Dictionary<string, object>>();
        
        Func<List<Dictionary<string, object>>, Task> callback = async images =>
        {
            callbackInvoked = true;
            capturedImages.AddRange(images);
            await Task.CompletedTask;
        };

        var mockResponse = CreateMockImageResponse(5); // 5 images in response
        
        _mockCancellationStore.Setup(x => x.CheckCancellationFlagAsync(ValidMigrationId))
            .ReturnsAsync(false);
        
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.FetchProductImagesStreamingAsync(
            ValidProductId, _validStore, ValidMigrationId, callback);

        // Assert
        Assert.Equal(5, result);
        Assert.True(callbackInvoked);
        Assert.Equal(5, capturedImages.Count);
        
        // Verify API was called
        _mockApiRequestHandler.Verify(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
            It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WithNullProductId_ShouldThrowArgumentException()
    {
        // Arrange
        Func<List<Dictionary<string, object>>, Task> callback = _ => Task.CompletedTask;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.FetchProductImagesStreamingAsync(null!, _validStore, ValidMigrationId, callback));
        
        Assert.Equal("sourceProductId", exception.ParamName);
        Assert.Contains("Source product ID cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WithEmptyProductId_ShouldThrowArgumentException()
    {
        // Arrange
        Func<List<Dictionary<string, object>>, Task> callback = _ => Task.CompletedTask;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.FetchProductImagesStreamingAsync("", _validStore, ValidMigrationId, callback));
        
        Assert.Equal("sourceProductId", exception.ParamName);
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WithInvalidStore_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidStore = new StoreConfiguration(); // Missing required fields
        Func<List<Dictionary<string, object>>, Task> callback = _ => Task.CompletedTask;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.FetchProductImagesStreamingAsync(ValidProductId, invalidStore, ValidMigrationId, callback));
        
        Assert.Equal("sourceStore", exception.ParamName);
        Assert.Contains("Invalid source store configuration", exception.Message);
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WithNullMigrationId_ShouldThrowArgumentException()
    {
        // Arrange
        Func<List<Dictionary<string, object>>, Task> callback = _ => Task.CompletedTask;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.FetchProductImagesStreamingAsync(ValidProductId, _validStore, null!, callback));
        
        Assert.Equal("migrationId", exception.ParamName);
        Assert.Contains("Migration ID cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WithNullCallback_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.FetchProductImagesStreamingAsync(ValidProductId, _validStore, ValidMigrationId, null!));
        
        Assert.Equal("onImageChunkFetched", exception.ParamName);
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WithMultiplePages_ShouldStreamAllImages()
    {
        // Arrange
        var allCapturedImages = new List<Dictionary<string, object>>();
        var callbackInvocations = 0;
        
        Func<List<Dictionary<string, object>>, Task> callback = async images =>
        {
            callbackInvocations++;
            allCapturedImages.AddRange(images);
            await Task.CompletedTask;
        };

        // Setup multiple API responses
        var page1Response = CreateMockImageResponse(50); // Full page
        var page2Response = CreateMockImageResponse(30); // Partial page

        _mockCancellationStore.Setup(x => x.CheckCancellationFlagAsync(ValidMigrationId))
            .ReturnsAsync(false);

        _mockApiRequestHandler.SetupSequence(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page1Response)
            .ReturnsAsync(page2Response);

        // Act
        var result = await _service.FetchProductImagesStreamingAsync(
            ValidProductId, _validStore, ValidMigrationId, callback);

        // Assert
        Assert.Equal(80, result); // 50 + 30 images
        Assert.Equal(2, callbackInvocations); // Callback called twice
        Assert.Equal(80, allCapturedImages.Count);
        
        // Verify API was called twice
        _mockApiRequestHandler.Verify(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
            It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WithEmptyResponse_ShouldReturnZero()
    {
        // Arrange
        var callbackInvoked = false;
        Func<List<Dictionary<string, object>>, Task> callback = async images =>
        {
            callbackInvoked = true;
            await Task.CompletedTask;
        };

        var emptyResponse = CreateMockImageResponse(0); // No images
        
        _mockCancellationStore.Setup(x => x.CheckCancellationFlagAsync(ValidMigrationId))
            .ReturnsAsync(false);
        
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyResponse);

        // Act
        var result = await _service.FetchProductImagesStreamingAsync(
            ValidProductId, _validStore, ValidMigrationId, callback);

        // Assert
        Assert.Equal(0, result);
        Assert.False(callbackInvoked); // Callback should not be invoked for empty response
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WhenCancelled_ShouldThrowOperationCanceledException()
    {
        // Arrange
        Func<List<Dictionary<string, object>>, Task> callback = _ => Task.CompletedTask;

        _mockCancellationStore.Setup(x => x.CheckCancellationFlagAsync(ValidMigrationId))
            .ReturnsAsync(true); // Migration is cancelled

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.FetchProductImagesStreamingAsync(ValidProductId, _validStore, ValidMigrationId, callback));
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_With1000ImageLimit_ShouldStopAt1000()
    {
        // Arrange
        var totalCaptured = 0;
        Func<List<Dictionary<string, object>>, Task> callback = async images =>
        {
            totalCaptured += images.Count;
            await Task.CompletedTask;
        };

        // Setup 21 pages of 50 images each (would be 1050 total, but limited to 1000)
        var fullPageResponse = CreateMockImageResponse(50);
        
        _mockCancellationStore.Setup(x => x.CheckCancellationFlagAsync(ValidMigrationId))
            .ReturnsAsync(false);

        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullPageResponse);

        // Act
        var result = await _service.FetchProductImagesStreamingAsync(
            ValidProductId, _validStore, ValidMigrationId, callback);

        // Assert
        Assert.Equal(1000, result); // Should stop at 1000
        Assert.Equal(1000, totalCaptured);
        
        // Should call API exactly 20 times (20 * 50 = 1000)
        _mockApiRequestHandler.Verify(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
            It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(20));
    }

    #endregion

    #region FetchProductImagesPageAsync Tests

    [Fact]
    public async Task FetchProductImagesPageAsync_WithValidInputs_ShouldReturnImages()
    {
        // Arrange
        var mockResponse = CreateMockImageResponse(10);
        
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.FetchProductImagesPageAsync(
            ValidProductId, _validStore, 1, 10, ValidMigrationId);

        // Assert
        Assert.Equal(10, result.Count);
        
        // Verify API was called with correct URL pattern
        _mockApiRequestHandler.Verify(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
            It.Is<ApiRequest>(req => req.Url.Contains($"products/{ValidProductId}/images") && 
                                   req.Url.Contains("page=1") && 
                                   req.Url.Contains("limit=10")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchProductImagesPageAsync_WithNullProductId_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.FetchProductImagesPageAsync(null!, _validStore, 1, 10, ValidMigrationId));
        
        Assert.Equal("sourceProductId", exception.ParamName);
    }

    [Fact]
    public async Task FetchProductImagesPageAsync_WithInvalidPage_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.FetchProductImagesPageAsync(ValidProductId, _validStore, 0, 10, ValidMigrationId));
        
        Assert.Equal("page", exception.ParamName);
        Assert.Contains("Page must be greater than 0", exception.Message);
    }

    [Fact]
    public async Task FetchProductImagesPageAsync_WithInvalidLimit_ShouldThrowArgumentException()
    {
        // Test limit too small
        var exception1 = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.FetchProductImagesPageAsync(ValidProductId, _validStore, 1, 0, ValidMigrationId));
        
        Assert.Equal("limit", exception1.ParamName);
        Assert.Contains("Limit must be between 1 and 50", exception1.Message);

        // Test limit too large
        var exception2 = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.FetchProductImagesPageAsync(ValidProductId, _validStore, 1, 51, ValidMigrationId));
        
        Assert.Equal("limit", exception2.ParamName);
    }

    [Fact]
    public async Task FetchProductImagesPageAsync_WithApiException_ShouldThrowAndLog()
    {
        // Arrange
        var apiException = new HttpRequestException("API Error");
        
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(apiException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _service.FetchProductImagesPageAsync(ValidProductId, _validStore, 1, 10, ValidMigrationId));
        
        Assert.Equal("API Error", exception.Message);
    }

    #endregion

    #region Helper Methods Tests

    [Fact]
    public async Task FetchProductImagesPageAsync_WithMalformedResponse_ShouldReturnEmptyList()
    {
        // Arrange
        var malformedResponse = new Dictionary<string, object>
        {
            { "data", "invalid-data-not-array" } // Should be an array
        };
        
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(malformedResponse);

        // Act
        var result = await _service.FetchProductImagesPageAsync(
            ValidProductId, _validStore, 1, 10, ValidMigrationId);

        // Assert
        Assert.Empty(result); // Should return empty list for malformed response
    }

    [Fact]
    public async Task FetchProductImagesStreamingAsync_WithCancellationCheckException_ShouldContinueProcessing()
    {
        // Arrange
        var callbackInvoked = false;
        Func<List<Dictionary<string, object>>, Task> callback = async images =>
        {
            callbackInvoked = true;
            await Task.CompletedTask;
        };

        var mockResponse = CreateMockImageResponse(5);
        
        // First cancellation check succeeds, but later one fails
        _mockCancellationStore.SetupSequence(x => x.CheckCancellationFlagAsync(ValidMigrationId))
            .ReturnsAsync(false) // Initial check passes
            .ThrowsAsync(new Exception("Cancellation check failed")); // Subsequent check fails but should continue
        
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.FetchProductImagesStreamingAsync(
            ValidProductId, _validStore, ValidMigrationId, callback);

        // Assert
        Assert.Equal(5, result); // Should complete despite cancellation check failure
        Assert.True(callbackInvoked);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a mock BigCommerce API response with the specified number of images
    /// </summary>
    private static Dictionary<string, object> CreateMockImageResponse(int imageCount)
    {
        var images = new List<object>();
        
        for (int i = 1; i <= imageCount; i++)
        {
            var imageJson = JsonSerializer.Serialize(new
            {
                id = i,
                product_id = 123,
                url_zoom = $"https://example.com/image{i}_zoom.jpg",
                url_standard = $"https://example.com/image{i}_standard.jpg",
                url_thumbnail = $"https://example.com/image{i}_thumb.jpg",
                alt_text = $"Image {i}",
                sort_order = i,
                is_thumbnail = i == 1
            });
            
            var imageElement = JsonSerializer.Deserialize<JsonElement>(imageJson);
            images.Add(imageElement);
        }

        var dataArray = JsonSerializer.Serialize(images);
        var dataElement = JsonSerializer.Deserialize<JsonElement>(dataArray);

        return new Dictionary<string, object>
        {
            { "data", dataElement },
            { "meta", JsonSerializer.Deserialize<JsonElement>("{\"pagination\":{\"total\":"+imageCount+",\"count\":"+imageCount+",\"per_page\":50,\"current_page\":1,\"total_pages\":1}}") }
        };
    }

    #endregion
}
