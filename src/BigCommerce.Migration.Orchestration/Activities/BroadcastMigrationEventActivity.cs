using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity for broadcasting migration events via SignalR for real-time dashboard updates
/// ✅ ARCHITECTURAL FIX: Fixed to use configurable SignalR service with graceful degradation
/// </summary>
public class BroadcastMigrationEventActivity
{
    private readonly ILogger<BroadcastMigrationEventActivity> _logger;
    private readonly IMigrationSignalRService _signalRService;

    public BroadcastMigrationEventActivity(
        ILogger<BroadcastMigrationEventActivity> logger,
        IMigrationSignalRService signalRService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signalRService = signalRService ?? throw new ArgumentNullException(nameof(signalRService));
    }

    /// <summary>
    /// Broadcast migration started event
    /// ✅ ARCHITECTURAL NOTE: Fixed to use configurable SignalR service
    /// </summary>
    [Function("BroadcastMigrationStarted")]
    public async Task<bool> BroadcastMigrationStartedAsync(
        [ActivityTrigger] BroadcastMigrationEventRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting migration started event for {MigrationId}", request.MigrationId);

            await _signalRService.BroadcastMigrationStartedAsync(
                request.MigrationId, 
                request.Data, 
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast migration started event for {MigrationId}", request.MigrationId);
            return false; // Don't fail the migration for SignalR issues
        }
    }

    /// <summary>
    /// Broadcast migration completed event
    /// ✅ ARCHITECTURAL FIX: Uses configurable SignalR service with graceful degradation
    /// </summary>
    [Function("BroadcastMigrationCompleted")]
    public async Task<bool> BroadcastMigrationCompletedAsync(
        [ActivityTrigger] BroadcastMigrationEventRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting migration completed event for {MigrationId}", request.MigrationId);

            await _signalRService.BroadcastMigrationCompletedAsync(
                request.MigrationId, 
                request.Data, 
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast migration completed event for {MigrationId}", request.MigrationId);
            return false; // Don't fail the migration for SignalR issues
        }
    }

    /// <summary>
    /// Broadcast migration failed event
    /// ✅ ARCHITECTURAL FIX: Uses configurable SignalR service with graceful degradation
    /// </summary>
    [Function("BroadcastMigrationFailed")]
    public async Task<bool> BroadcastMigrationFailedAsync(
        [ActivityTrigger] BroadcastMigrationEventRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting migration failed event for {MigrationId}", request.MigrationId);

            await _signalRService.BroadcastMigrationFailedAsync(
                request.MigrationId, 
                request.Data, 
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast migration failed event for {MigrationId}", request.MigrationId);
            return false; // Don't fail the migration for SignalR issues
        }
    }

    /// <summary>
    /// Broadcast migration cancelled event
    /// ✅ ARCHITECTURAL FIX: Uses configurable SignalR service with graceful degradation
    /// </summary>
    [Function("BroadcastMigrationCancelled")]
    public async Task<bool> BroadcastMigrationCancelledAsync(
        [ActivityTrigger] BroadcastMigrationEventRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting migration cancelled event for {MigrationId}", request.MigrationId);

            await _signalRService.BroadcastMigrationCancelledAsync(
                request.MigrationId, 
                request.Data, 
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast migration cancelled event for {MigrationId}", request.MigrationId);
            return false; // Don't fail the migration for SignalR issues
        }
    }

    /// <summary>
    /// Broadcast entity phase started event
    /// ✅ ARCHITECTURAL FIX: Uses configurable SignalR service with graceful degradation
    /// </summary>
    [Function("BroadcastEntityPhaseStarted")]
    public async Task<bool> BroadcastEntityPhaseStartedAsync(
        [ActivityTrigger] BroadcastEntityEventRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting entity phase started event for {MigrationId}, entity {EntityType}", 
                request.MigrationId, request.EntityType);

            await _signalRService.BroadcastEntityPhaseStartAsync(
                request.MigrationId, 
                request.EntityType,
                request.Data, 
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast entity phase started event for {MigrationId}, entity {EntityType}", 
                request.MigrationId, request.EntityType);
            return false; // Don't fail the migration for SignalR issues
        }
    }

    /// <summary>
    /// Broadcast entity phase completed event
    /// ✅ ARCHITECTURAL FIX: Uses configurable SignalR service with graceful degradation
    /// </summary>
    [Function("BroadcastEntityPhaseCompleted")]
    public async Task<bool> BroadcastEntityPhaseCompletedAsync(
        [ActivityTrigger] BroadcastEntityEventRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting entity phase completed event for {MigrationId}, entity {EntityType}", 
                request.MigrationId, request.EntityType);

            await _signalRService.BroadcastEntityPhaseCompletedAsync(
                request.MigrationId, 
                request.EntityType,
                request.Data, 
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast entity phase completed event for {MigrationId}, entity {EntityType}", 
                request.MigrationId, request.EntityType);
            return false; // Don't fail the migration for SignalR issues
        }
    }

    /// <summary>
    /// Broadcast general migration status update event
    /// ✅ ARCHITECTURAL FIX: Uses configurable SignalR service with graceful degradation
    /// </summary>
    [Function("BroadcastMigrationStatusUpdate")]
    public async Task<bool> BroadcastMigrationStatusUpdateAsync(
        [ActivityTrigger] BroadcastMigrationEventRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting migration status update for {MigrationId}", request.MigrationId);

            await _signalRService.BroadcastStatusUpdateAsync(
                request.MigrationId, 
                request.Data, 
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast migration status update for {MigrationId}", request.MigrationId);
            return false; // Don't fail the migration for SignalR issues
        }
    }
}

/// <summary>
/// Request model for broadcasting migration events
/// </summary>
public class BroadcastMigrationEventRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public object Data { get; set; } = new();
}

/// <summary>
/// Request model for broadcasting entity-specific events
/// </summary>
public class BroadcastEntityEventRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public object Data { get; set; } = new();
} 