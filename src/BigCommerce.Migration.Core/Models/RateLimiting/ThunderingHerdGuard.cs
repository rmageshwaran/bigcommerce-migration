namespace BigCommerce.Migration.Core.Models.RateLimiting;

/// <summary>
/// Thundering herd protection guard for coordination operations
/// Prevents multiple instances from overwhelming the system with simultaneous operations
/// </summary>
public class ThunderingHerdGuard
{
    private readonly Dictionary<CoordinationOperation, OperationLimiter> _operationLimiters = new();
    private readonly object _limiterLock = new object();

    /// <summary>
    /// Initializes a new instance of the ThunderingHerdGuard
    /// </summary>
    public ThunderingHerdGuard()
    {
        // Initialize operation limiters with appropriate constraints
        _operationLimiters[CoordinationOperation.QuotaUpdate] = new OperationLimiter(maxPerSecond: 2, burstLimit: 5);
        _operationLimiters[CoordinationOperation.TokenReservation] = new OperationLimiter(maxPerSecond: 10, burstLimit: 20);
        _operationLimiters[CoordinationOperation.InstanceRegistration] = new OperationLimiter(maxPerSecond: 1, burstLimit: 3);
        _operationLimiters[CoordinationOperation.EmergencyScaleBack] = new OperationLimiter(maxPerSecond: 1, burstLimit: 1);
    }

    /// <summary>
    /// Checks if an operation can be executed without causing thundering herd
    /// </summary>
    public bool CanExecuteOperation(CoordinationOperation operation)
    {
        lock (_limiterLock)
        {
            if (_operationLimiters.TryGetValue(operation, out var limiter))
            {
                return limiter.TryAcquire();
            }

            // Unknown operation - allow but with conservative limit
            var defaultLimiter = new OperationLimiter(maxPerSecond: 1, burstLimit: 2);
            _operationLimiters[operation] = defaultLimiter;
            return defaultLimiter.TryAcquire();
        }
    }

    /// <summary>
    /// Resets all operation limiters
    /// </summary>
    public void Reset()
    {
        lock (_limiterLock)
        {
            foreach (var limiter in _operationLimiters.Values)
            {
                limiter.Reset();
            }
        }
    }

    /// <summary>
    /// Gets operation statistics
    /// </summary>
    public Dictionary<CoordinationOperation, OperationStats> GetOperationStats()
    {
        lock (_limiterLock)
        {
            return _operationLimiters.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.GetStats()
            );
        }
    }
}

/// <summary>
/// Token bucket-based operation limiter
/// </summary>
public class OperationLimiter
{
    private readonly int _maxTokens;
    private readonly double _refillRate; // tokens per second
    private int _currentTokens;
    private DateTimeOffset _lastRefill;
    private long _totalRequests;
    private long _acceptedRequests;
    private readonly object _tokenLock = new object();

    /// <summary>
    /// Initializes a new instance of the OperationLimiter
    /// </summary>
    public OperationLimiter(double maxPerSecond, int burstLimit)
    {
        _maxTokens = burstLimit;
        _refillRate = maxPerSecond;
        _currentTokens = burstLimit;
        _lastRefill = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Attempts to acquire a token for operation execution
    /// </summary>
    public bool TryAcquire()
    {
        lock (_tokenLock)
        {
            RefillTokens();
            _totalRequests++;

            if (_currentTokens > 0)
            {
                _currentTokens--;
                _acceptedRequests++;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Resets the limiter to full token capacity
    /// </summary>
    public void Reset()
    {
        lock (_tokenLock)
        {
            _currentTokens = _maxTokens;
            _lastRefill = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Gets operation statistics
    /// </summary>
    public OperationStats GetStats()
    {
        lock (_tokenLock)
        {
            return new OperationStats
            {
                TotalRequests = _totalRequests,
                AcceptedRequests = _acceptedRequests,
                RejectedRequests = _totalRequests - _acceptedRequests,
                AcceptanceRate = _totalRequests > 0 ? (double)_acceptedRequests / _totalRequests : 0.0,
                CurrentTokens = _currentTokens,
                MaxTokens = _maxTokens
            };
        }
    }

    /// <summary>
    /// Refills tokens based on elapsed time
    /// </summary>
    private void RefillTokens()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastRefill;
        
        if (elapsed > TimeSpan.Zero)
        {
            var tokensToAdd = (int)(elapsed.TotalSeconds * _refillRate);
            if (tokensToAdd > 0)
            {
                _currentTokens = Math.Min(_maxTokens, _currentTokens + tokensToAdd);
                _lastRefill = now;
            }
        }
    }
}

/// <summary>
/// Operation statistics for monitoring
/// </summary>
/// <summary>
/// Operation statistics for monitoring
/// </summary>
public class OperationStats
{
    /// <summary>
    /// Total number of requests attempted
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Number of requests that were accepted
    /// </summary>
    public long AcceptedRequests { get; set; }

    /// <summary>
    /// Number of requests that were rejected
    /// </summary>
    public long RejectedRequests { get; set; }

    /// <summary>
    /// Percentage of requests that were accepted
    /// </summary>
    public double AcceptanceRate { get; set; }

    /// <summary>
    /// Current number of tokens available
    /// </summary>
    public int CurrentTokens { get; set; }

    /// <summary>
    /// Maximum number of tokens allowed
    /// </summary>
    public int MaxTokens { get; set; }

    /// <summary>
    /// Returns a string representation of the operation statistics
    /// </summary>
    /// <returns>String with request counts, acceptance rate, and token counts</returns>
    public override string ToString()
    {
        return $"OperationStats: {AcceptedRequests}/{TotalRequests} ({AcceptanceRate:P1}), " +
               $"Tokens: {CurrentTokens}/{MaxTokens}";
    }
}

/// <summary>
/// Coordination operation types
/// </summary>
/// <summary>
/// Coordination operation types
/// </summary>
public enum CoordinationOperation
{
    /// <summary>
    /// Update quota information from BigCommerce API
    /// </summary>
    QuotaUpdate,

    /// <summary>
    /// Reserve tokens for API requests
    /// </summary>
    TokenReservation,

    /// <summary>
    /// Register instance with coordination system
    /// </summary>
    InstanceRegistration,

    /// <summary>
    /// Emergency scale back of token allocations
    /// </summary>
    EmergencyScaleBack,

    /// <summary>
    /// Check health of coordination system
    /// </summary>
    HealthCheck,

    /// <summary>
    /// Clean up stale coordination data
    /// </summary>
    DataCleanup
}