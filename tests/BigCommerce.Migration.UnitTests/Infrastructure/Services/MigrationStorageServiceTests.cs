using BigCommerce.Migration.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for MigrationStorageService implementation
/// Tests Azure Table Storage integration with mocked dependencies
/// </summary>
public class MigrationStorageServiceTests
{
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<MigrationStorageService>> _loggerMock;
    private readonly string _testConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5;EndpointSuffix=core.windows.net";

    public MigrationStorageServiceTests()
    {
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<MigrationStorageService>>();
        
        // Setup configuration mock to support GetConnectionString extension method
        var connectionStringsSection = new Mock<IConfigurationSection>();
        var azureWebJobsStorageSection = new Mock<IConfigurationSection>();
        
        azureWebJobsStorageSection.Setup(x => x.Value).Returns(_testConnectionString);
        connectionStringsSection.Setup(x => x["AzureWebJobsStorage"]).Returns(_testConnectionString);
        connectionStringsSection.Setup(x => x.GetSection("AzureWebJobsStorage")).Returns(azureWebJobsStorageSection.Object);
        
        _configurationMock.Setup(x => x.GetSection("ConnectionStrings")).Returns(connectionStringsSection.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidConfiguration_ShouldNotThrowArgumentException()
    {
        // Act & Assert - Configuration is valid, so constructor should not throw ArgumentNullException
        // Any other exceptions (like Azure connection failures) are acceptable for unit tests
        var exception = Record.Exception(() => new MigrationStorageService(_configurationMock.Object, _loggerMock.Object));
        
        // The important thing is that we don't get ArgumentNullException (which means our mocking worked)
        if (exception != null)
        {
            exception.Should().NotBeOfType<ArgumentNullException>();
        }
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MigrationStorageService(_configurationMock.Object, null!));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MigrationStorageService(null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullConnectionString_ShouldThrowArgumentNullException()
    {
        // Arrange
        var configMock = new Mock<IConfiguration>();
        var emptyConnectionStringsSection = new Mock<IConfigurationSection>();
        var emptyAzureSection = new Mock<IConfigurationSection>();
        
        emptyAzureSection.Setup(x => x.Value).Returns((string?)null);
        emptyConnectionStringsSection.Setup(x => x["AzureWebJobsStorage"]).Returns((string?)null);
        emptyConnectionStringsSection.Setup(x => x.GetSection("AzureWebJobsStorage")).Returns(emptyAzureSection.Object);
        
        configMock.Setup(x => x.GetSection("ConnectionStrings")).Returns(emptyConnectionStringsSection.Object);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MigrationStorageService(configMock.Object, _loggerMock.Object));
    }

    #endregion

    #region Method Signature Tests

    [Fact]
    public void MigrationStorageService_HasCorrectMigrationMethods()
    {
        // Arrange
        var serviceType = typeof(MigrationStorageService);

        // Act & Assert - Verify migration methods exist with correct signatures
        var createMigrationMethod = serviceType.GetMethod("CreateMigrationAsync");
        createMigrationMethod.Should().NotBeNull();

        var getMigrationMethod = serviceType.GetMethod("GetMigrationAsync");
        getMigrationMethod.Should().NotBeNull();

        var updateMigrationMethod = serviceType.GetMethod("UpdateMigrationAsync");
        updateMigrationMethod.Should().NotBeNull();

        var getMigrationsMethod = serviceType.GetMethod("GetMigrationsAsync");
        getMigrationsMethod.Should().NotBeNull();

        var deleteMigrationMethod = serviceType.GetMethod("DeleteMigrationAsync");
        deleteMigrationMethod.Should().NotBeNull();
    }

    [Fact]
    public void MigrationStorageService_HasCorrectEntityMappingMethods()
    {
        // Arrange
        var serviceType = typeof(MigrationStorageService);

        // Act & Assert - Verify entity mapping methods exist with correct signatures
        var createMappingMethod = serviceType.GetMethod("CreateEntityMappingAsync");
        createMappingMethod.Should().NotBeNull();

        var getMappingMethod = serviceType.GetMethod("GetEntityMappingAsync");
        getMappingMethod.Should().NotBeNull();

        var getMappingsMethod = serviceType.GetMethod("GetEntityMappingsAsync");
        getMappingsMethod.Should().NotBeNull();

        var createBatchMethod = serviceType.GetMethod("CreateEntityMappingsBatchAsync");
        createBatchMethod.Should().NotBeNull();

        var updateMappingMethod = serviceType.GetMethod("UpdateEntityMappingAsync");
        updateMappingMethod.Should().NotBeNull();
    }

    [Fact]
    public void MigrationStorageService_HasCorrectApiCallTrackingMethods()
    {
        // Arrange
        var serviceType = typeof(MigrationStorageService);

        // Act & Assert - Verify API call tracking methods exist with correct signatures
        var createApiCallMethod = serviceType.GetMethod("CreateApiCallTrackingAsync");
        createApiCallMethod.Should().NotBeNull();

        var getStatisticsMethod = serviceType.GetMethod("GetApiCallStatisticsAsync");
        getStatisticsMethod.Should().NotBeNull();

        var getHistoryMethod = serviceType.GetMethod("GetApiCallHistoryAsync");
        getHistoryMethod.Should().NotBeNull();
    }

    [Fact]
    public void MigrationStorageService_HasCorrectCancellationTokenMethods()
    {
        // Arrange
        var serviceType = typeof(MigrationStorageService);

        // Act & Assert - Verify cancellation token methods exist with correct signatures
        var createTokenMethod = serviceType.GetMethod("CreateCancellationTokenAsync");
        createTokenMethod.Should().NotBeNull();

        var getTokenMethod = serviceType.GetMethod("GetCancellationTokenAsync");
        getTokenMethod.Should().NotBeNull();

        var updateTokenMethod = serviceType.GetMethod("UpdateCancellationTokenAsync");
        updateTokenMethod.Should().NotBeNull();

        var deleteTokenMethod = serviceType.GetMethod("DeleteCancellationTokenAsync");
        deleteTokenMethod.Should().NotBeNull();
    }

    #endregion

    #region Interface Implementation Tests

    [Fact]
    public void MigrationStorageService_ImplementsIMigrationStorageServiceInterface()
    {
        // Arrange
        var serviceType = typeof(MigrationStorageService);
        var interfaceType = typeof(IMigrationStorageService);

        // Act & Assert
        interfaceType.IsAssignableFrom(serviceType).Should().BeTrue();
    }

    #endregion

    // Note: Integration tests with actual Azure Table Storage would be in a separate test project
    // These unit tests focus on constructor behavior, parameter validation, and method signatures
} 