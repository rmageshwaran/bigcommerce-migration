using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Functions.Hubs
{
    /// <summary>
    /// Interface for SignalR service for real-time migration monitoring and updates
    /// </summary>
    public interface IMigrationHub
    {
        /// <summary>
        /// Add connection to migration monitoring group
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="migrationId">Migration ID to monitor</param>
        /// <param name="userId">User ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task JoinMigrationGroup(string connectionId, string migrationId, string userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove connection from migration monitoring group
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="migrationId">Migration ID to stop monitoring</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task LeaveMigrationGroup(string connectionId, string migrationId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send migration progress update to specific migration group
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="progress">Progress data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendMigrationProgress(string migrationId, object progress, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send migration status update to specific migration group
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="status">Status data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendMigrationStatus(string migrationId, object status, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send system health update to all clients
        /// </summary>
        /// <param name="healthData">Health data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendSystemHealth(object healthData, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send message to specific migration group
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="eventName">Event name</param>
        /// <param name="data">Data to send</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendToMigrationGroup(string migrationId, string eventName, object data, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send message to all clients
        /// </summary>
        /// <param name="eventName">Event name</param>
        /// <param name="data">Data to send</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendToAllClients(string eventName, object data, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send error message to specific client
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="error">Error message</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendError(string connectionId, string error, CancellationToken cancellationToken = default);

        /// <summary>
        /// Notify client of successful connection
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task NotifyConnected(string connectionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Handle client disconnection
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="reason">Disconnection reason</param>
        void HandleDisconnection(string connectionId, string reason);

        /// <summary>
        /// Get count of active connections for a migration
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <returns>Number of active connections</returns>
        int GetActiveConnectionsCount(string migrationId);

        /// <summary>
        /// Get active connections for a migration
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <returns>Collection of active connections</returns>
        System.Collections.Generic.IEnumerable<UserConnection> GetActiveConnections(string migrationId);
    }
} 