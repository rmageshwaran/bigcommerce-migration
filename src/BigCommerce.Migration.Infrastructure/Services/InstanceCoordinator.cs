using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
#pragma warning disable CS8602 // Dereference of a possibly null reference
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type

using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Instance coordinator for multi-instance rate limiting coordination
/// Implements heartbeat system, active instance discovery, and phantom cleanup
/// </summary>
public class InstanceCoordinator : IInstanceCoordinator
{
    private readonly IRateLimitingTableStorageFactory _tableStorageFactory;
    private readonly ILogger<InstanceCoordinator> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;
    private readonly string _instanceId;
    private readonly object _lockObject = new object();
    
    // Instance registration tracking
    private readonly Dictionary<string, DateTimeOffset> _lastHeartbeats = new();
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

    /// <summary>
    /// Gets the unique instance identifier for this coordinator
    /// </summary>
    public string InstanceId => _instanceId;

    /// <summary>
    /// Initializes a new instance of the InstanceCoordinator
    /// </summary>
    public InstanceCoordinator(
        IRateLimitingTableStorageFactory tableStorageFactory,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<InstanceCoordinator> logger)
    {
        _tableStorageFactory = tableStorageFactory ?? throw new ArgumentNullException(nameof(tableStorageFactory));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Generate stable instance ID: MachineName-ProcessId-StartTime
        _instanceId = InstanceCoordinationEntity.GenerateInstanceId();
        
        _logger.LogInformation("Initialized InstanceCoordinator with ID: {InstanceId}", _instanceId);
    }

    /// <inheritdoc />
    public async Task RegisterInstanceAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetInstanceTableClientAsync(cancellationToken);
            var entity = InstanceCoordinationEntity.CreateNew(_instanceId, storeId);

            // Use AddEntity for initial registration (creates if not exists)
            await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
            
            lock (_lockObject)
            {
                _lastHeartbeats[storeId] = DateTimeOffset.UtcNow;
            }

            _logger.LogInformation("Registered instance {InstanceId} for store {StoreId} " +
                                   "(Machine: {MachineName}, PID: {ProcessId})",
                _instanceId, storeId, Environment.MachineName, Environment.ProcessId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register instance {InstanceId} for store {StoreId}", 
                _instanceId, storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SendHeartbeatAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        // Throttling: Only send heartbeat if enough time has passed
        lock (_lockObject)
        {
            if (_lastHeartbeats.TryGetValue(storeId, out var lastHeartbeat))
            {
                var timeSinceLastHeartbeat = DateTimeOffset.UtcNow - lastHeartbeat;
                var heartbeatInterval = TimeSpan.FromSeconds(_configuration.Predictive.HeartbeatIntervalSeconds);
                
                if (timeSinceLastHeartbeat < heartbeatInterval)
                {
                    _logger.LogTrace("Heartbeat throttled for store {StoreId} (last: {LastHeartbeat})", 
                        storeId, timeSinceLastHeartbeat);
                    return; // Throttle heartbeat
                }
            }
        }

        const int maxRetries = 3;
        var baseDelay = 100; // Base delay in milliseconds
        var random = new Random();

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var tableClient = await _tableStorageFactory.GetInstanceTableClientAsync(cancellationToken);

                // Get existing entity for ETag-based update
                var existingResponse = await tableClient.GetEntityIfExistsAsync<InstanceCoordinationEntity>(
                    storeId, _instanceId, cancellationToken: cancellationToken);

                InstanceCoordinationEntity entity;
                bool isNewEntity = false;

                if (existingResponse.HasValue)
                {
                    entity = existingResponse.Value;
                    entity.UpdateHeartbeat();
                }
                else
                {
                    // Entity doesn't exist - create new one
                    entity = InstanceCoordinationEntity.CreateNew(_instanceId, storeId);
                    isNewEntity = true;
                }

                // Atomic heartbeat update
                if (isNewEntity)
                {
                    await tableClient.AddEntityAsync(entity, cancellationToken);
                }
                else
                {
                    await tableClient.UpdateEntityAsync(entity, entity.ETag, cancellationToken: cancellationToken);
                }

                // Update local tracking
                lock (_lockObject)
                {
                    _lastHeartbeats[storeId] = DateTimeOffset.UtcNow;
                }

                _logger.LogTrace("Sent heartbeat for instance {InstanceId} on store {StoreId}", 
                    _instanceId, storeId);
                return; // Success
            }
            catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
            {
                // Another thread updated simultaneously - retry with backoff
                var jitter = random.Next(0, 100);
                var delay = (int)(baseDelay * Math.Pow(1.5, retry)) + jitter;
                
                _logger.LogDebug("ETag conflict on heartbeat for store {StoreId}, retry {Retry}/{Max} (delay: {Delay}ms)",
                    storeId, retry + 1, maxRetries, delay);

                if (retry < maxRetries - 1)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send heartbeat for store {StoreId} on attempt {Retry}/{Max}",
                    storeId, retry + 1, maxRetries);
                
                if (retry == maxRetries - 1)
                {
                    throw;
                }
            }
        }

        _logger.LogWarning("Failed to send heartbeat for store {StoreId} after {Retries} retries due to ETag conflicts",
            storeId, maxRetries);
    }

    /// <inheritdoc />
    public async Task<List<string>> GetActiveInstanceIdsAsync(string storeId, TimeSpan? timeoutPeriod = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        var timeout = timeoutPeriod ?? TimeSpan.FromSeconds(_configuration.Predictive.InstanceTimeoutSeconds);
        var activeInstances = await GetActiveInstancesAsync(storeId, timeout, cancellationToken);
        
        return activeInstances.Select(i => i.RowKey).ToList();
    }

    /// <inheritdoc />
    public async Task<int> GetActiveInstanceCountAsync(string storeId, TimeSpan? timeoutPeriod = null, CancellationToken cancellationToken = default)
    {
        var activeInstanceIds = await GetActiveInstanceIdsAsync(storeId, timeoutPeriod, cancellationToken);
        return activeInstanceIds.Count;
    }

    /// <inheritdoc />
    public async Task<List<InstanceCoordinationEntity>> GetActiveInstancesAsync(string storeId, TimeSpan? timeoutPeriod = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetInstanceTableClientAsync(cancellationToken);
            var timeout = timeoutPeriod ?? TimeSpan.FromSeconds(_configuration.Predictive.InstanceTimeoutSeconds);
            var cutoffTime = DateTimeOffset.UtcNow - timeout;

            // Query all instances for this store
            var query = tableClient.QueryAsync<InstanceCoordinationEntity>(
                filter: $"PartitionKey eq '{storeId}'",
                cancellationToken: cancellationToken);

            var activeInstances = new List<InstanceCoordinationEntity>();

            await foreach (var instance in query)
            {
                if (instance.IsActive(timeout))
                {
                    activeInstances.Add(instance);
                }
            }

            _logger.LogDebug("Found {ActiveCount} active instances for store {StoreId} (timeout: {Timeout})",
                activeInstances.Count, storeId, timeout);

            return activeInstances;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active instances for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupStaleInstancesAsync(string storeId, TimeSpan? timeoutPeriod = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        // Throttle cleanup: Only run once per coordination health check interval
        var cleanupInterval = TimeSpan.FromSeconds(_configuration.Predictive.CoordinationHealthCheckSeconds);
        lock (_lockObject)
        {
            if (DateTimeOffset.UtcNow - _lastCleanup < cleanupInterval)
            {
                _logger.LogTrace("Cleanup throttled for store {StoreId}", storeId);
                return 0;
            }
            _lastCleanup = DateTimeOffset.UtcNow;
        }

        try
        {
            var tableClient = await _tableStorageFactory.GetInstanceTableClientAsync(cancellationToken);
            var timeout = timeoutPeriod ?? TimeSpan.FromSeconds(_configuration.Predictive.InstanceTimeoutSeconds);
            var cutoffTime = DateTimeOffset.UtcNow - timeout;

            // Query all instances for this store
            var query = tableClient.QueryAsync<InstanceCoordinationEntity>(
                filter: $"PartitionKey eq '{storeId}'",
                cancellationToken: cancellationToken);

            var staleInstances = new List<InstanceCoordinationEntity>();

            await foreach (var instance in query)
            {
                if (!instance.IsActive(timeout))
                {
                    staleInstances.Add(instance);
                }
            }

            // Delete stale instances in parallel (but limited concurrency)
            var deleteTasks = staleInstances.Select(async instance =>
            {
                try
                {
                    await tableClient.DeleteEntityAsync(instance.PartitionKey, instance.RowKey, 
                        instance.ETag, cancellationToken);
                    
                    _logger.LogDebug("Cleaned up stale instance {InstanceId} for store {StoreId} " +
                                     "(last heartbeat: {LastHeartbeat})",
                        instance.RowKey, storeId, instance.LastHeartbeat);
                    return 1;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Instance already deleted - that's fine
                    return 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup stale instance {InstanceId} for store {StoreId}",
                        instance.RowKey, storeId);
                    return 0;
                }
            });

            var results = await Task.WhenAll(deleteTasks);
            var cleanedCount = results.Sum();

            if (cleanedCount > 0)
            {
                _logger.LogInformation("Cleaned up {CleanedCount} stale instances for store {StoreId}", 
                    cleanedCount, storeId);
            }

            return cleanedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup stale instances for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UnregisterInstanceAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetInstanceTableClientAsync(cancellationToken);
            
            await tableClient.DeleteEntityAsync(storeId, _instanceId, ETag.All, cancellationToken);
            
            lock (_lockObject)
            {
                _lastHeartbeats.Remove(storeId);
            }

            _logger.LogInformation("Unregistered instance {InstanceId} from store {StoreId}", 
                _instanceId, storeId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Instance already removed - that's fine
            _logger.LogDebug("Instance {InstanceId} already unregistered from store {StoreId}", 
                _instanceId, storeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unregister instance {InstanceId} from store {StoreId}", 
                _instanceId, storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RecordRequestProcessedAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetInstanceTableClientAsync(cancellationToken);

            // Get existing entity for atomic update
            var existingResponse = await tableClient.GetEntityIfExistsAsync<InstanceCoordinationEntity>(
                storeId, _instanceId, cancellationToken: cancellationToken);

            if (!existingResponse.HasValue)
            {
                _logger.LogDebug("Instance {InstanceId} not registered for store {StoreId} - cannot record request",
                    _instanceId, storeId);
                return;
            }

            var entity = existingResponse.Value;
            entity.RecordRequestProcessed();

            await tableClient.UpdateEntityAsync(entity, entity.ETag, cancellationToken: cancellationToken);
            
            _logger.LogTrace("Recorded request processed for instance {InstanceId} on store {StoreId} " +
                             "(total: {TotalRequests})",
                _instanceId, storeId, entity.TotalRequestsProcessed);
        }
        catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
        {
            // Another thread updated simultaneously - acceptable loss for performance metrics
            _logger.LogTrace("ETag conflict recording request for instance {InstanceId} on store {StoreId}",
                _instanceId, storeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record request processed for instance {InstanceId} on store {StoreId}",
                _instanceId, storeId);
            // Don't throw - this is not critical for rate limiting functionality
        }
    }

    /// <inheritdoc />
    public async Task UpdateLoadFactorAsync(string storeId, double loadFactor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetInstanceTableClientAsync(cancellationToken);

            // Get existing entity for atomic update
            var existingResponse = await tableClient.GetEntityIfExistsAsync<InstanceCoordinationEntity>(
                storeId, _instanceId, cancellationToken: cancellationToken);

            if (!existingResponse.HasValue)
            {
                _logger.LogDebug("Instance {InstanceId} not registered for store {StoreId} - cannot update load factor",
                    _instanceId, storeId);
                return;
            }

            var entity = existingResponse.Value;
            entity.UpdateLoadFactor(loadFactor);

            await tableClient.UpdateEntityAsync(entity, entity.ETag, cancellationToken: cancellationToken);
            
            _logger.LogTrace("Updated load factor for instance {InstanceId} on store {StoreId}: {LoadFactor:F2}",
                _instanceId, storeId, loadFactor);
        }
        catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
        {
            // Another thread updated simultaneously - acceptable loss for performance metrics
            _logger.LogTrace("ETag conflict updating load factor for instance {InstanceId} on store {StoreId}",
                _instanceId, storeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update load factor for instance {InstanceId} on store {StoreId}",
                _instanceId, storeId);
            // Don't throw - this is not critical for rate limiting functionality
        }
    }

    /// <inheritdoc />
    public async Task<CoordinationHealthMetrics> GetCoordinationHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var activeInstances = await GetActiveInstancesAsync(storeId, cancellationToken: cancellationToken);
            var totalInstances = activeInstances.Count;
            
            if (totalInstances == 0)
            {
                return new CoordinationHealthMetrics
                {
                    StoreId = storeId,
                    ActiveInstanceCount = 0,
                    TotalRequestsProcessed = 0,
                    AverageLoadFactor = 0.0,
                    HealthStatus = "Critical",
                    LastHeartbeatAge = TimeSpan.MaxValue,
                    CoordinationEfficiency = 0.0
                };
            }

            var totalRequests = activeInstances.Sum(i => i.TotalRequestsProcessed);
            var averageLoadFactor = activeInstances.Average(i => i.CurrentLoadFactor);
            var oldestHeartbeat = activeInstances.Max(i => DateTimeOffset.UtcNow - i.LastHeartbeat);

            // Calculate coordination efficiency based on load distribution
            var loadFactors = activeInstances.Select(i => i.CurrentLoadFactor).ToList();
            var loadVariance = CalculateVariance(loadFactors);
            var coordinationEfficiency = Math.Max(0.0, 1.0 - (loadVariance * 2)); // Higher variance = lower efficiency

            var healthStatus = DetermineHealthStatus(totalInstances, oldestHeartbeat, coordinationEfficiency);

            return new CoordinationHealthMetrics
            {
                StoreId = storeId,
                ActiveInstanceCount = totalInstances,
                TotalRequestsProcessed = totalRequests,
                AverageLoadFactor = averageLoadFactor,
                HealthStatus = healthStatus,
                LastHeartbeatAge = oldestHeartbeat,
                CoordinationEfficiency = coordinationEfficiency
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get coordination health for store {StoreId}", storeId);
            throw;
        }
    }

    /// <summary>
    /// Calculates variance of load factors for coordination efficiency metric
    /// </summary>
    private static double CalculateVariance(List<double> values)
    {
        if (values.Count <= 1) return 0.0;
        
        var mean = values.Average();
        var sumOfSquaredDeviations = values.Sum(v => Math.Pow(v - mean, 2));
        return sumOfSquaredDeviations / values.Count;
    }

    /// <summary>
    /// Determines health status based on coordination metrics
    /// </summary>
    private string DetermineHealthStatus(int instanceCount, TimeSpan oldestHeartbeat, double efficiency)
    {
        if (instanceCount == 0 || oldestHeartbeat > TimeSpan.FromMinutes(5))
            return "Critical";
        
        if (instanceCount == 1 || efficiency < 0.5 || oldestHeartbeat > TimeSpan.FromMinutes(2))
            return "Warning";
        
        return "Healthy";
    }
}