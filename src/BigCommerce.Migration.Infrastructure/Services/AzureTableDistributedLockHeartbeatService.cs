using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Azure Table Storage implementation of distributed lock heartbeat service
    /// Phase 3.2: Manages periodic heartbeat signals to prevent phantom orchestrators
    /// </summary>
    public class AzureTableDistributedLockHeartbeatService : IDistributedLockHeartbeatService, IDisposable
    {
        private readonly IDistributedLockService _lockService;
        private readonly ILogger<AzureTableDistributedLockHeartbeatService> _logger;
        
        // Track active heartbeat tasks
        private readonly ConcurrentDictionary<string, HeartbeatTask> _activeHeartbeats = new();
        private readonly object _heartbeatLock = new();
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the heartbeat service
        /// </summary>
        /// <param name="lockService">The distributed lock service</param>
        /// <param name="logger">Logger instance</param>
        public AzureTableDistributedLockHeartbeatService(
            IDistributedLockService lockService,
            ILogger<AzureTableDistributedLockHeartbeatService> logger)
        {
            _lockService = lockService ?? throw new ArgumentNullException(nameof(lockService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _logger.LogInformation("Initialized AzureTableDistributedLockHeartbeatService");
        }

        /// <inheritdoc />
        public Task StartHeartbeatAsync(
            string lockKey, 
            string instanceId, 
            TimeSpan heartbeatInterval, 
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateParameters(lockKey, instanceId);

            if (heartbeatInterval < TimeSpan.FromSeconds(10))
            {
                throw new ArgumentException("Heartbeat interval must be at least 10 seconds", nameof(heartbeatInterval));
            }

            var heartbeatKey = GetHeartbeatKey(lockKey, instanceId);
            
            lock (_heartbeatLock)
            {
                if (_activeHeartbeats.ContainsKey(heartbeatKey))
                {
                    _logger.LogWarning("Heartbeat already active for lock: {LockKey}, instance: {InstanceId}", lockKey, instanceId);
                    return Task.CompletedTask;
                }

                _logger.LogInformation("Starting heartbeat for lock: {LockKey}, instance: {InstanceId}, interval: {Interval}", 
                    lockKey, instanceId, heartbeatInterval);

                var heartbeatTask = new HeartbeatTask
                {
                    LockKey = lockKey,
                    InstanceId = instanceId,
                    HeartbeatInterval = heartbeatInterval,
                    StartedAt = DateTime.UtcNow,
                    IsActive = true,
                    CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                };

                _activeHeartbeats[heartbeatKey] = heartbeatTask;

                // Start the heartbeat background task
                heartbeatTask.Task = Task.Run(async () => 
                    await RunHeartbeatLoopAsync(heartbeatTask), heartbeatTask.CancellationTokenSource.Token);
            }
            
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StopHeartbeatAsync(string lockKey, string instanceId)
        {
            ThrowIfDisposed();
            ValidateParameters(lockKey, instanceId);

            var heartbeatKey = GetHeartbeatKey(lockKey, instanceId);
            
            _logger.LogInformation("Stopping heartbeat for lock: {LockKey}, instance: {InstanceId}", lockKey, instanceId);

            HeartbeatTask? heartbeatTask = null;
            lock (_heartbeatLock)
            {
                if (_activeHeartbeats.TryRemove(heartbeatKey, out heartbeatTask))
                {
                    heartbeatTask.IsActive = false;
                    heartbeatTask.CancellationTokenSource.Cancel();
                }
            }

            if (heartbeatTask?.Task != null)
            {
                try
                {
                    await heartbeatTask.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling heartbeat
                    _logger.LogDebug("Heartbeat task cancelled for lock: {LockKey}, instance: {InstanceId}", lockKey, instanceId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception while stopping heartbeat for lock: {LockKey}, instance: {InstanceId}", lockKey, instanceId);
                }
                finally
                {
                    heartbeatTask.CancellationTokenSource.Dispose();
                }
            }

            _logger.LogInformation("Successfully stopped heartbeat for lock: {LockKey}, instance: {InstanceId}", lockKey, instanceId);
        }

        /// <inheritdoc />
        public async Task<bool> SendHeartbeatAsync(
            string lockKey, 
            string instanceId, 
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateParameters(lockKey, instanceId);

            try
            {
                _logger.LogDebug("Sending heartbeat for lock: {LockKey}, instance: {InstanceId}", lockKey, instanceId);

                // Renew the lock with a short lease time (heartbeat operation)
                var renewResult = await _lockService.RenewLockAsync(
                    lockKey, 
                    instanceId, 
                    TimeSpan.FromMinutes(5), // Short renewal for heartbeat
                    cancellationToken).ConfigureAwait(false);

                if (renewResult.Success)
                {
                    _logger.LogDebug("Heartbeat sent successfully for lock: {LockKey}, instance: {InstanceId}", lockKey, instanceId);
                    
                    // Update heartbeat statistics if we have an active heartbeat
                    var heartbeatKey = GetHeartbeatKey(lockKey, instanceId);
                    if (_activeHeartbeats.TryGetValue(heartbeatKey, out var heartbeatTask))
                    {
                        heartbeatTask.LastHeartbeat = DateTime.UtcNow;
                        heartbeatTask.HeartbeatCount++;
                    }
                    
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to send heartbeat for lock: {LockKey}, instance: {InstanceId}. Reason: {Reason}", 
                        lockKey, instanceId, renewResult.FailureReason);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception sending heartbeat for lock: {LockKey}, instance: {InstanceId}", lockKey, instanceId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsLockExpiredAsync(
            string lockKey, 
            TimeSpan maxHeartbeatAge, 
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrWhiteSpace(lockKey))
                throw new ArgumentException("Lock key cannot be null or empty", nameof(lockKey));

            try
            {
                var lockInfo = await _lockService.GetLockInfoAsync(lockKey, cancellationToken).ConfigureAwait(false);
                
                if (lockInfo == null)
                {
                    // Lock doesn't exist, consider it "expired"
                    return true;
                }

                // Check if the lock has expired based on heartbeat
                var timeSinceLastHeartbeat = DateTime.UtcNow - lockInfo.LastHeartbeat;
                var isExpired = timeSinceLastHeartbeat > maxHeartbeatAge;

                if (isExpired)
                {
                    _logger.LogInformation("Lock {LockKey} has expired. Last heartbeat: {LastHeartbeat}, Age: {Age}, Max age: {MaxAge}", 
                        lockKey, lockInfo.LastHeartbeat, timeSinceLastHeartbeat, maxHeartbeatAge);
                }

                return isExpired;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception checking if lock is expired: {LockKey}", lockKey);
                // In case of error, assume lock is not expired to be safe
                return false;
            }
        }

        /// <inheritdoc />
        public Task<List<HeartbeatInfo>> GetActiveHeartbeatsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var heartbeats = new List<HeartbeatInfo>();
            
            lock (_heartbeatLock)
            {
                foreach (var kvp in _activeHeartbeats)
                {
                    var task = kvp.Value;
                    heartbeats.Add(new HeartbeatInfo
                    {
                        LockKey = task.LockKey,
                        InstanceId = task.InstanceId,
                        StartedAt = task.StartedAt,
                        LastHeartbeat = task.LastHeartbeat,
                        HeartbeatCount = task.HeartbeatCount,
                        IsActive = task.IsActive && !task.CancellationTokenSource.Token.IsCancellationRequested,
                        MigrationId = task.LockKey // Assuming lock key is migration ID
                    });
                }
            }

            return Task.FromResult(heartbeats);
        }

        /// <inheritdoc />
        public async Task<int> CleanupExpiredLocksAsync(
            TimeSpan maxHeartbeatAge, 
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            _logger.LogInformation("Starting cleanup of expired locks. Max heartbeat age: {MaxAge}", maxHeartbeatAge);

            var cleanedUpCount = 0;

            try
            {
                // Get all active heartbeats first to avoid cleaning up locks we're actively maintaining
                var activeHeartbeats = await GetActiveHeartbeatsAsync(cancellationToken).ConfigureAwait(false);
                var activeLockKeys = activeHeartbeats.Select(h => h.LockKey).ToHashSet();

                // For now, we don't have a direct way to enumerate all locks from IDistributedLockService
                // This would require extending the interface or using a different approach
                // For the current implementation, we'll focus on the locks we know about

                _logger.LogInformation("Cleanup completed. Active heartbeats protected: {ActiveCount}, Cleaned up: {CleanedCount}", 
                    activeLockKeys.Count, cleanedUpCount);

                return cleanedUpCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during lock cleanup");
                return cleanedUpCount;
            }
        }

        /// <summary>
        /// Main heartbeat loop for a specific lock
        /// </summary>
        private async Task RunHeartbeatLoopAsync(HeartbeatTask heartbeatTask)
        {
            var cancellationToken = heartbeatTask.CancellationTokenSource.Token;
            
            _logger.LogInformation("Starting heartbeat loop for lock: {LockKey}, instance: {InstanceId}", 
                heartbeatTask.LockKey, heartbeatTask.InstanceId);

            try
            {
                while (!cancellationToken.IsCancellationRequested && heartbeatTask.IsActive)
                {
                    try
                    {
                        // Send heartbeat
                        var success = await SendHeartbeatAsync(
                            heartbeatTask.LockKey, 
                            heartbeatTask.InstanceId, 
                            cancellationToken).ConfigureAwait(false);

                        if (!success)
                        {
                            _logger.LogWarning("Heartbeat failed for lock: {LockKey}, instance: {InstanceId}. Stopping heartbeat loop.", 
                                heartbeatTask.LockKey, heartbeatTask.InstanceId);
                            break;
                        }

                        // Wait for next heartbeat interval
                        await Task.Delay(heartbeatTask.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when stopping heartbeat
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception in heartbeat loop for lock: {LockKey}, instance: {InstanceId}", 
                            heartbeatTask.LockKey, heartbeatTask.InstanceId);
                        
                        // Wait a bit before retrying
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                _logger.LogInformation("Heartbeat loop ended for lock: {LockKey}, instance: {InstanceId}", 
                    heartbeatTask.LockKey, heartbeatTask.InstanceId);
            }
        }

        private static string GetHeartbeatKey(string lockKey, string instanceId) 
            => $"{lockKey}:{instanceId}";

        private static void ValidateParameters(string lockKey, string instanceId)
        {
            if (string.IsNullOrWhiteSpace(lockKey))
                throw new ArgumentException("Lock key cannot be null or empty", nameof(lockKey));
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Instance ID cannot be null or empty", nameof(instanceId));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AzureTableDistributedLockHeartbeatService));
        }

        /// <summary>
        /// Disposes the heartbeat service and stops all active heartbeats
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _logger.LogInformation("Disposing AzureTableDistributedLockHeartbeatService. Stopping {Count} active heartbeats.", 
                _activeHeartbeats.Count);

            // Stop all active heartbeats
            var tasks = new List<Task>();
            foreach (var kvp in _activeHeartbeats.ToList())
            {
                var heartbeatTask = kvp.Value;
                heartbeatTask.IsActive = false;
                heartbeatTask.CancellationTokenSource.Cancel();
                
                if (heartbeatTask.Task != null)
                {
                    tasks.Add(heartbeatTask.Task);
                }
            }

            // Wait for all heartbeat tasks to complete (with timeout)
            try
            {
                Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while waiting for heartbeat tasks to complete during disposal");
            }

            // Dispose cancellation token sources
            foreach (var heartbeatTask in _activeHeartbeats.Values)
            {
                heartbeatTask.CancellationTokenSource.Dispose();
            }

            _activeHeartbeats.Clear();
            
            _logger.LogInformation("AzureTableDistributedLockHeartbeatService disposed successfully");
        }

        /// <summary>
        /// Internal class to track heartbeat tasks
        /// </summary>
        private class HeartbeatTask
        {
            public string LockKey { get; set; } = string.Empty;
            public string InstanceId { get; set; } = string.Empty;
            public TimeSpan HeartbeatInterval { get; set; }
            public DateTime StartedAt { get; set; }
            public DateTime LastHeartbeat { get; set; }
            public int HeartbeatCount { get; set; }
            public bool IsActive { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; } = new();
            public Task? Task { get; set; }
        }
    }
} 