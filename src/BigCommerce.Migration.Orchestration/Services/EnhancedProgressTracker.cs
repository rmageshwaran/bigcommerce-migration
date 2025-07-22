using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System.Collections.Concurrent;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Enhanced progress tracker with detailed batch tracking and real-time context
/// Extends the existing ProgressTracker with granular progress information
/// </summary>
public class EnhancedProgressTracker : ProgressTracker
{
    private readonly IEnhancedMigrationSignalRService _enhancedSignalRService;
    private readonly ConcurrentDictionary<string, EnhancedMigrationProgress> _enhancedProgressCache;
    private readonly ConcurrentDictionary<string, Dictionary<string, List<double>>> _performanceHistory;
    private readonly ConcurrentDictionary<string, List<int>> _milestoneTracker;
    private readonly object _enhancedLock = new object();

    public EnhancedProgressTracker(
        ILogger<EnhancedProgressTracker> logger, 
        IEnhancedMigrationSignalRService enhancedSignalRService,
        IMigrationStorageService? storageService = null) 
        : base(logger, enhancedSignalRService, storageService)
    {
        _enhancedSignalRService = enhancedSignalRService ?? throw new ArgumentNullException(nameof(enhancedSignalRService));
        _enhancedProgressCache = new ConcurrentDictionary<string, EnhancedMigrationProgress>();
        _performanceHistory = new ConcurrentDictionary<string, Dictionary<string, List<double>>>();
        _milestoneTracker = new ConcurrentDictionary<string, List<int>>();
    }



    /// <summary>
    /// Updates batch progress with success/failure counts for detailed tracking
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="currentBatch">Current batch number</param>
    /// <param name="totalBatches">Total batches for this entity</param>
    /// <param name="batchSize">Size of current batch</param>
    /// <param name="processedInBatch">Entities processed in current batch</param>
    /// <param name="successfulInBatch">Successful entities in current batch</param>
    /// <param name="failedInBatch">Failed entities in current batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task UpdateBatchProgressAsync(
        string migrationId, 
        string entityType, 
        int currentBatch, 
        int totalBatches, 
        int batchSize, 
        int processedInBatch,
        int successfulInBatch,
        int failedInBatch,
        CancellationToken cancellationToken = default)
    {
        // Check cancellation token
        cancellationToken.ThrowIfCancellationRequested();

        // Validate parameters
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null, empty, or whitespace.", nameof(migrationId));

        if (processedInBatch > batchSize)
            throw new ArgumentException("Processed count cannot exceed batch size.", nameof(processedInBatch));

        if (successfulInBatch + failedInBatch > processedInBatch)
            throw new ArgumentException("Sum of successful and failed entities cannot exceed processed count.", nameof(successfulInBatch));

        try
        {
            var enhancedProgress = GetOrCreateEnhancedProgress(migrationId);
            
            lock (_enhancedLock)
            {
                UpdateCurrentProcessingContext(enhancedProgress, entityType, currentBatch, batchSize, processedInBatch);
                UpdateBatchProgressSummary(enhancedProgress, entityType, currentBatch, totalBatches, batchSize);
                UpdateRemainingWorkload(enhancedProgress);
                UpdatePerformanceMetrics(enhancedProgress, migrationId);
            }

            // Broadcast batch progress
            var batchDetails = new CurrentBatchDetails
            {
                BatchNumber = currentBatch,
                BatchSize = batchSize,
                ProcessedInBatch = processedInBatch,
                BatchProgressPercentage = batchSize > 0 ? (double)processedInBatch / batchSize * 100 : 0,
                BatchProcessingSpeed = 0.0,
                BatchElapsedTime = TimeSpan.Zero,
                EstimatedBatchTimeRemaining = TimeSpan.Zero
            };
            await _enhancedSignalRService.BroadcastBatchProgressAsync(migrationId, entityType, batchDetails, cancellationToken);

            // Broadcast detailed progress update
            await _enhancedSignalRService.BroadcastDetailedProgressAsync(migrationId, enhancedProgress, cancellationToken);
            
            // Broadcast current processing context
            await _enhancedSignalRService.BroadcastCurrentProcessingContextAsync(migrationId, enhancedProgress.CurrentProcessing, cancellationToken);
            
            // Check for milestones
            await CheckAndBroadcastMilestones(migrationId, enhancedProgress, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("SignalR"))
        {
            Logger.LogError(ex, "Failed to broadcast batch progress for migration {MigrationId}", migrationId);
            // Don't rethrow SignalR errors
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update batch progress for migration {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Updates batch progress with success/failure counts (simplified signature for testing)
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="batchNumber">Batch number</param>
    /// <param name="processedInBatch">Entities processed in current batch</param>
    /// <param name="successfulInBatch">Successful entities in current batch</param>
    /// <param name="failedInBatch">Failed entities in current batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task UpdateBatchProgressAsync(
        string migrationId, 
        string entityType, 
        int batchNumber, 
        int processedInBatch,
        int successfulInBatch,
        int failedInBatch,
        CancellationToken cancellationToken = default)
    {
        // Check cancellation token
        cancellationToken.ThrowIfCancellationRequested();

        // Validate parameters
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null, empty, or whitespace.", nameof(migrationId));

        if (successfulInBatch + failedInBatch > processedInBatch)
            throw new ArgumentException("Sum of successful and failed entities cannot exceed processed count.", nameof(successfulInBatch));

        try
        {
            var enhancedProgress = GetOrCreateEnhancedProgress(migrationId);
            
            // Use default values for missing parameters
            var batchSize = processedInBatch; // Assume batch size equals processed count for this simplified method
            var totalBatches = 1; // Default to 1 batch for this simplified method
            
            lock (_enhancedLock)
            {
                UpdateCurrentProcessingContext(enhancedProgress, entityType, batchNumber, batchSize, processedInBatch);
                UpdateBatchProgressSummary(enhancedProgress, entityType, batchNumber, totalBatches, batchSize);
                UpdateRemainingWorkload(enhancedProgress);
                UpdatePerformanceMetrics(enhancedProgress, migrationId);
            }

            // Broadcast batch progress
            var batchDetails = new CurrentBatchDetails
            {
                BatchNumber = batchNumber,
                BatchSize = batchSize,
                ProcessedInBatch = processedInBatch,
                BatchProgressPercentage = batchSize > 0 ? (double)processedInBatch / batchSize * 100 : 0,
                BatchProcessingSpeed = 0.0,
                BatchElapsedTime = TimeSpan.Zero,
                EstimatedBatchTimeRemaining = TimeSpan.Zero
            };
            await _enhancedSignalRService.BroadcastBatchProgressAsync(migrationId, entityType, batchDetails, cancellationToken);

            // Broadcast detailed progress update
            await _enhancedSignalRService.BroadcastDetailedProgressAsync(migrationId, enhancedProgress, cancellationToken);
            
            // Broadcast current processing context
            await _enhancedSignalRService.BroadcastCurrentProcessingContextAsync(migrationId, enhancedProgress.CurrentProcessing, cancellationToken);
            
            // Check for milestones
            await CheckAndBroadcastMilestones(migrationId, enhancedProgress, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("SignalR"))
        {
            Logger.LogError(ex, "Failed to broadcast batch progress for migration {MigrationId}", migrationId);
            // Don't rethrow SignalR errors
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update batch progress for migration {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Starts a new batch with detailed tracking
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="batchNumber">Batch number</param>
    /// <param name="batchSize">Batch size</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task StartBatchAsync(
        string migrationId, 
        string entityType, 
        int batchNumber, 
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var enhancedProgress = GetOrCreateEnhancedProgress(migrationId);
            
            var batchDetails = new CurrentBatchDetails
            {
                BatchNumber = batchNumber,
                BatchSize = batchSize,
                ProcessedInBatch = 0,
                BatchProgressPercentage = 0.0,
                BatchProcessingSpeed = 0.0,
                BatchElapsedTime = TimeSpan.Zero,
                EstimatedBatchTimeRemaining = TimeSpan.Zero
            };

            lock (_enhancedLock)
            {
                enhancedProgress.CurrentProcessing.CurrentEntity = entityType;
                enhancedProgress.CurrentProcessing.CurrentBatchNumber = batchNumber;
                enhancedProgress.CurrentProcessing.CurrentBatch = batchDetails;
                enhancedProgress.CurrentProcessing.CurrentPhase = $"Processing {entityType}";
                enhancedProgress.CurrentProcessing.CurrentActivity = $"Starting batch {batchNumber}";
                enhancedProgress.CurrentProcessing.CurrentBatchStartTime = DateTime.UtcNow;
            }

            // Broadcast batch started event
            await _enhancedSignalRService.BroadcastBatchStartedAsync(migrationId, entityType, batchDetails, cancellationToken);
            
            Logger.LogDebug("Started batch {BatchNumber} for {EntityType} in migration {MigrationId} ({BatchSize} entities)", 
                batchNumber, entityType, migrationId, batchSize);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to start batch for migration {MigrationId}", migrationId);
        }
    }

    /// <summary>
    /// Completes a batch with summary information
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="batchNumber">Completed batch number</param>
    /// <param name="entitiesProcessed">Total entities processed in batch</param>
    /// <param name="successfulEntities">Successful entities in batch</param>
    /// <param name="failedEntities">Failed entities in batch</param>
    /// <param name="processingDuration">Batch processing duration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task CompleteBatchAsync(
        string migrationId,
        string entityType,
        int batchNumber,
        int entitiesProcessed,
        int successfulEntities,
        int failedEntities,
        TimeSpan processingDuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var enhancedProgress = GetOrCreateEnhancedProgress(migrationId);
            
            // Calculate batch metrics
            var processingSpeed = processingDuration.TotalSeconds > 0 ? entitiesProcessed / processingDuration.TotalSeconds : 0;
            var errorRate = entitiesProcessed > 0 ? (double)failedEntities / entitiesProcessed : 0;
            
            var batchSummary = new BatchCompletionSummary
            {
                BatchNumber = batchNumber,
                EntitiesProcessed = entitiesProcessed,
                SuccessfulEntities = successfulEntities,
                FailedEntities = failedEntities,
                ProcessingDuration = processingDuration,
                ProcessingSpeed = processingSpeed,
                ErrorRate = errorRate,
                RemainingBatches = CalculateRemainingBatches(enhancedProgress, entityType),
                RemainingEntities = CalculateRemainingEntities(enhancedProgress, entityType)
            };

            lock (_enhancedLock)
            {
                // Update batch progress
                if (enhancedProgress.BatchProgress.EntityBatches.ContainsKey(entityType))
                {
                    enhancedProgress.BatchProgress.EntityBatches[entityType].CompletedBatches++;
                    enhancedProgress.BatchProgress.EntityBatches[entityType].CurrentBatch = batchNumber + 1;
                    enhancedProgress.BatchProgress.EntityBatches[entityType].RemainingBatches = Math.Max(0, 
                        enhancedProgress.BatchProgress.EntityBatches[entityType].TotalBatches - enhancedProgress.BatchProgress.EntityBatches[entityType].CompletedBatches);
                }

                // Update overall batch progress
                enhancedProgress.BatchProgress.CompletedBatches++;
                enhancedProgress.BatchProgress.RemainingBatches = Math.Max(0, enhancedProgress.BatchProgress.TotalBatches - enhancedProgress.BatchProgress.CompletedBatches);
                enhancedProgress.BatchProgress.BatchCompletionPercentage = enhancedProgress.BatchProgress.TotalBatches > 0 
                    ? (double)enhancedProgress.BatchProgress.CompletedBatches / enhancedProgress.BatchProgress.TotalBatches * 100 
                    : 0;

                // Update performance history
                RecordPerformanceMetric(migrationId, entityType, processingSpeed);
            }

            // Broadcast batch completed event
            await _enhancedSignalRService.BroadcastBatchCompletedAsync(migrationId, entityType, batchNumber, batchSummary, cancellationToken);
            
            // Update remaining workload
            await UpdateAndBroadcastRemainingWorkload(migrationId, enhancedProgress, cancellationToken);
            
            Logger.LogDebug("Completed batch {BatchNumber} for {EntityType} in migration {MigrationId}: {Successful}/{Total} successful", 
                batchNumber, entityType, migrationId, successfulEntities, entitiesProcessed);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to complete batch for migration {MigrationId}", migrationId);
        }
    }

    /// <summary>
    /// Updates the current processing context with real-time information
    /// </summary>
    private void UpdateCurrentProcessingContext(EnhancedMigrationProgress progress, string entityType, int currentBatch, int batchSize, int processedInBatch)
    {
        var context = progress.CurrentProcessing;
        context.CurrentEntity = entityType;
        context.CurrentBatchNumber = currentBatch;
        context.CurrentPhase = $"Processing {entityType}";
        context.CurrentActivity = $"Batch {currentBatch}: {processedInBatch}/{batchSize} entities";

        // Update current batch details
        var batchProgress = batchSize > 0 ? (double)processedInBatch / batchSize * 100 : 0;
        var batchElapsed = DateTime.UtcNow - context.CurrentBatchStartTime;
        var batchSpeed = batchElapsed.TotalSeconds > 0 ? processedInBatch / batchElapsed.TotalSeconds : 0;
        
        // Calculate estimated time remaining with bounds checking to prevent DateTime overflow
        TimeSpan estimatedTimeRemaining;
        if (batchSpeed > 0 && (batchSize - processedInBatch) > 0)
        {
            var secondsRemaining = (batchSize - processedInBatch) / batchSpeed;
            // Cap at 1 year to prevent DateTime overflow
            if (secondsRemaining > 31536000) // 365 days in seconds
            {
                estimatedTimeRemaining = TimeSpan.FromDays(365);
            }
            else
            {
                estimatedTimeRemaining = TimeSpan.FromSeconds(secondsRemaining);
            }
        }
        else
        {
            estimatedTimeRemaining = TimeSpan.Zero;
        }

        context.CurrentBatch.BatchNumber = currentBatch;
        context.CurrentBatch.BatchSize = batchSize;
        context.CurrentBatch.ProcessedInBatch = processedInBatch;
        context.CurrentBatch.BatchProgressPercentage = batchProgress;
        context.CurrentBatch.BatchElapsedTime = batchElapsed;
        context.CurrentBatch.BatchProcessingSpeed = batchSpeed;
        context.CurrentBatch.EstimatedBatchTimeRemaining = estimatedTimeRemaining;
        context.EstimatedBatchCompletion = DateTime.UtcNow.Add(estimatedTimeRemaining);
    }

    /// <summary>
    /// Updates the batch progress summary across all entities
    /// </summary>
    private void UpdateBatchProgressSummary(EnhancedMigrationProgress progress, string entityType, int currentBatch, int totalBatches, int batchSize)
    {
        // Initialize or update entity batch progress
        if (!progress.BatchProgress.EntityBatches.ContainsKey(entityType))
        {
            progress.BatchProgress.EntityBatches[entityType] = new EntityBatchProgress
            {
                EntityType = entityType,
                TotalBatches = totalBatches,
                CompletedBatches = 0,
                CurrentBatch = currentBatch,
                RemainingBatches = totalBatches,
                BatchSize = batchSize,
                CompletionPercentage = 0.0
            };

            // Update total batches count
            progress.BatchProgress.TotalBatches += totalBatches;
        }

        var entityBatch = progress.BatchProgress.EntityBatches[entityType];
        entityBatch.CurrentBatch = currentBatch;
        entityBatch.CompletionPercentage = totalBatches > 0 ? (double)currentBatch / totalBatches * 100 : 0;
        
        // Update overall batch progress
        var totalCompleted = progress.BatchProgress.EntityBatches.Values.Sum(eb => eb.CompletedBatches);
        var totalBatchesAll = progress.BatchProgress.EntityBatches.Values.Sum(eb => eb.TotalBatches);
        
        progress.BatchProgress.CompletedBatches = totalCompleted;
        progress.BatchProgress.TotalBatches = totalBatchesAll;
        progress.BatchProgress.RemainingBatches = totalBatchesAll - totalCompleted;
        progress.BatchProgress.BatchCompletionPercentage = totalBatchesAll > 0 ? (double)totalCompleted / totalBatchesAll * 100 : 0;
        
        // Count currently processing batches
        progress.BatchProgress.ProcessingBatches = progress.BatchProgress.EntityBatches.Values.Count(eb => 
            eb.CurrentBatch > 0 && eb.CurrentBatch <= eb.TotalBatches && eb.CompletedBatches < eb.TotalBatches);
    }

    /// <summary>
    /// Updates the remaining workload calculations
    /// </summary>
    private void UpdateRemainingWorkload(EnhancedMigrationProgress progress)
    {
        var remaining = progress.RemainingWork;
        
        // Calculate remaining entities by type
        remaining.RemainingByEntityType.Clear();
        remaining.RemainingBatchesByEntityType.Clear();
        
        int totalRemainingEntities = 0;
        int totalRemainingBatches = 0;
        
        foreach (var entityProgress in progress.EntityProgress)
        {
            var entityType = entityProgress.Key;
            var entityData = entityProgress.Value;
            
            var remainingForEntity = entityData.TotalCount - entityData.ProcessedCount;
            remaining.RemainingByEntityType[entityType] = remainingForEntity;
            totalRemainingEntities += remainingForEntity;
            
            if (progress.BatchProgress.EntityBatches.ContainsKey(entityType))
            {
                var remainingBatches = progress.BatchProgress.EntityBatches[entityType].RemainingBatches;
                remaining.RemainingBatchesByEntityType[entityType] = remainingBatches;
                totalRemainingBatches += remainingBatches;
            }
        }
        
        remaining.RemainingEntities = totalRemainingEntities;
        remaining.RemainingBatches = totalRemainingBatches;
        
        // Calculate estimated time remaining based on current performance
        if (progress.Performance.CurrentProcessingSpeed > 0)
        {
            remaining.EstimatedTimeRemaining = TimeSpan.FromSeconds(totalRemainingEntities / progress.Performance.CurrentProcessingSpeed);
        }
        else if (progress.EntitiesPerSecond > 0)
        {
            remaining.EstimatedTimeRemaining = TimeSpan.FromSeconds(totalRemainingEntities / progress.EntitiesPerSecond);
        }
        else
        {
            remaining.EstimatedTimeRemaining = TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Updates real-time performance metrics
    /// </summary>
    private void UpdatePerformanceMetrics(EnhancedMigrationProgress progress, string migrationId)
    {
        var metrics = progress.Performance;
        var history = GetPerformanceHistory(migrationId);
        
        // Calculate current processing speed
        metrics.CurrentProcessingSpeed = progress.EntitiesPerSecond;
        
        // Calculate average and peak speeds from history
        if (history.ContainsKey("overall") && history["overall"].Any())
        {
            var overallHistory = history["overall"];
            metrics.AverageProcessingSpeed = overallHistory.Average();
            metrics.PeakProcessingSpeed = overallHistory.Max();
            
            // Determine performance trend
            if (overallHistory.Count >= 10)
            {
                var recent = overallHistory.TakeLast(5).Average();
                var previous = overallHistory.SkipLast(5).TakeLast(5).Average();
                var change = (recent - previous) / previous;
                
                if (change > 0.1) metrics.PerformanceTrend = "improving";
                else if (change < -0.1) metrics.PerformanceTrend = "declining";
                else metrics.PerformanceTrend = "stable";
            }
        }
        
        metrics.CurrentErrorRate = progress.ErrorRate;
        metrics.LastCalculation = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a performance metric for trend analysis
    /// </summary>
    private void RecordPerformanceMetric(string migrationId, string entityType, double speed)
    {
        var history = _performanceHistory.GetOrAdd(migrationId, _ => new Dictionary<string, List<double>>());
        
        if (!history.ContainsKey(entityType))
        {
            history[entityType] = new List<double>();
        }
        
        if (!history.ContainsKey("overall"))
        {
            history["overall"] = new List<double>();
        }
        
        history[entityType].Add(speed);
        history["overall"].Add(speed);
        
        // Keep only last 100 measurements
        if (history[entityType].Count > 100)
        {
            history[entityType] = history[entityType].TakeLast(100).ToList();
        }
        
        if (history["overall"].Count > 100)
        {
            history["overall"] = history["overall"].TakeLast(100).ToList();
        }
    }

    /// <summary>
    /// Gets performance history for a migration
    /// </summary>
    private Dictionary<string, List<double>> GetPerformanceHistory(string migrationId)
    {
        return _performanceHistory.GetOrAdd(migrationId, _ => new Dictionary<string, List<double>>());
    }

    /// <summary>
    /// Gets or creates enhanced progress for a migration
    /// </summary>
    private EnhancedMigrationProgress GetOrCreateEnhancedProgress(string migrationId)
    {
        return _enhancedProgressCache.GetOrAdd(migrationId, _ =>
        {
            // Get base progress from parent class
            var baseProgress = GetProgressAsync(migrationId, CancellationToken.None).Result;
            
            return new EnhancedMigrationProgress
            {
                // Copy base properties
                MigrationId = baseProgress.MigrationId,
                Status = baseProgress.Status,
                StartTime = baseProgress.StartTime,
                LastUpdated = baseProgress.LastUpdated,
                ElapsedTime = baseProgress.ElapsedTime,
                EstimatedTimeRemaining = baseProgress.EstimatedTimeRemaining,
                TotalEntities = baseProgress.TotalEntities,
                ProcessedEntities = baseProgress.ProcessedEntities,
                SuccessfulEntities = baseProgress.SuccessfulEntities,
                FailedEntities = baseProgress.FailedEntities,
                OverallProgressPercentage = baseProgress.OverallProgressPercentage,
                EntityProgress = baseProgress.EntityProgress,
                CurrentPhase = baseProgress.CurrentPhase,
                CurrentEntity = baseProgress.CurrentEntity,
                EntitiesPerSecond = baseProgress.EntitiesPerSecond,
                ErrorRate = baseProgress.ErrorRate,
                
                // Initialize enhanced properties
                CurrentProcessing = new ProcessingContext(),
                BatchProgress = new BatchProgressSummary(),
                RemainingWork = new RemainingWorkload(),
                Performance = new RealTimeMetrics()
            };
        });
    }

    /// <summary>
    /// Calculates remaining batches for an entity
    /// </summary>
    private int CalculateRemainingBatches(EnhancedMigrationProgress progress, string entityType)
    {
        if (progress.BatchProgress.EntityBatches.ContainsKey(entityType))
        {
            return progress.BatchProgress.EntityBatches[entityType].RemainingBatches;
        }
        return 0;
    }

    /// <summary>
    /// Calculates remaining entities for an entity type
    /// </summary>
    private int CalculateRemainingEntities(EnhancedMigrationProgress progress, string entityType)
    {
        if (progress.EntityProgress.ContainsKey(entityType))
        {
            var entityProgress = progress.EntityProgress[entityType];
            return entityProgress.TotalCount - entityProgress.ProcessedCount;
        }
        return 0;
    }

    /// <summary>
    /// Updates and broadcasts remaining workload information
    /// </summary>
    private async Task UpdateAndBroadcastRemainingWorkload(string migrationId, EnhancedMigrationProgress progress, CancellationToken cancellationToken)
    {
        UpdateRemainingWorkload(progress);
        await _enhancedSignalRService.BroadcastRemainingWorkloadAsync(migrationId, progress.RemainingWork, cancellationToken);
    }

    /// <summary>
    /// Checks for milestone achievements and broadcasts them
    /// </summary>
    private async Task CheckAndBroadcastMilestones(string migrationId, EnhancedMigrationProgress progress, CancellationToken cancellationToken)
    {
        var currentPercentage = (int)Math.Floor(progress.OverallProgressPercentage);
        var milestones = new[] { 25, 50, 75, 90 };
        var achievedMilestones = _milestoneTracker.GetOrAdd(migrationId, _ => new List<int>());
        
        foreach (var milestone in milestones)
        {
            if (currentPercentage >= milestone && !achievedMilestones.Contains(milestone))
            {
                achievedMilestones.Add(milestone);
                
                var milestoneData = new MilestoneData
                {
                    Percentage = milestone,
                    TimeToMilestone = progress.ElapsedTime,
                    EntitiesProcessed = progress.ProcessedEntities,
                    AverageSpeed = progress.Performance.AverageProcessingSpeed,
                    EstimatedTimeToCompletion = progress.RemainingWork.EstimatedTimeRemaining,
                    Message = GetMilestoneMessage(milestone, progress),
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["entitiesRemaining"] = progress.RemainingWork.RemainingEntities,
                        ["batchesRemaining"] = progress.RemainingWork.RemainingBatches,
                        ["errorRate"] = progress.ErrorRate,
                        ["currentSpeed"] = progress.Performance.CurrentProcessingSpeed
                    }
                };
                
                await _enhancedSignalRService.BroadcastMigrationMilestoneAsync(migrationId, milestone, milestoneData, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Gets a friendly milestone message
    /// </summary>
    private string GetMilestoneMessage(int milestone, EnhancedMigrationProgress progress)
    {
        return milestone switch
        {
            25 => $"Quarter way there! {progress.ProcessedEntities:N0} entities migrated successfully.",
            50 => $"Halfway point reached! {progress.ProcessedEntities:N0} entities completed with {progress.Performance.AverageProcessingSpeed:F1} entities/sec average speed.",
            75 => $"Three quarters complete! Only {progress.RemainingWork.RemainingEntities:N0} entities remaining.",
            90 => $"Almost finished! Final {progress.RemainingWork.RemainingEntities:N0} entities in progress.",
            _ => $"{milestone}% milestone achieved!"
        };
    }

    /// <summary>
    /// Gets the protected Logger property from base class
    /// </summary>
    protected ILogger Logger => (ILogger)typeof(ProgressTracker)
        .GetField("_logger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?.GetValue(this) ?? throw new InvalidOperationException("Could not access base logger");
} 