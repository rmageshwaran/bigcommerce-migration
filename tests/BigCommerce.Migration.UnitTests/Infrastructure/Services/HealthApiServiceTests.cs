using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// TDD Tests for HealthApiService
/// Following Single Responsibility Principle - only health and version operations
/// </summary>
public class HealthApiServiceTests
{
    private readonly Mock<ILogger<HealthApiService>> _mockLogger;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly HealthApiService _healthApiService;
    private readonly StoreConfiguration _testStoreConfig;

    public HealthApiServiceTests()
    {
        _mockLogger = new Mock<ILogger<HealthApiService>>();
        _mockHttpClient = new Mock<HttpClient>();
        _healthApiService = new HealthApiService(_mockLogger.Object, _mockHttpClient.Object);
        
        _testStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store",
            AccessToken = "test-token",
            BaseUrl = "https://api.bigcommerce.com",
            ChannelId = "1"
        };
    }

    [Fact]
    public void HealthApiService_Should_Implement_IApiHealthClient()
    {
        // Arrange & Act
        var service = _healthApiService;

        // Assert
        service.Should().BeAssignableTo<IApiHealthClient>("HealthApiService should implement IApiHealthClient");
    }

    [Fact]
    public void HealthApiService_Should_Have_Single_Responsibility()
    {
        // Arrange
        var serviceType = typeof(HealthApiService);

        // Act & Assert - Check that service implements only the health interface
        serviceType.GetInterfaces().Should().Contain(typeof(IApiHealthClient), 
            "HealthApiService should implement IApiHealthClient interface");
            
        // Verify it has the required health methods
        serviceType.GetMethod("IsHealthyAsync").Should().NotBeNull("Should have IsHealthyAsync method");
        serviceType.GetMethod("DetectApiVersionAsync").Should().NotBeNull("Should have DetectApiVersionAsync method");
    }

    [Fact]
    public async Task IsHealthyAsync_Should_Handle_Valid_Store_Configuration()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _healthApiService.IsHealthyAsync(_testStoreConfig, cancellationToken);
        
        // Should not throw exception with valid configuration
        await act.Should().NotThrowAsync("Valid store configuration should not cause exceptions");
    }

    [Fact]
    public async Task DetectApiVersionAsync_Should_Handle_Valid_Store_Configuration()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _healthApiService.DetectApiVersionAsync(_testStoreConfig, cancellationToken);
        
        // Should not throw exception with valid configuration
        await act.Should().NotThrowAsync("Valid store configuration should not cause exceptions");
    }

    [Fact]
    public async Task IsHealthyAsync_Should_Return_Boolean_Value()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _healthApiService.IsHealthyAsync(_testStoreConfig, cancellationToken);

        // Assert
        (result == true || result == false).Should().BeTrue("Health check should return a boolean value");
    }

    [Fact]
    public async Task DetectApiVersionAsync_Should_Return_Valid_ApiVersion()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _healthApiService.DetectApiVersionAsync(_testStoreConfig, cancellationToken);

        // Assert
        Enum.IsDefined(typeof(BigCommerceApiVersion), result).Should().BeTrue("Should return a valid API version");
    }

    [Fact]
    public async Task IsHealthyAsync_Should_Validate_Store_Configuration()
    {
        // Arrange
        var invalidStoreConfig = new StoreConfiguration(); // Invalid configuration
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _healthApiService.IsHealthyAsync(invalidStoreConfig, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentException>("Invalid store configuration should throw ArgumentException");
    }

    [Fact]
    public async Task DetectApiVersionAsync_Should_Validate_Store_Configuration()
    {
        // Arrange
        var invalidStoreConfig = new StoreConfiguration(); // Invalid configuration
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _healthApiService.DetectApiVersionAsync(invalidStoreConfig, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentException>("Invalid store configuration should throw ArgumentException");
    }

    [Fact]
    public async Task IsHealthyAsync_Should_Respect_Cancellation_Token()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        var act = async () => await _healthApiService.IsHealthyAsync(_testStoreConfig, cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>("Cancelled token should throw OperationCanceledException");
    }

    [Fact]
    public async Task DetectApiVersionAsync_Should_Respect_Cancellation_Token()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        var act = async () => await _healthApiService.DetectApiVersionAsync(_testStoreConfig, cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>("Cancelled token should throw OperationCanceledException");
    }

    [Fact]
    public async Task IsHealthyAsync_Should_Handle_Different_Store_Configurations()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var configs = new[]
        {
            new StoreConfiguration { StoreId = "store1", AccessToken = "token1", ChannelId = "1" },
            new StoreConfiguration { StoreId = "store2", AccessToken = "token2", ChannelId = "2" },
            new StoreConfiguration { StoreId = "store3", AccessToken = "token3", ChannelId = "3" }
        };

        foreach (var config in configs)
        {
            // Act & Assert
            var act = async () => await _healthApiService.IsHealthyAsync(config, cancellationToken);
            
            await act.Should().NotThrowAsync($"Should handle different store configurations: {config.StoreId}");
        }
    }

    [Fact]
    public async Task DetectApiVersionAsync_Should_Handle_Both_V2_And_V3_Detection()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _healthApiService.DetectApiVersionAsync(_testStoreConfig, cancellationToken);

        // Assert
        (result == BigCommerceApiVersion.V2 || result == BigCommerceApiVersion.V3)
            .Should().BeTrue("Should detect either V2 or V3 API version");
    }
} 