using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for DynamicRateLimitService
/// Tests the coordination of health monitoring, rate calculation, and existing rate limiting
/// Follows TDD approach - tests written before implementation
/// </summary>
public class DynamicRateLimitServiceTests
{
    private readonly Mock<ILogger<DynamicRateLimitService>> _mockLogger;
    private readonly Mock<IRateLimitService> _mockRateLimitService;
    private readonly Mock<IApiHealthMonitor> _mockHealthMonitor;
    private readonly Mock<IRateCalculator> _mockRateCalculator;
    private readonly string _testStoreId = "test-store-123";

    public DynamicRateLimitServiceTests()
    {
        _mockLogger = new Mock<ILogger<DynamicRateLimitService>>();
        _mockRateLimitService = new Mock<IRateLimitService>();
        _mockHealthMonitor = new Mock<IApiHealthMonitor>();
        _mockRateCalculator = new Mock<IRateCalculator>();
    }

    [Fact]
    public void Constructor_Should_ThrowArgumentNullException_When_LoggerIsNull()
    {
        // Act & Assert
        var action = () => new DynamicRateLimitService(
            null!, 
            _mockRateLimitService.Object,
            _mockHealthMonitor.Object,
            _mockRateCalculator.Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_Should_ThrowArgumentNullException_When_RateLimitServiceIsNull()
    {
        // Act & Assert
        var action = () => new DynamicRateLimitService(
            _mockLogger.Object,
            null!,
            _mockHealthMonitor.Object,
            _mockRateCalculator.Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("rateLimitService");
    }

    [Fact]
    public void Constructor_Should_ThrowArgumentNullException_When_HealthMonitorIsNull()
    {
        // Act & Assert
        var action = () => new DynamicRateLimitService(
            _mockLogger.Object,
            _mockRateLimitService.Object,
            null!,
            _mockRateCalculator.Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("healthMonitor");
    }

    [Fact]
    public void Constructor_Should_ThrowArgumentNullException_When_RateCalculatorIsNull()
    {
        // Act & Assert
        var action = () => new DynamicRateLimitService(
            _mockLogger.Object,
            _mockRateLimitService.Object,
            _mockHealthMonitor.Object,
            null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("rateCalculator");
    }

    [Fact]
    public async Task GetOptimalRateAsync_Should_UseHealthDataAndCalculator_To_DetermineRate()
    {
        // Arrange
        var service = CreateService();
        var healthMetrics = CreateHealthMetrics(150, 0.02);
        var expectedRate = 35.0;

        _mockHealthMonitor.Setup(h => h.GetApiHealthAsync(_testStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthMetrics);
        _mockRateCalculator.Setup(c => c.CalculateOptimalRate(healthMetrics))
            .Returns(expectedRate);

        // Act
        var result = await service.GetOptimalRateAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.Should().Be(expectedRate);
        _mockHealthMonitor.Verify(h => h.GetApiHealthAsync(_testStoreId, It.IsAny<CancellationToken>()), Times.Once);
        _mockRateCalculator.Verify(c => c.CalculateOptimalRate(healthMetrics), Times.Once);
    }

    [Fact]
    public async Task UpdateApiHealthAsync_Should_DelegateToHealthMonitor()
    {
        // Arrange
        var service = CreateService();
        var rateLimitInfo = CreateRateLimitInfo();

        // Act
        await service.UpdateApiHealthAsync(_testStoreId, rateLimitInfo, CancellationToken.None);

        // Assert
        _mockHealthMonitor.Verify(h => h.RecordApiCallAsync(_testStoreId, rateLimitInfo, It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetApiHealthAsync_Should_DelegateToHealthMonitor()
    {
        // Arrange
        var service = CreateService();
        var expectedHealthMetrics = CreateHealthMetrics(200, 0.05);

        _mockHealthMonitor.Setup(h => h.GetApiHealthAsync(_testStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedHealthMetrics);

        // Act
        var result = await service.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expectedHealthMetrics);
        _mockHealthMonitor.Verify(h => h.GetApiHealthAsync(_testStoreId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEnhancedRateLimitStatusAsync_Should_CombineHealthDataWithOptimalRate()
    {
        // Arrange
        var service = CreateService();
        var healthMetrics = CreateHealthMetrics(300, 0.08);
        var optimalRate = 28.0;
        var baseStatus = new RateLimitStatus
        {
            StoreId = _testStoreId,
            IsLimited = false,
            RequestsRemaining = 45,
            WindowResetTime = DateTime.UtcNow.AddMinutes(2),
            RequestsPerMinute = 720,
            RecommendedDelayMs = 83
        };

        _mockHealthMonitor.Setup(h => h.GetApiHealthAsync(_testStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthMetrics);
        _mockRateCalculator.Setup(c => c.CalculateOptimalRate(healthMetrics))
            .Returns(optimalRate);
        _mockRateLimitService.Setup(r => r.GetRateLimitStatusAsync(_testStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseStatus);

        // Act
        var result = await service.GetEnhancedRateLimitStatusAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.StoreId.Should().Be(_testStoreId);
        result.OptimalRate.Should().Be((int)Math.Round(optimalRate * 60)); // Convert req/sec to req/min
        result.HealthScore.Should().Be(healthMetrics.GetHealthScore());
        result.BigCommerceRateLimit.Should().BeSameAs(healthMetrics.BigCommerceRateLimit);
        result.IsLimited.Should().Be(baseStatus.IsLimited);
        result.RequestsRemaining.Should().Be(baseStatus.RequestsRemaining);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanMakeRequestAsync_Should_UseOptimalRateForDecision(bool expectedResult)
    {
        // Arrange
        var service = CreateService();
        var healthMetrics = CreateHealthMetrics(100, 0.01);
        var optimalRate = 45.0;

        _mockHealthMonitor.Setup(h => h.GetApiHealthAsync(_testStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthMetrics);
        _mockRateCalculator.Setup(c => c.CalculateOptimalRate(healthMetrics))
            .Returns(optimalRate);

        // Mock the base service to return the expected result
        _mockRateLimitService.Setup(r => r.CanMakeRequestAsync(_testStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await service.CanMakeRequestAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.Should().Be(expectedResult);
        _mockRateLimitService.Verify(r => r.CanMakeRequestAsync(_testStoreId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordApiCallAsync_Should_UpdateHealthAndDelegateToBaseService()
    {
        // Arrange
        var service = CreateService();
        var url = "https://api.bigcommerce.com/stores/test/v3/products";
        var elapsedMs = 180.0;
        var isSuccess = true;

        // Act
        await service.RecordApiCallAsync(_testStoreId, url, elapsedMs, isSuccess, CancellationToken.None);

        // Assert
        _mockHealthMonitor.Verify(h => h.RecordApiCallAsync(_testStoreId, null, elapsedMs, isSuccess, It.IsAny<CancellationToken>()), Times.Once);
        _mockRateLimitService.Verify(r => r.RecordApiCallAsync(_testStoreId, url, elapsedMs, isSuccess, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Events_Should_BeTriggeredWhen_OptimalRateChangesSignificantly()
    {
        // Arrange
        var service = CreateService();
        var healthMetrics1 = CreateHealthMetrics(1000, 0.15);
        var healthMetrics2 = CreateHealthMetrics(150, 0.02);
        
        var optimalRateChanged = false;
        var apiHealthChanged = false;

        service.OptimalRateChanged += (sender, args) => optimalRateChanged = true;
        service.ApiHealthChanged += (sender, args) => apiHealthChanged = true;

        _mockRateCalculator.SetupSequence(c => c.CalculateOptimalRate(It.IsAny<ApiHealthMetrics>()))
            .Returns(15.0) // First call - poor health
            .Returns(40.0); // Second call - good health (significant change)

        _mockHealthMonitor.SetupSequence(h => h.GetApiHealthAsync(_testStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthMetrics1)
            .ReturnsAsync(healthMetrics2);

        // Act
        var rate1 = await service.GetOptimalRateAsync(_testStoreId, CancellationToken.None);
        var rate2 = await service.GetOptimalRateAsync(_testStoreId, CancellationToken.None);

        // Assert
        optimalRateChanged.Should().BeTrue("Rate changed significantly from 15 to 40");
        apiHealthChanged.Should().BeTrue("Health changed significantly");
    }

    [Fact]
    public async Task GetOptimalRateAsync_Should_ReturnMinimumRate_When_HealthDataUnavailable()
    {
        // Arrange
        var service = CreateService();
        
        _mockHealthMonitor.Setup(h => h.GetApiHealthAsync(_testStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiHealthMetrics)null!);

        // Act
        var result = await service.GetOptimalRateAsync(_testStoreId, CancellationToken.None);

        // Assert
        result.Should().Be(5.0); // Minimum safety rate
    }

    /// <summary>
    /// Creates a DynamicRateLimitService instance for testing
    /// </summary>
    private DynamicRateLimitService CreateService()
    {
        return new DynamicRateLimitService(
            _mockLogger.Object,
            _mockRateLimitService.Object,
            _mockHealthMonitor.Object,
            _mockRateCalculator.Object);
    }

    /// <summary>
    /// Creates ApiHealthMetrics for testing
    /// </summary>
    private ApiHealthMetrics CreateHealthMetrics(double avgResponseTime, double errorRate)
    {
        // Create with calculated values since TotalRequests is computed
        const int totalRequests = 100;
        var failedRequests = (int)(totalRequests * errorRate);
        var successfulRequests = totalRequests - failedRequests;

        return new ApiHealthMetrics(_testStoreId)
        {
            AverageResponseTimeMs = avgResponseTime,
            ErrorRate = errorRate,
            SuccessfulRequests = successfulRequests,
            FailedRequests = failedRequests,
            BigCommerceRateLimit = CreateRateLimitInfo(),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates BigCommerceRateLimitInfo for testing
    /// </summary>
    private BigCommerceRateLimitInfo CreateRateLimitInfo()
    {
        return new BigCommerceRateLimitInfo
        {
            StoreId = _testStoreId,
            RequestsLeft = 180,
            RequestsQuota = 250,
            TimeResetMs = 45000,
            TimeWindowMs = 300000,
            Timestamp = DateTime.UtcNow
        };
    }
} 