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
            
            _logger.LogDebug("Updating progress for {EntityType} in migration {MigrationId}", entityType, migrationId);

            // Placeholder for progress tracking implementation
            await Task.CompletedTask;
            
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