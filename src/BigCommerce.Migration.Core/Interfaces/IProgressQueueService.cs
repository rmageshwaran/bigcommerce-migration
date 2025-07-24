using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Interface for progress event queue operations abstraction
    /// Follows SOLID principles by providing a dependency abstraction for progress queue operations
    /// Separate from IQueueService to avoid conflicts with migration queue operations
    /// </summary>
    public interface IProgressQueueService
    {
        /// <summary>
        /// Sends a JSON message to the specified queue
        /// </summary>
        /// <param name="queueName">Name of the queue to send message to</param>
        /// <param name="jsonMessage">JSON message content to send</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task SendJsonMessageAsync(string queueName, string jsonMessage, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ensures the specified queue exists, creating it if necessary
        /// </summary>
        /// <param name="queueName">Name of the queue to ensure exists</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task EnsureQueueExistsAsync(string queueName, CancellationToken cancellationToken = default);
    }
} 