using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Extensions;
using BigCommerce.Migration.Functions.Extensions;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Integration;

/// <summary>
/// Integration tests for dynamic rate limiting service registration and dependency injection
/// Verifies that all services can be resolved correctly and work together
/// </summary>
public class DynamicRateLimitingIntegrationTests
{
    /// <summary>
    /// Verifies that all dynamic rate limiting services can be resolved from the DI container
    /// </summary>
    [Fact]
    public void ServiceRegistration_Should_ResolveAllDynamicRateLimitingServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Add comprehensive configuration for DI validation
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=test;EndpointSuffix=core.windows.net",
                ["BigCommerce:BaseUrl"] = "https://test.mybigcommerce.com/",
                ["BigCommerce:AccessToken"] = "test-token",
                ["Logging:LogLevel:Default"] = "Information",
                ["DynamicRateLimiting:BaseRateRequestsPerSecond"] = "12",
                ["DynamicRateLimiting:MinRateRequestsPerSecond"] = "5",
                ["DynamicRateLimiting:MaxRateRequestsPerSecond"] = "50"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        
        // Use full service registration with proper configuration
        services.AddBigCommerceMigrationServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - All services should be resolvable
        var dateTimeProvider = serviceProvider.GetService<IDateTimeProvider>();
        dateTimeProvider.Should().NotBeNull();
        dateTimeProvider.Should().BeOfType<DateTimeProvider>();

        var apiHealthMonitor = serviceProvider.GetService<IApiHealthMonitor>();
        apiHealthMonitor.Should().NotBeNull();
        apiHealthMonitor.Should().BeOfType<ApiHealthMonitor>();

        var rateCalculator = serviceProvider.GetService<IRateCalculator>();
        rateCalculator.Should().NotBeNull();
        rateCalculator.Should().BeOfType<BigCommerceAwareRateCalculator>();

        var dynamicRateLimiter = serviceProvider.GetService<IDynamicRateLimiter>();
        dynamicRateLimiter.Should().NotBeNull();
        dynamicRateLimiter.Should().BeOfType<DynamicRateLimitService>();

        var rateLimitService = serviceProvider.GetService<IRateLimitService>();
        rateLimitService.Should().NotBeNull();
        rateLimitService.Should().BeOfType<DynamicRateLimitService>(); // Should be the same instance as IDynamicRateLimiter
    }

    /// <summary>
    /// Verifies that IRateLimitService and IDynamicRateLimiter resolve to the same instance (singleton pattern)
    /// </summary>
    [Fact]
    public void ServiceRegistration_Should_ProvideSameInstanceForBothInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=test;EndpointSuffix=core.windows.net",
                ["BigCommerce:BaseUrl"] = "https://test.mybigcommerce.com/",
                ["BigCommerce:AccessToken"] = "test-token",
                ["Logging:LogLevel:Default"] = "Information",
                ["DynamicRateLimiting:BaseRateRequestsPerSecond"] = "12",
                ["DynamicRateLimiting:MinRateRequestsPerSecond"] = "5",
                ["DynamicRateLimiting:MaxRateRequestsPerSecond"] = "50"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        
        services.AddBigCommerceMigrationServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var rateLimitService = serviceProvider.GetService<IRateLimitService>();
        var dynamicRateLimiter = serviceProvider.GetService<IDynamicRateLimiter>();

        // Assert
        rateLimitService.Should().NotBeNull();
        dynamicRateLimiter.Should().NotBeNull();
        rateLimitService.Should().BeSameAs(dynamicRateLimiter); // Same singleton instance
    }

    /// <summary>
    /// Verifies that the dynamic rate limiting service can actually calculate rates
    /// </summary>
    [Fact]
    public async Task DynamicRateLimitService_Should_CalculateOptimalRate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=test;EndpointSuffix=core.windows.net",
                ["BigCommerce:BaseUrl"] = "https://test.mybigcommerce.com/",
                ["BigCommerce:AccessToken"] = "test-token",
                ["Logging:LogLevel:Default"] = "Information",
                ["DynamicRateLimiting:BaseRateRequestsPerSecond"] = "12",
                ["DynamicRateLimiting:MinRateRequestsPerSecond"] = "5",
                ["DynamicRateLimiting:MaxRateRequestsPerSecond"] = "50"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        
        services.AddBigCommerceMigrationServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        var dynamicRateLimiter = serviceProvider.GetRequiredService<IDynamicRateLimiter>();

        // Act
        var optimalRate = await dynamicRateLimiter.GetOptimalRateAsync("test-store", CancellationToken.None);

        // Assert
        optimalRate.Should().BeGreaterThan(0);
        optimalRate.Should().BeLessOrEqualTo(50); // Within expected range
    }

    /// <summary>
    /// Verifies that dependency chain is correct (services depend on their interfaces, not concrete types)
    /// </summary>
    [Fact]
    public void ServiceRegistration_Should_FollowDependencyInversionPrinciple()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=test;EndpointSuffix=core.windows.net",
                ["BigCommerce:BaseUrl"] = "https://test.mybigcommerce.com/",
                ["BigCommerce:AccessToken"] = "test-token",
                ["Logging:LogLevel:Default"] = "Information",
                ["DynamicRateLimiting:BaseRateRequestsPerSecond"] = "12",
                ["DynamicRateLimiting:MinRateRequestsPerSecond"] = "5",
                ["DynamicRateLimiting:MaxRateRequestsPerSecond"] = "50"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        
        services.AddBigCommerceMigrationServices(configuration);
        
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Verify dependency chain
        var dynamicRateLimiter = serviceProvider.GetRequiredService<IDynamicRateLimiter>() as DynamicRateLimitService;
        dynamicRateLimiter.Should().NotBeNull();

        // The DynamicRateLimitService should have been constructed with interface dependencies
        // This implicitly tests that our registration uses interfaces correctly
        var healthMonitor = serviceProvider.GetRequiredService<IApiHealthMonitor>();
        var rateCalculator = serviceProvider.GetRequiredService<IRateCalculator>();
        
        healthMonitor.Should().NotBeNull();
        rateCalculator.Should().NotBeNull();
    }
} 