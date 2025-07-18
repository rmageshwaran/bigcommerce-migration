namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Abstraction for Queue Storage operations to enforce Dependency Inversion Principle
/// Provides testable interface without direct dependency on any queue implementation
/// </summary>
public interface IQueueStorageClient
{
    /// <summary>
    /// Gets a queue client for the specified queue
    /// </summary>
    /// <param name="queueName">Name of the queue</param>
    /// <returns>Queue client abstraction</returns>
    IQueueClient GetQueueClient(string queueName);
}

/// <summary>
/// Abstraction for Queue Client operations to enforce Dependency Inversion Principle
/// </summary>
public interface IQueueClient
{
    /// <summary>
    /// Creates a queue if it does not exist
    /// </summary>
    /// <param name="metadata">Optional metadata for the queue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation with success indicator</returns>
    Task<bool> CreateIfNotExistsAsync(IDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends a message to the queue
    /// </summary>
    /// <param name="messageText">Message content</param>
    /// <param name="visibilityTimeout">Time before message becomes visible</param>
    /// <param name="timeToLive">Time message can remain in queue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message ID of sent message</returns>
    Task<string> SendMessageAsync(string messageText, TimeSpan? visibilityTimeout = null, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends a message to the queue with binary content
    /// </summary>
    /// <param name="messageBody">Binary message content</param>
    /// <param name="visibilityTimeout">Time before message becomes visible</param>
    /// <param name="timeToLive">Time message can remain in queue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message ID of sent message</returns>
    Task<string> SendMessageAsync(byte[] messageBody, TimeSpan? visibilityTimeout = null, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Receives messages from the queue
    /// </summary>
    /// <param name="maxMessages">Maximum number of messages to receive</param>
    /// <param name="visibilityTimeout">Time before messages become visible again</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of received messages</returns>
    Task<IEnumerable<IQueueMessage>> ReceiveMessagesAsync(int? maxMessages = null, TimeSpan? visibilityTimeout = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a message from the queue
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="popReceipt">Pop receipt from message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation with success indicator</returns>
    Task<bool> DeleteMessageAsync(string messageId, string popReceipt, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates the visibility timeout of a message
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="popReceipt">Pop receipt from message</param>
    /// <param name="visibilityTimeout">New visibility timeout</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated pop receipt</returns>
    Task<string> UpdateMessageAsync(string messageId, string popReceipt, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the approximate number of messages in the queue
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Approximate message count</returns>
    Task<int> GetApproximateMessageCountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Generic abstraction for queue messages without implementation dependencies
/// </summary>
public interface IQueueMessage
{
    /// <summary>
    /// Gets the message ID
    /// </summary>
    string MessageId { get; }
    
    /// <summary>
    /// Gets the pop receipt
    /// </summary>
    string PopReceipt { get; }
    
    /// <summary>
    /// Gets the message text
    /// </summary>
    string MessageText { get; }
    
    /// <summary>
    /// Gets the message body as binary data
    /// </summary>
    byte[] MessageBody { get; }
    
    /// <summary>
    /// Gets the dequeue count
    /// </summary>
    int DequeueCount { get; }
    
    /// <summary>
    /// Gets the time the message was inserted
    /// </summary>
    DateTimeOffset InsertedOn { get; }
    
    /// <summary>
    /// Gets the time the message expires
    /// </summary>
    DateTimeOffset ExpiresOn { get; }
    
    /// <summary>
    /// Gets the time the message becomes visible next
    /// </summary>
    DateTimeOffset NextVisibleOn { get; }
} 