using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Functions.Extensions;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.UnitTests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Functions;

/// <summary>
/// Unit tests for dependency injection configuration
/// Tests service registration and configuration with new request-based architecture
/// </summary>
public class DependencyInjectionTests
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    public DependencyInjectionTests()
    {
        _services = new ServiceCollection();
        
        // Setup configuration for testing - global settings only
        var configValues = new Dictionary<string, string?>
        {
            ["BigCommerce:BaseUrl"] = "https://api.bigcommerce.com",
            ["BigCommerce:RequestTimeout"] = "00:00:30",
            ["BigCommerce:MaxRetries"] = "3",
            ["BigCommerce:RateLimitRequestsPerSecond"] = "12",
            ["BigCommerce:EnableDebugLogging"] = "false",
            ["BigCommerce:UserAgent"] = "BigCommerce-Migration-System/1.0-Test",
            ["OpenSearch:Endpoint"] = "https://localhost:9200",
            ["OpenSearch:Username"] = "test-user",
            ["OpenSearch:Password"] = "test-password",
            ["OpenSearch:DefaultIndex"] = "bigcommerce-migration-test"
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
    }

    [Fact]
    public void AddBigCommerceMigrationServices_WithValidConfiguration_ShouldRegisterAllServices()
    {
        // Act
        _services.AddBigCommerceMigrationServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert - Core services
        serviceProvider.GetService<IBigCommerceApiClient>().Should().NotBeNull();
        serviceProvider.GetService<ICategoryTreeResolver>().Should().NotBeNull();
        serviceProvider.GetService<IOpenSearchService>().Should().NotBeNull();

        // Assert - Configuration
        serviceProvider.GetService<IOptions<BigCommerceConfiguration>>().Should().NotBeNull();
        serviceProvider.GetService<IOptions<OpenSearchConfiguration>>().Should().NotBeNull();

        // Assert - HTTP client
        serviceProvider.GetService<HttpClient>().Should().NotBeNull();

        // Assert - Logging
        serviceProvider.GetService<ILogger<BigCommerceApiClient>>().Should().NotBeNull();
        serviceProvider.GetService<ILogger<CategoryTreeResolver>>().Should().NotBeNull();
        serviceProvider.GetService<ILogger<OpenSearchService>>().Should().NotBeNull();
    }

    [Fact]
    public void AddBigCommerceMigrationServices_ShouldRegisterBigCommerceConfigurationCorrectly()
    {
        // Act
        _services.AddBigCommerceMigrationServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var configOptions = serviceProvider.GetRequiredService<IOptions<BigCommerceConfiguration>>();
        var config = configOptions.Value;

        config.Should().NotBeNull();
        config.BaseUrl.Should().Be("https://api.bigcommerce.com");
        config.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
        config.MaxRetries.Should().Be(3);
        config.RateLimitRequestsPerSecond.Should().Be(12);
        config.EnableDebugLogging.Should().BeFalse();
        config.UserAgent.Should().Be("BigCommerce-Migration-System/1.0-Test");
    }

    [Fact]
    public void AddBigCommerceMigrationServices_ShouldRegisterOpenSearchConfigurationCorrectly()
    {
        // Act
        _services.AddBigCommerceMigrationServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var configOptions = serviceProvider.GetRequiredService<IOptions<OpenSearchConfiguration>>();
        var config = configOptions.Value;

        config.Should().NotBeNull();
        config.Endpoint.Should().Be("https://localhost:9200");
        config.Username.Should().Be("test-user");
        config.Password.Should().Be("test-password");
        config.DefaultIndex.Should().Be("bigcommerce-migration-test");
    }

    [Fact]
    public void AddBigCommerceMigrationServices_ShouldRegisterServicesAsSingleton()
    {
        // Act
        _services.AddBigCommerceMigrationServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert - Multiple requests should return same instance
        var apiClient1 = serviceProvider.GetService<IBigCommerceApiClient>();
        var apiClient2 = serviceProvider.GetService<IBigCommerceApiClient>();
        apiClient1.Should().BeSameAs(apiClient2);

        var categoryResolver1 = serviceProvider.GetService<ICategoryTreeResolver>();
        var categoryResolver2 = serviceProvider.GetService<ICategoryTreeResolver>();
        categoryResolver1.Should().BeSameAs(categoryResolver2);

        var openSearchService1 = serviceProvider.GetService<IOpenSearchService>();
        var openSearchService2 = serviceProvider.GetService<IOpenSearchService>();
        openSearchService1.Should().BeSameAs(openSearchService2);
    }

    [Fact]
    public void AddBigCommerceMigrationServices_ShouldRegisterCorrectImplementations()
    {
        // Act
        _services.AddBigCommerceMigrationServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var apiClient = serviceProvider.GetService<IBigCommerceApiClient>();
        apiClient.Should().BeOfType<BigCommerceApiClient>();

        var categoryResolver = serviceProvider.GetService<ICategoryTreeResolver>();
        categoryResolver.Should().BeOfType<CategoryTreeResolver>();

        var openSearchService = serviceProvider.GetService<IOpenSearchService>();
        openSearchService.Should().BeOfType<OpenSearchService>();
    }

    [Fact]
    public void AddBigCommerceMigrationServices_WithMissingConfiguration_ShouldUseDefaults()
    {
        // Arrange - Minimal configuration
        var minimalConfigValues = new Dictionary<string, string?>
        {
            ["BigCommerce:BaseUrl"] = "https://api.bigcommerce.com"
        };

        var minimalConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(minimalConfigValues)
            .Build();

        // Act
        _services.AddBigCommerceMigrationServices(minimalConfig);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var configOptions = serviceProvider.GetRequiredService<IOptions<BigCommerceConfiguration>>();
        var config = configOptions.Value;

        config.Should().NotBeNull();
        config.BaseUrl.Should().Be("https://api.bigcommerce.com");
        // Should use default values for missing properties
        config.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
        config.MaxRetries.Should().Be(3);
        config.RateLimitRequestsPerSecond.Should().Be(12);
        config.EnableDebugLogging.Should().BeFalse();
        config.UserAgent.Should().Be("BigCommerce-Migration-System/1.0");
    }

    [Fact]
    public void AddBigCommerceMigrationServices_ShouldConfigureHttpClientCorrectly()
    {
        // Act
        _services.AddBigCommerceMigrationServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var httpClient = serviceProvider.GetService<HttpClient>();
        httpClient.Should().NotBeNull();
        httpClient!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void BigCommerceConfiguration_WithValidGlobalSettings_ShouldBeValid()
    {
        // Arrange
        var config = TestDataFactory.CreateBigCommerceConfiguration();

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void BigCommerceConfiguration_WithInvalidBaseUrl_ShouldNotBeValid()
    {
        // Arrange
        var config = TestDataFactory.CreateBigCommerceConfiguration(
            baseUrl: "invalid-url"
        );

        // Act
        var isValid = config.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void OpenSearchConfiguration_ShouldBeConfiguredCorrectly()
    {
        // Act
        _services.AddBigCommerceMigrationServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var openSearchConfigOptions = serviceProvider.GetRequiredService<IOptions<OpenSearchConfiguration>>();
        var openSearchConfig = openSearchConfigOptions.Value;

        openSearchConfig.Should().NotBeNull();
        openSearchConfig.Endpoint.Should().Be("https://localhost:9200");
        openSearchConfig.Username.Should().Be("test-user");
        openSearchConfig.Password.Should().Be("test-password");
        openSearchConfig.DefaultIndex.Should().Be("bigcommerce-migration-test");
    }

    [Fact]
    public void ServiceRegistration_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _services.AddBigCommerceMigrationServices(null!));
    }

    [Fact]
    public void ServiceProvider_ShouldResolveDependenciesCorrectly()
    {
        // Act
        _services.AddBigCommerceMigrationServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert - Should not throw when resolving complex dependencies
        var apiClient = serviceProvider.GetRequiredService<IBigCommerceApiClient>();
        apiClient.Should().NotBeNull();

        var categoryResolver = serviceProvider.GetRequiredService<ICategoryTreeResolver>();
        categoryResolver.Should().NotBeNull();

        var openSearchService = serviceProvider.GetRequiredService<IOpenSearchService>();
        openSearchService.Should().NotBeNull();
    }

    [Fact]
    public void Configuration_ShouldSupportMultipleEnvironments()
    {
        // Arrange - Development configuration
        var devConfigValues = new Dictionary<string, string?>
        {
            ["BigCommerce:BaseUrl"] = "https://api.bigcommerce.com",
            ["BigCommerce:EnableDebugLogging"] = "true",
            ["BigCommerce:UserAgent"] = "BigCommerce-Migration-System/1.0-Dev"
        };

        var devConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(devConfigValues)
            .Build();

        // Act
        _services.AddBigCommerceMigrationServices(devConfig);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var configOptions = serviceProvider.GetRequiredService<IOptions<BigCommerceConfiguration>>();
        var config = configOptions.Value;

        config.EnableDebugLogging.Should().BeTrue();
        config.UserAgent.Should().Be("BigCommerce-Migration-System/1.0-Dev");
    }

    [Fact]
    public void DependencyInjection_ShouldHandleComplexDependencyChains()
    {
        // Act
        _services.AddBigCommerceMigrationServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert - CategoryTreeResolver depends on IBigCommerceApiClient, ILogger, and IOpenSearchService
        var categoryResolver = serviceProvider.GetRequiredService<ICategoryTreeResolver>();
        categoryResolver.Should().NotBeNull();
        categoryResolver.Should().BeOfType<CategoryTreeResolver>();
    }
} 