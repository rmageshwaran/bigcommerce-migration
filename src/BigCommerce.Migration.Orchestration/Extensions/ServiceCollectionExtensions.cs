using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Services;
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
                MaxRetries = int.Parse(configuration["BigCommerce:MaxRetries"] ?? "3"),
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
        services.TryAddSingleton<IOpenSearchService, OpenSearchService>();
        services.TryAddSingleton<IBigCommerceApiClient, BigCommerceApiClient>();
        
        // Register Azure Storage services
        services.TryAddSingleton<IBlobService, BlobService>();
        services.TryAddSingleton<IQueueService, QueueService>();
        services.TryAddSingleton<IMigrationStorageService, MigrationStorageService>();
        
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
        
        return services;
    }
} 