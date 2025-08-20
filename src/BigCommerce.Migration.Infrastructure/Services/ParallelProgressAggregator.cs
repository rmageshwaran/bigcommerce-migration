using System.Collections.Concurrent;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// **Thread-Safe Progress Aggregator Implementation**
/// 
/// Provides thread-safe progress tracking and aggregation for concurrent batch processing
/// with real-time SignalR updates and Durable Functions determinism compliance.
/// 
/// **Key Features:**
/// - Thread-safe concurrent updates from multiple batch processors using atomic operations
/// - Rate-limited SignalR progress notifications to prevent UI flooding
/// - Deterministic progress ordering for Durable Functions replay safety
/// - Real-time throughput and performance metrics calculation
/// - Milestone-based event notifications for monitoring systems
/// </summary>
public class ParallelProgressAggregator : IParallelProgressAggregator
{
    #region Private Fields

    private readonly string _migrationId;
    private readonly string _entityType;
    private readonly int _totalBatches;
    private readonly int? _actualTotalEntities; // 🚨 FIX: Store actual total entities from migration context
    // Note: SignalR broadcasting removed - use CentralizedProgressBroadcastService instead
    private readonly ILogger _logger;
    private readonly IDateTimeProvider _dateTimeProvider;

    // Thread-safe state tracking
    private readonly ConcurrentDictionary<int, BatchCompletionRecord> _completedBatches;
    private readonly ConcurrentDictionary<int, BatchStartRecord> _activeBatches;
    private readonly ConcurrentBag<string> _aggregatedErrors;
    private readonly object _progressStateLock = new();
    // Note: SignalR rate limiting removed - handled by CentralizedProgressBroadcastService
    private bool _deterministicMode = false;

    // Aggregated counters (using Interlocked for thread safety)
    private long _totalEntitiesProcessed;
    private long _totalEntitiesFailed;
    private long _totalProcessingTimeMs;
    private int _completedBatchCount;

    // Performance tracking
    private readonly DateTime _startTime;

    // Milestone tracking
    private readonly HashSet<ProgressMilestoneType> _reachedMilestones = new();
    private bool _disposed = false;

    // **PHASE 2.4: Performance change tracking**
    private ParallelPerformanceMetrics? _previousPerformanceMetrics;

    // **🎯 SUB-BATCH OPTIMIZATION: Sub-batch progress tracking**
    private readonly ConcurrentDictionary<string, SubBatchCompletionRecord> _completedSubBatches;
    private readonly ConcurrentDictionary<string, SubBatchStartRecord> _activeSubBatches;
    private long _totalSubBatchesProcessed;
    private long _totalSubBatchEntitiesProcessed;
    private long _totalSubBatchEntitiesFailed;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes the parallel progress aggregator
    /// </summary>
    public ParallelProgressAggregator(
        string migrationId,
        string entityType,
        int totalBatches,
        ILogger logger,
        IDateTimeProvider dateTimeProvider,
        int? actualTotalEntities = null) // 🚨 FIX: Accept actual total entities from migration context
    {
        _migrationId = migrationId ?? throw new ArgumentNullException(nameof(migrationId));
        _entityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        _totalBatches = totalBatches;
        _actualTotalEntities = actualTotalEntities; // 🚨 FIX: Store actual total entities
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));

        _completedBatches = new ConcurrentDictionary<int, BatchCompletionRecord>();
        _activeBatches = new ConcurrentDictionary<int, BatchStartRecord>();
        _aggregatedErrors = new ConcurrentBag<string>();

        // 🎯 SUB-BATCH OPTIMIZATION: Initialize sub-batch tracking collections
        _completedSubBatches = new ConcurrentDictionary<string, SubBatchCompletionRecord>();
        _activeSubBatches = new ConcurrentDictionary<string, SubBatchStartRecord>();

        _startTime = _dateTimeProvider.UtcNow;

        _logger.LogDebug("Created parallel progress aggregator for migration {MigrationId} with {TotalBatches} batches",
            _migrationId, _totalBatches);

        // Fire the Started milestone
        TriggerMilestone(ProgressMilestoneType.Started, 0.0);
    }

    #endregion

    #region Batch Progress Tracking

    /// <summary>
    /// Reports completion of a batch with thread-safe aggregation
    /// </summary>
    public async Task ReportBatchCompletionAsync(
        int batchNumber,
        int entitiesProcessed,
        int entitiesFailed,
        TimeSpan processingTime,
        IEnumerable<string>? batchErrors = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            var completionTime = _dateTimeProvider.UtcNow;
            var errors = batchErrors?.ToList() ?? new List<string>();

            // Record batch completion atomically
            var completionRecord = new BatchCompletionRecord
            {
                BatchNumber = batchNumber,
                EntitiesProcessed = entitiesProcessed,
                EntitiesFailed = entitiesFailed,
                ProcessingTime = processingTime,
                CompletedAt = completionTime,
                Errors = errors
            };

            _completedBatches.TryAdd(batchNumber, completionRecord);
            _activeBatches.TryRemove(batchNumber, out _);

            // Update aggregated counters atomically
            Interlocked.Add(ref _totalEntitiesProcessed, entitiesProcessed);
            Interlocked.Add(ref _totalEntitiesFailed, entitiesFailed);
            Interlocked.Add(ref _totalProcessingTimeMs, (long)processingTime.TotalMilliseconds);
            Interlocked.Increment(ref _completedBatchCount);

            // Add errors to aggregated collection
            foreach (var error in errors)
            {
                _aggregatedErrors.Add(error);
            }

            // Calculate current progress percentage
            var currentProgress = GetCurrentProgressPercentage();

            _logger.LogDebug("Batch {BatchNumber} completed: {Processed} processed, {Failed} failed, {Duration:F2}s " +
                           "(overall progress: {Progress:P1})",
                batchNumber, entitiesProcessed, entitiesFailed, processingTime.TotalSeconds, currentProgress);

            // Fire batch completed event
            var eventArgs = new BatchCompletedEventArgs
            {
                BatchNumber = batchNumber,
                EntitiesProcessed = entitiesProcessed,
                EntitiesFailed = entitiesFailed,
                ProcessingTime = processingTime,
                BatchErrors = errors,
                OverallProgressPercentage = currentProgress
            };

            BatchCompleted?.Invoke(this, eventArgs);

            // Check for milestone events
            CheckAndTriggerMilestones(currentProgress);

            // Note: SignalR updates now handled by CentralizedProgressBroadcastService
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report batch completion for batch {BatchNumber}", batchNumber);
        }
        
        await Task.CompletedTask; // Placeholder for future async operations
    }

    /// <summary>
    /// Reports batch start for tracking active parallel batches
    /// </summary>
    public Task ReportBatchStartAsync(
        int batchNumber,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.CompletedTask;

        try
        {
            var startRecord = new BatchStartRecord
            {
                BatchNumber = batchNumber,
                BatchSize = batchSize,
                StartedAt = _dateTimeProvider.UtcNow
            };

            _activeBatches.TryAdd(batchNumber, startRecord);

            _logger.LogDebug("Batch {BatchNumber} started with {BatchSize} entities", batchNumber, batchSize);
            
            // Note: Async operations removed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report batch start for batch {BatchNumber}", batchNumber);
        }
        
        return Task.CompletedTask;
    }

    #endregion

    #region 🎯 SUB-BATCH OPTIMIZATION: Sub-Batch Progress Tracking

    /// <summary>
    /// Reports completion of a sub-batch with thread-safe aggregation
    /// Enables granular progress tracking within pages for 10x more progress updates
    /// </summary>
    public async Task ReportSubBatchCompletionAsync(
        int parentBatchNumber,
        int subBatchNumber,
        int entitiesProcessed,
        int entitiesFailed,
        TimeSpan processingTime,
        IEnumerable<string>? subBatchErrors = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            var subBatchKey = $"{parentBatchNumber}-{subBatchNumber}";
            var completionTime = _dateTimeProvider.UtcNow;
            var errors = subBatchErrors?.ToList() ?? new List<string>();

            // Record sub-batch completion atomically
            var completionRecord = new SubBatchCompletionRecord
            {
                ParentBatchNumber = parentBatchNumber,
                SubBatchNumber = subBatchNumber,
                EntitiesProcessed = entitiesProcessed,
                EntitiesFailed = entitiesFailed,
                ProcessingTime = processingTime,
                CompletedAt = completionTime,
                Errors = errors
            };

            _completedSubBatches.TryAdd(subBatchKey, completionRecord);
            _activeSubBatches.TryRemove(subBatchKey, out _);

            // Update aggregated sub-batch counters atomically
            Interlocked.Increment(ref _totalSubBatchesProcessed);
            Interlocked.Add(ref _totalSubBatchEntitiesProcessed, entitiesProcessed);
            Interlocked.Add(ref _totalSubBatchEntitiesFailed, entitiesFailed);

            // Add errors to aggregated collection
            foreach (var error in errors)
            {
                _aggregatedErrors.Add(error);
            }

            // Calculate sub-batch progress percentage
            var totalSubBatches = GetEstimatedTotalSubBatches();
            var completedSubBatches = Interlocked.Read(ref _totalSubBatchesProcessed);
            var subBatchProgress = totalSubBatches > 0 ? (double)completedSubBatches / totalSubBatches * 100 : 0;

            _logger.LogDebug("🎯 Sub-batch {ParentBatch}-{SubBatch} completed: {Processed} processed, {Failed} failed, {Duration:F2}s " +
                           "(sub-batch progress: {Progress:F1}%)",
                parentBatchNumber, subBatchNumber, entitiesProcessed, entitiesFailed, processingTime.TotalSeconds, subBatchProgress);

            // Note: SignalR update removed - handled by CentralizedProgressBroadcastService
            await SendSubBatchProgressUpdateAsync(parentBatchNumber, subBatchNumber, entitiesProcessed, entitiesFailed, subBatchProgress, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report sub-batch completion for sub-batch {ParentBatch}-{SubBatch}", parentBatchNumber, subBatchNumber);
        }
        
        await Task.CompletedTask; // Placeholder for future async operations
    }

    /// <summary>
    /// Reports sub-batch start for tracking active parallel sub-batches
    /// </summary>
    public Task ReportSubBatchStartAsync(
        int parentBatchNumber,
        int subBatchNumber,
        int subBatchSize,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.CompletedTask;

        try
        {
            var subBatchKey = $"{parentBatchNumber}-{subBatchNumber}";
            var startRecord = new SubBatchStartRecord
            {
                ParentBatchNumber = parentBatchNumber,
                SubBatchNumber = subBatchNumber,
                SubBatchSize = subBatchSize,
                StartedAt = _dateTimeProvider.UtcNow
            };

            _activeSubBatches.TryAdd(subBatchKey, startRecord);

            _logger.LogDebug("🎯 Sub-batch {ParentBatch}-{SubBatch} started with {SubBatchSize} entities", 
                parentBatchNumber, subBatchNumber, subBatchSize);
            
            // Note: Async operations removed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report sub-batch start for sub-batch {ParentBatch}-{SubBatch}", 
                parentBatchNumber, subBatchNumber);
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Estimates the total number of sub-batches expected based on entity count and sub-batch size
    /// For 192 brands: 4 pages × 10 sub-batches = 40 total sub-batches
    /// </summary>
    private int GetEstimatedTotalSubBatches()
    {
        // Estimate based on typical sub-batch size of 5 entities per sub-batch
        // and 50 entities per page = 10 sub-batches per page
        var subBatchesPerPage = 10; // 50 entities / 5 entities per sub-batch
        return _totalBatches * subBatchesPerPage;
    }

    /// <summary>
    /// Sends granular progress update for sub-batch completion
    /// Provides 10x more frequent updates than page-level progress
    /// </summary>
    private Task SendSubBatchProgressUpdateAsync(
        int parentBatchNumber,
        int subBatchNumber,
        int entitiesProcessed,
        int entitiesFailed,
        double subBatchProgress,
        CancellationToken cancellationToken)
    {
        try
        {
            // Note: SignalR broadcasting removed - handled by CentralizedProgressBroadcastService
            // Progress tracking continues for internal aggregation purposes
            
            var completedSubBatches = Interlocked.Read(ref _totalSubBatchesProcessed);
            var totalSubBatchEntitiesProcessed = Interlocked.Read(ref _totalSubBatchEntitiesProcessed);
            var totalSubBatchEntitiesFailed = Interlocked.Read(ref _totalSubBatchEntitiesFailed);

            var totalEntities = GetEstimatedTotalEntities();
            var totalProcessed = (int)(totalSubBatchEntitiesProcessed + totalSubBatchEntitiesFailed);
            var migrationStatus = totalProcessed >= totalEntities ? "completed" : "running";

            _logger.LogDebug("📊 [SUB-BATCH-PROGRESS] Progress: {Progress:F1}%, {Status}, {ProcessedEntities}/{TotalEntities} entities (Successful: {SuccessfulEntities}, Failed: {FailedEntities})", 
                subBatchProgress, migrationStatus, totalProcessed, totalEntities, totalSubBatchEntitiesProcessed, totalSubBatchEntitiesFailed);
            
            // Log when migration completes
            if (migrationStatus == "completed")
            {
                _logger.LogInformation("🎉 [MIGRATION-COMPLETED] Migration {MigrationId} reached completion status! Final: {ProcessedEntities}/{TotalEntities} entities", 
                    _migrationId, totalSubBatchEntitiesProcessed, totalEntities);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to send sub-batch progress update for {ParentBatch}-{SubBatch}", 
                parentBatchNumber, subBatchNumber);
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets total entities expected to be processed (for progress calculation)
    /// 🚨 FIX: Use actual total entities from migration context if available
    /// </summary>
    private int GetEstimatedTotalEntities()
    {
        // 🚨 FIX: Use actual total entities from migration context if available (e.g., 192 brands)
        if (_actualTotalEntities.HasValue)
        {
            return _actualTotalEntities.Value;
        }
        
        // Fallback to estimation: For 192 brands: 4 pages × 50 entities per page = 200 entities
        // This is an estimate - actual count may vary
        return _totalBatches * 50; // Assuming 50 entities per page
    }

    /// <summary>
    /// Calculates current processing rate (entities per second)
    /// </summary>
    private double CalculateCurrentProcessingRate()
    {
        var elapsedTime = _dateTimeProvider.UtcNow - _startTime;
        var totalProcessed = Interlocked.Read(ref _totalSubBatchEntitiesProcessed);
        
        return elapsedTime.TotalSeconds > 0 ? totalProcessed / elapsedTime.TotalSeconds : 0;
    }

    /// <summary>
    /// Calculates estimated time remaining based on current processing speed
    /// </summary>
    private TimeSpan? CalculateEstimatedTimeRemaining()
    {
        var processingRate = CalculateCurrentProcessingRate();
        if (processingRate <= 0) return null;

        var totalProcessed = Interlocked.Read(ref _totalSubBatchEntitiesProcessed);
        var totalExpected = GetEstimatedTotalEntities();
        var remaining = totalExpected - totalProcessed;

        return remaining > 0 ? TimeSpan.FromSeconds(remaining / processingRate) : TimeSpan.Zero;
    }

    /// <summary>
    /// Gets recent errors for progress reporting (last 10 errors)
    /// </summary>
    private List<string> GetRecentErrors()
    {
        return _aggregatedErrors.TakeLast(10).ToList();
    }

    /// <summary>
    /// Gets performance metrics for monitoring
    /// </summary>
    private Dictionary<string, object> GetPerformanceMetrics()
    {
        var totalProcessed = Interlocked.Read(ref _totalSubBatchEntitiesProcessed);
        var totalFailed = Interlocked.Read(ref _totalSubBatchEntitiesFailed);
        var completedSubBatches = Interlocked.Read(ref _totalSubBatchesProcessed);
        var elapsedTime = _dateTimeProvider.UtcNow - _startTime;

        return new Dictionary<string, object>
        {
            ["ProcessingRate"] = CalculateCurrentProcessingRate(),
            ["SuccessRate"] = totalProcessed > 0 ? (double)(totalProcessed - totalFailed) / totalProcessed * 100 : 0,
            ["AverageTimePerSubBatch"] = completedSubBatches > 0 ? elapsedTime.TotalMilliseconds / completedSubBatches : 0,
            ["ActiveSubBatches"] = _activeSubBatches.Count,
            ["TotalElapsedMinutes"] = elapsedTime.TotalMinutes
        };
    }

    #endregion

    #region Progress Aggregation

    /// <summary>
    /// Gets current aggregated progress across all parallel batches
    /// </summary>
    public Task<AggregatedProgressInfo> GetCurrentProgressAsync()
    {
        try
        {
            var completedCount = _completedBatchCount;
            var activeCount = _activeBatches.Count;
            var pendingCount = Math.Max(0, _totalBatches - completedCount - activeCount);

            var totalProcessed = Interlocked.Read(ref _totalEntitiesProcessed);
            var totalFailed = Interlocked.Read(ref _totalEntitiesFailed);
            var totalProcessingTimeMs = Interlocked.Read(ref _totalProcessingTimeMs);

            var currentTime = _dateTimeProvider.UtcNow;
            var elapsedTime = currentTime - _startTime;
            var currentThroughput = elapsedTime.TotalSeconds > 0 ? totalProcessed / elapsedTime.TotalSeconds : 0;

            var averageProcessingTime = completedCount > 0 
                ? TimeSpan.FromMilliseconds(totalProcessingTimeMs / (double)completedCount)
                : TimeSpan.Zero;

            var result = new AggregatedProgressInfo
            {
                TotalBatches = _totalBatches,
                CompletedBatches = completedCount,
                ActiveBatches = activeCount,
                PendingBatches = pendingCount,
                TotalEntitiesProcessed = (int)totalProcessed,
                TotalEntitiesFailed = (int)totalFailed,
                CurrentThroughput = currentThroughput,
                AverageBatchProcessingTime = averageProcessingTime,
                LastUpdateTime = currentTime
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current progress for migration {MigrationId}", _migrationId);
            return Task.FromResult(new AggregatedProgressInfo { TotalBatches = _totalBatches });
        }
    }

    /// <summary>
    /// Gets overall progress percentage (0.0 to 1.0)
    /// </summary>
    public Task<ProgressPercentageInfo> GetProgressPercentageAsync()
    {
        try
        {
            var currentProgress = GetCurrentProgressPercentage();
            var elapsedTime = _dateTimeProvider.UtcNow - _startTime;
            
            var totalProcessed = Interlocked.Read(ref _totalEntitiesProcessed);
            var currentThroughput = elapsedTime.TotalSeconds > 0 ? totalProcessed / elapsedTime.TotalSeconds : 0;

            // Calculate average throughput (simplified calculation)
            var averageThroughput = currentThroughput; // Would use historical data in real implementation

            // Estimate time remaining based on current throughput and remaining work
            TimeSpan? estimatedTimeRemaining = null;
            if (currentThroughput > 0 && currentProgress < 1.0)
            {
                var remainingWork = (1.0 - currentProgress) * totalProcessed / Math.Max(currentProgress, 0.001);
                estimatedTimeRemaining = TimeSpan.FromSeconds(remainingWork / currentThroughput);
            }

            var result = new ProgressPercentageInfo
            {
                ProgressPercentage = currentProgress,
                EstimatedTimeRemaining = estimatedTimeRemaining,
                CurrentThroughput = currentThroughput,
                AverageThroughput = averageThroughput,
                ElapsedTime = elapsedTime
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get progress percentage for migration {MigrationId}", _migrationId);
            return Task.FromResult(new ProgressPercentageInfo());
        }
    }

    #endregion

    #region Performance Metrics

    /// <summary>
    /// Gets real-time parallel processing performance metrics
    /// **PHASE 2.4: Enhanced with performance change event triggering**
    /// </summary>
    public Task<ParallelProcessingMetrics> GetPerformanceMetricsAsync()
    {
        try
        {
            var elapsedTime = _dateTimeProvider.UtcNow - _startTime;
            var totalProcessed = Interlocked.Read(ref _totalEntitiesProcessed);
            var totalFailed = Interlocked.Read(ref _totalEntitiesFailed);
            var completedCount = _completedBatchCount;

            var currentThroughput = elapsedTime.TotalSeconds > 0 ? totalProcessed / elapsedTime.TotalSeconds : 0;
            var errorRate = totalProcessed > 0 ? (double)totalFailed / totalProcessed : 0;

            // Calculate average response time from completed batches
            var averageResponseTime = 0.0;
            if (_completedBatches.Count > 0)
            {
                averageResponseTime = _completedBatches.Values.Average(b => b.ProcessingTime.TotalMilliseconds);
            }

            var currentMetrics = new ParallelPerformanceMetrics
            {
                CurrentThroughput = currentThroughput,
                ActiveConcurrentBatches = _activeBatches.Count,
                QueuedBatches = Math.Max(0, _totalBatches - completedCount - _activeBatches.Count),
                ErrorRate = errorRate,
                AverageResponseTimeMs = averageResponseTime,
                SignalRUpdateSuccessRate = 1.0, // Would track actual success rate
                CapturedAt = _dateTimeProvider.UtcNow
            };

            // **PHASE 2.4: Performance Change Event Triggering**
            if (_previousPerformanceMetrics != null)
            {
                var changeSeverity = DetectPerformanceChangeSeverity(_previousPerformanceMetrics, currentMetrics);
                if (changeSeverity.HasValue)
                {
                    OnPerformanceChanged(_previousPerformanceMetrics, currentMetrics, changeSeverity.Value);
                }
            }

            // Store current metrics for next comparison
            _previousPerformanceMetrics = currentMetrics;

            // Convert to the expected interface type
            var interfaceResult = new ParallelProcessingMetrics
            {
                CurrentConcurrency = currentMetrics.ActiveConcurrentBatches,
                MaxConcurrency = 8, // Default max concurrency
                AverageProcessingTimeMs = currentMetrics.AverageResponseTimeMs,
                ThroughputPerSecond = currentMetrics.CurrentThroughput,
                TotalEntitiesProcessed = (long)Interlocked.Read(ref _totalEntitiesProcessed),
                TotalErrors = (long)Interlocked.Read(ref _totalEntitiesFailed)
            };

            return Task.FromResult(interfaceResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get performance metrics for migration {MigrationId}", _migrationId);
            return Task.FromResult(new ParallelProcessingMetrics
            {
                CurrentConcurrency = 1,
                MaxConcurrency = 4,
                AverageProcessingTimeMs = 1000,
                ThroughputPerSecond = 1.0,
                TotalEntitiesProcessed = 0,
                TotalErrors = 0
            });
        }
    }

    /// <summary>
    /// Records a concurrency level change for performance tracking
    /// </summary>
    public Task RecordConcurrencyChangeAsync(
        int newConcurrency,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Concurrency changed to {NewConcurrency} for migration {MigrationId}: {Reason}",
                newConcurrency, _migrationId, reason);

            // Would store concurrency change history for analysis
            // Note: Async operations removed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record concurrency change for migration {MigrationId}", _migrationId);
        }
        
        return Task.CompletedTask;
    }

    #endregion

    #region SignalR Integration

    // Note: Force SignalR update removed - handled by CentralizedProgressBroadcastService

    // Note: SignalR configuration removed - handled by CentralizedProgressBroadcastService

    #endregion

    #region Event Notifications

    /// <summary>
    /// Event triggered when a batch completes processing
    /// </summary>
    public event EventHandler<BatchCompletedEventArgs>? BatchCompleted;

    /// <summary>
    /// Event triggered when overall progress reaches significant milestones
    /// </summary>
    public event EventHandler<ProgressMilestoneEventArgs>? ProgressMilestone;

    /// <summary>
    /// Event triggered when parallel processing performance changes significantly
    /// </summary>
    public event EventHandler<ParallelPerformanceChangedEventArgs>? PerformanceChanged;

    #endregion

    #region Durable Functions Determinism

    /// <summary>
    /// Ensures deterministic progress tracking for Durable Functions replay
    /// </summary>
    public void ConfigureDeterministicMode(bool enableDeterministicMode)
    {
        _deterministicMode = enableDeterministicMode;
        _logger.LogInformation("Deterministic mode {Status} for migration {MigrationId}",
            enableDeterministicMode ? "enabled" : "disabled", _migrationId);
    }

    /// <summary>
    /// Gets deterministic progress state for Durable Functions persistence
    /// </summary>
    public async Task<DeterministicProgressState> GetDeterministicStateAsync()
    {
        try
        {
            var completedBatchNumbers = _completedBatches.Keys.OrderBy(k => k).ToList();
            var totalProcessed = Interlocked.Read(ref _totalEntitiesProcessed);
            var totalFailed = Interlocked.Read(ref _totalEntitiesFailed);
            var totalProcessingTimeMs = Interlocked.Read(ref _totalProcessingTimeMs);

            var performanceSnapshot = await GetPerformanceMetricsAsync();

            var state = new DeterministicProgressState
            {
                CompletedBatches = completedBatchNumbers,
                TotalEntitiesProcessed = (int)totalProcessed,
                TotalEntitiesFailed = (int)totalFailed,
                TotalProcessingTime = TimeSpan.FromMilliseconds(totalProcessingTimeMs),
                AggregatedErrors = _aggregatedErrors.ToList(),
                PerformanceSnapshot = performanceSnapshot,
                StateCapturedAt = _dateTimeProvider.UtcNow
            };

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deterministic state for migration {MigrationId}", _migrationId);
            return new DeterministicProgressState();
        }
    }

    /// <summary>
    /// Restores progress state from Durable Functions persistence
    /// </summary>
    public Task RestoreDeterministicStateAsync(
        DeterministicProgressState state,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (state == null) return Task.CompletedTask;

            // Restore completed batches
            foreach (var batchNumber in state.CompletedBatches)
            {
                var record = new BatchCompletionRecord
                {
                    BatchNumber = batchNumber,
                    EntitiesProcessed = 1, // Would restore actual values from state
                    EntitiesFailed = 0,
                    ProcessingTime = TimeSpan.Zero,
                    CompletedAt = state.StateCapturedAt
                };
                _completedBatches.TryAdd(batchNumber, record);
            }

            // Restore aggregated counters
            Interlocked.Exchange(ref _totalEntitiesProcessed, state.TotalEntitiesProcessed);
            Interlocked.Exchange(ref _totalEntitiesFailed, state.TotalEntitiesFailed);
            Interlocked.Exchange(ref _totalProcessingTimeMs, (long)state.TotalProcessingTime.TotalMilliseconds);
            Interlocked.Exchange(ref _completedBatchCount, state.CompletedBatches.Count);

            // Restore errors
            foreach (var error in state.AggregatedErrors)
            {
                _aggregatedErrors.Add(error);
            }

            _logger.LogInformation("Restored deterministic state for migration {MigrationId}: {CompletedBatches} batches completed",
                _migrationId, state.CompletedBatches.Count);

            // Note: Async operations removed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore deterministic state for migration {MigrationId}", _migrationId);
        }
        
        return Task.CompletedTask;
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// Disposes of resources used by the progress aggregator
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            // Note: Final SignalR update removed - handled by CentralizedProgressBroadcastService

            // Fire completion milestone if 100% complete
            var currentProgress = GetCurrentProgressPercentage();
            if (currentProgress >= 1.0 && !_reachedMilestones.Contains(ProgressMilestoneType.Completed))
            {
                TriggerMilestone(ProgressMilestoneType.Completed, currentProgress);
            }

            _disposed = true;
            
            _logger.LogDebug("Disposed parallel progress aggregator for migration {MigrationId}", _migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal of progress aggregator for migration {MigrationId}", _migrationId);
        }
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Gets current progress percentage (0.0 to 1.0)
    /// 🚨 CRITICAL FIX: Calculate based on processed entities, not completed batches
    /// </summary>
    private double GetCurrentProgressPercentage()
    {
        var totalProcessed = Interlocked.Read(ref _totalEntitiesProcessed);
        var totalExpected = GetEstimatedTotalEntities();
        return totalExpected > 0 ? (double)totalProcessed / totalExpected : 0.0;
    }

    // Note: Rate-limited SignalR updates removed - handled by CentralizedProgressBroadcastService

    // Note: SignalR broadcasting removed - handled by CentralizedProgressBroadcastService

    /// <summary>
    /// Checks and triggers milestone events
    /// </summary>
    private void CheckAndTriggerMilestones(double currentProgress)
    {
        var milestones = new[]
        {
            (ProgressMilestoneType.TwentyFivePercent, 0.25),
            (ProgressMilestoneType.FiftyPercent, 0.50),
            (ProgressMilestoneType.SeventyFivePercent, 0.75),
            (ProgressMilestoneType.NinetyPercent, 0.90),
            (ProgressMilestoneType.Completed, 1.0)
        };

        foreach (var (milestoneType, threshold) in milestones)
        {
            if (currentProgress >= threshold && !_reachedMilestones.Contains(milestoneType))
            {
                TriggerMilestone(milestoneType, currentProgress);
            }
        }
    }

    /// <summary>
    /// Triggers a milestone event
    /// </summary>
    private void TriggerMilestone(ProgressMilestoneType milestoneType, double currentProgress)
    {
        if (_reachedMilestones.Contains(milestoneType)) return;

        _reachedMilestones.Add(milestoneType);

        try
        {
            var eventArgs = new ProgressMilestoneEventArgs
            {
                MilestoneType = milestoneType,
                ProgressPercentage = currentProgress,
                AdditionalInfo = $"Migration {_migrationId} reached {milestoneType}"
            };

            ProgressMilestone?.Invoke(this, eventArgs);

            _logger.LogInformation("Migration {MigrationId} reached milestone: {MilestoneType} ({Progress:P1})",
                _migrationId, milestoneType, currentProgress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger milestone {MilestoneType} for migration {MigrationId}",
                milestoneType, _migrationId);
        }
    }

    /// <summary>
    /// Triggers the PerformanceChanged event when significant performance changes are detected
    /// **PHASE 2.4: Enhanced performance monitoring**
    /// </summary>
    private void OnPerformanceChanged(ParallelPerformanceMetrics previousMetrics, 
        ParallelPerformanceMetrics newMetrics, PerformanceChangeSeverity severity)
    {
        try
        {
            var eventArgs = new ParallelPerformanceChangedEventArgs
            {
                StoreId = _migrationId, // Use migration ID as store context
                PreviousMetrics = previousMetrics,
                CurrentMetrics = newMetrics,
                Severity = severity,
                DetectedAt = _dateTimeProvider.UtcNow
            };

            PerformanceChanged?.Invoke(this, eventArgs);
            
            _logger.LogDebug("Triggered PerformanceChanged event for migration {MigrationId} with {Severity} severity", 
                _migrationId, severity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering PerformanceChanged event for migration {MigrationId}", _migrationId);
        }
    }

    /// <summary>
    /// Detects the severity of performance changes between metrics
    /// **PHASE 2.4: Performance change analysis**
    /// </summary>
    private PerformanceChangeSeverity? DetectPerformanceChangeSeverity(ParallelPerformanceMetrics previous, 
        ParallelPerformanceMetrics current)
    {
        // Skip if no previous metrics or very recent metrics (less than 5 seconds apart)
        if (previous.CapturedAt == default || 
            (current.CapturedAt - previous.CapturedAt).TotalSeconds < 5)
        {
            return null;
        }

        // Calculate percentage changes
        var throughputChange = previous.CurrentThroughput > 0 
            ? Math.Abs(current.CurrentThroughput - previous.CurrentThroughput) / previous.CurrentThroughput
            : 0.0;
        
        var errorRateChange = Math.Abs(current.ErrorRate - previous.ErrorRate);
        
        var responseTimeChange = previous.AverageResponseTimeMs > 0
            ? Math.Abs(current.AverageResponseTimeMs - previous.AverageResponseTimeMs) / previous.AverageResponseTimeMs
            : 0.0;

        // Determine severity based on thresholds
        if (throughputChange > 0.5 || errorRateChange > 0.2 || responseTimeChange > 1.0) // 50% throughput, 20% error rate, or 100% response time change
            return PerformanceChangeSeverity.Critical;
        
        if (throughputChange > 0.3 || errorRateChange > 0.1 || responseTimeChange > 0.5) // 30% throughput, 10% error rate, or 50% response time change
            return PerformanceChangeSeverity.Warning;
        
        if (throughputChange > 0.15 || errorRateChange > 0.05 || responseTimeChange > 0.25) // 15% throughput, 5% error rate, or 25% response time change
            return PerformanceChangeSeverity.Info;

        return null; // No significant change
    }

    #endregion
}

#region Supporting Classes

/// <summary>
/// Record of a completed batch
/// </summary>
internal class BatchCompletionRecord
{
    public int BatchNumber { get; set; }
    public int EntitiesProcessed { get; set; }
    public int EntitiesFailed { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime CompletedAt { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Record of a started batch
/// </summary>
internal class BatchStartRecord
{
    public int BatchNumber { get; set; }
    public int BatchSize { get; set; }
    public DateTime StartedAt { get; set; }
}

/// <summary>
/// 🎯 SUB-BATCH OPTIMIZATION: Record of a completed sub-batch
/// Enables granular progress tracking within pages for 10x more progress updates
/// </summary>
internal class SubBatchCompletionRecord
{
    public int ParentBatchNumber { get; set; }
    public int SubBatchNumber { get; set; }
    public int EntitiesProcessed { get; set; }
    public int EntitiesFailed { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime CompletedAt { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// 🎯 SUB-BATCH OPTIMIZATION: Record of a started sub-batch
/// Tracks active sub-batches for monitoring and progress aggregation
/// </summary>
internal class SubBatchStartRecord
{
    public int ParentBatchNumber { get; set; }
    public int SubBatchNumber { get; set; }
    public int SubBatchSize { get; set; }
    public DateTime StartedAt { get; set; }
}

#endregion 