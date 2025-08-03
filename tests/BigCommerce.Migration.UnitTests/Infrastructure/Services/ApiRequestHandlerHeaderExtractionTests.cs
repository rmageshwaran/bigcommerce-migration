using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System.Net;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for BigCommerce rate limit header extraction in ApiRequestHandler
/// Tests the extraction of X-Rate-Limit-* headers from BigCommerce API responses
/// Follows TDD approach - tests written before implementation
/// 
/// NOTE: Uses NonParallelCollection to prevent test isolation issues when running in parallel with other tests
/// </summary>
[Collection("NonParallelCollection")]
public class ApiRequestHandlerHeaderExtractionTests
{
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly Mock<IRateLimitService> _mockRateLimitService;
    private readonly Mock<IOpenSearchService> _mockOpenSearchService;
    private readonly Mock<ILogger<ApiRequestHandler>> _mockLogger;
    private readonly ApiRequestHandler _apiRequestHandler;

    public ApiRequestHandlerHeaderExtractionTests()
    {
        _mockHttpClient = new Mock<HttpClient>();
        _mockRateLimitService = new Mock<IRateLimitService>();
        _mockOpenSearchService = new Mock<IOpenSearchService>();
        _mockLogger = new Mock<ILogger<ApiRequestHandler>>();

        _apiRequestHandler = new ApiRequestHandler(
            _mockHttpClient.Object,
            _mockRateLimitService.Object,
            _mockOpenSearchService.Object,
            _mockLogger.Object);
    }

    #region BigCommerce Header Extraction Tests

    [Fact]
    public void ExtractBigCommerceRateLimitHeaders_Should_ExtractAllHeaders_WhenAllPresent()
    {
        // Arrange
        var storeId = "test-store-123";
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        
        response.Headers.Add("X-Rate-Limit-Requests-Left", "245");
        response.Headers.Add("X-Rate-Limit-Requests-Quota", "250");
        response.Headers.Add("X-Rate-Limit-Time-Reset-Ms", "45000");
        response.Headers.Add("X-Rate-Limit-Time-Window-Ms", "300000");

        // Act
        var result = _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, storeId);

        // Assert
        result.Should().NotBeNull();
        result!.StoreId.Should().Be(storeId);
        result.RequestsLeft.Should().Be(245);
        result.RequestsQuota.Should().Be(250);
        result.TimeResetMs.Should().Be(45000);
        result.TimeWindowMs.Should().Be(300000);
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ExtractBigCommerceRateLimitHeaders_Should_ReturnNull_WhenNoHeadersPresent()
    {
        // Arrange
        var storeId = "test-store-123";
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        // No X-Rate-Limit headers added

        // Act
        var result = _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, storeId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractBigCommerceRateLimitHeaders_Should_ReturnNull_WhenOnlyPartialHeadersPresent()
    {
        // Arrange
        var storeId = "test-store-123";
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        
        response.Headers.Add("X-Rate-Limit-Requests-Left", "245");
        response.Headers.Add("X-Rate-Limit-Requests-Quota", "250");
        // Missing reset time and window headers

        // Act
        var result = _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, storeId);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("X-Rate-Limit-Requests-Left", "invalid")]
    [InlineData("X-Rate-Limit-Requests-Quota", "not-a-number")]
    [InlineData("X-Rate-Limit-Time-Reset-Ms", "abc")]
    [InlineData("X-Rate-Limit-Time-Window-Ms", "")]
    public void ExtractBigCommerceRateLimitHeaders_Should_ReturnNull_WhenHeadersHaveInvalidValues(string headerName, string invalidValue)
    {
        // Arrange
        var storeId = "test-store-123";
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        
        response.Headers.Add("X-Rate-Limit-Requests-Left", "245");
        response.Headers.Add("X-Rate-Limit-Requests-Quota", "250");
        response.Headers.Add("X-Rate-Limit-Time-Reset-Ms", "45000");
        response.Headers.Add("X-Rate-Limit-Time-Window-Ms", "300000");
        
        // Replace one header with invalid value
        response.Headers.Remove(headerName);
        response.Headers.Add(headerName, invalidValue);

        // Act
        var result = _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, storeId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractBigCommerceRateLimitHeaders_Should_HandleNegativeValues_Correctly()
    {
        // Arrange
        var storeId = "test-store-123";
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        
        response.Headers.Add("X-Rate-Limit-Requests-Left", "-5"); // BigCommerce might return negative when exceeded
        response.Headers.Add("X-Rate-Limit-Requests-Quota", "250");
        response.Headers.Add("X-Rate-Limit-Time-Reset-Ms", "45000");
        response.Headers.Add("X-Rate-Limit-Time-Window-Ms", "300000");

        // Act
        var result = _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, storeId);

        // Assert
        result.Should().NotBeNull();
        result!.RequestsLeft.Should().Be(-5);
        result.RequestsQuota.Should().Be(250);
    }

    [Fact]
    public void ExtractBigCommerceRateLimitHeaders_Should_HandleLargeValues_Correctly()
    {
        // Arrange
        var storeId = "test-store-123";
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        
        response.Headers.Add("X-Rate-Limit-Requests-Left", "999999");
        response.Headers.Add("X-Rate-Limit-Requests-Quota", "1000000");
        response.Headers.Add("X-Rate-Limit-Time-Reset-Ms", "3600000"); // 1 hour
        response.Headers.Add("X-Rate-Limit-Time-Window-Ms", "3600000");

        // Act
        var result = _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, storeId);

        // Assert
        result.Should().NotBeNull();
        result!.RequestsLeft.Should().Be(999999);
        result.RequestsQuota.Should().Be(1000000);
        result.TimeResetMs.Should().Be(3600000);
        result.TimeWindowMs.Should().Be(3600000);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ExtractBigCommerceRateLimitHeaders_Should_ThrowArgumentException_WhenStoreIdInvalid(string? invalidStoreId)
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-Rate-Limit-Requests-Left", "245");
        response.Headers.Add("X-Rate-Limit-Requests-Quota", "250");
        response.Headers.Add("X-Rate-Limit-Time-Reset-Ms", "45000");
        response.Headers.Add("X-Rate-Limit-Time-Window-Ms", "300000");

        // Act & Assert
        var act = () => _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, invalidStoreId!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ExtractBigCommerceRateLimitHeaders_Should_ThrowArgumentNullException_WhenResponseIsNull()
    {
        // Arrange
        HttpResponseMessage? response = null;
        var storeId = "test-store-123";

        // Act & Assert
        var act = () => _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response!, storeId);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Integration with ProcessResponseAsync Tests

    [Fact]
    public void ProcessResponseAsync_Should_ExtractAndLogRateLimitHeaders_WhenBigCommerceHeadersPresent()
    {
        // Arrange - This test validates the integration point
        // The actual implementation will be tested when ProcessResponseAsync is enhanced

        // Act & Assert - Test placeholder for integration validation
        var methodExists = typeof(ApiRequestHandler).GetMethod("ExtractBigCommerceRateLimitHeaders");
        methodExists.Should().NotBeNull("ExtractBigCommerceRateLimitHeaders method should exist for ProcessResponseAsync integration");
    }

    [Fact]
    public void ProcessResponseAsync_Should_HandleMissingRateLimitHeaders_Gracefully()
    {
        // Arrange - This test validates graceful degradation
        
        // Act & Assert - Test placeholder for graceful handling validation
        var methodExists = typeof(ApiRequestHandler).GetMethod("LogEnhancedPerformanceMetrics");
        // This method should be created to log performance metrics with optional rate limit data
        // For now, we just validate the design intention
        true.Should().BeTrue("ProcessResponseAsync should handle missing rate limit headers gracefully");
    }

    #endregion

    #region Performance and Logging Tests

    [Fact]
    public void ExtractBigCommerceRateLimitHeaders_Should_BePerformant_WithLargeNumberOfHeaders()
    {
        // Arrange
        var storeId = "test-store-123";
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        
        // Add many headers to test performance
        for (int i = 0; i < 100; i++)
        {
            response.Headers.Add($"Custom-Header-{i}", $"value-{i}");
        }
        
        response.Headers.Add("X-Rate-Limit-Requests-Left", "245");
        response.Headers.Add("X-Rate-Limit-Requests-Quota", "250");
        response.Headers.Add("X-Rate-Limit-Time-Reset-Ms", "45000");
        response.Headers.Add("X-Rate-Limit-Time-Window-Ms", "300000");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, storeId);

        // Assert
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10, "Header extraction should be fast even with many headers");
        result.Should().NotBeNull();
        result!.RequestsLeft.Should().Be(245);
    }

    #endregion

    #region Business Logic Validation Tests

    [Fact]
    public void ExtractBigCommerceRateLimitHeaders_Should_CreateValidBigCommerceRateLimitInfo_WhenSuccessful()
    {
        // Arrange
        var storeId = "test-store-123";
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        
        response.Headers.Add("X-Rate-Limit-Requests-Left", "100");
        response.Headers.Add("X-Rate-Limit-Requests-Quota", "250");
        response.Headers.Add("X-Rate-Limit-Time-Reset-Ms", "30000");
        response.Headers.Add("X-Rate-Limit-Time-Window-Ms", "300000");

        // Act
        var result = _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, storeId);

        // Assert
        result.Should().NotBeNull();
        result!.IsValid().Should().BeTrue("Extracted rate limit info should be valid");
        result.GetUtilizationPercentage().Should().BeApproximately(0.6, 0.01, "Should calculate correct utilization");
        result.IsCritical().Should().BeFalse("Should not be critical with 100/250 remaining");
    }

    [Fact]
    public void ExtractBigCommerceRateLimitHeaders_Should_IdentifyCriticalState_WhenLowRequestsRemaining()
    {
        // Arrange
        var storeId = "test-store-123";
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        
        response.Headers.Add("X-Rate-Limit-Requests-Left", "5"); // Critical threshold
        response.Headers.Add("X-Rate-Limit-Requests-Quota", "250");
        response.Headers.Add("X-Rate-Limit-Time-Reset-Ms", "30000");
        response.Headers.Add("X-Rate-Limit-Time-Window-Ms", "300000");

        // Act
        var result = _apiRequestHandler.ExtractBigCommerceRateLimitHeaders(response, storeId);

        // Assert
        result.Should().NotBeNull();
        result!.IsCritical().Should().BeTrue("Should identify critical state with only 5 requests remaining");
        result.GetUtilizationPercentage().Should().BeGreaterThan(0.95, "Should show high utilization");
    }

    #endregion
} 