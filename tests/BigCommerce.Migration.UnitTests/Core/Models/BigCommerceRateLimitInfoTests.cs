using Xunit;
using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using System.Text.Json;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for BigCommerceRateLimitInfo data model
/// Tests BigCommerce API rate limit header data capture and validation
/// Follows TDD approach - tests written before implementation
/// </summary>
public class BigCommerceRateLimitInfoTests
{
    #region Constructor Tests

    [Fact]
    public void BigCommerceRateLimitInfo_DefaultConstructor_Should_InitializeWithDefaultValues()
    {
        // Act
        var rateLimitInfo = new BigCommerceRateLimitInfo();

        // Assert
        rateLimitInfo.RequestsLeft.Should().Be(0);
        rateLimitInfo.RequestsQuota.Should().Be(0);
        rateLimitInfo.TimeResetMs.Should().Be(0);
        rateLimitInfo.TimeWindowMs.Should().Be(0);
        rateLimitInfo.StoreId.Should().BeEmpty();
        rateLimitInfo.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void BigCommerceRateLimitInfo_ParameterizedConstructor_Should_SetAllProperties()
    {
        // Arrange
        const string storeId = "test-store-123";
        const int requestsLeft = 245;
        const int requestsQuota = 250;
        const long timeResetMs = 45000;
        const long timeWindowMs = 300000;

        // Act
        var rateLimitInfo = new BigCommerceRateLimitInfo(
            storeId, requestsLeft, requestsQuota, timeResetMs, timeWindowMs);

        // Assert
        rateLimitInfo.StoreId.Should().Be(storeId);
        rateLimitInfo.RequestsLeft.Should().Be(requestsLeft);
        rateLimitInfo.RequestsQuota.Should().Be(requestsQuota);
        rateLimitInfo.TimeResetMs.Should().Be(timeResetMs);
        rateLimitInfo.TimeWindowMs.Should().Be(timeWindowMs);
        rateLimitInfo.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Property Validation Tests

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RequestsLeft_Should_HandleNegativeValues(int negativeValue)
    {
        // Arrange & Act
        var rateLimitInfo = new BigCommerceRateLimitInfo();
        rateLimitInfo.RequestsLeft = negativeValue;

        // Assert - Should allow negative values (BigCommerce might return negative when exceeded)
        rateLimitInfo.RequestsLeft.Should().Be(negativeValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(250)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void RequestsQuota_Should_AcceptValidPositiveValues(int validQuota)
    {
        // Arrange & Act
        var rateLimitInfo = new BigCommerceRateLimitInfo();
        rateLimitInfo.RequestsQuota = validQuota;

        // Assert
        rateLimitInfo.RequestsQuota.Should().Be(validQuota);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(30000L)]
    [InlineData(300000L)]
    [InlineData(long.MaxValue)]
    public void TimeResetMs_Should_AcceptValidValues(long validTimeMs)
    {
        // Arrange & Act
        var rateLimitInfo = new BigCommerceRateLimitInfo();
        rateLimitInfo.TimeResetMs = validTimeMs;

        // Assert
        rateLimitInfo.TimeResetMs.Should().Be(validTimeMs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("store-123")]
    [InlineData("very-long-store-id-with-many-characters-123456789")]
    public void StoreId_Should_AcceptVariousStringFormats(string storeId)
    {
        // Arrange & Act
        var rateLimitInfo = new BigCommerceRateLimitInfo();
        rateLimitInfo.StoreId = storeId;

        // Assert
        rateLimitInfo.StoreId.Should().Be(storeId);
    }

    #endregion

    #region Business Logic Tests

    [Theory]
    [InlineData(50, 250, 0.8)]      // 80% utilization
    [InlineData(125, 250, 0.5)]     // 50% utilization  
    [InlineData(0, 250, 1.0)]       // 100% utilization (exhausted)
    [InlineData(250, 250, 0.0)]     // 0% utilization (full capacity)
    public void GetUtilizationPercentage_Should_CalculateCorrectly(int requestsLeft, int quota, double expected)
    {
        // Arrange
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = requestsLeft,
            RequestsQuota = quota
        };

        // Act
        var utilization = rateLimitInfo.GetUtilizationPercentage();

        // Assert
        utilization.Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void GetUtilizationPercentage_Should_ReturnZero_WhenQuotaIsZero()
    {
        // Arrange
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = 10,
            RequestsQuota = 0
        };

        // Act
        var utilization = rateLimitInfo.GetUtilizationPercentage();

        // Assert
        utilization.Should().Be(0.0);
    }

    [Theory]
    [InlineData(100, 250, false)]   // Healthy
    [InlineData(25, 250, false)]    // Still healthy
    [InlineData(10, 250, true)]     // Critical (< 5%)
    [InlineData(0, 250, true)]      // Exhausted
    public void IsCritical_Should_IdentifyCriticalCapacity(int requestsLeft, int quota, bool expectedCritical)
    {
        // Arrange
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = requestsLeft,
            RequestsQuota = quota
        };

        // Act
        var isCritical = rateLimitInfo.IsCritical();

        // Assert
        isCritical.Should().Be(expectedCritical);
    }

    [Theory]
    [InlineData(30000)]    // 30 seconds
    [InlineData(60000)]    // 1 minute
    [InlineData(300000)]   // 5 minutes
    public void GetTimeUntilReset_Should_CalculateCorrectTimeSpan(long timeResetMs)
    {
        // Arrange
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            TimeResetMs = timeResetMs
        };

        // Act
        var timeUntilReset = rateLimitInfo.GetTimeUntilReset();

        // Assert
        timeUntilReset.TotalMilliseconds.Should().Be(timeResetMs);
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public void BigCommerceRateLimitInfo_Should_SerializeToJson_Successfully()
    {
        // Arrange
        var rateLimitInfo = new BigCommerceRateLimitInfo("store-123", 245, 250, 45000, 300000);
        
        // Act
        var json = JsonSerializer.Serialize(rateLimitInfo);
        var deserialized = JsonSerializer.Deserialize<BigCommerceRateLimitInfo>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.StoreId.Should().Be(rateLimitInfo.StoreId);
        deserialized.RequestsLeft.Should().Be(rateLimitInfo.RequestsLeft);
        deserialized.RequestsQuota.Should().Be(rateLimitInfo.RequestsQuota);
        deserialized.TimeResetMs.Should().Be(rateLimitInfo.TimeResetMs);
        deserialized.TimeWindowMs.Should().Be(rateLimitInfo.TimeWindowMs);
    }

    [Fact]
    public void BigCommerceRateLimitInfo_Should_HandleNullValues_InSerialization()
    {
        // Arrange
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            StoreId = null!
        };

        // Act & Assert - Should not throw during serialization
        var json = JsonSerializer.Serialize(rateLimitInfo);
        json.Should().NotBeNull();
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void BigCommerceRateLimitInfo_Should_ImplementEquality_Correctly()
    {
        // Arrange
        var info1 = new BigCommerceRateLimitInfo("store-123", 245, 250, 45000, 300000);
        var info2 = new BigCommerceRateLimitInfo("store-123", 245, 250, 45000, 300000);
        var info3 = new BigCommerceRateLimitInfo("store-456", 245, 250, 45000, 300000);

        // Act & Assert
        info1.Equals(info2).Should().BeTrue();
        info1.Equals(info3).Should().BeFalse();
        (info1 == info2).Should().BeTrue();
        (info1 != info3).Should().BeTrue();
    }

    [Fact]
    public void BigCommerceRateLimitInfo_Should_ImplementGetHashCode_Correctly()
    {
        // Arrange
        var info1 = new BigCommerceRateLimitInfo("store-123", 245, 250, 45000, 300000);
        var info2 = new BigCommerceRateLimitInfo("store-123", 245, 250, 45000, 300000);

        // Act & Assert
        info1.GetHashCode().Should().Be(info2.GetHashCode());
    }

    #endregion

    #region Validation Tests

    [Theory]
    [InlineData("", false)]          // Empty store ID
    [InlineData(null, false)]        // Null store ID
    [InlineData("store-123", true)]  // Valid store ID
    public void IsValid_Should_ValidateStoreId(string storeId, bool expectedValid)
    {
        // Arrange
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            StoreId = storeId!,
            RequestsQuota = 250
        };

        // Act
        var isValid = rateLimitInfo.IsValid();

        // Assert
        isValid.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData(-1, false)]     // Negative quota
    [InlineData(0, false)]      // Zero quota  
    [InlineData(1, true)]       // Valid quota
    [InlineData(250, true)]     // Valid quota
    public void IsValid_Should_ValidateRequestsQuota(int quota, bool expectedValid)
    {
        // Arrange
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            StoreId = "store-123",
            RequestsQuota = quota
        };

        // Act
        var isValid = rateLimitInfo.IsValid();

        // Assert
        isValid.Should().Be(expectedValid);
    }

    #endregion
} 