using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Azure Table Storage implementation of distributed locking for orchestrator instance collision detection
    /// Phase 3.1: Implements atomic lock operations using Azure Table Storage conditional operations
    /// </summary>
    public class AzureTableDistributedLockService : IDistributedLockService
    {
        private readonly TableServiceClient _tableServiceClient;
        private readonly ILogger<AzureTableDistributedLockService> _logger;
        private readonly string _instanceId;
        
        private const string LocksTableName = "distributedlocks";
        private const string PartitionKey = "lock";

        /// <summary>
        /// Initializes a new instance of the AzureTableDistributedLockService
        /// </summary>
        /// <param name="configuration">Application configuration</param>
        /// <param name="logger">Logger instance</param>
        public AzureTableDistributedLockService(
            IConfiguration configuration, 
            ILogger<AzureTableDistributedLockService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Try ConnectionStrings section first, then fall back to Values section (Azure Functions style)
            var connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
                ?? configuration["AzureWebJobsStorage"]
                ?? throw new ArgumentNullException("AzureWebJobsStorage connection string is required");

            _tableServiceClient = new TableServiceClient(connectionString);
            
            // Generate unique instance ID for this service instance
            _instanceId = Environment.MachineName + "-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogInformation("Initialized AzureTableDistributedLockService with instance ID: {InstanceId}", _instanceId);
        }

        /// <inheritdoc />
        public async Task<DistributedLockResult> TryAcquireLockAsync(
            string lockKey, 
            string instanceId, 
            TimeSpan leaseTime, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Attempting to acquire lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);

                var tableClient = await GetTableClientAsync().ConfigureAwait(false);
                var now = DateTime.UtcNow;
                var expiresAt = now.Add(leaseTime);

                var lockEntity = new TableEntity(PartitionKey, lockKey)
                {
                    ["LockKey"] = lockKey,
                    ["InstanceId"] = instanceId,
                    ["AcquiredAt"] = now,
                    ["ExpiresAt"] = expiresAt,
                    ["LastHeartbeat"] = now,
                    ["MigrationId"] = lockKey, // Assuming lockKey is migration ID
                    ["Status"] = DistributedLockStatus.Active.ToString(),
                    ["RenewalCount"] = 0
                };

                try
                {
                    // Try to create the entity - this will fail if it already exists
                    await tableClient.AddEntityAsync(lockEntity, cancellationToken).ConfigureAwait(false);
                    
                    _logger.LogInformation("Successfully acquired lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);
                    
                    return new DistributedLockResult
                    {
                        Success = true,
                        LockKey = lockKey,
                        InstanceId = instanceId,
                        AcquiredAt = now,
                        ExpiresAt = expiresAt
                    };
                }
                catch (RequestFailedException ex) when (ex.Status == 409) // Conflict - entity already exists
                {
                    _logger.LogWarning("Lock already exists: {LockKey}. Checking if expired...", lockKey);
                    
                    // Check if existing lock is expired
                    var existingLock = await GetLockInfoAsync(lockKey, cancellationToken).ConfigureAwait(false);
                    if (existingLock != null)
                    {
                        if (existingLock.ExpiresAt <= now)
                        {
                            _logger.LogInformation("Existing lock is expired. Attempting to override: {LockKey}", lockKey);
                            
                            // Try to update the expired lock
                            lockEntity.ETag = ETag.All; // Overwrite regardless of current state
                            try
                            {
                                await tableClient.UpdateEntityAsync(lockEntity, ETag.All, cancellationToken: cancellationToken).ConfigureAwait(false);
                                
                                _logger.LogInformation("Successfully acquired expired lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);
                                
                                return new DistributedLockResult
                                {
                                    Success = true,
                                    LockKey = lockKey,
                                    InstanceId = instanceId,
                                    AcquiredAt = now,
                                    ExpiresAt = expiresAt
                                };
                            }
                            catch (RequestFailedException updateEx)
                            {
                                _logger.LogWarning(updateEx, "Failed to update expired lock: {LockKey}", lockKey);
                            }
                        }
                        
                        return new DistributedLockResult
                        {
                            Success = false,
                            LockKey = lockKey,
                            InstanceId = instanceId,
                            FailureReason = $"Lock is held by instance {existingLock.InstanceId} until {existingLock.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC",
                            CurrentLockHolder = existingLock
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error acquiring lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);
                
                return new DistributedLockResult
                {
                    Success = false,
                    LockKey = lockKey,
                    InstanceId = instanceId,
                    FailureReason = $"Exception occurred: {ex.Message}"
                };
            }
            
            return new DistributedLockResult
            {
                Success = false,
                LockKey = lockKey,
                InstanceId = instanceId,
                FailureReason = "Unknown error occurred"
            };
        }

        /// <inheritdoc />
        public async Task<DistributedLockResult> RenewLockAsync(
            string lockKey, 
            string instanceId, 
            TimeSpan leaseTime, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Renewing lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);

                var tableClient = await GetTableClientAsync().ConfigureAwait(false);
                var now = DateTime.UtcNow;

                // Get existing lock
                var existingEntity = await tableClient.GetEntityAsync<TableEntity>(PartitionKey, lockKey, cancellationToken: cancellationToken).ConfigureAwait(false);
                var entity = existingEntity.Value;

                // Verify ownership
                if (entity["InstanceId"].ToString() != instanceId)
                {
                    return new DistributedLockResult
                    {
                        Success = false,
                        LockKey = lockKey,
                        InstanceId = instanceId,
                        FailureReason = $"Lock is owned by different instance: {entity["InstanceId"]}"
                    };
                }

                // Update expiration and heartbeat
                entity["ExpiresAt"] = now.Add(leaseTime);
                entity["LastHeartbeat"] = now;
                entity["RenewalCount"] = (int)entity["RenewalCount"] + 1;

                await tableClient.UpdateEntityAsync(entity, entity.ETag, cancellationToken: cancellationToken).ConfigureAwait(false);

                _logger.LogDebug("Successfully renewed lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);

                return new DistributedLockResult
                {
                    Success = true,
                    LockKey = lockKey,
                    InstanceId = instanceId,
                    AcquiredAt = (DateTime)entity["AcquiredAt"],
                    ExpiresAt = (DateTime)entity["ExpiresAt"]
                };
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return new DistributedLockResult
                {
                    Success = false,
                    LockKey = lockKey,
                    InstanceId = instanceId,
                    FailureReason = "Lock not found"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renewing lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);
                
                return new DistributedLockResult
                {
                    Success = false,
                    LockKey = lockKey,
                    InstanceId = instanceId,
                    FailureReason = $"Exception occurred: {ex.Message}"
                };
            }
        }

        /// <inheritdoc />
        public async Task<bool> ReleaseLockAsync(
            string lockKey, 
            string instanceId, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Releasing lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);

                var tableClient = await GetTableClientAsync().ConfigureAwait(false);

                // Get existing lock to verify ownership
                var existingEntity = await tableClient.GetEntityAsync<TableEntity>(PartitionKey, lockKey, cancellationToken: cancellationToken).ConfigureAwait(false);
                var entity = existingEntity.Value;

                // Verify ownership
                if (entity["InstanceId"].ToString() != instanceId)
                {
                    _logger.LogWarning("Cannot release lock {LockKey} - owned by different instance: {Owner}", lockKey, entity["InstanceId"]);
                    return false;
                }

                // Delete the lock
                await tableClient.DeleteEntityAsync(PartitionKey, lockKey, entity.ETag, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Successfully released lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("Lock not found when attempting to release: {LockKey}", lockKey);
                return false; // Lock doesn't exist, consider it "released"
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing lock: {LockKey} for instance: {InstanceId}", lockKey, instanceId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<DistributedLockInfo?> GetLockInfoAsync(
            string lockKey, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var tableClient = await GetTableClientAsync().ConfigureAwait(false);
                var entity = await tableClient.GetEntityAsync<TableEntity>(PartitionKey, lockKey, cancellationToken: cancellationToken).ConfigureAwait(false);

                return MapToLockInfo(entity.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting lock info: {LockKey}", lockKey);
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsLockHeldAsync(
            string lockKey, 
            CancellationToken cancellationToken = default)
        {
            var lockInfo = await GetLockInfoAsync(lockKey, cancellationToken).ConfigureAwait(false);
            return lockInfo != null && lockInfo.ExpiresAt > DateTime.UtcNow;
        }

        /// <inheritdoc />
        public async Task<bool> ForceReleaseLockAsync(
            string lockKey, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogWarning("Force releasing lock: {LockKey}", lockKey);

                var tableClient = await GetTableClientAsync().ConfigureAwait(false);
                await tableClient.DeleteEntityAsync(PartitionKey, lockKey, ETag.All, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Successfully force released lock: {LockKey}", lockKey);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation("Lock not found when attempting to force release: {LockKey}", lockKey);
                return true; // Lock doesn't exist, consider it "released"
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error force releasing lock: {LockKey}", lockKey);
                return false;
            }
        }

        /// <summary>
        /// Gets or creates the table client for distributed locks
        /// </summary>
        private async Task<TableClient> GetTableClientAsync()
        {
            var tableClient = _tableServiceClient.GetTableClient(LocksTableName);
            await tableClient.CreateIfNotExistsAsync().ConfigureAwait(false);
            return tableClient;
        }

        /// <inheritdoc />
        public async Task<List<DistributedLockInfo>> GetAllLocksAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Getting all active locks from distributed lock system");

                var tableClient = await GetTableClientAsync().ConfigureAwait(false);
                var locks = new List<DistributedLockInfo>();

                // Query all entities in the locks table
                await foreach (var entity in tableClient.QueryAsync<TableEntity>(cancellationToken: cancellationToken))
                {
                    try
                    {
                        var lockInfo = MapToLockInfo(entity);
                        locks.Add(lockInfo);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to map lock entity: {PartitionKey}/{RowKey}", 
                            entity.PartitionKey, entity.RowKey);
                        // Continue processing other entities
                    }
                }

                _logger.LogInformation("Successfully retrieved {LockCount} locks from distributed lock system", locks.Count);
                return locks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all locks from distributed lock system");
                throw;
            }
        }

        /// <summary>
        /// Maps a table entity to distributed lock info
        /// </summary>
        private static DistributedLockInfo MapToLockInfo(TableEntity entity)
        {
            return new DistributedLockInfo
            {
                LockKey = entity["LockKey"].ToString() ?? string.Empty,
                InstanceId = entity["InstanceId"].ToString() ?? string.Empty,
                AcquiredAt = (DateTime)entity["AcquiredAt"],
                ExpiresAt = (DateTime)entity["ExpiresAt"],
                LastHeartbeat = (DateTime)entity["LastHeartbeat"],
                MigrationId = entity["MigrationId"].ToString() ?? string.Empty,
                Status = Enum.TryParse<DistributedLockStatus>(entity["Status"].ToString(), out var status) ? status : DistributedLockStatus.Unknown,
                RenewalCount = (int)entity["RenewalCount"]
            };
        }
    }
} 