using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Advanced coordination health monitor with circuit breakers, thundering herd prevention, and split-brain detection
/// Provides bulletproof reliability for the distributed rate limiting system
/// </summary>
public class CoordinationHealthMonitor : ICoordinationHealthMonitor
{
    private readonly IDistributedQuotaTracker _quotaTracker;
    private readonly IInstanceCoordinationManager _coordinationManager;
    private readonly ITokenConsensusManager _tokenConsensusManager;
    private readonly ILogger<CoordinationHealthMonitor> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;

    // Health state tracking
    private readonly Dictionary<string, SystemHealthState> _systemHealthStates = new();
    private readonly Dictionary<string, ThunderingHerdGuard> _thunderingHerdGuards = new();
    private readonly object _healthStateLock = new object();

    // Performance monitoring
    private readonly Dictionary<string, BigCommerce.Migration.Core.Models.RateLimiting.PerformanceMetrics> _performanceMetrics = new();
    private readonly object _performanceLock = new object();

    /// <summary>
    /// Initializes a new instance of the CoordinationHealthMonitor
    /// </summary>
    public CoordinationHealthMonitor(
        IDistributedQuotaTracker quotaTracker,
        IInstanceCoordinationManager coordinationManager,
        ITokenConsensusManager tokenConsensusManager,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<CoordinationHealthMonitor> logger)
    {
        _quotaTracker = quotaTracker ?? throw new ArgumentNullException(nameof(quotaTracker));
        _coordinationManager = coordinationManager ?? throw new ArgumentNullException(nameof(coordinationManager));
        _tokenConsensusManager = tokenConsensusManager ?? throw new ArgumentNullException(nameof(tokenConsensusManager));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("Initialized CoordinationHealthMonitor with advanced reliability patterns");
    }

    /// <inheritdoc />
    public async Task<SystemHealthState> GetSystemHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            // Get existing health state or create new one
            SystemHealthState healthState;
            lock (_healthStateLock)
            {
                if (!_systemHealthStates.TryGetValue(storeId, out SystemHealthState? existingState))
                {
                    healthState = new SystemHealthState { StoreId = storeId };
                    _systemHealthStates[storeId] = healthState;
                }
                else
                {
                    healthState = existingState ?? new SystemHealthState { StoreId = storeId };
                }
            }

            // Gather health metrics from all subsystems
            var quotaHealth = await _quotaTracker.GetQuotaHealthAsync(storeId, cancellationToken);
            var coordinationHealth = await _coordinationManager.GetCoordinationHealthAsync(storeId, cancellationToken);
            var consensusHealth = await _tokenConsensusManager.GetConsensusHealthAsync(storeId, cancellationToken);

            // Update health state
            healthState.UpdateHealth(quotaHealth, coordinationHealth, consensusHealth);

            // Detect anomalies and potential issues
            await DetectSystemAnomaliesAsync(storeId, healthState, cancellationToken);

            // Update performance metrics
            UpdatePerformanceMetrics(storeId, healthState);

            _logger.LogTrace("Updated system health for store {StoreId}: {OverallHealth} (Q:{QuotaHealth}, C:{CoordinationHealth}, T:{ConsensusHealth})",
                storeId, healthState.OverallHealthStatus, quotaHealth.HealthStatus, coordinationHealth.HealthStatus, consensusHealth.HealthStatus);

            return healthState;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system health for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsSystemHealthyAsync(string storeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var healthState = await GetSystemHealthAsync(storeId, cancellationToken);
            return healthState.OverallHealthStatus == SystemHealthStatus.Healthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check system health for store {StoreId} - assuming unhealthy", storeId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CanExecuteOperationAsync(string storeId, CoordinationOperation operation, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            // Check thundering herd protection
            if (!CheckThunderingHerdProtection(storeId, operation))
            {
                _logger.LogDebug("Operation {Operation} blocked by thundering herd protection for store {StoreId}", operation, storeId);
                return false;
            }

            // Get current system health
            var healthState = await GetSystemHealthAsync(storeId, cancellationToken);

            // Apply operation-specific health checks
            var canExecute = operation switch
            {
                CoordinationOperation.QuotaUpdate => CanExecuteQuotaOperation(healthState),
                CoordinationOperation.TokenReservation => CanExecuteTokenOperation(healthState),
                CoordinationOperation.InstanceRegistration => CanExecuteCoordinationOperation(healthState),
                CoordinationOperation.EmergencyScaleBack => true, // Always allow emergency operations
                _ => healthState.OverallHealthStatus != SystemHealthStatus.Critical
            };

            if (canExecute)
            {
                // Record successful operation authorization
                RecordOperationAttempt(storeId, operation, true);
            }
            else
            {
                _logger.LogDebug("Operation {Operation} denied due to health status {HealthStatus} for store {StoreId}",
                    operation, healthState.OverallHealthStatus, storeId);
                RecordOperationAttempt(storeId, operation, false);
            }

            return canExecute;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check operation authorization for store {StoreId}, operation {Operation}", storeId, operation);
            RecordOperationAttempt(storeId, operation, false);
            return false; // Conservative fallback
        }
    }

    /// <inheritdoc />
    public async Task TriggerConservativeFallbackAsync(string storeId, FallbackReason reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            _logger.LogWarning("Triggering conservative fallback for store {StoreId}, reason: {Reason}", storeId, reason);

            // Update system health state to reflect fallback mode
            lock (_healthStateLock)
            {
                if (_systemHealthStates.TryGetValue(storeId, out var healthState))
                {
                    healthState.EnterFallbackMode(reason);
                }
            }

            // Execute fallback actions based on reason
            switch (reason)
            {
                case FallbackReason.SplitBrainDetected:
                    await HandleSplitBrainFallbackAsync(storeId, cancellationToken);
                    break;

                case FallbackReason.CoordinationFailure:
                    await HandleCoordinationFailureFallbackAsync(storeId, cancellationToken);
                    break;

                case FallbackReason.QuotaDataStale:
                    await HandleStaleDataFallbackAsync(storeId, cancellationToken);
                    break;

                case FallbackReason.HighConflictRate:
                    await HandleHighConflictFallbackAsync(storeId, cancellationToken);
                    break;

                case FallbackReason.PerformanceDegradation:
                    await HandlePerformanceFallbackAsync(storeId, cancellationToken);
                    break;

                default:
                    await HandleGenericFallbackAsync(storeId, cancellationToken);
                    break;
            }

            _logger.LogInformation("Conservative fallback completed for store {StoreId}, reason: {Reason}", storeId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger conservative fallback for store {StoreId}, reason: {Reason}", storeId, reason);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AttemptSystemRecoveryAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            _logger.LogInformation("Attempting system recovery for store {StoreId}", storeId);

            // Step 1: Refresh all data to ensure freshness
            await _quotaTracker.RefreshQuotaAsync(storeId, cancellationToken);
            await _tokenConsensusManager.CleanupExpiredAllocationsAsync(storeId, cancellationToken);

            // Step 2: Reset thundering herd protections
            ResetThunderingHerdProtection(storeId);

            // Step 3: Check if system health has improved
            await Task.Delay(1000, cancellationToken); // Brief pause for systems to stabilize
            var healthState = await GetSystemHealthAsync(storeId, cancellationToken);

            // Step 4: Exit fallback mode if health is good
            if (healthState.OverallHealthStatus == SystemHealthStatus.Healthy ||
                healthState.OverallHealthStatus == SystemHealthStatus.Warning)
            {
                lock (_healthStateLock)
                {
                    healthState.ExitFallbackMode();
                }

                _logger.LogInformation("System recovery successful for store {StoreId}, health: {HealthStatus}",
                    storeId, healthState.OverallHealthStatus);
                return true;
            }
            else
            {
                _logger.LogWarning("System recovery incomplete for store {StoreId}, health still: {HealthStatus}",
                    storeId, healthState.OverallHealthStatus);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to attempt system recovery for store {StoreId}", storeId);
            return false;
        }
    }

    /// <inheritdoc />
    public BigCommerce.Migration.Core.Models.RateLimiting.PerformanceMetrics GetPerformanceMetrics(string storeId)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        lock (_performanceLock)
        {
            if (_performanceMetrics.TryGetValue(storeId, out var metrics))
            {
                return metrics.Clone();
            }

            return new BigCommerce.Migration.Core.Models.RateLimiting.PerformanceMetrics { StoreId = storeId };
        }
    }

    /// <inheritdoc />
    public List<string> GetActiveStores()
    {
        lock (_healthStateLock)
        {
            return _systemHealthStates.Keys.ToList();
        }
    }

    /// <summary>
    /// Detects system anomalies and potential issues
    /// </summary>
    private async Task DetectSystemAnomaliesAsync(string storeId, SystemHealthState healthState, CancellationToken cancellationToken)
    {
        try
        {
            // Detect split-brain scenarios
            if (await DetectSplitBrainScenarioAsync(storeId, cancellationToken))
            {
                await TriggerConservativeFallbackAsync(storeId, FallbackReason.SplitBrainDetected, cancellationToken);
                return;
            }

            // Detect stale data issues
            if (healthState.QuotaDataAge > TimeSpan.FromMinutes(10))
            {
                await TriggerConservativeFallbackAsync(storeId, FallbackReason.QuotaDataStale, cancellationToken);
                return;
            }

            // Detect high conflict rates
            var performanceMetrics = GetPerformanceMetrics(storeId);
            if (performanceMetrics.ETagConflictRate > 0.1) // More than 10% conflict rate
            {
                await TriggerConservativeFallbackAsync(storeId, FallbackReason.HighConflictRate, cancellationToken);
                return;
            }

            // Detect coordination failures
            if (healthState.CoordinationFailureCount > 5)
            {
                await TriggerConservativeFallbackAsync(storeId, FallbackReason.CoordinationFailure, cancellationToken);
                return;
            }

            // Detect performance degradation
            if (performanceMetrics.AverageOperationLatency > TimeSpan.FromSeconds(5))
            {
                await TriggerConservativeFallbackAsync(storeId, FallbackReason.PerformanceDegradation, cancellationToken);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect system anomalies for store {StoreId}", storeId);
        }
    }

    /// <summary>
    /// Detects split-brain scenarios where coordination is fragmented
    /// </summary>
    private async Task<bool> DetectSplitBrainScenarioAsync(string storeId, CancellationToken cancellationToken)
    {
        try
        {
            var coordinationHealth = await _coordinationManager.GetCoordinationHealthAsync(storeId, cancellationToken);
            var consensusHealth = await _tokenConsensusManager.GetConsensusHealthAsync(storeId, cancellationToken);

            // Split-brain indicators:
            // 1. Multiple instances but very low coordination efficiency
            // 2. Token allocations that don't match active instances
            // 3. High ETag conflict rates combined with coordination issues

            if (coordinationHealth.ActiveInstanceCount > 1 && coordinationHealth.CoordinationEfficiency < 0.3)
            {
                _logger.LogWarning("Potential split-brain detected for store {StoreId}: {InstanceCount} instances with {Efficiency:P1} efficiency",
                    storeId, coordinationHealth.ActiveInstanceCount, coordinationHealth.CoordinationEfficiency);
                return true;
            }

            if (consensusHealth.ActiveAllocations != coordinationHealth.ActiveInstanceCount && 
                Math.Abs(consensusHealth.ActiveAllocations - coordinationHealth.ActiveInstanceCount) > 2)
            {
                _logger.LogWarning("Allocation mismatch detected for store {StoreId}: {Allocations} allocations vs {Instances} instances",
                    storeId, consensusHealth.ActiveAllocations, coordinationHealth.ActiveInstanceCount);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect split-brain scenario for store {StoreId}", storeId);
            return false;
        }
    }

    /// <summary>
    /// Checks thundering herd protection for operations
    /// </summary>
    private bool CheckThunderingHerdProtection(string storeId, CoordinationOperation operation)
    {
        lock (_healthStateLock)
        {
            if (!_thunderingHerdGuards.TryGetValue(storeId, out var guard))
            {
                guard = new ThunderingHerdGuard();
                _thunderingHerdGuards[storeId] = guard;
            }

            return guard.CanExecuteOperation(operation);
        }
    }

    /// <summary>
    /// Records operation attempt for metrics
    /// </summary>
    private void RecordOperationAttempt(string storeId, CoordinationOperation operation, bool success)
    {
        lock (_performanceLock)
        {
            if (!_performanceMetrics.TryGetValue(storeId, out var metrics))
            {
                metrics = new BigCommerce.Migration.Core.Models.RateLimiting.PerformanceMetrics { StoreId = storeId };
                _performanceMetrics[storeId] = metrics;
            }

            metrics.RecordOperation(operation, success);
        }
    }

    /// <summary>
    /// Updates performance metrics based on health state
    /// </summary>
    private void UpdatePerformanceMetrics(string storeId, SystemHealthState healthState)
    {
        lock (_performanceLock)
        {
            if (!_performanceMetrics.TryGetValue(storeId, out var metrics))
            {
                metrics = new BigCommerce.Migration.Core.Models.RateLimiting.PerformanceMetrics { StoreId = storeId };
                _performanceMetrics[storeId] = metrics;
            }

            metrics.UpdateFromHealthState(healthState);
        }
    }

    /// <summary>
    /// Resets thundering herd protection for a store
    /// </summary>
    private void ResetThunderingHerdProtection(string storeId)
    {
        lock (_healthStateLock)
        {
            if (_thunderingHerdGuards.TryGetValue(storeId, out var guard))
            {
                guard.Reset();
            }
        }
    }

    // Operation-specific health checks
    private bool CanExecuteQuotaOperation(SystemHealthState healthState)
    {
        return healthState.OverallHealthStatus != SystemHealthStatus.Critical &&
               !healthState.IsInFallbackMode &&
               healthState.QuotaDataAge < TimeSpan.FromMinutes(5);
    }

    private bool CanExecuteTokenOperation(SystemHealthState healthState)
    {
        return healthState.OverallHealthStatus != SystemHealthStatus.Critical &&
               healthState.CoordinationEfficiency > 0.5;
    }

    private bool CanExecuteCoordinationOperation(SystemHealthState healthState)
    {
        return healthState.OverallHealthStatus != SystemHealthStatus.Critical;
    }

    // Fallback handlers continue in next part...
    
    /// <summary>
    /// Handles split-brain fallback scenario
    /// </summary>
    private async Task HandleSplitBrainFallbackAsync(string storeId, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Handling split-brain fallback for store {StoreId}", storeId);
        
        // Clear all coordination data to force fresh registration
        await _coordinationManager.UnregisterStoreAsync(storeId, cancellationToken);
        await _tokenConsensusManager.CleanupExpiredAllocationsAsync(storeId, cancellationToken);
        
        // Force fresh registration
        await _coordinationManager.EnsureStoreRegistrationAsync(storeId, cancellationToken);
    }

    /// <summary>
    /// Handles coordination failure fallback
    /// </summary>
    private async Task HandleCoordinationFailureFallbackAsync(string storeId, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Handling coordination failure fallback for store {StoreId}", storeId);
        
        // Switch to single-instance mode temporarily
        await _coordinationManager.UnregisterStoreAsync(storeId, cancellationToken);
        ResetThunderingHerdProtection(storeId);
    }

    /// <summary>
    /// Handles stale data fallback
    /// </summary>
    private async Task HandleStaleDataFallbackAsync(string storeId, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Handling stale data fallback for store {StoreId}", storeId);
        
        // Force fresh data
        await _quotaTracker.RefreshQuotaAsync(storeId, cancellationToken);
    }

    /// <summary>
    /// Handles high conflict rate fallback
    /// </summary>
    private async Task HandleHighConflictFallbackAsync(string storeId, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Handling high conflict rate fallback for store {StoreId}", storeId);
        
        // Reduce concurrency by adding delays
        await Task.Delay(Random.Shared.Next(1000, 3000), cancellationToken);
        ResetThunderingHerdProtection(storeId);
    }

    /// <summary>
    /// Handles performance degradation fallback
    /// </summary>
    private async Task HandlePerformanceFallbackAsync(string storeId, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Handling performance degradation fallback for store {StoreId}", storeId);
        
        // Clean up and reduce system load
        await _tokenConsensusManager.CleanupExpiredAllocationsAsync(storeId, cancellationToken);
        ResetThunderingHerdProtection(storeId);
    }

    /// <summary>
    /// Handles generic fallback scenario
    /// </summary>
    private async Task HandleGenericFallbackAsync(string storeId, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Handling generic fallback for store {StoreId}", storeId);
        
        // General cleanup and reset
        await _tokenConsensusManager.CleanupExpiredAllocationsAsync(storeId, cancellationToken);
        ResetThunderingHerdProtection(storeId);
    }
}