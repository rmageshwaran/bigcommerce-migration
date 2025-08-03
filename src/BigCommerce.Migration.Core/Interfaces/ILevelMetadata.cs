namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for level-specific metadata operations
/// Follows Interface Segregation Principle - focused only on individual level concerns
/// Segregated from IHierarchyMetadata to maintain focused responsibilities
/// </summary>
/// <remarks>
/// This interface abstracts level-specific metadata for chunked category migration.
/// Separated from hierarchy-level concerns to follow Interface Segregation Principle.
/// 
/// Key Design Principles:
/// - Interface Segregation: Only level-specific concerns, no hierarchy-wide operations
/// - Single Responsibility: Handles metadata for one specific level only
/// - Determinism: All operations must be deterministic for Azure Durable Functions
/// - Memory Safety: Designed for processing individual levels with minimal memory usage
/// </remarks>
public interface ILevelMetadata
{
    /// <summary>
    /// The hierarchy level this metadata represents (0-based)
    /// Level 0 = root categories, Level 1 = first-level children, etc.
    /// </summary>
    int Level { get; }

    /// <summary>
    /// Number of categories at this specific level
    /// Used for batch processing and progress calculation
    /// </summary>
    int CategoryCount { get; }

    /// <summary>
    /// Parent category ID for this level (null for root level)
    /// Used for hierarchical relationship validation and child discovery
    /// </summary>
    int? ParentCategoryId { get; }

    /// <summary>
    /// Estimated processing time in minutes for this level only
    /// Used for individual level timeout validation and progress tracking
    /// </summary>
    double EstimatedLevelProcessingTimeMinutes { get; }

    /// <summary>
    /// Whether this level has been processed successfully
    /// Used for resume capability and progress tracking
    /// </summary>
    bool IsProcessed { get; }

    /// <summary>
    /// Number of batches required to process this level
    /// Calculated based on CategoryCount and configured BatchSize
    /// </summary>
    int RequiredBatchCount { get; }
}