using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Distributed token consensus manager for fair API quota allocation across multiple instances
/// Implements atomic allocation, over-reservation prevention, and proportional scale-back algorithms
/// </summary>
public class TokenConsensusManager : ITokenConsensusManager
{
    private readonly IRateLimitingTableStorageFactory _tableStorageFactory;
    private readonly IDistributedQuotaTracker _quotaTracker;
    private readonly IInstanceCoordinationManager _coordinationManager;
    private readonly ILogger<TokenConsensusManager> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;
    private readonly string _instanceId;

    // Token allocation caching and throttling
    private readonly Dictionary<string, (DateTimeOffset LastAllocation, int CachedTokens)> _allocationCache = new();
    private readonly object _cacheLock = new object();

    /// <summary>
    /// Initializes a new instance of the TokenConsensusManager
    /// </summary>
    public TokenConsensusManager(
        IRateLimitingTableStorageFactory tableStorageFactory,
        IDistributedQuotaTracker quotaTracker,
        IInstanceCoordinationManager coordinationManager,
        IOptions<DynamicRateLimitingConfiguration> configuration,
        ILogger<TokenConsensusManager> logger)
    {
        _tableStorageFactory = tableStorageFactory ?? throw new ArgumentNullException(nameof(tableStorageFactory));
        _quotaTracker = quotaTracker ?? throw new ArgumentNullException(nameof(quotaTracker));
        _coordinationManager = coordinationManager ?? throw new ArgumentNullException(nameof(coordinationManager));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _instanceId = coordinationManager.GetCurrentInstanceId();

        _logger.LogInformation("Initialized TokenConsensusManager for instance {InstanceId}", _instanceId);
    }

    /// <inheritdoc />
    public async Task<int> ReserveTokensAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        // Check cache first to avoid excessive Table Storage operations
        lock (_cacheLock)
        {
            if (_allocationCache.TryGetValue(storeId, out var cached))
            {
                var timeSinceLastAllocation = DateTimeOffset.UtcNow - cached.LastAllocation;
                var expiryDuration = TimeSpan.FromSeconds(_configuration.Predictive.TokenExpirySeconds);
                
                if (timeSinceLastAllocation < expiryDuration && cached.CachedTokens > 0)
                {
                    _logger.LogTrace("Using cached token allocation for store {StoreId}: {CachedTokens} tokens", 
                        storeId, cached.CachedTokens);
                    return cached.CachedTokens;
                }
            }
        }

        try
        {
            // Ensure instance coordination is active
            await _coordinationManager.EnsureStoreRegistrationAsync(storeId, cancellationToken);

            // Get current quota state
            var quotaHealth = await _quotaTracker.GetQuotaHealthAsync(storeId, cancellationToken);
            
            if (quotaHealth.TotalQuota <= 0 || quotaHealth.SafeTokens <= 0)
            {
                _logger.LogDebug("No safe tokens available for store {StoreId}: {SafeTokens}/{TotalQuota}", 
                    storeId, quotaHealth.SafeTokens, quotaHealth.TotalQuota);
                return 0;
            }

            // Get active instance count for fair distribution
            var activeInstanceCount = await _coordinationManager.GetActiveInstanceCountAsync(storeId, cancellationToken);
            
            if (activeInstanceCount <= 0)
            {
                _logger.LogWarning("No active instances found for store {StoreId} - using single instance allocation", storeId);
                activeInstanceCount = 1;
            }

            // Calculate fair token allocation
            var fairAllocation = await CalculateFairTokenDistributionAsync(storeId, quotaHealth.SafeTokens, activeInstanceCount, cancellationToken);
            
            if (fairAllocation <= 0)
            {
                _logger.LogDebug("No tokens available for fair distribution to store {StoreId}", storeId);
                return 0;
            }

            // Perform atomic token reservation
            var reservedTokens = await PerformAtomicTokenReservationAsync(storeId, fairAllocation, quotaHealth, cancellationToken);

            // Update cache
            lock (_cacheLock)
            {
                _allocationCache[storeId] = (DateTimeOffset.UtcNow, reservedTokens);
            }

            _logger.LogDebug("Reserved {ReservedTokens} tokens for store {StoreId} (fair allocation: {FairAllocation}, instances: {InstanceCount})",
                reservedTokens, storeId, fairAllocation, activeInstanceCount);

            return reservedTokens;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reserve tokens for store {StoreId}. Returning safe fallback value to avoid breaking application functionality.", storeId);
            
            // Return a conservative fallback allocation instead of throwing
            // This ensures the application continues to function even if rate limiting fails
            const int SafeFallbackTokens = 10; // Conservative fallback to prevent API overwhelm
            _logger.LogWarning("Using safe fallback allocation of {FallbackTokens} tokens for store {StoreId} due to rate limiting error", 
                SafeFallbackTokens, storeId);
            
            return SafeFallbackTokens;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetAvailableTokensAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetTokenTableClientAsync(cancellationToken);
            
            var existingResponse = await tableClient.GetEntityIfExistsAsync<TokenAllocationEntity>(
                storeId, _instanceId, cancellationToken: cancellationToken);

            if (!existingResponse.HasValue)
            {
                _logger.LogTrace("No token allocation found for instance {InstanceId} on store {StoreId}", _instanceId, storeId);
                return 0;
            }

            var allocation = existingResponse.Value;
            
            if (allocation?.IsExpired == true)
            {
                _logger.LogTrace("Token allocation expired for instance {InstanceId} on store {StoreId}", _instanceId, storeId);
                return 0;
            }

            if (allocation == null)
            {
                _logger.LogTrace("No allocation found for instance {InstanceId} on store {StoreId}", _instanceId, storeId);
                return 0;
            }

            return allocation.AvailableTokens;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get available tokens for store {StoreId}", storeId);
            return 0; // Conservative fallback
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeTokenAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        const int maxRetries = 3;
        var baseDelay = 50;
        var random = new Random();

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var tableClient = await _tableStorageFactory.GetTokenTableClientAsync(cancellationToken);
                
                var existingResponse = await tableClient.GetEntityIfExistsAsync<TokenAllocationEntity>(
                    storeId, _instanceId, cancellationToken: cancellationToken);

                if (!existingResponse.HasValue)
                {
                    _logger.LogTrace("No token allocation found for instance {InstanceId} on store {StoreId} - cannot consume", 
                        _instanceId, storeId);
                    return false;
                }

                var allocation = existingResponse.Value;
                
                if (allocation?.IsExpired == true)
                {
                    _logger.LogTrace("Token allocation expired for instance {InstanceId} on store {StoreId} - cannot consume", 
                        _instanceId, storeId);
                    return false;
                }

                if (allocation == null)
                {
                    _logger.LogTrace("No allocation found for instance {InstanceId} on store {StoreId} - cannot consume", 
                        _instanceId, storeId);
                    return false;
                }

                if (!allocation.HasAvailableTokens)
                {
                    _logger.LogTrace("No available tokens for instance {InstanceId} on store {StoreId}", 
                        _instanceId, storeId);
                    return false;
                }

                // Atomic token consumption
                if (allocation.TryConsumeToken())
                {
                    await tableClient.UpdateEntityAsync(allocation, allocation.ETag, cancellationToken: cancellationToken);
                    
                    _logger.LogTrace("Consumed 1 token for instance {InstanceId} on store {StoreId} ({Available} remaining)",
                        _instanceId, storeId, allocation.AvailableTokens);
                    
                    // Update cache
                    lock (_cacheLock)
                    {
                        if (_allocationCache.TryGetValue(storeId, out var cached))
                        {
                            _allocationCache[storeId] = (cached.LastAllocation, Math.Max(0, cached.CachedTokens - 1));
                        }
                    }

                    return true;
                }
                else
                {
                    _logger.LogTrace("Failed to consume token - none available for instance {InstanceId} on store {StoreId}", 
                        _instanceId, storeId);
                    return false;
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
            {
                var jitter = random.Next(0, 50);
                var delay = (int)(baseDelay * Math.Pow(2, retry)) + jitter;
                
                _logger.LogTrace("ETag conflict consuming token for store {StoreId}, retry {Retry}/{Max} (delay: {Delay}ms)",
                    storeId, retry + 1, maxRetries, delay);

                if (retry < maxRetries - 1)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to consume token for store {StoreId} on attempt {Retry}/{Max}",
                    storeId, retry + 1, maxRetries);
                
                if (retry == maxRetries - 1)
                {
                    throw;
                }
            }
        }

        _logger.LogWarning("Failed to consume token for store {StoreId} after {Retries} retries due to ETag conflicts",
            storeId, maxRetries);
        return false;
    }

    /// <inheritdoc />
    public async Task ReleaseTokensAsync(string storeId, int tokensToRelease, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        if (tokensToRelease <= 0)
            return;

        try
        {
            var tableClient = await _tableStorageFactory.GetTokenTableClientAsync(cancellationToken);
            
            var existingResponse = await tableClient.GetEntityIfExistsAsync<TokenAllocationEntity>(
                storeId, _instanceId, cancellationToken: cancellationToken);

            if (!existingResponse.HasValue)
            {
                _logger.LogDebug("No token allocation found for instance {InstanceId} on store {StoreId} - cannot release tokens", 
                    _instanceId, storeId);
                return;
            }

            var allocation = existingResponse.Value;
            
            if (allocation == null)
            {
                _logger.LogWarning("Null allocation found for instance {InstanceId} on store {StoreId}", _instanceId, storeId);
                return;
            }

            // Add tokens back to available pool
            allocation.AvailableTokens += tokensToRelease;
            allocation.LastUpdateReason = $"Released {tokensToRelease} tokens";

            await tableClient.UpdateEntityAsync(allocation, allocation.ETag, cancellationToken: cancellationToken);
            
            _logger.LogDebug("Released {ReleasedTokens} tokens for instance {InstanceId} on store {StoreId} ({Available} total available)",
                tokensToRelease, _instanceId, storeId, allocation.AvailableTokens);

            // Update cache
            lock (_cacheLock)
            {
                if (_allocationCache.TryGetValue(storeId, out var cached))
                {
                    _allocationCache[storeId] = (cached.LastAllocation, cached.CachedTokens + tokensToRelease);
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
        {
            _logger.LogDebug("ETag conflict releasing tokens for store {StoreId} - will retry on next allocation", storeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release {TokensToRelease} tokens for store {StoreId}", tokensToRelease, storeId);
        }
    }

    /// <inheritdoc />
    public async Task<int> GetTotalAllocatedTokensAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var allocations = await GetAllAllocationsAsync(storeId, cancellationToken);
            return allocations.Where(a => !a.IsExpired).Sum(a => a.AllocatedTokens);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get total allocated tokens for store {StoreId}", storeId);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<int> TriggerProportionalScaleBackAsync(string storeId, int maxSafeTokens, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        if (maxSafeTokens <= 0)
            return 0;

        try
        {
            var allocations = await GetAllAllocationsAsync(storeId, cancellationToken);
            var activeAllocations = allocations.Where(a => !a.IsExpired).ToList();
            
            if (activeAllocations.Count == 0)
            {
                _logger.LogDebug("No active allocations found for store {StoreId} - no scale-back needed", storeId);
                return 0;
            }

            var totalAllocated = activeAllocations.Sum(a => a.AllocatedTokens);
            
            if (totalAllocated <= maxSafeTokens)
            {
                _logger.LogDebug("Total allocated tokens ({TotalAllocated}) within safe limit ({MaxSafe}) for store {StoreId} - no scale-back needed",
                    totalAllocated, maxSafeTokens, storeId);
                return 0;
            }

            // Calculate proportional scale-back factor
            var scaleBackFactor = (double)maxSafeTokens / totalAllocated;
            var totalScaledBack = 0;
            
            _logger.LogInformation("Triggering proportional scale-back for store {StoreId}: {TotalAllocated} → {MaxSafe} tokens (factor: {ScaleFactor:F3})",
                storeId, totalAllocated, maxSafeTokens, scaleBackFactor);

            // Apply proportional scale-back to each allocation
            var scaleBackTasks = activeAllocations.Select(allocation => 
                ScaleBackAllocationAsync(storeId, allocation, scaleBackFactor, cancellationToken));

            var scaleBackResults = await Task.WhenAll(scaleBackTasks);
            totalScaledBack = scaleBackResults.Sum();

            _logger.LogInformation("Completed proportional scale-back for store {StoreId}: reduced by {TotalScaledBack} tokens",
                storeId, totalScaledBack);

            return totalScaledBack;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger proportional scale-back for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<TokenAllocationEntity?> GetInstanceAllocationAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetTokenTableClientAsync(cancellationToken);
            
            var response = await tableClient.GetEntityIfExistsAsync<TokenAllocationEntity>(
                storeId, _instanceId, cancellationToken: cancellationToken);

            return response.HasValue ? response.Value : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get instance allocation for store {StoreId}", storeId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<TokenAllocationEntity>> GetAllAllocationsAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetTokenTableClientAsync(cancellationToken);
            
            var query = tableClient.QueryAsync<TokenAllocationEntity>(
                filter: $"PartitionKey eq '{storeId}'",
                cancellationToken: cancellationToken);

            var allocations = new List<TokenAllocationEntity>();
            
            await foreach (var allocation in query.ConfigureAwait(false))
            {
                if (allocation != null)
                {
                    allocations.Add(allocation);
                }
            }

            return allocations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all allocations for store {StoreId}", storeId);
            // Return empty list instead of throwing to avoid breaking functionality
            return new List<TokenAllocationEntity>();
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAllocationsAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var allocations = await GetAllAllocationsAsync(storeId, cancellationToken);
            var expiredAllocations = allocations.Where(a => a.IsExpired).ToList();
            
            if (expiredAllocations.Count == 0)
            {
                _logger.LogTrace("No expired allocations found for store {StoreId}", storeId);
                return 0;
            }

            var tableClient = await _tableStorageFactory.GetTokenTableClientAsync(cancellationToken);
            
            // Delete expired allocations in parallel
            var deleteTasks = expiredAllocations.Select(async allocation =>
            {
                try
                {
                    await tableClient.DeleteEntityAsync(allocation.PartitionKey, allocation.RowKey, 
                        allocation.ETag, cancellationToken);
                    
                    _logger.LogTrace("Deleted expired allocation for instance {InstanceId} on store {StoreId}",
                        allocation.RowKey, storeId);
                    return 1;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Already deleted - that's fine
                    return 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete expired allocation for instance {InstanceId} on store {StoreId}",
                        allocation.RowKey, storeId);
                    return 0;
                }
            });

            var results = await Task.WhenAll(deleteTasks);
            var cleanedCount = results.Sum();

            if (cleanedCount > 0)
            {
                _logger.LogInformation("Cleaned up {CleanedCount} expired token allocations for store {StoreId}", 
                    cleanedCount, storeId);
            }

            return cleanedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired allocations for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RefreshAllocationExpiryAsync(string storeId, TimeSpan expiryDuration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var tableClient = await _tableStorageFactory.GetTokenTableClientAsync(cancellationToken);
            
            var existingResponse = await tableClient.GetEntityIfExistsAsync<TokenAllocationEntity>(
                storeId, _instanceId, cancellationToken: cancellationToken);

            if (!existingResponse.HasValue)
            {
                _logger.LogTrace("No allocation found to refresh expiry for instance {InstanceId} on store {StoreId}", 
                    _instanceId, storeId);
                return;
            }

            var allocation = existingResponse.Value;
            if (allocation == null)
            {
                _logger.LogWarning("Null allocation found for instance {InstanceId} on store {StoreId}", _instanceId, storeId);
                return;
            }
            allocation.RefreshExpiry(expiryDuration);

            await tableClient.UpdateEntityAsync(allocation, allocation.ETag, cancellationToken: cancellationToken);
            
            _logger.LogTrace("Refreshed allocation expiry for instance {InstanceId} on store {StoreId} (expires: {ExpiresAt})",
                _instanceId, storeId, allocation.ExpiresAt);
        }
        catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
        {
            _logger.LogTrace("ETag conflict refreshing allocation expiry for store {StoreId}", storeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh allocation expiry for store {StoreId}", storeId);
        }
    }

    /// <inheritdoc />
    public async Task<ConsensusHealthMetrics> GetConsensusHealthAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var allocations = await GetAllAllocationsAsync(storeId, cancellationToken);
            var activeAllocations = allocations.Where(a => !a.IsExpired).ToList();
            var expiredCount = allocations.Count - activeAllocations.Count;
            
            var totalAllocated = activeAllocations.Sum(a => a.AllocatedTokens);
            var totalAvailable = activeAllocations.Sum(a => a.AvailableTokens);
            var totalConsumed = activeAllocations.Sum(a => a.TokensConsumed);
            
            var averageEfficiency = activeAllocations.Count > 0 
                ? activeAllocations.Average(a => a.AllocationEfficiency) 
                : 0.0;
            
            var etagConflicts = activeAllocations.Sum(a => a.ETagConflictCount);
            
            // Calculate consensus health
            var healthScore = CalculateConsensusHealthScore(activeAllocations.Count(), averageEfficiency, etagConflicts);
            var healthStatus = DetermineConsensusHealthStatus(healthScore, expiredCount);

            return new ConsensusHealthMetrics
            {
                StoreId = storeId,
                ActiveAllocations = activeAllocations.Count(),
                ExpiredAllocations = expiredCount,
                TotalAllocatedTokens = totalAllocated,
                TotalAvailableTokens = totalAvailable,
                TotalConsumedTokens = totalConsumed,
                AverageAllocationEfficiency = averageEfficiency,
                TotalETagConflicts = etagConflicts,
                HealthScore = healthScore,
                HealthStatus = healthStatus
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get consensus health for store {StoreId}", storeId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> CalculateFairTokenDistributionAsync(string storeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            var quotaHealth = await _quotaTracker.GetQuotaHealthAsync(storeId, cancellationToken);
            var activeInstanceCount = await _coordinationManager.GetActiveInstanceCountAsync(storeId, cancellationToken);
            
            return await CalculateFairTokenDistributionAsync(storeId, quotaHealth.SafeTokens, activeInstanceCount, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate fair token distribution for store {StoreId}", storeId);
            throw;
        }
    }

    // Private helper methods continue in next part due to length...
    
    /// <summary>
    /// Calculates fair token distribution based on available tokens and active instances
    /// </summary>
    private async Task<int> CalculateFairTokenDistributionAsync(string storeId, int safeTokens, int activeInstanceCount, CancellationToken cancellationToken)
    {
        if (safeTokens <= 0 || activeInstanceCount <= 0)
            return 0;

        try
        {
            // Get current total allocated tokens to prevent over-allocation
            var totalAllocated = await GetTotalAllocatedTokensAsync(storeId, cancellationToken);
            var availableForDistribution = Math.Max(0, safeTokens - totalAllocated);
            
            if (availableForDistribution <= 0)
            {
                _logger.LogTrace("No tokens available for distribution to store {StoreId}: {SafeTokens} safe, {TotalAllocated} allocated",
                    storeId, safeTokens, totalAllocated);
                return 0;
            }

            // Fair distribution: divide equally among active instances
            var fairShare = availableForDistribution / activeInstanceCount;
            
            // Reserve some tokens for late-joining instances (10% buffer)
            var reserveBuffer = Math.Max(1, availableForDistribution / 10);
            var distributionAmount = Math.Max(0, fairShare - (reserveBuffer / activeInstanceCount));

            _logger.LogTrace("Fair token distribution for store {StoreId}: {DistributionAmount} per instance " +
                             "({AvailableForDistribution} available / {ActiveInstanceCount} instances, {ReserveBuffer} reserved)",
                storeId, distributionAmount, availableForDistribution, activeInstanceCount, reserveBuffer);

            return distributionAmount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate fair distribution for store {StoreId}", storeId);
            return 0;
        }
    }

    /// <summary>
    /// Performs atomic token reservation with conflict resolution
    /// </summary>
    private async Task<int> PerformAtomicTokenReservationAsync(string storeId, int requestedTokens, QuotaHealthMetrics quotaHealth, CancellationToken cancellationToken)
    {
        if (quotaHealth == null)
        {
            _logger.LogWarning("Quota health is null for store {StoreId}. Using safe fallback allocation.", storeId);
            return Math.Min(requestedTokens, 5); // Very conservative fallback
        }

        const int maxRetries = 5;
        var baseDelay = 100;
        var random = new Random();

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var tableClient = await _tableStorageFactory.GetTokenTableClientAsync(cancellationToken);
                var expiryDuration = TimeSpan.FromSeconds(_configuration.Predictive.TokenExpirySeconds);

                // Get or create token allocation entity
                var existingResponse = await tableClient.GetEntityIfExistsAsync<TokenAllocationEntity>(
                    storeId, _instanceId, cancellationToken: cancellationToken);

                TokenAllocationEntity allocation;
                bool isNewAllocation = false;

                if (existingResponse.HasValue)
                {
                    var existingAllocation = existingResponse.Value;
                    if (existingAllocation == null)
                    {
                        _logger.LogWarning("Null allocation found for instance {InstanceId} on store {StoreId}", _instanceId, storeId);
                        allocation = TokenAllocationEntity.CreateNew(storeId, _instanceId, requestedTokens, expiryDuration, "Initial allocation");
                        isNewAllocation = true;
                    }
                    else
                    {
                        allocation = existingAllocation;
                        
                        if (allocation.IsExpired)
                        {
                            // Reset expired allocation
                            allocation.AllocateTokens(requestedTokens, expiryDuration, quotaHealth?.ToString() ?? "Unknown");
                        }
                        else
                        {
                            // Refresh existing allocation if needed
                            var additionalTokens = Math.Max(0, requestedTokens - allocation.AvailableTokens);
                            if (additionalTokens > 0)
                            {
                                allocation.AllocateTokens(allocation.AllocatedTokens + additionalTokens, expiryDuration, 
                                    $"Added {additionalTokens} tokens");
                            }
                        }
                    }
                }
                else
                {
                    // Create new allocation
                    allocation = TokenAllocationEntity.CreateNew(storeId, _instanceId, requestedTokens, expiryDuration, "Initial allocation");
                    isNewAllocation = true;
                }

                // Atomic operation
                if (isNewAllocation)
                {
                    await tableClient.AddEntityAsync(allocation, cancellationToken);
                }
                else
                {
                    await tableClient.UpdateEntityAsync(allocation, allocation.ETag, cancellationToken: cancellationToken);
                }

                _logger.LogDebug("Successfully reserved {ReservedTokens} tokens for instance {InstanceId} on store {StoreId}",
                    allocation.AllocatedTokens, _instanceId, storeId);

                return allocation.AvailableTokens;
            }
            catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
            {
                var jitter = random.Next(0, 100);
                var delay = (int)(baseDelay * Math.Pow(1.5, retry)) + jitter;
                
                _logger.LogDebug("ETag conflict reserving tokens for store {StoreId}, retry {Retry}/{Max} (delay: {Delay}ms)",
                    storeId, retry + 1, maxRetries, delay);

                if (retry < maxRetries - 1)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reserve tokens for store {StoreId} on attempt {Retry}/{Max}",
                    storeId, retry + 1, maxRetries);
                
                if (retry == maxRetries - 1)
                {
                    throw;
                }
            }
        }

        _logger.LogWarning("Failed to reserve tokens for store {StoreId} after {Retries} retries due to ETag conflicts",
            storeId, maxRetries);
        return 0;
    }

    /// <summary>
    /// Scales back an individual allocation proportionally
    /// </summary>
    private async Task<int> ScaleBackAllocationAsync(string storeId, TokenAllocationEntity allocation, double scaleBackFactor, CancellationToken cancellationToken)
    {
        try
        {
            var originalTokens = allocation.AllocatedTokens;
            var newTokenCount = Math.Max(1, (int)(originalTokens * scaleBackFactor)); // Minimum 1 token
            var tokensRemoved = originalTokens - newTokenCount;

            if (tokensRemoved <= 0)
                return 0;

            allocation.ScaleBack(newTokenCount, $"Proportional scale-back by {tokensRemoved} tokens");
            
            var tableClient = await _tableStorageFactory.GetTokenTableClientAsync(cancellationToken);
            await tableClient.UpdateEntityAsync(allocation, allocation.ETag, cancellationToken: cancellationToken);
            
            _logger.LogDebug("Scaled back allocation for instance {InstanceId} on store {StoreId}: {OriginalTokens} → {NewTokens} tokens",
                allocation.RowKey, storeId, originalTokens, newTokenCount);

            return tokensRemoved;
        }
        catch (RequestFailedException ex) when (ex.Status == 412) // ETag conflict
        {
            _logger.LogDebug("ETag conflict during scale-back for instance {InstanceId} on store {StoreId}",
                allocation.RowKey, storeId);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scale back allocation for instance {InstanceId} on store {StoreId}",
                allocation.RowKey, storeId);
            return 0;
        }
    }

    /// <summary>
    /// Calculates consensus health score based on system metrics
    /// </summary>
    private double CalculateConsensusHealthScore(int activeAllocations, double averageEfficiency, int etagConflicts)
    {
        var baseScore = 1.0;
        
        // Penalize low allocation counts (indicates coordination issues)
        if (activeAllocations == 0)
            baseScore *= 0.0;
        else if (activeAllocations == 1)
            baseScore *= 0.7; // Single instance is okay but not optimal
        
        // Factor in allocation efficiency
        baseScore *= averageEfficiency;
        
        // Penalize high ETag conflict rates
        var conflictPenalty = Math.Min(0.5, etagConflicts * 0.01); // Max 50% penalty
        baseScore *= (1.0 - conflictPenalty);
        
        return Math.Max(0.0, Math.Min(1.0, baseScore));
    }

    /// <summary>
    /// Determines consensus health status based on score and metrics
    /// </summary>
    private string DetermineConsensusHealthStatus(double healthScore, int expiredAllocations)
    {
        if (healthScore < 0.3 || expiredAllocations > 10)
            return "Critical";
        
        if (healthScore < 0.7 || expiredAllocations > 5)
            return "Warning";
        
        return "Healthy";
    }
}