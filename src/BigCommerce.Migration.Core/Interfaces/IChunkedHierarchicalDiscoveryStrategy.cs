using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for chunked hierarchical category discovery strategies
/// Follows Dependency Inversion Principle - depends on abstractions, not concretions
/// Specialized for memory-safe hierarchical processing with chunked operations
/// </summary>
/// <remarks>
/// This interface abstracts chunked hierarchical discovery operations for large category hierarchies.
/// Designed to support Azure Functions constraints with memory-safe operations and continue-on-error policy.
/// 
/// Key Design Principles:
/// - Dependency Inversion: Depends on abstractions (interfaces and models), not concrete implementations
/// - Interface Segregation: Focused only on chunked hierarchical discovery concerns
/// - Memory Safety: All operations designed for Azure Functions 1.5GB memory limit
/// - Continue-on-Error: Operations return results with error tracking instead of throwing exceptions
/// - Performance Optimization: Supports BigCommerce bulk creation API patterns
/// 
/// Integration with Phase 1 Models:
/// - Uses HierarchyMetadata for hierarchy analysis results
/// - Uses LevelFetchRequest for level-specific discovery requests
/// - Uses LevelProcessingResult for continue-on-error processing results
/// - Uses ChunkedHierarchyConfiguration for operation parameters
/// </remarks>
public interface IChunkedHierarchicalDiscoveryStrategy : IEntityDiscoveryStrategy
{
    /// <summary>
    /// Maximum memory usage in MB for this strategy
    /// Used for Azure Functions memory constraint validation
    /// Target: ≤10MB during discovery operations
    /// </summary>
    double MaxMemoryUsageMB { get; }

    /// <summary>
    /// Analyzes the category hierarchy structure without loading all data into memory
    /// Performs memory-safe analysis to generate hierarchy metadata for chunked processing
    /// </summary>
    /// <param name="request">Entity discovery request with source store and configuration</param>
    /// <param name="config">Chunked hierarchy configuration with memory and performance limits</param>
    /// <param name="cancellationToken">Cancellation token for Azure Functions timeout handling</param>
    /// <returns>
    /// Hierarchy metadata containing:
    /// - Total category count across all levels
    /// - Category count per hierarchy level
    /// - Maximum hierarchy depth
    /// - Estimated processing time for the entire hierarchy
    /// </returns>
    /// <remarks>
    /// This method performs lightweight analysis without loading full category data.
    /// Memory usage should never exceed 10MB during analysis.
    /// 
    /// Analysis Process:
    /// 1. Count root categories (level 0)
    /// 2. Iterate through hierarchy levels counting children
    /// 3. Stop when no more children found or max depth reached
    /// 4. Generate processing time estimates based on category counts
    /// 5. Return metadata for chunked processing planning
    /// 
    /// Error Handling:
    /// - API failures are logged but don't stop the analysis
    /// - Returns partial metadata if some levels fail
    /// - Estimation continues with available data (continue-on-error)
    /// </remarks>
    Task<HierarchyMetadata> AnalyzeHierarchyAsync(
        EntityDiscoveryRequest request,
        ChunkedHierarchyConfiguration config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers categories at a specific hierarchy level using chunked processing
    /// Implements memory-safe level-by-level discovery with batch processing
    /// </summary>
    /// <param name="levelRequest">Level-specific fetch request with hierarchy level and batch configuration</param>
    /// <param name="discoveryRequest">Overall discovery request with source store and context</param>
    /// <param name="cancellationToken">Cancellation token for timeout and memory safety</param>
    /// <returns>
    /// Level processing result containing:
    /// - Successfully discovered category IDs
    /// - Processing errors with full context
    /// - Performance metrics (processing time, throughput, memory usage)
    /// - Success/failure rates for continue-on-error analysis
    /// </returns>
    /// <remarks>
    /// This method implements chunked processing for individual hierarchy levels.
    /// Designed to work with BigCommerce bulk creation API patterns.
    /// 
    /// Processing Approach:
    /// 1. Fetch categories at specified level in batches
    /// 2. Process each batch independently (continue-on-error)
    /// 3. Track memory usage and performance metrics
    /// 4. Return comprehensive results with error details
    /// 5. Support cancellation for timeout handling
    /// 
    /// Memory Safety:
    /// - Process categories in small batches (configured batch size)
    /// - Release memory after each batch
    /// - Monitor memory usage throughout processing
    /// - Cancel if memory usage exceeds limits
    /// 
    /// Error Handling:
    /// - Individual category failures don't stop level processing
    /// - Collect detailed error information for debugging
    /// - Continue processing remaining categories
    /// - Return comprehensive success/failure metrics
    /// </remarks>
    Task<LevelProcessingResult> DiscoverLevelAsync(
        LevelFetchRequest levelRequest,
        EntityDiscoveryRequest discoveryRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates the total processing time for the hierarchy discovery operation
    /// Provides time estimates for Azure Functions timeout planning and progress tracking
    /// </summary>
    /// <param name="request">Entity discovery request with source store information</param>
    /// <param name="config">Chunked hierarchy configuration with performance parameters</param>
    /// <param name="cancellationToken">Cancellation token for operation timeout</param>
    /// <returns>
    /// Estimated processing time in minutes for the entire hierarchy discovery.
    /// Used for:
    /// - Azure Functions timeout validation (≤5 minutes)
    /// - Progress tracking and user communication
    /// - Resource planning and optimization
    /// </returns>
    /// <remarks>
    /// Estimation Algorithm:
    /// 1. Perform lightweight hierarchy analysis (count categories per level)
    /// 2. Apply processing time formulas based on:
    ///    - Total category count
    ///    - Hierarchy depth
    ///    - Batch processing overhead
    ///    - API rate limits and performance
    /// 3. Factor in BigCommerce bulk creation API optimizations
    /// 4. Return conservative estimate with safety margin
    /// 
    /// Accuracy Target: Within 20% of actual processing time
    /// Safety Margin: Estimates include buffer for Azure Functions constraints
    /// 
    /// Performance Considerations:
    /// - Uses cached analysis results when available
    /// - Performs minimal API calls for estimation
    /// - Optimized for chunked processing patterns
    /// - Accounts for continue-on-error processing overhead
    /// </remarks>
    Task<double> EstimateProcessingTimeAsync(
        EntityDiscoveryRequest request,
        ChunkedHierarchyConfiguration config,
        CancellationToken cancellationToken = default);
}