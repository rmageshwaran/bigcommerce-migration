using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Service for publishing progress events to Azure Storage Queue for SignalR broadcasting
    /// Follows SOLID principles: Single Responsibility (progress event publishing only)
    /// Uses dependency injection for queue operations (IProgressQueueService)
    /// </summary>
    public class ProgressEventPublisher : IProgressEventPublisher
    {
        private const string PROGRESS_QUEUE_NAME = "signalr-progress-events";
        
        private readonly IProgressQueueService _progressQueueService;
        private readonly ILogger<ProgressEventPublisher> _logger;

        /// <summary>
        /// Initializes a new instance of the ProgressEventPublisher
        /// </summary>
        /// <param name="progressQueueService">Progress queue service for publishing events</param>
        /// <param name="logger">Logger instance</param>
        public ProgressEventPublisher(IProgressQueueService progressQueueService, ILogger<ProgressEventPublisher> logger)
        {
            _progressQueueService = progressQueueService ?? throw new ArgumentNullException(nameof(progressQueueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _logger.LogInformation("🔧 [PROGRESS-PUB] ProgressEventPublisher initialized with IProgressQueueService dependency");
        }

        /// <inheritdoc />
        public async Task PublishAsync(ProgressEvent progressEvent, CancellationToken cancellationToken = default)
        {
            if (progressEvent == null)
            {
                _logger.LogWarning("❌ [PROGRESS-PUB] Attempted to publish null progress event");
                return;
            }

            if (string.IsNullOrEmpty(progressEvent.MigrationId))
            {
                _logger.LogWarning("❌ [PROGRESS-PUB] Attempted to publish progress event with null or empty MigrationId");
                return;
            }

            try
            {
                _logger.LogInformation("📤 [PROGRESS-PUB] Starting to publish progress event for MigrationId: {MigrationId}, EventType: {EventType}", 
                    progressEvent.MigrationId, progressEvent.EventType);

                // Ensure queue exists first
                await _progressQueueService.EnsureQueueExistsAsync(PROGRESS_QUEUE_NAME, cancellationToken);

                _logger.LogInformation("📝 [PROGRESS-PUB] Serializing progress event to JSON for MigrationId: {MigrationId}", progressEvent.MigrationId);

                // Serialize the event to JSON
                var messageContent = JsonSerializer.Serialize(progressEvent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                // Send message via queue service
                await _progressQueueService.SendJsonMessageAsync(PROGRESS_QUEUE_NAME, messageContent, cancellationToken);

                _logger.LogInformation("✅ [PROGRESS-PUB] Successfully published progress event! MigrationId: {MigrationId}, EventType: {EventType}, HubMethod: {HubMethod}",
                    progressEvent.MigrationId,
                    progressEvent.EventType,
                    progressEvent.HubMethod);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "💥 [PROGRESS-PUB] Failed to publish progress event to queue. MigrationId: {MigrationId}, EventType: {EventType}",
                    progressEvent.MigrationId,
                    progressEvent.EventType);
                
                // Don't rethrow - we don't want progress publishing failures to break the migration
                // This follows the resilience pattern where progress events are "nice to have"
            }
        }

        /// <inheritdoc />
        public async Task PublishMigrationStartedAsync(MigrationStartedEvent startedEvent, CancellationToken cancellationToken = default)
        {
            await PublishAsync(startedEvent, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PublishEntityStartedAsync(EntityStartedEvent entityStartedEvent, CancellationToken cancellationToken = default)
        {
            await PublishAsync(entityStartedEvent, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PublishEntityChunkProgressAsync(EntityChunkProgressEvent chunkEvent, CancellationToken cancellationToken = default)
        {
            await PublishAsync(chunkEvent, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PublishMigrationCompletedAsync(MigrationCompletedEvent completedEvent, CancellationToken cancellationToken = default)
        {
            await PublishAsync(completedEvent, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PublishErrorAsync(ErrorProgressEvent errorEvent, CancellationToken cancellationToken = default)
        {
            await PublishAsync(errorEvent, cancellationToken).ConfigureAwait(false);
        }
    }
} 