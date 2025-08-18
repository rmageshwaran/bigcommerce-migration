using Azure;
using Azure.Data.Tables;
using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Centralized service for initializing Azure Tables with Azurite compatibility and robust error handling
/// </summary>
public class AzureTableInitializationService : IAzureTableInitializationService, IDisposable
{
    #region Private Fields

    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<AzureTableInitializationService> _logger;
    
    // Cache for initialized table clients to avoid expensive CreateIfNotExistsAsync calls
    private readonly ConcurrentDictionary<string, TableClient> _cachedTableClients = new();
    private readonly ConcurrentDictionary<string, bool> _tableInitializationStatus = new();
    
    // Semaphore per table to prevent concurrent initialization of the same table
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tableInitializationLocks = new();
    
    // Retry configuration for Azurite compatibility
    private static readonly TimeSpan[] RetryDelays = 
    {
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1)
    };

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the AzureTableInitializationService
    /// </summary>
    /// <param name="configuration">Application configuration for Azure storage connection</param>
    /// <param name="logger">Logger instance for monitoring and debugging</param>
    public AzureTableInitializationService(IConfiguration configuration, ILogger<AzureTableInitializationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
        
        // Get Azure Storage connection string
        var connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
            ?? configuration["AzureWebJobsStorage"]
            ?? throw new ArgumentNullException("AzureWebJobsStorage connection string is required");
        
        _tableServiceClient = new TableServiceClient(connectionString);
        
        _logger.LogInformation("🏗️ AzureTableInitializationService initialized with Azure Storage");
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task<TableClient> GetTableClientAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName))
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));

        // Return cached client if table is already initialized
        if (_tableInitializationStatus.GetValueOrDefault(tableName, false) && 
            _cachedTableClients.TryGetValue(tableName, out var cachedClient))
        {
            return cachedClient;
        }

        // Get or create semaphore for this specific table
        var semaphore = _tableInitializationLocks.GetOrAdd(tableName, _ => new SemaphoreSlim(1, 1));
        
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            // Double-check pattern: another thread might have initialized while we waited
            if (_tableInitializationStatus.GetValueOrDefault(tableName, false) && 
                _cachedTableClients.TryGetValue(tableName, out var doubleCheckClient))
            {
                return doubleCheckClient;
            }

            return await InitializeTableWithRetryAsync(tableName, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, TableClient>> GetTableClientsAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (tableNames == null)
            throw new ArgumentNullException(nameof(tableNames));

        var tableNamesList = tableNames.ToList();
        if (!tableNamesList.Any())
            return new Dictionary<string, TableClient>();

        _logger.LogInformation("🏗️ Initializing {Count} tables: {TableNames}", 
            tableNamesList.Count, string.Join(", ", tableNamesList));

        var results = new Dictionary<string, TableClient>();
        var tasks = tableNamesList.Select(async tableName =>
        {
            try
            {
                var client = await GetTableClientAsync(tableName, cancellationToken);
                return new { TableName = tableName, Client = (TableClient?)client, Success = true, Error = (Exception?)null };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize table {TableName}", tableName);
                return new { TableName = tableName, Client = (TableClient?)null, Success = false, Error = (Exception?)ex };
            }
        });

        var completedTasks = await Task.WhenAll(tasks);
        
        foreach (var result in completedTasks)
        {
            if (result.Success && result.Client != null)
            {
                results[result.TableName] = result.Client;
            }
            else if (result.Error != null)
            {
                throw new InvalidOperationException($"Failed to initialize table {result.TableName}: {result.Error.Message}", result.Error);
            }
        }

        _logger.LogInformation("✅ Successfully initialized {Count}/{Total} tables", 
            results.Count, tableNamesList.Count);
        
        return results;
    }

    /// <inheritdoc />
    public async Task<bool> IsTableHealthyAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName))
            return false;

        try
        {
            var tableClient = await GetTableClientAsync(tableName, cancellationToken);
            
            // Try a minimal query to verify table accessibility
            var query = tableClient.QueryAsync<TableEntity>(
                filter: "PartitionKey eq 'health-check-non-existent'",
                maxPerPage: 1,
                cancellationToken: cancellationToken);

            await foreach (var _ in query.WithCancellation(cancellationToken))
            {
                break; // Just need to verify we can query
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Health check failed for table {TableName}: {Error}", tableName, ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, bool>> GetAllTablesHealthAsync(CancellationToken cancellationToken = default)
    {
        var healthStatus = new Dictionary<string, bool>();
        var tableNames = _cachedTableClients.Keys.ToList();

        if (!tableNames.Any())
        {
            _logger.LogDebug("No tables have been initialized yet");
            return healthStatus;
        }

        _logger.LogDebug("🔍 Checking health of {Count} tables: {TableNames}", 
            tableNames.Count, string.Join(", ", tableNames));

        var healthTasks = tableNames.Select(async tableName =>
        {
            var isHealthy = await IsTableHealthyAsync(tableName, cancellationToken);
            return new { TableName = tableName, IsHealthy = isHealthy };
        });

        var results = await Task.WhenAll(healthTasks);
        
        foreach (var result in results)
        {
            healthStatus[result.TableName] = result.IsHealthy;
        }

        var healthyCount = healthStatus.Values.Count(h => h);
        _logger.LogInformation("📊 Table health check complete: {HealthyCount}/{TotalCount} tables healthy", 
            healthyCount, healthStatus.Count);

        return healthStatus;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Initializes a table with retry logic for Azurite compatibility
    /// </summary>
    private async Task<TableClient> InitializeTableWithRetryAsync(string tableName, CancellationToken cancellationToken)
    {
        var tableClient = _tableServiceClient.GetTableClient(tableName);
        Exception? lastException = null;

        for (int attempt = 0; attempt < RetryDelays.Length + 1; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    _logger.LogWarning("🔄 AZURITE-RETRY: Table creation attempt {Attempt} for {TableName}", attempt + 1, tableName);
                    await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
                }
                
                await tableClient.CreateIfNotExistsAsync(cancellationToken);
                _logger.LogInformation("✅ Table {TableName} initialized successfully after {Attempts} attempts", tableName, attempt + 1);
                
                // 🔄 AZURITE FIX: Small delay to ensure table is fully ready after creation
                if (attempt > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                }
                
                // Cache the successful client and mark as initialized
                _cachedTableClients[tableName] = tableClient;
                _tableInitializationStatus[tableName] = true;
                
                return tableClient;
            }
            catch (RequestFailedException ex) when (IsTransientError(ex) && attempt < RetryDelays.Length)
            {
                lastException = ex;
                _logger.LogWarning(ex, "⚠️ AZURITE-RETRY: Transient error on attempt {Attempt} for table {TableName}: {Error}", 
                    attempt + 1, tableName, ex.Message);
                continue;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                lastException = ex;
                _logger.LogWarning(ex, "⚠️ AZURITE-RETRY: Error on attempt {Attempt} for table {TableName}: {Error}", 
                    attempt + 1, tableName, ex.Message);
                continue;
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }
        
        // If we get here, all attempts failed
        _logger.LogError(lastException, "❌ CRITICAL: Failed to initialize table {TableName} after {Attempts} attempts", 
            tableName, RetryDelays.Length + 1);
        
        // 🔄 CRITICAL: Reset cache state on failure
        _cachedTableClients.TryRemove(tableName, out _);
        _tableInitializationStatus[tableName] = false;
        
        throw new InvalidOperationException($"Failed to initialize table {tableName} after {RetryDelays.Length + 1} attempts: {lastException?.Message}", lastException);
    }

    /// <summary>
    /// Determines if an exception is transient and should be retried
    /// </summary>
    private static bool IsTransientError(RequestFailedException ex)
    {
        return ex.Status switch
        {
            408 => true,  // Request Timeout
            429 => true,  // Too Many Requests
            500 => true,  // Internal Server Error
            502 => true,  // Bad Gateway
            503 => true,  // Service Unavailable
            504 => true,  // Gateway Timeout
            _ => false
        };
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes of resources used by the AzureTableInitializationService
    /// </summary>
    public void Dispose()
    {
        foreach (var semaphore in _tableInitializationLocks.Values)
        {
            semaphore?.Dispose();
        }
        _tableInitializationLocks.Clear();
        
        _cachedTableClients.Clear();
        _tableInitializationStatus.Clear();
        
        GC.SuppressFinalize(this);
    }

    #endregion
}