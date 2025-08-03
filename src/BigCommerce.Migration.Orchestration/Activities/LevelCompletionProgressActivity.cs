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
/// Activity function for updating progress when each hierarchy level completes
/// Task 4.1.2: Real-time level-by-level progress updates for chunked category migration
/// Uses centralized SignalREventFactory for consistent event creation
/// </summary>
public class LevelCompletionProgressActivity
{
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ISignalREventFactory _signalREventFactory; // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
    private readonly ILogger<LevelCompletionProgressActivity> _logger;

    public LevelCompletionProgressActivity(
        IProgressEventPublisher progressEventPublisher,
        ISignalREventFactory signalREventFactory,
        ILogger<LevelCompletionProgressActivity> logger)
    {
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory)); // 🎯 CENTRALIZED SIGNALR: Store factory reference
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Updates progress when a hierarchy level completes processing
    /// </summary>
    /// <param name="levelRequest">Level completion progress request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("LevelCompletionProgressActivity")]
    public async Task UpdateLevelCompletionProgressAsync(
        [ActivityTrigger] LevelCompletionProgressRequest levelRequest, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Validate required properties (will be caught by continue-on-error below)
            if (string.IsNullOrWhiteSpace(levelRequest.MigrationId))
                throw new ArgumentException("MigrationId is required", nameof(levelRequest));
            
            var migrationId = levelRequest.MigrationId;
            var level = levelRequest.Level;
            
            _logger.LogInformation("📊 [LEVEL-PROGRESS] Updating level completion progress for MigrationId: {MigrationId}, Level: {Level}. " +
                "Success: {Success}, Categories: {CategoriesProcessed}, Time: {ProcessingTimeMinutes}min", 
                migrationId, level, levelRequest.Success, levelRequest.CategoriesProcessed, levelRequest.ProcessingTimeMinutes);

            // Calculate overall progress percentage
            var overallProgress = levelRequest.TotalLevels > 0 
                ? (double)levelRequest.CompletedLevels / levelRequest.TotalLevels * 100.0 
                : 0.0;

            // 🎯 CENTRALIZED SIGNALR: Create migration progress event using factory
            var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
            {
                OverallProgress = overallProgress,
                Status = "processing",
                TotalEntities = levelRequest.TotalCategories ?? 0,
                ProcessedEntities = levelRequest.CumulativeCategoriesProcessed,
                FailedEntities = levelRequest.CumulativeCategoriesFailed,
                SuccessfulEntities = levelRequest.CumulativeCategoriesProcessed - levelRequest.CumulativeCategoriesFailed,
                CurrentEntityType = "categories",
                CurrentActivity = $"Processing Level {level} Categories",
                CurrentBatchNumber = level + 1, // Display as 1-based for user
                GroupName = $"migration-{migrationId}",
                CurrentBatch = new CurrentBatchDetails
                {
                    BatchNumber = level + 1, // Display as 1-based
                    BatchSize = levelRequest.CategoriesProcessed,
                    ProcessedInBatch = levelRequest.CategoriesProcessed,
                    BatchProgressPercentage = levelRequest.Success ? 100.0 : 0.0,
                    BatchProcessingSpeed = levelRequest.ProcessingTimeMinutes > 0 ? levelRequest.CategoriesProcessed / (levelRequest.ProcessingTimeMinutes * 60) : 0.0,
                    BatchElapsedTime = TimeSpan.FromMinutes(levelRequest.ProcessingTimeMinutes),
                    EstimatedBatchTimeRemaining = TimeSpan.Zero
                }
            });

            // Publish the main progress event
            await _progressEventPublisher.PublishMigrationProgressAsync(progressEvent, cancellationToken);

            // 🎯 LEVEL-SPECIFIC: Create detailed level completion event
            var levelStatusEvent = _signalREventFactory.CreateStatusProgress(migrationId, new StatusProgressOptions
            {
                Status = levelRequest.Success ? "level-completed" : "level-failed",
                Message = levelRequest.Success 
                    ? $"✅ Level {level} completed: {levelRequest.CategoriesProcessed} categories processed in {levelRequest.ProcessingTimeMinutes:F2} minutes"
                    : $"❌ Level {level} failed: processed {levelRequest.CategoriesProcessed} categories",
                Data = new Dictionary<string, object>
                {
                    ["Level"] = level,
                    ["Success"] = levelRequest.Success,
                    ["CategoriesProcessed"] = levelRequest.CategoriesProcessed,
                    ["ProcessingTimeMinutes"] = levelRequest.ProcessingTimeMinutes,
                    ["CompletedLevels"] = levelRequest.CompletedLevels,
                    ["TotalLevels"] = levelRequest.TotalLevels,
                    ["OverallProgressPercent"] = overallProgress,
                    ["CumulativeCategoriesProcessed"] = levelRequest.CumulativeCategoriesProcessed,
                    ["CumulativeCategoriesFailed"] = levelRequest.CumulativeCategoriesFailed
                },
                GroupName = $"migration-{migrationId}"
            });

            await _progressEventPublisher.PublishStatusAsync(levelStatusEvent, cancellationToken);

            // 🎯 BATCH PROGRESS: Create batch-specific progress event for this level
            var batchProgressEvent = _signalREventFactory.CreateBatchProgress(migrationId, new BatchProgressOptions
            {
                EntityType = "categories",
                BatchNumber = level + 1, // Display as 1-based
                TotalBatches = levelRequest.TotalLevels,
                BatchSize = levelRequest.CategoriesProcessed,
                ProcessedCount = levelRequest.CategoriesProcessed,
                FailedCount = levelRequest.Success ? 0 : levelRequest.CategoriesProcessed,
                Status = levelRequest.Success ? "completed" : "failed",
                GroupName = $"migration-{migrationId}"
            });

            await _progressEventPublisher.PublishBatchProgressAsync(batchProgressEvent, cancellationToken);
            
            _logger.LogInformation("✅ [LEVEL-PROGRESS] Successfully published level completion events for MigrationId: {MigrationId}, Level: {Level}", 
                migrationId, level);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("🚫 [LEVEL-PROGRESS] Level completion progress was cancelled for MigrationId: {MigrationId}, Level: {Level}", 
                levelRequest.MigrationId, levelRequest.Level);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [LEVEL-PROGRESS] Failed to update level completion progress for MigrationId: {MigrationId}, Level: {Level}: {ErrorMessage}", 
                levelRequest.MigrationId, levelRequest.Level, ex.Message);
            _logger.LogWarning("🔄 [LEVEL-PROGRESS] Continuing migration despite progress tracking failure (continue-on-error policy)");
            // Continue-on-error: Don't throw, just log and continue
        }
    }
}

/// <summary>
/// Request model for level completion progress updates
/// </summary>
public class LevelCompletionProgressRequest
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Hierarchy level that completed (0-based)
    /// </summary>
    public int Level { get; set; }
    
    /// <summary>
    /// Whether the level completed successfully
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Number of categories processed in this level
    /// </summary>
    public int CategoriesProcessed { get; set; }
    
    /// <summary>
    /// Processing time for this level in minutes
    /// </summary>
    public double ProcessingTimeMinutes { get; set; }
    
    /// <summary>
    /// Number of levels completed so far (including this one)
    /// </summary>
    public int CompletedLevels { get; set; }
    
    /// <summary>
    /// Total number of levels in the migration
    /// </summary>
    public int TotalLevels { get; set; }
    
    /// <summary>
    /// Cumulative categories processed across all levels so far
    /// </summary>
    public int CumulativeCategoriesProcessed { get; set; }
    
    /// <summary>
    /// Cumulative categories failed across all levels so far
    /// </summary>
    public int CumulativeCategoriesFailed { get; set; }
    
    /// <summary>
    /// Total categories in the entire migration (optional)
    /// </summary>
    public int? TotalCategories { get; set; }
}