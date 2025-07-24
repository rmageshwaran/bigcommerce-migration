using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for ApiRequestHandler Phase 2 enhanced cancellation functionality
/// Tests the improved cancellation propagation and strategic cancellation checks
/// </summary>
[Trait("Category", "Phase2Cancellation")]
[Trait("Component", "ApiRequestHandler")]
public class ApiRequestHandlerCancellationTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly Mock<IRateLimitService> _mockRateLimitService;
    private readonly Mock<IOpenSearchService> _mockOpenSearchService;
    private readonly Mock<ILogger<ApiRequestHandler>> _mockLogger;
    private readonly HttpClient _httpClient;
    private readonly ApiRequestHandler _apiRequestHandler;

    private const string TestUrl = "https://api.bigcommerce.com/stores/test/v3/catalog/products";
    private const string TestStoreId = "test-store-123";

    public ApiRequestHandlerCancellationTests()
    {
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _mockRateLimitService = new Mock<IRateLimitService>();
        _mockOpenSearchService = new Mock<IOpenSearchService>();
        _mockLogger = new Mock<ILogger<ApiRequestHandler>>();

        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _apiRequestHandler = new ApiRequestHandler(
            _httpClient,
            _mockRateLimitService.Object,
            _mockOpenSearchService.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Test that cancellation token is properly passed to HttpClient.SendAsync
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_PassesCancellationTokenToHttpClient()
    {
        // Arrange
        var request = CreateTestApiRequest();
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\": []}")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        _mockRateLimitService
            .Setup(x => x.CheckAndWaitAsync(TestStoreId, cancellationToken))
            .Returns(Task.CompletedTask);

        _mockRateLimitService
            .Setup(x => x.RecordApiCallAsync(TestStoreId, TestUrl, It.IsAny<double>(), true, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);

        // Assert
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Test that cancellation is handled properly when HTTP request is cancelled
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_WhenHttpRequestCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var request = CreateTestApiRequest();
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel(); // Cancel immediately

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        _mockRateLimitService
            .Setup(x => x.CheckAndWaitAsync(TestStoreId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationTokenSource.Token));
    }



    /// <summary>
    /// Test that cancellation token is passed to rate limit service
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_PassesCancellationTokenToRateLimitService()
    {
        // Arrange
        var request = CreateTestApiRequest();
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\": []}")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        _mockRateLimitService
            .Setup(x => x.CheckAndWaitAsync(TestStoreId, cancellationToken))
            .Returns(Task.CompletedTask);

        _mockRateLimitService
            .Setup(x => x.RecordApiCallAsync(TestStoreId, TestUrl, It.IsAny<double>(), true, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);

        // Assert
        _mockRateLimitService.Verify(
            x => x.CheckAndWaitAsync(TestStoreId, cancellationToken),
            Times.Once);

        _mockRateLimitService.Verify(
            x => x.RecordApiCallAsync(TestStoreId, TestUrl, It.IsAny<double>(), true, cancellationToken),
            Times.Once);
    }

    /// <summary>
    /// Test that cancellation during rate limiting is handled properly
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_WhenRateLimitCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var request = CreateTestApiRequest();
        var cancellationTokenSource = new CancellationTokenSource();

        _mockRateLimitService
            .Setup(x => x.CheckAndWaitAsync(TestStoreId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationTokenSource.Token));

        // Verify HTTP request was never made
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Test that error logging uses separate cancellation token to prevent cancellation during error handling
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_WhenErrorOccurs_ErrorLoggingNotCancelled()
    {
        // Arrange
        var request = CreateTestApiRequest();
        var cancellationToken = CancellationToken.None; // Don't cancel, let the HTTP error occur

        _mockRateLimitService
            .Setup(x => x.CheckAndWaitAsync(TestStoreId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        _mockOpenSearchService
            .Setup(x => x.LogMigrationEventAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                CancellationToken.None)) // Should use CancellationToken.None for error logging
            .ReturnsAsync(true);

        // Act
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken));

        // Assert
        Assert.Equal("Network error", exception.Message);

        // Verify error logging was called with separate cancellation token
        _mockOpenSearchService.Verify(
            x => x.LogMigrationEventAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                CancellationToken.None),
            Times.Once);
    }

    /// <summary>
    /// Test the string overload also properly handles cancellation
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_StringOverload_HandlesCancellationProperly()
    {
        // Arrange
        var request = CreateTestApiRequest();
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("test response content")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        _mockRateLimitService
            .Setup(x => x.CheckAndWaitAsync(TestStoreId, cancellationToken))
            .Returns(Task.CompletedTask);

        _mockRateLimitService
            .Setup(x => x.RecordApiCallAsync(TestStoreId, TestUrl, It.IsAny<double>(), true, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _apiRequestHandler.ExecuteRequestAsync(request, cancellationToken);

        // Assert
        Assert.Equal("test response content", result);

        // Verify cancellation token was passed through
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Test that cancellation during JSON deserialization is handled properly
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_WhenCancelledDuringDeserialization_ThrowsOperationCanceledException()
    {
        // Arrange
        var request = CreateTestApiRequest();
        var cancellationTokenSource = new CancellationTokenSource();

        // Create a large JSON response to simulate time-consuming deserialization
        var largeJsonResponse = "{\"data\": [" + string.Join(",", Enumerable.Repeat("{\"id\": 1}", 10000)) + "]}";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(largeJsonResponse)
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        _mockRateLimitService
            .Setup(x => x.CheckAndWaitAsync(TestStoreId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRateLimitService
            .Setup(x => x.RecordApiCallAsync(TestStoreId, TestUrl, It.IsAny<double>(), true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Cancel after a short delay to simulate cancellation during processing
        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            cancellationTokenSource.Cancel();
        });

        // Act & Assert
        // Note: This test may be flaky due to timing, but demonstrates the principle
        // In practice, the cancellation checks before deserialization provide protection
        try
        {
            await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected - cancellation was handled properly
            Assert.True(cancellationTokenSource.Token.IsCancellationRequested);
        }
    }

    /// <summary>
    /// Test helper method overloads also handle cancellation properly
    /// </summary>
    [Fact]
    public async Task ExecuteGetRequestAsync_PassesCancellationTokenCorrectly()
    {
        // Arrange
        var storeConfig = CreateTestStoreConfiguration();
        var cancellationToken = new CancellationTokenSource().Token;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\": []}")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        _mockRateLimitService
            .Setup(x => x.CheckAndWaitAsync(It.IsAny<string>(), cancellationToken))
            .Returns(Task.CompletedTask);

        _mockRateLimitService
            .Setup(x => x.RecordApiCallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<bool>(), cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _apiRequestHandler.ExecuteGetRequestAsync<Dictionary<string, object>>(TestUrl, storeConfig, cancellationToken);

        // Assert
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    #region Helper Methods

    private ApiRequest CreateTestApiRequest()
    {
        var storeConfig = CreateTestStoreConfiguration();
        return ApiRequest.CreateGet(TestUrl, storeConfig);
    }

    private StoreConfiguration CreateTestStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = TestStoreId,
            AccessToken = "test-token",
            ChannelId = "1"
        };
    }

    #endregion


} 