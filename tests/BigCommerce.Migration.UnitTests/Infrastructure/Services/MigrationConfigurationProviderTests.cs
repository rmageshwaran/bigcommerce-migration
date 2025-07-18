using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for concrete MigrationConfigurationProvider implementation
/// Tests DIP compliance and configuration abstraction functionality
/// </summary>
public class MigrationConfigurationProviderTests
{
    private readonly Mock<ILogger<MigrationConfigurationProvider>> _mockLogger;

    public MigrationConfigurationProviderTests()
    {
        _mockLogger = new Mock<ILogger<MigrationConfigurationProvider>>();
    }

    [Fact]
    public void GetConnectionString_ShouldReturnConnectionString_WhenExists()
    {
        // Arrange
        var configuration = CreateTestConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AzureWebJobsStorage"] = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key"
        });
        var provider = new MigrationConfigurationProvider(configuration, _mockLogger.Object);

        // Act
        var result = provider.GetConnectionString("AzureWebJobsStorage");

        // Assert
        Assert.Equal("DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key", result);
    }

    [Fact]
    public void GetConnectionString_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var configuration = CreateTestConfiguration(new Dictionary<string, string?>());
        var provider = new MigrationConfigurationProvider(configuration, _mockLogger.Object);

        // Act
        var result = provider.GetConnectionString("NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetBigCommerceConfiguration_ShouldReturnConfiguration_WhenSectionExists()
    {
        // Arrange
        var configuration = CreateTestConfiguration(new Dictionary<string, string?>
        {
            ["BigCommerce:BaseUrl"] = "https://api.bigcommerce.com",
            ["BigCommerce:RequestTimeout"] = "00:00:30",
            ["BigCommerce:MaxRetries"] = "0",
            ["BigCommerce:RateLimitRequestsPerSecond"] = "12",
            ["BigCommerce:EnableDebugLogging"] = "false",
            ["BigCommerce:UserAgent"] = "BigCommerce-Migration-System/1.0"
        });
        var provider = new MigrationConfigurationProvider(configuration, _mockLogger.Object);

        // Act
        var result = provider.GetBigCommerceConfiguration();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("https://api.bigcommerce.com", result.BaseUrl);
        Assert.Equal(TimeSpan.FromSeconds(30), result.RequestTimeout);
        Assert.Equal(0, result.MaxRetries);
        Assert.Equal(12, result.RateLimitRequestsPerSecond);
        Assert.False(result.EnableDebugLogging);
        Assert.Equal("BigCommerce-Migration-System/1.0", result.UserAgent);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConfigurationIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MigrationConfigurationProvider(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Arrange
        var configuration = CreateTestConfiguration(new Dictionary<string, string?>());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MigrationConfigurationProvider(configuration, null!));
    }

    private static IConfiguration CreateTestConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
} 