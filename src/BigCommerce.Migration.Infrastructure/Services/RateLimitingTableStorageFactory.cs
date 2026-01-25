using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Factory for creating rate limiting Table Storage clients
/// Follows existing MigrationStorageService and AzureTableDistributedLockService patterns
/// </summary>
public class RateLimitingTableStorageFactory : IRateLimitingTableStorageFactory
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<RateLimitingTableStorageFactory> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the RateLimitingTableStorageFactory
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <param name="rateLimitingConfig">Rate limiting configuration</param>
    /// <param name="logger">Logger instance</param>
    public RateLimitingTableStorageFactory(
        IConfiguration configuration,
        IOptions<DynamicRateLimitingConfiguration> rateLimitingConfig,
        ILogger<RateLimitingTableStorageFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = rateLimitingConfig?.Value ?? throw new ArgumentNullException(nameof(rateLimitingConfig));
        _ = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Follow existing pattern: Try ConnectionStrings section first, then fall back to Values section
        var connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
            ?? configuration["AzureWebJobsStorage"]
            ?? throw new ArgumentNullException("AzureWebJobsStorage connection string is required");

        _tableServiceClient = new TableServiceClient(connectionString);
        
        _logger.LogInformation("Initialized RateLimitingTableStorageFactory with tables: {QuotaTable}, {InstanceTable}, {TokenTable}",
            _configuration.Predictive.TableStorage.QuotaTableName,
            _configuration.Predictive.TableStorage.InstanceTableName,
            _configuration.Predictive.TableStorage.TokenTableName);
    }

    /// <inheritdoc />
    public async Task<TableClient> GetQuotaTableClientAsync(CancellationToken cancellationToken = default)
    {
        return await GetTableClientAsync(_configuration.Predictive.TableStorage.QuotaTableName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TableClient> GetInstanceTableClientAsync(CancellationToken cancellationToken = default)
    {
        return await GetTableClientAsync(_configuration.Predictive.TableStorage.InstanceTableName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TableClient> GetTokenTableClientAsync(CancellationToken cancellationToken = default)
    {
        return await GetTableClientAsync(_configuration.Predictive.TableStorage.TokenTableName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> AllTablesExistAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Use TableServiceClient to check if tables exist
            var tableNames = new[]
            {
                _configuration.Predictive.TableStorage.QuotaTableName,
                _configuration.Predictive.TableStorage.InstanceTableName,
                _configuration.Predictive.TableStorage.TokenTableName
            };

            await foreach (var table in _tableServiceClient.QueryAsync(cancellationToken: cancellationToken))
            {
                if (tableNames.Contains(table.Name))
                {
                    tableNames = tableNames.Where(name => name != table.Name).ToArray();
                    if (tableNames.Length == 0)
                        return true; // All tables exist
                }
            }

            return false; // Some tables don't exist
        }
        catch
        {
            return false; // Any exception means we can't determine table existence
        }
    }

    /// <inheritdoc />
    public async Task EnsureTablesExistAsync(CancellationToken cancellationToken = default)
    {
        if (!_configuration.Predictive.TableStorage.AutoCreateTables)
        {
            _logger.LogInformation("Auto-create tables is disabled, skipping table creation");
            return;
        }

        try
        {
            _logger.LogInformation("Ensuring rate limiting tables exist...");

            // Create all tables in parallel for better performance
            var quotaTask = CreateTableIfNotExistsAsync(_configuration.Predictive.TableStorage.QuotaTableName, cancellationToken);
            var instanceTask = CreateTableIfNotExistsAsync(_configuration.Predictive.TableStorage.InstanceTableName, cancellationToken);
            var tokenTask = CreateTableIfNotExistsAsync(_configuration.Predictive.TableStorage.TokenTableName, cancellationToken);

            await Task.WhenAll(quotaTask, instanceTask, tokenTask);

            _logger.LogInformation("Successfully ensured all rate limiting tables exist");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure rate limiting tables exist");
            throw;
        }
    }

    /// <summary>
    /// Gets a table client with auto-creation following existing MigrationStorageService pattern
    /// </summary>
    private async Task<TableClient> GetTableClientAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var tableClient = _tableServiceClient.GetTableClient(tableName);
        
        if (_configuration.Predictive.TableStorage.AutoCreateTables)
        {
            await tableClient.CreateIfNotExistsAsync(cancellationToken);
        }
        
        return tableClient;
    }

    /// <summary>
    /// Creates a table if it doesn't exist with proper error handling
    /// </summary>
    private async Task CreateTableIfNotExistsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(tableName);
            var response = await tableClient.CreateIfNotExistsAsync(cancellationToken);
            
            if (response?.Value != null)
            {
                _logger.LogInformation("Created rate limiting table: {TableName}", tableName);
            }
            else
            {
                _logger.LogDebug("Rate limiting table already exists: {TableName}", tableName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create rate limiting table: {TableName}", tableName);
            throw;
        }
    }
}