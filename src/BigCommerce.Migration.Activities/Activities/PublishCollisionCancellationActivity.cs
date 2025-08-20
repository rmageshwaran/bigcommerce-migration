using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity for publishing collision cancellation events via native cancellation system
/// Phase 2.3.2: Integrates collision detection with blob store + SignalR notifications
/// </summary>
public class PublishCollisionCancellationActivity
{
    private readonly ICancellationStore _cancellationStore;
    private readonly ISignalREventFactory _signalREventFactory;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ILogger<PublishCollisionCancellationActivity> _logger;

    public PublishCollisionCancellationActivity(
        ICancellationStore cancellationStore,
        ISignalREventFactory signalREventFactory,
        IProgressEventPublisher progressEventPublisher,
        ILogger<PublishCollisionCancellationActivity> logger)
    {
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Publishes collision cancellation using native cancellation system
    /// Sets blob flag and sends SignalR notification
    /// </summary>
    /// <param name="request">Collision cancellation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    [Function("PublishCollisionCancellation")]
    public async Task PublishCollisionCancellationAsync(
        [ActivityTrigger] CollisionCancellationNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("🔒 [COLLISION-CANCEL] Publishing collision cancellation for migration {MigrationId}: {Reason}", 
                request.MigrationId, request.Reason);

            // Step 1: Set cancellation flag in blob storage (cooperative cancellation)
            await _cancellationStore.SetCancellationFlagAsync(request.MigrationId, request.Reason);
            _logger.LogInformation("🔒 [COLLISION-CANCEL] Cancellation flag set in blob storage for migration {MigrationId}", request.MigrationId);

            // Step 2: Send SignalR notification about collision cancellation
            var errorEvent = _signalREventFactory.CreateErrorProgress(request.MigrationId, new ErrorProgressOptions
            {
                ErrorMessage = $"Migration cancelled due to orchestrator collision: {request.Reason}",
                Severity = "critical",
                IsCancelled = true,
                CancellationReason = request.Reason,
                CancelledAt = request.CancelledAt,
                IsContinuable = false // Collision cancellation stops the migration
            });

            await _progressEventPublisher.PublishErrorAsync(errorEvent);
            _logger.LogInformation("🔒 [COLLISION-CANCEL] SignalR collision cancellation notification sent for migration {MigrationId}", request.MigrationId);

        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Collision cancellation publishing was cancelled for migration {MigrationId}", request.MigrationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ERROR: Failed to publish collision cancellation for migration {MigrationId}", request.MigrationId);
            // Don't rethrow - collision cancellation should not fail the orchestrator
            // The orchestrator will still return the proper result
        }
    }
}

/// <summary>
/// Request model for collision cancellation notification
/// </summary>
public class CollisionCancellationNotificationRequest
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Instance identifier that detected the collision
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Collision/cancellation reason
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Cancellation timestamp
    /// </summary>
    public DateTime CancelledAt { get; set; }
}