using Azure.Data.Tables;
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
    private readonly IAzureTableInitializationService _tableInitializationService;
    private readonly ILogger<RateLimitingTableStorageFactory> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the RateLimitingTableStorageFactory
    /// </summary>
    /// <param name="tableInitializationService">Centralized table initialization service</param>
    /// <param name="rateLimitingConfig">Rate limiting configuration</param>
    /// <param name="logger">Logger instance</param>
    public RateLimitingTableStorageFactory(
        IAzureTableInitializationService tableInitializationService,
        IOptions<DynamicRateLimitingConfiguration> rateLimitingConfig,
        ILogger<RateLimitingTableStorageFactory> logger)
    {
        _tableInitializationService = tableInitializationService ?? throw new ArgumentNullException(nameof(tableInitializationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = rateLimitingConfig?.Value ?? throw new ArgumentNullException(nameof(rateLimitingConfig));
        
        _logger.LogInformation("✅ RateLimitingTableStorageFactory initialized with centralized table management for: {QuotaTable}, {InstanceTable}, {TokenTable}",
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
        var tableNames = new[]
        {
            _configuration.Predictive.TableStorage.QuotaTableName,
            _configuration.Predictive.TableStorage.InstanceTableName,
            _configuration.Predictive.TableStorage.TokenTableName
        };

        var healthStatus = await _tableInitializationService.GetAllTablesHealthAsync(cancellationToken);
        
        return tableNames.All(tableName => healthStatus.GetValueOrDefault(tableName, false));
    }

    /// <inheritdoc />
    public async Task EnsureTablesExistAsync(CancellationToken cancellationToken = default)
    {
        if (!_configuration.Predictive.TableStorage.AutoCreateTables)
        {
            _logger.LogInformation("Auto-create tables is disabled, skipping table creation");
            return;
        }

        var tableNames = new[]
        {
            _configuration.Predictive.TableStorage.QuotaTableName,
            _configuration.Predictive.TableStorage.InstanceTableName,
            _configuration.Predictive.TableStorage.TokenTableName
        };

        _logger.LogInformation("🏗️ Ensuring rate limiting tables exist: {TableNames}", string.Join(", ", tableNames));

        // Use centralized service to create all tables efficiently
        await _tableInitializationService.GetTableClientsAsync(tableNames, cancellationToken);

        _logger.LogInformation("✅ Successfully ensured all rate limiting tables exist");
    }

    /// <summary>
    /// Gets a table client using the centralized table initialization service
    /// </summary>
    private async Task<TableClient> GetTableClientAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (!_configuration.Predictive.TableStorage.AutoCreateTables)
        {
            // If auto-create is disabled, we still need to get a client but won't create the table
            _logger.LogWarning("⚠️ Auto-create tables is disabled for {TableName} - table must exist or operations will fail", tableName);
        }
        
        return await _tableInitializationService.GetTableClientAsync(tableName, cancellationToken);
    }
}