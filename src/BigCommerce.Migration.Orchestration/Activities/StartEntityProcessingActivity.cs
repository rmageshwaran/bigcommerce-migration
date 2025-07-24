using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for starting entity processing and initializing progress tracking
/// </summary>
public class StartEntityProcessingActivity
{
    private readonly IProgressTracker _progressTracker;
    private readonly ILogger<StartEntityProcessingActivity> _logger;

    public StartEntityProcessingActivity(
        IProgressTracker progressTracker,
        ILogger<StartEntityProcessingActivity> logger)
    {
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts entity processing and initializes progress tracking
    /// Phase 4.1: Enhanced with soft cancellation check from request parameter (no storage calls)
    /// </summary>
    /// <param name="request">Start entity processing request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("StartEntityProcessingActivity")]
    public async Task StartEntityProcessingAsync([ActivityTrigger] StartEntityProcessingRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var migrationId = request.MigrationId;
            var entityType = request.EntityType;
            var totalCount = request.TotalCount;

            // Phase 4.1: Check soft cancellation token from request (no storage calls)
            if (request.IsCancelled)
            {
                _logger.LogInformation("🛑 [SOFT-CANCEL] Skipping entity processing start for cancelled migration {MigrationId}, EntityType: {EntityType}. Reason: {Reason}", 
                    migrationId, entityType, request.CancellationReason ?? "Unknown");
                return;
            }
            
            _logger.LogInformation("Starting {EntityType} processing for migration {MigrationId}: {TotalCount} entities", 
                entityType, migrationId, totalCount);

            // Initialize progress tracking for this entity type
            await _progressTracker.StartEntityProcessingAsync(migrationId, entityType, totalCount, cancellationToken);
            
            _logger.LogInformation("Successfully started {EntityType} processing for migration {MigrationId}", 
                entityType, migrationId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("StartEntityProcessingAsync was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start entity processing for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            throw;
        }
    }
} 