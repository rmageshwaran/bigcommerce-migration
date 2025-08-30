using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service for managing atomic RowNumber assignment for EntityMapping creation
/// Provides thread-safe sequential RowNumber allocation for composite RowKey generation
/// 
/// Key Features:
/// - Atomic counter operations using Azure Table Storage optimistic concurrency
/// - Per-migration-entity-type isolation
/// - High-performance throughput (target: &gt;100 requests/second)
/// - Comprehensive error handling and retry logic
/// - Range allocation support for batch operations
/// 
/// Usage Pattern:
/// 1. Single RowNumber: GetNextRowNumberAsync() for individual EntityMapping creation
/// 2. Range Allocation: AllocateRangeAsync() for batch EntityMapping creation
/// 3. Count Queries: GetCurrentCounterAsync() for discovery and validation
/// </summary>
public interface IRowNumberService
{
    /// <summary>
    /// Gets the next sequential RowNumber for EntityMapping creation
    /// Thread-safe operation using optimistic concurrency control
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type (e.g., "products", "categories")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Next sequential RowNumber (1, 2, 3, ...)</returns>
    /// <exception cref="ArgumentNullException">Thrown when migrationId or entityType is null/empty</exception>
    /// <exception cref="InvalidOperationException">Thrown when counter service fails after retries</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    Task<long> GetNextRowNumberAsync(
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates a range of sequential RowNumbers for batch operations
    /// More efficient than multiple GetNextRowNumberAsync calls
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type (e.g., "products", "categories")</param>
    /// <param name="count">Number of RowNumbers to allocate (must be &gt; 0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Start and end RowNumbers (inclusive range)</returns>
    /// <exception cref="ArgumentNullException">Thrown when migrationId or entityType is null/empty</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when count is &lt;= 0 or &gt; 10000</exception>
    /// <exception cref="InvalidOperationException">Thrown when counter service fails after retries</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    /// <example>
    /// // Allocate 100 RowNumbers for batch creation
    /// var (start, end) = await service.AllocateRangeAsync("migration123", "products", 100);
    /// // Returns: start=1001, end=1100 (if counter was at 1000)
    /// </example>
    Task<(long StartRowNumber, long EndRowNumber)> AllocateRangeAsync(
        string migrationId, 
        string entityType, 
        int count, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current counter state for a migration and entity type
    /// Useful for discovery, validation, and progress tracking
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type (e.g., "products", "categories")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current counter state, or null if counter doesn't exist</returns>
    /// <exception cref="ArgumentNullException">Thrown when migrationId or entityType is null/empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    Task<RowNumberCounter?> GetCurrentCounterAsync(
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the counter for a migration and entity type
    /// WARNING: This will cause RowKey conflicts if EntityMappings already exist
    /// Should only be used during development or for complete migration restart
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type (e.g., "products", "categories")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Reset counter state</returns>
    /// <exception cref="ArgumentNullException">Thrown when migrationId or entityType is null/empty</exception>
    /// <exception cref="InvalidOperationException">Thrown when reset operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    Task<RowNumberCounter> ResetCounterAsync(
        string migrationId, 
        string entityType, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the counter service is healthy and operational
    /// Performs basic connectivity and functionality tests
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if service is healthy, false otherwise</returns>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets performance metrics for the counter service
    /// Useful for monitoring and optimization
    /// </summary>
    /// <param name="migrationId">Migration identifier (optional - if null, returns global metrics)</param>
    /// <param name="entityType">Entity type (optional - if null, returns all entity types)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Performance metrics for the specified scope</returns>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    Task<RowNumberServiceMetrics> GetMetricsAsync(
        string? migrationId = null, 
        string? entityType = null, 
        CancellationToken cancellationToken = default);
}
