using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Interface for managing heartbeat signals for distributed locks to prevent phantom orchestrators
    /// Phase 3.2: Instance Heartbeat Mechanism
    /// </summary>
    public interface IDistributedLockHeartbeatService
    {
        /// <summary>
        /// Starts sending periodic heartbeat signals for a specific lock
        /// </summary>
        /// <param name="lockKey">The lock key to send heartbeats for</param>
        /// <param name="instanceId">The instance ID holding the lock</param>
        /// <param name="heartbeatInterval">Interval between heartbeat signals</param>
        /// <param name="cancellationToken">Cancellation token to stop heartbeat</param>
        /// <returns>Task representing the heartbeat operation</returns>
        Task StartHeartbeatAsync(
            string lockKey, 
            string instanceId, 
            TimeSpan heartbeatInterval, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops sending heartbeat signals for a specific lock
        /// </summary>
        /// <param name="lockKey">The lock key to stop heartbeats for</param>
        /// <param name="instanceId">The instance ID to stop heartbeats for</param>
        /// <returns>Task representing the stop operation</returns>
        Task StopHeartbeatAsync(string lockKey, string instanceId);

        /// <summary>
        /// Sends a single heartbeat signal to update the last heartbeat timestamp
        /// </summary>
        /// <param name="lockKey">The lock key to send heartbeat for</param>
        /// <param name="instanceId">The instance ID sending the heartbeat</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if heartbeat was sent successfully, false otherwise</returns>
        Task<bool> SendHeartbeatAsync(
            string lockKey, 
            string instanceId, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a lock has expired based on its last heartbeat timestamp
        /// </summary>
        /// <param name="lockKey">The lock key to check</param>
        /// <param name="maxHeartbeatAge">Maximum age of last heartbeat before considering expired</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the lock has expired, false otherwise</returns>
        Task<bool> IsLockExpiredAsync(
            string lockKey, 
            TimeSpan maxHeartbeatAge, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about all active heartbeats for monitoring purposes
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of active heartbeat information</returns>
        Task<List<HeartbeatInfo>> GetActiveHeartbeatsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Cleans up expired locks that haven't received heartbeats within the specified time
        /// </summary>
        /// <param name="maxHeartbeatAge">Maximum age of last heartbeat before cleanup</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of expired locks cleaned up</returns>
        Task<int> CleanupExpiredLocksAsync(
            TimeSpan maxHeartbeatAge, 
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Information about an active heartbeat
    /// </summary>
    public class HeartbeatInfo
    {
        /// <summary>
        /// The lock key
        /// </summary>
        public string LockKey { get; set; } = string.Empty;

        /// <summary>
        /// The instance ID sending heartbeats
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// When the heartbeat was started
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// Last heartbeat timestamp
        /// </summary>
        public DateTime LastHeartbeat { get; set; }

        /// <summary>
        /// Number of heartbeats sent
        /// </summary>
        public int HeartbeatCount { get; set; }

        /// <summary>
        /// Whether the heartbeat is currently active
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Migration ID associated with this heartbeat
        /// </summary>
        public string? MigrationId { get; set; }
    }
} 