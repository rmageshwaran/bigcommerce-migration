using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Activities.Extensions;
using BigCommerce.Migration.Activities.Models;

namespace BigCommerce.Migration.Activities.Services
{
    /// <summary>
    /// Service for detecting and preventing orchestrator instance collisions
    /// Phase 3.1: Integrates distributed locking with deterministic cancellation pattern
    /// Phase 3.2: Enhanced with heartbeat mechanism to prevent phantom orchestrators
    /// </summary>
    public class OrchestratorCollisionDetectionService
    {
        private readonly IDistributedLockService _distributedLockService;
        private readonly IDistributedLockHeartbeatService _heartbeatService;
        private readonly ICancellationStore _cancellationStore;
        private readonly ISignalREventFactory _signalREventFactory;
        private readonly IProgressEventPublisher _progressEventPublisher;
        private readonly ILogger<OrchestratorCollisionDetectionService> _logger;
        
        private readonly TimeSpan _defaultLeaseTime = TimeSpan.FromMinutes(30); // 30 minutes default lease
        private readonly TimeSpan _renewalInterval = TimeSpan.FromMinutes(10); // Renew every 10 minutes
        private readonly TimeSpan _heartbeatInterval = TimeSpan.FromMinutes(2); // Heartbeat every 2 minutes
        private readonly TimeSpan _maxHeartbeatAge = TimeSpan.FromMinutes(5); // Consider lock expired after 5 minutes without heartbeat

        /// <summary>
        /// Initializes a new instance of the OrchestratorCollisionDetectionService
        /// </summary>
        /// <param name="distributedLockService">Distributed lock service for atomic operations</param>
        /// <param name="heartbeatService">Heartbeat service for phantom orchestrator prevention</param>
        /// <param name="cancellationStore">Cancellation store for native cancellation integration</param>
        /// <param name="signalREventFactory">SignalR event factory for real-time notifications</param>
        /// <param name="progressEventPublisher">Progress event publisher for UI updates</param>
        /// <param name="logger">Logger instance</param>
        public OrchestratorCollisionDetectionService(
            IDistributedLockService distributedLockService,
            IDistributedLockHeartbeatService heartbeatService,
            ICancellationStore cancellationStore,
            ISignalREventFactory signalREventFactory,
            IProgressEventPublisher progressEventPublisher,
            ILogger<OrchestratorCollisionDetectionService> logger)
        {
            _distributedLockService = distributedLockService ?? throw new ArgumentNullException(nameof(distributedLockService));
            _heartbeatService = heartbeatService ?? throw new ArgumentNullException(nameof(heartbeatService));
            _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
            _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
            _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Attempts to acquire an orchestrator lock for a migration to prevent duplicate instances
        /// </summary>
        /// <param name="migrationId">Migration identifier</param>
        /// <param name="instanceId">Unique instance identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collision detection result with lock information</returns>
        public async Task<OrchestratorCollisionResult> TryAcquireOrchestratorLockAsync(
            string migrationId, 
            string instanceId, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Checking for orchestrator collisions for migration: {MigrationId}, instance: {InstanceId}", 
                    migrationId, instanceId);

                var lockKey = $"orchestrator-{migrationId}";
                var lockResult = await _distributedLockService.TryAcquireLockAsync(
                    lockKey, 
                    instanceId, 
                    _defaultLeaseTime, 
                    cancellationToken).ConfigureAwait(false);

                if (lockResult.Success)
                {
                    _logger.LogInformation("Successfully acquired orchestrator lock for migration: {MigrationId}, instance: {InstanceId}", 
                        migrationId, instanceId);

                    // Phase 3.2: Start heartbeat to prevent phantom orchestrator issues
                    try
                    {
                        _logger.LogInformation("Starting heartbeat for orchestrator lock: {LockKey}, instance: {InstanceId}", 
                            lockKey, instanceId);
                        
                        // Start heartbeat in fire-and-forget manner (non-blocking)
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _heartbeatService.StartHeartbeatAsync(
                                    lockKey, 
                                    instanceId, 
                                    _heartbeatInterval, 
                                    cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to start heartbeat for lock: {LockKey}, instance: {InstanceId}", 
                                    lockKey, instanceId);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Exception starting heartbeat for lock: {LockKey}, instance: {InstanceId}", 
                            lockKey, instanceId);
                        // Don't fail lock acquisition due to heartbeat issues
                    }

                    return new OrchestratorCollisionResult
                    {
                        CanProceed = true,
                        MigrationId = migrationId,
                        InstanceId = instanceId,
                        LockAcquired = true,
                        LockKey = lockKey,
                        ExpiresAt = lockResult.ExpiresAt,
                        Message = "Orchestrator lock acquired successfully with heartbeat started"
                    };
                }
                else
                {
                    // Phase 3.2: Check if the existing lock has expired due to missing heartbeat
                    if (lockResult.CurrentLockHolder != null)
                    {
                        bool isExpired = false;
                        try
                        {
                            isExpired = await _heartbeatService.IsLockExpiredAsync(
                                lockKey, 
                                _maxHeartbeatAge, 
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to check if lock is expired for {LockKey}. Treating as active lock.", lockKey);
                            // Treat as not expired (safe default) and continue with collision detection
                            isExpired = false;
                        }

                        if (isExpired)
                        {
                            _logger.LogInformation("Existing lock has expired due to missing heartbeat. Attempting to override: {LockKey}", lockKey);
                            
                            // Try to force release the expired lock and retry acquisition
                            var forceReleased = await _distributedLockService.ForceReleaseLockAsync(lockKey, cancellationToken).ConfigureAwait(false);
                            
                            if (forceReleased)
                            {
                                _logger.LogInformation("Successfully force-released expired lock. Retrying acquisition: {LockKey}", lockKey);
                                
                                // Retry lock acquisition
                                var retryResult = await _distributedLockService.TryAcquireLockAsync(
                                    lockKey, 
                                    instanceId, 
                                    _defaultLeaseTime, 
                                    cancellationToken).ConfigureAwait(false);

                                if (retryResult.Success)
                                {
                                    _logger.LogInformation("Successfully acquired lock after cleaning up expired lock: {LockKey}", lockKey);
                                    
                                    // Start heartbeat for the newly acquired lock
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await _heartbeatService.StartHeartbeatAsync(
                                                lockKey, 
                                                instanceId, 
                                                _heartbeatInterval, 
                                                cancellationToken).ConfigureAwait(false);
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "Failed to start heartbeat after expired lock cleanup: {LockKey}", lockKey);
                                        }
                                    });

                                    return new OrchestratorCollisionResult
                                    {
                                        CanProceed = true,
                                        MigrationId = migrationId,
                                        InstanceId = instanceId,
                                        LockAcquired = true,
                                        LockKey = lockKey,
                                        ExpiresAt = retryResult.ExpiresAt,
                                        Message = "Orchestrator lock acquired after cleaning up expired lock"
                                    };
                                }
                            }
                        }
                    }

                    _logger.LogWarning("Orchestrator collision detected for migration: {MigrationId}. Current holder: {CurrentHolder}", 
                        migrationId, lockResult.CurrentLockHolder?.InstanceId);

                    return new OrchestratorCollisionResult
                    {
                        CanProceed = false,
                        MigrationId = migrationId,
                        InstanceId = instanceId,
                        LockAcquired = false,
                        CollisionDetected = true,
                        CurrentLockHolder = lockResult.CurrentLockHolder,
                        Message = lockResult.FailureReason ?? "Another orchestrator instance is already running for this migration"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during orchestrator collision detection for migration: {MigrationId}", migrationId);
                
                return new OrchestratorCollisionResult
                {
                    CanProceed = false,
                    MigrationId = migrationId,
                    InstanceId = instanceId,
                    LockAcquired = false,
                    HasError = true,
                    Message = $"Error during collision detection: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Renews the orchestrator lock to maintain ownership
        /// Should be called periodically during orchestrator execution
        /// </summary>
        /// <param name="migrationId">Migration identifier</param>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if renewal was successful</returns>
        public async Task<bool> RenewOrchestratorLockAsync(
            string migrationId, 
            string instanceId, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var lockKey = $"orchestrator-{migrationId}";
                var renewResult = await _distributedLockService.RenewLockAsync(
                    lockKey, 
                    instanceId, 
                    _defaultLeaseTime, 
                    cancellationToken).ConfigureAwait(false);

                if (renewResult.Success)
                {
                    _logger.LogDebug("Successfully renewed orchestrator lock for migration: {MigrationId}", migrationId);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to renew orchestrator lock for migration: {MigrationId}. Reason: {Reason}", 
                        migrationId, renewResult.FailureReason);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renewing orchestrator lock for migration: {MigrationId}", migrationId);
                return false;
            }
        }

        /// <summary>
        /// Releases the orchestrator lock when migration completes or fails
        /// Phase 3.2: Enhanced to stop heartbeat when releasing lock
        /// </summary>
        /// <param name="migrationId">Migration identifier</param>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if release was successful</returns>
        public async Task<bool> ReleaseOrchestratorLockAsync(
            string migrationId, 
            string instanceId, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Releasing orchestrator lock for migration: {MigrationId}, instance: {InstanceId}", 
                    migrationId, instanceId);

                var lockKey = $"orchestrator-{migrationId}";
                
                // Phase 3.2: Stop heartbeat first before releasing lock
                try
                {
                    _logger.LogInformation("Stopping heartbeat for orchestrator lock: {LockKey}, instance: {InstanceId}", 
                        lockKey, instanceId);
                    
                    await _heartbeatService.StopHeartbeatAsync(lockKey, instanceId).ConfigureAwait(false);
                    
                    _logger.LogInformation("Successfully stopped heartbeat for lock: {LockKey}, instance: {InstanceId}", 
                        lockKey, instanceId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception stopping heartbeat for lock: {LockKey}, instance: {InstanceId}. Proceeding with lock release.", 
                        lockKey, instanceId);
                    // Continue with lock release even if heartbeat stop fails
                }

                var released = await _distributedLockService.ReleaseLockAsync(
                    lockKey, 
                    instanceId, 
                    cancellationToken).ConfigureAwait(false);

                if (released)
                {
                    _logger.LogInformation("Successfully released orchestrator lock for migration: {MigrationId}", migrationId);
                }
                else
                {
                    _logger.LogWarning("Failed to release orchestrator lock for migration: {MigrationId}", migrationId);
                }

                return released;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing orchestrator lock for migration: {MigrationId}", migrationId);
                return false;
            }
        }

        /// <summary>
        /// Gets information about the current orchestrator lock holder
        /// </summary>
        /// <param name="migrationId">Migration identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Lock information or null if no lock exists</returns>
        public async Task<DistributedLockInfo?> GetOrchestratorLockInfoAsync(
            string migrationId, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var lockKey = $"orchestrator-{migrationId}";
                return await _distributedLockService.GetLockInfoAsync(lockKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting orchestrator lock info for migration: {MigrationId}", migrationId);
                return null;
            }
        }

        // Note: Collision cancellation now handled by updated PublishCollisionCancellationActivity
        // Uses native cancellation infrastructure (blob store + SignalR) for consistency
    }

    /// <summary>
    /// Result of orchestrator collision detection
    /// </summary>
    public class OrchestratorCollisionResult
    {
        /// <summary>
        /// Whether this orchestrator instance can proceed with the migration
        /// </summary>
        public bool CanProceed { get; set; }

        /// <summary>
        /// Migration identifier
        /// </summary>
        public string MigrationId { get; set; } = string.Empty;

        /// <summary>
        /// Instance identifier that attempted to acquire the lock
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// Whether a lock was successfully acquired
        /// </summary>
        public bool LockAcquired { get; set; }

        /// <summary>
        /// Whether a collision with another orchestrator was detected
        /// </summary>
        public bool CollisionDetected { get; set; }

        /// <summary>
        /// Whether an error occurred during collision detection
        /// </summary>
        public bool HasError { get; set; }

        /// <summary>
        /// Lock key used for this migration
        /// </summary>
        public string LockKey { get; set; } = string.Empty;

        /// <summary>
        /// When the lock expires (if acquired)
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Information about the current lock holder (if collision detected)
        /// </summary>
        public DistributedLockInfo? CurrentLockHolder { get; set; }

        /// <summary>
        /// Descriptive message about the collision detection result
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
} 