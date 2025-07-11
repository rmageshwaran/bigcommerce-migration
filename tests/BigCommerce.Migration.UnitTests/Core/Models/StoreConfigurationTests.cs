using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for StoreConfiguration model
/// Tests validation, URL generation, and authentication headers
/// </summary>
public class StoreConfigurationTests
{
    [Fact]
    public void StoreConfiguration_WithValidProperties_ShouldBeValid()
    {
        // Arrange
        var config = new StoreConfiguration
        {
            StoreId = "v6q95r5n91",
            AccessToken = "test-access-token-123",
            ChannelId = "1"
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "token", "1")]
    [InlineData("", "token", "1")]
    [InlineData("   ", "token", "1")]
    [InlineData("store", null, "1")]
    [InlineData("store", "", "1")]
    [InlineData("store", "   ", "1")]
    [InlineData("store", "token", null)]
    [InlineData("store", "token", "")]
    [InlineData("store", "token", "   ")]
    public void StoreConfiguration_WithInvalidProperties_ShouldNotBeValid(string? storeId, string? accessToken, string? channelId)
    {
        // Arrange
        var config = new StoreConfiguration
        {
            StoreId = storeId,
            AccessToken = accessToken,
            ChannelId = channelId
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void GetApiBaseUrl_WithDefaultBaseUrl_ShouldReturnCorrectUrl()
    {
        // Arrange
        var config = new StoreConfiguration
        {
            StoreId = "v6q95r5n91",
            AccessToken = "test-token",
            ChannelId = "1"
        };

        // Act
        var url = config.GetApiBaseUrl();

        // Assert
        url.Should().Be("https://api.bigcommerce.com/stores/v6q95r5n91/v3");
    }

    [Fact]
    public void GetApiBaseUrl_WithCustomBaseUrl_ShouldReturnCorrectUrl()
    {
        // Arrange
        var config = new StoreConfiguration
        {
            StoreId = "v6q95r5n91",
            AccessToken = "test-token",
            ChannelId = "1",
            BaseUrl = "https://custom.api.com"
        };

        // Act
        var url = config.GetApiBaseUrl();

        // Assert
        url.Should().Be("https://custom.api.com/stores/v6q95r5n91/v3");
    }

    [Fact]
    public void GetAuthHeaders_ShouldReturnCorrectHeaders()
    {
        // Arrange
        var config = new StoreConfiguration
        {
            StoreId = "v6q95r5n91",
            AccessToken = "test-access-token-123",
            ChannelId = "1"
        };

        // Act
        var headers = config.GetAuthHeaders();

        // Assert
        headers.Should().ContainKey("X-Auth-Token");
        headers["X-Auth-Token"].Should().Be("test-access-token-123");
        headers.Should().ContainKey("Accept");
        headers["Accept"].Should().Be("application/json");
        headers.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("", "[EMPTY]")]
    [InlineData("   ", "[EMPTY]")]
    [InlineData("abc", "****")]
    [InlineData("abcdefgh", "****")]
    [InlineData("abcdefghijk", "abcd****ijk")]
    [InlineData("test-access-token-123456", "test****3456")]
    public void GetMaskedAccessToken_ShouldMaskTokenCorrectly(string token, string expected)
    {
        // Arrange
        var config = new StoreConfiguration
        {
            StoreId = "v6q95r5n91",
            AccessToken = token,
            ChannelId = "1"
        };

        // Act
        var masked = config.GetMaskedAccessToken();

        // Assert
        masked.Should().Be(expected);
    }

    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var config = new StoreConfiguration
        {
            StoreId = "v6q95r5n91",
            AccessToken = "test-access-token-123",
            ChannelId = "1"
        };

        // Act
        var result = config.ToString();

        // Assert
        result.Should().Be("Store: v6q95r5n91, Channel: 1, Token: test****123");
    }
} 