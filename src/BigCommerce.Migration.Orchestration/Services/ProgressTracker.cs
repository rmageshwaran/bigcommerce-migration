using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using System.Collections.Concurrent;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Service for tracking and reporting migration progress
/// Implements comprehensive progress tracking with proper cancellation support and real-time SignalR broadcasting
/// </summary>
public class ProgressTracker : IProgressTracker
{
    private readonly ILogger<ProgressTracker> _logger;
    private readonly ConcurrentDictionary<string, MigrationProgress> _progressCache;
    private readonly object _lock = new object();
    private readonly IMigrationSignalRService _signalRService;
    
    /// <summary>
    /// Initializes a new instance of the ProgressTracker
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="signalRService">SignalR service for real-time updates</param>
    public ProgressTracker(ILogger<ProgressTracker> logger, IMigrationSignalRService signalRService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progressCache = new ConcurrentDictionary<string, MigrationProgress>();
        _signalRService = signalRService; // Optional for backward compatibility
    }
    
    /// <inheritdoc />
    public async Task UpdateProgressAsync(string migrationId, ProgressUpdate update, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (update == null)
            throw new ArgumentNullException(nameof(update));
        
        _logger.LogDebug("Updating progress for migration {MigrationId}: {EntityType} - {Phase}", 
            migrationId, update.EntityType, update.Phase);
        
        try
        {
            // Update in-memory cache
            var progress = GetOrCreateProgress(migrationId);
            lock (_lock)
            {
                UpdateProgressFromUpdate(progress, update);
                CalculateOverallProgress(progress);
            }
            
            // Broadcast real-time progress update
            if (_signalRService != null)
            {
                try
                {
                    await _signalRService.BroadcastProgressUpdateAsync(migrationId, CreateProgressCopy(progress), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to broadcast progress update for migration {MigrationId}", migrationId);
                }
            }
            
            // TODO: Implement progress storage in future phase when needed
            // For now, use in-memory tracking which is sufficient for current requirements
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("UpdateProgressAsync was cancelled for migration {MigrationId}", migrationId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task<MigrationProgress> GetProgressAsync(string migrationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        _logger.LogDebug("Getting progress for migration {MigrationId}", migrationId);
        
        try
        {
            var progress = GetOrCreateProgress(migrationId);
            lock (_lock)
            {
                // Update calculated fields
                CalculateOverallProgress(progress);
                progress.LastUpdated = DateTime.UtcNow;
                progress.ElapsedTime = progress.LastUpdated - progress.StartTime;
            }
            
            // Return a copy to avoid external modification
            return CreateProgressCopy(progress);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("GetProgressAsync was cancelled for migration {MigrationId}", migrationId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task StartEntityProcessingAsync(string migrationId, string entityType, int totalCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrEmpty(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        
        if (totalCount < 0)
            throw new ArgumentException("Total count cannot be negative", nameof(totalCount));
        
        _logger.LogDebug("Starting {EntityType} processing for migration {MigrationId}: {TotalCount} entities", 
            entityType, migrationId, totalCount);
        
        try
        {
            var progress = GetOrCreateProgress(migrationId);
            lock (_lock)
            {
                if (!progress.EntityProgress.ContainsKey(entityType))
                {
                    progress.EntityProgress[entityType] = new EntityProgress
                    {
                        EntityType = entityType,
                        TotalCount = totalCount,
                        ProcessedCount = 0,
                        SuccessCount = 0,
                        FailureCount = 0,
                        ProgressPercentage = 0.0,
                        Status = "processing",
                        StartTime = DateTime.UtcNow
                    };
                }
                
                progress.CurrentEntity = entityType;
                progress.CurrentPhase = $"{entityType}_migration";
                CalculateOverallProgress(progress);
            }
            
            // Broadcast entity start notification
            if (_signalRService != null)
            {
                try
                {
                    await _signalRService.BroadcastEntityStartAsync(migrationId, entityType, totalCount, cancellationToken);
                    await _signalRService.BroadcastProgressUpdateAsync(migrationId, CreateProgressCopy(progress), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to broadcast entity start notification for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("StartEntityProcessingAsync was cancelled for migration {MigrationId}", migrationId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task RecordBatchCompletionAsync(string migrationId, string entityType, int batchNumber, 
        int processedCount, int successCount, int failureCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrEmpty(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        
        if (successCount + failureCount > processedCount)
            throw new ArgumentException("Success count plus failure count cannot exceed processed count");
        
        _logger.LogDebug("Recording batch completion for migration {MigrationId}, {EntityType} batch {BatchNumber}: " +
            "processed {ProcessedCount}, success {SuccessCount}, failures {FailureCount}", 
            migrationId, entityType, batchNumber, processedCount, successCount, failureCount);
        
        try
        {
            var progress = GetOrCreateProgress(migrationId);
            lock (_lock)
            {
                if (progress.EntityProgress.ContainsKey(entityType))
                {
                    var entityProgress = progress.EntityProgress[entityType];
                    entityProgress.ProcessedCount += processedCount;
                    entityProgress.SuccessCount += successCount;
                    entityProgress.FailureCount += failureCount;
                    
                    // Update entity progress percentage
                    if (entityProgress.TotalCount > 0)
                    {
                        entityProgress.ProgressPercentage = (double)entityProgress.ProcessedCount / entityProgress.TotalCount * 100.0;
                    }
                    
                    entityProgress.ProcessingTime = DateTime.UtcNow - entityProgress.StartTime;
                }
                
                // Update overall progress
                progress.ProcessedEntities += processedCount;
                progress.SuccessfulEntities += successCount;
                progress.FailedEntities += failureCount;
                
                CalculateOverallProgress(progress);
                CalculateProcessingSpeed(progress);
            }
            
            // Broadcast batch completion notification
            if (_signalRService != null)
            {
                try
                {
                    var batchResults = new
                    {
                        BatchNumber = batchNumber,
                        EntityType = entityType,
                        ProcessedCount = processedCount,
                        SuccessCount = successCount,
                        FailureCount = failureCount,
                        Timestamp = DateTime.UtcNow
                    };
                    
                    await _signalRService.BroadcastBatchCompletionAsync(migrationId, entityType, batchNumber, batchResults, cancellationToken);
                    await _signalRService.BroadcastProgressUpdateAsync(migrationId, CreateProgressCopy(progress), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to broadcast batch completion for migration {MigrationId}, entity {EntityType}, batch {BatchNumber}", migrationId, entityType, batchNumber);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RecordBatchCompletionAsync was cancelled for migration {MigrationId}", migrationId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task CompleteEntityProcessingAsync(string migrationId, string entityType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrEmpty(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        
        _logger.LogDebug("Completing {EntityType} processing for migration {MigrationId}", 
            entityType, migrationId);
        
        try
        {
            var progress = GetOrCreateProgress(migrationId);
            lock (_lock)
            {
                if (progress.EntityProgress.ContainsKey(entityType))
                {
                    var entityProgress = progress.EntityProgress[entityType];
                    entityProgress.Status = "completed";
                    entityProgress.EndTime = DateTime.UtcNow;
                    entityProgress.ProcessingTime = entityProgress.EndTime.Value - entityProgress.StartTime;
                    entityProgress.ProgressPercentage = 100.0;
                }
                
                CalculateOverallProgress(progress);
                
                // Check if all entities are completed
                if (progress.EntityProgress.Values.All(e => e.Status == "completed"))
                {
                    progress.Status = "completed";
                    progress.CurrentPhase = "completed";
                }
            }
            
            // Broadcast entity completion notification
            if (_signalRService != null)
            {
                try
                {
                    var entityResults = progress.EntityProgress.ContainsKey(entityType) ? progress.EntityProgress[entityType] : null;
                    var results = new
                    {
                        EntityType = entityType,
                        Status = "completed",
                        TotalCount = entityResults?.TotalCount ?? 0,
                        ProcessedCount = entityResults?.ProcessedCount ?? 0,
                        SuccessCount = entityResults?.SuccessCount ?? 0,
                        FailureCount = entityResults?.FailureCount ?? 0,
                        ProcessingTime = entityResults?.ProcessingTime ?? TimeSpan.Zero,
                        Timestamp = DateTime.UtcNow
                    };
                    
                    await _signalRService.BroadcastEntityCompletionAsync(migrationId, entityType, results, cancellationToken);
                    await _signalRService.BroadcastProgressUpdateAsync(migrationId, CreateProgressCopy(progress), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to broadcast entity completion for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CompleteEntityProcessingAsync was cancelled for migration {MigrationId}", migrationId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task NotifyProgressUpdateAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (progress == null)
            throw new ArgumentNullException(nameof(progress));
        
        _logger.LogDebug("Notifying progress update for migration {MigrationId}: {OverallProgress}%", 
            migrationId, progress.OverallProgressPercentage);
        
        try
        {
            // Broadcast real-time progress notification via SignalR
            if (_signalRService != null)
            {
                try
                {
                    await _signalRService.BroadcastProgressUpdateAsync(migrationId, progress, cancellationToken);
                    
                    _logger.LogDebug("Broadcasted progress notification for {MigrationId}: {OverallProgress}% complete, " +
                        "{ProcessedEntities}/{TotalEntities} entities processed", 
                        migrationId, progress.OverallProgressPercentage, progress.ProcessedEntities, progress.TotalEntities);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to broadcast progress notification for migration {MigrationId}", migrationId);
                }
            }
            else
            {
                // Fallback to logging when SignalR service is not available
                _logger.LogInformation("Progress notification for {MigrationId}: {OverallProgress}% complete, " +
                    "{ProcessedEntities}/{TotalEntities} entities processed", 
                    migrationId, progress.OverallProgressPercentage, progress.ProcessedEntities, progress.TotalEntities);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("NotifyProgressUpdateAsync was cancelled for migration {MigrationId}", migrationId);
            throw;
        }
    }
    
    /// <summary>
    /// Gets or creates a progress object for the specified migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Migration progress object</returns>
    private MigrationProgress GetOrCreateProgress(string migrationId)
    {
        return _progressCache.GetOrAdd(migrationId, _ => new MigrationProgress
        {
            MigrationId = migrationId,
            Status = "in_progress",
            StartTime = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            ElapsedTime = TimeSpan.Zero,
            EstimatedTimeRemaining = TimeSpan.Zero,
            TotalEntities = 0,
            ProcessedEntities = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            OverallProgressPercentage = 0.0,
            EntityProgress = new Dictionary<string, EntityProgress>(),
            CurrentPhase = "initialization",
            CurrentEntity = string.Empty,
            EntitiesPerSecond = 0.0,
            ErrorRate = 0.0
        });
    }
    
    /// <summary>
    /// Updates progress from a progress update
    /// </summary>
    /// <param name="progress">Migration progress to update</param>
    /// <param name="update">Progress update information</param>
    private void UpdateProgressFromUpdate(MigrationProgress progress, ProgressUpdate update)
    {
        progress.CurrentPhase = update.Phase;
        progress.CurrentEntity = update.EntityType;
        progress.LastUpdated = update.Timestamp;
        
        // Update entity-specific progress if exists
        if (progress.EntityProgress.ContainsKey(update.EntityType))
        {
            var entityProgress = progress.EntityProgress[update.EntityType];
            entityProgress.ProcessedCount = update.ProcessedCount;
            entityProgress.SuccessCount = update.SuccessCount;
            entityProgress.FailureCount = update.FailureCount;
        }
    }
    
    /// <summary>
    /// Calculates overall progress percentage and totals
    /// </summary>
    /// <param name="progress">Migration progress to calculate</param>
    private void CalculateOverallProgress(MigrationProgress progress)
    {
        progress.TotalEntities = progress.EntityProgress.Values.Sum(e => e.TotalCount);
        progress.ProcessedEntities = progress.EntityProgress.Values.Sum(e => e.ProcessedCount);
        progress.SuccessfulEntities = progress.EntityProgress.Values.Sum(e => e.SuccessCount);
        progress.FailedEntities = progress.EntityProgress.Values.Sum(e => e.FailureCount);
        
        if (progress.TotalEntities > 0)
        {
            progress.OverallProgressPercentage = (double)progress.ProcessedEntities / progress.TotalEntities * 100.0;
        }
        
        // Calculate error rate
        if (progress.ProcessedEntities > 0)
        {
            progress.ErrorRate = (double)progress.FailedEntities / progress.ProcessedEntities;
        }
        
        // Estimate time remaining
        CalculateTimeRemaining(progress);
    }
    
    /// <summary>
    /// Calculates processing speed and estimated time remaining
    /// </summary>
    /// <param name="progress">Migration progress to calculate</param>
    private void CalculateProcessingSpeed(MigrationProgress progress)
    {
        var elapsedSeconds = (DateTime.UtcNow - progress.StartTime).TotalSeconds;
        if (elapsedSeconds > 0 && progress.ProcessedEntities > 0)
        {
            progress.EntitiesPerSecond = progress.ProcessedEntities / elapsedSeconds;
        }
    }
    
    /// <summary>
    /// Calculates estimated time remaining based on current progress
    /// </summary>
    /// <param name="progress">Migration progress to calculate</param>
    private void CalculateTimeRemaining(MigrationProgress progress)
    {
        if (progress.EntitiesPerSecond > 0)
        {
            var remainingEntities = progress.TotalEntities - progress.ProcessedEntities;
            var estimatedSecondsRemaining = remainingEntities / progress.EntitiesPerSecond;
            progress.EstimatedTimeRemaining = TimeSpan.FromSeconds(estimatedSecondsRemaining);
        }
        else
        {
            progress.EstimatedTimeRemaining = TimeSpan.Zero;
        }
    }
    
    /// <summary>
    /// Creates a deep copy of migration progress to avoid external modification
    /// </summary>
    /// <param name="original">Original progress object</param>
    /// <returns>Copy of the progress object</returns>
    private MigrationProgress CreateProgressCopy(MigrationProgress original)
    {
        return new MigrationProgress
        {
            MigrationId = original.MigrationId,
            Status = original.Status,
            StartTime = original.StartTime,
            LastUpdated = original.LastUpdated,
            ElapsedTime = original.ElapsedTime,
            EstimatedTimeRemaining = original.EstimatedTimeRemaining,
            TotalEntities = original.TotalEntities,
            ProcessedEntities = original.ProcessedEntities,
            SuccessfulEntities = original.SuccessfulEntities,
            FailedEntities = original.FailedEntities,
            OverallProgressPercentage = original.OverallProgressPercentage,
            EntityProgress = original.EntityProgress.ToDictionary(
                kvp => kvp.Key,
                kvp => new EntityProgress
                {
                    EntityType = kvp.Value.EntityType,
                    TotalCount = kvp.Value.TotalCount,
                    ProcessedCount = kvp.Value.ProcessedCount,
                    SuccessCount = kvp.Value.SuccessCount,
                    FailureCount = kvp.Value.FailureCount,
                    ProgressPercentage = kvp.Value.ProgressPercentage,
                    Status = kvp.Value.Status,
                    StartTime = kvp.Value.StartTime,
                    EndTime = kvp.Value.EndTime,
                    ProcessingTime = kvp.Value.ProcessingTime
                }),
            CurrentPhase = original.CurrentPhase,
            CurrentEntity = original.CurrentEntity,
            EntitiesPerSecond = original.EntitiesPerSecond,
            ErrorRate = original.ErrorRate
        };
    }
} 