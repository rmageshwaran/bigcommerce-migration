using System.Collections.Concurrent;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Universal Migration Progress Aggregator (Tier 2) Implementation
/// 
/// Provides ecosystem-wide progress tracking across all entity types and processing modes.
/// Serves as the central hub for migration progress visibility in dashboards.
/// 
/// **Dual-Tier Architecture Integration:**
/// - Receives comprehensive entity updates from PipelineProgressAggregator (Tier 1)
/// - Manages primary entity progress from standard processing
/// - Broadcasts unified progress events for complete migration visibility
/// 
/// **Thread Safety:**
/// - Uses ConcurrentDictionary for thread-safe concurrent updates
/// - Atomic progress calculations with proper locking
/// - Safe for use across multiple parallel processing streams
/// </summary>
public class UniversalMigrationProgressAggregator : IUniversalMigrationProgressAggregator
{
    #region Private Fields

    private readonly ILogger<UniversalMigrationProgressAggregator> _logger;
    private readonly ISignalREventFactory _signalREventFactory;
    private readonly IProgressEventPublisher _progressEventPublisher;

    // Thread-safe progress tracking
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, EntityTypeProgress>> _primaryProgress = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, EntityTypeProgress>> _comprehensiveProgress = new();
    
    // Broadcasting management
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _broadcastCancellationTokens = new();
    private readonly ConcurrentDictionary<string, Task> _broadcastTasks = new();

    private volatile bool _disposed = false;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the UniversalMigrationProgressAggregator
    /// </summary>
    public UniversalMigrationProgressAggregator(
        ILogger<UniversalMigrationProgressAggregator> logger,
        ISignalREventFactory signalREventFactory,
        IProgressEventPublisher progressEventPublisher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
    }

    #endregion

    #region Primary Entity Progress (Tier 2 - Ecosystem Level)

    /// <summary>
    /// Updates progress for primary entity types (brands, products, enhanced-products)
    /// Used by standard processing and comprehensive pipeline completion
    /// </summary>
    public async Task UpdatePrimaryEntityProgressAsync(
        string migrationId,
        string entityType,
        PrimaryEntityProgress progress,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            // Get or create migration progress tracking
            var migrationProgress = _primaryProgress.GetOrAdd(migrationId, _ => new ConcurrentDictionary<string, EntityTypeProgress>());
            
            // Convert PrimaryEntityProgress to EntityTypeProgress
            var entityProgress = new EntityTypeProgress
            {
                EntityType = progress.EntityType,
                TotalEntities = progress.TotalCount,
                ProcessedEntities = progress.ProcessedCount,
                SuccessfulEntities = progress.SuccessCount,
                FailedEntities = progress.FailureCount,
                SkippedEntities = progress.SkippedCount,  // 🚨 FIX: Include SkippedEntities
                CancelledEntities = progress.CancelledCount,  // 🚨 CANCELLATION FIX: Include CancelledEntities
                Status = progress.Status,
                StartTime = DateTime.UtcNow.Subtract(progress.ProcessingTime ?? TimeSpan.Zero),
                CompletionTime = progress.Status == "completed" ? DateTime.UtcNow : null
            };
            
            // Update primary entity progress atomically
            migrationProgress.AddOrUpdate(entityType, entityProgress, (key, existing) => 
            {
                // Merge progress updates
                existing.TotalEntities = Math.Max(existing.TotalEntities, entityProgress.TotalEntities);
                existing.ProcessedEntities = entityProgress.ProcessedEntities;
                existing.SuccessfulEntities = entityProgress.SuccessfulEntities;
                existing.FailedEntities = entityProgress.FailedEntities;
                existing.SkippedEntities = entityProgress.SkippedEntities;  // 🚨 FIX: Include SkippedEntities
                existing.CancelledEntities = entityProgress.CancelledEntities;  // 🚨 CANCELLATION FIX: Include CancelledEntities
                existing.Status = entityProgress.Status;
                existing.CompletionTime = entityProgress.CompletionTime;
                return existing;
            });

            _logger.LogDebug("🎯 [UNIVERSAL-AGGREGATOR] Updated primary entity progress: {EntityType} = {ProcessedCount}/{TotalCount} ({ProgressPercentage:P1})",
                entityType, progress.ProcessedCount, progress.TotalCount, progress.ProgressPercentage);

            // Immediate broadcast for primary entity completion
            if (progress.Status == "completed")
            {
                await BroadcastUniversalProgressAsync(migrationId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [UNIVERSAL-AGGREGATOR] Failed to update primary entity progress for {EntityType} in migration {MigrationId}",
                entityType, migrationId);
        }
    }

    #endregion

    #region Comprehensive Entity Progress (Bridge from Tier 1)

    /// <summary>
    /// Updates comprehensive entity progress received from PipelineProgressAggregator (Tier 1)
    /// Tracks sub-entity processing: options, modifiers, images, reviews
    /// </summary>
    public Task UpdateComprehensiveEntityProgressAsync(
        string migrationId,
        string parentEntityType,
        string entityType,
        ComprehensiveEntityProgress progress,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.CompletedTask;

        try
        {
            // Get or create comprehensive progress tracking
            var comprehensiveKey = $"{parentEntityType}.{entityType}";
            var migrationProgress = _comprehensiveProgress.GetOrAdd(migrationId, _ => new ConcurrentDictionary<string, EntityTypeProgress>());
            
            // Convert ComprehensiveEntityProgress to EntityTypeProgress
            var entityProgress = new EntityTypeProgress
            {
                EntityType = progress.EntityType,
                TotalEntities = progress.TotalCount,
                ProcessedEntities = progress.ProcessedCount,
                SuccessfulEntities = progress.SuccessCount,
                FailedEntities = progress.FailureCount,
                Status = progress.Status,
                StartTime = DateTime.UtcNow.Subtract(progress.ProcessingTime ?? TimeSpan.Zero),
                CompletionTime = progress.Status == "completed" ? DateTime.UtcNow : null
            };
            
            // Update comprehensive entity progress atomically
            migrationProgress.AddOrUpdate(comprehensiveKey, entityProgress, (key, existing) => 
            {
                // Aggregate progress from multiple channels
                existing.TotalEntities += entityProgress.TotalEntities;
                existing.ProcessedEntities += entityProgress.ProcessedEntities;
                existing.SuccessfulEntities += entityProgress.SuccessfulEntities;
                existing.FailedEntities += entityProgress.FailedEntities;
                existing.Status = entityProgress.Status;
                existing.CompletionTime = entityProgress.CompletionTime;
                return existing;
            });

            _logger.LogDebug("🔄 [UNIVERSAL-AGGREGATOR] Updated comprehensive entity progress: {ParentEntityType}.{EntityType} = {ProcessedCount}/{TotalCount} (Channel: {ProcessingChannel})",
                parentEntityType, entityType, progress.ProcessedCount, progress.TotalCount, progress.ProcessingChannel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [UNIVERSAL-AGGREGATOR] Failed to update comprehensive entity progress for {ParentEntityType}.{EntityType} in migration {MigrationId}",
                parentEntityType, entityType, migrationId);
        }
        
        return Task.CompletedTask;
    }

    #endregion

    #region Universal Progress Broadcasting

    /// <summary>
    /// Gets current universal migration progress combining all tiers
    /// Provides complete migration visibility for dashboards
    /// </summary>
    public Task<UniversalMigrationProgress> GetUniversalProgressAsync(
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromResult(new UniversalMigrationProgress { MigrationId = migrationId });

        try
        {
            var universalProgress = new UniversalMigrationProgress
            {
                MigrationId = migrationId,
                LastUpdated = DateTime.UtcNow
            };

            // Aggregate primary entity progress
            if (_primaryProgress.TryGetValue(migrationId, out var primaryProgress))
            {
                universalProgress.PrimaryEntityProgress = primaryProgress.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                
                // Calculate overall statistics
                var totalEntities = primaryProgress.Values.Sum(p => p.TotalEntities);
                var processedEntities = primaryProgress.Values.Sum(p => p.ProcessedEntities);
                var successfulEntities = primaryProgress.Values.Sum(p => p.SuccessfulEntities);
                var failedEntities = primaryProgress.Values.Sum(p => p.FailedEntities);
                
                universalProgress.OverallStatistics = new MigrationStatistics
                {
                    TotalEntities = totalEntities,
                    ProcessedEntities = processedEntities,
                    SuccessfulEntities = successfulEntities,
                    FailedEntities = failedEntities,
                    StartTime = DateTime.UtcNow // Will be calculated automatically by the property
                };
            }

            // Aggregate comprehensive entity progress by parent type
            if (_comprehensiveProgress.TryGetValue(migrationId, out var comprehensiveProgress))
            {
                // Group comprehensive entities by parent type (e.g., "enhanced-products")
                var groupedByParent = comprehensiveProgress
                    .GroupBy(kvp => kvp.Key.Split('.')[0]) // Split "enhanced-products.options" → "enhanced-products"
                    .ToDictionary(
                        g => g.Key,
                        g => g.ToDictionary(kvp => kvp.Key.Split('.')[1], kvp => kvp.Value) // "options" → EntityTypeProgress
                    );
                    
                universalProgress.ComprehensiveEntityProgress = groupedByParent;
            }

            return Task.FromResult(universalProgress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [UNIVERSAL-AGGREGATOR] Failed to get universal progress for migration {MigrationId}", migrationId);
            return Task.FromResult(new UniversalMigrationProgress { MigrationId = migrationId });
        }
    }

    /// <summary>
    /// Starts automatic progress broadcasting with 250ms intervals
    /// Publishes unified dashboard events for real-time updates
    /// </summary>
    public async Task StartProgressBroadcastingAsync(
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            // Stop existing broadcasting if running
            await StopProgressBroadcastingAsync(migrationId);

            // Create new cancellation token
            var broadcastCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _broadcastCancellationTokens[migrationId] = broadcastCts;

            // Start broadcasting task
            var broadcastTask = BroadcastProgressContinuouslyAsync(migrationId, broadcastCts.Token);
            _broadcastTasks[migrationId] = broadcastTask;

            _logger.LogInformation("📡 [UNIVERSAL-AGGREGATOR] Started progress broadcasting for migration {MigrationId} with 250ms intervals", migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [UNIVERSAL-AGGREGATOR] Failed to start progress broadcasting for migration {MigrationId}", migrationId);
        }
    }

    /// <summary>
    /// Stops automatic progress broadcasting
    /// </summary>
    public async Task StopProgressBroadcastingAsync(string migrationId)
    {
        try
        {
            // Cancel broadcasting
            if (_broadcastCancellationTokens.TryRemove(migrationId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            // Wait for task completion
            if (_broadcastTasks.TryRemove(migrationId, out var task))
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }
            }

            _logger.LogInformation("⏹️ [UNIVERSAL-AGGREGATOR] Stopped progress broadcasting for migration {MigrationId}", migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [UNIVERSAL-AGGREGATOR] Failed to stop progress broadcasting for migration {MigrationId}", migrationId);
        }
    }

    #endregion

    #region Private Broadcasting Methods

    private async Task BroadcastProgressContinuouslyAsync(string migrationId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                await BroadcastUniversalProgressAsync(migrationId, cancellationToken);
                await Task.Delay(250, cancellationToken); // 250ms intervals as per document
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [UNIVERSAL-AGGREGATOR] Broadcasting loop failed for migration {MigrationId}", migrationId);
        }
    }

    private async Task BroadcastUniversalProgressAsync(string migrationId, CancellationToken cancellationToken)
    {
        try
        {
            var universalProgress = await GetUniversalProgressAsync(migrationId, cancellationToken);
            
            // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation
            var progressEvent = _signalREventFactory.CreateStatusProgress(migrationId, new StatusProgressOptions
            {
                Status = "Migration Progress",
                Message = $"Overall: {universalProgress.OverallStatistics.ProgressPercentage:P1} - Processing",
                Data = universalProgress // Pass the entire universal progress as data
            });

            await _progressEventPublisher.PublishStatusAsync(progressEvent, cancellationToken);

            _logger.LogDebug("📡 [UNIVERSAL-AGGREGATOR] Broadcasted universal progress: {OverallProgress:P1}",
                universalProgress.OverallStatistics.ProgressPercentage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [UNIVERSAL-AGGREGATOR] Failed to broadcast universal progress for migration {MigrationId}", migrationId);
        }
    }

    #endregion

    #region Disposal

    /// <summary>
    /// Disposes the aggregator and stops all broadcasting
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop all broadcasting
        var stopTasks = _broadcastCancellationTokens.Keys.Select(StopProgressBroadcastingAsync);
        Task.WaitAll(stopTasks.ToArray(), TimeSpan.FromSeconds(5));

        // Dispose remaining resources
        foreach (var cts in _broadcastCancellationTokens.Values)
        {
            cts?.Dispose();
        }

        _broadcastCancellationTokens.Clear();
        _broadcastTasks.Clear();
        _primaryProgress.Clear();
        _comprehensiveProgress.Clear();

        _logger.LogInformation("🧹 [UNIVERSAL-AGGREGATOR] Disposed successfully");
    }

    #endregion
}