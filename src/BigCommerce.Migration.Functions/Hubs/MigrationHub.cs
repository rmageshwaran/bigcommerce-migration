using Microsoft.Extensions.Logging;
using Microsoft.Azure.SignalR.Management;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Functions.Hubs
{
    /// <summary>
    /// SignalR service for real-time migration monitoring and updates
    /// </summary>
    public class MigrationHub
    {
        private readonly ILogger<MigrationHub> _logger;
        private readonly ServiceHubContext _hubContext;
        private static readonly ConcurrentDictionary<string, UserConnection> _connections = new();

        public MigrationHub(ILogger<MigrationHub> logger, ServiceHubContext? hubContext)
        {
            _logger = logger;
            _hubContext = hubContext!; // We'll handle null checks in methods
        }

        /// <summary>
        /// Add connection to migration monitoring group
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="migrationId">Migration ID to monitor</param>
        /// <param name="userId">User ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task JoinMigrationGroup(string connectionId, string migrationId, string userId, CancellationToken cancellationToken = default)
        {
            try
            {
                var groupName = GetMigrationGroupName(migrationId);
                await _hubContext.Groups.AddToGroupAsync(connectionId, groupName, cancellationToken);
                
                // Track connection
                _connections.TryAdd(connectionId, new UserConnection
                {
                    ConnectionId = connectionId,
                    MigrationId = migrationId,
                    UserId = userId,
                    ConnectedAt = DateTime.UtcNow
                });

                _logger.LogInformation("Connection {ConnectionId} joined migration group {MigrationId}", 
                    connectionId, migrationId);

                // Send initial confirmation
                await _hubContext.Clients.Client(connectionId).SendAsync("JoinedMigrationGroup", migrationId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining migration group {MigrationId} for connection {ConnectionId}", 
                    migrationId, connectionId);
                await _hubContext.Clients.Client(connectionId).SendAsync("Error", "Failed to join migration group", cancellationToken);
            }
        }

        /// <summary>
        /// Remove connection from migration monitoring group
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="migrationId">Migration ID to stop monitoring</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task LeaveMigrationGroup(string connectionId, string migrationId, CancellationToken cancellationToken = default)
        {
            try
            {
                var groupName = GetMigrationGroupName(migrationId);
                await _hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);

                _logger.LogInformation("Connection {ConnectionId} left migration group {MigrationId}", 
                    connectionId, migrationId);

                await _hubContext.Clients.Client(connectionId).SendAsync("LeftMigrationGroup", migrationId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving migration group {MigrationId} for connection {ConnectionId}", 
                    migrationId, connectionId);
                await _hubContext.Clients.Client(connectionId).SendAsync("Error", "Failed to leave migration group", cancellationToken);
            }
        }

        /// <summary>
        /// Send migration progress update to specific migration group
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="progress">Progress data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task SendMigrationProgress(string migrationId, object progress, CancellationToken cancellationToken = default)
        {
            if (_hubContext == null)
            {
                _logger.LogWarning("SignalR hub context not available. Progress update for {MigrationId} not sent.", migrationId);
                return;
            }

            try
            {
                var groupName = GetMigrationGroupName(migrationId);
                await _hubContext.Clients.Group(groupName).SendAsync("MigrationProgress", progress, cancellationToken);
                
                _logger.LogDebug("Sent migration progress update for {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending migration progress for {MigrationId}", migrationId);
            }
        }

        /// <summary>
        /// Send migration status update to specific migration group
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <param name="status">Status data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task SendMigrationStatus(string migrationId, object status, CancellationToken cancellationToken = default)
        {
            if (_hubContext == null)
            {
                _logger.LogWarning("SignalR hub context not available. Status update for {MigrationId} not sent.", migrationId);
                return;
            }

            try
            {
                var groupName = GetMigrationGroupName(migrationId);
                await _hubContext.Clients.Group(groupName).SendAsync("MigrationStatus", status, cancellationToken);
                
                _logger.LogDebug("Sent migration status update for {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending migration status for {MigrationId}", migrationId);
            }
        }

        /// <summary>
        /// Send system health update to all connected clients
        /// </summary>
        /// <param name="healthData">System health data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task SendSystemHealth(object healthData, CancellationToken cancellationToken = default)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("SystemHealth", healthData, cancellationToken);
                
                _logger.LogDebug("Sent system health update to all clients");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending system health update");
            }
        }

        /// <summary>
        /// Send error notification to specific connection
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="error">Error message</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task SendError(string connectionId, string error, CancellationToken cancellationToken = default)
        {
            try
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("Error", error, cancellationToken);
                
                _logger.LogDebug("Sent error notification to connection {ConnectionId}", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending error notification to connection {ConnectionId}", connectionId);
            }
        }

        /// <summary>
        /// Notify connection successful
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task NotifyConnected(string connectionId, CancellationToken cancellationToken = default)
        {
            try
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("Connected", connectionId, cancellationToken);
                
                _logger.LogInformation("Client connected: {ConnectionId}", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying connection {ConnectionId}", connectionId);
            }
        }

        /// <summary>
        /// Handle client disconnection
        /// </summary>
        /// <param name="connectionId">Connection ID</param>
        /// <param name="reason">Disconnection reason</param>
        public void HandleDisconnection(string connectionId, string reason)
        {
            try
            {
                _connections.TryRemove(connectionId, out var connection);
                
                _logger.LogInformation("Client disconnected: {ConnectionId}, Reason: {Reason}", 
                    connectionId, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling client disconnection {ConnectionId}", connectionId);
            }
        }

        /// <summary>
        /// Get active connections count
        /// </summary>
        /// <returns>Number of active connections</returns>
        public static int GetActiveConnectionsCount()
        {
            return _connections.Count;
        }

        /// <summary>
        /// Get connections for specific migration
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <returns>Number of connections monitoring this migration</returns>
        public static int GetMigrationConnectionsCount(string migrationId)
        {
            var count = 0;
            foreach (var connection in _connections.Values)
            {
                if (connection.MigrationId == migrationId)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Get migration group name
        /// </summary>
        /// <param name="migrationId">Migration ID</param>
        /// <returns>Group name</returns>
        public static string GetMigrationGroupName(string migrationId)
        {
            return $"migration_{migrationId}";
        }
    }

    /// <summary>
    /// Represents a user connection to the SignalR hub
    /// </summary>
    public class UserConnection
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string MigrationId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }
    }
} 