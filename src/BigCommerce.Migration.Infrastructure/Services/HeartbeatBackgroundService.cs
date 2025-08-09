using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Background service for automatic heartbeat management across all active stores
/// Ensures consistent instance coordination without manual heartbeat management
/// </summary>
public class HeartbeatBackgroundService : BackgroundService, IHeartbeatBackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HeartbeatBackgroundService> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;
    
    // Active store tracking
    private readonly HashSet<string> _activeStores = new();
    private readonly object _storesLock = new object();

    /// <summary>
    /// Initializes a new instance of the HeartbeatBackgroundService
    /// </summary>
    public HeartbeatBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<HeartbeatBackgroundService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers a store for automatic heartbeat management
    /// </summary>
    public void RegisterStore(string storeId)
    {
        if (string.IsNullOrEmpty(storeId)) return;

        lock (_storesLock)
        {
            if (_activeStores.Add(storeId))
            {
                _logger.LogInformation("Registered store {StoreId} for automatic heartbeat management", storeId);
            }
        }
    }

    /// <summary>
    /// Unregisters a store from automatic heartbeat management
    /// </summary>
    public void UnregisterStore(string storeId)
    {
        if (string.IsNullOrEmpty(storeId)) return;

        lock (_storesLock)
        {
            if (_activeStores.Remove(storeId))
            {
                _logger.LogInformation("Unregistered store {StoreId} from automatic heartbeat management", storeId);
            }
        }
    }

    /// <summary>
    /// Gets the list of actively managed stores
    /// </summary>
    public List<string> GetActiveStores()
    {
        lock (_storesLock)
        {
            return _activeStores.ToList();
        }
    }

    /// <summary>
    /// Main background service execution loop
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.Features.EnableInstanceCoordination)
        {
            _logger.LogInformation("Instance coordination disabled - HeartbeatBackgroundService not running");
            return;
        }

        _logger.LogInformation("HeartbeatBackgroundService started with {HeartbeatInterval}s interval", 
            _configuration.Predictive.HeartbeatIntervalSeconds);

        var heartbeatInterval = TimeSpan.FromSeconds(_configuration.Predictive.HeartbeatIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessHeartbeatsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HeartbeatBackgroundService execution");
            }

            try
            {
                await Task.Delay(heartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
        }

        _logger.LogInformation("HeartbeatBackgroundService stopped");
    }

    /// <summary>
    /// Processes heartbeats for all active stores
    /// </summary>
    private async Task ProcessHeartbeatsAsync(CancellationToken cancellationToken)
    {
        List<string> storesToProcess;
        
        lock (_storesLock)
        {
            storesToProcess = _activeStores.ToList();
        }

        if (storesToProcess.Count == 0)
        {
            _logger.LogTrace("No active stores to process heartbeats for");
            return;
        }

        _logger.LogTrace("Processing heartbeats for {StoreCount} active stores", storesToProcess.Count);

        // Process heartbeats in parallel with limited concurrency
        var semaphore = new SemaphoreSlim(5); // Max 5 concurrent heartbeats
        var heartbeatTasks = storesToProcess.Select(async storeId =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await ProcessStoreHeartbeatAsync(storeId, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(heartbeatTasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Some heartbeats failed to process");
        }
    }

    /// <summary>
    /// Processes heartbeat for a single store
    /// </summary>
    private async Task ProcessStoreHeartbeatAsync(string storeId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var instanceCoordinator = scope.ServiceProvider.GetRequiredService<IInstanceCoordinator>();

            // Send heartbeat
            await instanceCoordinator.SendHeartbeatAsync(storeId, cancellationToken);

            // Occasionally cleanup stale instances (every 10th heartbeat cycle approximately)
            if (Random.Shared.Next(10) == 0)
            {
                try
                {
                    var cleanedCount = await instanceCoordinator.CleanupStaleInstancesAsync(storeId, cancellationToken: cancellationToken);
                    if (cleanedCount > 0)
                    {
                        _logger.LogDebug("Cleaned up {CleanedCount} stale instances for store {StoreId}", 
                            cleanedCount, storeId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup stale instances for store {StoreId}", storeId);
                }
            }

            _logger.LogTrace("Processed heartbeat for store {StoreId}", storeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process heartbeat for store {StoreId}", storeId);
        }
    }

    /// <summary>
    /// Cleanup on service stop
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HeartbeatBackgroundService stopping - unregistering {StoreCount} stores", 
            _activeStores.Count);

        try
        {
            // Unregister all active stores
            var storesToUnregister = GetActiveStores();
            
            if (storesToUnregister.Count > 0)
            {
                using var scope = _serviceProvider.CreateScope();
                var instanceCoordinator = scope.ServiceProvider.GetRequiredService<IInstanceCoordinator>();

                var unregisterTasks = storesToUnregister.Select(storeId =>
                    UnregisterStoreGracefullyAsync(instanceCoordinator, storeId, cancellationToken));

                await Task.WhenAll(unregisterTasks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to gracefully unregister stores during shutdown");
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Gracefully unregister a store during shutdown
    /// </summary>
    private async Task UnregisterStoreGracefullyAsync(IInstanceCoordinator instanceCoordinator, string storeId, CancellationToken cancellationToken)
    {
        try
        {
            await instanceCoordinator.UnregisterInstanceAsync(storeId, cancellationToken);
            _logger.LogDebug("Gracefully unregistered instance from store {StoreId}", storeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to gracefully unregister instance from store {StoreId}", storeId);
        }
    }
}