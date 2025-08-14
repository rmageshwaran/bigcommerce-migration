using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Activities.Services
{
    /// <summary>
    /// Implementation of orchestrator cleanup service for batch maintenance and orphaned lock cleanup
    /// Phase 3.3: Provides administrative operations for distributed lock system maintenance
    /// </summary>
    public class OrchestratorCleanupService : IOrchestratorCleanupService
    {
        private readonly IDistributedLockService _lockService;
        private readonly IDistributedLockHeartbeatService _heartbeatService;
        private readonly ILogger<OrchestratorCleanupService> _logger;
        
        // Configuration constants
        private readonly TimeSpan _defaultMaxHeartbeatAge = TimeSpan.FromMinutes(10);
        private readonly TimeSpan _staleLockThreshold = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _healthyLockThreshold = TimeSpan.FromMinutes(3);

        /// <summary>
        /// Initializes a new instance of the OrchestratorCleanupService
        /// </summary>
        /// <param name="lockService">Distributed lock service</param>
        /// <param name="heartbeatService">Heartbeat service for lock health monitoring</param>
        /// <param name="logger">Logger instance</param>
        public OrchestratorCleanupService(
            IDistributedLockService lockService,
            IDistributedLockHeartbeatService heartbeatService,
            ILogger<OrchestratorCleanupService> logger)
        {
            _lockService = lockService ?? throw new ArgumentNullException(nameof(lockService));
            _heartbeatService = heartbeatService ?? throw new ArgumentNullException(nameof(heartbeatService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<CleanupResult> CleanupOrphanedLocksAsync(
            TimeSpan maxHeartbeatAge, 
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new CleanupResult
            {
                CleanupTimestamp = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation("Starting cleanup of orphaned locks. Max heartbeat age: {MaxAge}", maxHeartbeatAge);

                // Get all active locks
                var allLocks = await _lockService.GetAllLocksAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Found {TotalLocks} total locks in the system", allLocks.Count);

                // Identify orphaned locks
                var orphanedLocks = new List<DistributedLockInfo>();
                foreach (var lockInfo in allLocks)
                {
                    var timeSinceHeartbeat = DateTime.UtcNow - lockInfo.LastHeartbeat;
                    if (timeSinceHeartbeat > maxHeartbeatAge)
                    {
                        orphanedLocks.Add(lockInfo);
                        _logger.LogInformation("Found orphaned lock: {LockKey}, Instance: {InstanceId}, Last heartbeat: {LastHeartbeat}, Age: {Age}",
                            lockInfo.LockKey, lockInfo.InstanceId, lockInfo.LastHeartbeat, timeSinceHeartbeat);
                    }
                }

                result.OrphanedLocksFound = orphanedLocks.Count;
                _logger.LogInformation("Identified {OrphanedCount} orphaned locks for cleanup", orphanedLocks.Count);

                // Clean up orphaned locks
                foreach (var lockInfo in orphanedLocks)
                {
                    try
                    {
                        _logger.LogInformation("Attempting to clean up orphaned lock: {LockKey}", lockInfo.LockKey);
                        
                        var released = await _lockService.ForceReleaseLockAsync(lockInfo.LockKey, cancellationToken).ConfigureAwait(false);
                        
                        if (released)
                        {
                            result.LocksCleanedUp++;
                            result.CleanedUpMigrationIds.Add(lockInfo.MigrationId);
                            _logger.LogInformation("Successfully cleaned up orphaned lock: {LockKey}", lockInfo.LockKey);
                        }
                        else
                        {
                            result.FailedCleanups++;
                            result.Failures.Add(new CleanupFailure
                            {
                                MigrationId = lockInfo.MigrationId,
                                InstanceId = lockInfo.InstanceId,
                                ErrorMessage = "Failed to force release lock",
                                FailureTimestamp = DateTime.UtcNow
                            });
                            _logger.LogWarning("Failed to clean up orphaned lock: {LockKey}", lockInfo.LockKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailedCleanups++;
                        result.Failures.Add(new CleanupFailure
                        {
                            MigrationId = lockInfo.MigrationId,
                            InstanceId = lockInfo.InstanceId,
                            ErrorMessage = ex.Message,
                            ExceptionDetails = ex.ToString(),
                            FailureTimestamp = DateTime.UtcNow
                        });
                        _logger.LogError(ex, "Exception during cleanup of orphaned lock: {LockKey}", lockInfo.LockKey);
                    }
                }

                result.CleanupDuration = stopwatch.Elapsed;
                _logger.LogInformation("Cleanup completed. Orphaned: {OrphanedCount}, Cleaned: {CleanedCount}, Failed: {FailedCount}, Duration: {Duration}",
                    result.OrphanedLocksFound, result.LocksCleanedUp, result.FailedCleanups, result.CleanupDuration);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during orphaned lock cleanup");
                result.CleanupDuration = stopwatch.Elapsed;
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<bool> ForceReleaseLockAsync(
            string migrationId, 
            string reason, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(migrationId))
                    throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

                var lockKey = $"orchestrator-{migrationId}";
                
                _logger.LogWarning("Force releasing lock for migration: {MigrationId}, Reason: {Reason}", 
                    migrationId, reason);

                var released = await _lockService.ForceReleaseLockAsync(lockKey, cancellationToken).ConfigureAwait(false);
                
                if (released)
                {
                    _logger.LogInformation("Successfully force released lock for migration: {MigrationId}", migrationId);
                }
                else
                {
                    _logger.LogWarning("Failed to force release lock for migration: {MigrationId}", migrationId);
                }

                return released;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error force releasing lock for migration: {MigrationId}", migrationId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<List<OrchestratorLockInfo>> GetAllActiveLocksAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Getting all active orchestrator locks");

                var allLocks = await _lockService.GetAllLocksAsync(cancellationToken).ConfigureAwait(false);
                
                var orchestratorLocks = allLocks
                    .Where(lockInfo => lockInfo.LockKey.StartsWith("orchestrator-"))
                    .Select(MapToOrchestratorLockInfo)
                    .ToList();

                _logger.LogDebug("Found {Count} active orchestrator locks", orchestratorLocks.Count);
                return orchestratorLocks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all active orchestrator locks");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<List<OrchestratorLockInfo>> GetCleanupCandidatesAsync(
            TimeSpan maxHeartbeatAge, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Identifying cleanup candidates with max heartbeat age: {MaxAge}", maxHeartbeatAge);

                var allLocks = await GetAllActiveLocksAsync(cancellationToken).ConfigureAwait(false);
                
                var candidates = allLocks
                    .Where(lockInfo => lockInfo.TimeSinceLastHeartbeat > maxHeartbeatAge)
                    .ToList();

                // Mark candidates as orphaned
                foreach (var candidate in candidates)
                {
                    candidate.IsOrphaned = true;
                }

                _logger.LogInformation("Found {Count} cleanup candidates", candidates.Count);
                return candidates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error identifying cleanup candidates");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<LockSystemHealthResult> ValidateSystemHealthAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Validating distributed lock system health");

                var allLocks = await GetAllActiveLocksAsync(cancellationToken).ConfigureAwait(false);
                
                var result = new LockSystemHealthResult
                {
                    CheckTimestamp = DateTime.UtcNow,
                    TotalActiveLocks = allLocks.Count
                };

                if (allLocks.Count == 0)
                {
                    result.OverallHealth = LockSystemHealth.Healthy;
                    result.HealthMessages.Add("No active locks - system is idle");
                    return result;
                }

                // Categorize locks by health status
                var healthyLocks = allLocks.Where(l => l.TimeSinceLastHeartbeat <= _healthyLockThreshold).ToList();
                var staleLocks = allLocks.Where(l => l.TimeSinceLastHeartbeat > _healthyLockThreshold && l.TimeSinceLastHeartbeat <= _staleLockThreshold).ToList();
                var orphanedLocks = allLocks.Where(l => l.TimeSinceLastHeartbeat > _staleLockThreshold).ToList();

                result.HealthyLocks = healthyLocks.Count;
                result.StaleLocks = staleLocks.Count;
                result.OrphanedLocks = orphanedLocks.Count;

                // Calculate average heartbeat age
                if (allLocks.Any())
                {
                    result.AverageHeartbeatAge = TimeSpan.FromTicks((long)allLocks.Average(l => l.TimeSinceLastHeartbeat.Ticks));
                }

                // Determine overall health
                var healthyPercentage = (double)healthyLocks.Count / allLocks.Count;
                var orphanedPercentage = (double)orphanedLocks.Count / allLocks.Count;

                if (orphanedPercentage > 0.5) // More than 50% orphaned
                {
                    result.OverallHealth = LockSystemHealth.Critical;
                    result.HealthMessages.Add($"Critical: {orphanedPercentage:P0} of locks appear orphaned");
                }
                else if (orphanedPercentage > 0.2 || healthyPercentage < 0.5) // More than 20% orphaned or less than 50% healthy
                {
                    result.OverallHealth = LockSystemHealth.Warning;
                    result.HealthMessages.Add($"Warning: {orphanedPercentage:P0} orphaned, {healthyPercentage:P0} healthy");
                }
                else
                {
                    result.OverallHealth = LockSystemHealth.Healthy;
                    result.HealthMessages.Add($"Healthy: {healthyPercentage:P0} of locks are healthy");
                }

                // Add detailed messages
                if (result.StaleLocks > 0)
                {
                    result.HealthMessages.Add($"{result.StaleLocks} locks have stale heartbeats (>{_healthyLockThreshold.TotalMinutes} minutes)");
                }
                if (result.OrphanedLocks > 0)
                {
                    result.HealthMessages.Add($"{result.OrphanedLocks} locks appear orphaned (>{_staleLockThreshold.TotalMinutes} minutes)");
                }

                _logger.LogInformation("System health check completed. Overall health: {Health}, Total locks: {Total}, Healthy: {Healthy}, Stale: {Stale}, Orphaned: {Orphaned}",
                    result.OverallHealth, result.TotalActiveLocks, result.HealthyLocks, result.StaleLocks, result.OrphanedLocks);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during system health validation");
                return new LockSystemHealthResult
                {
                    OverallHealth = LockSystemHealth.Unknown,
                    CheckTimestamp = DateTime.UtcNow,
                    HealthMessages = { $"Health check failed: {ex.Message}" }
                };
            }
        }

        /// <inheritdoc />
        public async Task<MaintenanceResult> PerformMaintenanceAsync(
            MaintenanceOptions maintenanceOptions, 
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new MaintenanceResult
            {
                MaintenanceTimestamp = DateTime.UtcNow,
                Success = true
            };

            try
            {
                _logger.LogInformation("Starting comprehensive system maintenance. Reason: {Reason}, Options: {@Options}",
                    maintenanceOptions.MaintenanceReason, new { 
                        maintenanceOptions.CleanupOrphanedLocks, 
                        maintenanceOptions.ValidateHealth, 
                        maintenanceOptions.ForceCleanup,
                        maintenanceOptions.MaxCleanupCount,
                        maintenanceOptions.MaxHeartbeatAge 
                    });

                result.Messages.Add($"Starting maintenance: {maintenanceOptions.MaintenanceReason}");

                // Perform health validation if requested
                if (maintenanceOptions.ValidateHealth)
                {
                    _logger.LogInformation("Performing system health validation");
                    result.HealthResult = await ValidateSystemHealthAsync(cancellationToken).ConfigureAwait(false);
                    result.Messages.Add($"Health check completed - Status: {result.HealthResult.OverallHealth}");
                }

                // Perform cleanup if requested
                if (maintenanceOptions.CleanupOrphanedLocks)
                {
                    _logger.LogInformation("Performing orphaned lock cleanup");
                    
                    var maxAge = maintenanceOptions.ForceCleanup ? TimeSpan.FromMinutes(1) : maintenanceOptions.MaxHeartbeatAge;
                    result.CleanupResult = await CleanupOrphanedLocksAsync(maxAge, cancellationToken).ConfigureAwait(false);
                    
                    result.Messages.Add($"Cleanup completed - Found: {result.CleanupResult.OrphanedLocksFound}, Cleaned: {result.CleanupResult.LocksCleanedUp}, Failed: {result.CleanupResult.FailedCleanups}");
                    
                    // Check if we hit the cleanup limit
                    if (result.CleanupResult.LocksCleanedUp >= maintenanceOptions.MaxCleanupCount)
                    {
                        result.Messages.Add($"Cleanup stopped at limit of {maintenanceOptions.MaxCleanupCount} locks");
                    }
                }

                result.MaintenanceDuration = stopwatch.Elapsed;
                
                _logger.LogInformation("Maintenance completed successfully. Duration: {Duration}", result.MaintenanceDuration);
                result.Messages.Add($"Maintenance completed in {result.MaintenanceDuration.TotalSeconds:F2} seconds");

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.MaintenanceDuration = stopwatch.Elapsed;
                result.Messages.Add($"Maintenance failed: {ex.Message}");
                
                _logger.LogError(ex, "Error during system maintenance");
                throw;
            }
        }

        /// <summary>
        /// Maps a DistributedLockInfo to OrchestratorLockInfo
        /// </summary>
        private OrchestratorLockInfo MapToOrchestratorLockInfo(DistributedLockInfo lockInfo)
        {
            var migrationId = lockInfo.LockKey.StartsWith("orchestrator-") 
                ? lockInfo.LockKey.Substring("orchestrator-".Length) 
                : lockInfo.MigrationId;

            return new OrchestratorLockInfo
            {
                MigrationId = migrationId,
                InstanceId = lockInfo.InstanceId,
                AcquiredAt = lockInfo.AcquiredAt,
                ExpiresAt = lockInfo.ExpiresAt,
                LastHeartbeat = lockInfo.LastHeartbeat,
                IsOrphaned = DateTime.UtcNow - lockInfo.LastHeartbeat > _defaultMaxHeartbeatAge,
                Status = lockInfo.Status,
                RenewalCount = lockInfo.RenewalCount
            };
        }
    }
} 