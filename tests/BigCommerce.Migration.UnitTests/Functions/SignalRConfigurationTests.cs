using Xunit;
using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace BigCommerce.Migration.UnitTests.Functions;

/// <summary>
/// TDD Tests for SignalRConfiguration following Test-Driven Development principles
/// Tests define expected behavior BEFORE implementation to ensure correct functionality
/// </summary>
public class SignalRConfigurationTests
{
    #region Constructor and Property Tests

    [Fact]
    public void SignalRConfiguration_DefaultConstructor_ShouldSetDefaultValues()
    {
        // Arrange & Act
        var config = new SignalRConfiguration();

        // Assert - Default values should be set
        config.BaseUrl.Should().Be("http://localhost:7071", "default should be localhost for development");
        config.TimeoutSeconds.Should().Be(5, "default timeout should be 5 seconds for fast-fail");
        config.Enabled.Should().BeTrue("SignalR should be enabled by default");
        config.EnableDebugLogging.Should().BeFalse("debug logging should be disabled by default for performance");
        config.MaxRetries.Should().Be(0, "no retries by default to respect user's preference");
    }

    [Fact]
    public void SignalRConfiguration_BaseUrl_ShouldAcceptValidUrls()
    {
        // Arrange
        var config = new SignalRConfiguration();
        var validUrls = new[]
        {
            "http://localhost:7071",
            "https://localhost:7071", 
            "https://my-functions-app.azurewebsites.net",
            "https://api.mycompany.com:8080"
        };

        // Act & Assert
        foreach (var url in validUrls)
        {
            config.BaseUrl = url;
            config.BaseUrl.Should().Be(url, $"should accept valid URL: {url}");
        }
    }

    [Fact]
    public void SignalRConfiguration_TimeoutSeconds_ShouldAcceptValidRange()
    {
        // Arrange
        var config = new SignalRConfiguration();
        var validTimeouts = new[] { 1, 5, 10, 30, 60, 300 };

        // Act & Assert
        foreach (var timeout in validTimeouts)
        {
            config.TimeoutSeconds = timeout;
            config.TimeoutSeconds.Should().Be(timeout, $"should accept valid timeout: {timeout}");
        }
    }

    #endregion

    #region Validation Tests (TDD - Define expected validation behavior)

    [Fact]
    public void SignalRConfiguration_IsValid_WithValidConfiguration_ShouldReturnTrue()
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://my-functions-app.azurewebsites.net",
            TimeoutSeconds = 10,
            Enabled = true,
            EnableDebugLogging = false,
            MaxRetries = 0
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeTrue("valid configuration should return true");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid-url")]
    [InlineData("ftp://localhost")]
    [InlineData("not-a-url-at-all")]
    public void SignalRConfiguration_IsValid_WithInvalidBaseUrl_ShouldReturnFalse(string invalidUrl)
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = invalidUrl,
            TimeoutSeconds = 10,
            Enabled = true
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeFalse($"invalid URL '{invalidUrl}' should make configuration invalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    [InlineData(301)]
    [InlineData(1000)]
    public void SignalRConfiguration_IsValid_WithInvalidTimeout_ShouldReturnFalse(int invalidTimeout)
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://localhost:7071",
            TimeoutSeconds = invalidTimeout,
            Enabled = true
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeFalse($"invalid timeout '{invalidTimeout}' should make configuration invalid");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(100)]
    public void SignalRConfiguration_IsValid_WithInvalidMaxRetries_ShouldReturnFalse(int invalidRetries)
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://localhost:7071",
            TimeoutSeconds = 10,
            MaxRetries = invalidRetries,
            Enabled = true
        };

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeFalse($"invalid max retries '{invalidRetries}' should make configuration invalid");
    }

    #endregion

    #region URL Helper Method Tests (TDD)

    [Fact]
    public void SignalRConfiguration_GetFullUrl_ShouldCombineBaseUrlAndEndpoint()
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://my-functions-app.azurewebsites.net"
        };
        var endpoint = "/api/signalr/migration-progress";

        // Act
        var fullUrl = config.GetFullUrl(endpoint);

        // Assert
        fullUrl.Should().Be("https://my-functions-app.azurewebsites.net/api/signalr/migration-progress");
    }

    [Fact]
    public void SignalRConfiguration_GetFullUrl_WithTrailingSlashInBaseUrl_ShouldNotDuplicate()
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://my-functions-app.azurewebsites.net/"
        };
        var endpoint = "/api/signalr/migration-progress";

        // Act
        var fullUrl = config.GetFullUrl(endpoint);

        // Assert
        fullUrl.Should().Be("https://my-functions-app.azurewebsites.net/api/signalr/migration-progress");
    }

    [Fact]
    public void SignalRConfiguration_GetFullUrl_WithoutLeadingSlashInEndpoint_ShouldAddSlash()
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://my-functions-app.azurewebsites.net"
        };
        var endpoint = "api/signalr/migration-progress";

        // Act
        var fullUrl = config.GetFullUrl(endpoint);

        // Assert
        fullUrl.Should().Be("https://my-functions-app.azurewebsites.net/api/signalr/migration-progress");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" ")]
    public void SignalRConfiguration_GetFullUrl_WithEmptyEndpoint_ShouldReturnBaseUrl(string emptyEndpoint)
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://my-functions-app.azurewebsites.net"
        };

        // Act
        var fullUrl = config.GetFullUrl(emptyEndpoint);

        // Assert
        fullUrl.Should().Be("https://my-functions-app.azurewebsites.net");
    }

    #endregion

    #region Performance and Thread Safety Tests

    [Fact]
    public async Task SignalRConfiguration_GetFullUrl_ShouldBeThreadSafe()
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://my-functions-app.azurewebsites.net"
        };
        var endpoint = "/api/signalr/migration-progress";
        var tasks = new List<Task<string>>();

        // Act - Multiple concurrent calls
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => config.GetFullUrl(endpoint)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All should return the same result
        var expectedUrl = "https://my-functions-app.azurewebsites.net/api/signalr/migration-progress";
        foreach (var result in results)
        {
            result.Should().Be(expectedUrl);
        }
    }

    [Fact]
    public async Task SignalRConfiguration_IsValid_ShouldBeThreadSafe()
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://my-functions-app.azurewebsites.net",
            TimeoutSeconds = 10,
            Enabled = true
        };
        var tasks = new List<Task<bool>>();

        // Act - Multiple concurrent calls
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => config.IsValid()));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All should return true
        foreach (var result in results)
        {
            result.Should().BeTrue();
        }
    }

    #endregion

    #region Edge Cases and Error Handling Tests

    [Fact]
    public void SignalRConfiguration_GetFullUrl_WithNullBaseUrl_ShouldHandleGracefully()
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = null! // Simulate null value
        };
        var endpoint = "/api/signalr/migration-progress";

        // Act
        var result = Record.Exception(() => config.GetFullUrl(endpoint));

        // Assert - Should not throw, should handle gracefully
        result.Should().BeNull("GetFullUrl should handle null BaseUrl gracefully");
    }

    [Fact]
    public void SignalRConfiguration_ToString_ShouldReturnReadableFormat()
    {
        // Arrange
        var config = new SignalRConfiguration
        {
            BaseUrl = "https://localhost:7071",
            TimeoutSeconds = 5,
            Enabled = true,
            EnableDebugLogging = false,
            MaxRetries = 0
        };

        // Act
        var result = config.ToString();

        // Assert
        result.Should().Contain("BaseUrl");
        result.Should().Contain("TimeoutSeconds");
        result.Should().Contain("Enabled");
        result.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Integration with Configuration System Tests

    [Fact]
    public void SignalRConfiguration_ShouldSupportConfigurationBinding()
    {
        // This test verifies that the configuration class works with ASP.NET Core configuration binding
        // Arrange
        var config = new SignalRConfiguration();

        // Act & Assert - Properties should be settable for configuration binding
        config.BaseUrl = "https://test.example.com";
        config.TimeoutSeconds = 15;
        config.Enabled = false;
        config.EnableDebugLogging = true;
        config.MaxRetries = 3;

        // Verify all properties were set correctly
        config.BaseUrl.Should().Be("https://test.example.com");
        config.TimeoutSeconds.Should().Be(15);
        config.Enabled.Should().BeFalse();
        config.EnableDebugLogging.Should().BeTrue();
        config.MaxRetries.Should().Be(3);
    }

    #endregion
} 