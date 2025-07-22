using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Enhanced progress tracking activity functions for detailed real-time updates
/// Note: SignalR calls are moved to orchestrator level to prevent replay issues
/// </summary>
public class EnhancedProgressActivities
{
    private readonly ILogger<EnhancedProgressActivities> _logger;
    private readonly IProgressTracker _progressTracker;

    public EnhancedProgressActivities(
        ILogger<EnhancedProgressActivities> logger,
        IProgressTracker progressTracker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));
    }

    /// <summary>
    /// Initializes enhanced batch tracking for an entity
    /// Note: SignalR calls moved to orchestrator to prevent replay issues
    /// </summary>
    /// <param name="request">Initialize tracking request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("InitializeEntityBatchTracking")]
    public async Task InitializeEntityBatchTrackingAsync(
        [ActivityTrigger] InitializeEntityBatchTrackingRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Check cancellation token
        cancellationToken.ThrowIfCancellationRequested();

        // Validate request parameters
        if (string.IsNullOrWhiteSpace(request.MigrationId))
            throw new ArgumentException("Migration ID cannot be null, empty, or whitespace.", nameof(request.MigrationId));

        try
        {
            _logger.LogDebug("Initializing enhanced batch tracking for {EntityType} in migration {MigrationId}: {TotalBatches} batches, {TotalEntities} entities",
                request.EntityType, request.MigrationId, request.TotalBatches, request.TotalEntities);

            // Initialize entity processing with enhanced tracking
            await _progressTracker.StartEntityProcessingAsync(
                request.MigrationId, 
                request.EntityType, 
                request.TotalEntities, 
                cancellationToken);

            _logger.LogInformation("Successfully initialized enhanced batch tracking for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize enhanced batch tracking for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Starts batch tracking for a specific batch
    /// Note: SignalR calls moved to orchestrator to prevent replay issues
    /// </summary>
    /// <param name="request">Start batch tracking request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("StartBatchTracking")]
    public async Task StartBatchTrackingAsync(
        [ActivityTrigger] StartBatchTrackingRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Validate request parameters
        if (request.BatchNumber <= 0)
            throw new ArgumentException("Batch number must be greater than 0.", nameof(request.BatchNumber));

        try
        {
            _logger.LogDebug("Starting batch tracking for {EntityType} batch {BatchNumber}/{TotalBatches} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.TotalBatches, request.MigrationId);

            // Update progress tracker with batch start
            await _progressTracker.RecordBatchCompletionAsync(
                request.MigrationId,
                request.EntityType,
                request.BatchNumber,
                0, // processed
                0, // successful
                0, // failed
                cancellationToken);

            _logger.LogInformation("Successfully started batch tracking for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start batch tracking for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Completes batch tracking with results
    /// Note: SignalR calls moved to orchestrator to prevent replay issues
    /// </summary>
    /// <param name="request">Complete batch tracking request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("CompleteBatchTracking")]
    public async Task CompleteBatchTrackingAsync(
        [ActivityTrigger] CompleteBatchTrackingRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Validate request parameters
        if (request.SuccessfulEntities + request.FailedEntities > request.EntitiesProcessed)
            throw new ArgumentException("Sum of successful and failed entities cannot exceed processed count.", nameof(request.SuccessfulEntities));

        try
        {
            _logger.LogDebug("Completing batch tracking for {EntityType} batch {BatchNumber} in migration {MigrationId}: {Successful}/{Processed} successful",
                request.EntityType, request.BatchNumber, request.MigrationId, request.SuccessfulEntities, request.EntitiesProcessed);

            // Update progress tracker with batch completion
            await _progressTracker.RecordBatchCompletionAsync(
                request.MigrationId,
                request.EntityType,
                request.BatchNumber,
                request.EntitiesProcessed,
                request.SuccessfulEntities,
                request.FailedEntities,
                cancellationToken);

            _logger.LogInformation("Successfully completed batch tracking for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete batch tracking for {EntityType} batch {BatchNumber} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Updates enhanced entity progress
    /// Note: SignalR calls moved to orchestrator to prevent replay issues
    /// </summary>
    /// <param name="request">Update progress request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("UpdateEnhancedEntityProgress")]
    public async Task UpdateEnhancedEntityProgressAsync(
        [ActivityTrigger] UpdateEnhancedEntityProgressRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Validate progress percentage
        if (request.ProgressPercentage < 0 || request.ProgressPercentage > 100)
        {
            throw new ArgumentException($"Progress percentage must be between 0 and 100, but was {request.ProgressPercentage}", nameof(request.ProgressPercentage));
        }

        try
        {
            _logger.LogDebug("Updating enhanced entity progress for {EntityType} in migration {MigrationId}: {ProgressPercentage}% complete",
                request.EntityType, request.MigrationId, request.ProgressPercentage);

            // Update progress tracker
            var progressUpdate = new ProgressUpdate
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                ProcessedCount = request.ProcessedEntities,
                SuccessCount = request.SuccessfulEntities,
                FailureCount = request.FailedEntities,
                Phase = request.Phase,
                StatusMessage = request.CurrentActivity
            };

            await _progressTracker.UpdateProgressAsync(request.MigrationId, progressUpdate, cancellationToken);

            _logger.LogInformation("Successfully updated enhanced entity progress for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update enhanced entity progress for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Completes entity batch tracking
    /// Note: SignalR calls moved to orchestrator to prevent replay issues
    /// </summary>
    /// <param name="request">Complete entity tracking request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("CompleteEntityBatchTracking")]
    public async Task CompleteEntityBatchTrackingAsync(
        [ActivityTrigger] CompleteEntityBatchTrackingRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Validate request parameters
        if (request.SuccessfulEntities + request.FailedEntities > request.TotalProcessed)
            throw new ArgumentException("Sum of successful and failed entities cannot exceed total processed count.", nameof(request.SuccessfulEntities));

        try
        {
            _logger.LogDebug("Completing entity batch tracking for {EntityType} in migration {MigrationId}: {Successful}/{Total} successful",
                request.EntityType, request.MigrationId, request.SuccessfulEntities, request.TotalProcessed);

            // Complete entity processing
            await _progressTracker.CompleteEntityProcessingAsync(
                request.MigrationId,
                request.EntityType,
                cancellationToken);

            _logger.LogInformation("Successfully completed entity batch tracking for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete entity batch tracking for {EntityType} in migration {MigrationId}",
                request.EntityType, request.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Tests concurrent execution handling
    /// </summary>
    [Function("TestConcurrentExecution")]
    public async Task TestConcurrentExecutionAsync(
        [ActivityTrigger] string testData,
        CancellationToken cancellationToken = default)
    {
        // Simulate some work
        await Task.Delay(100, cancellationToken);
        
        _logger.LogDebug("Concurrent execution test completed for data: {TestData}", testData);
    }
}

/// <summary>
/// Request model for initializing entity batch tracking
/// </summary>
public class InitializeEntityBatchTrackingRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int TotalBatches { get; set; }
    public int TotalEntities { get; set; }
}

/// <summary>
/// Request model for starting batch tracking
/// </summary>
public class StartBatchTrackingRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public int BatchSize { get; set; }
    public int TotalBatches { get; set; }
}

/// <summary>
/// Request model for completing batch tracking
/// </summary>
public class CompleteBatchTrackingRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int BatchNumber { get; set; }
    public int EntitiesProcessed { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
    public int BatchSize { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Request model for updating enhanced entity progress
/// </summary>
public class UpdateEnhancedEntityProgressRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int CurrentBatch { get; set; }
    public int TotalBatches { get; set; }
    public int TotalEntities { get; set; }
    public int ProcessedEntities { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
    public double ProgressPercentage { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string CurrentActivity { get; set; } = string.Empty;
    public int BatchSize { get; set; }
    public int RemainingBatches { get; set; }
    public int RemainingEntities { get; set; }
}

/// <summary>
/// Request model for completing entity batch tracking
/// </summary>
public class CompleteEntityBatchTrackingRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int TotalProcessed { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
} 