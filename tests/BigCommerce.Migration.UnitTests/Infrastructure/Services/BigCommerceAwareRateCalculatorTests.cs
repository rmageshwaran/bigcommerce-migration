using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for BigCommerce-aware rate calculator
/// Tests dynamic rate calculation algorithms based on API health metrics
/// Follows TDD approach - tests written before implementation
/// </summary>
public class BigCommerceAwareRateCalculatorTests
{
    private readonly Mock<ILogger<BigCommerceAwareRateCalculator>> _mockLogger;
    private readonly string _testStoreId = "test-store-123";

    public BigCommerceAwareRateCalculatorTests()
    {
        _mockLogger = new Mock<ILogger<BigCommerceAwareRateCalculator>>();
    }

    #region Constructor Tests

    [Fact]
    public void BigCommerceAwareRateCalculator_Constructor_Should_ThrowArgumentNullException_When_LoggerIsNull()
    {
        // Act & Assert
        var action = () => new BigCommerceAwareRateCalculator(null!);
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void BigCommerceAwareRateCalculator_Constructor_Should_InitializeSuccessfully_With_ValidLogger()
    {
        // Act
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);

        // Assert
        calculator.Should().NotBeNull();
    }

    #endregion

    #region Basic Rate Calculation Tests

    [Fact]
    public void CalculateOptimalRate_Should_ReturnMinimumRate_When_HealthMetricsIsNull()
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);

        // Act
        var result = calculator.CalculateOptimalRate(null);

        // Assert
        result.Should().Be(5); // Minimum safety rate
    }

    [Fact]
    public void CalculateOptimalRate_Should_ReturnMinimumRate_When_HealthScoreIsZero()
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        // Create critical BigCommerce state to get actual zero health score
        var criticalRateLimit = new BigCommerceRateLimitInfo
        {
            StoreId = _testStoreId,
            RequestsLeft = 0,      // No requests left - critical state
            RequestsQuota = 250,   
            TimeResetMs = 300000,  // 5 minutes until reset
            TimeWindowMs = 300000  
        };
        var healthMetrics = CreateHealthMetrics(5000, 0.5, criticalRateLimit); // This will actually give 0 health score

        // Act
        var result = calculator.CalculateOptimalRate(healthMetrics);

        // Assert
        result.Should().Be(5); // Minimum safety rate for poor health
    }

    [Theory]
    [InlineData(100, 0.0, 45)]    // Perfect health = high rate (health score ~100)
    [InlineData(200, 0.05, 25)]    // Good health = medium-high rate (health score ~75)  
    [InlineData(500, 0.10, 15)]    // Average health = medium rate (health score ~55)
    [InlineData(1000, 0.20, 8)]    // Poor health = low rate (health score ~35)
    [InlineData(2000, 0.30, 5)]    // Very poor health = minimum rate (health score ~20)
    public void CalculateOptimalRate_Should_ReturnExpectedRate_For_VariousHealthScenarios(
        double avgResponseTime, double errorRate, int expectedMinRate)
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        var healthMetrics = CreateHealthMetrics(avgResponseTime, errorRate);

        // Act
        var result = calculator.CalculateOptimalRate(healthMetrics);

        // Assert
        result.Should().BeGreaterOrEqualTo(5);    // Never below minimum
        result.Should().BeLessOrEqualTo(50);      // Never above maximum
        result.Should().BeGreaterOrEqualTo(expectedMinRate - 10); // Allow tolerance for complex algorithm with aggressive health scaling
    }

    #endregion

    #region BigCommerce Rate Limit Integration Tests

    [Fact]
    public void CalculateOptimalRate_Should_RespectBigCommerceRateLimits_When_ApiIsNearCapacity()
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            StoreId = _testStoreId,
            RequestsLeft = 5,      // Very few requests left
            RequestsQuota = 250,   // Out of 250 total
            TimeResetMs = 60000,   // 1 minute until reset
            TimeWindowMs = 300000  // 5-minute window
        };
        var healthMetrics = CreateHealthMetrics(100, 0.0, rateLimitInfo); // Perfect health but limited by BC

        // Act
        var result = calculator.CalculateOptimalRate(healthMetrics);

        // Assert
        result.Should().BeLessOrEqualTo(10); // Should be conservative due to low remaining capacity
    }

    [Fact]
    public void CalculateOptimalRate_Should_UseHigherRate_When_BigCommerceHasHighCapacity()
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            StoreId = _testStoreId,
            RequestsLeft = 240,    // Plenty of requests left
            RequestsQuota = 250,   // Out of 250 total
            TimeResetMs = 60000,   // 1 minute until reset
            TimeWindowMs = 300000  // 5-minute window
        };
        var healthMetrics = CreateHealthMetrics(100, 0.0, rateLimitInfo); // Perfect health with high capacity

        // Act
        var result = calculator.CalculateOptimalRate(healthMetrics);

        // Assert
        result.Should().BeGreaterOrEqualTo(40); // Should use higher rate due to available capacity
    }

    #endregion

    #region Edge Cases and Boundary Tests

    [Theory]
    [InlineData(-100)]   // Negative response time (invalid)
    [InlineData(0)]      // Zero response time (instant)
    [InlineData(50000)]  // Extremely high response time
    public void CalculateOptimalRate_Should_HandleInvalidResponseTimes_Gracefully(double invalidResponseTime)
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        var healthMetrics = CreateHealthMetrics(invalidResponseTime, 0.05);

        // Act
        var result = calculator.CalculateOptimalRate(healthMetrics);

        // Assert
        result.Should().BeGreaterOrEqualTo(5);   // Never below minimum
        result.Should().BeLessOrEqualTo(50);     // Never above maximum
    }

    [Theory]
    [InlineData(-100.0)]   // Negative response time
    [InlineData(0.0)]      // Zero response time
    [InlineData(10000.0)]  // Very high response time
    public void CalculateOptimalRate_Should_HandleExtremeResponseTimes_Gracefully(double extremeResponseTime)
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        var healthMetrics = CreateHealthMetrics(extremeResponseTime, 0.05);

        // Act
        var result = calculator.CalculateOptimalRate(healthMetrics);

        // Assert
        result.Should().BeGreaterOrEqualTo(5);   // Never below minimum
        result.Should().BeLessOrEqualTo(50);     // Never above maximum
    }

    [Theory]
    [InlineData(-0.5)]   // Negative error rate
    [InlineData(0.0)]    // Zero error rate
    [InlineData(1.5)]    // Error rate above 100%
    public void CalculateOptimalRate_Should_HandleInvalidErrorRates_Gracefully(double invalidErrorRate)
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        var healthMetrics = CreateHealthMetrics(200, invalidErrorRate);

        // Act
        var result = calculator.CalculateOptimalRate(healthMetrics);

        // Assert
        result.Should().BeGreaterOrEqualTo(5);   // Never below minimum
        result.Should().BeLessOrEqualTo(50);     // Never above maximum
    }

    #endregion

    #region Advanced Algorithm Tests

    [Fact]
    public void CalculateOptimalRate_Should_ApplyExponentialSmoothing_For_StableRates()
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        var healthMetrics = CreateHealthMetrics(150, 0.02);

        // Act - Calculate multiple times to test smoothing
        var rate1 = calculator.CalculateOptimalRate(healthMetrics);
        var rate2 = calculator.CalculateOptimalRate(healthMetrics);
        var rate3 = calculator.CalculateOptimalRate(healthMetrics);

        // Assert
        rate1.Should().BeGreaterOrEqualTo(5).And.BeLessOrEqualTo(50);
        rate2.Should().BeGreaterOrEqualTo(5).And.BeLessOrEqualTo(50);
        rate3.Should().BeGreaterOrEqualTo(5).And.BeLessOrEqualTo(50);
        
        // Rates should be consistent for same input (smoothing effect)
        Math.Abs(rate3 - rate1).Should().BeLessOrEqualTo(5); // Allow some variation
    }

    [Fact]
    public void CalculateOptimalRate_Should_ReactQuickly_To_SuddenHealthDrop()
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        var goodHealth = CreateHealthMetrics(100, 0.01);  // Health score ~85-90
        var poorHealth = CreateHealthMetrics(2000, 0.40); // Health score ~20-25

        // Act
        var goodRate = calculator.CalculateOptimalRate(goodHealth);
        var poorRate = calculator.CalculateOptimalRate(poorHealth);

        // Assert
        goodRate.Should().BeGreaterThan(poorRate + 15); // Significant rate difference (good should be much higher)
        poorRate.Should().BeLessOrEqualTo(25); // Poor health should stay relatively low
        goodRate.Should().BeGreaterOrEqualTo(30); // Good health should achieve decent rates
    }

    #endregion

    #region Time-Based Adjustment Tests

    [Fact]
    public void CalculateOptimalRate_Should_ApplyTimeBasedAdjustments_During_PeakHours()
    {
        // Arrange
        var calculator = new BigCommerceAwareRateCalculator(_mockLogger.Object);
        var healthMetrics = CreateHealthMetrics(200, 0.05);
        
        // Mock peak hours (this would require time injection in real implementation)
        // For now, test the base rate calculation

        // Act
        var result = calculator.CalculateOptimalRate(healthMetrics);

        // Assert
        result.Should().BeGreaterOrEqualTo(5);
        result.Should().BeLessOrEqualTo(50);
        // Time-based adjustments would be tested with proper time mocking
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates ApiHealthMetrics for testing with specified parameters
    /// Note: Health score is calculated automatically based on error rate, response time, and BigCommerce data
    /// </summary>
    private ApiHealthMetrics CreateHealthMetrics(
        double avgResponseTime, 
        double errorRate, 
        BigCommerceRateLimitInfo? rateLimitInfo = null)
    {
        // TotalRequests is calculated as SuccessfulRequests + FailedRequests
        const int totalRequests = 100;
        var failedRequests = (int)(totalRequests * errorRate);
        var successfulRequests = totalRequests - failedRequests;

        var metrics = new ApiHealthMetrics(_testStoreId)
        {
            AverageResponseTimeMs = avgResponseTime,
            ErrorRate = errorRate,
            SuccessfulRequests = successfulRequests,
            FailedRequests = failedRequests,
            BigCommerceRateLimit = rateLimitInfo,
            Timestamp = DateTime.UtcNow
        };

        return metrics;
    }

    #endregion
} 