using System.Collections.Concurrent;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Pipeline Progress Aggregator (Tier 1) Implementation
/// 
/// Provides comprehensive entity progress tracking within individual processing chunks.
/// Designed for real-time sub-entity progress visibility during parallel processing.
/// 
/// **Dual-Tier Architecture Integration:**
/// - Tier 1: Real-time sub-entity progress with 250ms broadcasting
/// - Automatic bridge to UniversalMigrationProgressAggregator (Tier 2)
/// - Thread-safe updates from parallel sub-entity processors
/// 
/// **Key Features:**
/// - Individual entity counts: "Options: 150/200 (75%) 15.2/sec"
/// - Timer-based broadcasting for optimal UX
/// - Seamless bridge to ecosystem-wide progress tracking
/// </summary>
public class PipelineProgressAggregator : IPipelineProgressAggregator
{
    #region Private Fields

    private readonly string _migrationId;
    private readonly string _parentEntityType;
    private readonly ILogger<PipelineProgressAggregator> _logger;
    private readonly ISignalREventFactory _signalREventFactory;
    private readonly IProgressEventPublisher _progressEventPublisher;

    // Thread-safe sub-entity progress tracking
    private readonly ConcurrentDictionary<string, ComprehensiveEntityProgress> _subEntityProgress = new();
    
    // Universal Aggregator bridge
    private IUniversalMigrationProgressAggregator? _universalAggregator;
    private string _universalMigrationId = string.Empty;
    private string _universalParentEntityType = string.Empty;

    // Broadcasting management
    private CancellationTokenSource? _broadcastCancellationTokenSource;
    private Task? _broadcastTask;

    private volatile bool _disposed = false;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the PipelineProgressAggregator
    /// </summary>
    public PipelineProgressAggregator(
        string migrationId,
        string parentEntityType,
        ILogger<PipelineProgressAggregator> logger,
        ISignalREventFactory signalREventFactory,
        IProgressEventPublisher progressEventPublisher)
    {
        _migrationId = migrationId ?? throw new ArgumentNullException(nameof(migrationId));
        _parentEntityType = parentEntityType ?? throw new ArgumentNullException(nameof(parentEntityType));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));

        _logger.LogInformation("🔧 [PIPELINE-AGGREGATOR] Created for migration {MigrationId}, parent entity {ParentEntityType}",
            migrationId, parentEntityType);
    }

    #endregion

    #region Comprehensive Entity Progress Tracking (Tier 1)

    /// <summary>
    /// Updates progress for a specific sub-entity type within the pipeline
    /// Used by parallel processors for options, modifiers, images, reviews
    /// </summary>
    public async Task UpdateSubEntityProgressAsync(
        string entityType,
        SubEntityProgressUpdate progress,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            // Update sub-entity progress atomically
            var comprehensiveProgress = _subEntityProgress.AddOrUpdate(entityType, 
                // Create new progress entry
                new ComprehensiveEntityProgress
                {
                    ParentEntityType = _parentEntityType,
                    EntityType = entityType,
                    TotalCount = progress.ProcessedCount, // Will be updated as we discover more
                    ProcessedCount = progress.ProcessedCount,
                    SuccessCount = progress.SuccessCount,
                    FailureCount = progress.FailureCount,
                    SkippedCount = progress.SkippedCount,  // 🚨 FIX: Include SkippedCount
                    CancelledCount = progress.CancelledCount,  // 🚨 CANCELLATION FIX: Include CancelledCount
                    Status = progress.Status,
                    ProcessingChannel = progress.ProcessingChannel,
                    ActiveChannels = 1,
                    ProcessingTime = progress.ProcessingTime,
                    ThroughputPerSecond = CalculateThroughput(progress.ProcessedCount, progress.ProcessingTime)
                },
                // Update existing progress entry
                (key, existing) => 
                {
                    existing.TotalCount = Math.Max(existing.TotalCount, existing.ProcessedCount + progress.ProcessedCount);
                    existing.ProcessedCount += progress.ProcessedCount;
                    existing.SuccessCount += progress.SuccessCount;
                    existing.FailureCount += progress.FailureCount;
                    existing.SkippedCount += progress.SkippedCount;  // 🚨 FIX: Include SkippedCount
                    existing.CancelledCount += progress.CancelledCount;  // 🚨 CANCELLATION FIX: Include CancelledCount
                    existing.Status = progress.Status;
                    existing.ProcessingChannel = progress.ProcessingChannel;
                    existing.ProcessingTime = existing.ProcessingTime?.Add(progress.ProcessingTime) ?? progress.ProcessingTime;
                    existing.ThroughputPerSecond = CalculateThroughput(existing.ProcessedCount, existing.ProcessingTime ?? TimeSpan.Zero);
                    return existing;
                });

            _logger.LogDebug("⚡ [PIPELINE-AGGREGATOR] Updated {EntityType} progress: {ProcessedCount}/{TotalCount} ({ProgressPercentage:P1}) - Channel: {ProcessingChannel}",
                entityType, comprehensiveProgress.ProcessedCount, comprehensiveProgress.TotalCount, 
                comprehensiveProgress.ProgressPercentage, progress.ProcessingChannel);

            // Bridge to Universal Aggregator (Tier 1 → Tier 2)
            if (_universalAggregator != null)
            {
                await _universalAggregator.UpdateComprehensiveEntityProgressAsync(
                    _universalMigrationId,
                    _universalParentEntityType,
                    entityType,
                    comprehensiveProgress,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PIPELINE-AGGREGATOR] Failed to update sub-entity progress for {EntityType} in migration {MigrationId}",
                entityType, _migrationId);
        }
    }

    #endregion

    #region Real-time Broadcasting (Tier 1)

    /// <summary>
    /// Starts real-time progress broadcasting with 250ms intervals
    /// Publishes individual sub-entity progress events for granular visibility
    /// </summary>
    public async Task StartRealtimeBroadcastingAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            // Stop existing broadcasting if running
            await StopRealtimeBroadcastingAsync();

            // Start new broadcasting
            _broadcastCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _broadcastTask = BroadcastProgressContinuouslyAsync(_broadcastCancellationTokenSource.Token);

            _logger.LogInformation("📡 [PIPELINE-AGGREGATOR] Started real-time broadcasting for migration {MigrationId} with 250ms intervals", _migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PIPELINE-AGGREGATOR] Failed to start real-time broadcasting for migration {MigrationId}", _migrationId);
        }
    }

    /// <summary>
    /// Stops real-time broadcasting
    /// </summary>
    public async Task StopRealtimeBroadcastingAsync()
    {
        try
        {
            // Cancel broadcasting
            _broadcastCancellationTokenSource?.Cancel();

            // Wait for task completion
            if (_broadcastTask != null)
            {
                try
                {
                    await _broadcastTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }
            }

            // Dispose resources
            _broadcastCancellationTokenSource?.Dispose();
            _broadcastCancellationTokenSource = null;
            _broadcastTask = null;

            _logger.LogInformation("⏹️ [PIPELINE-AGGREGATOR] Stopped real-time broadcasting for migration {MigrationId}", _migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PIPELINE-AGGREGATOR] Failed to stop real-time broadcasting for migration {MigrationId}", _migrationId);
        }
    }

    #endregion

    #region Bridge to Universal Aggregator (Tier 1 → Tier 2)

    /// <summary>
    /// Gets current pipeline progress summary for bridge to Universal Aggregator
    /// Aggregates all sub-entity progress into comprehensive summary
    /// </summary>
    public Task<PipelineProgressSummary> GetPipelineProgressSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromResult(new PipelineProgressSummary { MigrationId = _migrationId, ParentEntityType = _parentEntityType });

        try
        {
            var summary = new PipelineProgressSummary
            {
                MigrationId = _migrationId,
                ParentEntityType = _parentEntityType,
                SubEntityProgress = _subEntityProgress.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                ActiveChannels = _subEntityProgress.Values.Sum(p => p.ActiveChannels),
                TotalProcessingTime = TimeSpan.FromMilliseconds(_subEntityProgress.Values.Sum(p => p.ProcessingTime?.TotalMilliseconds ?? 0)),
                Timestamp = DateTime.UtcNow
            };

            // Determine overall status
            var allStatuses = _subEntityProgress.Values.Select(p => p.Status).Distinct().ToList();
            if (allStatuses.All(s => s == "completed"))
                summary.Status = "completed";
            else if (allStatuses.Any(s => s == "failed"))
                summary.Status = "failed";
            else
                summary.Status = "processing";

            return Task.FromResult(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PIPELINE-AGGREGATOR] Failed to get pipeline progress summary for migration {MigrationId}", _migrationId);
            return Task.FromResult(new PipelineProgressSummary { MigrationId = _migrationId, ParentEntityType = _parentEntityType });
        }
    }

    /// <summary>
    /// Configures automatic bridge to Universal Aggregator
    /// Enables seamless Tier 1 → Tier 2 progress flow
    /// </summary>
    public void ConfigureUniversalAggregatorBridge(
        IUniversalMigrationProgressAggregator universalAggregator,
        string migrationId,
        string parentEntityType)
    {
        _universalAggregator = universalAggregator ?? throw new ArgumentNullException(nameof(universalAggregator));
        _universalMigrationId = migrationId ?? throw new ArgumentNullException(nameof(migrationId));
        _universalParentEntityType = parentEntityType ?? throw new ArgumentNullException(nameof(parentEntityType));

        _logger.LogInformation("🌉 [PIPELINE-AGGREGATOR] Configured Universal Aggregator bridge: {MigrationId} → {ParentEntityType}",
            migrationId, parentEntityType);
    }

    #endregion

    #region Private Broadcasting Methods

    private async Task BroadcastProgressContinuouslyAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                await BroadcastComprehensiveProgressAsync(cancellationToken);
                await Task.Delay(250, cancellationToken); // 250ms intervals as per document
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PIPELINE-AGGREGATOR] Broadcasting loop failed for migration {MigrationId}", _migrationId);
        }
    }

    private async Task BroadcastComprehensiveProgressAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Broadcast individual sub-entity progress events
            foreach (var (entityType, progress) in _subEntityProgress)
            {
                // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation
                var progressEvent = _signalREventFactory.CreateEntityProgress(_migrationId, new EntityProgressOptions
                {
                    EntityType = $"{_parentEntityType}.{entityType}", // e.g., "enhanced-products.options"
                    TotalCount = progress.TotalCount,
                    ProcessedCount = progress.ProcessedCount,
                    Status = progress.Status,
                    ProcessingTime = progress.ProcessingTime,
                    // SuccessCount and FailureCount are calculated by the factory
                });

                await _progressEventPublisher.PublishEntityProgressAsync(progressEvent, cancellationToken);

                _logger.LogDebug("📡 [PIPELINE-AGGREGATOR] Broadcasted {EntityType} progress: {ProcessedCount}/{TotalCount} ({ThroughputPerSecond:F1}/sec)",
                    entityType, progress.ProcessedCount, progress.TotalCount, progress.ThroughputPerSecond);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PIPELINE-AGGREGATOR] Failed to broadcast comprehensive progress for migration {MigrationId}", _migrationId);
        }
    }

    #endregion

    #region Helper Methods

    private static double CalculateThroughput(int processedCount, TimeSpan processingTime)
    {
        if (processingTime.TotalSeconds <= 0) return 0.0;
        return processedCount / processingTime.TotalSeconds;
    }

    #endregion

    #region Disposal

    /// <summary>
    /// Disposes the pipeline aggregator and stops real-time broadcasting
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop broadcasting
        Task.Run(async () => await StopRealtimeBroadcastingAsync()).Wait(TimeSpan.FromSeconds(2));

        // Clear progress data
        _subEntityProgress.Clear();

        _logger.LogInformation("🧹 [PIPELINE-AGGREGATOR] Disposed successfully for migration {MigrationId}", _migrationId);
    }

    #endregion
}