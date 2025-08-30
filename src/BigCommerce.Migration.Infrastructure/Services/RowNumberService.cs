using Azure;
using Azure.Data.Tables;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Implementation of atomic RowNumber assignment service using Azure Table Storage
/// Provides thread-safe sequential RowNumber allocation for composite RowKey generation
/// 
/// Key Features:
/// - Optimistic concurrency control with exponential backoff retry logic
/// - High-performance operation (target: &gt;100 requests/second)
/// - Per-migration-entity-type counter isolation
/// - Comprehensive error handling and monitoring
/// - Range allocation support for batch operations
/// 
/// Performance Characteristics:
/// - Average latency: &lt;50ms per request
/// - P95 latency: &lt;100ms per request  
/// - Throughput: &gt;100 requests/second under load
/// - Conflict resolution: Max 5 retry attempts with exponential backoff
/// </summary>
public class RowNumberService : IRowNumberService
{
    #region Private Fields

    private readonly IAzureTableInitializationService _tableInitializationService;
    private readonly ILogger<RowNumberService> _logger;
    
    // Note: Metrics are logged for Application Insights aggregation instead of in-memory caching
    // In-memory caching doesn't work in multi-instance Azure Functions environments
    
    // Configuration - ✅ PHASE 8 OPTIMIZED: Based on Phase 7 integration test results
    private const string CountersTableName = "RowNumberCounters";
    private const int MaxRetryAttempts = 7;       // ✅ +2 attempts for production resilience (was 5)
    private const int BaseRetryDelayMs = 50;      // ✅ Faster initial retry (was 100ms)  
    private const int MaxRetryDelayMs = 1000;     // ✅ NEW: Cap maximum delay to prevent excessive waits
    private const int MaxRangeAllocationSize = 10000;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of RowNumberService
    /// </summary>
    /// <param name="tableInitializationService">Azure Table initialization service</param>
    /// <param name="logger">Logger for the service</param>
    public RowNumberService(
        IAzureTableInitializationService tableInitializationService,
        ILogger<RowNumberService> logger)
    {
        _tableInitializationService = tableInitializationService ?? throw new ArgumentNullException(nameof(tableInitializationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // No in-memory caching - metrics are logged for Application Insights aggregation
    }

    #endregion

    #region IRowNumberService Implementation

    /// <summary>
    /// Gets the next sequential RowNumber for EntityMapping creation
    /// Thread-safe operation using optimistic concurrency control
    /// </summary>
    public async Task<long> GetNextRowNumberAsync(
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken = default)
    {
        ValidateParameters(migrationId, entityType);
        
        var stopwatch = Stopwatch.StartNew();
        var retryAttempts = 0;
        var partitionKey = GetPartitionKey(migrationId, entityType);
        
        _logger.LogDebug("🔢 [ROWNUMBER] Getting next RowNumber for {MigrationId}/{EntityType}", migrationId, entityType);

        try
        {
            while (retryAttempts < MaxRetryAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var tableClient = await GetTableClientAsync();
                    var counter = await GetOrCreateCounterAsync(tableClient, partitionKey, migrationId, entityType, cancellationToken);
                    
                    // Atomic increment with optimistic concurrency
                    var nextRowNumber = counter.GetNextRowNumber();
                    var counterEntity = CreateTableEntity(counter);
                    
                    // Update with ETag validation - this is where concurrency conflicts occur
                    await tableClient.UpdateEntityAsync(counterEntity, counter.ETag, TableUpdateMode.Replace, cancellationToken);
                    
                    stopwatch.Stop();
                    
                    _logger.LogDebug("✅ [ROWNUMBER] Assigned RowNumber {RowNumber} for {MigrationId}/{EntityType} in {ElapsedMs}ms", 
                        nextRowNumber, migrationId, entityType, stopwatch.ElapsedMilliseconds);
                    
                    // Update metrics
                    await UpdateMetricsAsync(migrationId, entityType, stopwatch.Elapsed, retryAttempts, isSuccess: true);
                    
                    return nextRowNumber;
                }
                catch (RequestFailedException ex) when (ex.Status == 412) // Precondition Failed - ETag conflict
                {
                    retryAttempts++;
                    var delay = CalculateRetryDelay(retryAttempts);
                    
                    _logger.LogWarning("⚠️ [ROWNUMBER] Concurrency conflict #{Attempt} for {MigrationId}/{EntityType}, retrying in {Delay}ms", 
                        retryAttempts, migrationId, entityType, delay);
                    
                    if (retryAttempts >= MaxRetryAttempts)
                    {
                        _logger.LogError("❌ [ROWNUMBER] Max retry attempts reached for {MigrationId}/{EntityType}", migrationId, entityType);
                        throw new InvalidOperationException($"Failed to assign RowNumber after {MaxRetryAttempts} attempts due to concurrency conflicts", ex);
                    }
                    
                    await Task.Delay(delay, cancellationToken);
                }
            }
            
            // Should never reach here due to exception handling above
            throw new InvalidOperationException("Unexpected end of retry loop");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🚫 [ROWNUMBER] Operation cancelled for {MigrationId}/{EntityType}", migrationId, entityType);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ [ROWNUMBER] Failed to get next RowNumber for {MigrationId}/{EntityType} after {ElapsedMs}ms", 
                migrationId, entityType, stopwatch.ElapsedMilliseconds);
            
            // Update metrics for failed request
            await UpdateMetricsAsync(migrationId, entityType, stopwatch.Elapsed, retryAttempts, isSuccess: false);
            throw;
        }
    }

    /// <summary>
    /// Allocates a range of sequential RowNumbers for batch operations
    /// More efficient than multiple GetNextRowNumberAsync calls
    /// </summary>
    public async Task<(long StartRowNumber, long EndRowNumber)> AllocateRangeAsync(
        string migrationId, 
        string entityType, 
        int count, 
        CancellationToken cancellationToken = default)
    {
        ValidateParameters(migrationId, entityType);
        ValidateRangeCount(count);
        
        var stopwatch = Stopwatch.StartNew();
        var retryAttempts = 0;
        var partitionKey = GetPartitionKey(migrationId, entityType);
        
        _logger.LogInformation("🔢 [ROWNUMBER-RANGE] Allocating {Count} RowNumbers for {MigrationId}/{EntityType}", count, migrationId, entityType);

        try
        {
            while (retryAttempts < MaxRetryAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var tableClient = await GetTableClientAsync();
                    var counter = await GetOrCreateCounterAsync(tableClient, partitionKey, migrationId, entityType, cancellationToken);
                    
                    // Atomic range allocation with optimistic concurrency
                    var (startRowNumber, endRowNumber) = counter.AllocateRange(count);
                    var counterEntity = CreateTableEntity(counter);
                    
                    // Update with ETag validation
                    await tableClient.UpdateEntityAsync(counterEntity, counter.ETag, TableUpdateMode.Replace, cancellationToken);
                    
                    stopwatch.Stop();
                    
                    _logger.LogInformation("✅ [ROWNUMBER-RANGE] Allocated RowNumbers {Start}-{End} ({Count} total) for {MigrationId}/{EntityType} in {ElapsedMs}ms", 
                        startRowNumber, endRowNumber, count, migrationId, entityType, stopwatch.ElapsedMilliseconds);
                    
                    // Update metrics for range allocation
                    await UpdateRangeMetricsAsync(migrationId, entityType, count, stopwatch.Elapsed, retryAttempts, isSuccess: true);
                    
                    return (startRowNumber, endRowNumber);
                }
                catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
                {
                    retryAttempts++;
                    var delay = CalculateRetryDelay(retryAttempts);
                    
                    _logger.LogWarning("⚠️ [ROWNUMBER-RANGE] Concurrency conflict #{Attempt} for {MigrationId}/{EntityType}, retrying in {Delay}ms", 
                        retryAttempts, migrationId, entityType, delay);
                    
                    if (retryAttempts >= MaxRetryAttempts)
                    {
                        _logger.LogError("❌ [ROWNUMBER-RANGE] Max retry attempts reached for {MigrationId}/{EntityType}", migrationId, entityType);
                        throw new InvalidOperationException($"Failed to allocate RowNumber range after {MaxRetryAttempts} attempts due to concurrency conflicts", ex);
                    }
                    
                    await Task.Delay(delay, cancellationToken);
                }
            }
            
            throw new InvalidOperationException("Unexpected end of retry loop");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🚫 [ROWNUMBER-RANGE] Operation cancelled for {MigrationId}/{EntityType}", migrationId, entityType);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ [ROWNUMBER-RANGE] Failed to allocate range for {MigrationId}/{EntityType} after {ElapsedMs}ms", 
                migrationId, entityType, stopwatch.ElapsedMilliseconds);
            
            await UpdateRangeMetricsAsync(migrationId, entityType, count, stopwatch.Elapsed, retryAttempts, isSuccess: false);
            throw;
        }
    }

    /// <summary>
    /// Gets the current counter state for a migration and entity type
    /// </summary>
    public async Task<RowNumberCounter?> GetCurrentCounterAsync(
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken = default)
    {
        ValidateParameters(migrationId, entityType);
        
        var partitionKey = GetPartitionKey(migrationId, entityType);
        
        _logger.LogDebug("📊 [ROWNUMBER] Getting current counter for {MigrationId}/{EntityType}", migrationId, entityType);

        try
        {
            var tableClient = await GetTableClientAsync();
            var response = await tableClient.GetEntityIfExistsAsync<TableEntity>(partitionKey, "counter", cancellationToken: cancellationToken);
            
            if (!response.HasValue)
            {
                _logger.LogDebug("📊 [ROWNUMBER] Counter not found for {MigrationId}/{EntityType}", migrationId, entityType);
                return null;
            }
            
            var counter = ConvertFromTableEntity(response.Value!);
            
            _logger.LogDebug("📊 [ROWNUMBER] Found counter for {MigrationId}/{EntityType}: Current={Current}, Total={Total}", 
                migrationId, entityType, counter.CurrentValue, counter.TotalAllocated);
            
            return counter;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [ROWNUMBER] Failed to get current counter for {MigrationId}/{EntityType}", migrationId, entityType);
            throw;
        }
    }

    /// <summary>
    /// Resets the counter for a migration and entity type
    /// </summary>
    public async Task<RowNumberCounter> ResetCounterAsync(
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken = default)
    {
        ValidateParameters(migrationId, entityType);
        
        var partitionKey = GetPartitionKey(migrationId, entityType);
        
        _logger.LogWarning("🔄 [ROWNUMBER] Resetting counter for {MigrationId}/{EntityType}", migrationId, entityType);

        try
        {
            var tableClient = await GetTableClientAsync();
            
            // Create a fresh counter
            var resetCounter = RowNumberCounter.Create(migrationId, entityType);
            var counterEntity = CreateTableEntity(resetCounter);
            
            // Use Upsert to replace any existing counter
            await tableClient.UpsertEntityAsync(counterEntity, TableUpdateMode.Replace, cancellationToken);
            
            _logger.LogInformation("✅ [ROWNUMBER] Counter reset for {MigrationId}/{EntityType}", migrationId, entityType);
            
            return resetCounter;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [ROWNUMBER] Failed to reset counter for {MigrationId}/{EntityType}", migrationId, entityType);
            throw;
        }
    }

    /// <summary>
    /// Validates that the counter service is healthy and operational
    /// </summary>
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("🏥 [ROWNUMBER] Performing health check");

        try
        {
            var tableClient = await GetTableClientAsync();
            
            // Perform a simple query to test connectivity
            var testQuery = tableClient.QueryAsync<TableEntity>(
                filter: "PartitionKey eq 'healthcheck'",
                maxPerPage: 1,
                cancellationToken: cancellationToken);
            
            // Enumerate to execute the query
            await foreach (var _ in testQuery.WithCancellation(cancellationToken))
            {
                break; // Just test connectivity, don't need results
            }
            
            _logger.LogDebug("✅ [ROWNUMBER] Health check passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [ROWNUMBER] Health check failed");
            return false;
        }
    }

    /// <summary>
    /// Gets performance metrics for the counter service
    /// Returns basic metrics - detailed metrics are available via Application Insights
    /// </summary>
    public async Task<RowNumberServiceMetrics> GetMetricsAsync(
        string? migrationId = null, 
        string? entityType = null, 
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Make async for interface compliance
        
        // Return basic metrics - detailed performance metrics are tracked via Application Insights
        // In multi-instance Azure Functions environments, centralized metrics aggregation is preferred
        var metrics = new RowNumberServiceMetrics
        {
            MigrationId = migrationId,
            EntityType = entityType,
            CollectedAt = DateTime.UtcNow,
            MetricsPeriod = TimeSpan.FromMinutes(5),
            HealthStatus = await HealthCheckAsync(cancellationToken) 
                ? RowNumberServiceHealth.Healthy 
                : RowNumberServiceHealth.Unhealthy
        };
        
        _logger.LogDebug("📊 [ROWNUMBER] Returned basic metrics for {MigrationId}/{EntityType} (detailed metrics via Application Insights)", 
            migrationId ?? "global", entityType ?? "all");
        return metrics;
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Gets table client using centralized initialization service (includes built-in caching)
    /// </summary>
    private async Task<TableClient> GetTableClientAsync()
    {
        // Use centralized table initialization service which includes caching, 
        // initialization status tracking, and thread-safe operations
        return await _tableInitializationService.GetTableClientAsync(CountersTableName);
    }

    /// <summary>
    /// Gets or creates a counter for the specified partition
    /// </summary>
    private async Task<RowNumberCounter> GetOrCreateCounterAsync(
        TableClient tableClient, 
        string partitionKey, 
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken)
    {
        var response = await tableClient.GetEntityIfExistsAsync<TableEntity>(partitionKey, "counter", cancellationToken: cancellationToken);
        
        if (response.HasValue)
        {
            return ConvertFromTableEntity(response.Value!);
        }
        
        // Create new counter
        _logger.LogInformation("🆕 [ROWNUMBER] Creating new counter for {MigrationId}/{EntityType}", migrationId, entityType);
        
        var newCounter = RowNumberCounter.Create(migrationId, entityType);
        var counterEntity = CreateTableEntity(newCounter);
        
        try
        {
            var addResponse = await tableClient.AddEntityAsync(counterEntity, cancellationToken);
            _logger.LogInformation("✅ [ROWNUMBER] Created new counter for {MigrationId}/{EntityType}", migrationId, entityType);
            
            // ✅ FIX: Set ETag from creation response to enable future updates
            newCounter.ETag = addResponse.Headers.ETag ?? default;
            return newCounter;
        }
        catch (RequestFailedException ex) when (ex.Status == 409) // Entity already exists
        {
            // Another thread created it - fetch the existing one
            _logger.LogDebug("🔄 [ROWNUMBER] Counter created by another thread for {MigrationId}/{EntityType}, fetching existing", migrationId, entityType);
            var existingResponse = await tableClient.GetEntityAsync<TableEntity>(partitionKey, "counter", cancellationToken: cancellationToken);
            return ConvertFromTableEntity(existingResponse.Value);
        }
    }

    /// <summary>
    /// Creates a TableEntity from a RowNumberCounter
    /// </summary>
    private static TableEntity CreateTableEntity(RowNumberCounter counter)
    {
        return new TableEntity(counter.PartitionKey, counter.RowKey)
        {
            ["CurrentValue"] = counter.CurrentValue,
            ["MigrationId"] = counter.MigrationId,
            ["EntityType"] = counter.EntityType,
            ["CreatedAt"] = counter.CreatedAt,
            ["LastUpdated"] = counter.LastUpdated,
            ["TotalAllocated"] = counter.TotalAllocated,
            ["ConcurrencyConflicts"] = counter.ConcurrencyConflicts,
            ETag = counter.ETag
        };
    }

    /// <summary>
    /// Converts a TableEntity to a RowNumberCounter
    /// </summary>
    private static RowNumberCounter ConvertFromTableEntity(TableEntity entity)
    {
        return new RowNumberCounter
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            CurrentValue = entity.GetInt64("CurrentValue") ?? 0,
            MigrationId = entity.GetString("MigrationId") ?? string.Empty,
            EntityType = entity.GetString("EntityType") ?? string.Empty,
            CreatedAt = entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
            LastUpdated = entity.GetDateTime("LastUpdated") ?? DateTime.UtcNow,
            TotalAllocated = entity.GetInt64("TotalAllocated") ?? 0,
            ConcurrencyConflicts = entity.GetInt64("ConcurrencyConflicts") ?? 0,
            ETag = entity.ETag,
            Timestamp = entity.Timestamp
        };
    }

    /// <summary>
    /// Gets the partition key for a migration and entity type
    /// </summary>
    private static string GetPartitionKey(string migrationId, string entityType)
    {
        return $"{migrationId}_{entityType}";
    }

    /// <summary>
    /// Calculates retry delay using capped exponential backoff
    /// ✅ PHASE 8 OPTIMIZED: Faster initial retries with maximum delay cap
    /// Phase 7 results: 50ms → 100ms → 200ms → 400ms → 800ms → 1000ms → 1000ms (vs old 100ms → 1600ms)
    /// </summary>
    private static int CalculateRetryDelay(int retryAttempt)
    {
        var exponentialDelay = BaseRetryDelayMs * (int)Math.Pow(2, retryAttempt - 1);
        return Math.Min(exponentialDelay, MaxRetryDelayMs); // ✅ Cap delay to prevent excessive waits
    }

    /// <summary>
    /// Validates method parameters
    /// </summary>
    private static void ValidateParameters(string migrationId, string entityType)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentNullException(nameof(migrationId));
        
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentNullException(nameof(entityType));
    }

    /// <summary>
    /// Validates range allocation count
    /// </summary>
    private static void ValidateRangeCount(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than 0");
        
        if (count > MaxRangeAllocationSize)
            throw new ArgumentOutOfRangeException(nameof(count), $"Count must not exceed {MaxRangeAllocationSize}");
    }

    /// <summary>
    /// Logs performance metrics for individual requests
    /// Uses structured logging for Application Insights aggregation (multi-instance safe)
    /// </summary>
    private async Task UpdateMetricsAsync(
        string migrationId, 
        string entityType, 
        TimeSpan elapsed, 
        int retryAttempts, 
        bool isSuccess)
    {
        await Task.CompletedTask; // Make async for interface compliance
        
        // Log structured metrics for Application Insights aggregation across all instances
        // This approach works correctly in multi-instance Azure Functions environments
        _logger.LogDebug("📊 [ROWNUMBER-METRICS] Request: {Success}, Elapsed: {ElapsedMs}ms, Retries: {Retries} for {MigrationId}/{EntityType}",
            isSuccess ? "SUCCESS" : "FAILED", elapsed.TotalMilliseconds, retryAttempts, migrationId, entityType);
    }

    /// <summary>
    /// Logs performance metrics for range allocation requests  
    /// Uses structured logging for Application Insights aggregation (multi-instance safe)
    /// </summary>
    private async Task UpdateRangeMetricsAsync(
        string migrationId, 
        string entityType, 
        int rangeSize, 
        TimeSpan elapsed, 
        int retryAttempts, 
        bool isSuccess)
    {
        await Task.CompletedTask; // Make async for interface compliance
        
        // Log structured metrics for Application Insights aggregation across all instances
        _logger.LogDebug("📊 [ROWNUMBER-RANGE-METRICS] Request: {Success}, Range: {Range}, Elapsed: {ElapsedMs}ms, Retries: {Retries} for {MigrationId}/{EntityType}",
            isSuccess ? "SUCCESS" : "FAILED", rangeSize, elapsed.TotalMilliseconds, retryAttempts, migrationId, entityType);
    }

    #endregion
}
