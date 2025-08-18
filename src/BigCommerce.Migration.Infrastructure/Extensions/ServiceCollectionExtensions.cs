using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace BigCommerce.Migration.Infrastructure.Extensions;

/// <summary>
/// Extension methods for configuring BigCommerce Migration Infrastructure services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds incremental progress tracking services to the dependency injection container
    /// Enables real-time progress updates and prevents data loss during migration cancellations
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configuration">Configuration containing Azure storage settings</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddIncrementalProgressServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Validate configuration
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Validate Azure Storage connection string is available
        var connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
            ?? configuration["AzureWebJobsStorage"];
            
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "AzureWebJobsStorage connection string is required for incremental progress services. " +
                "Please ensure it's configured in app settings or connection strings.");
        }

        // Register the increment events service as a singleton
        // Centralized table management service
        services.AddSingleton<IAzureTableInitializationService, AzureTableInitializationService>();
        
        // Singleton is appropriate because:
        // 1. Service is stateless (no per-request state)
        // 2. TableServiceClient is thread-safe and designed for reuse
        // 3. Better performance (avoid repeated initialization)
        services.AddSingleton<IIncrementEventsService, IncrementEventsService>();

        return services;
    }
}