namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// Interface for entity processor used in memory usage analysis tests
/// Supports different processing strategies to analyze memory patterns
/// </summary>
public interface ITestEntityProcessor
{
    /// <summary>
    /// Processes entities using standard approach (for baseline memory measurement)
    /// </summary>
    /// <param name="entities">Entities to process</param>
    /// <returns>Task representing the processing operation</returns>
    Task ProcessEntitiesAsync(List<Dictionary<string, object>> entities);

    /// <summary>
    /// Processes entities using streaming approach (memory-efficient)
    /// </summary>
    /// <param name="entities">Entities to process</param>
    /// <returns>Task representing the processing operation</returns>
    Task ProcessEntitiesStreamingAsync(List<Dictionary<string, object>> entities);

    /// <summary>
    /// Processes entities using batch approach with configurable batch size
    /// </summary>
    /// <param name="entities">Entities to process</param>
    /// <param name="batchSize">Size of each processing batch</param>
    /// <returns>Task representing the processing operation</returns>
    Task ProcessEntitiesBatchAsync(List<Dictionary<string, object>> entities, int batchSize);

    /// <summary>
    /// Processes entities with garbage collection analysis hooks
    /// </summary>
    /// <param name="entities">Entities to process</param>
    /// <returns>Task representing the processing operation</returns>
    Task ProcessEntitiesWithGCAnalysisAsync(List<Dictionary<string, object>> entities);
} 