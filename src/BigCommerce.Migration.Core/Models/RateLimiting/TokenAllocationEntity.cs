using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models.RateLimiting;

/// <summary>
/// Azure Table Storage entity for distributed token allocation and consensus
/// Manages atomic token reservations across multiple instances
/// </summary>
public class TokenAllocationEntity : ITableEntity
{
    /// <summary>
    /// Partition key: Store ID (e.g., "store-12345")
    /// Groups all token allocations by store
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key: Instance ID (e.g., "web-01-12345-a1b2c3d4")
    /// Unique token allocation per instance per store
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// ETag for optimistic concurrency control
    /// CRITICAL: Enables atomic Compare-And-Swap operations
    /// </summary>
    public ETag ETag { get; set; }

    /// <summary>
    /// Timestamp for Azure Table Storage
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Number of tokens allocated to this instance
    /// Result of distributed consensus algorithm
    /// </summary>
    [Required]
    public int AllocatedTokens { get; set; }

    /// <summary>
    /// Number of tokens currently available for use
    /// Decreases as instance consumes tokens
    /// </summary>
    [Required]
    public int AvailableTokens { get; set; }

    /// <summary>
    /// When this token allocation expires (UTC)
    /// Prevents token hoarding by dead instances
    /// </summary>
    [Required]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// When this allocation was created/last refreshed (UTC)
    /// </summary>
    [Required]
    public DateTimeOffset LastRefresh { get; set; }

    /// <summary>
    /// ETag of the quota snapshot used for this allocation
    /// Links token allocation to specific quota state
    /// </summary>
    public string? QuotaSnapshotETag { get; set; }

    /// <summary>
    /// Number of ETag conflicts encountered during allocation
    /// Performance and contention monitoring
    /// </summary>
    public int ETagConflictCount { get; set; } = 0;

    /// <summary>
    /// Total tokens consumed by this instance since allocation
    /// For efficiency tracking and debugging
    /// </summary>
    public int TokensConsumed { get; set; } = 0;

    /// <summary>
    /// Allocation efficiency: consumed / allocated ratio
    /// Used for token waste monitoring
    /// </summary>
    public double AllocationEfficiency => AllocatedTokens > 0 ? (double)TokensConsumed / AllocatedTokens : 0.0;

    /// <summary>
    /// Reason for last allocation update
    /// For debugging and audit purposes
    /// </summary>
    public string? LastUpdateReason { get; set; }

    /// <summary>
    /// Checks if this token allocation has expired
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    /// <summary>
    /// Checks if tokens are available for consumption
    /// </summary>
    public bool HasAvailableTokens => AvailableTokens > 0 && !IsExpired;

    /// <summary>
    /// Consumes a token if available
    /// Returns true if token was consumed, false if none available
    /// </summary>
    public bool TryConsumeToken()
    {
        if (!HasAvailableTokens) return false;

        AvailableTokens--;
        TokensConsumed++;
        return true;
    }

    /// <summary>
    /// Allocates tokens to this instance with expiry time
    /// </summary>
    public void AllocateTokens(int tokens, TimeSpan expiryDuration, string? quotaETag = null, string? reason = null)
    {
        AllocatedTokens = tokens;
        AvailableTokens = tokens;
        TokensConsumed = 0;
        LastRefresh = DateTimeOffset.UtcNow;
        ExpiresAt = LastRefresh.Add(expiryDuration);
        QuotaSnapshotETag = quotaETag;
        LastUpdateReason = reason;
    }

    /// <summary>
    /// Updates allocation due to proportional scale-back
    /// Maintains consumption tracking while reducing available tokens
    /// </summary>
    public void ScaleBack(int newAllocation, string reason)
    {
        // Proportionally reduce available tokens
        var consumedRatio = AllocatedTokens > 0 ? (double)TokensConsumed / AllocatedTokens : 0.0;
        var newConsumed = (int)(newAllocation * consumedRatio);
        
        AllocatedTokens = newAllocation;
        AvailableTokens = Math.Max(0, newAllocation - newConsumed);
        TokensConsumed = newConsumed;
        LastRefresh = DateTimeOffset.UtcNow;
        LastUpdateReason = $"Scale-back: {reason}";
    }

    /// <summary>
    /// Records an ETag conflict for monitoring
    /// </summary>
    public void RecordETagConflict()
    {
        ETagConflictCount++;
    }

    /// <summary>
    /// Refreshes the allocation with new expiry time
    /// Used for extending token validity
    /// </summary>
    public void RefreshExpiry(TimeSpan expiryDuration)
    {
        LastRefresh = DateTimeOffset.UtcNow;
        ExpiresAt = LastRefresh.Add(expiryDuration);
    }

    /// <summary>
    /// Creates a new token allocation entity
    /// </summary>
    public static TokenAllocationEntity CreateNew(string storeId, string instanceId, int tokens, TimeSpan expiryDuration, string? quotaETag = null)
    {
        var entity = new TokenAllocationEntity
        {
            PartitionKey = storeId,
            RowKey = instanceId
        };
        
        entity.AllocateTokens(tokens, expiryDuration, quotaETag, "Initial allocation");
        return entity;
    }

    /// <summary>
    /// Checks if this allocation is healthy (not expired, has tokens, low conflicts)
    /// </summary>
    public bool IsHealthy(int maxETagConflicts = 10)
    {
        return !IsExpired && 
               AvailableTokens >= 0 && 
               ETagConflictCount <= maxETagConflicts;
    }

    /// <summary>
    /// Returns a string representation of the token allocation entity
    /// </summary>
    /// <returns>String with token counts, expiry time, and allocation efficiency</returns>
    public override string ToString()
    {
        var remainingTime = ExpiresAt - DateTimeOffset.UtcNow;
        return $"TokenAllocation[{RowKey}]: {AvailableTokens}/{AllocatedTokens} tokens, Expires: {remainingTime.TotalSeconds:F0}s, Efficiency: {AllocationEfficiency:P1}";
    }
}