using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Strategies;
using BigCommerce.Migration.Orchestration.Services.EntityCreation;
using BigCommerce.Migration.Orchestration.Orchestrators;
using BigCommerce.Migration.Orchestration.Activities;
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
    /// ARCHITECTURAL CHANGE: Core services moved to Functions project to ensure optimizations are applied
    /// This method now only registers orchestration infrastructure dependencies
    /// </summary>
    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // 🏗️ ARCHITECTURAL CHANGE: Moved all core business services to Functions project
        // This ensures that the Functions project's optimized services (Dynamic Rate Limiting, 
        // Enhanced Parallel Processing) are the ones that get registered, not overridden
        
        // Only register absolute infrastructure dependencies that Orchestration needs
        // All business logic services are now registered in Functions project AddCoreServices
        
        return services;
    }
    
    /// <summary>
    /// Adds orchestration-specific services ONLY
    /// ARCHITECTURAL CHANGE: Removed all core business services to prevent duplicates with Functions project
    /// </summary>
    private static IServiceCollection AddOrchestrationSpecificServices(this IServiceCollection services, IConfiguration? configuration)
    {
        // 🏗️ ARCHITECTURAL CHANGE: Removed all duplicate core services
        // Core business services (IApiRequestHandler, IBigCommerceApiClient, etc.) are now 
        // exclusively registered in Functions project to ensure optimizations are applied
        
        // ✅ **ORCHESTRATION-SPECIFIC SERVICES ONLY** - No duplicates with Functions project
        
        // Phase 3.1: Register distributed lock service for orchestrator collision detection
        services.AddSingleton<IDistributedLockService, AzureTableDistributedLockService>();
        
        // Phase 3.2: Register distributed lock heartbeat service for phantom orchestrator prevention  
        services.AddSingleton<IDistributedLockHeartbeatService, AzureTableDistributedLockHeartbeatService>();
        
        // Phase 3.1: Register orchestrator collision detection service (Phase 3.2: Enhanced with heartbeat)
        services.AddSingleton<OrchestratorCollisionDetectionService>();
        
        // Phase 3.3: Register orchestrator cleanup service for administrative operations
        services.AddSingleton<IOrchestratorCleanupService, OrchestratorCleanupService>();
        
        // Phase 4.3: Register progress state validation service for cancellation consistency checks
        services.AddScoped<IProgressStateValidator, ProgressStateValidator>();
        
        // ✅ Register error message formatter (SOLID: Single Responsibility)
        services.AddSingleton<IErrorMessageFormatter, ErrorMessageFormatter>();
        
        // 🚀 SUB-BATCH PROCESSING: Register sub-batch configuration and processing services
        services.AddSingleton<ISubBatchConfigurationService, SubBatchConfigurationService>();
        services.AddScoped<ISubBatchProcessor, SubBatchProcessor>();
        
        // 🎯 Register configuration activity for orchestrator use
        services.AddScoped<GetEntityConfigurationActivity>();
        
        // Register entity discovery strategy pattern implementations (Task 2.3.3 - COMPLETED)
        services.AddSingleton<IEntityDiscoveryStrategyFactory, EntityDiscoveryStrategyFactory>();
        services.AddSingleton<V2DirectPaginationStrategy>();
        services.AddSingleton<V3EfficientPaginationStrategy>();
        services.AddSingleton<V3HierarchicalStrategy>();
        
        // 🎯 Register entity creation strategy pattern implementations (Task 3.1 - COMPLETED)
        // Strategy Pattern for Open/Closed Principle compliance
        services.AddScoped<IEntityCreationStrategyFactory, EntityCreationStrategyFactory>();
        services.AddScoped<IEntityCreationStrategy, CategoryCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, ProductCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, BrandCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, VariantCreationStrategy>();
        services.AddScoped<IEntityCreationStrategy, ImageCreationStrategy>();
        
        // 🔄 Register entity transform strategy pattern implementations (Task 3.2 - COMPLETED)
        services.AddScoped<IEntityTransformStrategyFactory, EntityTransformStrategyFactory>();
        services.AddScoped<IEntityTransformStrategy, CategoryTransformStrategy>();
        services.AddScoped<IEntityTransformStrategy, ProductTransformStrategy>();
        services.AddScoped<IEntityTransformStrategy, BrandTransformStrategy>();
        services.AddScoped<IEntityTransformStrategy, VariantTransformStrategy>();
        
        // 🔽 Register entity fetch strategy pattern implementations (Task 3.3 - COMPLETED)
        services.AddScoped<IEntityFetchStrategyFactory, EntityFetchStrategyFactory>();
        services.AddScoped<IEntityFetchStrategy, CategoryFetchStrategy>();
        services.AddScoped<IEntityFetchStrategy, ProductFetchStrategy>();
        services.AddScoped<IEntityFetchStrategy, BrandFetchStrategy>();
        
        // Note: EntityMigrationDurableOrchestrator is now used in Functions project - no DI registration needed here
        
        return services;
    }
} 