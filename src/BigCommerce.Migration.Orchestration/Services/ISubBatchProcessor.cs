using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Service for processing entities in sub-batches with concurrency control and rate limiting
/// Provides generic sub-batch processing capabilities for optimal API utilization
/// </summary>
public interface ISubBatchProcessor
{
    /// <summary>
    /// Processes entities in sub-batches according to the provided configuration
    /// </summary>
    /// <typeparam name="TInput">Type of input entities</typeparam>
    /// <typeparam name="TOutput">Type of output entities</typeparam>
    /// <param name="entities">Entities to process</param>
    /// <param name="config">Sub-batch configuration</param>
    /// <param name="processor">Function to process each sub-batch</param>
    /// <param name="entityType">Entity type for logging</param>
    /// <param name="migrationId">Migration ID for tracking</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of processed entities</returns>
    Task<List<TOutput>> ProcessInSubBatchesAsync<TInput, TOutput>(
        List<TInput> entities,
        SubBatchConfiguration config,
        Func<List<TInput>, CancellationToken, Task<List<TOutput>>> processor,
        string entityType,
        string migrationId,
        CancellationToken cancellationToken = default)
        where TOutput : class;

    /// <summary>
    /// Processes entities in sub-batches with individual entity processing
    /// Useful when you need to process entities one by one but with sub-batch concurrency control
    /// </summary>
    /// <typeparam name="TInput">Type of input entities</typeparam>
    /// <typeparam name="TOutput">Type of output entities</typeparam>
    /// <param name="entities">Entities to process</param>
    /// <param name="config">Sub-batch configuration</param>
    /// <param name="individualProcessor">Function to process each individual entity</param>
    /// <param name="entityType">Entity type for logging</param>
    /// <param name="migrationId">Migration ID for tracking</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of processed entities (excludes null results)</returns>
    Task<List<TOutput>> ProcessIndividuallyInSubBatchesAsync<TInput, TOutput>(
        List<TInput> entities,
        SubBatchConfiguration config,
        Func<TInput, CancellationToken, Task<TOutput?>> individualProcessor,
        string entityType,
        string migrationId,
        CancellationToken cancellationToken = default)
        where TOutput : class;
}