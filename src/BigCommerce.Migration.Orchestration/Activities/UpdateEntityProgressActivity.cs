using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for updating entity migration progress
/// </summary>
public class UpdateEntityProgressActivity
{
    private readonly IProgressTracker _progressTracker;
    private readonly ILogger<UpdateEntityProgressActivity> _logger;

    public UpdateEntityProgressActivity(
        IProgressTracker progressTracker,
        ILogger<UpdateEntityProgressActivity> logger)
    {
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Updates entity migration progress
    /// Phase 4.1: Enhanced with soft cancellation check from request parameter (no storage calls)
    /// </summary>
    /// <param name="progressUpdate">Progress update data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("UpdateEntityProgressActivity")]
    public async Task UpdateEntityProgressAsync([ActivityTrigger] UpdateEntityProgressRequest progressUpdate, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var migrationId = progressUpdate.MigrationId;
            var entityType = progressUpdate.EntityType;

            // Phase 4.1: Check soft cancellation token from request (no storage calls)
            if (progressUpdate.IsCancelled)
            {
                _logger.LogInformation("🛑 [SOFT-CANCEL] Skipping progress update for cancelled migration {MigrationId}, EntityType: {EntityType}. Reason: {Reason}", 
                    migrationId, entityType, progressUpdate.CancellationReason ?? "Unknown");
                return;
            }
            
            _logger.LogInformation("🔄 [UPDATE-PROGRESS] Starting progress update for {EntityType} in migration {MigrationId}: Phase={Phase}, Processed={ProcessedEntities}/{TotalEntities}, Success={SuccessfulEntities}, Failed={FailedEntities}", 
                entityType, migrationId, progressUpdate.Phase, progressUpdate.ProcessedEntities, progressUpdate.TotalEntities, progressUpdate.SuccessfulEntities, progressUpdate.FailedEntities);

            // Create progress update object
            var update = new ProgressUpdate
            {
                MigrationId = migrationId,
                EntityType = entityType,
                Phase = progressUpdate.Phase,
                ProcessedCount = progressUpdate.ProcessedEntities,
                SuccessCount = progressUpdate.SuccessfulEntities,
                FailureCount = progressUpdate.FailedEntities,
                CurrentBatch = progressUpdate.CurrentBatch,
                TotalBatches = progressUpdate.TotalBatches,
                StatusMessage = $"Processing {entityType}: {progressUpdate.ProcessedEntities}/{progressUpdate.TotalEntities} entities",
                Timestamp = progressUpdate.Timestamp, // Phase 4.1: Use deterministic timestamp from orchestrator
                // Phase 4.2: Pass soft cancellation state to ProgressTracker for SignalR filtering
                IsCancelled = progressUpdate.IsCancelled,
                CancellationReason = progressUpdate.CancellationReason,
                CancelledAt = progressUpdate.CancelledAt
            };

            _logger.LogInformation("📈 [UPDATE-PROGRESS] Calling ProgressTracker.UpdateProgressAsync for migration {MigrationId}", migrationId);
            
            // Update progress using the progress tracker
            await _progressTracker.UpdateProgressAsync(migrationId, update, cancellationToken);
            
            _logger.LogInformation("✅ [UPDATE-PROGRESS] Successfully updated progress for migration {MigrationId}", migrationId);
            
            // If this is a completion phase, also mark the entity as completed
            if (progressUpdate.Phase.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🏁 [UPDATE-PROGRESS] Marking {EntityType} as completed for migration {MigrationId}", entityType, migrationId);
                await _progressTracker.CompleteEntityProcessingAsync(migrationId, entityType, cancellationToken);
            }
            
            _logger.LogDebug("Progress updated successfully for {EntityType} in migration {MigrationId}", entityType, migrationId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Progress update was cancelled");
            // Don't rethrow - progress updates are not critical
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update progress");
            // Don't rethrow - progress updates are not critical
        }
    }
} 