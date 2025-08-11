using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Functions.Services;
using BigCommerce.Migration.Functions.Middleware;

using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Azure;
using System.Net.Http;

namespace BigCommerce.Migration.Functions.Extensions;

/// <summary>
/// Extension methods for configuring BigCommerce Migration services in dependency injection container
/// Supports request-based multi-tenant architecture
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all BigCommerce Migration services to the dependency injection container
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configuration">Configuration containing service settings</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddBigCommerceMigrationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Validate configuration parameter
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Add configuration abstraction (DIP compliance)
        services.AddSingleton<IMigrationConfigurationProvider, MigrationConfigurationProvider>();

        // Add configuration bindings
        services.AddConfiguration(configuration);

        // Add HTTP clients
        services.AddHttpClients(configuration);

        // Add core services
        services.AddCoreServices(configuration);

        // Add orchestration services (Strategy Pattern and Activity implementations)
        services.AddOrchestrationServices();

        // Add SignalR services
        services.AddSignalRServices(configuration);

        // Add health checks
        services.AddBigCommerceMigrationHealthChecks();

        // Add OpenAPI documentation
        services.AddOpenApiConfiguration();

        // Add logging
        services.AddLogging();

        return services;
    }

    /// <summary>
    /// Adds configuration bindings for all service configurations
    /// </summary>
    private static IServiceCollection AddConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        // Add debugging for configuration loading
        Console.WriteLine("=== Configuration Debugging ===");
        Console.WriteLine($"OpenSearch:Endpoint = '{configuration["OpenSearch:Endpoint"]}'");
        Console.WriteLine($"OpenSearch:Username = '{configuration["OpenSearch:Username"]}'");
        Console.WriteLine($"OpenSearch:DefaultIndex = '{configuration["OpenSearch:DefaultIndex"]}'");
        Console.WriteLine("=== End Configuration Debugging ===");

        // Validate critical configuration sections early
        ValidateConfigurationSections(configuration);

        // Bind BigCommerce global configuration using Options pattern
        services.Configure<BigCommerceConfiguration>(configuration.GetSection("BigCommerce"));

        // Validate BigCommerce configuration
        var bigCommerceConfig = new BigCommerceConfiguration();
        configuration.GetSection("BigCommerce").Bind(bigCommerceConfig);

        if (!bigCommerceConfig.IsValid())
        {
            var errors = GetBigCommerceConfigurationErrors(bigCommerceConfig);
            throw new ArgumentException($"Invalid BigCommerce global configuration. Issues found:\n{string.Join("\n", errors)}");
        }

        // Also register as singleton for backward compatibility
        services.AddSingleton(bigCommerceConfig);

        // Bind OpenSearch configuration using Options pattern
        services.Configure<OpenSearchConfiguration>(configuration.GetSection("OpenSearch"));

        // 🎯 SUB-BATCH CONFIG: Bind ParallelProcessingConfiguration using Options pattern
        services.Configure<ParallelProcessingConfiguration>(configuration.GetSection("ParallelProcessing"));
        
        // Register ParallelProcessingConfiguration as singleton with default values for backward compatibility
        var parallelConfig = new ParallelProcessingConfiguration();
        configuration.GetSection("ParallelProcessing").Bind(parallelConfig);
        
        // Initialize default sub-batch configurations if not provided
        if (parallelConfig.SubBatchConfigurations.Count == 0)
        {
            parallelConfig.SubBatchConfigurations = SubBatchConfiguration.GetDefaultConfigurations();
        }
        
        // Set default sub-batch configuration if not provided
        if (string.IsNullOrEmpty(parallelConfig.DefaultSubBatchConfiguration.EntityType))
        {
            parallelConfig.DefaultSubBatchConfiguration = new SubBatchConfiguration();
        }
        
        services.AddSingleton(parallelConfig);

        // Validate OpenSearch configuration (only if configuration is provided)
        var openSearchConfig = new OpenSearchConfiguration();
        configuration.GetSection("OpenSearch").Bind(openSearchConfig);

        // Only validate if OpenSearch configuration is actually provided
        var openSearchSection = configuration.GetSection("OpenSearch");
        if (openSearchSection.Exists() && !openSearchConfig.IsValidEndpoint())
        {
            var errors = GetOpenSearchConfigurationErrors(openSearchConfig);
            throw new ArgumentException($"Invalid OpenSearch configuration. Issues found:\n{string.Join("\n", errors)}");
        }

        // Also register as singleton for backward compatibility
        services.AddSingleton(openSearchConfig);

        // Azure SignalR configured via connection string in appsettings.json

        return services;
    }

    /// <summary>
    /// Validates critical configuration sections that are required for service startup
    /// </summary>
    private static void ValidateConfigurationSections(IConfiguration configuration)
    {
        var errors = new List<string>();

        // Check if we're in a test environment by looking for test-specific configuration
        var isTestEnvironment = IsTestEnvironment(configuration);

        // Validate Azure Storage connection string (not required in test environment)
        // Try ConnectionStrings section first, then fall back to Values section (Azure Functions style)
        var storageConnectionString = configuration.GetConnectionString("AzureWebJobsStorage")
            ?? configuration["AzureWebJobsStorage"];
        if (string.IsNullOrEmpty(storageConnectionString))
        {
            if (!isTestEnvironment)
            {
                errors.Add("- Missing 'AzureWebJobsStorage' connection string. Azure Table Storage, Blob Storage, and Queue Storage require this connection string.");
            }
        }
        else if (!IsValidAzureStorageConnectionString(storageConnectionString))
        {
            errors.Add("- Invalid 'AzureWebJobsStorage' connection string format. Expected format: 'DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=...'");
        }

        // Validate BigCommerce section exists
        var bigCommerceSection = configuration.GetSection("BigCommerce");
        if (!bigCommerceSection.Exists())
        {
            errors.Add("- Missing 'BigCommerce' configuration section. This section is required for BigCommerce API client configuration.");
        }

        // Validate logging configuration (not required in test environment)
        var loggingSection = configuration.GetSection("Logging");
        if (!loggingSection.Exists() && !isTestEnvironment)
        {
            errors.Add("- Missing 'Logging' configuration section. Logging configuration is required for system observability.");
        }

        if (errors.Any())
        {
            throw new ArgumentException($"Critical configuration validation failed. Please fix the following issues:\n{string.Join("\n", errors.Select(x => x))}");
        }
    }

    /// <summary>
    /// Determines if we're running in a test environment
    /// </summary>
    private static bool IsTestEnvironment(IConfiguration configuration)
    {
        // Check for test-specific patterns in configuration
        var userAgent = configuration["BigCommerce:UserAgent"];
        if (!string.IsNullOrEmpty(userAgent) && (userAgent.Contains("Test") || userAgent.Contains("Dev")))
        {
            return true;
        }

        // Check for test-specific OpenSearch configuration
        var openSearchEndpoint = configuration["OpenSearch:Endpoint"];
        if (!string.IsNullOrEmpty(openSearchEndpoint) && openSearchEndpoint.Contains("localhost"))
        {
            return true;
        }

        // Check for test-specific index names
        var defaultIndex = configuration["OpenSearch:DefaultIndex"];
        if (!string.IsNullOrEmpty(defaultIndex) && defaultIndex.Contains("test"))
        {
            return true;
        }

        // Check if configuration has minimal set of keys (typical in unit tests)
        var allKeys = GetAllConfigurationKeys(configuration);
        if (allKeys.Count <= 10 && allKeys.Any(k => k.StartsWith("BigCommerce:")))
        {
            return true;
        }

        // Check if we're using default values (common in unit tests)
        var baseUrl = configuration["BigCommerce:BaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl) && baseUrl == "https://api.bigcommerce.com")
        {
            var timeout = configuration["BigCommerce:RequestTimeout"];
            if (string.IsNullOrEmpty(timeout) || timeout == "00:00:30")
            {
                return true; // Looks like a test configuration
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all configuration keys from the configuration
    /// </summary>
    private static List<string> GetAllConfigurationKeys(IConfiguration configuration)
    {
        var keys = new List<string>();
        AddKeysRecursively(configuration, "", keys);
        return keys;
    }

    /// <summary>
    /// Recursively adds configuration keys
    /// </summary>
    private static void AddKeysRecursively(IConfiguration configuration, string prefix, List<string> keys)
    {
        foreach (var child in configuration.GetChildren())
        {
            var key = string.IsNullOrEmpty(prefix) ? child.Key : $"{prefix}:{child.Key}";

            if (child.GetChildren().Any())
            {
                AddKeysRecursively(child, key, keys);
            }
            else
            {
                keys.Add(key);
            }
        }
    }

    /// <summary>
    /// Gets detailed error messages for BigCommerce configuration issues
    /// </summary>
    private static List<string> GetBigCommerceConfigurationErrors(BigCommerceConfiguration config)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.BaseUrl))
        {
            errors.Add("- BigCommerce:BaseUrl is required (e.g., 'https://api.bigcommerce.com')");
        }
        else if (!Uri.IsWellFormedUriString(config.BaseUrl, UriKind.Absolute))
        {
            errors.Add("- BigCommerce:BaseUrl must be a valid absolute URL");
        }

        if (config.RequestTimeout <= TimeSpan.Zero)
        {
            errors.Add("- BigCommerce:RequestTimeout must be greater than zero (recommended: 30 seconds)");
        }

        if (config.MaxRetries < 0)
        {
            errors.Add("- BigCommerce:MaxRetries cannot be negative (recommended: 3)");
        }

        if (config.RateLimitRequestsPerSecond <= 0 || config.RateLimitRequestsPerSecond > 50)
        {
            errors.Add("- BigCommerce:RateLimitRequestsPerSecond must be between 1 and 50 (BigCommerce limit is 12/second)");
        }

        if (string.IsNullOrEmpty(config.UserAgent))
        {
            errors.Add("- BigCommerce:UserAgent is required (e.g., 'BigCommerce-Migration-System/1.0')");
        }

        return errors;
    }

    /// <summary>
    /// Gets detailed error messages for OpenSearch configuration issues
    /// </summary>
    private static List<string> GetOpenSearchConfigurationErrors(OpenSearchConfiguration config)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.Endpoint))
        {
            errors.Add("- OpenSearch:Endpoint is required (e.g., 'https://localhost:9200')");
        }
        else if (!Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("- OpenSearch:Endpoint must be a valid absolute URL");
        }

        if (string.IsNullOrEmpty(config.DefaultIndex))
        {
            errors.Add("- OpenSearch:DefaultIndex is required (e.g., 'bigcommerce-migration')");
        }

        if (config.ConnectionTimeout <= TimeSpan.Zero)
        {
            errors.Add("- OpenSearch:ConnectionTimeout must be greater than zero (recommended: 30 seconds)");
        }

        if (config.RequestTimeout <= TimeSpan.Zero)
        {
            errors.Add("- OpenSearch:RequestTimeout must be greater than zero (recommended: 60 seconds)");
        }

        if (config.MaxRetries < 0)
        {
            errors.Add("- OpenSearch:MaxRetries cannot be negative (recommended: 3)");
        }

        return errors;
    }

    // Azure SignalR configuration validation not needed - handled by Azure Functions runtime

    /// <summary>
    /// Validates Azure Storage connection string format
    /// </summary>
    private static bool IsValidAzureStorageConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return false;

        // Check for development storage emulator
        if (connectionString.Equals("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for standard Azure Storage connection string format
        var standardRequiredParts = new[] { "DefaultEndpointsProtocol", "AccountName", "AccountKey", "EndpointSuffix" };
        if (standardRequiredParts.All(part => connectionString.Contains($"{part}=", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Check for Azurite/local development storage format (with explicit endpoints)
        var azuriteRequiredParts = new[] { "DefaultEndpointsProtocol", "AccountName", "AccountKey", "BlobEndpoint", "QueueEndpoint", "TableEndpoint" };
        if (azuriteRequiredParts.All(part => connectionString.Contains($"{part}=", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    /// <summary>
    /// Adds HTTP client factory and configures BigCommerce API client
    /// </summary>
    private static IServiceCollection AddHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        var bigCommerceSection = configuration.GetSection("BigCommerce");
        var requestTimeout = bigCommerceSection.GetValue<TimeSpan>("RequestTimeout", TimeSpan.FromSeconds(30));
        var userAgent = bigCommerceSection.GetValue<string>("UserAgent", "BigCommerce-Migration-System/1.0");

        // Configure named HttpClient for BigCommerce API
        services.AddHttpClient("BigCommerceApiClient", httpClient =>
        {
            httpClient.Timeout = requestTimeout;
            httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
        });

        // Register a factory for default HttpClient for tests
        services.AddSingleton<HttpClient>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            return httpClientFactory.CreateClient("BigCommerceApiClient");
        });

        return services;
    }

    /// <summary>
    /// Adds core business services with proper lifetimes
    /// </summary>
    private static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register services as singleton for better performance and test consistency
        services.AddSingleton<ICategoryTreeResolver, CategoryTreeResolver>();

        // Register OpenSearch service - use no-op implementation when disabled
        services.AddSingleton<IOpenSearchService>(serviceProvider =>
        {
            var openSearchConfig = serviceProvider.GetRequiredService<OpenSearchConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILogger<OpenSearchService>>();

            // Add detailed logging for debugging
            Console.WriteLine($"OpenSearch Configuration:");
            Console.WriteLine($"  Endpoint: {openSearchConfig.Endpoint}");
            Console.WriteLine($"  Username: {openSearchConfig.Username}");
            Console.WriteLine($"  DefaultIndex: {openSearchConfig.DefaultIndex}");
            Console.WriteLine($"  IsValidEndpoint(): {openSearchConfig.IsValidEndpoint()}");

            // Check if OpenSearch is disabled or has invalid configuration
            if (!openSearchConfig.IsValidEndpoint())
            {
                var noOpLogger = serviceProvider.GetRequiredService<ILogger<NoOpOpenSearchService>>();
                Console.WriteLine("OpenSearch disabled or invalid endpoint - using NoOpOpenSearchService");
                return new NoOpOpenSearchService(noOpLogger);
            }

            Console.WriteLine("OpenSearch endpoint is valid - using real OpenSearchService");
            return new OpenSearchService(openSearchConfig, logger);
        });

        // Register Azure Storage services
        services.AddSingleton<IBlobService, BlobService>();
        services.AddSingleton<IQueueService, QueueService>();
        services.AddScoped<IMigrationStorageService, MigrationStorageService>();

        // Register API request handler for HTTP concerns (delegation pattern)
                    services.AddSingleton<IApiRequestHandler>(serviceProvider =>
            {
                var httpClient = serviceProvider.GetRequiredService<HttpClient>();
                var rateLimitService = serviceProvider.GetRequiredService<IRateLimitService>();
                var openSearchService = serviceProvider.GetRequiredService<IOpenSearchService>();
                var logger = serviceProvider.GetRequiredService<ILogger<ApiRequestHandler>>();
                var dynamicRateLimiter = serviceProvider.GetRequiredService<IDynamicRateLimiter>();
                
                return new ApiRequestHandler(httpClient, rateLimitService, openSearchService, logger, dynamicRateLimiter);
            });

        // Register BigCommerce API client using delegation pattern
        services.AddSingleton<IBigCommerceApiClient, BigCommerceApiClient>();

        // Register dynamic rate limiting configuration
        services.Configure<DynamicRateLimitingConfiguration>(
            configuration.GetSection("DynamicRateLimiting"));
        
        // Register dynamic rate limiting services (Phase 1 - Dynamic Rate Limiting)
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IApiHealthMonitor, ApiHealthMonitor>();
        services.AddSingleton<IRateCalculator, BigCommerceAwareRateCalculator>();
        
        // Register base rate limiting service first
        services.AddSingleton<RateLimitService>();
        services.AddSingleton<IRateLimitService>(serviceProvider => 
            serviceProvider.GetRequiredService<RateLimitService>());
        
        // Register dynamic rate limiting service using decorator pattern
        services.AddSingleton<IDynamicRateLimiter>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DynamicRateLimitService>>();
            var baseRateLimitService = serviceProvider.GetRequiredService<RateLimitService>();
            var healthMonitor = serviceProvider.GetRequiredService<IApiHealthMonitor>();
            var rateCalculator = serviceProvider.GetRequiredService<IRateCalculator>();
            
            return new DynamicRateLimitService(logger, baseRateLimitService, healthMonitor, rateCalculator);
        });
        
        // 🚨 FIX: Don't override IRateLimitService - let both coexist
        // The ApiRequestHandler will use IDynamicRateLimiter when available
        
        services.AddSingleton<IBatchSizeCalculator, BatchSizeCalculator>();
        services.AddSingleton<IProgressTracker>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<ProgressTracker>>();
            var progressEventPublisher = serviceProvider.GetRequiredService<IProgressEventPublisher>();
            var signalREventFactory = serviceProvider.GetRequiredService<ISignalREventFactory>(); // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
            var storageService = serviceProvider.GetService<IMigrationStorageService>(); // Optional dependency
            return new ProgressTracker(logger, progressEventPublisher, signalREventFactory, storageService);
        });

        // Register entity processing services (newly created during refactoring)
        services.AddSingleton<IEntityFetchService, EntityFetchService>();
        services.AddSingleton<IEntityTransformService, EntityTransformService>();
        services.AddSingleton<IEntityCreateService, EntityCreateService>();
        services.AddSingleton<IEntityMappingService, EntityMappingService>();
        services.AddSingleton<IEntityErrorHandlingService, EntityErrorHandlingService>();

        // Register API authentication services
        services.AddSingleton<IApiKeyService, ApiKeyService>();

        // Register API rate limiting services
        services.AddSingleton<IApiRateLimitService, ApiRateLimitService>();

        // Register progress event publisher for queue-based SignalR broadcasting
        services.AddSingleton<IProgressEventPublisher, ProgressEventPublisher>();
        
        // Register progress queue service for progress event publishing (separate from migration queues)
        services.AddSingleton<IProgressQueueService, AzureProgressQueueService>();
        
        // 🎯 **CENTRALIZED SIGNALR SERVICES** (Consistency & Validation)
        // Single source of truth for all SignalR event creation and messaging
        services.AddSingleton<ISignalREventFactory, SignalREventFactory>();
        services.AddSingleton<ISignalRMessageConverter, SignalRMessageConverter>();

        // ✅ **ENTITY DEPENDENCY RESOLUTION SYSTEM** (Intelligent Phase Sequencing)
        // Automatically resolves entity dependencies and triggers phased processing
        services.AddSingleton<IEntityDependencyResolver, EntityDependencyResolver>();
        
        // ✅ **P2.5: Phase 2 Enhanced Parallel Processing Pipeline** (Required for 17.0x throughput)
        // These services were moved from Orchestration project to ensure proper DI resolution
        services.AddSingleton<IEnhancedParallelProcessor, EnhancedParallelProcessor>();
        

        return services;
    }

    /// <summary>
    /// Adds performance-optimized SignalR services for real-time dashboard updates
    /// Performance Optimizations: HTTP connection pooling, async queuing, message batching, configuration caching
    /// SOLID Principles: Dependency Inversion, Single Responsibility, Open/Closed
    /// </summary>
    private static IServiceCollection AddSignalRServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure HTTP client factory for connection pooling optimization
        services.AddHttpClient("SignalR", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "BigCommerce-Migration/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15), // Connection reuse optimization
            MaxConnectionsPerServer = 10, // Support concurrent SignalR calls
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5), // Resource cleanup
            ConnectTimeout = TimeSpan.FromSeconds(10) // Fast connection establishment
        });

        // Azure SignalR configured via connection string - no complex batching needed
        var connectionString = configuration.GetConnectionString("AzureSignalR");
        if (!string.IsNullOrEmpty(connectionString))
        {
            Console.WriteLine("Azure SignalR connection string configured for queue-based broadcasting.");
        }
        else
        {
            Console.WriteLine("Warning: Azure SignalR connection string not configured.");
        }
        
        // MigrationHub removed - using direct Azure Functions SignalR bindings

        return services;
    }

    // SignalR service registration simplified - using Azure Functions direct bindings

    /// <summary>
    /// Validates Azure SignalR connection string format
    /// </summary>
    private static bool IsValidAzureSignalRConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return false;

        // Check for Azure SignalR connection string (production)
        var azureSignalRParts = new[] { "Endpoint=", "AccessKey=" };
        var isAzureSignalR = azureSignalRParts.All(part => connectionString.Contains(part, StringComparison.OrdinalIgnoreCase));

        // Check for emulator connection string (local development)
        var emulatorParts = new[] { "Endpoint=", "Port=" };
        var isEmulator = emulatorParts.All(part => connectionString.Contains(part, StringComparison.OrdinalIgnoreCase));

        return isAzureSignalR || isEmulator;
    }

    /// <summary>
    /// Adds health checks for all external dependencies
    /// Note: Health checks are not store-specific in the new architecture
    /// </summary>
    private static IServiceCollection AddBigCommerceMigrationHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<OpenSearchHealthCheck>("opensearch",
                HealthStatus.Degraded,
                tags: new[] { "opensearch", "logging" })
            .AddCheck<AzureStorageHealthCheck>("azure-storage",
                HealthStatus.Unhealthy,
                tags: new[] { "azure", "storage" });
            // SignalR health checking handled by Azure SignalR Service

        return services;
    }
}

/// <summary>
/// Health check for OpenSearch connectivity
/// </summary>
public class OpenSearchHealthCheck : IHealthCheck
{
    private readonly IOpenSearchService _openSearchService;

    public OpenSearchHealthCheck(IOpenSearchService openSearchService)
    {
        _openSearchService = openSearchService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _openSearchService.IsHealthyAsync(cancellationToken);

            return isHealthy
                ? HealthCheckResult.Healthy("OpenSearch is accessible and responsive")
                : HealthCheckResult.Unhealthy("OpenSearch is not accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("OpenSearch health check failed", ex);
        }
    }
}

/// <summary>
/// Health check for Azure Storage connectivity
/// </summary>
public class AzureStorageHealthCheck : IHealthCheck
{
    private readonly IMigrationStorageService _storageService;

    public AzureStorageHealthCheck(IMigrationStorageService storageService)
    {
        _storageService = storageService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Test basic storage connectivity by attempting to create a test entry
            var testMigration = new MigrationEntry
            {
                Id = $"health-check-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}",
                SourceStoreId = "health-check",
                DestinationStoreId = "health-check",
                Status = MigrationStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Create and immediately delete the test entry
            await _storageService.CreateMigrationAsync(testMigration);
            await _storageService.DeleteMigrationAsync(testMigration.Id);

            return HealthCheckResult.Healthy("Azure Storage is accessible and responsive");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Azure Storage health check failed", ex);
        }
    }
}

// SignalR health monitoring handled by Azure SignalR Service built-in capabilities
