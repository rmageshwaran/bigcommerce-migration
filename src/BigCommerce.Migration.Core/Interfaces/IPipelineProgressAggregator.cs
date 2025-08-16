using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Pipeline Progress Aggregator (Tier 1)
/// Provides comprehensive entity progress tracking within individual processing chunks
/// 
/// **Dual-Tier Architecture:**
/// - Tier 1: PipelineProgressAggregator - Real-time sub-entity progress within chunks
/// - Tier 2: UniversalMigrationProgressAggregator - Overall ecosystem progress
/// 
/// **Key Features:**
/// - Real-time channel updates for options, modifiers, images, reviews
/// - Timer-based broadcasting with 250ms intervals for optimal UX
/// - Thread-safe updates from parallel sub-entity processors
/// - Automatic bridge to Universal Aggregator (Tier 2)
/// - Individual entity counts and throughput metrics
/// </summary>
public interface IPipelineProgressAggregator : IDisposable
{
    #region Comprehensive Entity Progress Tracking (Tier 1)

    /// <summary>
    /// Updates progress for a specific sub-entity type within the pipeline
    /// Used by parallel processors for options, modifiers, images, reviews
    /// </summary>
    /// <param name="entityType">Sub-entity type (options, modifiers, images, reviews)</param>
    /// <param name="progress">Progress update data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateSubEntityProgressAsync(
        string entityType,
        SubEntityProgressUpdate progress,
        CancellationToken cancellationToken = default);

    #endregion

    #region Real-time Broadcasting (Tier 1)

    /// <summary>
    /// Starts real-time progress broadcasting with 250ms intervals
    /// Publishes individual sub-entity progress events for granular visibility
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartRealtimeBroadcastingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops real-time broadcasting
    /// </summary>
    Task StopRealtimeBroadcastingAsync();

    #endregion

    #region Bridge to Universal Aggregator (Tier 1 → Tier 2)

    /// <summary>
    /// Gets current pipeline progress summary for bridge to Universal Aggregator
    /// Aggregates all sub-entity progress into comprehensive summary
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<PipelineProgressSummary> GetPipelineProgressSummaryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Configures automatic bridge to Universal Aggregator
    /// Enables seamless Tier 1 → Tier 2 progress flow
    /// </summary>
    /// <param name="universalAggregator">Universal aggregator instance</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="parentEntityType">Parent entity type (enhanced-products)</param>
    void ConfigureUniversalAggregatorBridge(
        IUniversalMigrationProgressAggregator universalAggregator,
        string migrationId,
        string parentEntityType);

    #endregion
}

#region Supporting Models for Pipeline Progress

/// <summary>
/// Progress update for individual sub-entity processing
/// </summary>
public class SubEntityProgressUpdate
{
    /// <summary>
    /// Number of entities processed in this update
    /// </summary>
    public int ProcessedCount { get; set; }

    /// <summary>
    /// Number of successful entities in this update
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of failed entities in this update
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Number of skipped entities in this update
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Processing time for this update
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }

    /// <summary>
    /// Processing channel identifier
    /// </summary>
    public string ProcessingChannel { get; set; } = string.Empty;

    /// <summary>
    /// Current status
    /// </summary>
    public string Status { get; set; } = "processing"; // processing, completed, failed
}

/// <summary>
/// Summary of all pipeline progress for bridge to Universal Aggregator
/// </summary>
public class PipelineProgressSummary
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Parent entity type (enhanced-products)
    /// </summary>
    public string ParentEntityType { get; set; } = string.Empty;

    /// <summary>
    /// Comprehensive progress for each sub-entity type
    /// </summary>
    public Dictionary<string, ComprehensiveEntityProgress> SubEntityProgress { get; set; } = new();

    /// <summary>
    /// Overall pipeline status
    /// </summary>
    public string Status { get; set; } = "processing"; // processing, completed, failed

    /// <summary>
    /// Total pipeline processing time
    /// </summary>
    public TimeSpan TotalProcessingTime { get; set; }

    /// <summary>
    /// Number of active processing channels
    /// </summary>
    public int ActiveChannels { get; set; }

    /// <summary>
    /// Timestamp of this summary
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

#endregion