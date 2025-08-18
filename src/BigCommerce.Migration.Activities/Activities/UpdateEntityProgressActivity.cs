using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Activities.Models;

namespace BigCommerce.Migration.Activities.Activities;

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

            // 🚨 CRITICAL FIX: Don't skip cancelled updates - we need to process them to set correct status
            // Only skip if this is a regular progress update for a cancelled migration (not the final status update)
            if (progressUpdate.IsCancelled && !progressUpdate.Phase.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🛑 [SOFT-CANCEL] Skipping progress update for cancelled migration {MigrationId}, EntityType: {EntityType}. Reason: {Reason}", 
                    migrationId, entityType, progressUpdate.CancellationReason ?? "Unknown");
                return;
            }
            
            // 🚫 CANCELLATION PROCESSING: Allow cancelled phase updates to proceed
            if (progressUpdate.IsCancelled && progressUpdate.Phase.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🚫 [CANCELLATION-UPDATE] Processing final cancellation status update for {EntityType} in migration {MigrationId}", 
                    entityType, migrationId);
            }
            
            _logger.LogInformation("🔄 [UPDATE-PROGRESS] Starting progress update for {EntityType} in migration {MigrationId}: Phase={Phase}, Processed={ProcessedEntities}/{TotalEntities}, Success={SuccessfulEntities}, Failed={FailedEntities}, Skipped={SkippedEntities}", 
                entityType, migrationId, progressUpdate.Phase, progressUpdate.ProcessedEntities, progressUpdate.TotalEntities, progressUpdate.SuccessfulEntities, progressUpdate.FailedEntities, progressUpdate.SkippedEntities);

            // Create progress update object
            var update = new ProgressUpdate
            {
                MigrationId = migrationId,
                EntityType = entityType,
                Phase = progressUpdate.Phase,
                ProcessedCount = progressUpdate.ProcessedEntities,
                SuccessCount = progressUpdate.SuccessfulEntities,
                FailureCount = progressUpdate.FailedEntities,
                SkippedCount = progressUpdate.SkippedEntities,
                CancelledCount = progressUpdate.CancelledEntities,  // 🚨 CANCELLATION FIX: Include CancelledCount
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
            
            // If this is a completion phase, also mark the entity as completed with real final counts
            if (progressUpdate.Phase.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🏁 [UPDATE-PROGRESS] Marking {EntityType} as completed for migration {MigrationId}", entityType, migrationId);
                
                // 🎯 ENHANCEMENT: Get real final counts from chunkincrementevents for entity completion
                _logger.LogInformation("📊 [ENTITY-COMPLETION] Getting real final counts from chunkincrementevents for {MigrationId}:{EntityType}", 
                    migrationId, entityType);
                
                var realProgress = await _progressTracker.GetLatestAggregatedProgressAsync(migrationId, cancellationToken);
                
                // Update with real entity-specific counts if available
                if (realProgress?.EntityProgress.ContainsKey(entityType) == true)
                {
                    var realEntityProgress = realProgress.EntityProgress[entityType];
                    _logger.LogInformation("✅ [ENTITY-COMPLETION] Using real aggregated counts for {EntityType} - " +
                        "Success: {Success}, Failed: {Failed}, Skipped: {Skipped}, Cancelled: {Cancelled}",
                        entityType, realEntityProgress.SuccessCount, realEntityProgress.FailureCount, 
                        realEntityProgress.SkippedCount, realEntityProgress.CancelledCount);
                    
                    // Create enhanced update with real counts
                    var enhancedUpdate = new ProgressUpdate
                    {
                        MigrationId = migrationId,
                        EntityType = entityType,
                        Phase = "Completed",
                        ProcessedCount = realEntityProgress.ProcessedCount,
                        SuccessCount = realEntityProgress.SuccessCount,
                        FailureCount = realEntityProgress.FailureCount,
                        SkippedCount = realEntityProgress.SkippedCount,
                        CancelledCount = realEntityProgress.CancelledCount,
                        CurrentBatch = progressUpdate.CurrentBatch,
                        TotalBatches = progressUpdate.TotalBatches,
                        StatusMessage = $"Completed {entityType}: {realEntityProgress.ProcessedCount} entities processed",
                        Timestamp = progressUpdate.Timestamp,
                        IsCancelled = progressUpdate.IsCancelled,
                        CancellationReason = progressUpdate.CancellationReason,
                        CancelledAt = progressUpdate.CancelledAt
                    };
                    
                    // Update with enhanced real counts
                    await _progressTracker.UpdateProgressAsync(migrationId, enhancedUpdate, cancellationToken);
                }
                
                await _progressTracker.CompleteEntityProcessingAsync(migrationId, entityType, cancellationToken);
            }
            // 🚫 CANCELLATION FIX: If this is a cancellation phase, also mark the entity as completed (with cancelled status)
            else if (progressUpdate.Phase.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🚫 [UPDATE-PROGRESS] Marking {EntityType} as cancelled for migration {MigrationId}", entityType, migrationId);
                
                // 🎯 ENHANCEMENT: Get real final counts from chunkincrementevents for entity cancellation
                _logger.LogInformation("📊 [ENTITY-CANCELLATION] Getting real final counts from chunkincrementevents for {MigrationId}:{EntityType}", 
                    migrationId, entityType);
                
                var realProgress = await _progressTracker.GetLatestAggregatedProgressAsync(migrationId, cancellationToken);
                
                // Update with real entity-specific counts if available
                if (realProgress?.EntityProgress.ContainsKey(entityType) == true)
                {
                    var realEntityProgress = realProgress.EntityProgress[entityType];
                    _logger.LogInformation("✅ [ENTITY-CANCELLATION] Using real aggregated counts for cancelled {EntityType} - " +
                        "Success: {Success}, Failed: {Failed}, Skipped: {Skipped}, Cancelled: {Cancelled}",
                        entityType, realEntityProgress.SuccessCount, realEntityProgress.FailureCount, 
                        realEntityProgress.SkippedCount, realEntityProgress.CancelledCount);
                    
                    // Create enhanced update with real counts
                    var enhancedUpdate = new ProgressUpdate
                    {
                        MigrationId = migrationId,
                        EntityType = entityType,
                        Phase = "Cancelled",
                        ProcessedCount = realEntityProgress.ProcessedCount,
                        SuccessCount = realEntityProgress.SuccessCount,
                        FailureCount = realEntityProgress.FailureCount,
                        SkippedCount = realEntityProgress.SkippedCount,
                        CancelledCount = realEntityProgress.CancelledCount,
                        CurrentBatch = progressUpdate.CurrentBatch,
                        TotalBatches = progressUpdate.TotalBatches,
                        StatusMessage = $"Cancelled {entityType}: {realEntityProgress.ProcessedCount} entities processed before cancellation",
                        Timestamp = progressUpdate.Timestamp,
                        IsCancelled = true,
                        CancellationReason = progressUpdate.CancellationReason,
                        CancelledAt = progressUpdate.CancelledAt
                    };
                    
                    // Update with enhanced real counts
                    await _progressTracker.UpdateProgressAsync(migrationId, enhancedUpdate, cancellationToken);
                }
                
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