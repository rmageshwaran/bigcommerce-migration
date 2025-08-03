using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Orchestration.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for completing chunked category migration progress tracking
/// Task 4.1.2: Final SignalR progress event for chunked migration completion
/// Uses centralized SignalREventFactory for consistent event creation
/// </summary>
public class CompleteChunkedMigrationProgressActivity
{
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ISignalREventFactory _signalREventFactory; // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
    private readonly ILogger<CompleteChunkedMigrationProgressActivity> _logger;

    public CompleteChunkedMigrationProgressActivity(
        IProgressEventPublisher progressEventPublisher,
        ISignalREventFactory signalREventFactory,
        ILogger<CompleteChunkedMigrationProgressActivity> logger)
    {
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory)); // 🎯 CENTRALIZED SIGNALR: Store factory reference
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Completes chunked category migration progress tracking with final SignalR event
    /// </summary>
    /// <param name="completeRequest">Chunked migration completion request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("CompleteChunkedMigrationProgressActivity")]
    public async Task CompleteChunkedMigrationProgressAsync(
        [ActivityTrigger] CompleteChunkedMigrationProgressRequest completeRequest, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Validate required properties (will be caught by continue-on-error below)
            if (string.IsNullOrWhiteSpace(completeRequest.MigrationId))
                throw new ArgumentException("MigrationId is required", nameof(completeRequest));
            
            var migrationId = completeRequest.MigrationId;
            
            _logger.LogInformation("🏁 [CHUNKED-COMPLETE] Completing chunked category migration progress for MigrationId: {MigrationId}. " +
                "Success: {Success}, Categories: {TotalCategoriesCreated}/{TotalCategoriesProcessed}, Performance: {PerformanceImprovement:F1}x, " +
                "Time: {ProcessingTimeMinutes:F2}min, Levels: {LevelsProcessed}/{LevelsFailed}", 
                migrationId, completeRequest.Success, completeRequest.TotalCategoriesCreated, completeRequest.TotalCategoriesProcessed, 
                completeRequest.PerformanceImprovement, completeRequest.ProcessingTimeMinutes, 
                completeRequest.LevelsProcessed, completeRequest.LevelsFailed);

            // Determine final status based on success and completion
            var finalStatus = completeRequest.Success 
                ? (completeRequest.LevelsFailed > 0 ? "completed_with_errors" : "completed")
                : "failed";

            // Calculate success rate
            var successRatePercent = completeRequest.TotalCategoriesProcessed > 0 
                ? (double)completeRequest.TotalCategoriesCreated / completeRequest.TotalCategoriesProcessed * 100.0 
                : 0.0;

            // 🎯 CENTRALIZED SIGNALR: Create final migration progress event using factory
            var finalProgressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
            {
                OverallProgress = 100.0, // Migration complete
                Status = finalStatus,
                TotalEntities = completeRequest.TotalCategoriesProcessed,
                ProcessedEntities = completeRequest.TotalCategoriesProcessed,
                FailedEntities = completeRequest.TotalCategoriesProcessed - completeRequest.TotalCategoriesCreated,
                SuccessfulEntities = completeRequest.TotalCategoriesCreated,
                CurrentEntityType = "categories",
                CurrentActivity = "Chunked Category Migration Complete",
                CurrentBatchNumber = completeRequest.LevelsProcessed + completeRequest.LevelsFailed,
                GroupName = $"migration-{migrationId}",
                CurrentBatch = new CurrentBatchDetails
                {
                    BatchNumber = completeRequest.LevelsProcessed + completeRequest.LevelsFailed,
                    BatchSize = 0,
                    ProcessedInBatch = 0,
                    BatchProgressPercentage = 100.0,
                    BatchProcessingSpeed = 0.0,
                    BatchElapsedTime = TimeSpan.FromMinutes(completeRequest.ProcessingTimeMinutes),
                    EstimatedBatchTimeRemaining = TimeSpan.Zero
                }
            });

            // Publish the final progress event
            await _progressEventPublisher.PublishMigrationProgressAsync(finalProgressEvent, cancellationToken);

            // 🎯 COMPLETION-SPECIFIC: Create detailed completion status event
            var completionStatusEvent = _signalREventFactory.CreateStatusProgress(migrationId, new StatusProgressOptions
            {
                Status = $"chunked-migration-{finalStatus}",
                Message = completeRequest.Success 
                    ? $"🎉 Chunked category migration completed! Created {completeRequest.TotalCategoriesCreated} categories with {completeRequest.PerformanceImprovement:F1}x performance improvement"
                    : $"❌ Chunked category migration failed after processing {completeRequest.TotalCategoriesProcessed} categories",
                Data = new Dictionary<string, object>
                {
                    ["Success"] = completeRequest.Success,
                    ["FinalStatus"] = finalStatus,
                    ["TotalCategoriesProcessed"] = completeRequest.TotalCategoriesProcessed,
                    ["TotalCategoriesCreated"] = completeRequest.TotalCategoriesCreated,
                    ["PerformanceImprovement"] = completeRequest.PerformanceImprovement,
                    ["ProcessingTimeMinutes"] = completeRequest.ProcessingTimeMinutes,
                    ["LevelsProcessed"] = completeRequest.LevelsProcessed,
                    ["LevelsFailed"] = completeRequest.LevelsFailed,
                    ["SuccessRatePercent"] = successRatePercent,
                    ["ProcessingStrategy"] = "Level-by-Level Chunked Processing",
                    ["BulkCreationUsed"] = true
                },
                GroupName = $"migration-{migrationId}"
            });

            await _progressEventPublisher.PublishStatusAsync(completionStatusEvent, cancellationToken);

            // 🎯 PERFORMANCE METRICS: Create entity-specific completion event for performance tracking
            var entityCompletionEvent = _signalREventFactory.CreateEntityProgress(migrationId, new EntityProgressOptions
            {
                EntityType = "categories",
                Status = finalStatus,
                TotalCount = completeRequest.TotalCategoriesProcessed,
                ProcessedCount = completeRequest.TotalCategoriesProcessed,
                ProcessingTime = TimeSpan.FromMinutes(completeRequest.ProcessingTimeMinutes),
                GroupName = $"migration-{migrationId}"
            });

            await _progressEventPublisher.PublishEntityProgressAsync(entityCompletionEvent, cancellationToken);

            // 🎯 ERROR REPORTING: If there were failures, create error event
            if (completeRequest.LevelsFailed > 0 || completeRequest.TotalCategoriesCreated < completeRequest.TotalCategoriesProcessed)
            {
                var errorEvent = _signalREventFactory.CreateErrorProgress(migrationId, new ErrorProgressOptions
                {
                    ErrorMessage = $"Chunked migration completed with errors: {completeRequest.LevelsFailed} levels failed, " +
                                  $"{completeRequest.TotalCategoriesProcessed - completeRequest.TotalCategoriesCreated} categories failed",
                    GroupName = $"migration-{migrationId}"
                });

                await _progressEventPublisher.PublishErrorAsync(errorEvent, cancellationToken);
            }
            
            _logger.LogInformation("✅ [CHUNKED-COMPLETE] Successfully published chunked migration completion events for MigrationId: {MigrationId}, Status: {Status}", 
                migrationId, finalStatus);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("🚫 [CHUNKED-COMPLETE] Complete chunked migration progress was cancelled for MigrationId: {MigrationId}", 
                completeRequest.MigrationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHUNKED-COMPLETE] Failed to complete chunked migration progress for MigrationId: {MigrationId}: {ErrorMessage}", 
                completeRequest.MigrationId, ex.Message);
            
            // ✅ CONTINUE-ON-ERROR: Don't throw exception - allow migration to complete despite progress tracking failure
            _logger.LogWarning("🔄 [CHUNKED-COMPLETE] Migration completed despite progress tracking failure (continue-on-error policy)");
        }
    }
}

/// <summary>
/// Request model for completing chunked category migration progress
/// </summary>
public class CompleteChunkedMigrationProgressRequest
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the overall migration was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Total number of categories processed across all levels
    /// </summary>
    public int TotalCategoriesProcessed { get; set; }
    
    /// <summary>
    /// Total number of categories successfully created
    /// </summary>
    public int TotalCategoriesCreated { get; set; }
    
    /// <summary>
    /// Performance improvement factor achieved (e.g., 8.5 = 8.5x faster)
    /// </summary>
    public double PerformanceImprovement { get; set; }
    
    /// <summary>
    /// Total processing time across all levels in minutes
    /// </summary>
    public double ProcessingTimeMinutes { get; set; }
    
    /// <summary>
    /// Number of levels successfully processed
    /// </summary>
    public int LevelsProcessed { get; set; }
    
    /// <summary>
    /// Number of levels that failed processing
    /// </summary>
    public int LevelsFailed { get; set; }
}