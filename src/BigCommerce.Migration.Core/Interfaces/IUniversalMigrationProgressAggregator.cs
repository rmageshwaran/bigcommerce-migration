using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Universal Migration Progress Aggregator (Tier 2)
/// Provides ecosystem-wide progress tracking across all entity types and processing modes
/// 
/// **Dual-Tier Architecture:**
/// - Tier 1: PipelineProgressAggregator - Comprehensive entity progress within chunks
/// - Tier 2: UniversalMigrationProgressAggregator - Overall migration and primary entity progress
/// 
/// **Key Features:**
/// - Primary entity tracking: brands, products, enhanced-products  
/// - Comprehensive entity tracking: options, modifiers, images, reviews
/// - Real-time SignalR broadcasting with 250ms intervals
/// - Thread-safe concurrent updates from multiple aggregators
/// - Unified dashboard events for complete migration visibility
/// </summary>
public interface IUniversalMigrationProgressAggregator : IDisposable
{
    #region Primary Entity Progress (Tier 2 - Ecosystem Level)

    /// <summary>
    /// Updates progress for primary entity types (brands, products, enhanced-products)
    /// Used by standard processing and comprehensive pipeline completion
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Primary entity type (brands, products, enhanced-products)</param>
    /// <param name="progress">Primary entity progress data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdatePrimaryEntityProgressAsync(
        string migrationId,
        string entityType,
        PrimaryEntityProgress progress,
        CancellationToken cancellationToken = default);

    #endregion

    #region Comprehensive Entity Progress (Bridge from Tier 1)

    /// <summary>
    /// Updates comprehensive entity progress received from PipelineProgressAggregator (Tier 1)
    /// Tracks sub-entity processing: options, modifiers, images, reviews
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="parentEntityType">Parent entity (enhanced-products)</param>
    /// <param name="entityType">Sub-entity type (options, modifiers, images, reviews)</param>
    /// <param name="progress">Comprehensive entity progress from pipeline</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateComprehensiveEntityProgressAsync(
        string migrationId,
        string parentEntityType,
        string entityType,
        ComprehensiveEntityProgress progress,
        CancellationToken cancellationToken = default);

    #endregion

    #region Universal Progress Broadcasting

    /// <summary>
    /// Gets current universal migration progress combining all tiers
    /// Provides complete migration visibility for dashboards
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<UniversalMigrationProgress> GetUniversalProgressAsync(
        string migrationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts automatic progress broadcasting with 250ms intervals
    /// Publishes unified dashboard events for real-time updates
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartProgressBroadcastingAsync(
        string migrationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops automatic progress broadcasting
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    Task StopProgressBroadcastingAsync(string migrationId);

    #endregion
}

#region Supporting Models for Universal Progress

/// <summary>
/// Primary entity progress for brands, products, enhanced-products
/// </summary>
public class PrimaryEntityProgress
{
    /// <summary>
    /// Entity type (brands, products, enhanced-products)
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Total entities for this type
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Entities processed
    /// </summary>
    public int ProcessedCount { get; set; }

    /// <summary>
    /// Successful entities
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Failed entities
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Skipped entities (duplicates, etc.)
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Processing status
    /// </summary>
    public string Status { get; set; } = "pending"; // pending, processing, completed

    /// <summary>
    /// Current throughput (entities per second)
    /// </summary>
    public double ThroughputPerSecond { get; set; }

    /// <summary>
    /// Processing time
    /// </summary>
    public TimeSpan? ProcessingTime { get; set; }

    /// <summary>
    /// Progress percentage (0.0 to 1.0)
    /// </summary>
    public double ProgressPercentage => TotalCount > 0 ? (double)ProcessedCount / TotalCount : 0.0;
}

/// <summary>
/// Comprehensive entity progress for options, modifiers, images, reviews
/// </summary>
public class ComprehensiveEntityProgress
{
    /// <summary>
    /// Parent entity type (enhanced-products)
    /// </summary>
    public string ParentEntityType { get; set; } = string.Empty;

    /// <summary>
    /// Sub-entity type (options, modifiers, images, reviews)
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Total sub-entities discovered
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Sub-entities processed
    /// </summary>
    public int ProcessedCount { get; set; }

    /// <summary>
    /// Successful sub-entities
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Failed sub-entities
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Skipped sub-entities
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Processing status
    /// </summary>
    public string Status { get; set; } = "pending"; // pending, processing, completed

    /// <summary>
    /// Current throughput (entities per second)
    /// </summary>
    public double ThroughputPerSecond { get; set; }

    /// <summary>
    /// Processing channel identifier
    /// </summary>
    public string ProcessingChannel { get; set; } = string.Empty;

    /// <summary>
    /// Active parallel channels
    /// </summary>
    public int ActiveChannels { get; set; }

    /// <summary>
    /// Processing time
    /// </summary>
    public TimeSpan? ProcessingTime { get; set; }

    /// <summary>
    /// Progress percentage (0.0 to 1.0)
    /// </summary>
    public double ProgressPercentage => TotalCount > 0 ? (double)ProcessedCount / TotalCount : 0.0;
}

#endregion