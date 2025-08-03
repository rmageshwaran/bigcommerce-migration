using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for ApiHealthMonitor service
/// Tests API health aggregation and monitoring capabilities for dynamic rate limiting
/// Follows TDD approach - tests written before implementation
/// </summary>
public class ApiHealthMonitorTests
{
    private readonly Mock<ILogger<ApiHealthMonitor>> _mockLogger;
    private readonly Mock<IDateTimeProvider> _mockDateTimeProvider;
    private readonly DateTime _testTimestamp = new(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
    private readonly string _testStoreId = "test-store-456";

    public ApiHealthMonitorTests()
    {
        _mockLogger = new Mock<ILogger<ApiHealthMonitor>>();
        _mockDateTimeProvider = new Mock<IDateTimeProvider>();
        _mockDateTimeProvider.Setup(x => x.UtcNow).Returns(_testTimestamp);
    }

    #region Constructor Tests

    [Fact]
    public void ApiHealthMonitor_Constructor_Should_InitializeCorrectly()
    {
        // Act & Assert
        var act = () => new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        act.Should().NotThrow();
    }

    [Fact]
    public void ApiHealthMonitor_Constructor_Should_ThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        var act = () => new ApiHealthMonitor(null!, _mockDateTimeProvider.Object);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void ApiHealthMonitor_Constructor_Should_ThrowArgumentNullException_WhenDateTimeProviderIsNull()
    {
        // Act & Assert
        var act = () => new ApiHealthMonitor(_mockLogger.Object, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("dateTimeProvider");
    }

    #endregion

    #region RecordApiCall Tests

    [Fact]
    public async Task RecordApiCallAsync_Should_AcceptValidBigCommerceRateLimitInfo()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var rateLimitInfo = CreateValidRateLimitInfo();

        // Act
        var act = async () => await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 150.0, true, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecordApiCallAsync_Should_ThrowArgumentException_WhenStoreIdIsEmpty()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var rateLimitInfo = CreateValidRateLimitInfo();

        // Act & Assert
        var act = async () => await monitor.RecordApiCallAsync(string.Empty, rateLimitInfo, 150.0, true, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("storeId");
    }

    [Fact]
    public async Task RecordApiCallAsync_Should_ThrowArgumentOutOfRangeException_WhenResponseTimeIsNegative()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var rateLimitInfo = CreateValidRateLimitInfo();

        // Act & Assert
        var act = async () => await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, -50.0, true, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("responseTimeMs");
    }

    [Fact]
    public async Task RecordApiCallAsync_Should_AcceptNullRateLimitInfo()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);

        // Act
        var act = async () => await monitor.RecordApiCallAsync(_testStoreId, null, 150.0, true, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(0.0)]     // Instant response
    [InlineData(100.5)]   // Normal response
    [InlineData(5000.0)]  // Slow response
    [InlineData(30000.0)] // Very slow response
    public async Task RecordApiCallAsync_Should_AcceptVariousResponseTimes(double responseTimeMs)
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var rateLimitInfo = CreateValidRateLimitInfo();

        // Act
        var act = async () => await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, responseTimeMs, true, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetApiHealth Tests

    [Fact]
    public async Task GetApiHealthAsync_Should_ReturnDefaultHealthMetrics_WhenNoDataRecorded()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);

        // Act
        var result = await monitor.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.StoreId.Should().Be(_testStoreId);
        result.AverageResponseTimeMs.Should().Be(0.0);
        result.ErrorRate.Should().Be(0.0);
        result.TotalRequests.Should().Be(0);
        result.GetHealthScore().Should().Be(80); // 100 - 20 (no BigCommerce data penalty)
        result.Timestamp.Should().BeCloseTo(_testTimestamp, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetApiHealthAsync_Should_ThrowArgumentException_WhenStoreIdIsNull()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);

        // Act & Assert
        var act = async () => await monitor.GetApiHealthAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("storeId");
    }

    [Fact]
    public async Task GetApiHealthAsync_Should_CalculateCorrectAverageResponseTime()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var rateLimitInfo = CreateValidRateLimitInfo();

        // Record multiple API calls with different response times
        await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 100.0, true, CancellationToken.None);
        await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 200.0, true, CancellationToken.None);
        await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 300.0, true, CancellationToken.None);

        // Act
        var result = await monitor.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.AverageResponseTimeMs.Should().Be(200.0); // (100 + 200 + 300) / 3 = 200
        result.TotalRequests.Should().Be(3);
    }

    [Fact]
    public async Task GetApiHealthAsync_Should_CalculateCorrectErrorRate()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var rateLimitInfo = CreateValidRateLimitInfo();

        // Record API calls with mixed success/failure
        await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 100.0, true, CancellationToken.None);   // Success
        await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 200.0, false, CancellationToken.None);  // Error
        await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 150.0, true, CancellationToken.None);   // Success
        await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 180.0, false, CancellationToken.None);  // Error

        // Act
        var result = await monitor.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.ErrorRate.Should().Be(0.5); // 2 errors out of 4 requests = 50%
        result.TotalRequests.Should().Be(4);
    }

    [Fact]
    public async Task GetApiHealthAsync_Should_UseLatestBigCommerceRateLimitInfo()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var firstRateLimitInfo = CreateValidRateLimitInfo(requestsLeft: 100, quota: 250);
        var latestRateLimitInfo = CreateValidRateLimitInfo(requestsLeft: 50, quota: 250);

        // Record multiple API calls with different BigCommerce rate limit info
        await monitor.RecordApiCallAsync(_testStoreId, firstRateLimitInfo, 100.0, true, CancellationToken.None);
        await monitor.RecordApiCallAsync(_testStoreId, latestRateLimitInfo, 150.0, true, CancellationToken.None);

        // Act
        var result = await monitor.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.BigCommerceRateLimit.Should().NotBeNull();
        result.BigCommerceRateLimit!.RequestsLeft.Should().Be(50); // Latest value
        result.BigCommerceRateLimit.RequestsQuota.Should().Be(250);
    }

    #endregion

    #region Health Score Calculation Tests

    [Theory]
    [InlineData(50.0, 0.0, 95)]   // Fast response, no errors = Excellent health
    [InlineData(200.0, 0.0, 85)]  // Moderate response, no errors = Good health  
    [InlineData(1000.0, 0.0, 60)] // Slow response, no errors = Fair health
    [InlineData(100.0, 0.1, 75)]  // Fast response, 10% errors = Good health
    [InlineData(500.0, 0.3, 35)]  // Slow response, 30% errors = Poor health
    public async Task GetApiHealthAsync_Should_CalculateCorrectHealthScore(double avgResponseTime, double errorRate, int expectedMinScore)
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var rateLimitInfo = CreateValidRateLimitInfo();

        // Simulate the scenario based on parameters
        var totalCalls = 10;
        var errorCalls = (int)(totalCalls * errorRate);
        var successCalls = totalCalls - errorCalls;

        for (int i = 0; i < successCalls; i++)
        {
            await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, avgResponseTime, true, CancellationToken.None);
        }
        
        for (int i = 0; i < errorCalls; i++)
        {
            await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, avgResponseTime, false, CancellationToken.None);
        }

        // Act
        var result = await monitor.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.GetHealthScore().Should().BeGreaterOrEqualTo(expectedMinScore - 10); // Allow 10 point tolerance
        result.GetHealthScore().Should().BeLessOrEqualTo(100);
        result.GetHealthScore().Should().BeGreaterOrEqualTo(0);
    }

    #endregion

    #region Time Window Tests

    [Fact]
    public async Task GetApiHealthAsync_Should_OnlyIncludeDataWithinTimeWindow()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var rateLimitInfo = CreateValidRateLimitInfo();

        // Record some API calls
        await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 100.0, true, CancellationToken.None);
        
        // Advance time beyond the monitoring window (assume 5 minutes window)
        _mockDateTimeProvider.Setup(x => x.UtcNow).Returns(_testTimestamp.AddMinutes(10));
        
        // Record new API calls
        await monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 200.0, false, CancellationToken.None);

        // Act
        var result = await monitor.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.TotalRequests.Should().Be(1); // Only the recent call should be included
        result.AverageResponseTimeMs.Should().Be(200.0);
        result.ErrorRate.Should().Be(1.0); // 100% error rate for the single recent call
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task ApiHealthMonitor_Should_HandleConcurrentAccess_Safely()
    {
        // Arrange
        var monitor = new ApiHealthMonitor(_mockLogger.Object, _mockDateTimeProvider.Object);
        var rateLimitInfo = CreateValidRateLimitInfo();
        var tasks = new List<Task>();

        // Act - Simulate concurrent API call recordings
        for (int i = 0; i < 20; i++)
        {
            var task = monitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 100.0 + i, i % 2 == 0, CancellationToken.None);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Get health metrics
        var result = await monitor.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.TotalRequests.Should().Be(20);
        result.ErrorRate.Should().Be(0.5); // Half the calls were errors (i % 2 == 0 for success)
        result.Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    private BigCommerceRateLimitInfo CreateValidRateLimitInfo(int requestsLeft = 245, int quota = 250, long timeResetMs = 45000, long timeWindowMs = 300000)
    {
        return new BigCommerceRateLimitInfo
        {
            StoreId = _testStoreId,
            RequestsLeft = requestsLeft,
            RequestsQuota = quota,
            TimeResetMs = timeResetMs,
            TimeWindowMs = timeWindowMs,
            Timestamp = _testTimestamp
        };
    }

    #endregion
} 