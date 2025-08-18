using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity function for completing a migration
/// </summary>
public class CompleteMigrationActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly IOpenSearchService _openSearchService;
    private readonly IProgressTracker _progressTracker;
    private readonly ILogger<CompleteMigrationActivity> _logger;

    public CompleteMigrationActivity(
        IMigrationStorageService storageService,
        IOpenSearchService openSearchService,
        IProgressTracker progressTracker,
        ILogger<CompleteMigrationActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Completes the migration process
    /// Enhanced to use real chunk data from chunkincrementevents for accurate final counts
    /// </summary>
    /// <param name="request">Migration completion request</param>
    [Function("CompleteMigration")]
    public async Task CompleteMigrationAsync([ActivityTrigger] CompleteMigrationRequest request)
    {
        try
        {
            _logger.LogInformation("🏁 COMPLETION: Starting migration completion for {MigrationId} with status {Status}", 
                request.MigrationId, request.Result.Status);

            // 🎯 ENHANCEMENT: Get real final counts from chunkincrementevents aggregation
            _logger.LogInformation("📊 COMPLETION: Getting final accurate counts from chunkincrementevents for {MigrationId}", 
                request.MigrationId);
            
            var realProgress = await _progressTracker.GetLatestAggregatedProgressAsync(request.MigrationId, CancellationToken.None);
            
            // Update migration status in storage with REAL final counts
            var migrationEntry = await _storageService.GetMigrationAsync(request.MigrationId);
            if (migrationEntry != null)
            {
                migrationEntry.Status = request.Result.Status;
                migrationEntry.UpdatedAt = DateTime.UtcNow;
                migrationEntry.ErrorMessage = request.Result.Errors.Any() ? string.Join("; ", request.Result.Errors) : null;
                
                // 🚨 CRITICAL ENHANCEMENT: Use real aggregated counts from chunkincrementevents
                if (realProgress != null)
                {
                    _logger.LogInformation("✅ COMPLETION: Using real aggregated counts - " +
                        "Total: {Total}, Processed: {Processed}, Success: {Success}, Failed: {Failed}, Skipped: {Skipped}, Cancelled: {Cancelled}",
                        realProgress.TotalEntities, realProgress.ProcessedEntities, realProgress.SuccessfulEntities, 
                        realProgress.FailedEntities, realProgress.SkippedEntities, realProgress.CancelledEntities);
                        
                    migrationEntry.TotalEntities = realProgress.TotalEntities;
                    migrationEntry.ProcessedEntities = realProgress.ProcessedEntities;
                    migrationEntry.SuccessfulEntities = realProgress.SuccessfulEntities;
                    migrationEntry.FailedEntities = realProgress.FailedEntities;
                    migrationEntry.SkippedEntities = realProgress.SkippedEntities;
                    migrationEntry.CancelledEntities = realProgress.CancelledEntities;
                    migrationEntry.ProgressPercentage = (int)Math.Round(realProgress.OverallProgressPercentage);
                }
                else
                {
                    _logger.LogWarning("⚠️ COMPLETION: No chunkincrementevents data found, attempting to aggregate from entity progress tables");
                    
                    // 🔄 FALLBACK: Aggregate from entity progress when chunkincrementevents is empty
                    try
                    {
                        var entityProgressList = await _storageService.GetEntityProgressAsync(request.MigrationId);
                        if (entityProgressList?.Any() == true)
                        {
                            var aggregatedSuccess = entityProgressList.Sum(ep => ep.SuccessCount);
                            var aggregatedFailed = entityProgressList.Sum(ep => ep.FailureCount);
                            var aggregatedSkipped = entityProgressList.Sum(ep => ep.SkippedCount);
                            var aggregatedCancelled = entityProgressList.Sum(ep => ep.CancelledCount);
                            var aggregatedProcessed = aggregatedSuccess + aggregatedFailed + aggregatedSkipped + aggregatedCancelled;
                            
                            _logger.LogInformation("✅ COMPLETION: Fallback aggregation successful - " +
                                "Processed: {Processed}, Success: {Success}, Failed: {Failed}, Skipped: {Skipped}, Cancelled: {Cancelled}",
                                aggregatedProcessed, aggregatedSuccess, aggregatedFailed, aggregatedSkipped, aggregatedCancelled);
                                
                            migrationEntry.ProcessedEntities = aggregatedProcessed;
                            migrationEntry.SuccessfulEntities = aggregatedSuccess;
                            migrationEntry.FailedEntities = aggregatedFailed;
                            migrationEntry.SkippedEntities = aggregatedSkipped;
                            migrationEntry.CancelledEntities = aggregatedCancelled;
                            migrationEntry.ProgressPercentage = migrationEntry.TotalEntities > 0 
                                ? (int)Math.Round((double)aggregatedProcessed / migrationEntry.TotalEntities * 100) 
                                : 0;
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ COMPLETION: No entity progress data found either, keeping orchestrator results");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ COMPLETION: Failed to aggregate from entity progress, keeping orchestrator results");
                    }
                }
                
                await _storageService.UpdateMigrationAsync(migrationEntry);
            }

            // Log completion event to OpenSearch
            await _openSearchService.LogMigrationEventAsync(
                "migration_completed",
                request.MigrationId,
                new
                {
                    Status = request.Result.Status.ToString(),
                    Duration = request.Result.Duration,
                    EntityResults = request.Result.EntityResults,
                    Statistics = request.Result.Statistics,
                    ErrorCount = request.Result.Errors.Count
                });

            _logger.LogInformation("Migration {MigrationId} completed successfully with status {Status}", 
                request.MigrationId, request.Result.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete migration {MigrationId}", request.MigrationId);
            throw;
        }
    }
}

/// <summary>
/// Request model for migration completion
/// </summary>
public class CompleteMigrationRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public MigrationResult Result { get; set; } = new();
} 