using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity for retrieving the latest aggregated progress from the database
/// Part of Task 3.3: Orchestrator Simplification
/// 
/// This activity replaces complex in-memory aggregation in orchestrators by querying
/// the database for real-time incremental progress data that's maintained by Task 3.1.
/// 
/// Benefits:
/// - Single source of truth (database)
/// - Eliminates memory/database inconsistencies  
/// - Automatic cancellation handling (data already persisted)
/// - Simplifies orchestrator logic dramatically
/// </summary>
public class GetLatestAggregatedProgressActivity
{
    private readonly IProgressTracker _progressTracker;
    private readonly ILogger<GetLatestAggregatedProgressActivity> _logger;

    /// <summary>
    /// Initializes the GetLatestAggregatedProgressActivity
    /// </summary>
    /// <param name="progressTracker">Progress tracker for database queries</param>
    /// <param name="logger">Logger for monitoring and debugging</param>
    public GetLatestAggregatedProgressActivity(
        IProgressTracker progressTracker,
        ILogger<GetLatestAggregatedProgressActivity> logger)
    {
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the latest aggregated progress for a migration from the database
    /// This method provides real-time progress data including incremental updates
    /// from chunk processing, eliminating the need for complex orchestrator aggregation
    /// </summary>
    /// <param name="request">Progress request with migration ID and optional entity type filter</param>
    /// <returns>Complete migration progress with real-time data from incremental updates</returns>
    [Function("GetLatestAggregatedProgressActivity")]
    public async Task<MigrationProgress> GetLatestAggregatedProgressAsync(
        [ActivityTrigger] GetProgressRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request), "Progress request is required");
        }

        if (string.IsNullOrEmpty(request.MigrationId))
        {
            throw new ArgumentException("Migration ID is required", nameof(request));
        }

        try
        {
            _logger.LogInformation("🔍 AGGREGATE-PROGRESS: Starting GetLatestAggregatedProgressAsync for migration {MigrationId}, EntityType: {EntityType}",
                request.MigrationId, request.EntityType ?? "All");

            // Get the latest aggregated progress from database
            // This includes real-time incremental data from chunk processing
            _logger.LogInformation("📊 AGGREGATE-PROGRESS: Calling ProgressTracker.GetLatestAggregatedProgressAsync for {MigrationId}",
                request.MigrationId);
                
            var progress = await _progressTracker.GetLatestAggregatedProgressAsync(
                request.MigrationId, CancellationToken.None);

            if (progress == null)
            {
                _logger.LogWarning("⚠️ AGGREGATE-PROGRESS: No progress found for migration {MigrationId}", request.MigrationId);
                
                // Return empty progress instead of null to prevent orchestrator issues
                return new MigrationProgress
                {
                    MigrationId = request.MigrationId,
                    Status = "NotFound",
                    StartTime = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow,
                    TotalEntities = 0,
                    ProcessedEntities = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = 0,
                    SkippedEntities = 0,
                    CancelledEntities = 0,
                    OverallProgressPercentage = 0.0,
                    EntityProgress = new Dictionary<string, EntityProgress>()
                };
            }

            _logger.LogInformation("✅ AGGREGATE-PROGRESS: ProgressTracker returned progress data for {MigrationId} - " +
                "Total: {Total}, Processed: {Processed}, Successful: {Successful}, Failed: {Failed}, Skipped: {Skipped}, Cancelled: {Cancelled}",
                request.MigrationId, progress.TotalEntities, progress.ProcessedEntities,
                progress.SuccessfulEntities, progress.FailedEntities, progress.SkippedEntities, progress.CancelledEntities);

            // Filter by entity type if specified
            if (!string.IsNullOrEmpty(request.EntityType))
            {
                _logger.LogInformation("🔍 AGGREGATE-PROGRESS: Filtering progress for entity type: {EntityType}", request.EntityType);
                
                // Create a filtered view focusing on the specific entity type
                var filteredProgress = CreateEntityTypeFilteredProgress(progress, request.EntityType);
                
                _logger.LogInformation("✅ AGGREGATE-PROGRESS: Retrieved filtered progress for {MigrationId}:{EntityType} - " +
                               "Successful: {Successful}, Failed: {Failed}, Skipped: {Skipped}, Cancelled: {Cancelled}",
                    request.MigrationId, request.EntityType, 
                    filteredProgress.SuccessfulEntities, filteredProgress.FailedEntities, 
                    filteredProgress.SkippedEntities, filteredProgress.CancelledEntities);
                
                return filteredProgress;
            }

            _logger.LogInformation("✅ AGGREGATE-PROGRESS: Retrieved complete progress for {MigrationId} - " +
                           "Total: {Total}, Processed: {Processed}, Successful: {Successful}, Failed: {Failed}, Skipped: {Skipped}, Cancelled: {Cancelled}",
                request.MigrationId, progress.TotalEntities, progress.ProcessedEntities,
                progress.SuccessfulEntities, progress.FailedEntities, progress.SkippedEntities, progress.CancelledEntities);

            return progress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [GET-PROGRESS] Failed to retrieve progress for migration {MigrationId}, EntityType: {EntityType}",
                request.MigrationId, request.EntityType ?? "All");
            throw;
        }
    }

    /// <summary>
    /// Creates a filtered progress view for a specific entity type
    /// </summary>
    /// <param name="fullProgress">Complete migration progress</param>
    /// <param name="entityType">Entity type to filter by</param>
    /// <returns>Progress view filtered to specific entity type</returns>
    private static MigrationProgress CreateEntityTypeFilteredProgress(MigrationProgress fullProgress, string entityType)
    {
        var filteredProgress = new MigrationProgress
        {
            MigrationId = fullProgress.MigrationId,
            Status = fullProgress.Status,
            StartTime = fullProgress.StartTime,
            LastUpdated = fullProgress.LastUpdated,
            ElapsedTime = fullProgress.ElapsedTime,
            EstimatedTimeRemaining = fullProgress.EstimatedTimeRemaining,
            CurrentPhase = fullProgress.CurrentPhase,
            CurrentEntity = entityType,
            EntitiesPerSecond = fullProgress.EntitiesPerSecond,
            ErrorRate = fullProgress.ErrorRate,
            IsCancelled = fullProgress.IsCancelled,
            CancellationReason = fullProgress.CancellationReason,
            CancelledAt = fullProgress.CancelledAt,
            EntityProgress = new Dictionary<string, EntityProgress>()
        };

        // Include only the specified entity type
        if (fullProgress.EntityProgress.ContainsKey(entityType))
        {
            var entityProgress = fullProgress.EntityProgress[entityType];
            filteredProgress.EntityProgress[entityType] = entityProgress;
            
            // Set totals based on this entity type only
            filteredProgress.TotalEntities = entityProgress.TotalCount;
            filteredProgress.ProcessedEntities = entityProgress.ProcessedCount;
            filteredProgress.SuccessfulEntities = entityProgress.SuccessCount;
            filteredProgress.FailedEntities = entityProgress.FailureCount;
            filteredProgress.SkippedEntities = entityProgress.SkippedCount;
            filteredProgress.CancelledEntities = entityProgress.CancelledCount;
            filteredProgress.OverallProgressPercentage = entityProgress.ProgressPercentage;
        }
        else
        {
            // Entity type not found - return zero values
            filteredProgress.TotalEntities = 0;
            filteredProgress.ProcessedEntities = 0;
            filteredProgress.SuccessfulEntities = 0;
            filteredProgress.FailedEntities = 0;
            filteredProgress.SkippedEntities = 0;
            filteredProgress.CancelledEntities = 0;
            filteredProgress.OverallProgressPercentage = 0.0;
        }

        return filteredProgress;
    }
}