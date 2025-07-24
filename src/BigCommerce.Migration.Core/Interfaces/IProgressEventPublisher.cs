using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Interface for publishing progress events to Azure Storage Queues for SignalR broadcasting
    /// SOLID: Dependency Inversion - abstracts queue publishing implementation
    /// SOLID: Interface Segregation - focused on progress event publishing only
    /// </summary>
    public interface IProgressEventPublisher
    {
        /// <summary>
        /// Publishes a migration progress event to the queue
        /// </summary>
        /// <param name="progressEvent">The progress event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishMigrationProgressAsync(MigrationProgressEvent progressEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes a batch progress event to the queue
        /// </summary>
        /// <param name="batchEvent">The batch progress event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishBatchProgressAsync(BatchProgressEvent batchEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes an entity progress event to the queue
        /// </summary>
        /// <param name="entityEvent">The entity progress event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishEntityProgressAsync(EntityProgressEvent entityEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes an error event to the queue
        /// </summary>
        /// <param name="errorEvent">The error event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishErrorAsync(ErrorProgressEvent errorEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes a status change event to the queue
        /// </summary>
        /// <param name="statusEvent">The status event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishStatusAsync(StatusProgressEvent statusEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes any progress event to the queue (generic method)
        /// </summary>
        /// <param name="progressEvent">The progress event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishAsync(ProgressEvent progressEvent, CancellationToken cancellationToken = default);
    }
} 