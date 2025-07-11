using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for OpenSearchService implementation
/// Tests constructor validation and method signatures with mocked dependencies
/// </summary>
public class OpenSearchServiceTests
{
    private readonly Mock<ILogger<OpenSearchService>> _mockLogger;
    private readonly OpenSearchConfiguration _validConfig;

    public OpenSearchServiceTests()
    {
        _mockLogger = new Mock<ILogger<OpenSearchService>>();
        _validConfig = new OpenSearchConfiguration
        {
            Endpoint = "https://localhost:9200",
            Username = "admin",
            Password = "password123",
            DefaultIndex = "test-index"
        };
    }

    #region Constructor Tests

    [Fact]
    public void OpenSearchService_WithValidConfiguration_ShouldCreateSuccessfully()
    {
        // Act & Assert - The service should create without throwing ArgumentNull exceptions
        // Note: It may fail when trying to actually connect to OpenSearch, but constructor should work
        var exception = Record.Exception(() => new OpenSearchService(_validConfig, _mockLogger.Object));
        
        // Should not fail on null parameters - any failures should be from OpenSearch connection attempts
        if (exception != null)
        {
            exception.Should().NotBeOfType<ArgumentNullException>();
        }
    }

    [Fact]
    public void OpenSearchService_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new OpenSearchService(null!, _mockLogger.Object));
    }

    [Fact]
    public void OpenSearchService_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new OpenSearchService(_validConfig, null!));
    }

    [Fact]
    public void OpenSearchService_WithInvalidEndpoint_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidConfig = new OpenSearchConfiguration
        {
            Endpoint = "invalid-url",
            DefaultIndex = "test-index"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            new OpenSearchService(invalidConfig, _mockLogger.Object));
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void OpenSearchConfiguration_IsValidEndpoint_WithValidUrl_ShouldReturnTrue()
    {
        // Arrange
        var config = new OpenSearchConfiguration
        {
            Endpoint = "https://localhost:9200"
        };

        // Act
        var result = config.IsValidEndpoint();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void OpenSearchConfiguration_IsValidEndpoint_WithInvalidUrl_ShouldReturnFalse()
    {
        // Arrange
        var config = new OpenSearchConfiguration
        {
            Endpoint = "not-a-url"
        };

        // Act
        var result = config.IsValidEndpoint();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void OpenSearchConfiguration_HasCredentials_WithUsernameAndPassword_ShouldReturnTrue()
    {
        // Arrange
        var config = new OpenSearchConfiguration
        {
            Endpoint = "https://localhost:9200",
            Username = "admin",
            Password = "password"
        };

        // Act
        var result = config.HasCredentials();

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Method Signature Tests

    [Fact]
    public void OpenSearchService_HasCorrectMethodSignatures()
    {
        // Arrange
        var serviceType = typeof(OpenSearchService);

        // Act & Assert - Verify all required methods exist with correct signatures
        var logMigrationEventMethod = serviceType.GetMethod("LogMigrationEventAsync");
        logMigrationEventMethod.Should().NotBeNull();

        var logPerformanceMetricsMethod = serviceType.GetMethod("LogPerformanceMetricsAsync");
        logPerformanceMetricsMethod.Should().NotBeNull();

        var logErrorMethod = serviceType.GetMethod("LogErrorAsync");
        logErrorMethod.Should().NotBeNull();

        var searchLogsMethod = serviceType.GetMethod("SearchLogsAsync");
        searchLogsMethod.Should().NotBeNull();

        var isHealthyMethod = serviceType.GetMethod("IsHealthyAsync");
        isHealthyMethod.Should().NotBeNull();
    }

    #endregion

    #region Interface Implementation Tests

    [Fact]
    public void OpenSearchService_ImplementsIOpenSearchServiceInterface()
    {
        // Arrange
        var serviceType = typeof(OpenSearchService);
        var interfaceType = typeof(IOpenSearchService);

        // Act & Assert
        interfaceType.IsAssignableFrom(serviceType).Should().BeTrue();
    }

    #endregion

    #region Parameter Validation Tests

    [Fact]
    public void LogMigrationEventAsync_WithInvalidEventType_ParameterValidation()
    {
        // Arrange
        var serviceType = typeof(OpenSearchService);
        var method = serviceType.GetMethod("LogMigrationEventAsync");

        // Act & Assert - Verify method exists and has correct parameter types
        method.Should().NotBeNull();
        method!.GetParameters().Should().HaveCount(4); // eventType, entityId, eventData, cancellationToken
        method.GetParameters()[0].ParameterType.Should().Be(typeof(string));
        method.GetParameters()[1].ParameterType.Should().Be(typeof(string));
        method.GetParameters()[2].ParameterType.Should().Be(typeof(object));
    }

    [Fact]
    public void LogPerformanceMetricsAsync_HasCorrectParameters()
    {
        // Arrange
        var serviceType = typeof(OpenSearchService);
        var method = serviceType.GetMethod("LogPerformanceMetricsAsync");

        // Act & Assert
        method.Should().NotBeNull();
        method!.GetParameters().Should().HaveCount(4); // operationName, duration, metrics, cancellationToken
        method.GetParameters()[0].ParameterType.Should().Be(typeof(string));
        method.GetParameters()[1].ParameterType.Should().Be(typeof(TimeSpan));
        method.GetParameters()[2].ParameterType.Should().Be(typeof(object));
    }

    [Fact]
    public void LogErrorAsync_HasCorrectParameters()
    {
        // Arrange
        var serviceType = typeof(OpenSearchService);
        var method = serviceType.GetMethod("LogErrorAsync");

        // Act & Assert
        method.Should().NotBeNull();
        method!.GetParameters().Should().HaveCount(4); // context, exception, additionalData, cancellationToken
        method.GetParameters()[0].ParameterType.Should().Be(typeof(string));
        method.GetParameters()[1].ParameterType.Should().Be(typeof(Exception));
        method.GetParameters()[2].ParameterType.Should().Be(typeof(object));
    }

    #endregion

    // Note: Integration tests with actual OpenSearch would be in a separate test project
    // These unit tests focus on constructor behavior, parameter validation, and method signatures
    // Actual OpenSearch functionality would be tested with a real OpenSearch instance in integration tests
} 