using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Interface for orchestrator cleanup service that provides batch maintenance and orphaned lock cleanup
    /// Phase 3.3: Orchestrator Cleanup Service for administrative operations
    /// </summary>
    public interface IOrchestratorCleanupService
    {
        /// <summary>
        /// Scans for and cleans up all orphaned orchestrator locks
        /// </summary>
        /// <param name="maxHeartbeatAge">Maximum age since last heartbeat before considering a lock orphaned</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Cleanup result with details of operations performed</returns>
        Task<CleanupResult> CleanupOrphanedLocksAsync(
            TimeSpan maxHeartbeatAge, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Forces release of a specific lock regardless of heartbeat status (administrative operation)
        /// </summary>
        /// <param name="migrationId">Migration ID whose lock should be force-released</param>
        /// <param name="reason">Reason for the force release (for audit logging)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the lock was successfully released</returns>
        Task<bool> ForceReleaseLockAsync(
            string migrationId, 
            string reason, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about all active orchestrator locks for monitoring purposes
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of all active lock information</returns>
        Task<List<OrchestratorLockInfo>> GetAllActiveLocksAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Identifies locks that are candidates for cleanup (expired but not yet cleaned up)
        /// </summary>
        /// <param name="maxHeartbeatAge">Maximum age since last heartbeat before considering a lock expired</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of lock information for locks that are candidates for cleanup</returns>
        Task<List<OrchestratorLockInfo>> GetCleanupCandidatesAsync(
            TimeSpan maxHeartbeatAge, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates the health of the distributed lock system
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Health check result with system status</returns>
        Task<LockSystemHealthResult> ValidateSystemHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs comprehensive system maintenance including cleanup, validation, and optimization
        /// </summary>
        /// <param name="maintenanceOptions">Options for maintenance operations</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Maintenance result with summary of all operations performed</returns>
        Task<MaintenanceResult> PerformMaintenanceAsync(
            MaintenanceOptions maintenanceOptions, 
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of cleanup operations
    /// </summary>
    public class CleanupResult
    {
        /// <summary>
        /// Number of orphaned locks found
        /// </summary>
        public int OrphanedLocksFound { get; set; }

        /// <summary>
        /// Number of locks successfully cleaned up
        /// </summary>
        public int LocksCleanedUp { get; set; }

        /// <summary>
        /// Number of cleanup operations that failed
        /// </summary>
        public int FailedCleanups { get; set; }

        /// <summary>
        /// Total time taken for cleanup operations
        /// </summary>
        public TimeSpan CleanupDuration { get; set; }

        /// <summary>
        /// Details of locks that were cleaned up
        /// </summary>
        public List<string> CleanedUpMigrationIds { get; set; } = new();

        /// <summary>
        /// Details of cleanup failures
        /// </summary>
        public List<CleanupFailure> Failures { get; set; } = new();

        /// <summary>
        /// When the cleanup operation was performed
        /// </summary>
        public DateTime CleanupTimestamp { get; set; }
    }

    /// <summary>
    /// Information about an orchestrator lock for monitoring and cleanup purposes
    /// </summary>
    public class OrchestratorLockInfo
    {
        /// <summary>
        /// Migration ID associated with this lock
        /// </summary>
        public string MigrationId { get; set; } = string.Empty;

        /// <summary>
        /// Instance ID holding the lock
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
        /// Last heartbeat timestamp
        /// </summary>
        public DateTime LastHeartbeat { get; set; }

        /// <summary>
        /// Time since last heartbeat
        /// </summary>
        public TimeSpan TimeSinceLastHeartbeat => DateTime.UtcNow - LastHeartbeat;

        /// <summary>
        /// Whether this lock appears to be orphaned
        /// </summary>
        public bool IsOrphaned { get; set; }

        /// <summary>
        /// Lock status
        /// </summary>
        public DistributedLockStatus Status { get; set; }

        /// <summary>
        /// Number of times the lock has been renewed
        /// </summary>
        public int RenewalCount { get; set; }
    }

    /// <summary>
    /// Details of a cleanup failure
    /// </summary>
    public class CleanupFailure
    {
        /// <summary>
        /// Migration ID that failed to clean up
        /// </summary>
        public string MigrationId { get; set; } = string.Empty;

        /// <summary>
        /// Instance ID that was holding the lock
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// Error message describing the failure
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Exception details if available
        /// </summary>
        public string? ExceptionDetails { get; set; }

        /// <summary>
        /// When the failure occurred
        /// </summary>
        public DateTime FailureTimestamp { get; set; }
    }

    /// <summary>
    /// Health check result for the distributed lock system
    /// </summary>
    public class LockSystemHealthResult
    {
        /// <summary>
        /// Overall system health status
        /// </summary>
        public LockSystemHealth OverallHealth { get; set; }

        /// <summary>
        /// Total number of active locks
        /// </summary>
        public int TotalActiveLocks { get; set; }

        /// <summary>
        /// Number of locks with recent heartbeats
        /// </summary>
        public int HealthyLocks { get; set; }

        /// <summary>
        /// Number of locks that appear stale
        /// </summary>
        public int StaleLocks { get; set; }

        /// <summary>
        /// Number of locks that appear orphaned
        /// </summary>
        public int OrphanedLocks { get; set; }

        /// <summary>
        /// Average time since last heartbeat across all locks
        /// </summary>
        public TimeSpan AverageHeartbeatAge { get; set; }

        /// <summary>
        /// Detailed health check messages
        /// </summary>
        public List<string> HealthMessages { get; set; } = new();

        /// <summary>
        /// When the health check was performed
        /// </summary>
        public DateTime CheckTimestamp { get; set; }
    }

    /// <summary>
    /// Options for maintenance operations
    /// </summary>
    public class MaintenanceOptions
    {
        /// <summary>
        /// Maximum age since last heartbeat for cleanup consideration
        /// </summary>
        public TimeSpan MaxHeartbeatAge { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Whether to perform orphaned lock cleanup
        /// </summary>
        public bool CleanupOrphanedLocks { get; set; } = true;

        /// <summary>
        /// Whether to validate system health
        /// </summary>
        public bool ValidateHealth { get; set; } = true;

        /// <summary>
        /// Whether to force cleanup even for potentially active locks (use with caution)
        /// </summary>
        public bool ForceCleanup { get; set; } = false;

        /// <summary>
        /// Maximum number of locks to clean up in a single operation
        /// </summary>
        public int MaxCleanupCount { get; set; } = 100;

        /// <summary>
        /// Reason for performing maintenance (for audit logging)
        /// </summary>
        public string MaintenanceReason { get; set; } = "Scheduled maintenance";
    }

    /// <summary>
    /// Result of comprehensive maintenance operations
    /// </summary>
    public class MaintenanceResult
    {
        /// <summary>
        /// Cleanup operation result
        /// </summary>
        public CleanupResult? CleanupResult { get; set; }

        /// <summary>
        /// Health validation result
        /// </summary>
        public LockSystemHealthResult? HealthResult { get; set; }

        /// <summary>
        /// Total maintenance duration
        /// </summary>
        public TimeSpan MaintenanceDuration { get; set; }

        /// <summary>
        /// Overall success status
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Maintenance operation messages
        /// </summary>
        public List<string> Messages { get; set; } = new();

        /// <summary>
        /// When maintenance was performed
        /// </summary>
        public DateTime MaintenanceTimestamp { get; set; }
    }

    /// <summary>
    /// Lock system health status enumeration
    /// </summary>
    public enum LockSystemHealth
    {
        /// <summary>
        /// System is healthy
        /// </summary>
        Healthy,

        /// <summary>
        /// System has some issues but is functional
        /// </summary>
        Warning,

        /// <summary>
        /// System has critical issues
        /// </summary>
        Critical,

        /// <summary>
        /// Unable to determine system health
        /// </summary>
        Unknown
    }
} 