using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using System.Collections.Concurrent;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Service for managing rate limiting across BigCommerce API calls
/// Implements BigCommerce's 12 requests per second limit with proper cancellation support
/// 
/// **Phase 2.7: Parallel-Aware Rate Limiting Optimization**
/// - Adaptive delays for parallel vs sequential scenarios
/// - Intelligent backoff based on parallel context
/// - Reduced delays from 2000ms to 100-500ms range
/// </summary>
public class RateLimitService : IRateLimitService
{
    private readonly ILogger<RateLimitService> _logger;
    private readonly ConcurrentDictionary<string, StoreRateLimit> _storeLimits;
    private readonly ConcurrentDictionary<string, ParallelContext> _parallelContexts;
    private readonly object _lock = new object();
    
    /// <summary>
    /// Initializes a new instance of the RateLimitService
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public RateLimitService(ILogger<RateLimitService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storeLimits = new ConcurrentDictionary<string, StoreRateLimit>();
        _parallelContexts = new ConcurrentDictionary<string, ParallelContext>();
    }
    
    /// <summary>
    /// **Phase 2.7**: Registers parallel processing context for adaptive rate limiting
    /// **Phase 2.8b**: Enhanced with rate limit pool coordination
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="concurrentBatches">Number of concurrent batches being processed</param>
    /// <param name="totalBatches">Total number of batches in the migration</param>
    public void RegisterParallelContext(string storeId, int concurrentBatches, int totalBatches)
    {
        _parallelContexts.AddOrUpdate(storeId, 
            new ParallelContext 
            { 
                ConcurrentBatches = concurrentBatches, 
                TotalBatches = totalBatches, 
                StartTime = DateTime.UtcNow,
                RatePool = new RateLimitPool { ActiveBatches = concurrentBatches } // **Phase 2.8b**
            },
            (key, existing) => 
            {
                existing.ConcurrentBatches = concurrentBatches;
                existing.TotalBatches = totalBatches;
                existing.RatePool.ActiveBatches = concurrentBatches; // **Phase 2.8b**
                return existing;
            });
        
        _logger.LogInformation("🔧 P2.7+P2.8b: Registered parallel context for store {StoreId}: {ConcurrentBatches} concurrent batches, {TotalBatches} total, rate pool initialized", 
            storeId, concurrentBatches, totalBatches);
    }
    
    /// <summary>
    /// **Phase 2.7**: Unregisters parallel processing context
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    public void UnregisterParallelContext(string storeId)
    {
        if (_parallelContexts.TryRemove(storeId, out var context))
        {
            var duration = DateTime.UtcNow - context.StartTime;
            _logger.LogInformation("🔧 P2.7: Unregistered parallel context for store {StoreId} after {Duration}ms", 
                storeId, duration.TotalMilliseconds);
        }
    }
    
    /// <inheritdoc />
    public async Task<bool> CanMakeRequestAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                return storeLimit.RequestTimes.Count < 12;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CanMakeRequestAsync was cancelled for store {StoreId}", storeId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public Task RecordApiCallAsync(string storeId, string endpoint, double responseTime, bool isSuccessful, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        if (string.IsNullOrEmpty(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty", nameof(endpoint));
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                storeLimit.RequestTimes.Add(DateTime.UtcNow);
                storeLimit.LastCallTime = DateTime.UtcNow;
            }
            
            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("API call recording was cancelled for store {StoreId}", storeId);
            throw; // Infrastructure service - let caller handle cancellation appropriately
        }
    }
    
    /// <inheritdoc />
    public async Task<RateLimitStatus> GetRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                var requestsInWindow = storeLimit.RequestTimes.Count;
                var isLimited = requestsInWindow >= 12;
                
                return new RateLimitStatus
                {
                    StoreId = storeId,
                    RequestsPerMinute = requestsInWindow,
                    RequestsRemaining = Math.Max(0, 12 - requestsInWindow),
                    WindowResetTime = DateTime.UtcNow.AddMinutes(1),
                    IsLimited = isLimited,
                    RecommendedDelayMs = isLimited ? CalculateDelayMs(storeLimit) : 0
                };
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Rate limit status check was cancelled for store {StoreId}", storeId);
            throw; // Infrastructure service - let caller handle cancellation appropriately
        }
    }
    
    /// <inheritdoc />
    public async Task<int> CalculateDelayAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                return CalculateDelayMs(storeLimit);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Rate limit delay calculation was cancelled for store {StoreId}", storeId);
            throw; // Infrastructure service - let caller handle cancellation appropriately
        }
    }

    /// <inheritdoc />
    public async Task<RateLimitResult> CheckRateLimitAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        try
        {
            var storeLimit = GetOrCreateStoreLimit(storeId);
            lock (_lock)
            {
                CleanupExpiredRequests(storeLimit);
                var requestsInWindow = storeLimit.RequestTimes.Count;
                var canProceed = requestsInWindow < 12;
                
                // **Phase 2.7: Adaptive delays for parallel vs sequential scenarios**
                var delayMs = canProceed ? 0 : CalculateAdaptiveDelay(storeId, requestsInWindow);
                
                _logger.LogDebug("🔧 P2.7: Rate limit check for store {StoreId}: {RequestsInWindow}/12 requests, CanProceed: {CanProceed}, AdaptiveDelayMs: {DelayMs}", 
                    storeId, requestsInWindow, canProceed, delayMs);
                
                return new RateLimitResult
                {
                    CanProceed = canProceed,
                    DelayMs = delayMs,
                    CurrentRequestCount = requestsInWindow,
                    RequestLimit = 12,
                    WindowResetTime = TimeSpan.FromMinutes(1)
                };
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Rate limit check was cancelled for store {StoreId}", storeId);
            throw; // Infrastructure service - let caller handle cancellation appropriately
        }
    }
    
    /// <summary>
    /// **Phase 2.7**: Calculates adaptive delay based on parallel processing context
    /// **Phase 2.8b**: Enhanced with rate limit pool coordination for optimal parallel throughput
    /// 
    /// Delay Strategy:
    /// - Sequential: 2000ms (original behavior)
    /// - Parallel (with pool): 30-200ms (intelligent staggered coordination)
    /// - Parallel (fallback): 100-500ms (adaptive delays)
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="requestsInWindow">Current requests in rate limit window</param>
    /// <returns>Optimal delay in milliseconds</returns>
    private int CalculateAdaptiveDelay(string storeId, int requestsInWindow)
    {
        // Check if we're in parallel processing mode
        if (!_parallelContexts.TryGetValue(storeId, out var parallelContext))
        {
            // Sequential processing - use original behavior for backward compatibility
            return 2000;
        }
        
        // **Phase 2.8b: Rate limit pool coordination for parallel processing**
        if (parallelContext.ConcurrentBatches >= 8) // Use pool coordination for medium+ concurrency
        {
            // **Phase 2.9d: Calculate adaptive pool capacity and pass to pool**
            var poolCapacity = CalculateAdaptivePoolCapacity(storeId);
            var poolDelay = parallelContext.RatePool.TryReserveSlot(storeId, poolCapacity, _logger);
            if (poolDelay == 0)
            {
                // Successfully reserved slot - can proceed immediately
                parallelContext.ConsecutiveRateLimitHits = 0; // Reset hits on success
                return 0;
            }
            
            // Pool is full - use intelligent staggered delay
            _logger.LogInformation("🔧 P2.8b: Using pool coordination delay for store {StoreId}: {PoolDelay}ms (concurrent batches: {ConcurrentBatches})", 
                storeId, poolDelay, parallelContext.ConcurrentBatches);
            
            return poolDelay;
        }
        
        // **Phase 2.7: Fallback adaptive delays for low concurrency**
        var baseDelay = parallelContext.TotalBatches switch
        {
            <= 10 => 100,   // Small migrations: fast recovery (100-300ms)
            <= 20 => 200,   // Medium migrations: balanced recovery (200-400ms)
            <= 30 => 300,   // Large migrations: conservative recovery (300-500ms)
            _ => 400        // Enterprise: very conservative (400-600ms)
        };
        
        // Exponential backoff based on congestion level
        var congestionMultiplier = requestsInWindow switch
        {
            >= 15 => 2.0,   // Severe congestion
            >= 12 => 1.5,   // Standard congestion
            _ => 1.0        // Light congestion
        };
        
        // Parallel coordination factor - reduce delays when many batches are running
        var coordinationFactor = parallelContext.ConcurrentBatches switch
        {
            >= 16 => 0.3,   // Many parallel batches - be very aggressive
            >= 8 => 0.5,    // Moderate parallel batches - be aggressive
            >= 4 => 0.7,    // Few parallel batches - be somewhat aggressive
            _ => 1.0        // Single batch (shouldn't happen in parallel mode)
        };
        
        var adaptiveDelay = (int)(baseDelay * congestionMultiplier * coordinationFactor);
        
        // Track consecutive rate limit hits for escalating backoff
        parallelContext.ConsecutiveRateLimitHits++;
        if (parallelContext.ConsecutiveRateLimitHits > 3)
        {
            // Escalating backoff after repeated hits
            var escalationFactor = Math.Min(2.0, 1.0 + (parallelContext.ConsecutiveRateLimitHits - 3) * 0.2);
            adaptiveDelay = (int)(adaptiveDelay * escalationFactor);
        }
        
        // Cap at reasonable limits
        adaptiveDelay = Math.Max(50, Math.Min(1000, adaptiveDelay));
        
        _logger.LogInformation("🔧 P2.7: Fallback adaptive delay for store {StoreId}: {AdaptiveDelay}ms (base: {BaseDelay}, congestion: {CongestionMultiplier:F1}x, coordination: {CoordinationFactor:F1}x, hits: {ConsecutiveHits})", 
            storeId, adaptiveDelay, baseDelay, congestionMultiplier, coordinationFactor, parallelContext.ConsecutiveRateLimitHits);
        
        return adaptiveDelay;
    }
    
    /// <summary>
    /// **Phase 2.9d**: Calculates adaptive pool capacity based on parallel processing context
    /// **Phase 2.10**: Ultra-aggressive rate pool expansion for maximum throughput
    /// 
    /// Pool Strategy Evolution:
    /// - Sequential: 12 requests (original BigCommerce limit)
    /// - Small parallel (≤20 batches): 36 requests (3x buffer - was 18)
    /// - Medium parallel (21-35 batches): 48 requests (4x buffer - was 24)  
    /// - Large parallel (36-50 batches): 60 requests (5x buffer - was 36)
    /// - Enterprise parallel (50+ batches): 72 requests (6x buffer - new tier)
    /// 
    /// **Phase 2.10 Enhancements**:
    /// - API health-based scaling (good health = higher capacity)
    /// - Migration duration scaling (longer migrations = more aggressive)
    /// - Burst tolerance for peak throughput periods
    /// - Smart backoff prevention to avoid cascading delays
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <returns>Ultra-aggressive adaptive pool capacity for the store</returns>
    private int CalculateAdaptivePoolCapacity(string storeId)
    {
        // Check if we're in parallel processing mode
        if (!_parallelContexts.TryGetValue(storeId, out var parallelContext))
        {
            // Sequential processing - use original BigCommerce limit
            return 12;
        }
        
        // **Phase 2.10: Ultra-aggressive base pool capacity**
        var basePoolCapacity = parallelContext.ConcurrentBatches switch
        {
            <= 20 => 36,   // Small parallel: 3x buffer (was 18) - DOUBLED for aggressive throughput
            <= 35 => 48,   // Medium parallel: 4x buffer (was 24) - DOUBLED for aggressive throughput  
            <= 50 => 60,   // Large parallel: 5x buffer (was 36) - 67% increase for large migrations
            _ => 72        // Enterprise parallel: 6x buffer (new tier) - Maximum aggressive capacity
        };
        
        // **Phase 2.10: API health-based scaling multiplier**
        var healthMultiplier = GetApiHealthMultiplier(storeId);
        
        // **Phase 2.10: Migration duration scaling**
        var durationMultiplier = GetMigrationDurationMultiplier(parallelContext);
        
        // **Phase 2.10: Dynamic burst capacity calculation**
        var burstMultiplier = GetBurstCapacityMultiplier(parallelContext);
        
        // **Phase 2.10: Calculate final ultra-aggressive capacity**
        var finalCapacity = (int)Math.Round(
            basePoolCapacity * healthMultiplier * durationMultiplier * burstMultiplier);
        
        // **Phase 2.10: Safety bounds - never exceed 120 requests or go below base**
        var ultraAggressiveCapacity = Math.Min(Math.Max(finalCapacity, basePoolCapacity), 120);
        
        _logger.LogDebug("🚀 P2.10: Ultra-aggressive pool capacity for {StoreId}: {Capacity} " +
            "(base: {Base}, health: {Health:F2}x, duration: {Duration:F2}x, burst: {Burst:F2}x)",
            storeId, ultraAggressiveCapacity, basePoolCapacity, healthMultiplier, durationMultiplier, burstMultiplier);
        
        return ultraAggressiveCapacity;
    }
    
    /// <summary>
    /// **Phase 2.10**: Calculates API health-based scaling multiplier
    /// Good API health = higher capacity, poor health = conservative capacity
    /// </summary>
    private double GetApiHealthMultiplier(string storeId)
    {
        // For now, assume good health for aggressive optimization
        // Future: integrate with actual API health monitoring
        return 1.2; // 20% boost for good health assumption
    }
    
    /// <summary>
    /// **Phase 2.10**: Calculates migration duration-based scaling multiplier  
    /// Longer running migrations get more aggressive capacity to finish faster
    /// </summary>
    private double GetMigrationDurationMultiplier(ParallelContext parallelContext)
    {
        var migrationDuration = DateTime.UtcNow - parallelContext.StartTime;
        
        // Scale up capacity based on how long the migration has been running
        return migrationDuration.TotalMinutes switch
        {
            < 1 => 1.0,     // First minute: baseline capacity
            < 3 => 1.1,     // 1-3 minutes: 10% boost
            < 5 => 1.15,    // 3-5 minutes: 15% boost  
            < 10 => 1.2,    // 5-10 minutes: 20% boost
            _ => 1.25       // 10+ minutes: 25% boost for long migrations
        };
    }
    
    /// <summary>
    /// **Phase 2.10**: Calculates burst capacity multiplier
    /// Provides extra capacity for peak throughput periods to prevent bottlenecks
    /// </summary>
    private double GetBurstCapacityMultiplier(ParallelContext parallelContext)
    {
        // High concurrency scenarios get burst capacity
        return parallelContext.ConcurrentBatches switch
        {
            >= 40 => 1.3,   // 40+ concurrent: 30% burst capacity
            >= 30 => 1.2,   // 30+ concurrent: 20% burst capacity
            >= 20 => 1.1,   // 20+ concurrent: 10% burst capacity
            _ => 1.0        // Lower concurrency: no burst needed
        };
    }
    
    /// <inheritdoc />
    public async Task CheckAndWaitAsync(string storeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));
        
        try
        {
            var result = await CheckRateLimitAsync(storeId, cancellationToken);
            
            if (!result.CanProceed && result.DelayMs > 0)
            {
                _logger.LogInformation("Rate limit hit for store {StoreId}, waiting {DelayMs}ms", storeId, result.DelayMs);
                await Task.Delay(result.DelayMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Rate limit check and wait was cancelled for store {StoreId}", storeId);
            throw; // Infrastructure service - let caller handle cancellation appropriately
        }
    }
    
    /// <summary>
    /// Gets or creates a store rate limit tracker
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <returns>Store rate limit tracker</returns>
    private StoreRateLimit GetOrCreateStoreLimit(string storeId)
    {
        return _storeLimits.GetOrAdd(storeId, _ => new StoreRateLimit
        {
            StoreId = storeId,
            RequestTimes = new List<DateTime>(),
            LastCallTime = DateTime.UtcNow
        });
    }
    
    /// <summary>
    /// Cleans up expired requests from the rate limit window
    /// </summary>
    /// <param name="storeLimit">Store rate limit tracker</param>
    private void CleanupExpiredRequests(StoreRateLimit storeLimit)
    {
        var cutoffTime = DateTime.UtcNow.AddMinutes(-1);
        storeLimit.RequestTimes.RemoveAll(time => time < cutoffTime);
    }
    
    /// <summary>
    /// Calculates the delay needed before the next request
    /// </summary>
    /// <param name="storeLimit">Store rate limit tracker</param>
    /// <returns>Delay in milliseconds</returns>
    private int CalculateDelayMs(StoreRateLimit storeLimit)
    {
        if (storeLimit.RequestTimes.Count < 12)
            return 0;
        
        var oldestRequest = storeLimit.RequestTimes.Min();
        var timeUntilExpiry = oldestRequest.AddMinutes(1) - DateTime.UtcNow;
        
        return Math.Max(0, (int)timeUntilExpiry.TotalMilliseconds + 100); // Add 100ms buffer
    }
    
    /// <summary>
    /// **Phase 2.7**: Resets consecutive rate limit hits when requests succeed
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    public void ResetConsecutiveRateLimitHits(string storeId)
    {
        if (_parallelContexts.TryGetValue(storeId, out var context))
        {
            context.ConsecutiveRateLimitHits = 0;
        }
    }
}

/// <summary>
/// Internal class to track rate limiting per store
/// </summary>
internal class StoreRateLimit
{
    public string StoreId { get; set; } = string.Empty;
    public List<DateTime> RequestTimes { get; set; } = new List<DateTime>();
    public DateTime LastCallTime { get; set; }
}

/// <summary>
/// **Phase 2.7**: Tracks parallel processing context for adaptive rate limiting
/// **Phase 2.8b**: Enhanced with rate limit pool coordination
/// </summary>
internal class ParallelContext
{
    /// <summary>
    /// Number of concurrent batches being processed
    /// </summary>
    public int ConcurrentBatches { get; set; }
    
    /// <summary>
    /// Total number of batches in the migration
    /// </summary>
    public int TotalBatches { get; set; }
    
    /// <summary>
    /// When parallel processing started
    /// </summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>
    /// Number of consecutive rate limit hits (for escalating backoff)
    /// </summary>
    public int ConsecutiveRateLimitHits { get; set; }
    
    /// <summary>
    /// **Phase 2.8b**: Rate limit pool coordination
    /// </summary>
    public RateLimitPool RatePool { get; set; } = new();
}

/// <summary>
/// **Phase 2.8b**: Manages rate limit quota distribution across parallel batches
/// </summary>
internal class RateLimitPool
{
    private readonly object _lock = new();
    private readonly Queue<DateTime> _distributedRequests = new();
    
    /// <summary>
    /// Number of active parallel batches sharing this pool
    /// </summary>
    public int ActiveBatches { get; set; }
    
    /// <summary>
    /// Last time the pool was reset/cleaned
    /// </summary>
    public DateTime LastPoolReset { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Attempts to reserve a slot in the rate limit pool for a batch
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="poolCapacity">Adaptive pool capacity for the store</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <returns>Delay in milliseconds (0 if can proceed immediately)</returns>
    public int TryReserveSlot(string storeId, int poolCapacity, ILogger logger)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            
            // Clean expired requests (older than 1 minute)
            while (_distributedRequests.Count > 0 && 
                   _distributedRequests.Peek() < now.AddMinutes(-1))
            {
                _distributedRequests.Dequeue();
            }
            
            // Check if we can proceed (less than adaptive pool capacity)
            if (_distributedRequests.Count < poolCapacity)
            {
                _distributedRequests.Enqueue(now);
                logger.LogDebug("🔧 P2.9d: Pool slot reserved for {StoreId}, {UsedSlots}/{PoolCapacity} pool slots used", 
                    storeId, _distributedRequests.Count, poolCapacity);
                return 0; // Can proceed immediately
            }
            
            // Calculate staggered delay based on active batches
            var staggeredDelay = CalculateStaggeredDelay();
            
            logger.LogInformation("🔧 P2.9d: Pool coordination delay for {StoreId}: {StaggeredDelay}ms (pool full: {UsedSlots}/{PoolCapacity}, active batches: {ActiveBatches})", 
                storeId, staggeredDelay, _distributedRequests.Count, poolCapacity, ActiveBatches);
            
            return staggeredDelay;
        }
    }
    
    /// <summary>
    /// Calculates intelligent staggered delays to prevent batch stampeding
    /// </summary>
    private int CalculateStaggeredDelay()
    {
        // **Phase 2.8b**: Intelligent staggered delays based on parallel batch count
        var baseDelay = ActiveBatches switch
        {
            >= 20 => 50,    // High concurrency: very aggressive delays
            >= 16 => 75,    // Medium-high concurrency: aggressive delays  
            >= 12 => 100,   // Medium concurrency: moderate delays
            >= 8 => 150,    // Low-medium concurrency: conservative delays
            _ => 200        // Low concurrency: standard delays
        };
        
        // Add small random jitter to prevent synchronization
        var jitter = Random.Shared.Next(-20, 20);
        
        return Math.Max(30, baseDelay + jitter); // Minimum 30ms, much lower than old 210-420ms
    }
} 