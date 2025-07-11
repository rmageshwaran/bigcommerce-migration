using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for OpenSearch configuration models
/// Tests configuration validation and connection settings
/// </summary>
public class OpenSearchConfigurationTests
{
    [Fact]
    public void OpenSearchConfiguration_WithValidSettings_ShouldCreateSuccessfully()
    {
        // Arrange
        var endpoint = "https://search-domain.us-east-1.es.amazonaws.com";
        var username = "admin";
        var password = "SecurePassword123!";
        var defaultIndex = "bigcommerce-migration-logs";

        // Act
        var config = new OpenSearchConfiguration
        {
            Endpoint = endpoint,
            Username = username,
            Password = password,
            DefaultIndex = defaultIndex
        };

        // Assert
        config.Endpoint.Should().Be(endpoint);
        config.Username.Should().Be(username);
        config.Password.Should().Be(password);
        config.DefaultIndex.Should().Be(defaultIndex);
    }

    [Fact]
    public void OpenSearchConfiguration_WithConnectionTimeout_ShouldSetTimeout()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(30);
        
        // Act
        var config = new OpenSearchConfiguration
        {
            Endpoint = "https://localhost:9200",
            ConnectionTimeout = timeout
        };

        // Assert
        config.ConnectionTimeout.Should().Be(timeout);
    }

    [Fact]
    public void OpenSearchConfiguration_WithDefaultValues_ShouldHaveReasonableDefaults()
    {
        // Act
        var config = new OpenSearchConfiguration();

        // Assert
        config.ConnectionTimeout.Should().Be(TimeSpan.FromSeconds(30));
        config.RequestTimeout.Should().Be(TimeSpan.FromSeconds(60));
        config.MaxRetries.Should().Be(3);
        config.EnableDebugMode.Should().BeFalse();
        config.DefaultIndex.Should().Be("bigcommerce-migration");
    }

    [Theory]
    [InlineData("https://localhost:9200", true)]
    [InlineData("http://localhost:9200", true)]
    [InlineData("https://search-domain.us-east-1.es.amazonaws.com", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not-a-url", false)]
    public void OpenSearchConfiguration_IsValidEndpoint_ShouldValidateCorrectly(string endpoint, bool expectedValid)
    {
        // Arrange
        var config = new OpenSearchConfiguration
        {
            Endpoint = endpoint
        };

        // Act
        var isValid = config.IsValidEndpoint();

        // Assert
        isValid.Should().Be(expectedValid);
    }

    [Fact]
    public void OpenSearchConfiguration_WithCredentials_ShouldHaveCredentials()
    {
        // Arrange
        var config = new OpenSearchConfiguration
        {
            Username = "admin",
            Password = "password123"
        };

        // Act
        var hasCredentials = config.HasCredentials();

        // Assert
        hasCredentials.Should().BeTrue();
    }

    [Fact]
    public void OpenSearchConfiguration_WithoutCredentials_ShouldNotHaveCredentials()
    {
        // Arrange
        var config = new OpenSearchConfiguration
        {
            Username = null,
            Password = null
        };

        // Act
        var hasCredentials = config.HasCredentials();

        // Assert
        hasCredentials.Should().BeFalse();
    }

    [Fact]
    public void OpenSearchConfiguration_WithPartialCredentials_ShouldNotHaveCredentials()
    {
        // Arrange
        var config = new OpenSearchConfiguration
        {
            Username = "admin",
            Password = null
        };

        // Act
        var hasCredentials = config.HasCredentials();

        // Assert
        hasCredentials.Should().BeFalse();
    }

    [Fact]
    public void OpenSearchConfiguration_GetConnectionString_ShouldReturnFormattedString()
    {
        // Arrange
        var config = new OpenSearchConfiguration
        {
            Endpoint = "https://localhost:9200",
            Username = "admin",
            Password = "password123"
        };

        // Act
        var connectionString = config.GetConnectionString();

        // Assert
        connectionString.Should().Contain("https://localhost:9200");
        connectionString.Should().Contain("admin");
        // Password should be masked in connection string for security
        connectionString.Should().NotContain("password123");
        connectionString.Should().Contain("***");
    }
} 