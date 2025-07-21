using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Services.EntityCreation;
using BigCommerce.Migration.Orchestration.Strategies;
using System.Net.Http;

namespace BigCommerce.Migration.Orchestration.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register orchestration services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds orchestration services to the service collection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOrchestrationServices(this IServiceCollection services)
    {
        // Add logging services (required by many services)
        services.AddLogging();
        
        // Add core business services
        services.AddCoreServices();
        
        // Add orchestration-specific services
        services.AddOrchestrationSpecificServices();
        
        return services;
    }
    
    /// <summary>
    /// Adds core business services with proper lifetimes
    /// </summary>
    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // Register HttpClient for API calls
        services.TryAddSingleton<HttpClient>();
        
        // Register configuration for BigCommerceApiClient
        services.TryAddSingleton<BigCommerce.Migration.Core.Models.BigCommerceConfiguration>(provider =>
        {
            var configuration = provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            return new BigCommerce.Migration.Core.Models.BigCommerceConfiguration
            {
                BaseUrl = configuration["BigCommerce:BaseUrl"] ?? "https://api.bigcommerce.com",
                RequestTimeout = TimeSpan.FromSeconds(double.Parse(configuration["BigCommerce:RequestTimeoutSeconds"] ?? "30")),
                MaxRetries = int.Parse(configuration["BigCommerce:MaxRetries"] ?? "0"), // ✅ Disable retry logic per user preference
                RateLimitRequestsPerSecond = int.Parse(configuration["BigCommerce:RateLimitRequestsPerSecond"] ?? "12"),
                EnableDebugLogging = bool.Parse(configuration["BigCommerce:EnableDebugLogging"] ?? "false"),
                UserAgent = configuration["BigCommerce:UserAgent"] ?? "BigCommerce-Migration-System/1.0"
            };
        });
        
        // Register configuration for OpenSearchService
        services.TryAddSingleton<BigCommerce.Migration.Core.Models.OpenSearchConfiguration>(provider =>
        {
            var configuration = provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            return new BigCommerce.Migration.Core.Models.OpenSearchConfiguration
            {
                Endpoint = configuration["OpenSearch:Endpoint"] ?? "https://localhost:9200",
                Username = configuration["OpenSearch:Username"],
                Password = configuration["OpenSearch:Password"],
                DefaultIndex = configuration["OpenSearch:DefaultIndex"] ?? "bigcommerce-migration",
                ConnectionTimeout = TimeSpan.FromSeconds(double.Parse(configuration["OpenSearch:ConnectionTimeoutSeconds"] ?? "30")),
                RequestTimeout = TimeSpan.FromSeconds(double.Parse(configuration["OpenSearch:RequestTimeoutSeconds"] ?? "60")),
                MaxRetries = int.Parse(configuration["OpenSearch:MaxRetries"] ?? "3"),
                EnableDebugMode = bool.Parse(configuration["OpenSearch:EnableDebugMode"] ?? "false")
            };
        });
        
        // Register services as singleton for better performance and test consistency
        services.TryAddSingleton<ICategoryTreeResolver, CategoryTreeResolver>();
        // Note: IOpenSearchService is registered in the Functions project with conditional logic
        // Don't register it here to avoid conflicts
        
        // Register API request handler for HTTP concerns (required by BigCommerceApiClient)
        services.TryAddSingleton<IApiRequestHandler, ApiRequestHandler>();
        services.TryAddSingleton<IBigCommerceApiClient, BigCommerceApiClient>();
        
        // Register segregated API client interfaces (Interface Segregation Principle)
        services.TryAddSingleton<ICategoryApiClient, CategoryApiService>();
        services.TryAddSingleton<IProductApiClient, ProductApiService>();
        services.TryAddSingleton<IPaginationApiClient, PaginationApiService>();
        services.TryAddSingleton<IApiHealthClient, HealthApiService>();
        
        // Register Azure Storage services
        services.TryAddSingleton<IBlobService, BlobService>();
        services.TryAddSingleton<IQueueService, QueueService>();
        services.TryAddScoped<IMigrationStorageService, MigrationStorageService>();
        
        return services;
    }
    
    /// <summary>
    /// Adds orchestration-specific services
    /// </summary>
    private static IServiceCollection AddOrchestrationSpecificServices(this IServiceCollection services)
    {
        // Register orchestration services as singleton
        services.TryAddSingleton<IRateLimitService, RateLimitService>();
        services.TryAddSingleton<IBatchSizeCalculator, BatchSizeCalculator>();
        services.TryAddSingleton<IProgressTracker, ProgressTracker>();
        
        // Register entity processing services (newly created during refactoring)
        services.TryAddSingleton<IEntityFetchService, EntityFetchService>();
        services.TryAddSingleton<IEntityTransformService, EntityTransformService>();
        services.TryAddSingleton<IEntityCreateService, EntityCreateService>();
        services.TryAddSingleton<IEntityMappingService, EntityMappingService>();
        services.TryAddSingleton<IEntityErrorHandlingService, EntityErrorHandlingService>();
        
        // ✅ Register error message formatter (SOLID: Single Responsibility)
        services.TryAddSingleton<IErrorMessageFormatter, ErrorMessageFormatter>();
        
        // Register entity discovery strategy pattern implementations (Task 2.3.3 - COMPLETED)
        services.TryAddSingleton<IEntityDiscoveryStrategyFactory, EntityDiscoveryStrategyFactory>();
        services.TryAddSingleton<V2DirectPaginationStrategy>();
        services.TryAddSingleton<V3EfficientPaginationStrategy>();
        services.TryAddSingleton<V3HierarchicalStrategy>();
        
        // 🎯 Register entity creation strategy pattern implementations (Task 3.1 - COMPLETED)
        // Strategy Pattern for Open/Closed Principle compliance
        services.TryAddScoped<IEntityCreationStrategyFactory, EntityCreationStrategyFactory>();
        services.AddScoped<IEntityCreationStrategy, CategoryCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, ProductCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, BrandCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, VariantCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, ImageCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, ModifierCreationStrategy>();
        
        // 🎯 Register entity transform strategy pattern implementations (Task 3.2.2 - COMPLETED)
        // Strategy Pattern for Open/Closed Principle compliance
        services.TryAddScoped<IEntityTransformStrategyFactory, EntityTransformStrategyFactory>();
        services.AddScoped<IEntityTransformStrategy, CategoryTransformStrategy>();
        services.AddScoped<IEntityTransformStrategy, ProductTransformStrategy>();
        services.AddScoped<IEntityTransformStrategy, BrandTransformStrategy>();
        services.AddScoped<IEntityTransformStrategy, VariantTransformStrategy>();
        services.AddScoped<IEntityTransformStrategy, ImageTransformStrategy>();
        services.AddScoped<IEntityTransformStrategy, ModifierTransformStrategy>();
        
        // 🎯 Register entity fetch strategy pattern implementations (Task 3.3.2 - NEW)
        // Strategy Pattern for Open/Closed Principle compliance
        services.TryAddScoped<IEntityFetchStrategyFactory, EntityFetchStrategyFactory>();
        services.AddScoped<IEntityFetchStrategy, CategoryFetchStrategy>();
        services.AddScoped<IEntityFetchStrategy, ProductFetchStrategy>();
        services.AddScoped<IEntityFetchStrategy, BrandFetchStrategy>();
        services.AddScoped<IEntityFetchStrategy, VariantFetchStrategy>();
        services.AddScoped<IEntityFetchStrategy, ImageFetchStrategy>();
        services.AddScoped<IEntityFetchStrategy, ModifierFetchStrategy>();
        
        return services;
    }
} 