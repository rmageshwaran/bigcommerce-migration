using System.Collections.Generic;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for hierarchy metadata operations
/// Follows Interface Segregation Principle - focused only on core hierarchy metadata concerns
/// Ensures Azure Durable Functions determinism through immutable operations
/// </summary>
/// <remarks>
/// This interface abstracts hierarchy-level metadata operations for chunked category migration.
/// Implementations must be deterministic for Azure Durable Functions orchestration compatibility.
/// 
/// Key Design Principles:
/// - Interface Segregation: Only hierarchy-level concerns, no level-specific details
/// - Determinism: All operations must be pure and reproducible
/// - Memory Safety: Operations designed for low memory usage in Azure Functions
/// - Performance: Support for large hierarchies (100K+ categories) with efficient access
/// </remarks>
public interface IHierarchyMetadata
{
    /// <summary>
    /// Total number of categories across all levels in the hierarchy
    /// Used for progress calculation and memory estimation
    /// </summary>
    int TotalCategories { get; }

    /// <summary>
    /// Maximum depth of the category hierarchy (0-based)
    /// Used for validation against MaxHierarchyDepth configuration
    /// </summary>
    int MaxDepth { get; }

    /// <summary>
    /// Count of categories at each level (level -> count mapping)
    /// Key: hierarchy level (0-based), Value: category count at that level
    /// Used for chunked processing and batch size calculation
    /// </summary>
    Dictionary<int, int>? LevelCounts { get; }

    /// <summary>
    /// Estimated processing time in minutes for the entire hierarchy
    /// Used for Azure Functions timeout validation and progress estimation
    /// </summary>
    double EstimatedProcessingTimeMinutes { get; }

    /// <summary>
    /// Gets the count of categories at a specific hierarchy level
    /// </summary>
    /// <param name="level">The hierarchy level (0-based)</param>
    /// <returns>Number of categories at the specified level, or 0 if level doesn't exist</returns>
    /// <remarks>
    /// This method must be deterministic for Azure Durable Functions.
    /// Returns 0 for non-existent levels to support continue-on-error policy.
    /// </remarks>
    int GetCountForLevel(int level);

    /// <summary>
    /// Checks if a specific hierarchy level exists and has categories
    /// </summary>
    /// <param name="level">The hierarchy level to check (0-based)</param>
    /// <returns>True if the level exists in the hierarchy, false otherwise</returns>
    /// <remarks>
    /// Returns true even if the level has 0 categories (level exists but empty).
    /// Used for level-by-level processing validation.
    /// </remarks>
    bool HasLevel(int level);

    /// <summary>
    /// Calculates the estimated processing time for the hierarchy
    /// </summary>
    /// <returns>Estimated processing time in minutes</returns>
    /// <remarks>
    /// Algorithm considers:
    /// - Total category count
    /// - Hierarchy depth (deeper hierarchies require more processing)
    /// - BigCommerce API rate limits and bulk creation optimizations
    /// - Azure Functions memory and timeout constraints
    /// 
    /// Estimation accuracy target: within 20% of actual processing time
    /// </remarks>
    double CalculateProcessingTimeEstimate();
}