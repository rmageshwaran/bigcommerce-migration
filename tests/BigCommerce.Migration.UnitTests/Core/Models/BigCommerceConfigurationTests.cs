using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for BigCommerceConfiguration model (global settings only)
/// Tests global configuration validation and URL generation
/// </summary>
public class BigCommerceConfigurationTests
{
    [Fact]
    public void BigCommerceConfiguration_WithValidSettings_ShouldBeValid()
    {
        // Arrange
        var config = new BigCommerceConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com",
            RequestTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3,
            RateLimitRequestsPerSecond = 12,
            EnableDebugLogging = false,
            UserAgent = "BigCommerce-Migration-System/1.0"
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://invalid.com")]
    public void BigCommerceConfiguration_WithInvalidBaseUrl_ShouldNotBeValid(string baseUrl)
    {
        // Arrange
        var config = new BigCommerceConfiguration
        {
            BaseUrl = baseUrl,
            RequestTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3,
            RateLimitRequestsPerSecond = 12
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void BigCommerceConfiguration_WithZeroRequestTimeout_ShouldNotBeValid()
    {
        // Arrange
        var config = new BigCommerceConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com",
            RequestTimeout = TimeSpan.Zero,
            MaxRetries = 3,
            RateLimitRequestsPerSecond = 12
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void BigCommerceConfiguration_WithNegativeMaxRetries_ShouldNotBeValid()
    {
        // Arrange
        var config = new BigCommerceConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com",
            RequestTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = -1,
            RateLimitRequestsPerSecond = 12
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void BigCommerceConfiguration_WithZeroRateLimit_ShouldNotBeValid()
    {
        // Arrange
        var config = new BigCommerceConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com",
            RequestTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3,
            RateLimitRequestsPerSecond = 0
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void GetApiUrl_ShouldReturnCorrectUrl()
    {
        // Arrange
        var config = new BigCommerceConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com"
        };

        // Act
        var url = config.GetApiUrl("v6q95r5n91");

        // Assert
        url.Should().Be("https://api.bigcommerce.com/stores/v6q95r5n91/v3");
    }

    [Fact]
    public void GetApiUrl_WithTrailingSlash_ShouldReturnCorrectUrl()
    {
        // Arrange
        var config = new BigCommerceConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com/"
        };

        // Act
        var url = config.GetApiUrl("v6q95r5n91");

        // Assert
        url.Should().Be("https://api.bigcommerce.com/stores/v6q95r5n91/v3");
    }

    [Fact]
    public void GetDefaultHeaders_ShouldReturnCorrectHeaders()
    {
        // Arrange
        var config = new BigCommerceConfiguration
        {
            UserAgent = "Test-Agent/1.0"
        };

        // Act
        var headers = config.GetDefaultHeaders();

        // Assert
        headers.Should().ContainKey("Accept");
        headers["Accept"].Should().Be("application/json");
        headers.Should().ContainKey("User-Agent");
        headers["User-Agent"].Should().Be("Test-Agent/1.0");
        headers.Should().HaveCount(2);
    }

    [Fact]
    public void BigCommerceConfiguration_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var config = new BigCommerceConfiguration();

        // Assert
        config.BaseUrl.Should().Be("https://api.bigcommerce.com");
        config.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
        config.MaxRetries.Should().Be(3);
        config.RateLimitRequestsPerSecond.Should().Be(12);
        config.EnableDebugLogging.Should().BeFalse();
        config.UserAgent.Should().Be("BigCommerce-Migration-System/1.0");
    }
} 