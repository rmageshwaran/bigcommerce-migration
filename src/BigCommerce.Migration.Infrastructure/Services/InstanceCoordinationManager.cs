using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// High-level manager for instance coordination across multiple stores
/// Provides simplified interface for rate limiting integration with automatic lifecycle management
/// </summary>
public class InstanceCoordinationManager : IInstanceCoordinationManager
{
    private readonly IInstanceCoordinator _instanceCoordinator;
    private readonly IHeartbeatBackgroundService _heartbeatService;
    private readonly ILogger<InstanceCoordinationManager> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;
    
    // Store registration tracking
    private readonly HashSet<string> _registeredStores = new();
    private readonly object _registrationLock = new object();

    /// <summary>
    /// Initializes a new instance of the InstanceCoordinationManager
    /// </summary>
    public InstanceCoordinationManager(
        IInstanceCoordinator instanceCoordinator,
        IHeartbeatBackgroundService heartbeatService,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<InstanceCoordinationManager> logger)
    {
        _instanceCoordinator = instanceCoordinator ?? throw new ArgumentNullException(nameof(instanceCoordinator));
        _heartbeatService = heartbeatService ?? throw new ArgumentNullException(nameof(heartbeatService));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task EnsureStoreRegistrationAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        if (!_configuration.Features.EnableInstanceCoordination)
        {
            _logger.LogTrace("Instance coordination disabled - skipping store registration for {StoreId}", storeId);
            return;
        }

        lock (_registrationLock)
        {
            if (_registeredStores.Contains(storeId))
            {
                _logger.LogTrace("Store {StoreId} already registered for instance coordination", storeId);
                return; // Already registered
            }
        }

        try
        {
            // Register instance with the coordinator
            await _instanceCoordinator.RegisterInstanceAsync(storeId, cancellationToken);
            
            // Add to automatic heartbeat management
            _heartbeatService.RegisterStore(storeId);
            
            lock (_registrationLock)
            {
                _registeredStores.Add(storeId);
            }

            _logger.LogInformation("Successfully registered instance coordination for store {StoreId}", storeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register instance coordination for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetActiveInstanceCountAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        if (!_configuration.Features.EnableInstanceCoordination)
        {
            // If coordination is disabled, assume single instance
            return 1;
        }

        try
        {
            // Ensure store is registered before querying
            await EnsureStoreRegistrationAsync(storeId, cancellationToken);
            
            return await _instanceCoordinator.GetActiveInstanceCountAsync(storeId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get active instance count for store {StoreId} - assuming single instance", storeId);
            return 1; // Conservative fallback
        }
    }

    /// <inheritdoc />
    public async Task<List<string>> GetActiveInstanceIdsAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        if (!_configuration.Features.EnableInstanceCoordination)
        {
            // If coordination is disabled, return current instance only
            return new List<string> { _instanceCoordinator.InstanceId };
        }

        try
        {
            // Ensure store is registered before querying
            await EnsureStoreRegistrationAsync(storeId, cancellationToken);
            
            return await _instanceCoordinator.GetActiveInstanceIdsAsync(storeId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get active instance IDs for store {StoreId} - returning current instance only", storeId);
            return new List<string> { _instanceCoordinator.InstanceId }; // Conservative fallback
        }
    }

    /// <inheritdoc />
    public async Task<CoordinationHealthMetrics> GetCoordinationHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        if (!_configuration.Features.EnableInstanceCoordination)
        {
            // Return healthy single-instance metrics
            return new CoordinationHealthMetrics
            {
                StoreId = storeId,
                ActiveInstanceCount = 1,
                TotalRequestsProcessed = 0,
                AverageLoadFactor = 0.0,
                HealthStatus = "Healthy",
                LastHeartbeatAge = TimeSpan.Zero,
                CoordinationEfficiency = 1.0
            };
        }

        try
        {
            // Ensure store is registered before querying
            await EnsureStoreRegistrationAsync(storeId, cancellationToken);
            
            return await _instanceCoordinator.GetCoordinationHealthAsync(storeId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get coordination health for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RecordRequestProcessedAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            return;

        if (!_configuration.Features.EnableInstanceCoordination)
        {
            return; // No coordination tracking needed
        }

        try
        {
            // Fire-and-forget: Don't block API processing for metrics tracking
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnsureStoreRegistrationAsync(storeId, cancellationToken);
                    await _instanceCoordinator.RecordRequestProcessedAsync(storeId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Failed to record request processed for store {StoreId}", storeId);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to initiate request recording for store {StoreId}", storeId);
        }
        
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task UpdateLoadFactorAsync(string storeId, double loadFactor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            return;

        if (!_configuration.Features.EnableInstanceCoordination)
        {
            return; // No coordination tracking needed
        }

        try
        {
            // Fire-and-forget: Don't block API processing for metrics tracking
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnsureStoreRegistrationAsync(storeId, cancellationToken);
                    await _instanceCoordinator.UpdateLoadFactorAsync(storeId, loadFactor, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Failed to update load factor for store {StoreId}", storeId);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to initiate load factor update for store {StoreId}", storeId);
        }
        
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GetCurrentInstanceId()
    {
        return _instanceCoordinator.InstanceId;
    }

    /// <inheritdoc />
    public bool IsCoordinationEnabled()
    {
        return _configuration.Features.EnableInstanceCoordination;
    }

    /// <inheritdoc />
    public List<string> GetRegisteredStores()
    {
        lock (_registrationLock)
        {
            return _registeredStores.ToList();
        }
    }

    /// <inheritdoc />
    public async Task UnregisterStoreAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            return;

        try
        {
            // Remove from heartbeat management
            _heartbeatService.UnregisterStore(storeId);
            
            // Unregister from coordinator
            await _instanceCoordinator.UnregisterInstanceAsync(storeId, cancellationToken);
            
            lock (_registrationLock)
            {
                _registeredStores.Remove(storeId);
            }

            _logger.LogInformation("Successfully unregistered instance coordination for store {StoreId}", storeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unregister instance coordination for store {StoreId}", storeId);
            // Don't throw - best effort cleanup
        }
    }
}