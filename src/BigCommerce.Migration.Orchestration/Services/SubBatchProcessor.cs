using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Implementation of ISubBatchProcessor that provides optimized sub-batch processing
/// with concurrency control, rate limiting, and configurable delays
/// </summary>
public class SubBatchProcessor : ISubBatchProcessor
{
    private readonly ILogger<SubBatchProcessor> _logger;

    public SubBatchProcessor(ILogger<SubBatchProcessor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<TOutput>> ProcessInSubBatchesAsync<TInput, TOutput>(
        List<TInput> entities,
        SubBatchConfiguration config,
        Func<List<TInput>, CancellationToken, Task<List<TOutput>>> processor,
        string entityType,
        string migrationId,
        CancellationToken cancellationToken = default)
        where TOutput : class
    {
        if (entities == null || !entities.Any())
        {
            return new List<TOutput>();
        }

        if (!config.EnableSubBatching)
        {
            _logger.LogInformation("🔧 [SUB-BATCH] Sub-batching disabled for {EntityType}, processing all {Count} entities at once",
                entityType, entities.Count);
            return await processor(entities, cancellationToken);
        }

        var executionId = migrationId.GetHashCode().ToString("X8");
        var startTime = DateTime.UtcNow;
        
        _logger.LogInformation("🚀 [SUB-BATCH-{ExecutionId}] Starting sub-batch processing: {TotalCount} {EntityType} entities " +
                             "in sub-batches of {SubBatchSize} with max {MaxConcurrency} concurrent at {StartTime}",
            executionId, entities.Count, entityType, config.SubBatchSize, config.MaxConcurrency, startTime.ToString("HH:mm:ss.fff"));

        var results = new List<TOutput>();
        var semaphore = new SemaphoreSlim(config.MaxConcurrency, config.MaxConcurrency);
        
        try
        {
            // Split entities into sub-batches
            var subBatches = entities.Chunk(config.SubBatchSize).ToList();
            
            _logger.LogDebug("🔧 [SUB-BATCH-{ExecutionId}] Created {SubBatchCount} sub-batches for {EntityType}",
                executionId, subBatches.Count, entityType);

            if (config.ProcessSubBatchesSequentially)
            {
                // Process sub-batches sequentially for better control and debugging
                results = await ProcessSubBatchesSequentiallyAsync(
                    subBatches, config, processor, entityType, executionId, semaphore, cancellationToken);
            }
            else
            {
                // Process sub-batches in parallel for maximum performance
                results = await ProcessSubBatchesInParallelAsync(
                    subBatches, config, processor, entityType, executionId, semaphore, cancellationToken);
            }

            _logger.LogInformation("✅ [SUB-BATCH-{ExecutionId}] Sub-batch processing completed: {ResultCount}/{TotalCount} {EntityType} entities processed successfully",
                executionId, results.Count, entities.Count, entityType);

            return results;
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    public async Task<List<TOutput>> ProcessIndividuallyInSubBatchesAsync<TInput, TOutput>(
        List<TInput> entities,
        SubBatchConfiguration config,
        Func<TInput, CancellationToken, Task<TOutput?>> individualProcessor,
        string entityType,
        string migrationId,
        CancellationToken cancellationToken = default)
        where TOutput : class
    {
        // Wrap individual processor to work with sub-batch processor
        async Task<List<TOutput>> SubBatchProcessor(List<TInput> subBatch, CancellationToken ct)
        {
            var subBatchResults = new List<TOutput>();
            var subBatchId = Guid.NewGuid().ToString("N")[..8];
            var threadId = Thread.CurrentThread.ManagedThreadId;
            
            _logger.LogDebug("🔧 [SUB-BATCH-{SubBatchId}] Processing sub-batch of {Count} {EntityType} entities on thread {ThreadId}",
                subBatchId, subBatch.Count, entityType, threadId);
            
            for (int i = 0; i < subBatch.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                try
                {
                    var entityStartTime = DateTime.UtcNow;
                    var result = await individualProcessor(subBatch[i], ct);
                    var entityDuration = DateTime.UtcNow - entityStartTime;
                    
                    if (result != null)
                    {
                        subBatchResults.Add(result);
                        _logger.LogDebug("✅ [SUB-BATCH-{SubBatchId}] Entity {Index}/{Count} processed successfully in {Duration}ms on thread {ThreadId}",
                            subBatchId, i + 1, subBatch.Count, entityDuration.TotalMilliseconds, threadId);
                    }
                    else
                    {
                        _logger.LogDebug("⚠️ [SUB-BATCH-{SubBatchId}] Entity {Index}/{Count} returned null (duplicate/skip) in {Duration}ms on thread {ThreadId}",
                            subBatchId, i + 1, subBatch.Count, entityDuration.TotalMilliseconds, threadId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "❌ [SUB-BATCH-{SubBatchId}] Entity {Index}/{Count} failed on thread {ThreadId}: {ErrorMessage}",
                        subBatchId, i + 1, subBatch.Count, threadId, ex.Message);
                    // Continue processing other entities in the sub-batch
                }
            }
            
            _logger.LogDebug("🔧 [SUB-BATCH-{SubBatchId}] Completed: {SuccessCount}/{TotalCount} entities processed successfully on thread {ThreadId}",
                subBatchId, subBatchResults.Count, subBatch.Count, threadId);
            
            return subBatchResults;
        }

        return await ProcessInSubBatchesAsync(entities, config, SubBatchProcessor, entityType, migrationId, cancellationToken);
    }

    /// <summary>
    /// Processes sub-batches sequentially for better control and debugging
    /// </summary>
    private async Task<List<TOutput>> ProcessSubBatchesSequentiallyAsync<TInput, TOutput>(
        List<TInput[]> subBatches,
        SubBatchConfiguration config,
        Func<List<TInput>, CancellationToken, Task<List<TOutput>>> processor,
        string entityType,
        string executionId,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
        where TOutput : class
    {
        var results = new List<TOutput>();

        for (int i = 0; i < subBatches.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subBatch = subBatches[i].ToList();
            
            _logger.LogDebug("🔧 [SUB-BATCH-{ExecutionId}] Processing sub-batch {SubBatchIndex}/{TotalSubBatches}: " +
                           "{SubBatchSize} {EntityType} entities (sequential)",
                executionId, i + 1, subBatches.Count, subBatch.Count, entityType);

            try
            {
                await semaphore.WaitAsync(cancellationToken);
                
                try
                {
                    // Apply configured delay between sub-batches
                    if (config.SubBatchDelayMs > 0 && i > 0)
                    {
                        _logger.LogDebug("⏱️ [SUB-BATCH-{ExecutionId}] Applying {DelayMs}ms delay before sub-batch {SubBatchIndex}",
                            executionId, config.SubBatchDelayMs, i + 1);
                        await Task.Delay(config.SubBatchDelayMs, cancellationToken);
                    }

                    var subBatchResults = await processor(subBatch, cancellationToken);
                    
                    if (subBatchResults != null && subBatchResults.Any())
                    {
                        results.AddRange(subBatchResults);
                    }

                    _logger.LogDebug("✅ [SUB-BATCH-{ExecutionId}] Sub-batch {SubBatchIndex} completed: " +
                                   "{ResultCount} {EntityType} entities processed",
                        executionId, i + 1, subBatchResults?.Count ?? 0, entityType);
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [SUB-BATCH-{ExecutionId}] Sub-batch {SubBatchIndex} failed: {ErrorMessage}",
                    executionId, i + 1, ex.Message);
                // Continue with next sub-batch instead of failing entire operation
            }
        }

        return results;
    }

    /// <summary>
    /// Processes sub-batches in parallel for maximum performance
    /// </summary>
    private async Task<List<TOutput>> ProcessSubBatchesInParallelAsync<TInput, TOutput>(
        List<TInput[]> subBatches,
        SubBatchConfiguration config,
        Func<List<TInput>, CancellationToken, Task<List<TOutput>>> processor,
        string entityType,
        string executionId,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
        where TOutput : class
    {
        var subBatchTasks = subBatches.Select(async (subBatch, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            
            try
            {
                _logger.LogDebug("🔧 [SUB-BATCH-{ExecutionId}] Starting sub-batch {SubBatchIndex}: " +
                               "{SubBatchSize} {EntityType} entities (parallel)",
                    executionId, index + 1, subBatch.Length, entityType);

                // Apply configured delay (staggered start for parallel processing)
                if (config.SubBatchDelayMs > 0)
                {
                    var staggeredDelay = (index * config.SubBatchDelayMs) / config.MaxConcurrency;
                    if (staggeredDelay > 0)
                    {
                        await Task.Delay(staggeredDelay, cancellationToken);
                    }
                }

                var subBatchResults = await processor(subBatch.ToList(), cancellationToken);
                
                _logger.LogDebug("✅ [SUB-BATCH-{ExecutionId}] Sub-batch {SubBatchIndex} completed: " +
                               "{ResultCount} {EntityType} entities (parallel)",
                    executionId, index + 1, subBatchResults?.Count ?? 0, entityType);

                return subBatchResults ?? new List<TOutput>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [SUB-BATCH-{ExecutionId}] Sub-batch {SubBatchIndex} failed: {ErrorMessage}",
                    executionId, index + 1, ex.Message);
                return new List<TOutput>();
            }
            finally
            {
                semaphore.Release();
            }
        });

        var allSubBatchResults = await Task.WhenAll(subBatchTasks);
        return allSubBatchResults.SelectMany(results => results).ToList();
    }
}