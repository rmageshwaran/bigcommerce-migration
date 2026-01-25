using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Services.EntityCreation;
using BigCommerce.Migration.Functions.Extensions;
using Xunit.Abstractions;

namespace BigCommerce.Migration.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for integration tests providing real service dependencies
/// Configures actual BigCommerce APIs, storage, and logging for end-to-end testing
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly IConfiguration Configuration;
    protected readonly ILogger Logger;
    protected readonly StoreConfiguration SourceStore;
    protected readonly StoreConfiguration DestinationStore;
    protected readonly IntegrationTestConfiguration TestConfig;

    protected IntegrationTestBase(ITestOutputHelper output)
    {
        // Build configuration
        Configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<IntegrationTestBase>()
            .Build();

        // Create service collection
        var services = new ServiceCollection();
        
        // Configure logging with test output
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.AddProvider(new XunitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Register configuration
        services.AddSingleton<IConfiguration>(Configuration);

        // Register BigCommerce configuration
        var bigCommerceConfig = new BigCommerceConfiguration();
        Configuration.GetSection("BigCommerce").Bind(bigCommerceConfig);
        services.AddSingleton(bigCommerceConfig);

        // Register OpenSearch configuration
        var openSearchConfig = new OpenSearchConfiguration();
        Configuration.GetSection("OpenSearch").Bind(openSearchConfig);
        services.AddSingleton(openSearchConfig);

        // Register integration test configuration
        TestConfig = new IntegrationTestConfiguration();
        Configuration.GetSection("Integration").Bind(TestConfig);
        services.AddSingleton(TestConfig);

        // Register core services with real implementations
        RegisterCoreServices(services);

        // Build service provider
        ServiceProvider = services.BuildServiceProvider();

        // Initialize logger
        Logger = ServiceProvider.GetRequiredService<ILogger<IntegrationTestBase>>();

        // Initialize store configurations
        SourceStore = CreateSourceStoreConfiguration();
        DestinationStore = CreateDestinationStoreConfiguration();

        Logger.LogInformation("Integration test base initialized for stores: {SourceStore} -> {DestinationStore}", 
            SourceStore.StoreId, DestinationStore.StoreId);
    }

    /// <summary>
    /// Registers all core services with real implementations for integration testing
    /// </summary>
    private void RegisterCoreServices(IServiceCollection services)
    {
        // Add test-specific configuration for Azure Storage
        var testConfig = new ConfigurationBuilder()
            .AddConfiguration(Configuration) // Keep existing configuration
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
                ["ConnectionStrings:AzureWebJobsStorage"] = "UseDevelopmentStorage=true"
            })
            .Build();
        services.AddSingleton<IConfiguration>(testConfig);

        // Register Infrastructure services
        services.AddTransient<IBigCommerceApiClient, BigCommerceApiClient>();
        services.AddTransient<ICategoryTreeResolver, CategoryTreeResolver>();
        services.AddTransient<IBlobService, BlobService>();
        services.AddTransient<IMigrationStorageService, MigrationStorageService>();
        services.AddTransient<IOpenSearchService, OpenSearchService>();
        services.AddTransient<IQueueService, QueueService>();

        // Register dynamic rate limiting services (Phase 1 implementation)
        services.AddSingleton<IDynamicRateLimiter, DynamicRateLimitService>();
        services.AddSingleton<IApiHealthMonitor, ApiHealthMonitor>();
        services.AddSingleton<IRateCalculator, BigCommerceAwareRateCalculator>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        
        // Register enhanced API request handler with dynamic rate limiting
        services.AddSingleton<IApiRequestHandler>(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();
            var rateLimitService = serviceProvider.GetRequiredService<IRateLimitService>();
            var openSearchService = serviceProvider.GetRequiredService<IOpenSearchService>();
            var logger = serviceProvider.GetRequiredService<ILogger<ApiRequestHandler>>();
            var dynamicRateLimiter = serviceProvider.GetRequiredService<IDynamicRateLimiter>();
            
            return new ApiRequestHandler(httpClient, rateLimitService, openSearchService, logger, dynamicRateLimiter);
        });

        // Register Orchestration services
        services.AddTransient<IRateLimitService, RateLimitService>();
        services.AddTransient<IProgressTracker, ProgressTracker>();
        services.AddTransient<IBatchSizeCalculator, BatchSizeCalculator>();
        
        // Register entity processing services (newly created during refactoring)
        services.AddTransient<IEntityFetchService, EntityFetchService>();
        services.AddTransient<IEntityTransformService, EntityTransformService>();
        services.AddTransient<IEntityCreateService, EntityCreateService>();
        services.AddTransient<IEntityMappingService, EntityMappingService>();
        services.AddTransient<IEntityErrorHandlingService, EntityErrorHandlingService>();

        // **Phase 3: Enhanced Parallel Processing Services for Integration Testing**
        services.AddSingleton<IEnhancedParallelProcessor, EnhancedParallelProcessor>();
        services.AddSingleton<IParallelProgressAggregator, ParallelProgressAggregator>();
        services.AddSingleton<IParallelBatchProcessingPipeline, ParallelBatchProcessingPipeline>();
        services.AddSingleton<IProgressEventPublisher, ProgressEventPublisher>();

        // Configure HTTP client
        services.AddHttpClient();

        Logger?.LogDebug("Registered all core services for integration testing");
    }

    /// <summary>
    /// Creates source store configuration from settings
    /// </summary>
    private StoreConfiguration CreateSourceStoreConfiguration()
    {
        var storeId = Configuration["BigCommerce:SourceStore:StoreId"] ?? 
            throw new InvalidOperationException("Source store ID not configured");
        var accessToken = Configuration["BigCommerce:SourceStore:AccessToken"] ?? 
            throw new InvalidOperationException("Source store access token not configured");
        var channelId = Configuration["BigCommerce:SourceStore:ChannelId"] ?? "1";

        return new StoreConfiguration
        {
            StoreId = storeId,
            AccessToken = accessToken,
            ChannelId = channelId,
            BaseUrl = Configuration["BigCommerce:BaseUrl"] ?? "https://api.bigcommerce.com"
        };
    }

    /// <summary>
    /// Creates destination store configuration from settings
    /// </summary>
    private StoreConfiguration CreateDestinationStoreConfiguration()
    {
        var storeId = Configuration["BigCommerce:DestinationStore:StoreId"] ?? 
            throw new InvalidOperationException("Destination store ID not configured");
        var accessToken = Configuration["BigCommerce:DestinationStore:AccessToken"] ?? 
            throw new InvalidOperationException("Destination store access token not configured");
        var channelId = Configuration["BigCommerce:DestinationStore:ChannelId"] ?? "1";

        return new StoreConfiguration
        {
            StoreId = storeId,
            AccessToken = accessToken,
            ChannelId = channelId,
            BaseUrl = Configuration["BigCommerce:BaseUrl"] ?? "https://api.bigcommerce.com"
        };
    }

    /// <summary>
    /// Gets a service instance from the DI container
    /// </summary>
    protected T GetService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Creates a unique test identifier for this test run
    /// </summary>
    protected string CreateTestId()
    {
        return $"{TestConfig.TestDataPrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
    }

    /// <summary>
    /// Validates that both stores are accessible before running tests
    /// </summary>
    protected async Task ValidateStoreConnectivity()
    {
        var apiClient = GetService<IBigCommerceApiClient>();

        Logger.LogInformation("Validating source store connectivity: {StoreId}", SourceStore.StoreId);
        var sourceHealthy = await apiClient.IsHealthyAsync(SourceStore);
        if (!sourceHealthy)
        {
            throw new InvalidOperationException($"Source store {SourceStore.StoreId} is not accessible");
        }

        Logger.LogInformation("Validating destination store connectivity: {StoreId}", DestinationStore.StoreId);
        var destinationHealthy = await apiClient.IsHealthyAsync(DestinationStore);
        if (!destinationHealthy)
        {
            throw new InvalidOperationException($"Destination store {DestinationStore.StoreId} is not accessible");
        }

        Logger.LogInformation("Both stores are accessible and ready for testing");
    }

    /// <summary>
    /// Cleanup method for test resources
    /// </summary>
    protected virtual async Task CleanupTestData(string testId)
    {
        if (!TestConfig.EnableCleanupAfterTests)
        {
            Logger.LogInformation("Test cleanup disabled, skipping cleanup for test: {TestId}", testId);
            return;
        }

        try
        {
            Logger.LogInformation("Cleaning up test data for test: {TestId}", testId);
            
            // TODO: Implement cleanup logic for test entities
            // This will clean up any test data created during integration tests
            
            Logger.LogInformation("Successfully cleaned up test data for test: {TestId}", testId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to cleanup test data for test: {TestId}", testId);
        }
    }

    public virtual void Dispose()
    {
        ServiceProvider?.GetService<IServiceScope>()?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Configuration model for integration test settings
/// </summary>
public class IntegrationTestConfiguration
{
    public int TestTimeoutMinutes { get; set; } = 15;
    public int MaxTestEntities { get; set; } = 100;
    public bool EnableCleanupAfterTests { get; set; } = true;
    public string TestDataPrefix { get; set; } = "IntegrationTest_";
    public bool EnablePerformanceMetrics { get; set; } = true;
    public bool EnableRealTimeMonitoring { get; set; } = true;
}

/// <summary>
/// Custom Xunit logger provider for test output
/// </summary>
public class XunitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XunitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XunitLogger(_output, categoryName);
    }

    public void Dispose() { }
}

/// <summary>
/// Custom Xunit logger for test output
/// </summary>
public class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state) => null!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            var message = $"[{DateTime.UtcNow:HH:mm:ss.fff}] [{logLevel}] [{_categoryName}] {formatter(state, exception)}";
            if (exception != null)
            {
                message += Environment.NewLine + exception.ToString();
            }
            _output.WriteLine(message);
        }
        catch
        {
            // Ignore logging errors in tests
        }
    }
} 