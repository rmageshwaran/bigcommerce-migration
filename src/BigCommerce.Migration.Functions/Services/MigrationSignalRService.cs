using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Functions.Hubs;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System;
using System.Text;
using System.Text.Json;

namespace BigCommerce.Migration.Functions.Services
{
    /// <summary>
    /// SignalR service implementation for Azure Functions using output bindings
    /// </summary>
    public class AzureFunctionsSignalRService : IMigrationSignalRService
    {
        private readonly ILogger<AzureFunctionsSignalRService> _logger;
        private readonly HttpClient _httpClient;
        private readonly SignalRConfiguration _signalRConfig;

        public AzureFunctionsSignalRService(ILogger<AzureFunctionsSignalRService> logger, HttpClient httpClient, SignalRConfiguration signalRConfig)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _signalRConfig = signalRConfig ?? throw new ArgumentNullException(nameof(signalRConfig));
        }

        /// <inheritdoc />
        public async Task BroadcastProgressUpdateAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting progress update for migration {MigrationId} via SignalR", migrationId);
                
                var progressData = new
                {
                    migrationId,
                    progress = new
                    {
                        processedEntities = progress.ProcessedEntities,
                        totalEntities = progress.TotalEntities,
                        status = progress.Status,
                        completionPercentage = (progress.TotalEntities > 0) ? (double)progress.ProcessedEntities / progress.TotalEntities * 100 : 0,
                        entitiesPerSecond = progress.EntitiesPerSecond,
                        currentPhase = progress.CurrentPhase,
                        timestamp = DateTime.UtcNow
                    }
                };

                await CallSignalREndpoint("/api/signalr/migration-progress", progressData, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast progress update for migration {MigrationId}", migrationId);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastStatusUpdateAsync(string migrationId, object status, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting status update for migration {MigrationId} via SignalR", migrationId);
                
                var statusData = new
                {
                    migrationId,
                    status,
                    timestamp = DateTime.UtcNow
                };

                await CallSignalREndpoint("/api/signalr/system-health", statusData, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast status update for migration {MigrationId}", migrationId);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastMigrationCompletedAsync(string migrationId, object completionData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting migration completed for migration {MigrationId} via SignalR", migrationId);
                
                var statusMessage = new
                {
                    MigrationId = migrationId,
                    Status = "completed",
                    Data = completionData,
                    Timestamp = DateTime.UtcNow
                };

                await CallSignalREndpoint("/api/send-migration-status", statusMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast migration completed for migration {MigrationId}", migrationId);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastMigrationFailedAsync(string migrationId, object errorData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting migration failed for migration {MigrationId} via SignalR", migrationId);
                
                var statusMessage = new
                {
                    MigrationId = migrationId,
                    Status = "failed",
                    Error = errorData,
                    Timestamp = DateTime.UtcNow
                };

                await CallSignalREndpoint("/api/send-migration-status", statusMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast migration failed for migration {MigrationId}", migrationId);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastMigrationCancelledAsync(string migrationId, object cancellationData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting migration cancelled for migration {MigrationId} via SignalR", migrationId);
                
                var statusMessage = new
                {
                    MigrationId = migrationId,
                    Status = "cancelled",
                    Data = cancellationData,
                    Timestamp = DateTime.UtcNow
                };

                await CallSignalREndpoint("/api/send-migration-status", statusMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast migration cancelled for migration {MigrationId}", migrationId);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastEntityStartAsync(string migrationId, string entityType, int totalCount, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting entity start for migration {MigrationId}, entity {EntityType} via SignalR", migrationId, entityType);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast entity start for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastEntityCompletionAsync(string migrationId, string entityType, object results, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting entity completion for migration {MigrationId}, entity {EntityType} via SignalR", migrationId, entityType);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast entity completion for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastBatchCompletionAsync(string migrationId, string entityType, int batchNumber, object batchResults, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting batch completion for migration {MigrationId}, entity {EntityType}, batch {BatchNumber} via SignalR", migrationId, entityType, batchNumber);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast batch completion for migration {MigrationId}, entity {EntityType}, batch {BatchNumber}", migrationId, entityType, batchNumber);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastSystemHealthAsync(object healthData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting system health via SignalR");
                await CallSignalREndpoint("/api/signalr/system-health", healthData, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast system health");
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastMigrationStartedAsync(string migrationId, object migrationData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting migration started for migration {MigrationId} via SignalR", migrationId);
                
                var statusMessage = new
                {
                    MigrationId = migrationId,
                    Status = "started",
                    Data = migrationData,
                    Timestamp = DateTime.UtcNow
                };

                await CallSignalREndpoint("/api/send-migration-status", statusMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast migration started for migration {MigrationId}", migrationId);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastEntityPhaseStartAsync(string migrationId, string entityType, object phaseData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting entity phase start for migration {MigrationId}, entity {EntityType} via SignalR", migrationId, entityType);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast entity phase start for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastEntityPhaseCompletedAsync(string migrationId, string entityType, object completionData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting entity phase completed for migration {MigrationId}, entity {EntityType} via SignalR", migrationId, entityType);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast entity phase completed for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastErrorNotificationAsync(string migrationId, object errorData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting error notification for migration {MigrationId} via SignalR", migrationId);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast error notification for migration {MigrationId}", migrationId);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <inheritdoc />
        public async Task BroadcastSystemAlertAsync(string alertType, object alertData, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Broadcasting system alert type {AlertType} via SignalR", alertType);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast system alert for type {AlertType}", alertType);
                // Don't rethrow to avoid breaking the migration workflow
            }
        }

        /// <summary>
        /// Helper method to call SignalR endpoints with connection fallback
        /// </summary>
        private async Task CallSignalREndpoint(string endpoint, object data, CancellationToken cancellationToken = default)
        {
            // Check if SignalR is disabled
            if (!_signalRConfig.Enabled)
            {
                if (_signalRConfig.EnableDebugLogging)
                {
                    _logger.LogDebug("SignalR is disabled in configuration - skipping endpoint {Endpoint}", endpoint);
                }
                return;
            }

            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(data);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                // Use configurable timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_signalRConfig.TimeoutSeconds));
                
                // Use configurable base URL instead of hardcoded localhost:7071
                var fullUrl = _signalRConfig.GetFullUrl(endpoint);
                var response = await _httpClient.PostAsync(fullUrl, content, timeoutCts.Token);
                
                if (response.IsSuccessStatusCode)
                {
                    if (_signalRConfig.EnableDebugLogging)
                    {
                        _logger.LogDebug("Successfully called SignalR endpoint {Endpoint} at {FullUrl}", endpoint, fullUrl);
                    }
                }
                else
                {
                    _logger.LogDebug("SignalR endpoint {Endpoint} at {FullUrl} returned status {StatusCode} - this is expected when dashboard is not running", endpoint, fullUrl, response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Respect the original cancellation token
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SignalR endpoint {Endpoint} at {BaseUrl} is unavailable - this is expected when dashboard is not running. Error: {Error}", endpoint, _signalRConfig.BaseUrl, ex.Message);
                // Don't rethrow to avoid breaking the migration flow - SignalR is optional for functionality
            }
        }
    }

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

        /// <inheritdoc />
        public async Task BroadcastMigrationStartedAsync(string migrationId, object migrationData, CancellationToken cancellationToken = default)
        {
            try
            {
                var startData = new
                {
                    MigrationId = migrationId,
                    Status = "started",
                    Data = migrationData,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, startData, cancellationToken);
                _logger.LogDebug("Broadcasted migration started for migration {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast migration started for migration {MigrationId}", migrationId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastMigrationCompletedAsync(string migrationId, object completionData, CancellationToken cancellationToken = default)
        {
            try
            {
                var completeData = new
                {
                    MigrationId = migrationId,
                    Status = "completed",
                    Data = completionData,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, completeData, cancellationToken);
                _logger.LogDebug("Broadcasted migration completed for migration {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast migration completed for migration {MigrationId}", migrationId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastMigrationFailedAsync(string migrationId, object errorData, CancellationToken cancellationToken = default)
        {
            try
            {
                var failureData = new
                {
                    MigrationId = migrationId,
                    Status = "failed",
                    Error = errorData,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, failureData, cancellationToken);
                _logger.LogDebug("Broadcasted migration failed for migration {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast migration failed for migration {MigrationId}", migrationId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastMigrationCancelledAsync(string migrationId, object cancellationData, CancellationToken cancellationToken = default)
        {
            try
            {
                var cancelData = new
                {
                    MigrationId = migrationId,
                    Status = "cancelled",
                    Data = cancellationData,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, cancelData, cancellationToken);
                _logger.LogDebug("Broadcasted migration cancelled for migration {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast migration cancelled for migration {MigrationId}", migrationId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastEntityPhaseStartAsync(string migrationId, string entityType, object phaseData, CancellationToken cancellationToken = default)
        {
            try
            {
                var startData = new
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    Phase = "phase_started",
                    Data = phaseData,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, startData, cancellationToken);
                _logger.LogDebug("Broadcasted entity phase start for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast entity phase start for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastEntityPhaseCompletedAsync(string migrationId, string entityType, object completionData, CancellationToken cancellationToken = default)
        {
            try
            {
                var completeData = new
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    Phase = "phase_completed",
                    Data = completionData,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, completeData, cancellationToken);
                _logger.LogDebug("Broadcasted entity phase completed for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast entity phase completed for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastErrorNotificationAsync(string migrationId, object errorData, CancellationToken cancellationToken = default)
        {
            try
            {
                var errorNotification = new
                {
                    MigrationId = migrationId,
                    Type = "error",
                    Error = errorData,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendMigrationStatus(migrationId, errorNotification, cancellationToken);
                _logger.LogDebug("Broadcasted error notification for migration {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast error notification for migration {MigrationId}", migrationId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task BroadcastSystemAlertAsync(string alertType, object alertData, CancellationToken cancellationToken = default)
        {
            try
            {
                var alert = new
                {
                    Type = alertType,
                    Data = alertData,
                    Timestamp = DateTime.UtcNow
                };

                await _migrationHub.SendSystemHealth(alert, cancellationToken);
                _logger.LogDebug("Broadcasted system alert for type {AlertType}", alertType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast system alert for type {AlertType}", alertType);
                throw;
            }
        }
    }

    /// <summary>
    /// No-op implementation of SignalR service for testing or when SignalR is not configured
    /// </summary>
    public class NoOpSignalRService : IMigrationSignalRService
    {
        private readonly ILogger<NoOpSignalRService> _logger;

        public NoOpSignalRService(ILogger<NoOpSignalRService> logger)
        {
            _logger = logger;
        }

        public Task BroadcastProgressUpdateAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Progress update for migration {MigrationId} (SignalR not configured)", migrationId);
            return Task.CompletedTask;
        }

        public Task BroadcastStatusUpdateAsync(string migrationId, object status, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Status update for migration {MigrationId} (SignalR not configured)", migrationId);
            return Task.CompletedTask;
        }

        public Task BroadcastEntityStartAsync(string migrationId, string entityType, int totalCount, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Entity start for migration {MigrationId}, entity {EntityType} (SignalR not configured)", migrationId, entityType);
            return Task.CompletedTask;
        }

        public Task BroadcastEntityCompletionAsync(string migrationId, string entityType, object results, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Entity completion for migration {MigrationId}, entity {EntityType} (SignalR not configured)", migrationId, entityType);
            return Task.CompletedTask;
        }

        public Task BroadcastBatchCompletionAsync(string migrationId, string entityType, int batchNumber, object batchResults, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Batch completion for migration {MigrationId}, entity {EntityType}, batch {BatchNumber} (SignalR not configured)", migrationId, entityType, batchNumber);
            return Task.CompletedTask;
        }

        public Task BroadcastSystemHealthAsync(object healthData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: System health update (SignalR not configured)");
            return Task.CompletedTask;
        }

        public Task BroadcastMigrationStartedAsync(string migrationId, object migrationData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Migration started for migration {MigrationId} (SignalR not configured)", migrationId);
            return Task.CompletedTask;
        }

        public Task BroadcastMigrationCompletedAsync(string migrationId, object completionData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Migration completed for migration {MigrationId} (SignalR not configured)", migrationId);
            return Task.CompletedTask;
        }

        public Task BroadcastMigrationFailedAsync(string migrationId, object errorData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Migration failed for migration {MigrationId} (SignalR not configured)", migrationId);
            return Task.CompletedTask;
        }

        public Task BroadcastMigrationCancelledAsync(string migrationId, object cancellationData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Migration cancelled for migration {MigrationId} (SignalR not configured)", migrationId);
            return Task.CompletedTask;
        }

        public Task BroadcastEntityPhaseStartAsync(string migrationId, string entityType, object phaseData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Entity phase start for migration {MigrationId}, entity {EntityType} (SignalR not configured)", migrationId, entityType);
            return Task.CompletedTask;
        }

        public Task BroadcastEntityPhaseCompletedAsync(string migrationId, string entityType, object completionData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Entity phase completed for migration {MigrationId}, entity {EntityType} (SignalR not configured)", migrationId, entityType);
            return Task.CompletedTask;
        }

        public Task BroadcastErrorNotificationAsync(string migrationId, object errorData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: Error notification for migration {MigrationId} (SignalR not configured)", migrationId);
            return Task.CompletedTask;
        }

        public Task BroadcastSystemAlertAsync(string alertType, object alertData, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("NoOp: System alert for type {AlertType} (SignalR not configured)", alertType);
            return Task.CompletedTask;
        }
    }
} 