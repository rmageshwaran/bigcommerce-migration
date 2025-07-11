using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service interface for Azure Queue Storage operations
/// Designed to work with Azure Functions output bindings
/// </summary>
public interface IQueueService
{
    #region Message Creation (for Output Bindings)

    /// <summary>
    /// Creates a migration start message for queue output binding
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="migrationRequest">Migration request details</param>
    /// <param name="categoryTreeContext">Category tree context for the migration</param>
    /// <returns>Queue message ready for output binding</returns>
    QueueMessage CreateMigrationStartMessage(string migrationId, MigrationRequest migrationRequest, CategoryTreeContext? categoryTreeContext = null);

    /// <summary>
    /// Creates an entity batch processing message for queue output binding
    /// </summary>
    /// <param name="batchMessage">Entity batch message</param>
    /// <returns>Queue message ready for output binding</returns>
    QueueMessage CreateEntityBatchMessage(EntityBatchMessage batchMessage);

    /// <summary>
    /// Creates a migration cancellation message for queue output binding
    /// </summary>
    /// <param name="migrationId">Migration ID to cancel</param>
    /// <param name="reason">Cancellation reason</param>
    /// <returns>Queue message ready for output binding</returns>
    QueueMessage CreateCancellationMessage(string migrationId, string reason);

    /// <summary>
    /// Creates multiple entity batch messages for parallel processing
    /// </summary>
    /// <param name="batchMessages">List of entity batch messages</param>
    /// <returns>List of queue messages ready for output binding</returns>
    List<QueueMessage> CreateEntityBatchMessages(List<EntityBatchMessage> batchMessages);

    /// <summary>
    /// Creates a batch completion notification message
    /// </summary>
    /// <param name="batchCompletionMessage">Batch completion details</param>
    /// <returns>Queue message ready for output binding</returns>
    QueueMessage CreateBatchCompletionMessage(BatchCompletionMessage batchCompletionMessage);

    #endregion

    #region Message Processing (from Queue Triggers)

    /// <summary>
    /// Processes a migration start message received from queue trigger
    /// </summary>
    /// <param name="queueMessage">Queue message from trigger</param>
    /// <returns>Processed message result</returns>
    Task<MessageProcessingResult> ProcessMigrationStartMessageAsync(QueueMessage queueMessage);

    /// <summary>
    /// Processes an entity batch message received from queue trigger
    /// </summary>
    /// <param name="queueMessage">Queue message from trigger</param>
    /// <returns>Processed message result</returns>
    Task<MessageProcessingResult> ProcessEntityBatchMessageAsync(QueueMessage queueMessage);

    /// <summary>
    /// Processes a cancellation message received from queue trigger
    /// </summary>
    /// <param name="queueMessage">Queue message from trigger</param>
    /// <returns>Processed message result</returns>
    Task<MessageProcessingResult> ProcessCancellationMessageAsync(QueueMessage queueMessage);

    /// <summary>
    /// Processes a batch completion message received from queue trigger
    /// </summary>
    /// <param name="queueMessage">Queue message from trigger</param>
    /// <returns>Processed message result</returns>
    Task<MessageProcessingResult> ProcessBatchCompletionMessageAsync(QueueMessage queueMessage);

    #endregion

    #region Message Validation and Parsing

    /// <summary>
    /// Validates and parses a queue message content
    /// </summary>
    /// <typeparam name="T">Expected message type</typeparam>
    /// <param name="queueMessage">Queue message to parse</param>
    /// <returns>Parsed message content</returns>
    Task<T?> ParseQueueMessageAsync<T>(QueueMessage queueMessage) where T : class;

    /// <summary>
    /// Validates a queue message format and content
    /// </summary>
    /// <param name="queueMessage">Queue message to validate</param>
    /// <returns>Validation result</returns>
    Task<MessageValidationResult> ValidateQueueMessageAsync(QueueMessage queueMessage);

    #endregion

    #region Dead Letter Queue Handling

    /// <summary>
    /// Creates a dead letter message for failed processing
    /// </summary>
    /// <param name="originalMessage">Original failed message</param>
    /// <param name="errorDetails">Error details</param>
    /// <returns>Dead letter message ready for output binding</returns>
    DeadLetterMessage CreateDeadLetterMessage(QueueMessage originalMessage, string errorDetails);

    /// <summary>
    /// Processes a message from the dead letter queue
    /// </summary>
    /// <param name="deadLetterMessage">Dead letter message to process</param>
    /// <returns>Processing result with retry recommendation</returns>
    Task<DeadLetterProcessingResult> ProcessDeadLetterMessageAsync(DeadLetterMessage deadLetterMessage);

    #endregion

    #region Direct Queue Operations (for immediate sending)

    /// <summary>
    /// Sends a message directly to the specified queue with retry logic and performance optimization
    /// </summary>
    /// <param name="queueName">Target queue name</param>
    /// <param name="queueMessage">Message to send</param>
    /// <param name="retryCount">Number of retry attempts (default: 3)</param>
    /// <returns>Task representing the send operation</returns>
    Task SendMessageAsync(string queueName, QueueMessage queueMessage, int retryCount = 3);

    /// <summary>
    /// Sends multiple messages to the same queue in batch for optimal performance
    /// </summary>
    /// <param name="queueName">Target queue name</param>
    /// <param name="queueMessages">Messages to send</param>
    /// <returns>Task representing the batch send operation</returns>
    Task SendMessageBatchAsync(string queueName, IEnumerable<QueueMessage> queueMessages);

    /// <summary>
    /// Sends a migration start message directly to the migration-start queue
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="migrationRequest">Migration request</param>
    /// <param name="categoryTreeContext">Category tree context for the migration</param>
    /// <returns>Task representing the send operation</returns>
    Task SendMigrationStartMessageAsync(string migrationId, MigrationRequest migrationRequest, CategoryTreeContext? categoryTreeContext = null);

    /// <summary>
    /// Sends a cancellation message directly to the cancellation queue
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="reason">Cancellation reason</param>
    /// <returns>Task representing the send operation</returns>
    Task SendCancellationMessageAsync(string migrationId, string reason);

    #endregion

    #region Queue Statistics (Direct Azure SDK when needed)

    /// <summary>
    /// Gets queue statistics for monitoring (uses Azure SDK directly)
    /// </summary>
    /// <param name="queueName">Queue name</param>
    /// <returns>Queue statistics</returns>
    Task<QueueStatistics> GetQueueStatisticsAsync(string queueName);

    /// <summary>
    /// Gets the approximate number of messages in the queue (uses Azure SDK directly)
    /// </summary>
    /// <param name="queueName">Queue name</param>
    /// <returns>Approximate message count</returns>
    Task<int> GetApproximateMessageCountAsync(string queueName);

    #endregion
} 