using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Interface for distributed locking mechanism to prevent orchestrator instance collisions.
    /// Phase 3.1: Orchestrator Instance Management and Collision Detection
    /// </summary>
    public interface IDistributedLockService
    {
        /// <summary>
        /// Attempts to acquire a distributed lock for an orchestrator instance
        /// </summary>
        /// <param name="lockKey">Unique identifier for the lock (typically migration ID)</param>
        /// <param name="instanceId">Unique identifier for this orchestrator instance</param>
        /// <param name="leaseTime">How long to hold the lock before auto-expiry</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Lock acquisition result with success status and lock metadata</returns>
        Task<DistributedLockResult> TryAcquireLockAsync(
            string lockKey, 
            string instanceId, 
            TimeSpan leaseTime, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Renews an existing distributed lock to extend its lease time
        /// </summary>
        /// <param name="lockKey">Lock identifier</param>
        /// <param name="instanceId">Instance that owns the lock</param>
        /// <param name="leaseTime">New lease time</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Lock renewal result</returns>
        Task<DistributedLockResult> RenewLockAsync(
            string lockKey, 
            string instanceId, 
            TimeSpan leaseTime, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a distributed lock held by this instance
        /// </summary>
        /// <param name="lockKey">Lock identifier</param>
        /// <param name="instanceId">Instance that owns the lock</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if lock was successfully released</returns>
        Task<bool> ReleaseLockAsync(
            string lockKey, 
            string instanceId, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about the current lock holder for a given key
        /// </summary>
        /// <param name="lockKey">Lock identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Lock information or null if no lock exists</returns>
        Task<DistributedLockInfo?> GetLockInfoAsync(
            string lockKey, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a lock is currently held by any instance
        /// </summary>
        /// <param name="lockKey">Lock identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if lock is currently held</returns>
        Task<bool> IsLockHeldAsync(
            string lockKey, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Forces release of a lock (for cleanup scenarios)
        /// Should be used carefully and typically only by cleanup services
        /// </summary>
        /// <param name="lockKey">Lock identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if lock was successfully force-released</returns>
        Task<bool> ForceReleaseLockAsync(
            string lockKey, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about all active locks in the system (administrative operation)
        /// Phase 3.3: Added for cleanup service support
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of all active lock information</returns>
        Task<List<DistributedLockInfo>> GetAllLocksAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a distributed lock operation
    /// </summary>
    public class DistributedLockResult
    {
        /// <summary>
        /// Whether the lock operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Lock identifier
        /// </summary>
        public string LockKey { get; set; } = string.Empty;

        /// <summary>
        /// Instance that acquired/owns the lock
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// When the lock was acquired
        /// </summary>
        public DateTime AcquiredAt { get; set; }

        /// <summary>
        /// When the lock expires
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Reason for failure (if Success is false)
        /// </summary>
        public string? FailureReason { get; set; }

        /// <summary>
        /// Information about the current lock holder (if different instance)
        /// </summary>
        public DistributedLockInfo? CurrentLockHolder { get; set; }
    }

    /// <summary>
    /// Information about a distributed lock
    /// </summary>
    public class DistributedLockInfo
    {
        /// <summary>
        /// Lock identifier
        /// </summary>
        public string LockKey { get; set; } = string.Empty;

        /// <summary>
        /// Instance that holds the lock
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// When the lock was acquired
        /// </summary>
        public DateTime AcquiredAt { get; set; }

        /// <summary>
        /// When the lock expires
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Last heartbeat from the lock holder
        /// </summary>
        public DateTime LastHeartbeat { get; set; }

        /// <summary>
        /// Migration ID associated with this lock
        /// </summary>
        public string MigrationId { get; set; } = string.Empty;

        /// <summary>
        /// Current status of the lock
        /// </summary>
        public DistributedLockStatus Status { get; set; }

        /// <summary>
        /// Number of times this lock has been renewed
        /// </summary>
        public int RenewalCount { get; set; }
    }

    /// <summary>
    /// Status of a distributed lock
    /// </summary>
    public enum DistributedLockStatus
    {
        /// <summary>
        /// Lock is active and healthy
        /// </summary>
        Active,

        /// <summary>
        /// Lock is expired but not yet cleaned up
        /// </summary>
        Expired,

        /// <summary>
        /// Lock holder appears to be unresponsive (missed heartbeats)
        /// </summary>
        Stale,

        /// <summary>
        /// Lock is being released
        /// </summary>
        Releasing,

        /// <summary>
        /// Lock is in an unknown state
        /// </summary>
        Unknown
    }
} 