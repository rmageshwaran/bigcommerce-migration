using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
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
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly IMigrationStorageService? _storageService;
    
    /// <summary>
    /// Initializes a new instance of the ProgressTracker
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="progressEventPublisher">Progress event publisher for queue-based SignalR broadcasting</param>
    /// <param name="storageService">Storage service for persisting progress</param>
    public ProgressTracker(
        ILogger<ProgressTracker> logger, 
        IProgressEventPublisher progressEventPublisher,
        IMigrationStorageService? storageService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _progressCache = new ConcurrentDictionary<string, MigrationProgress>();
        _storageService = storageService; // Optional for backward compatibility
    }
    
    /// <inheritdoc />
    public async Task UpdateProgressAsync(string migrationId, ProgressUpdate update, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (update == null)
            throw new ArgumentNullException(nameof(update));
        
        try
        {
            // Update in-memory cache
            var progress = GetOrCreateProgress(migrationId);
            lock (_lock)
            {
                UpdateProgressFromUpdate(progress, update);
                CalculateOverallProgress(progress);
            }
            
            // Publish progress update event to queue for SignalR broadcasting
            await PublishMigrationProgressEventAsync(migrationId, progress, cancellationToken);
            
            // Persist progress to storage for durability across application restarts
            if (_storageService != null)
            {
                try
                {
                    await PersistProgressToStorageAsync(migrationId, progress, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist progress to storage for migration {MigrationId}", migrationId);
                    // Don't fail the progress update if storage fails
                }
            }
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
        
        try
        {
            var progress = GetOrCreateProgress(migrationId);
            
            // Check if we have meaningful progress data in cache
            var hasMeaningfulProgress = progress.TotalEntities > 0 || 
                                       progress.ProcessedEntities > 0 || 
                                       progress.EntityProgress.Any() ||
                                       progress.StartTime < DateTime.UtcNow.AddMinutes(-1); // Not just created
            
            // If no meaningful progress data and we have storage service, try to reconstruct from storage
            if (!hasMeaningfulProgress && _storageService != null)
            {
                try
                {
                    _logger.LogDebug("Progress cache has no meaningful data for migration {MigrationId}, attempting to reconstruct from storage", migrationId);
                    
                    var migrationEntry = await _storageService.GetMigrationAsync(migrationId);
                    if (migrationEntry != null)
                    {
                        // Get entity-level progress from storage
                        var entityProgressEntries = await _storageService.GetEntityProgressAsync(migrationId);
                        
                        lock (_lock)
                        {
                            // Update progress with storage data
                            progress.Status = migrationEntry.Status.ToString().ToLower();
                            progress.StartTime = migrationEntry.CreatedAt;
                            progress.LastUpdated = migrationEntry.UpdatedAt;
                            progress.OverallProgressPercentage = migrationEntry.ProgressPercentage;
                            progress.TotalEntities = migrationEntry.TotalEntities;
                            progress.ProcessedEntities = migrationEntry.ProcessedEntities;
                            progress.SuccessfulEntities = migrationEntry.ProcessedEntities - migrationEntry.FailedEntities;
                            progress.FailedEntities = migrationEntry.FailedEntities;
                            progress.CurrentPhase = migrationEntry.CurrentPhase ?? "completed";
                            
                            // Reconstruct entity progress
                            progress.EntityProgress.Clear();
                            foreach (var entry in entityProgressEntries)
                            {
                                progress.EntityProgress[entry.EntityType] = new EntityProgress
                                {
                                    EntityType = entry.EntityType,
                                    TotalCount = entry.TotalCount,
                                    ProcessedCount = entry.ProcessedCount,
                                    SuccessCount = entry.SuccessCount,
                                    FailureCount = entry.FailureCount,
                                    ProgressPercentage = entry.ProgressPercentage,
                                    Status = entry.Status,
                                    StartTime = entry.StartTime,
                                    EndTime = entry.EndTime,
                                    ProcessingTime = entry.ProcessingTime
                                };
                            }
                            
                            // Update calculated fields
                            CalculateOverallProgress(progress);
                            progress.ElapsedTime = progress.LastUpdated - progress.StartTime;
                        }
                        
                        _logger.LogDebug("Successfully reconstructed progress for migration {MigrationId} from storage", migrationId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to reconstruct progress for migration {MigrationId} from storage", migrationId);
                    // Continue with in-memory progress
                }
            }
            else
            {
                lock (_lock)
                {
                    // Update calculated fields
                    CalculateOverallProgress(progress);
                    progress.LastUpdated = DateTime.UtcNow;
                    progress.ElapsedTime = progress.LastUpdated - progress.StartTime;
                }
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
            
            // Publish entity start event to queue for SignalR broadcasting
            await PublishEntityProgressEventAsync(migrationId, entityType, progress, cancellationToken);
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
            
            // Publish batch completion event to queue for SignalR broadcasting
            await PublishBatchProgressEventAsync(migrationId, entityType, batchNumber, processedCount, successCount, failureCount, cancellationToken);
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
            
            // Publish entity completion event to queue for SignalR broadcasting
            await PublishEntityProgressEventAsync(migrationId, entityType, progress, cancellationToken);
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
        
        try
        {
            // Publish progress notification event to queue for SignalR broadcasting
            await PublishMigrationProgressEventAsync(migrationId, progress, cancellationToken);
            
            _logger.LogInformation("Progress notification for {MigrationId}: {OverallProgress}% complete, " +
                "{ProcessedEntities}/{TotalEntities} entities processed", 
                migrationId, progress.OverallProgressPercentage, progress.ProcessedEntities, progress.TotalEntities);
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
            
            // Calculate entity progress percentage
            if (entityProgress.TotalCount > 0)
            {
                entityProgress.ProgressPercentage = (double)entityProgress.ProcessedCount / entityProgress.TotalCount * 100.0;
            }
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
    
    /// <summary>
    /// Persists progress to storage for durability across application restarts
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="progress">Progress to persist</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task PersistProgressToStorageAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            // Get the current migration entry from storage
            var migrationEntry = await _storageService.GetMigrationAsync(migrationId);
            if (migrationEntry != null)
            {
                // Update the migration entry with current progress
                migrationEntry.ProgressPercentage = (int)Math.Round(progress.OverallProgressPercentage);
                migrationEntry.CurrentPhase = progress.CurrentPhase;
                migrationEntry.TotalEntities = progress.TotalEntities;
                migrationEntry.ProcessedEntities = progress.ProcessedEntities;
                migrationEntry.FailedEntities = progress.FailedEntities;
                migrationEntry.UpdatedAt = DateTime.UtcNow;
                
                // Update the migration in storage
                await _storageService.UpdateMigrationAsync(migrationEntry);
                
            }

            // Persist entity-level progress data
            foreach (var entityProgress in progress.EntityProgress)
            {
                try
                {
                    var entityProgressEntry = new EntityProgressEntry
                    {
                        MigrationId = migrationId,
                        EntityType = entityProgress.Key,
                        TotalCount = entityProgress.Value.TotalCount,
                        ProcessedCount = entityProgress.Value.ProcessedCount,
                        SuccessCount = entityProgress.Value.SuccessCount,
                        FailureCount = entityProgress.Value.FailureCount,
                        ProgressPercentage = entityProgress.Value.ProgressPercentage,
                        Status = entityProgress.Value.Status,
                        StartTime = entityProgress.Value.StartTime,
                        EndTime = entityProgress.Value.EndTime,
                        ProcessingTime = entityProgress.Value.ProcessingTime,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await _storageService.CreateOrUpdateEntityProgressAsync(entityProgressEntry);
                    
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist entity progress for migration {MigrationId}, entity {EntityType}", 
                        migrationId, entityProgress.Key);
                    // Continue with other entities even if one fails
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist progress to storage for migration {MigrationId}", migrationId);
            // Don't rethrow - progress persistence is not critical for migration execution
        }
    }

    /// <summary>
    /// Publishes a migration progress event to the queue for SignalR broadcasting
    /// SOLID: Single Responsibility - handles only migration progress event publishing
    /// </summary>
    private async Task PublishMigrationProgressEventAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("📊 [PROGRESS-TRACKER] Publishing migration progress event for MigrationId: {MigrationId}, Progress: {Progress}%, Status: {Status}, Entities: {ProcessedEntities}/{TotalEntities}", 
                migrationId, progress.OverallProgressPercentage, progress.Status, progress.ProcessedEntities, progress.TotalEntities);

            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = migrationId,
                OverallProgress = progress.OverallProgressPercentage,
                Status = progress.Status,
                TotalEntities = progress.TotalEntities,
                ProcessedEntities = progress.ProcessedEntities,
                FailedEntities = progress.FailedEntities,
                CurrentEntityType = progress.CurrentEntity,
                EstimatedTimeRemaining = progress.EstimatedTimeRemaining
            };

            _logger.LogInformation("📡 [PROGRESS-TRACKER] Calling ProgressEventPublisher for MigrationId: {MigrationId}", migrationId);
            await _progressEventPublisher.PublishMigrationProgressAsync(progressEvent, cancellationToken);
            _logger.LogInformation("✅ [PROGRESS-TRACKER] Successfully published migration progress event for MigrationId: {MigrationId}", migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "💥 [PROGRESS-TRACKER] Failed to publish migration progress event for migration {MigrationId}", migrationId);
            // Don't rethrow - progress events should not break the migration
        }
    }

    /// <summary>
    /// Publishes an entity progress event to the queue for SignalR broadcasting
    /// SOLID: Single Responsibility - handles only entity progress event publishing
    /// </summary>
    private async Task PublishEntityProgressEventAsync(string migrationId, string entityType, MigrationProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            var entityProgress = progress.EntityProgress.ContainsKey(entityType) ? progress.EntityProgress[entityType] : null;
            
            var entityEvent = new EntityProgressEvent
            {
                MigrationId = migrationId,
                EntityType = entityType,
                TotalCount = entityProgress?.TotalCount ?? 0,
                ProcessedCount = entityProgress?.ProcessedCount ?? 0,
                SuccessCount = entityProgress?.SuccessCount ?? 0,
                FailureCount = entityProgress?.FailureCount ?? 0,
                Status = entityProgress?.Status ?? "starting",
                ProcessingTime = entityProgress?.ProcessingTime
            };

            await _progressEventPublisher.PublishEntityProgressAsync(entityEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish entity progress event for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
            // Don't rethrow - progress events should not break the migration
        }
    }

    /// <summary>
    /// Publishes a batch progress event to the queue for SignalR broadcasting
    /// SOLID: Single Responsibility - handles only batch progress event publishing
    /// </summary>
    private async Task PublishBatchProgressEventAsync(string migrationId, string entityType, int batchNumber, int processedCount, int successCount, int failureCount, CancellationToken cancellationToken)
    {
        try
        {
            var progress = GetOrCreateProgress(migrationId);
            var entityProgress = progress.EntityProgress.ContainsKey(entityType) ? progress.EntityProgress[entityType] : null;
            
            var batchEvent = new BatchProgressEvent
            {
                MigrationId = migrationId,
                EntityType = entityType,
                BatchNumber = batchNumber,
                TotalBatches = 0, // This could be calculated if we track total batches
                BatchSize = processedCount,
                ProcessedCount = processedCount,
                FailedCount = failureCount,
                Status = failureCount > 0 ? "completed_with_errors" : "completed",
                ProcessingTime = TimeSpan.FromSeconds(1) // Approximate - could be tracked more precisely
            };

            await _progressEventPublisher.PublishBatchProgressAsync(batchEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish batch progress event for migration {MigrationId}, entity {EntityType}, batch {BatchNumber}", migrationId, entityType, batchNumber);
            // Don't rethrow - progress events should not break the migration
        }
    }
} 