using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models.RateLimiting;

/// <summary>
/// Azure Table Storage entity for multi-instance coordination and heartbeat tracking
/// Enables accurate instance discovery for fair token distribution
/// </summary>
public class InstanceCoordinationEntity : ITableEntity
{
    /// <summary>
    /// Partition key: Store ID (e.g., "store-12345")
    /// Groups all instances by store for efficient coordination
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key: Instance ID (e.g., "web-01-12345-a1b2c3d4")
    /// Unique identifier for each application instance
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// ETag for optimistic concurrency control
    /// </summary>
    public ETag ETag { get; set; }

    /// <summary>
    /// Timestamp for Azure Table Storage
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// When this instance last sent a heartbeat (UTC)
    /// CRITICAL: Used for phantom instance detection and cleanup
    /// </summary>
    [Required]
    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// Machine name where this instance is running
    /// For debugging and operational visibility
    /// </summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Process ID of this instance
    /// Helps distinguish multiple instances on same machine
    /// </summary>
    public int ProcessId { get; set; } = Environment.ProcessId;

    /// <summary>
    /// When this instance was started (UTC)
    /// For operational tracking and debugging
    /// </summary>
    [Required]
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Total number of API requests processed by this instance
    /// For load balancing and performance monitoring
    /// </summary>
    public long TotalRequestsProcessed { get; set; } = 0;

    /// <summary>
    /// Current load factor for this instance (0.0 to 1.0)
    /// Could be used for intelligent token distribution in future
    /// </summary>
    public double CurrentLoadFactor { get; set; } = 0.0;

    /// <summary>
    /// Version of the rate limiting implementation
    /// For compatibility tracking during rolling deployments
    /// </summary>
    public string RateLimitingVersion { get; set; } = "1.0";

    /// <summary>
    /// Last known health status of this instance
    /// For circuit breaker and failover decisions
    /// </summary>
    public string HealthStatus { get; set; } = "Healthy";

    /// <summary>
    /// Checks if this instance is considered active based on heartbeat
    /// </summary>
    public bool IsActive(TimeSpan timeoutPeriod)
    {
        return DateTimeOffset.UtcNow - LastHeartbeat <= timeoutPeriod;
    }

    /// <summary>
    /// Updates the heartbeat to current time
    /// Call this every 60 seconds to maintain active status
    /// </summary>
    public void UpdateHeartbeat()
    {
        LastHeartbeat = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records request processing for load tracking
    /// </summary>
    public void RecordRequestProcessed()
    {
        TotalRequestsProcessed++;
        UpdateHeartbeat(); // Heartbeat on activity
    }

    /// <summary>
    /// Updates the current load factor
    /// </summary>
    public void UpdateLoadFactor(double loadFactor)
    {
        CurrentLoadFactor = Math.Max(0.0, Math.Min(1.0, loadFactor));
        UpdateHeartbeat();
    }

    /// <summary>
    /// Creates a new instance entity with current timestamp
    /// Follows AzureTableDistributedLockService instance ID pattern
    /// </summary>
    public static InstanceCoordinationEntity CreateNew(string storeId, string instanceId)
    {
        var now = DateTimeOffset.UtcNow;
        return new InstanceCoordinationEntity
        {
            PartitionKey = storeId,
            RowKey = instanceId,
            LastHeartbeat = now,
            StartedAt = now,
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId
        };
    }

    /// <summary>
    /// Generates a unique instance ID following existing patterns
    /// Format: "MachineName-ProcessId-ShortGuid"
    /// </summary>
    public static string GenerateInstanceId()
    {
        // Follow AzureTableDistributedLockService pattern
        return $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    /// <summary>
    /// Returns a string representation of the instance coordination entity including key metrics
    /// </summary>
    /// <returns>String representation with instance details</returns>
    public override string ToString()
    {
        var age = DateTimeOffset.UtcNow - LastHeartbeat;
        return $"Instance[{RowKey}]: {MachineName}, Heartbeat: {age.TotalSeconds:F0}s ago, Requests: {TotalRequestsProcessed}";
    }
}