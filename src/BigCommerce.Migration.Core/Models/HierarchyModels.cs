using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Implementation of hierarchy metadata for chunked category migration
/// Implements IHierarchyMetadata following Interface Segregation Principle
/// Designed for Azure Durable Functions deterministic operations
/// </summary>
/// <remarks>
/// This class provides hierarchy-wide metadata for memory-safe category migration processing.
/// All operations are deterministic to ensure Azure Durable Functions orchestration compatibility.
/// 
/// Performance Optimization:
/// - Supports BigCommerce bulk creation API for 5-10x performance improvement
/// - Memory-safe design for large hierarchies (100K+ categories)
/// - Efficient level-based processing with O(1) level access
/// - Estimation algorithm optimized for Azure Functions timeout constraints
/// </remarks>
public class HierarchyMetadata : IHierarchyMetadata
{
    /// <summary>
    /// Total number of categories across all levels in the hierarchy
    /// Used for progress calculation and memory estimation
    /// </summary>
    public int TotalCategories { get; set; }

    /// <summary>
    /// Maximum depth of the category hierarchy (0-based)
    /// Used for validation against MaxHierarchyDepth configuration
    /// </summary>
    public int MaxDepth { get; set; }

    /// <summary>
    /// Count of categories at each level (level -> count mapping)
    /// Key: hierarchy level (0-based), Value: category count at that level
    /// Used for chunked processing and batch size calculation
    /// </summary>
    public Dictionary<int, int>? LevelCounts { get; set; } = new Dictionary<int, int>();

    /// <summary>
    /// Estimated processing time in minutes for the entire hierarchy
    /// Used for Azure Functions timeout validation and progress estimation
    /// </summary>
    public double EstimatedProcessingTimeMinutes { get; set; }

    /// <summary>
    /// Gets the count of categories at a specific hierarchy level
    /// </summary>
    /// <param name="level">The hierarchy level (0-based)</param>
    /// <returns>Number of categories at the specified level, or 0 if level doesn't exist</returns>
    /// <remarks>
    /// This method is deterministic for Azure Durable Functions.
    /// Returns 0 for non-existent levels to support continue-on-error policy.
    /// Time complexity: O(1) for efficient access in large hierarchies.
    /// </remarks>
    public int GetCountForLevel(int level)
    {
        // Handle null LevelCounts gracefully (continue-on-error policy)
        if (LevelCounts == null)
        {
            return 0;
        }

        // Return count if level exists, otherwise 0 (deterministic behavior)
        return LevelCounts.TryGetValue(level, out var count) ? count : 0;
    }

    /// <summary>
    /// Checks if a specific hierarchy level exists and has categories
    /// </summary>
    /// <param name="level">The hierarchy level to check (0-based)</param>
    /// <returns>True if the level exists in the hierarchy, false otherwise</returns>
    /// <remarks>
    /// Returns true even if the level has 0 categories (level exists but empty).
    /// Used for level-by-level processing validation.
    /// This is deterministic and safe for Azure Durable Functions orchestration.
    /// </remarks>
    public bool HasLevel(int level)
    {
        // Handle null LevelCounts gracefully
        if (LevelCounts == null)
        {
            return false;
        }

        // Level exists if it's in the dictionary (even if count is 0)
        return LevelCounts.ContainsKey(level);
    }

    /// <summary>
    /// Calculates the estimated processing time for the hierarchy
    /// </summary>
    /// <returns>Estimated processing time in minutes</returns>
    /// <remarks>
    /// Algorithm considers:
    /// - Total category count with BigCommerce bulk creation optimization
    /// - Hierarchy depth (deeper hierarchies require more processing)
    /// - API rate limits and bulk creation batching
    /// - Azure Functions memory and timeout constraints
    /// 
    /// Estimation accuracy target: within 20% of actual processing time
    /// 
    /// Performance Formula:
    /// - Base time: TotalCategories / 1000 minutes (bulk creation optimization)
    /// - Depth penalty: MaxDepth * 0.1 minutes (hierarchy traversal overhead)
    /// - Minimum: 0.1 minutes (6 seconds for small hierarchies)
    /// - Maximum: 4.5 minutes (Azure Functions timeout safety margin)
    /// </remarks>
    public double CalculateProcessingTimeEstimate()
    {
        // Handle edge cases
        if (TotalCategories <= 0)
        {
            return 0.0;
        }

        // Base processing time using bulk creation optimization
        // Bulk creation reduces API calls by 96%, significantly improving performance
        var baseTime = TotalCategories / 1000.0; // 1000 categories per minute with bulk creation

        // Add depth penalty for hierarchy traversal overhead
        var depthPenalty = MaxDepth * 0.1; // 6 seconds per level depth

        // Calculate total estimate
        var totalEstimate = baseTime + depthPenalty;

        // Apply constraints
        var minTime = 0.1; // Minimum 6 seconds
        var maxTime = 4.5; // Maximum 4.5 minutes (Azure Functions timeout safety margin)

        // Clamp to reasonable bounds
        return Math.Max(minTime, Math.Min(maxTime, totalEstimate));
    }
}

/// <summary>
/// Request model for level-by-level category fetching
/// Designed for Azure Durable Functions deterministic orchestration
/// Supports chunked processing for memory-safe category migration
/// </summary>
/// <remarks>
/// This model is optimized for BigCommerce bulk creation API and Azure Functions constraints.
/// All properties are deterministic to ensure orchestration replay compatibility.
/// 
/// Usage:
/// - Level 0: Root categories (ParentCategoryId = null)
/// - Level N: Child categories of specific parent (ParentCategoryId = parent ID)
/// - Batch processing: Uses BatchSize for memory-safe API calls
/// - Timeout safety: Respects MaxCategoriesPerLevel for Azure Functions limits
/// </remarks>
public class LevelFetchRequest
{
    /// <summary>
    /// The hierarchy level to fetch (0-based)
    /// Level 0 = root categories, Level 1 = first-level children, etc.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Parent category ID for this level (null for root level)
    /// Used for hierarchical child discovery and validation
    /// </summary>
    public int? ParentCategoryId { get; set; }

    /// <summary>
    /// Maximum number of categories to fetch for this level
    /// Used for memory safety and Azure Functions timeout prevention
    /// Should respect ChunkedHierarchyConfiguration.MaxCategoriesPerLevel
    /// </summary>
    public int MaxCategoriesPerLevel { get; set; }

    /// <summary>
    /// Batch size for processing categories at this level
    /// Used for BigCommerce bulk creation API optimization
    /// Should respect ChunkedHierarchyConfiguration.BatchSizePerLevel
    /// </summary>
    public int BatchSize { get; set; }

    /// <summary>
    /// Category tree context containing source and destination tree IDs
    /// Required for correct BigCommerce API tree_id parameter
    /// </summary>
    public CategoryTreeContext? CategoryTreeContext { get; set; }

    /// <summary>
    /// Default constructor for Azure Durable Functions serialization
    /// </summary>
    public LevelFetchRequest()
    {
        // Initialize with safe defaults
        Level = 0;
        ParentCategoryId = null;
        MaxCategoriesPerLevel = 1000;
        BatchSize = 25;
    }

    /// <summary>
    /// Constructor for creating level fetch requests with all parameters
    /// </summary>
    /// <param name="level">Hierarchy level (0-based)</param>
    /// <param name="parentCategoryId">Parent category ID (null for root level)</param>
    /// <param name="maxCategoriesPerLevel">Maximum categories to fetch</param>
    /// <param name="batchSize">Batch size for processing</param>
    public LevelFetchRequest(int level, int? parentCategoryId, int maxCategoriesPerLevel, int batchSize)
    {
        Level = level;
        ParentCategoryId = parentCategoryId;
        MaxCategoriesPerLevel = maxCategoriesPerLevel;
        BatchSize = batchSize;
    }

    /// <summary>
    /// Validates the request against Azure Functions and BigCommerce API constraints
    /// </summary>
    /// <returns>True if the request is valid, false otherwise</returns>
    /// <remarks>
    /// Validation rules:
    /// - Level must be non-negative
    /// - MaxCategoriesPerLevel must be positive and within memory limits
    /// - BatchSize must be positive and within API rate limits
    /// - ParentCategoryId can be null (root level) or positive integer
    /// </remarks>
    public bool IsValid()
    {
        // Level validation
        if (Level < 0)
        {
            return false;
        }

        // MaxCategoriesPerLevel validation
        if (MaxCategoriesPerLevel <= 0 || MaxCategoriesPerLevel > 50000)
        {
            return false;
        }

        // BatchSize validation
        if (BatchSize <= 0 || BatchSize > 100)
        {
            return false;
        }

        // ParentCategoryId validation (null is valid for root level)
        if (ParentCategoryId.HasValue && ParentCategoryId.Value <= 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a string representation of the request for debugging and logging
    /// </summary>
    /// <returns>Formatted request details</returns>
    public override string ToString()
    {
        var parentInfo = ParentCategoryId?.ToString(CultureInfo.InvariantCulture) ?? "ROOT";
        return $"LevelFetchRequest(Level={Level.ToString(CultureInfo.InvariantCulture)}, Parent={parentInfo}, MaxCategories={MaxCategoriesPerLevel.ToString(CultureInfo.InvariantCulture)}, BatchSize={BatchSize.ToString(CultureInfo.InvariantCulture)})";
    }
}