using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.Azurite;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;

namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// Test fixture that manages Azurite (Azure Storage Emulator) for integration tests
/// Provides real Azure Table Storage behavior for authentic rate limiting tests
/// </summary>
public class AzuriteTestFixture : IAsyncLifetime
{
    private AzuriteContainer? _azuriteContainer;
    private string? _connectionString;

    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("Azurite not initialized");

    public async Task InitializeAsync()
    {
        // Start Azurite container
        _azuriteContainer = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .Build();

        await _azuriteContainer.StartAsync();
        _connectionString = _azuriteContainer.GetConnectionString();

        Console.WriteLine($"✅ Azurite started successfully");
        Console.WriteLine($"🔗 Connection String: {_connectionString}");

        // Initialize required tables
        await InitializeTablesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_azuriteContainer != null)
        {
            await _azuriteContainer.StopAsync();
            await _azuriteContainer.DisposeAsync();
            Console.WriteLine("🛑 Azurite stopped");
        }
    }

    /// <summary>
    /// Creates a real RateLimitingTableStorageFactory connected to Azurite
    /// </summary>
    public IRateLimitingTableStorageFactory CreateTableStorageFactory(DynamicRateLimitingConfiguration config)
    {
        config.Predictive.TableStorage.AutoCreateTables = true;

        // Create configuration that includes the Azurite connection string
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:AzureWebJobsStorage", ConnectionString }
            })
            .Build();

        var logger = CreateLogger<RateLimitingTableStorageFactory>();
        return new RateLimitingTableStorageFactory(configuration, Options.Create(config), logger);
    }

    /// <summary>
    /// Creates a properly configured DistributedQuotaTracker with real Azure Storage
    /// </summary>
    public DistributedQuotaTracker CreateDistributedQuotaTracker(
        DynamicRateLimitingConfiguration config,
        Core.Services.ISignalREventFactory signalREventFactory,
        Core.Interfaces.IProgressEventPublisher progressEventPublisher)
    {
        var tableStorageFactory = CreateTableStorageFactory(config);
        var logger = CreateLogger<DistributedQuotaTracker>();

        return new DistributedQuotaTracker(
            tableStorageFactory,
            signalREventFactory,
            progressEventPublisher,
            Options.Create(config),
            logger);
    }

    /// <summary>
    /// Creates a TokenConsensusManager with real Azure Storage integration
    /// </summary>
    public TokenConsensusManager CreateTokenConsensusManager(
        DynamicRateLimitingConfiguration config,
        IDistributedQuotaTracker quotaTracker,
        IInstanceCoordinationManager coordinationManager)
    {
        var tableStorageFactory = CreateTableStorageFactory(config);
        var logger = CreateLogger<TokenConsensusManager>();

        return new TokenConsensusManager(
            tableStorageFactory,
            quotaTracker,
            coordinationManager,
            Options.Create(config),
            logger);
    }

    /// <summary>
    /// Gets a direct TableClient for advanced table operations
    /// </summary>
    public async Task<TableClient> GetTableClientAsync(string tableName)
    {
        var tableClient = new TableClient(ConnectionString, tableName);
        await tableClient.CreateIfNotExistsAsync();
        return tableClient;
    }

    /// <summary>
    /// Clears all data from rate limiting tables (useful between tests)
    /// </summary>
    public async Task ClearAllTablesAsync()
    {
        var tableNames = new[]
        {
            "quotatracking",
            "instancecoordination", 
            "tokenallocation"
        };

        foreach (var tableName in tableNames)
        {
            try
            {
                var tableClient = new TableClient(ConnectionString, tableName);
                
                // Query all entities and delete them
                await foreach (var entity in tableClient.QueryAsync<TableEntity>())
                {
                    await tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Warning: Could not clear table {tableName}: {ex.Message}");
            }
        }

        Console.WriteLine("🧹 All rate limiting tables cleared");
    }

    /// <summary>
    /// Validates that all required tables exist and are accessible
    /// </summary>
    public async Task<bool> ValidateTablesAsync()
    {
        var tableNames = new[]
        {
            "quotatracking",
            "instancecoordination", 
            "tokenallocation"
        };

        foreach (var tableName in tableNames)
        {
            try
            {
                var tableClient = new TableClient(ConnectionString, tableName);
                await foreach (var entity in tableClient.QueryAsync<TableEntity>(maxPerPage: 1))
                {
                    // Just checking accessibility, we don't need the entity
                    break;
                }
                Console.WriteLine($"✅ Table '{tableName}' is accessible");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Table '{tableName}' validation failed: {ex.Message}");
                return false;
            }
        }

        return true;
    }

    private async Task InitializeTablesAsync()
    {
        var config = new DynamicRateLimitingConfiguration
        {
            Predictive = new PredictiveSettings
            {
                TableStorage = new TableStorageSettings
                {
                    QuotaTableName = "quotatracking",
                    InstanceTableName = "instancecoordination",
                    TokenTableName = "tokenallocation",
                    AutoCreateTables = true
                }
            }
        };

        var tableStorageFactory = CreateTableStorageFactory(config);

        try
        {
            // Initialize all tables by getting clients (auto-create is enabled)
            await tableStorageFactory.GetQuotaTableClientAsync(CancellationToken.None);
            await tableStorageFactory.GetInstanceTableClientAsync(CancellationToken.None);
            await tableStorageFactory.GetTokenTableClientAsync(CancellationToken.None);

            Console.WriteLine("🏗️  All rate limiting tables initialized");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to initialize tables: {ex.Message}");
            throw;
        }
    }

    private static ILogger<T> CreateLogger<T>()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging(builder => builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Information))
            .BuildServiceProvider();

        return serviceProvider.GetRequiredService<ILogger<T>>();
    }
}

/// <summary>
/// Collection fixture for sharing Azurite instance across multiple test classes
/// </summary>
[CollectionDefinition("Azurite")]
public class AzuriteCollection : ICollectionFixture<AzuriteTestFixture>
{
}