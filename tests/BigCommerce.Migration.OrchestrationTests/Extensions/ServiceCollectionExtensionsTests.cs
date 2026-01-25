using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
using Moq;
using BigCommerce.Migration.Orchestration.Extensions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.OrchestrationTests.Extensions;

/// <summary>
/// Tests for ServiceCollectionExtensions to validate dependency injection registration
/// </summary>
public class ServiceCollectionExtensionsTests
{
    private readonly IConfiguration _configuration;
    private readonly IServiceCollection _services;

    public ServiceCollectionExtensionsTests()
    {
        // Create test configuration
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
            ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
            ["OpenSearch:Endpoint"] = "https://localhost:9200",
            ["OpenSearch:Username"] = "admin",
            ["OpenSearch:Password"] = "admin"
        });
        _configuration = configurationBuilder.Build();
        
        _services = new ServiceCollection();
        _services.AddSingleton(_configuration);
        _services.AddLogging();
        
        // Add missing core services that orchestration depends on
        _services.AddSingleton<IOpenSearchService>(new Mock<IOpenSearchService>().Object);
        _services.AddSingleton<IApiRequestHandler>(new Mock<IApiRequestHandler>().Object);
        _services.AddSingleton<IProgressQueueService>(new Mock<IProgressQueueService>().Object);
    }

    [Fact]
    public void AddOrchestrationServices_ShouldRegisterAllRequiredServices()
    {
        // Act
        _services.AddOrchestrationServices();
        var serviceProvider = _services.BuildServiceProvider();

        // Assert - Verify all orchestration-specific services are registered
        Assert.NotNull(serviceProvider.GetService<IRateLimitService>());
        Assert.NotNull(serviceProvider.GetService<IBatchSizeCalculator>());
        Assert.NotNull(serviceProvider.GetService<IProgressTracker>());
        
        // Assert - Verify all entity processing services are registered
        Assert.NotNull(serviceProvider.GetService<IEntityFetchService>());
        Assert.NotNull(serviceProvider.GetService<IEntityTransformService>());
        Assert.NotNull(serviceProvider.GetService<IEntityCreateService>());
        Assert.NotNull(serviceProvider.GetService<IEntityMappingService>());
        Assert.NotNull(serviceProvider.GetService<IEntityErrorHandlingService>());
    }

    [Fact]
    public void AddOrchestrationServices_ShouldRegisterExistingCoreServices()
    {
        // Act
        _services.AddOrchestrationServices();
        var serviceProvider = _services.BuildServiceProvider();

        // Assert - Verify core services are available
        Assert.NotNull(serviceProvider.GetService<ICategoryTreeResolver>());
        Assert.NotNull(serviceProvider.GetService<IOpenSearchService>());
        Assert.NotNull(serviceProvider.GetService<IBigCommerceApiClient>());
        Assert.NotNull(serviceProvider.GetService<IBlobService>());
        Assert.NotNull(serviceProvider.GetService<IQueueService>());
        Assert.NotNull(serviceProvider.GetService<IMigrationStorageService>());
    }

    [Fact]
    public void AddOrchestrationServices_ShouldRegisterServicesAsSingleton()
    {
        // Act
        _services.AddOrchestrationServices();
        var serviceProvider = _services.BuildServiceProvider();

        // Assert - Verify services are registered as singleton
        var rateLimitService1 = serviceProvider.GetService<IRateLimitService>();
        var rateLimitService2 = serviceProvider.GetService<IRateLimitService>();
        Assert.Same(rateLimitService1, rateLimitService2);

        var batchSizeCalculator1 = serviceProvider.GetService<IBatchSizeCalculator>();
        var batchSizeCalculator2 = serviceProvider.GetService<IBatchSizeCalculator>();
        Assert.Same(batchSizeCalculator1, batchSizeCalculator2);
    }

    [Fact]
    public void AddOrchestrationServices_ShouldNotThrowException()
    {
        // Act & Assert
        var exception = Record.Exception(() => _services.AddOrchestrationServices());
        Assert.Null(exception);
    }

    [Fact]
    public void AddOrchestrationServices_ShouldAllowMultipleCalls()
    {
        // Act
        _services.AddOrchestrationServices();
        _services.AddOrchestrationServices(); // Should not throw

        // Assert
        var serviceProvider = _services.BuildServiceProvider();
        Assert.NotNull(serviceProvider.GetService<IRateLimitService>());
    }
} 