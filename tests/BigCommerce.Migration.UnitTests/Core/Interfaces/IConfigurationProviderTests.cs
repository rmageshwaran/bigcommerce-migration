using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Core.Interfaces;

/// <summary>
/// Unit tests for IMigrationConfigurationProvider interface following TDD approach
/// Tests the abstraction layer for configuration access (DIP compliance)
/// </summary>
public class IMigrationConfigurationProviderTests
{
    [Fact]
    public void GetConnectionString_ShouldReturnConnectionString_WhenValidKeyProvided()
    {
        // Arrange
        var mockProvider = new Mock<IMigrationConfigurationProvider>();
        var expected = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;EndpointSuffix=core.windows.net";
        mockProvider.Setup(x => x.GetConnectionString("AzureWebJobsStorage")).Returns(expected);

        // Act
        var result = mockProvider.Object.GetConnectionString("AzureWebJobsStorage");

        // Assert
        Assert.Equal(expected, result);
        mockProvider.Verify(x => x.GetConnectionString("AzureWebJobsStorage"), Times.Once);
    }

    [Fact]
    public void GetBigCommerceConfiguration_ShouldReturnConfiguration_WhenValidConfigurationExists()
    {
        // Arrange
        var mockProvider = new Mock<IMigrationConfigurationProvider>();
        var expected = new BigCommerceConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com",
            RequestTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 0,
            RateLimitRequestsPerSecond = 12,
            EnableDebugLogging = false,
            UserAgent = "BigCommerce-Migration-System/1.0"
        };
        mockProvider.Setup(x => x.GetBigCommerceConfiguration()).Returns(expected);

        // Act
        var result = mockProvider.Object.GetBigCommerceConfiguration();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected.BaseUrl, result.BaseUrl);
        Assert.Equal(expected.RequestTimeout, result.RequestTimeout);
        Assert.Equal(expected.MaxRetries, result.MaxRetries);
    }

    [Fact]
    public void GetOpenSearchConfiguration_ShouldReturnConfiguration_WhenValidConfigurationExists()
    {
        // Arrange
        var mockProvider = new Mock<IMigrationConfigurationProvider>();
        var expected = new OpenSearchConfiguration
        {
            Endpoint = "https://localhost:9200",
            Username = "admin",
            Password = "password",
            DefaultIndex = "bigcommerce-migration"
        };
        mockProvider.Setup(x => x.GetOpenSearchConfiguration()).Returns(expected);

        // Act
        var result = mockProvider.Object.GetOpenSearchConfiguration();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected.Endpoint, result.Endpoint);
        Assert.Equal(expected.Username, result.Username);
    }
} 