using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Simplified interface for publishing progress events to Azure Storage Queues for SignalR broadcasting
    /// 
    /// ✅ SIMPLIFIED APPROACH:
    /// - Only 4 event types (vs 11 previously)
    /// - Removed complex batch/status/migration-specific methods
    /// - Generic publish method for all events
    /// </summary>
    public interface IProgressEventPublisher
    {
        /// <summary>
        /// Publishes a migration started event to the queue
        /// </summary>
        /// <param name="startedEvent">The migration started event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishMigrationStartedAsync(MigrationStartedEvent startedEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes an entity started event to the queue
        /// </summary>
        /// <param name="entityStartedEvent">The entity started event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishEntityStartedAsync(EntityStartedEvent entityStartedEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes an entity chunk progress event to the queue
        /// This is the main progress event published during migration
        /// </summary>
        /// <param name="chunkEvent">The chunk progress event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishEntityChunkProgressAsync(EntityChunkProgressEvent chunkEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes an entity completed event to the queue
        /// </summary>
        /// <param name="completedEvent">The entity completed event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishEntityCompletedAsync(EntityCompletedEvent completedEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes a migration completed event to the queue
        /// </summary>
        /// <param name="completedEvent">The migration completed event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishMigrationCompletedAsync(MigrationCompletedEvent completedEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes an error event to the queue
        /// </summary>
        /// <param name="errorEvent">The error event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishErrorAsync(ErrorProgressEvent errorEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes any progress event to the queue (generic method)
        /// Works with all 4 simplified event types
        /// </summary>
        /// <param name="progressEvent">The progress event to publish</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishAsync(ProgressEvent progressEvent, CancellationToken cancellationToken = default);
    }
}