using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// High-level predictive rate limiting service that coordinates quota tracking, instance discovery, and token consensus
/// Provides the main interface for the rate limiting system with circuit breaker capabilities
/// </summary>
public class PredictiveRateLimitingService : IPredictiveRateLimitingService
{
    private readonly IDistributedQuotaTracker _quotaTracker;
    private readonly IInstanceCoordinationManager _coordinationManager;
    private readonly ITokenConsensusManager _tokenConsensusManager;
    private readonly ILogger<PredictiveRateLimitingService> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;

    // Circuit breaker state
    private readonly Dictionary<string, CircuitBreakerState> _circuitBreakers = new();
    private readonly object _circuitBreakerLock = new object();

    /// <summary>
    /// Initializes a new instance of the PredictiveRateLimitingService
    /// </summary>
    public PredictiveRateLimitingService(
        IDistributedQuotaTracker quotaTracker,
        IInstanceCoordinationManager coordinationManager,
        ITokenConsensusManager tokenConsensusManager,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<PredictiveRateLimitingService> logger)
    {
        _quotaTracker = quotaTracker ?? throw new ArgumentNullException(nameof(quotaTracker));
        _coordinationManager = coordinationManager ?? throw new ArgumentNullException(nameof(coordinationManager));
        _tokenConsensusManager = tokenConsensusManager ?? throw new ArgumentNullException(nameof(tokenConsensusManager));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("Initialized PredictiveRateLimitingService with circuit breaker protection");
    }

    /// <inheritdoc />
    public async Task<bool> CanProcessRequestAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        // Feature flag check
        if (!_configuration.Features.EnablePredictiveDistribution)
        {
            _logger.LogTrace("Predictive distribution disabled for store {StoreId} - allowing request", storeId);
            return true; // Fallback to existing rate limiting
        }

        // Circuit breaker check
        var circuitBreakerState = GetCircuitBreakerState(storeId);
        if (circuitBreakerState.IsOpen)
        {
            if (circuitBreakerState.ShouldAttemptReset())
            {
                _logger.LogDebug("Circuit breaker half-open for store {StoreId} - attempting request", storeId);
                // Continue with request attempt in half-open state
            }
            else
            {
                _logger.LogDebug("Circuit breaker open for store {StoreId} - rejecting request", storeId);
                return false;
            }
        }

        try
        {
            // Check if we have available tokens
            var availableTokens = await _tokenConsensusManager.GetAvailableTokensAsync(storeId, cancellationToken);
            
            if (availableTokens <= 0)
            {
                // Try to reserve more tokens
                var reservedTokens = await _tokenConsensusManager.ReserveTokensAsync(storeId, cancellationToken);
                
                if (reservedTokens <= 0)
                {
                    _logger.LogDebug("No tokens available for store {StoreId} - rejecting request", storeId);
                    RecordCircuitBreakerFailure(storeId);
                    return false;
                }
                
                _logger.LogTrace("Reserved {ReservedTokens} new tokens for store {StoreId}", reservedTokens, storeId);
            }

            // Try to consume a token
            var tokenConsumed = await _tokenConsensusManager.TryConsumeTokenAsync(storeId, cancellationToken);
            
            if (tokenConsumed)
            {
                _logger.LogTrace("Token consumed for store {StoreId} - allowing request", storeId);
                RecordCircuitBreakerSuccess(storeId);
                
                // Record request processed for coordination metrics
                _ = Task.Run(async () => await _coordinationManager.RecordRequestProcessedAsync(storeId, cancellationToken));
                
                return true;
            }
            else
            {
                _logger.LogDebug("Failed to consume token for store {StoreId} - rejecting request", storeId);
                RecordCircuitBreakerFailure(storeId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking request permission for store {StoreId} - rejecting request", storeId);
            RecordCircuitBreakerFailure(storeId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<PredictiveRateLimitStatus> GetRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            // Gather all health metrics
            var quotaHealth = await _quotaTracker.GetQuotaHealthAsync(storeId, cancellationToken);
            var coordinationHealth = await _coordinationManager.GetCoordinationHealthAsync(storeId, cancellationToken);
            var consensusHealth = await _tokenConsensusManager.GetConsensusHealthAsync(storeId, cancellationToken);
            var allocation = await _tokenConsensusManager.GetInstanceAllocationAsync(storeId, cancellationToken);
            
            var circuitBreakerState = GetCircuitBreakerState(storeId);

            return new PredictiveRateLimitStatus
            {
                StoreId = storeId,
                InstanceId = _coordinationManager.GetCurrentInstanceId(),
                
                // Quota information
                TotalQuota = quotaHealth.TotalQuota,
                RemainingTokens = quotaHealth.RemainingTokens,
                SafeTokens = quotaHealth.SafeTokens,
                QuotaUtilizationPercent = quotaHealth.QuotaUtilizationPercent * 100,
                QuotaHealthStatus = quotaHealth.HealthStatus.ToString(),
                QuotaResetTime = quotaHealth.QuotaResetTime,
                DataAge = quotaHealth.DataAge,
                
                // Instance coordination
                ActiveInstanceCount = coordinationHealth.ActiveInstanceCount,
                CoordinationHealthStatus = coordinationHealth.HealthStatus,
                CoordinationEfficiency = coordinationHealth.CoordinationEfficiency * 100,
                
                // Token allocation
                AllocatedTokens = allocation?.AllocatedTokens ?? 0,
                AvailableTokens = allocation?.AvailableTokens ?? 0,
                TokensConsumed = allocation?.TokensConsumed ?? 0,
                AllocationEfficiency = (allocation?.AllocationEfficiency ?? 0) * 100,
                TokenExpiresAt = allocation?.ExpiresAt,
                
                // Consensus health
                TotalAllocatedTokens = consensusHealth.TotalAllocatedTokens,
                ConsensusHealthStatus = consensusHealth.HealthStatus,
                
                // Circuit breaker
                CircuitBreakerStatus = circuitBreakerState.State.ToString(),
                CircuitBreakerFailureCount = circuitBreakerState.FailureCount,
                
                // Feature flags
                IsPredictiveEnabled = _configuration.Features.EnablePredictiveDistribution,
                IsCoordinationEnabled = _configuration.Features.EnableInstanceCoordination,
                IsQuotaTrackingEnabled = _configuration.Features.EnableQuotaTracking,
                
                // Timestamp
                StatusTimestamp = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get rate limit status for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task TriggerQuotaRefreshAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            _logger.LogInformation("Triggering quota refresh for store {StoreId}", storeId);
            
            // Refresh quota data (forces fresh fetch from BigCommerce API)
            await _quotaTracker.RefreshQuotaAsync(storeId, cancellationToken);
            
            // Cleanup expired token allocations
            var cleanedTokens = await _tokenConsensusManager.CleanupExpiredAllocationsAsync(storeId, cancellationToken);
            
            // Reset circuit breaker if it's not functioning well
            ResetCircuitBreaker(storeId);
            
            _logger.LogInformation("Quota refresh completed for store {StoreId} (cleaned {CleanedTokens} expired allocations)",
                storeId, cleanedTokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger quota refresh for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task TriggerEmergencyScaleBackAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            _logger.LogWarning("Triggering emergency scale-back for store {StoreId}", storeId);
            
            // Get current quota health
            var quotaHealth = await _quotaTracker.GetQuotaHealthAsync(storeId, cancellationToken);
            
            // Use critical safety buffer (50%) for emergency
            var emergencySafeTokens = (int)(quotaHealth.RemainingTokens * 0.5);
            
            // Trigger proportional scale-back
            var scaledBackTokens = await _tokenConsensusManager.TriggerProportionalScaleBackAsync(
                storeId, emergencySafeTokens, cancellationToken);
            
            // Open circuit breaker temporarily to reduce load
            var circuitBreakerState = GetCircuitBreakerState(storeId);
            circuitBreakerState.OpenCircuit();
            
            _logger.LogWarning("Emergency scale-back completed for store {StoreId}: scaled back {ScaledBackTokens} tokens, circuit breaker opened",
                storeId, scaledBackTokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger emergency scale-back for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public bool IsPredictiveEnabled(string storeId)
    {
        return _configuration.Features.EnablePredictiveDistribution;
    }

    /// <summary>
    /// Gets or creates circuit breaker state for a store
    /// </summary>
    private CircuitBreakerState GetCircuitBreakerState(string storeId)
    {
        lock (_circuitBreakerLock)
        {
            if (!_circuitBreakers.TryGetValue(storeId, out var state))
            {
                state = new CircuitBreakerState();
                _circuitBreakers[storeId] = state;
            }
            return state;
        }
    }

    /// <summary>
    /// Records a successful operation for circuit breaker
    /// </summary>
    private void RecordCircuitBreakerSuccess(string storeId)
    {
        var state = GetCircuitBreakerState(storeId);
        state.RecordSuccess();
    }

    /// <summary>
    /// Records a failed operation for circuit breaker
    /// </summary>
    private void RecordCircuitBreakerFailure(string storeId)
    {
        var state = GetCircuitBreakerState(storeId);
        state.RecordFailure();
        
        if (state.IsOpen)
        {
            _logger.LogWarning("Circuit breaker opened for store {StoreId} after {FailureCount} failures", 
                storeId, state.FailureCount);
        }
    }

    /// <summary>
    /// Resets circuit breaker for a store
    /// </summary>
    private void ResetCircuitBreaker(string storeId)
    {
        var state = GetCircuitBreakerState(storeId);
        state.Reset();
        _logger.LogInformation("Circuit breaker reset for store {StoreId}", storeId);
    }
}

/// <summary>
/// Circuit breaker state for rate limiting protection
/// </summary>
internal class CircuitBreakerState
{
    private const int FailureThreshold = 5;
    private const int TimeoutSeconds = 60;
    
    public CircuitBreakerStates State { get; private set; } = CircuitBreakerStates.Closed;
    public int FailureCount { get; private set; } = 0;
    public DateTimeOffset? LastFailureTime { get; private set; }
    
    public bool IsOpen => State == CircuitBreakerStates.Open;
    public bool IsHalfOpen => State == CircuitBreakerStates.HalfOpen;
    public bool IsClosed => State == CircuitBreakerStates.Closed;

    public void RecordSuccess()
    {
        FailureCount = 0;
        State = CircuitBreakerStates.Closed;
        LastFailureTime = null;
    }

    public void RecordFailure()
    {
        FailureCount++;
        LastFailureTime = DateTimeOffset.UtcNow;
        
        if (FailureCount >= FailureThreshold)
        {
            State = CircuitBreakerStates.Open;
        }
    }

    public bool ShouldAttemptReset()
    {
        if (State != CircuitBreakerStates.Open || !LastFailureTime.HasValue)
            return false;
        
        var timeSinceLastFailure = DateTimeOffset.UtcNow - LastFailureTime.Value;
        if (timeSinceLastFailure >= TimeSpan.FromSeconds(TimeoutSeconds))
        {
            State = CircuitBreakerStates.HalfOpen;
            return true;
        }
        
        return false;
    }

    public void OpenCircuit()
    {
        State = CircuitBreakerStates.Open;
        LastFailureTime = DateTimeOffset.UtcNow;
    }

    public void Reset()
    {
        State = CircuitBreakerStates.Closed;
        FailureCount = 0;
        LastFailureTime = null;
    }
}

/// <summary>
/// Circuit breaker states
/// </summary>
internal enum CircuitBreakerStates
{
    Closed,   // Normal operation
    Open,     // Circuit is open, rejecting requests
    HalfOpen  // Testing if circuit can be closed
}