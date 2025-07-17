using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Extensions;

namespace BigCommerce.Migration.UnitTests.Infrastructure;

/// <summary>
/// Tests for segregated interface registration in dependency injection
/// Ensures Interface Segregation Principle is properly implemented in DI container
/// Tests define expected behavior for Task 1.3.1 (TDD approach)
/// </summary>
public class SegregatedInterfaceRegistrationTests
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    public SegregatedInterfaceRegistrationTests()
    {
        _services = new ServiceCollection();
        
        // Setup minimal configuration for DI container
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BigCommerce:BaseUrl"] = "https://api.bigcommerce.com",
            ["BigCommerce:RequestTimeoutSeconds"] = "30",
            ["BigCommerce:MaxRetries"] = "0",
            ["BigCommerce:RateLimitRequestsPerSecond"] = "12",
            ["BigCommerce:EnableDebugLogging"] = "false",
            ["BigCommerce:UserAgent"] = "Test-Agent",
            ["OpenSearch:Endpoint"] = "https://localhost:9200",
            ["OpenSearch:DefaultIndex"] = "test-index",
            ["OpenSearch:ConnectionTimeoutSeconds"] = "30",
            ["OpenSearch:RequestTimeoutSeconds"] = "60",
            ["OpenSearch:MaxRetries"] = "3",
            ["OpenSearch:EnableDebugMode"] = "false"
        });
        _configuration = configBuilder.Build();
    }

    [Fact]
    public void AddOrchestrationServices_ShouldRegisterICategoryApiClient()
    {
        // Arrange & Act
        _services.AddOrchestrationServices();

        // Assert
        var serviceProvider = _services.BuildServiceProvider();
        var categoryApiClient = serviceProvider.GetService<ICategoryApiClient>();
        
        Assert.NotNull(categoryApiClient);
        Assert.IsType<CategoryApiService>(categoryApiClient);
    }

    [Fact]
    public void AddOrchestrationServices_ShouldRegisterIProductApiClient()
    {
        // Arrange & Act
        _services.AddOrchestrationServices();

        // Assert
        var serviceProvider = _services.BuildServiceProvider();
        var productApiClient = serviceProvider.GetService<IProductApiClient>();
        
        Assert.NotNull(productApiClient);
        Assert.IsType<ProductApiService>(productApiClient);
    }

    [Fact]
    public void AddOrchestrationServices_ShouldRegisterIPaginationApiClient()
    {
        // Arrange & Act
        _services.AddOrchestrationServices();

        // Assert
        var serviceProvider = _services.BuildServiceProvider();
        var paginationApiClient = serviceProvider.GetService<IPaginationApiClient>();
        
        Assert.NotNull(paginationApiClient);
        Assert.IsType<PaginationApiService>(paginationApiClient);
    }

    [Fact]
    public void AddOrchestrationServices_ShouldRegisterIApiHealthClient()
    {
        // Arrange & Act
        _services.AddOrchestrationServices();

        // Assert
        var serviceProvider = _services.BuildServiceProvider();
        var healthApiClient = serviceProvider.GetService<IApiHealthClient>();
        
        Assert.NotNull(healthApiClient);
        Assert.IsType<HealthApiService>(healthApiClient);
    }

    [Fact]
    public void AddOrchestrationServices_ShouldRegisterIApiRequestHandler()
    {
        // Arrange & Act
        _services.AddOrchestrationServices();

        // Assert
        var serviceProvider = _services.BuildServiceProvider();
        var apiRequestHandler = serviceProvider.GetService<IApiRequestHandler>();
        
        Assert.NotNull(apiRequestHandler);
        Assert.IsType<ApiRequestHandler>(apiRequestHandler);
    }

    [Fact]
    public void AddOrchestrationServices_ShouldRegisterBigCommerceApiClientAsCompositeInterface()
    {
        // Arrange & Act
        _services.AddOrchestrationServices();

        // Assert
        var serviceProvider = _services.BuildServiceProvider();
        var bigCommerceApiClient = serviceProvider.GetService<IBigCommerceApiClient>();
        
        Assert.NotNull(bigCommerceApiClient);
        Assert.IsType<BigCommerceApiClient>(bigCommerceApiClient);
        
        // Verify that the composite interface can be cast to segregated interfaces
        Assert.True(bigCommerceApiClient is ICategoryApiClient);
        Assert.True(bigCommerceApiClient is IProductApiClient);
        Assert.True(bigCommerceApiClient is IPaginationApiClient);
        Assert.True(bigCommerceApiClient is IApiHealthClient);
    }

    [Fact]
    public void SegregatedInterfaceImplementations_ShouldBeSeparateInstancesFromComposite()
    {
        // Arrange & Act
        _services.AddOrchestrationServices();

        // Assert
        var serviceProvider = _services.BuildServiceProvider();
        
        // Get segregated interface implementations
        var categoryApiClient = serviceProvider.GetService<ICategoryApiClient>();
        var productApiClient = serviceProvider.GetService<IProductApiClient>();
        var paginationApiClient = serviceProvider.GetService<IPaginationApiClient>();
        var healthApiClient = serviceProvider.GetService<IApiHealthClient>();
        
        // Get composite interface implementation
        var bigCommerceApiClient = serviceProvider.GetService<IBigCommerceApiClient>();
        
        // Verify segregated interfaces are NOT the same instance as composite
        // This ensures consumers can depend on only what they need
        Assert.NotSame(categoryApiClient, bigCommerceApiClient);
        Assert.NotSame(productApiClient, bigCommerceApiClient);
        Assert.NotSame(paginationApiClient, bigCommerceApiClient);
        Assert.NotSame(healthApiClient, bigCommerceApiClient);
        
        // Verify each segregated interface is properly typed
        Assert.IsType<CategoryApiService>(categoryApiClient);
        Assert.IsType<ProductApiService>(productApiClient);
        Assert.IsType<PaginationApiService>(paginationApiClient);
        Assert.IsType<HealthApiService>(healthApiClient);
    }

    [Fact]
    public void AllServices_ShouldBeRegisteredAsSingleton()
    {
        // Arrange & Act
        _services.AddOrchestrationServices();

        // Assert
        var serviceDescriptors = _services.Where(s => 
            s.ServiceType == typeof(ICategoryApiClient) ||
            s.ServiceType == typeof(IProductApiClient) ||
            s.ServiceType == typeof(IPaginationApiClient) ||
            s.ServiceType == typeof(IApiHealthClient) ||
            s.ServiceType == typeof(IApiRequestHandler) ||
            s.ServiceType == typeof(IBigCommerceApiClient)).ToList();

        Assert.All(serviceDescriptors, descriptor => 
        {
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        });
    }
} 