using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Azure Storage implementation of IProgressQueueService
    /// Encapsulates Azure Storage Queue operations for progress events following SOLID principles
    /// </summary>
    public class AzureProgressQueueService : IProgressQueueService
    {
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ILogger<AzureProgressQueueService> _logger;

        /// <summary>
        /// Initializes a new instance of the AzureProgressQueueService
        /// </summary>
        /// <param name="configuration">Configuration service for connection string</param>
        /// <param name="logger">Logger instance</param>
        public AzureProgressQueueService(IConfiguration configuration, ILogger<AzureProgressQueueService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Get connection string using the same pattern as ProgressEventPublisher
            var connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
                ?? configuration["AzureWebJobsStorage"]
                ?? throw new ArgumentNullException(nameof(configuration), "AzureWebJobsStorage connection string is required");
                
            _queueServiceClient = new QueueServiceClient(connectionString);
            
            _logger.LogInformation("🔧 [PROGRESS-QUEUE] AzureProgressQueueService initialized with connection string");
        }

        /// <inheritdoc />
        public async Task SendJsonMessageAsync(string queueName, string jsonMessage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(queueName))
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));
            
            if (string.IsNullOrEmpty(jsonMessage))
                throw new ArgumentException("JSON message cannot be null or empty", nameof(jsonMessage));

            try
            {
                _logger.LogInformation("📬 [PROGRESS-QUEUE] Sending message to queue: {QueueName}, MessageSize: {MessageSize} bytes", 
                    queueName, jsonMessage.Length);

                // Get queue client
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                
                // Azure Storage Queues require Base64 encoded content for JSON messages
                // This matches the behavior in the original ProgressEventPublisher
                var encodedContent = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonMessage));
                
                _logger.LogDebug("🔐 [PROGRESS-QUEUE] Base64 encoded message: {EncodedLength} chars", encodedContent.Length);

                // Send message to queue
                await queueClient.SendMessageAsync(encodedContent, cancellationToken);

                _logger.LogInformation("✅ [PROGRESS-QUEUE] Successfully sent message to queue: {QueueName}", queueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [PROGRESS-QUEUE] Failed to send message to queue: {QueueName}", queueName);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task EnsureQueueExistsAsync(string queueName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(queueName))
                throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));

            try
            {
                _logger.LogInformation("🏗️ [PROGRESS-QUEUE] Ensuring queue exists: {QueueName}", queueName);
                
                // Get queue client
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                
                // Create queue if it doesn't exist
                await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                
                _logger.LogInformation("✅ [PROGRESS-QUEUE] Queue ready: {QueueName}", queueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [PROGRESS-QUEUE] Failed to ensure queue exists: {QueueName}", queueName);
                throw;
            }
        }
    }
} 