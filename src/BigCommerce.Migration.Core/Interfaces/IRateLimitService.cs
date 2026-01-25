namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service for managing rate limiting across BigCommerce API calls
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Checks if a request can be made for the specified store
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if request can be made, false if rate limited</returns>
    Task<bool> CanMakeRequestAsync(string storeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Records an API call for rate limiting tracking
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="endpoint">API endpoint called</param>
    /// <param name="responseTime">Response time in milliseconds</param>
    /// <param name="isSuccessful">Whether the call was successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordApiCallAsync(string storeId, string endpoint, double responseTime, bool isSuccessful, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current rate limit status for a store
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rate limit status information</returns>
    Task<RateLimitStatus> GetRateLimitStatusAsync(string storeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Calculates the delay needed before next request
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Delay in milliseconds</returns>
    Task<int> CalculateDelayAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks rate limit status and returns delay information
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rate limit result with delay information</returns>
    Task<RateLimitResult> CheckRateLimitAsync(string storeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks rate limit and waits if necessary before allowing the request
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when request can proceed</returns>
    Task CheckAndWaitAsync(string storeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// **Phase 2.7**: Registers parallel processing context for adaptive rate limiting
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="concurrentBatches">Number of concurrent batches being processed</param>
    /// <param name="totalBatches">Total number of batches in the migration</param>
    void RegisterParallelContext(string storeId, int concurrentBatches, int totalBatches);
    
    /// <summary>
    /// **Phase 2.7**: Unregisters parallel processing context
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    void UnregisterParallelContext(string storeId);
    
    /// <summary>
    /// **Phase 2.7**: Resets consecutive rate limit hits when requests succeed
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    void ResetConsecutiveRateLimitHits(string storeId);
}

/// <summary>
/// Rate limit status information
/// </summary>
public class RateLimitStatus
{
    /// <summary>
    /// Store identifier
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Current requests per minute rate
    /// </summary>
    public int RequestsPerMinute { get; set; }
    
    /// <summary>
    /// Number of requests remaining in current window
    /// </summary>
    public int RequestsRemaining { get; set; }
    
    /// <summary>
    /// When the rate limit window resets
    /// </summary>
    public DateTime WindowResetTime { get; set; }
    
    /// <summary>
    /// Whether the store is currently rate limited
    /// </summary>
    public bool IsLimited { get; set; }
    
    /// <summary>
    /// Recommended delay before next request in milliseconds
    /// </summary>
    public int RecommendedDelayMs { get; set; }
}

 