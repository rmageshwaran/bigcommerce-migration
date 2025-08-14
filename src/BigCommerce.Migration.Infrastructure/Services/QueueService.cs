using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using AzureQueueMessage = Azure.Storage.Queues.Models.QueueMessage;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Azure Queue Storage implementation designed to work with Azure Functions output bindings
/// Provides message creation, processing, validation, and monitoring capabilities
/// </summary>
public class QueueService : IQueueService
{
    private readonly QueueServiceClient _queueServiceClient;
    private readonly ILogger<QueueService> _logger;
    private readonly string _connectionString;

    // Queue names (configurable via environment variables)
    private const string MigrationStartQueueName = "migration-start";
    private const string EntityBatchQueueName = "entity-batch";
    private const string BatchCompletionQueueName = "batch-completion";
    private const string DeadLetterQueueName = "dead-letter";

    /// <summary>
    /// Initializes a new instance of the QueueService
    /// </summary>
    /// <param name="configuration">Configuration service</param>
    /// <param name="logger">Logger instance</param>
    public QueueService(IConfiguration configuration, ILogger<QueueService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        
        // Try ConnectionStrings section first, then fall back to Values section (Azure Functions style)
        _connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
            ?? configuration["AzureWebJobsStorage"]
            ?? throw new ArgumentNullException(nameof(configuration), "AzureWebJobsStorage connection string is required");
        
        _queueServiceClient = new QueueServiceClient(_connectionString);
    }

    #region Message Creation (for Output Bindings)

    /// <summary>
    /// Creates a migration start message for queue output binding
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="migrationRequest">Migration request details</param>
    /// <param name="categoryTreeContext">Category tree context for the migration</param>
    /// <returns>Queue message ready for output binding</returns>
    public Core.Models.QueueMessage CreateMigrationStartMessage(string migrationId, MigrationRequest migrationRequest, CategoryTreeContext? categoryTreeContext = null)
    {
        try
        {
            _logger.LogInformation("Creating migration start message: {MigrationId}", migrationId);

            var messageContent = new
            {
                MessageType = "MigrationStart",
                MigrationId = migrationId,
                MigrationRequest = migrationRequest,
                CategoryTreeContext = categoryTreeContext,
                CreatedAt = DateTime.UtcNow,
                Version = "1.0"
            };

            var queueMessage = new Core.Models.QueueMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Content = JsonSerializer.Serialize(messageContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
                MessageType = "MigrationStart",
                InsertionTime = DateTime.UtcNow,
                ExpirationTime = DateTime.UtcNow.AddDays(7) // Messages expire after 7 days
            };

            _logger.LogInformation("Successfully created migration start message: {MessageId}", queueMessage.MessageId);
            return queueMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating migration start message: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Creates an entity batch processing message for queue output binding
    /// </summary>
    /// <param name="batchMessage">Entity batch message</param>
    /// <returns>Queue message ready for output binding</returns>
    public Core.Models.QueueMessage CreateEntityBatchMessage(EntityBatchMessage batchMessage)
    {
        try
        {
            _logger.LogInformation("Creating entity batch message: {MigrationId}, {EntityType}, Batch {BatchNumber}/{TotalBatches}", 
                batchMessage.MigrationId, batchMessage.EntityType, batchMessage.BatchNumber, batchMessage.TotalBatches);

            var messageContent = new
            {
                MessageType = "EntityBatch",
                BatchMessage = batchMessage,
                CreatedAt = DateTime.UtcNow,
                Version = "1.0"
            };

            var queueMessage = new Core.Models.QueueMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Content = JsonSerializer.Serialize(messageContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
                MessageType = "EntityBatch",
                InsertionTime = DateTime.UtcNow,
                ExpirationTime = DateTime.UtcNow.AddDays(7)
            };

            _logger.LogInformation("Successfully created entity batch message: {MessageId}", queueMessage.MessageId);
            return queueMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating entity batch message: {MigrationId}", batchMessage.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Creates a migration cancellation message for queue output binding
    /// </summary>
    /// <param name="migrationId">Migration ID to cancel</param>
    /// <param name="reason">Cancellation reason</param>
    /// <returns>Queue message ready for output binding</returns>
    public Core.Models.QueueMessage CreateCancellationMessage(string migrationId, string reason)
    {
        try
        {
            _logger.LogInformation("Creating cancellation message: {MigrationId}, Reason: {Reason}", migrationId, reason);

            var messageContent = new
            {
                MessageType = "MigrationCancellation",
                MigrationId = migrationId,
                Reason = reason,
                RequestedBy = "System", // Could be enhanced to track actual user
                CreatedAt = DateTime.UtcNow,
                Version = "1.0"
            };

            var queueMessage = new Core.Models.QueueMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Content = JsonSerializer.Serialize(messageContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
                MessageType = "MigrationCancellation",
                InsertionTime = DateTime.UtcNow,
                ExpirationTime = DateTime.UtcNow.AddDays(1) // Cancellations expire quickly
            };

            _logger.LogInformation("Successfully created cancellation message: {MessageId}", queueMessage.MessageId);
            return queueMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating cancellation message: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Creates multiple entity batch messages for parallel processing
    /// </summary>
    /// <param name="batchMessages">List of entity batch messages</param>
    /// <returns>List of queue messages ready for output binding</returns>
    public List<Core.Models.QueueMessage> CreateEntityBatchMessages(List<EntityBatchMessage> batchMessages)
    {
        try
        {
            _logger.LogInformation("Creating {Count} entity batch messages", batchMessages.Count);

            var queueMessages = new List<Core.Models.QueueMessage>();
            
            foreach (var batchMessage in batchMessages)
            {
                var queueMessage = CreateEntityBatchMessage(batchMessage);
                queueMessages.Add(queueMessage);
            }

            _logger.LogInformation("Successfully created {Count} entity batch messages", queueMessages.Count);
            return queueMessages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating entity batch messages");
            throw;
        }
    }

    /// <summary>
    /// Creates a batch completion notification message
    /// </summary>
    /// <param name="batchCompletionMessage">Batch completion details</param>
    /// <returns>Queue message ready for output binding</returns>
    public Core.Models.QueueMessage CreateBatchCompletionMessage(BatchCompletionMessage batchCompletionMessage)
    {
        try
        {
            _logger.LogInformation("Creating batch completion message: {MigrationId}, {EntityType}, Batch {BatchNumber}", 
                batchCompletionMessage.MigrationId, batchCompletionMessage.EntityType, batchCompletionMessage.BatchNumber);

            var messageContent = new
            {
                MessageType = "BatchCompletion",
                BatchCompletion = batchCompletionMessage,
                CreatedAt = DateTime.UtcNow,
                Version = "1.0"
            };

            var queueMessage = new Core.Models.QueueMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Content = JsonSerializer.Serialize(messageContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
                MessageType = "BatchCompletion",
                InsertionTime = DateTime.UtcNow,
                ExpirationTime = DateTime.UtcNow.AddDays(7)
            };

            _logger.LogInformation("Successfully created batch completion message: {MessageId}", queueMessage.MessageId);
            return queueMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating batch completion message: {MigrationId}", batchCompletionMessage.MigrationId);
            throw;
        }
    }

    #endregion

    #region Message Processing (from Queue Triggers)

    /// <summary>
    /// Processes a migration start message received from queue trigger
    /// </summary>
    /// <param name="queueMessage">Queue message from trigger</param>
    /// <returns>Processed message result</returns>
    public async Task<MessageProcessingResult> ProcessMigrationStartMessageAsync(Core.Models.QueueMessage queueMessage)
    {
        try
        {
            _logger.LogInformation("Processing migration start message: {MessageId}", queueMessage.MessageId);

            // Validate message format
            var validationResult = await ValidateQueueMessageAsync(queueMessage);
            if (!validationResult.IsValid)
            {
                return new MessageProcessingResult
                {
                    IsSuccess = false,
                    ErrorDetails = validationResult.ValidationError,
                    ShouldRetry = false,
                    ProcessedAt = DateTime.UtcNow
                };
            }

            // Parse message content
            var messageData = await ParseQueueMessageAsync<dynamic>(queueMessage);
            if (messageData == null)
            {
                return new MessageProcessingResult
                {
                    IsSuccess = false,
                    ErrorDetails = "Failed to parse message content",
                    ShouldRetry = false,
                    ProcessedAt = DateTime.UtcNow
                };
            }

            // TODO: Implement actual migration start processing logic
            // For now, simulate processing
            await Task.Delay(100);

            _logger.LogInformation("Successfully processed migration start message: {MessageId}", queueMessage.MessageId);
            
            return new MessageProcessingResult
            {
                IsSuccess = true,
                Message = "Migration start message processed successfully",
                ProcessedAt = DateTime.UtcNow,
                ProcessingDuration = TimeSpan.FromMilliseconds(100),
                Metadata = new Dictionary<string, object>
                {
                    ["MessageType"] = queueMessage.MessageType,
                    ["ProcessedBy"] = Environment.MachineName
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing migration start message: {MessageId}", queueMessage.MessageId);
            
            return new MessageProcessingResult
            {
                IsSuccess = false,
                ErrorDetails = ex.Message,
                ShouldRetry = false, // 🚨 DISABLED: No retries to avoid rate limit issues
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Processes an entity batch message received from queue trigger
    /// </summary>
    /// <param name="queueMessage">Queue message from trigger</param>
    /// <returns>Processed message result</returns>
    public async Task<MessageProcessingResult> ProcessEntityBatchMessageAsync(Core.Models.QueueMessage queueMessage)
    {
        try
        {
            _logger.LogInformation("Processing entity batch message: {MessageId}", queueMessage.MessageId);

            // Validate and parse message
            var validationResult = await ValidateQueueMessageAsync(queueMessage);
            if (!validationResult.IsValid)
            {
                return new MessageProcessingResult
                {
                    IsSuccess = false,
                    ErrorDetails = validationResult.ValidationError,
                    ShouldRetry = false,
                    ProcessedAt = DateTime.UtcNow
                };
            }

            // TODO: Implement actual entity batch processing logic
            // For now, simulate processing
            var processingTime = TimeSpan.FromSeconds(2);
            await Task.Delay(processingTime);

            _logger.LogInformation("Successfully processed entity batch message: {MessageId}", queueMessage.MessageId);
            
            return new MessageProcessingResult
            {
                IsSuccess = true,
                Message = "Entity batch processed successfully",
                ProcessedAt = DateTime.UtcNow,
                ProcessingDuration = processingTime,
                Metadata = new Dictionary<string, object>
                {
                    ["MessageType"] = queueMessage.MessageType,
                    ["ProcessedBy"] = Environment.MachineName
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing entity batch message: {MessageId}", queueMessage.MessageId);
            
            return new MessageProcessingResult
            {
                IsSuccess = false,
                ErrorDetails = ex.Message,
                ShouldRetry = true,
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Processes a cancellation message received from queue trigger
    /// </summary>
    /// <param name="queueMessage">Queue message from trigger</param>
    /// <returns>Processed message result</returns>
    public async Task<MessageProcessingResult> ProcessCancellationMessageAsync(Core.Models.QueueMessage queueMessage)
    {
        try
        {
            _logger.LogInformation("Processing cancellation message: {MessageId}", queueMessage.MessageId);

            // Validate and parse message
            var validationResult = await ValidateQueueMessageAsync(queueMessage);
            if (!validationResult.IsValid)
            {
                return new MessageProcessingResult
                {
                    IsSuccess = false,
                    ErrorDetails = validationResult.ValidationError,
                    ShouldRetry = false,
                    ProcessedAt = DateTime.UtcNow
                };
            }

            // TODO: Implement actual cancellation processing logic
            await Task.Delay(500);

            _logger.LogInformation("Successfully processed cancellation message: {MessageId}", queueMessage.MessageId);
            
            return new MessageProcessingResult
            {
                IsSuccess = true,
                Message = "Cancellation processed successfully",
                ProcessedAt = DateTime.UtcNow,
                ProcessingDuration = TimeSpan.FromMilliseconds(500)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing cancellation message: {MessageId}", queueMessage.MessageId);
            
            return new MessageProcessingResult
            {
                IsSuccess = false,
                ErrorDetails = ex.Message,
                ShouldRetry = true,
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Processes a batch completion message received from queue trigger
    /// </summary>
    /// <param name="queueMessage">Queue message from trigger</param>
    /// <returns>Processed message result</returns>
    public async Task<MessageProcessingResult> ProcessBatchCompletionMessageAsync(Core.Models.QueueMessage queueMessage)
    {
        try
        {
            _logger.LogInformation("Processing batch completion message: {MessageId}", queueMessage.MessageId);

            // Validate and parse message
            var validationResult = await ValidateQueueMessageAsync(queueMessage);
            if (!validationResult.IsValid)
            {
                return new MessageProcessingResult
                {
                    IsSuccess = false,
                    ErrorDetails = validationResult.ValidationError,
                    ShouldRetry = false,
                    ProcessedAt = DateTime.UtcNow
                };
            }

            // TODO: Implement actual batch completion processing logic
            await Task.Delay(300);

            _logger.LogInformation("Successfully processed batch completion message: {MessageId}", queueMessage.MessageId);
            
            return new MessageProcessingResult
            {
                IsSuccess = true,
                Message = "Batch completion processed successfully",
                ProcessedAt = DateTime.UtcNow,
                ProcessingDuration = TimeSpan.FromMilliseconds(300)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch completion message: {MessageId}", queueMessage.MessageId);
            
            return new MessageProcessingResult
            {
                IsSuccess = false,
                ErrorDetails = ex.Message,
                ShouldRetry = true,
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    #endregion

    #region Message Validation and Parsing

    /// <summary>
    /// Validates and parses a queue message content
    /// </summary>
    /// <typeparam name="T">Expected message type</typeparam>
    /// <param name="queueMessage">Queue message to parse</param>
    /// <returns>Parsed message content</returns>
    public async Task<T?> ParseQueueMessageAsync<T>(Core.Models.QueueMessage queueMessage) where T : class
    {
        try
        {
            await Task.CompletedTask; // Placeholder for async operations

            if (string.IsNullOrWhiteSpace(queueMessage.Content))
            {
                _logger.LogWarning("Queue message content is empty: {MessageId}", queueMessage.MessageId);
                return null;
            }

            var parsedContent = JsonSerializer.Deserialize<T>(queueMessage.Content, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            return parsedContent;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse queue message content: {MessageId}", queueMessage.MessageId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing queue message: {MessageId}", queueMessage.MessageId);
            return null;
        }
    }

    /// <summary>
    /// Validates a queue message format and content
    /// </summary>
    /// <param name="queueMessage">Queue message to validate</param>
    /// <returns>Validation result</returns>
    public async Task<MessageValidationResult> ValidateQueueMessageAsync(Core.Models.QueueMessage queueMessage)
    {
        try
        {
            await Task.CompletedTask; // Placeholder for async operations

            var validationResult = new MessageValidationResult
            {
                IsValid = true,
                ValidatedAt = DateTime.UtcNow
            };

            // Basic validation
            if (string.IsNullOrWhiteSpace(queueMessage.MessageId))
            {
                validationResult.IsValid = false;
                validationResult.ValidationIssues.Add("Message ID is required");
            }

            if (string.IsNullOrWhiteSpace(queueMessage.Content))
            {
                validationResult.IsValid = false;
                validationResult.ValidationIssues.Add("Message content is required");
            }

            if (string.IsNullOrWhiteSpace(queueMessage.MessageType))
            {
                validationResult.IsValid = false;
                validationResult.ValidationIssues.Add("Message type is required");
            }

            // Content validation
            if (!string.IsNullOrWhiteSpace(queueMessage.Content))
            {
                try
                {
                    JsonSerializer.Deserialize<object>(queueMessage.Content);
                }
                catch (JsonException)
                {
                    validationResult.IsValid = false;
                    validationResult.ValidationIssues.Add("Message content is not valid JSON");
                }
            }

            // Message type validation
            var validMessageTypes = new[] { "MigrationStart", "EntityBatch", "BatchCompletion", "MigrationCancellation" };
            if (!string.IsNullOrWhiteSpace(queueMessage.MessageType) && 
                !validMessageTypes.Contains(queueMessage.MessageType))
            {
                validationResult.IsValid = false;
                validationResult.ValidationIssues.Add($"Invalid message type: {queueMessage.MessageType}");
            }

            if (!validationResult.IsValid)
            {
                validationResult.ValidationError = string.Join("; ", validationResult.ValidationIssues);
            }

            return validationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating queue message: {MessageId}", queueMessage.MessageId);
            
            return new MessageValidationResult
            {
                IsValid = false,
                ValidationError = "Validation failed due to unexpected error",
                ValidatedAt = DateTime.UtcNow
            };
        }
    }

    #endregion

    #region Dead Letter Queue Handling

    /// <summary>
    /// Creates a dead letter message for failed processing
    /// </summary>
    /// <param name="originalMessage">Original failed message</param>
    /// <param name="errorDetails">Error details</param>
    /// <returns>Dead letter message ready for output binding</returns>
    public DeadLetterMessage CreateDeadLetterMessage(Core.Models.QueueMessage originalMessage, string errorDetails)
    {
        try
        {
            _logger.LogInformation("Creating dead letter message for: {MessageId}", originalMessage.MessageId);

            var deadLetterMessage = new DeadLetterMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                OriginalMessage = originalMessage,
                Reason = "Processing failed",
                ErrorDetails = errorDetails,
                RetryAttempts = originalMessage.DequeueCount,
                MovedAt = DateTime.UtcNow,
                IsRequeued = false
            };

            _logger.LogInformation("Successfully created dead letter message: {DeadLetterMessageId} for original: {OriginalMessageId}", 
                deadLetterMessage.MessageId, originalMessage.MessageId);

            return deadLetterMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating dead letter message for: {MessageId}", originalMessage.MessageId);
            throw;
        }
    }

    /// <summary>
    /// Processes a message from the dead letter queue
    /// </summary>
    /// <param name="deadLetterMessage">Dead letter message to process</param>
    /// <returns>Processing result with retry recommendation</returns>
    public async Task<DeadLetterProcessingResult> ProcessDeadLetterMessageAsync(DeadLetterMessage deadLetterMessage)
    {
        try
        {
            _logger.LogInformation("Processing dead letter message: {MessageId}", deadLetterMessage.MessageId);

            await Task.Delay(100); // Simulate processing

            // Determine if message should be retried based on error type and retry count
            var shouldRetry = false; // 🚨 DISABLED: No dead letter retries to avoid rate limit issues

            var result = new DeadLetterProcessingResult
            {
                IsSuccess = true,
                Message = "Dead letter message processed",
                ShouldRetry = shouldRetry,
                MaxRetryAttempts = 0, // 🚨 DISABLED: No retries
                CurrentRetryAttempt = deadLetterMessage.RetryAttempts,
                ProcessedAt = DateTime.UtcNow,
                ShouldDiscard = true, // 🚨 Always discard to prevent retries
                DiscardReason = "Retries disabled to avoid rate limit issues"
            };

            if (shouldRetry)
            {
                result.RetryDelay = TimeSpan.FromMinutes(Math.Pow(2, deadLetterMessage.RetryAttempts)); // Exponential backoff
            }

            _logger.LogInformation("Dead letter message processing completed: {MessageId}, ShouldRetry: {ShouldRetry}", 
                deadLetterMessage.MessageId, shouldRetry);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing dead letter message: {MessageId}", deadLetterMessage.MessageId);
            
            return new DeadLetterProcessingResult
            {
                IsSuccess = false,
                ErrorDetails = ex.Message,
                ShouldRetry = false,
                ShouldDiscard = true,
                DiscardReason = "Processing error",
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    #endregion

    #region Direct Queue Operations (for immediate sending)

    /// <summary>
    /// Sends a message directly to the specified queue with retry logic and performance optimization
    /// </summary>
    /// <param name="queueName">Target queue name</param>
    /// <param name="queueMessage">Message to send</param>
    /// <param name="retryCount">Number of retry attempts (default: 0 - no retries)</param>
    /// <returns>Task representing the send operation</returns>
    public async Task SendMessageAsync(string queueName, Core.Models.QueueMessage queueMessage, int retryCount = 0)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Sending message to queue {QueueName}: {MessageId}", queueName, queueMessage.MessageId);

            // Get or create the queue client (connection pooling handled by Azure SDK)
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();

            // Send the message content with retry logic
            await SendMessageWithRetryAsync(queueClient, queueMessage, retryCount);

            stopwatch.Stop();
            _logger.LogInformation("Successfully sent message to queue {QueueName}: {MessageId} in {ElapsedMs}ms", 
                queueName, queueMessage.MessageId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to send message to queue {QueueName}: {MessageId} after {ElapsedMs}ms. Exception: {ExceptionType}: {ExceptionMessage}", 
                queueName, queueMessage.MessageId, stopwatch.ElapsedMilliseconds, ex.GetType().Name, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Sends multiple messages to the same queue in batch for optimal performance
    /// </summary>
    /// <param name="queueName">Target queue name</param>
    /// <param name="queueMessages">Messages to send</param>
    /// <returns>Task representing the batch send operation</returns>
    public async Task SendMessageBatchAsync(string queueName, IEnumerable<Core.Models.QueueMessage> queueMessages)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var messagesList = queueMessages.ToList();
        
        try
        {
            _logger.LogInformation("Sending {Count} messages to queue {QueueName}", messagesList.Count, queueName);

            // Get or create the queue client
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();

            // Send messages in parallel with controlled concurrency
            var concurrencyLevel = Math.Min(messagesList.Count, 10); // Max 10 concurrent operations
            var semaphore = new SemaphoreSlim(concurrencyLevel, concurrencyLevel);
            
            var tasks = messagesList.Select(async message =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await SendMessageWithRetryAsync(queueClient, message, retryCount: 3);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            stopwatch.Stop();
            _logger.LogInformation("Successfully sent {Count} messages to queue {QueueName} in {ElapsedMs}ms", 
                messagesList.Count, queueName, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to send batch messages to queue {QueueName} after {ElapsedMs}ms", 
                queueName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Internal method to send a message with exponential backoff retry logic
    /// </summary>
    /// <param name="queueClient">Queue client</param>
    /// <param name="queueMessage">Message to send</param>
    /// <param name="retryCount">Number of retry attempts</param>
    /// <returns>Task representing the send operation</returns>
    private async Task SendMessageWithRetryAsync(Azure.Storage.Queues.QueueClient queueClient, Core.Models.QueueMessage queueMessage, int retryCount)
    {
        var maxRetries = Math.Max(1, retryCount);
        var attempt = 0;
        
        while (attempt < maxRetries)
        {
            try
            {
                attempt++;
                
                // Azure Storage Queues require Base64 encoded content when using direct QueueClient calls
                // (Output bindings handle this automatically, but we're using direct calls)
                var messageContent = queueMessage.Content ?? "";
                var encodedContent = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(messageContent));

                await queueClient.SendMessageAsync(encodedContent);
                
                return; // Success
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                var delayMs = (int)Math.Pow(2, attempt - 1) * 1000; // Exponential backoff: 1s, 2s, 4s
                
                _logger.LogWarning("Failed to send message {MessageId}, attempt {Attempt}/{MaxRetries}. Retrying in {DelayMs}ms. Error: {Error}",
                    queueMessage.MessageId, attempt, maxRetries, delayMs, ex.Message);
                
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Final attempt failed for message {MessageId}, attempt {Attempt}/{MaxRetries}. Error: {Error}",
                    queueMessage.MessageId, attempt, maxRetries, ex.Message);
                throw;
            }
        }
        
        // If we get here, all retries failed
        var finalError = $"Failed to send message {queueMessage.MessageId} after {maxRetries} attempts";
        _logger.LogError(finalError);
        throw new InvalidOperationException(finalError);
    }

    /// <summary>
    /// Sends a migration start message directly to the migration-start queue
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="migrationRequest">Migration request</param>
    /// <param name="categoryTreeContext">Category tree context for the migration</param>
    /// <returns>Task representing the send operation</returns>
    public async Task SendMigrationStartMessageAsync(string migrationId, MigrationRequest migrationRequest, CategoryTreeContext? categoryTreeContext = null)
    {
        var queueMessage = CreateMigrationStartMessage(migrationId, migrationRequest, categoryTreeContext);
        await SendMessageAsync(MigrationStartQueueName, queueMessage);
        
        _logger.LogInformation("Migration start message sent to queue for MigrationId: {MigrationId}", migrationId);
    }



    #endregion

    #region Queue Statistics (Direct Azure SDK when needed)

    /// <summary>
    /// Gets queue statistics for monitoring (uses Azure SDK directly)
    /// </summary>
    /// <param name="queueName">Queue name</param>
    /// <returns>Queue statistics</returns>
    public async Task<QueueStatistics> GetQueueStatisticsAsync(string queueName)
    {
        try
        {
            _logger.LogInformation("Getting queue statistics for: {QueueName}", queueName);

            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            
            // Ensure queue exists
            await queueClient.CreateIfNotExistsAsync();
            
            // Get queue properties
            var properties = await queueClient.GetPropertiesAsync();
            
            var statistics = new QueueStatistics
            {
                QueueName = queueName,
                ApproximateMessageCount = properties.Value.ApproximateMessagesCount,
                AverageMessageAge = TimeSpan.Zero, // Would need to track separately
                ThroughputPerMinute = 0, // Would need to track separately
                AverageProcessingTime = TimeSpan.Zero, // Would need to track separately
                MessagesInFlight = 0, // Would need to track separately
                DeadLetterCount = 0, // Would need to query dead letter queue
                CalculatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("Successfully retrieved queue statistics for: {QueueName}", queueName);
            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting queue statistics for: {QueueName}", queueName);
            throw;
        }
    }

    /// <summary>
    /// Gets the approximate number of messages in the queue (uses Azure SDK directly)
    /// </summary>
    /// <param name="queueName">Queue name</param>
    /// <returns>Approximate message count</returns>
    public async Task<int> GetApproximateMessageCountAsync(string queueName)
    {
        try
        {
            _logger.LogInformation("Getting message count for queue: {QueueName}", queueName);

            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            
            // Ensure queue exists
            await queueClient.CreateIfNotExistsAsync();
            
            // Get queue properties
            var properties = await queueClient.GetPropertiesAsync();
            
            var messageCount = properties.Value.ApproximateMessagesCount;
            
            _logger.LogInformation("Queue {QueueName} has approximately {MessageCount} messages", queueName, messageCount);
            return messageCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting message count for queue: {QueueName}", queueName);
            throw;
        }
    }

    #endregion
} 