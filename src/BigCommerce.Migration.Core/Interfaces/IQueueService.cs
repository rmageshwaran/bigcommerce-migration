using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Interface for Azure Queue Storage operations
    /// Provides message creation, processing, validation, and monitoring capabilities
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
        /// Creates a batch completion message for queue output binding
        /// </summary>
        /// <param name="batchCompletionMessage">Batch completion details</param>
        /// <returns>Queue message ready for output binding</returns>
        QueueMessage CreateBatchCompletionMessage(BatchCompletionMessage batchCompletionMessage);

        /// <summary>
        /// Creates a dead letter message for failed message processing
        /// </summary>
        /// <param name="originalMessage">Original message that failed</param>
        /// <param name="errorReason">Reason for the failure</param>
        /// <returns>Dead letter queue message</returns>
        DeadLetterMessage CreateDeadLetterMessage(QueueMessage originalMessage, string errorReason);

        #endregion

        #region Message Processing (for Queue Triggers)

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

        #region Direct Queue Operations (when needed)

        /// <summary>
        /// Sends a message directly to the specified queue with retry logic
        /// </summary>
        /// <param name="queueName">Target queue name</param>
        /// <param name="queueMessage">Message to send</param>
        /// <param name="retryCount">Number of retry attempts</param>
        /// <returns>Task representing the send operation</returns>
        Task SendMessageAsync(string queueName, QueueMessage queueMessage, int retryCount = 3);

        /// <summary>
        /// Sends multiple messages to the same queue in batch
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



        #endregion

        #region Queue Statistics

        /// <summary>
        /// Gets queue statistics for monitoring
        /// </summary>
        /// <param name="queueName">Queue name</param>
        /// <returns>Queue statistics</returns>
        Task<QueueStatistics> GetQueueStatisticsAsync(string queueName);

        /// <summary>
        /// Gets the approximate number of messages in the queue
        /// </summary>
        /// <param name="queueName">Queue name</param>
        /// <returns>Approximate message count</returns>
        Task<int> GetApproximateMessageCountAsync(string queueName);

        #endregion
    }
} 