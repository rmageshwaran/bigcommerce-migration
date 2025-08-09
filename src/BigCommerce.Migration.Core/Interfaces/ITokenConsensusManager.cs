using BigCommerce.Migration.Core.Models.RateLimiting;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for distributed token consensus and allocation management
/// Provides atomic token operations with over-allocation prevention
/// </summary>
public interface ITokenConsensusManager
{
    /// <summary>
    /// Reserve tokens for the current instance based on quota health and active instances
    /// Implements fair distribution algorithm with over-allocation prevention
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of tokens reserved for this instance</returns>
    Task<int> ReserveTokensAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get available tokens for this instance without reserving them
    /// Used for decision making and reporting
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of available tokens</returns>
    Task<int> GetAvailableTokensAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to consume a single token atomically
    /// Returns false if no tokens are available
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if token was consumed successfully</returns>
    Task<bool> TryConsumeTokenAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release unused tokens back to the pool
    /// Should be called when operations are cancelled or completed early
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="tokensToRelease">Number of tokens to release</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ReleaseTokensAsync(string storeId, int tokensToRelease, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get total tokens allocated across all instances
    /// Used for monitoring and over-allocation detection
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total allocated tokens</returns>
    Task<int> GetTotalAllocatedTokensAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trigger proportional scale-back when quota health deteriorates
    /// Reduces all instances' allocations proportionally to stay within safe limits
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="maxSafeTokens">Maximum safe token limit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of instances that were scaled back</returns>
    Task<int> TriggerProportionalScaleBackAsync(string storeId, int maxSafeTokens, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current token allocation for this instance
    /// Provides detailed allocation status and metadata
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current allocation or null if none exists</returns>
    Task<TokenAllocationEntity?> GetInstanceAllocationAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active token allocations for a store
    /// Used for monitoring and health analysis
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of all active allocations</returns>
    Task<List<TokenAllocationEntity>> GetAllAllocationsAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up expired token allocations
    /// Should be called periodically to maintain data hygiene
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of expired allocations removed</returns>
    Task<int> CleanupExpiredAllocationsAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh token allocation expiry to keep allocation active
    /// Should be called periodically by active instances
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="expiryDuration">New expiry duration from now</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RefreshAllocationExpiryAsync(string storeId, TimeSpan expiryDuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get comprehensive health metrics for token consensus system
    /// Includes allocation efficiency, conflict rates, and system health indicators
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed consensus health metrics</returns>
    Task<ConsensusHealthMetrics> GetConsensusHealthAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculate fair token distribution across active instances
    /// Uses advanced algorithms to optimize allocation fairness
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recommended tokens per instance</returns>
    Task<int> CalculateFairTokenDistributionAsync(string storeId, CancellationToken cancellationToken = default);
}