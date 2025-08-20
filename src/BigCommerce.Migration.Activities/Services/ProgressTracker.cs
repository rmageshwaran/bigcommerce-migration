using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using System.Collections.Concurrent;

namespace BigCommerce.Migration.Activities.Services;

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
    private readonly ISignalREventFactory _signalREventFactory; // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
    private readonly IMigrationStorageService? _storageService;
    private readonly IIncrementEventsService? _incrementEventsService; // 🆕 INCREMENTAL PROGRESS: Service for writing chunk increment events
    
    /// <summary>
    /// Initializes a new instance of the ProgressTracker
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="progressEventPublisher">Progress event publisher for queue-based SignalR broadcasting</param>
    /// <param name="signalREventFactory">SignalR event factory for consistent event creation</param>
    /// <param name="storageService">Storage service for persisting progress</param>
    /// <param name="incrementEventsService">Increment events service for real-time progress tracking</param>
    public ProgressTracker(
        ILogger<ProgressTracker> logger, 
        IProgressEventPublisher progressEventPublisher,
        ISignalREventFactory signalREventFactory, // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
        IMigrationStorageService? storageService = null,
        IIncrementEventsService? incrementEventsService = null) // 🆕 INCREMENTAL PROGRESS: Optional for backward compatibility
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory)); // 🎯 CENTRALIZED SIGNALR: Store factory reference
        _progressCache = new ConcurrentDictionary<string, MigrationProgress>();
        _storageService = storageService; // Optional for backward compatibility
        _incrementEventsService = incrementEventsService; // 🆕 INCREMENTAL PROGRESS: Optional for backward compatibility
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
            
            // Note: SignalR progress publishing removed - handled by CentralizedProgressBroadcastService
            
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
                            progress.SuccessfulEntities = migrationEntry.SuccessfulEntities; // 🚨 CRITICAL FIX: Load SuccessfulEntities directly from database
                            progress.FailedEntities = migrationEntry.FailedEntities;
                            progress.SkippedEntities = migrationEntry.SkippedEntities;  // 🚨 FIX: Include SkippedEntities  
                            progress.CancelledEntities = migrationEntry.CancelledEntities; // 🚨 CRITICAL FIX: Include CancelledEntities from database
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
                                    SkippedCount = entry.SkippedCount,  // 🚨 FIX: Include SkippedCount
                                    CancelledCount = entry.CancelledCount, // 🚨 CRITICAL FIX: Include CancelledCount from database  
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
                        SkippedCount = 0,  // 🚨 FIX: Initialize SkippedCount
                        CancelledCount = 0, // 🚨 FIX: Initialize CancelledCount
                        ProgressPercentage = 0.0,
                        Status = "processing",
                        StartTime = DateTime.UtcNow
                    };
                }
                
                progress.CurrentEntity = entityType;
                progress.CurrentPhase = $"{entityType}_migration";
                CalculateOverallProgress(progress);
            }
            
            // Note: SignalR entity publishing removed - handled by CentralizedProgressBroadcastService
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("StartEntityProcessingAsync was cancelled for migration {MigrationId}", migrationId);
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
                    
                    // 🚫 ENTITY STATUS CANCELLATION FIX: Check if migration was cancelled
                    if (progress.IsCancelled == true)
                    {
                        entityProgress.Status = "cancelled";
                        _logger.LogInformation("🚫 Marking {EntityType} status as 'cancelled' for migration {MigrationId}", entityType, migrationId);
                    }
                    else
                    {
                        entityProgress.Status = "completed";
                    }
                    
                    entityProgress.EndTime = DateTime.UtcNow;
                    entityProgress.ProcessingTime = entityProgress.EndTime.Value - entityProgress.StartTime;
                    entityProgress.ProgressPercentage = 100.0;
                }
                
                CalculateOverallProgress(progress);
                
                // Check if all entities are finished (completed or cancelled)
                if (progress.EntityProgress.Values.All(e => e.Status == "completed" || e.Status == "cancelled"))
                {
                    // 🚫 MIGRATION STATUS CANCELLATION FIX: Set overall status based on presence of cancelled entities
                    bool hasAnyCancelled = progress.EntityProgress.Values.Any(e => e.Status == "cancelled");
                    if (hasAnyCancelled || progress.IsCancelled == true)
                    {
                        progress.Status = "cancelled";
                        progress.CurrentPhase = "cancelled";
                        _logger.LogInformation("🚫 Setting overall migration status to 'cancelled' for migration {MigrationId}", migrationId);
                    }
                    else
                    {
                        progress.Status = "completed";
                        progress.CurrentPhase = "completed";
                    }
                }
            }
            
            // Note: SignalR entity completion publishing removed - handled by CentralizedProgressBroadcastService
            
            // NOTE: Database persistence is handled by UpdateProgressAsync flow, not needed here
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CompleteEntityProcessingAsync was cancelled for migration {MigrationId}", migrationId);
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
            SkippedEntities = 0, // 🚨 FIX: Initialize SkippedEntities
            CancelledEntities = 0, // 🚨 FIX: Initialize CancelledEntities
            OverallProgressPercentage = 0.0,
            EntityProgress = new Dictionary<string, EntityProgress>(),
            CurrentPhase = "initialization",
            CurrentEntity = string.Empty,
            EntitiesPerSecond = 0.0,
            ErrorRate = 0.0
        });
    }
    
    /// <summary>
    /// Updates the migration progress from an update object
    /// Phase 4.2: Enhanced to propagate soft cancellation state for SignalR filtering
    /// </summary>
    /// <param name="progress">Migration progress to update</param>
    /// <param name="update">Progress update information</param>
    private void UpdateProgressFromUpdate(MigrationProgress progress, ProgressUpdate update)
    {
        progress.CurrentPhase = update.Phase;
        progress.CurrentEntity = update.EntityType;
        progress.LastUpdated = update.Timestamp;
        
        // Phase 4.2: Propagate soft cancellation state from update to progress
        progress.IsCancelled = update.IsCancelled;
        progress.CancellationReason = update.CancellationReason;
        progress.CancelledAt = update.CancelledAt;
        
        // Update entity-specific progress if exists
        if (progress.EntityProgress.ContainsKey(update.EntityType))
        {
            var entityProgress = progress.EntityProgress[update.EntityType];
            entityProgress.ProcessedCount = update.ProcessedCount;
            entityProgress.SuccessCount = update.SuccessCount;
            entityProgress.FailureCount = update.FailureCount;
            entityProgress.SkippedCount = update.SkippedCount;
            entityProgress.CancelledCount = update.CancelledCount; // 🚨 CRITICAL FIX: Update cancelled count in database
            
            // 🎯 PROGRESSIVE DISCOVERY FIX: Update TotalCount for component entities when they complete
            // Use 0 to indicate "no update" instead of nullable
            if (update.TotalCount > 0)
            {
                entityProgress.TotalCount = update.TotalCount;
            }
            
            // 🚨 STATUS FIX: Calculate entity progress percentage using total processed (including skipped)
            // This ensures proper completion when ProcessedCount = SuccessCount + FailureCount + SkippedCount
            if (entityProgress.TotalCount > 0)
            {
                entityProgress.ProgressPercentage = (double)entityProgress.ProcessedCount / entityProgress.TotalCount * 100.0;
            }
            
            // 🚨 STATUS FIX: Update entity status based on whether all entities are accounted for and cancellation state
            if (entityProgress.ProcessedCount >= entityProgress.TotalCount)
            {
                // 🚫 ENTITY STATUS CANCELLATION FIX: Check if migration was cancelled
                if (progress.IsCancelled == true)
                {
                    entityProgress.Status = "cancelled";
                    _logger.LogInformation("🚫 Setting {EntityType} status to 'cancelled' for migration {MigrationId} (all entities processed but migration was cancelled)", 
                        update.EntityType, update.MigrationId);
                }
                else
                {
                    entityProgress.Status = "completed";
                }
            }
            else if (entityProgress.ProcessedCount > 0)
            {
                entityProgress.Status = "processing";
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
        progress.SkippedEntities = progress.EntityProgress.Values.Sum(e => e.SkippedCount);
        progress.CancelledEntities = progress.EntityProgress.Values.Sum(e => e.CancelledCount); // 🚨 CRITICAL FIX: Include CancelledEntities in overall calculation
        
        if (progress.TotalEntities > 0)
        {
            progress.OverallProgressPercentage = (double)progress.ProcessedEntities / progress.TotalEntities * 100.0;
        }
        
        // Calculate error rate
        if (progress.ProcessedEntities > 0)
        {
            progress.ErrorRate = (double)progress.FailedEntities / progress.ProcessedEntities;
        }
        
        // 🎯 ESTIMATED TIME FIX: Calculate processing speed first so EntitiesPerSecond is available for time estimation
        CalculateProcessingSpeed(progress);
        
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
            SkippedEntities = original.SkippedEntities,  // 🚨 FIX: Include SkippedEntities
            CancelledEntities = original.CancelledEntities, // 🚨 CRITICAL FIX: Include CancelledEntities in copy
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
                    SkippedCount = kvp.Value.SkippedCount,  // 🚨 FIX: Include SkippedCount
                    CancelledCount = kvp.Value.CancelledCount, // 🚨 CRITICAL FIX: Include CancelledCount in entity copy
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
            // Check if storage service is available
            if (_storageService == null)
            {
                _logger.LogDebug("Storage service not available - skipping progress persistence for migration {MigrationId}", migrationId);
                return;
            }
            
            // Get the current migration entry from storage
            var migrationEntry = await _storageService.GetMigrationAsync(migrationId);
            if (migrationEntry != null)
            {
                // Update the migration entry with current progress
                migrationEntry.ProgressPercentage = (int)Math.Round(progress.OverallProgressPercentage);
                migrationEntry.CurrentPhase = progress.CurrentPhase;
                migrationEntry.TotalEntities = progress.TotalEntities;
                migrationEntry.ProcessedEntities = progress.ProcessedEntities;
                migrationEntry.SuccessfulEntities = progress.SuccessfulEntities; // 🚨 CRITICAL FIX: Include SuccessfulEntities in migration summary
                migrationEntry.FailedEntities = progress.FailedEntities;
                migrationEntry.SkippedEntities = progress.SkippedEntities; // 🚨 FIX: Include SkippedEntities in migration summary
                migrationEntry.CancelledEntities = progress.CancelledEntities; // 🚨 CRITICAL FIX: Include CancelledEntities in migration summary
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
                        SkippedCount = entityProgress.Value.SkippedCount, // 🚨 FIX: Include SkippedCount in persistence
                        CancelledCount = entityProgress.Value.CancelledCount, // 🚨 CRITICAL FIX: Include CancelledCount in database persistence
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

    // Note: Migration progress publishing removed - handled by CentralizedProgressBroadcastService
    private Task PublishMigrationProgressEventAsync(string migrationId, MigrationProgress progress, CancellationToken cancellationToken)
    {
        // Method kept as stub for backward compatibility but no longer publishes events
        _logger.LogDebug("📊 [PROGRESS-TRACKER] Progress tracking for MigrationId: {MigrationId}, Progress: {Progress}%, Status: {Status}", 
            migrationId, progress.OverallProgressPercentage, progress.Status);
        return Task.CompletedTask;
    }

    // Note: Entity progress publishing removed - handled by CentralizedProgressBroadcastService
    private Task PublishEntityProgressEventAsync(string migrationId, string entityType, MigrationProgress progress, CancellationToken cancellationToken)
    {
        // Method kept as stub for backward compatibility but no longer publishes events
        var entityProgress = progress.EntityProgress.ContainsKey(entityType) ? progress.EntityProgress[entityType] : null;
        _logger.LogDebug("📊 [PROGRESS-TRACKER] Entity progress tracking for {EntityType}: {ProcessedCount}/{TotalCount}", 
            entityType, entityProgress?.ProcessedCount ?? 0, entityProgress?.TotalCount ?? 0);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task IncrementProgressAsync(
        string migrationId,
        string entityType,
        int chunkNumber,
        int chunkStartIndex,
        int chunkSize,
        int successfulEntities,
        int failedEntities,
        int skippedEntities,
        int cancelledEntities,
        DateTime processingStartTime,
        DateTime processingEndTime,
        string sourceStore,
        string destinationStore,
        IList<string>? errors = null,
        CancellationToken cancellationToken = default)
    {
        // Validate required parameters
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrEmpty(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        
        if (string.IsNullOrEmpty(sourceStore))
            throw new ArgumentException("Source store cannot be null or empty", nameof(sourceStore));
        
        if (string.IsNullOrEmpty(destinationStore))
            throw new ArgumentException("Destination store cannot be null or empty", nameof(destinationStore));

        // If no increment events service available, log warning and return (backward compatibility)
        if (_incrementEventsService == null)
        {
            _logger.LogWarning("❌ INCREMENTAL-PROGRESS: IncrementEventsService not available - skipping incremental progress update for {MigrationId}:{EntityType}:Chunk{ChunkNumber}. " +
                "This means progress data will be lost on cancellation!", migrationId, entityType, chunkNumber);
            return;
        }
        
        _logger.LogInformation("✅ INCREMENTAL-PROGRESS: IncrementEventsService available - proceeding with incremental progress recording for {MigrationId}:{EntityType}:Chunk{ChunkNumber}",
            migrationId, entityType, chunkNumber);

        try
        {
            // Create chunk increment event
            _logger.LogInformation("🔧 INCREMENTAL-PROGRESS: Creating ChunkIncrementEvent for {MigrationId}:{EntityType}:Chunk{ChunkNumber} " +
                "with Success={Success}, Failed={Failed}, Skipped={Skipped}, Cancelled={Cancelled}",
                migrationId, entityType, chunkNumber, successfulEntities, failedEntities, skippedEntities, cancelledEntities);
                
            var incrementEvent = ChunkIncrementEvent.Create(
                migrationId, entityType, chunkNumber, chunkStartIndex, chunkSize,
                successfulEntities, failedEntities, skippedEntities, cancelledEntities,
                processingStartTime, processingEndTime, sourceStore, destinationStore, errors);

            _logger.LogInformation("📋 INCREMENTAL-PROGRESS: ChunkIncrementEvent created successfully - PartitionKey={PartitionKey}, RowKey={RowKey}, Timestamp={Timestamp}",
                incrementEvent.PartitionKey, incrementEvent.RowKey, incrementEvent.Timestamp);

            // Fire-and-forget write to prevent blocking chunk processing
            _logger.LogInformation("🚀 INCREMENTAL-PROGRESS: Starting async write to IncrementEventsService for {MigrationId}:{EntityType}:Chunk{ChunkNumber}",
                migrationId, entityType, chunkNumber);
                
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("📤 INCREMENTAL-PROGRESS: Calling IncrementEventsService.WriteChunkIncrementAsync for {MigrationId}:{EntityType}:Chunk{ChunkNumber}",
                        migrationId, entityType, chunkNumber);
                        
                    await _incrementEventsService.WriteChunkIncrementAsync(incrementEvent, cancellationToken);
                    
                    _logger.LogInformation("✅ INCREMENTAL-PROGRESS: Chunk increment event written successfully for {MigrationId}:{EntityType}:Chunk{ChunkNumber} " +
                                   "Success={Success}, Failed={Failed}, Skipped={Skipped}, Cancelled={Cancelled}",
                        migrationId, entityType, chunkNumber, successfulEntities, failedEntities, skippedEntities, cancelledEntities);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🚫 INCREMENTAL-PROGRESS: Incremental progress write cancelled for {MigrationId}:{EntityType}:Chunk{ChunkNumber}",
                        migrationId, entityType, chunkNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ INCREMENTAL-PROGRESS: Failed to write incremental progress for {MigrationId}:{EntityType}:Chunk{ChunkNumber} - migration continues. Error: {ErrorMessage}",
                        migrationId, entityType, chunkNumber, ex.Message);
                    // Don't throw - incremental progress failures should not break migration
                }
            }, cancellationToken);

            // Log the immediate call completion (not the actual write completion)
            _logger.LogDebug("🚀 Incremental progress write initiated for {MigrationId}:{EntityType}:Chunk{ChunkNumber}",
                migrationId, entityType, chunkNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initiate incremental progress write for {MigrationId}:{EntityType}:Chunk{ChunkNumber} - migration continues",
                migrationId, entityType, chunkNumber);
            // Don't throw - incremental progress failures should not break migration
        }
    }

    /// <inheritdoc />
    public async Task<MigrationProgress> GetLatestAggregatedProgressAsync(string migrationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

        try
        {
            // Start with cached progress as baseline
            var cachedProgress = await GetProgressAsync(migrationId, cancellationToken);
            
            // If no increment events service available, return cached progress
            if (_incrementEventsService == null)
            {
                _logger.LogDebug("IncrementEventsService not available - returning cached progress for {MigrationId}", migrationId);
                return cachedProgress;
            }

            // 🎯 OPTION 2: SELECTIVE REAL-TIME SYNC
            // Use chunkincrementevents aggregation ONLY for active migrations (last 10 minutes)
            // Use primary tables (migrations/entityprogress) for completed/cancelled/old migrations
            
            bool isActiveMigration = IsActiveMigration(cachedProgress);
            
            _logger.LogInformation("🔄 SELECTIVE-SYNC: Migration {MigrationId} Status='{Status}', LastUpdated={LastUpdated}, IsActive={IsActive}", 
                migrationId, cachedProgress.Status, cachedProgress.LastUpdated, isActiveMigration);
            
            if (isActiveMigration)
            {
                _logger.LogInformation("🔄 ACTIVE-MIGRATION: Using real-time chunkincrementevents aggregation for active migration {MigrationId} (Status: {Status}, LastUpdated: {LastUpdated})", 
                    migrationId, cachedProgress.Status, cachedProgress.LastUpdated);
                
                // Use real-time aggregation for active migrations
                try
                {
                    // ⚡ ACTIVE-ONLY: Get aggregated increment events for real-time data (enabled for active migrations)
                    _logger.LogInformation("🔄 INCREMENTAL-QUERY: Attempting to get aggregated progress from chunkincrementevents for {MigrationId}", migrationId);
                    var aggregatedProgress = await _incrementEventsService.GetAggregatedProgressAsync(migrationId, cancellationToken);
                    _logger.LogInformation("✅ INCREMENTAL-QUERY: Successfully retrieved aggregated progress from chunkincrementevents for {MigrationId}", migrationId);
                
                if (aggregatedProgress.Any())
                {
                    _logger.LogDebug("Found {EntityCount} entity types with incremental progress for migration {MigrationId}",
                        aggregatedProgress.Count, migrationId);

                    // Create enhanced progress by combining cached data with real-time aggregated data
                    var enhancedProgress = CreateProgressCopy(cachedProgress);
                    
                    // Update entity progress with real-time aggregated data
                    foreach (var (entityType, summary) in aggregatedProgress)
                    {
                        enhancedProgress.EntityProgress[entityType] = new EntityProgress
                        {
                            EntityType = entityType,
                            TotalCount = enhancedProgress.EntityProgress.ContainsKey(entityType) 
                                ? enhancedProgress.EntityProgress[entityType].TotalCount 
                                : summary.TotalProcessed, // Use processed count as fallback if total unknown
                            ProcessedCount = summary.TotalProcessed,
                            SuccessCount = summary.TotalSuccessful,
                            FailureCount = summary.TotalFailed,
                            SkippedCount = summary.TotalSkipped,
                            CancelledCount = summary.TotalCancelled,
                            ProgressPercentage = enhancedProgress.EntityProgress.ContainsKey(entityType) && 
                                                enhancedProgress.EntityProgress[entityType].TotalCount > 0
                                ? (double)summary.TotalProcessed / enhancedProgress.EntityProgress[entityType].TotalCount * 100.0
                                : summary.SuccessRate, // Use success rate as fallback
                            Status = summary.TotalProcessed > 0 ? "processing" : "pending",
                            StartTime = summary.FirstChunkStartTime ?? DateTime.UtcNow,
                            EndTime = summary.LastChunkEndTime,
                            ProcessingTime = summary.TotalProcessingTime
                        };
                    }

                    // Recalculate overall progress based on real-time data
                    lock (_lock)
                    {
                        CalculateOverallProgress(enhancedProgress);
                        enhancedProgress.LastUpdated = DateTime.UtcNow;
                        enhancedProgress.ElapsedTime = enhancedProgress.LastUpdated - enhancedProgress.StartTime;
                    }

                    _logger.LogDebug("✅ Enhanced progress with real-time data for migration {MigrationId}: {OverallProgress:F1}% complete",
                        migrationId, enhancedProgress.OverallProgressPercentage);

                    return enhancedProgress;
                }
                else
                {
                    _logger.LogDebug("No incremental progress data found for active migration {MigrationId} - returning cached progress", migrationId);
                    return cachedProgress;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ INCREMENTAL-QUERY-ERROR: Failed to get aggregated increment events for active migration {MigrationId}. " +
                    "Error Type: {ErrorType}, Message: {ErrorMessage} - returning cached progress", 
                    migrationId, ex.GetType().Name, ex.Message);
                return cachedProgress;
            }
            }
            else
            {
                // 🏎️ PERFORMANCE: Use cached/persisted progress for completed/cancelled/old migrations
                _logger.LogInformation("🏁 COMPLETED-MIGRATION: Using cached/persisted progress for completed migration {MigrationId} (Status: {Status}, LastUpdated: {LastUpdated})", 
                    migrationId, cachedProgress.Status, cachedProgress.LastUpdated);
                return cachedProgress;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("GetLatestAggregatedProgressAsync was cancelled for migration {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Determines if a migration is considered "active" and should use real-time chunkincrementevents aggregation
    /// </summary>
    /// <param name="progress">Migration progress to evaluate</param>
    /// <returns>True if migration is active and should use real-time aggregation</returns>
    private static bool IsActiveMigration(MigrationProgress progress)
    {
        // Consider migration active if:
        // 1. Status indicates active processing (running, processing, in-progress)
        // 2. Status indicates recent cancellation (cancelled within last 30 minutes)
        // 3. OR last updated within the last 10 minutes (recent activity)
        
        var activeStatuses = new[] { "running", "processing", "in-progress", "started" };
        bool hasActiveStatus = activeStatuses.Contains(progress.Status?.ToLowerInvariant());
        
        // 🚨 CRITICAL FIX: Recently cancelled migrations should use real-time aggregation 
        // to get accurate final counts from chunkincrementevents
        var cancelledStatuses = new[] { "cancelled", "canceled" };
        bool isRecentlyCancelled = cancelledStatuses.Contains(progress.Status?.ToLowerInvariant()) &&
                                 progress.LastUpdated > DateTime.UtcNow.AddMinutes(-30);
        
        bool hasRecentActivity = progress.LastUpdated > DateTime.UtcNow.AddMinutes(-10);
        
        return hasActiveStatus || isRecentlyCancelled || hasRecentActivity;
    }
} 