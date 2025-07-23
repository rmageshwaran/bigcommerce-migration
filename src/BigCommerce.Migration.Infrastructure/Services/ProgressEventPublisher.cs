using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Implementation of IProgressEventPublisher using Azure Storage Queues
    /// SOLID: Single Responsibility - handles only progress event publishing to queues
    /// SOLID: Open/Closed - open for extension through interface, closed for modification
    /// SOLID: Dependency Inversion - depends on abstractions (ILogger, QueueServiceClient)
    /// </summary>
    public class ProgressEventPublisher : IProgressEventPublisher
    {
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ILogger<ProgressEventPublisher> _logger;
        private const string PROGRESS_QUEUE_NAME = "signalr-progress-events";

        /// <summary>
        /// Initializes a new instance of ProgressEventPublisher
        /// </summary>
        /// <param name="configuration">Configuration to get connection string</param>
        /// <param name="logger">Logger instance</param>
        public ProgressEventPublisher(
            IConfiguration configuration,
            ILogger<ProgressEventPublisher> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Create QueueServiceClient directly like QueueService does (which works!)
            var connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
                ?? configuration["AzureWebJobsStorage"]
                ?? throw new ArgumentNullException(nameof(configuration), "AzureWebJobsStorage connection string is required");
                
            _queueServiceClient = new QueueServiceClient(connectionString);
            
            _logger.LogInformation("🔧 [DEBUG] ProgressEventPublisher created QueueServiceClient directly with connection string");
        }

        /// <summary>
        /// Publishes a migration progress event to the queue
        /// </summary>
        public async Task PublishMigrationProgressAsync(MigrationProgressEvent progressEvent, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("🚀 [SIGNALR-PUB] Publishing MigrationProgressEvent for MigrationId: {MigrationId}, Status: {Status}, Progress: {Progress}%", 
                progressEvent.MigrationId, progressEvent.Status, progressEvent.OverallProgress);
            await PublishAsync(progressEvent, cancellationToken);
        }

        /// <summary>
        /// Publishes a batch progress event to the queue
        /// </summary>
        public async Task PublishBatchProgressAsync(BatchProgressEvent batchEvent, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("🚀 [SIGNALR-PUB] Publishing BatchProgressEvent for MigrationId: {MigrationId}, EntityType: {EntityType}, Batch: {BatchNumber}/{TotalBatches}", 
                batchEvent.MigrationId, batchEvent.EntityType, batchEvent.BatchNumber, batchEvent.TotalBatches);
            await PublishAsync(batchEvent, cancellationToken);
        }

        /// <summary>
        /// Publishes an entity progress event to the queue
        /// </summary>
        public async Task PublishEntityProgressAsync(EntityProgressEvent entityEvent, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("🚀 [SIGNALR-PUB] Publishing EntityProgressEvent for MigrationId: {MigrationId}, EntityType: {EntityType}, Status: {Status}, Progress: {ProcessedCount}/{TotalCount}", 
                entityEvent.MigrationId, entityEvent.EntityType, entityEvent.Status, entityEvent.ProcessedCount, entityEvent.TotalCount);
            await PublishAsync(entityEvent, cancellationToken);
        }

        /// <summary>
        /// Publishes an error event to the queue
        /// </summary>
        public async Task PublishErrorAsync(ErrorProgressEvent errorEvent, CancellationToken cancellationToken = default)
        {
            await PublishAsync(errorEvent, cancellationToken);
        }

        /// <summary>
        /// Publishes a status change event to the queue
        /// </summary>
        public async Task PublishStatusAsync(StatusProgressEvent statusEvent, CancellationToken cancellationToken = default)
        {
            await PublishAsync(statusEvent, cancellationToken);
        }

        /// <summary>
        /// Publishes any progress event to the queue (core implementation)
        /// SOLID: Single Responsibility - handles only the queue publishing logic
        /// </summary>
        public async Task PublishAsync(ProgressEvent progressEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("📤 [QUEUE-PUB] Starting to publish progress event for MigrationId: {MigrationId}, EventType: {EventType}", 
                    progressEvent?.MigrationId ?? "null", progressEvent?.EventType ?? "null");

                // Validate input
                if (progressEvent == null)
                {
                    _logger.LogWarning("❌ [QUEUE-PUB] Attempted to publish null progress event");
                    return;
                }

                if (string.IsNullOrEmpty(progressEvent.MigrationId))
                {
                    _logger.LogWarning("❌ [QUEUE-PUB] Attempted to publish progress event with null or empty MigrationId");
                    return;
                }

                _logger.LogInformation("🔄 [QUEUE-PUB] Getting or creating queue: {QueueName}", PROGRESS_QUEUE_NAME);

                // Get or create the queue
                var queueClient = await GetOrCreateQueueAsync(cancellationToken);

                _logger.LogInformation("📝 [QUEUE-PUB] Serializing progress event to JSON for MigrationId: {MigrationId}", progressEvent.MigrationId);

                // Serialize the event to JSON
                var messageContent = JsonSerializer.Serialize(progressEvent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                _logger.LogInformation("📬 [QUEUE-PUB] Sending message to queue for MigrationId: {MigrationId}, MessageSize: {MessageSize} bytes", 
                    progressEvent.MigrationId, messageContent.Length);

                // Azure Storage Queues require Base64 encoded content when using direct QueueClient calls
                // (Output bindings handle this automatically, but we're using direct calls like QueueService)
                var encodedContent = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(messageContent));
                
                _logger.LogInformation("🔐 [QUEUE-PUB] Base64 encoded message for queue (like migration-start queue does): {EncodedLength} chars", encodedContent.Length);

                // Send message to queue
                await queueClient.SendMessageAsync(encodedContent, cancellationToken);

                _logger.LogInformation("✅ [QUEUE-PUB] Successfully published progress event to queue! MigrationId: {MigrationId}, EventType: {EventType}, HubMethod: {HubMethod}",
                    progressEvent.MigrationId,
                    progressEvent.EventType,
                    progressEvent.HubMethod);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "💥 [QUEUE-PUB] Failed to publish progress event to queue. MigrationId: {MigrationId}, EventType: {EventType}",
                    progressEvent?.MigrationId ?? "Unknown",
                    progressEvent?.EventType ?? "Unknown");
                
                // Don't rethrow - progress events should not break the migration
                // The migration can continue without real-time updates
            }
        }

        /// <summary>
        /// Gets or creates the progress events queue
        /// SOLID: Single Responsibility - handles only queue creation logic
        /// </summary>
        private async Task<QueueClient> GetOrCreateQueueAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("🔧 [QUEUE-SETUP] Getting queue client for: {QueueName}", PROGRESS_QUEUE_NAME);
                var queueClient = _queueServiceClient.GetQueueClient(PROGRESS_QUEUE_NAME);
                
                _logger.LogInformation("🏗️ [QUEUE-SETUP] Creating queue if not exists: {QueueName}", PROGRESS_QUEUE_NAME);
                
                // Create queue if it doesn't exist
                await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                
                _logger.LogInformation("✅ [QUEUE-SETUP] Queue client ready: {QueueName}", PROGRESS_QUEUE_NAME);
                return queueClient;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [QUEUE-SETUP] Failed to get or create progress events queue: {QueueName}", PROGRESS_QUEUE_NAME);
                throw;
            }
        }
    }
} 