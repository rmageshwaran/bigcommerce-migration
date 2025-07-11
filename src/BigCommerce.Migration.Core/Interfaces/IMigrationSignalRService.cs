using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Interface for SignalR service to broadcast real-time migration updates
    /// </summary>
    public interface IMigrationSignalRService
    {
        /// <summary>
        /// Broadcast migration progress update to all connected clients monitoring the migration
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="progress">Progress data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task BroadcastProgressUpdateAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcast migration status update to all connected clients monitoring the migration
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="status">Status information</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task BroadcastStatusUpdateAsync(string migrationId, object status, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcast entity processing start notification
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="entityType">Entity type being processed</param>
        /// <param name="totalCount">Total number of entities</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task BroadcastEntityStartAsync(string migrationId, string entityType, int totalCount, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcast entity processing completion notification
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="entityType">Entity type completed</param>
        /// <param name="results">Processing results</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task BroadcastEntityCompletionAsync(string migrationId, string entityType, object results, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcast batch completion notification
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="entityType">Entity type</param>
        /// <param name="batchNumber">Batch number</param>
        /// <param name="batchResults">Batch processing results</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task BroadcastBatchCompletionAsync(string migrationId, string entityType, int batchNumber, object batchResults, CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcast system health update to all connected clients
        /// </summary>
        /// <param name="healthData">System health data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task BroadcastSystemHealthAsync(object healthData, CancellationToken cancellationToken = default);
    }
} 