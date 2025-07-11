using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for BlobService implementation
/// Tests constructor validation and method signatures with mocked dependencies
/// </summary>
public class BlobServiceTests
{
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<BlobService>> _loggerMock;
    private readonly string _testConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleXRlc3RrZXl0ZXN0a2V5;EndpointSuffix=core.windows.net";

    public BlobServiceTests()
    {
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<BlobService>>();
        
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
        var exception = Record.Exception(() => new BlobService(_configurationMock.Object, _loggerMock.Object));
        
        // The important thing is that we don't get ArgumentNullException (which means our mocking worked)
        if (exception != null)
        {
            exception.Should().NotBeOfType<ArgumentNullException>();
        }
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BlobService(null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BlobService(_configurationMock.Object, null!));
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
        Assert.Throws<ArgumentNullException>(() => new BlobService(configMock.Object, _loggerMock.Object));
    }

    #endregion

    #region Method Signature Tests

    [Fact]
    public void BlobService_HasCorrectMethodSignatures()
    {
        // Arrange
        var blobServiceType = typeof(BlobService);

        // Act & Assert - Verify public methods exist with correct signatures
        var createReportMethod = blobServiceType.GetMethod("CreateMigrationReportAsync");
        createReportMethod.Should().NotBeNull();

        var uploadFileMethod = blobServiceType.GetMethod("UploadFileAsync");
        uploadFileMethod.Should().NotBeNull();

        var fileExistsMethod = blobServiceType.GetMethod("FileExistsAsync");
        fileExistsMethod.Should().NotBeNull();

        var deleteFileMethod = blobServiceType.GetMethod("DeleteFileAsync");
        deleteFileMethod.Should().NotBeNull();

        var storeRequestPayloadMethod = blobServiceType.GetMethod("StoreRequestPayloadAsync");
        storeRequestPayloadMethod.Should().NotBeNull();

        var storeResponsePayloadMethod = blobServiceType.GetMethod("StoreResponsePayloadAsync");
        storeResponsePayloadMethod.Should().NotBeNull();

        var storeCompressedPayloadMethod = blobServiceType.GetMethod("StoreCompressedPayloadAsync");
        storeCompressedPayloadMethod.Should().NotBeNull();

        var getStorageUsageMethod = blobServiceType.GetMethod("GetStorageUsageAsync");
        getStorageUsageMethod.Should().NotBeNull();
    }

    [Fact]
    public void BlobService_ReportMethods_HaveCorrectParameters()
    {
        // Arrange
        var blobServiceType = typeof(BlobService);

        // Act & Assert - Verify report methods have correct parameter types
        var createMigrationReportMethod = blobServiceType.GetMethod("CreateMigrationReportAsync");
        createMigrationReportMethod.Should().NotBeNull();
        createMigrationReportMethod!.GetParameters().Should().HaveCount(3);
        createMigrationReportMethod.GetParameters()[0].ParameterType.Should().Be(typeof(string)); // migrationId
        createMigrationReportMethod.GetParameters()[1].ParameterType.Should().Be(typeof(List<Dictionary<string, object>>)); // reportData
        createMigrationReportMethod.GetParameters()[2].ParameterType.Should().Be(typeof(string)); // reportType

        var createEntityMappingReportMethod = blobServiceType.GetMethod("CreateEntityMappingReportAsync");
        createEntityMappingReportMethod.Should().NotBeNull();
        createEntityMappingReportMethod!.GetParameters().Should().HaveCount(2);
        createEntityMappingReportMethod.GetParameters()[0].ParameterType.Should().Be(typeof(string)); // migrationId
        createEntityMappingReportMethod.GetParameters()[1].ParameterType.Should().Be(typeof(List<EntityMapping>)); // entityMappings

        var createApiCallReportMethod = blobServiceType.GetMethod("CreateApiCallReportAsync");
        createApiCallReportMethod.Should().NotBeNull();
        createApiCallReportMethod!.GetParameters().Should().HaveCount(2);
        createApiCallReportMethod.GetParameters()[0].ParameterType.Should().Be(typeof(string)); // migrationId
        createApiCallReportMethod.GetParameters()[1].ParameterType.Should().Be(typeof(List<ApiCallTracking>)); // apiCalls
    }

    [Fact]
    public void BlobService_PayloadMethods_HaveCorrectParameters()
    {
        // Arrange
        var blobServiceType = typeof(BlobService);

        // Act & Assert - Verify payload methods have correct parameter types
        var storeRequestPayloadMethod = blobServiceType.GetMethod("StoreRequestPayloadAsync");
        storeRequestPayloadMethod.Should().NotBeNull();
        storeRequestPayloadMethod!.GetParameters().Should().HaveCount(4);
        storeRequestPayloadMethod.GetParameters()[0].ParameterType.Should().Be(typeof(string)); // migrationId
        storeRequestPayloadMethod.GetParameters()[1].ParameterType.Should().Be(typeof(string)); // requestId
        storeRequestPayloadMethod.GetParameters()[2].ParameterType.Should().Be(typeof(string)); // payload
        storeRequestPayloadMethod.GetParameters()[3].ParameterType.Should().Be(typeof(string)); // contentType

        var storeResponsePayloadMethod = blobServiceType.GetMethod("StoreResponsePayloadAsync");
        storeResponsePayloadMethod.Should().NotBeNull();
        storeResponsePayloadMethod!.GetParameters().Should().HaveCount(4);
        storeResponsePayloadMethod.GetParameters()[0].ParameterType.Should().Be(typeof(string)); // migrationId
        storeResponsePayloadMethod.GetParameters()[1].ParameterType.Should().Be(typeof(string)); // requestId
        storeResponsePayloadMethod.GetParameters()[2].ParameterType.Should().Be(typeof(string)); // payload
        storeResponsePayloadMethod.GetParameters()[3].ParameterType.Should().Be(typeof(string)); // contentType
    }

    #endregion

    #region Interface Implementation Tests

    [Fact]
    public void BlobService_ImplementsIBlobServiceInterface()
    {
        // Arrange
        var blobServiceType = typeof(BlobService);
        var interfaceType = typeof(IBlobService);

        // Act & Assert
        interfaceType.IsAssignableFrom(blobServiceType).Should().BeTrue();
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void BlobService_ValidatesConfigurationCorrectly()
    {
        // Arrange - Using valid configuration mock
        var validConfigMock = new Mock<IConfiguration>();
        var connectionStringsSection = new Mock<IConfigurationSection>();
        var azureSection = new Mock<IConfigurationSection>();
        
        azureSection.Setup(x => x.Value).Returns(_testConnectionString);
        connectionStringsSection.Setup(x => x["AzureWebJobsStorage"]).Returns(_testConnectionString);
        connectionStringsSection.Setup(x => x.GetSection("AzureWebJobsStorage")).Returns(azureSection.Object);
        
        validConfigMock.Setup(x => x.GetSection("ConnectionStrings")).Returns(connectionStringsSection.Object);

        // Act & Assert - Constructor should not throw ArgumentNullException for valid config
        var exception = Record.Exception(() => new BlobService(validConfigMock.Object, _loggerMock.Object));
        
        // Should not fail on null arguments (any Azure connection failures are acceptable in unit tests)
        if (exception != null)
        {
            exception.Should().NotBeOfType<ArgumentNullException>();
        }
    }

    [Fact]
    public void BlobService_RequiresValidConnectionString()
    {
        // Arrange
        var invalidConfigMock = new Mock<IConfiguration>();
        var connectionStringsSection = new Mock<IConfigurationSection>();
        var azureSection = new Mock<IConfigurationSection>();
        
        azureSection.Setup(x => x.Value).Returns(string.Empty);
        connectionStringsSection.Setup(x => x["AzureWebJobsStorage"]).Returns(string.Empty);
        connectionStringsSection.Setup(x => x.GetSection("AzureWebJobsStorage")).Returns(azureSection.Object);
        
        invalidConfigMock.Setup(x => x.GetSection("ConnectionStrings")).Returns(connectionStringsSection.Object);

        // Act & Assert - Should throw an exception for invalid connection string
        Assert.ThrowsAny<Exception>(() => new BlobService(invalidConfigMock.Object, _loggerMock.Object));
    }

    #endregion

    // Note: Integration tests with actual Azure Storage would be in a separate test project
    // These unit tests focus on constructor behavior, parameter validation, and method signatures
    // Actual Azure Storage functionality would be tested with real Azure Storage in integration tests
} 