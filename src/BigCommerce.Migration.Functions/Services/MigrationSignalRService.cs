using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Functions.Hubs;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Functions.Services
{
    /// <summary>
    /// SignalR service implementation for broadcasting real-time migration updates
    /// </summary>
    public class MigrationSignalRService : IMigrationSignalRService
    {
        private readonly ILogger<MigrationSignalRService> _logger;
        private readonly MigrationHub _migrationHub;

        public MigrationSignalRService(
            ILogger<MigrationSignalRService> logger,
            MigrationHub migrationHub)
        {
            _logger = logger;
            _migrationHub = migrationHub;
        }

        /// <inheritdoc />
        public async Task BroadcastProgressUpdateAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken = default)
        {
            try
            {
                await _migrationHub.SendMigrationProgress(migrationId, progress, cancellationToken);
                _logger.LogDebug("Broadcasted progress update for migration {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast progress update for migration {MigrationId}", migrationId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastStatusUpdateAsync(string migrationId, object status, CancellationToken cancellationToken = default)
        {
            try
            {
                await _migrationHub.SendMigrationStatus(migrationId, status, cancellationToken);
                _logger.LogDebug("Broadcasted status update for migration {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast status update for migration {MigrationId}", migrationId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastEntityStartAsync(string migrationId, string entityType, int totalCount, CancellationToken cancellationToken = default)
        {
            try
            {
                var startData = new
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    TotalCount = totalCount,
                    Phase = "started",
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, startData, cancellationToken);
                _logger.LogDebug("Broadcasted entity start for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast entity start for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastEntityCompletionAsync(string migrationId, string entityType, object results, CancellationToken cancellationToken = default)
        {
            try
            {
                var completionData = new
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    Phase = "completed",
                    Results = results,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, completionData, cancellationToken);
                _logger.LogDebug("Broadcasted entity completion for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast entity completion for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastBatchCompletionAsync(string migrationId, string entityType, int batchNumber, object batchResults, CancellationToken cancellationToken = default)
        {
            try
            {
                var batchData = new
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    BatchNumber = batchNumber,
                    Phase = "batch_completed",
                    Results = batchResults,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, batchData, cancellationToken);
                _logger.LogDebug("Broadcasted batch completion for migration {MigrationId}, entity {EntityType}, batch {BatchNumber}", migrationId, entityType, batchNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast batch completion for migration {MigrationId}, entity {EntityType}, batch {BatchNumber}", migrationId, entityType, batchNumber);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastSystemHealthAsync(object healthData, CancellationToken cancellationToken = default)
        {
            try
            {
                await _migrationHub.SendSystemHealth(healthData, cancellationToken);
                _logger.LogDebug("Broadcasted system health update");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast system health update");
                throw;
            }
        }
    }

    /// <summary>
    /// No-op SignalR service implementation for when SignalR is not configured
    /// </summary>
    public class NoOpSignalRService : IMigrationSignalRService
    {
        private readonly ILogger<NoOpSignalRService> _logger;

        public NoOpSignalRService(ILogger<NoOpSignalRService> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public Task BroadcastProgressUpdateAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("SignalR not configured - skipping progress broadcast for migration {MigrationId}", migrationId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task BroadcastStatusUpdateAsync(string migrationId, object status, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("SignalR not configured - skipping status broadcast for migration {MigrationId}", migrationId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task BroadcastEntityStartAsync(string migrationId, string entityType, int totalCount, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("SignalR not configured - skipping entity start broadcast for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task BroadcastEntityCompletionAsync(string migrationId, string entityType, object results, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("SignalR not configured - skipping entity completion broadcast for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task BroadcastBatchCompletionAsync(string migrationId, string entityType, int batchNumber, object batchResults, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("SignalR not configured - skipping batch completion broadcast for migration {MigrationId}, entity {EntityType}, batch {BatchNumber}", migrationId, entityType, batchNumber);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task BroadcastSystemHealthAsync(object healthData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("SignalR not configured - skipping system health broadcast");
            return Task.CompletedTask;
        }
    }
} 