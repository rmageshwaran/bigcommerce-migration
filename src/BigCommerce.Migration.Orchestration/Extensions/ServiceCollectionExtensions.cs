using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
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
    /// <param name="configuration">Configuration instance for service setup</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOrchestrationServices(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // Add logging services (required by many services)
        services.AddLogging();
        
        // Add core business services
        services.AddCoreServices();
        
        // Add orchestration-specific services
        services.AddOrchestrationSpecificServices(configuration);
        
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
        services.TryAddSingleton<IApiRequestHandler>(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();
            var rateLimitService = serviceProvider.GetRequiredService<IRateLimitService>();
            var openSearchService = serviceProvider.GetRequiredService<IOpenSearchService>();
            var logger = serviceProvider.GetRequiredService<ILogger<ApiRequestHandler>>();
            var dynamicRateLimiter = serviceProvider.GetRequiredService<IDynamicRateLimiter>();
            
            return new ApiRequestHandler(httpClient, rateLimitService, openSearchService, logger, dynamicRateLimiter);
        });
        services.TryAddSingleton<IBigCommerceApiClient, BigCommerceApiClient>();
        services.TryAddSingleton<IBatchApiClient, BatchApiClient>();
        
        // Register segregated API client interfaces (Interface Segregation Principle)
        services.TryAddSingleton<ICategoryApiClient, CategoryApiService>();
        services.TryAddSingleton<IProductApiClient, ProductApiService>();
        services.TryAddSingleton<IPaginationApiClient, PaginationApiService>();
        services.TryAddSingleton<IApiHealthClient, HealthApiService>();
        
        // Register Azure Storage services
        services.TryAddSingleton<IBlobService, BlobService>();
        services.TryAddSingleton<IQueueService, QueueService>();
        services.TryAddScoped<IMigrationStorageService, MigrationStorageService>();
        
        // Phase 3.1: Register distributed lock service for orchestrator collision detection
        services.TryAddSingleton<IDistributedLockService, AzureTableDistributedLockService>();
        // Phase 3.2: Register distributed lock heartbeat service for phantom orchestrator prevention
        services.TryAddSingleton<IDistributedLockHeartbeatService, AzureTableDistributedLockHeartbeatService>();
        
        // NOTE: QueueServiceClient registration removed - ProgressEventPublisher now creates client directly like QueueService
        
        return services;
    }
    
    /// <summary>
    /// Adds orchestration-specific services
    /// </summary>
    private static IServiceCollection AddOrchestrationSpecificServices(this IServiceCollection services, IConfiguration? configuration)
    {
        // Register dynamic rate limiting configuration
        if (configuration != null)
        {
            services.Configure<DynamicRateLimitingConfiguration>(
                configuration.GetSection("DynamicRateLimiting"));
        }
        
        // Register dynamic rate limiting services (Phase 1 - Dynamic Rate Limiting)
        services.TryAddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.TryAddSingleton<IApiHealthMonitor, ApiHealthMonitor>();
        services.TryAddSingleton<IRateCalculator, BigCommerceAwareRateCalculator>();
        
        // Register base rate limiting service first
        services.TryAddSingleton<RateLimitService>();
        
        // Register dynamic rate limiting service using decorator pattern
        services.TryAddSingleton<IDynamicRateLimiter>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DynamicRateLimitService>>();
            var baseRateLimitService = serviceProvider.GetRequiredService<RateLimitService>();
            var healthMonitor = serviceProvider.GetRequiredService<IApiHealthMonitor>();
            var rateCalculator = serviceProvider.GetRequiredService<IRateCalculator>();
            
            return new DynamicRateLimitService(logger, baseRateLimitService, healthMonitor, rateCalculator);
        });
        
        // Register IRateLimitService to use dynamic implementation for backward compatibility
        services.TryAddSingleton<IRateLimitService>(serviceProvider => 
            serviceProvider.GetRequiredService<IDynamicRateLimiter>());
        
        services.TryAddSingleton<IBatchSizeCalculator, BatchSizeCalculator>();
        
        // Register progress event publisher for queue-based SignalR integration
        services.TryAddSingleton<IProgressEventPublisher, ProgressEventPublisher>();
        services.TryAddSingleton<IProgressTracker, ProgressTracker>();
        
        // ✅ **P2.5: Phase 2 Enhanced Parallel Processing Pipeline** (Required for 12.5x throughput)
        services.TryAddSingleton<IEnhancedParallelProcessor, EnhancedParallelProcessor>();
        services.TryAddSingleton<IParallelBatchProcessingPipeline, ParallelBatchProcessingPipeline>();
        
        // Register entity processing services (newly created during refactoring)
        services.TryAddSingleton<IEntityFetchService, EntityFetchService>();
        services.TryAddSingleton<IEntityTransformService, EntityTransformService>();
        services.TryAddSingleton<IEntityCreateService, EntityCreateService>();
        services.TryAddSingleton<IEntityMappingService, EntityMappingService>();
        services.TryAddSingleton<IEntityErrorHandlingService, EntityErrorHandlingService>();
        
        // Phase 3.1: Register orchestrator collision detection service (Phase 3.2: Enhanced with heartbeat)
        services.TryAddSingleton<OrchestratorCollisionDetectionService>();
        // Phase 3.3: Register orchestrator cleanup service for administrative operations
        services.TryAddSingleton<IOrchestratorCleanupService, OrchestratorCleanupService>();
        
        // Phase 4.3: Register progress state validation service for cancellation consistency checks
        services.TryAddScoped<IProgressStateValidator, ProgressStateValidator>();
        
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