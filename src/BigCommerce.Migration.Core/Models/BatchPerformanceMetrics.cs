namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Performance metrics for batch API operations
/// Used to track efficiency improvements from batch processing
/// </summary>
public class BatchPerformanceMetrics
{
    /// <summary>
    /// Total number of batch operations performed
    /// </summary>
    public int TotalBatchOperations { get; set; }

    /// <summary>
    /// Total number of individual API calls that would have been made
    /// </summary>
    public int EquivalentIndividualCalls { get; set; }

    /// <summary>
    /// Actual number of API calls made using batch operations
    /// </summary>
    public int ActualBatchCalls { get; set; }

    /// <summary>
    /// Percentage reduction in API calls achieved through batching
    /// </summary>
    public double ApiCallReductionPercentage { get; set; }

    /// <summary>
    /// Average time per batch operation in milliseconds
    /// </summary>
    public double AverageBatchTimeMs { get; set; }

    /// <summary>
    /// Average time per individual operation (estimated) in milliseconds
    /// </summary>
    public double AverageIndividualTimeMs { get; set; }

    /// <summary>
    /// Total time saved through batch operations in milliseconds
    /// </summary>
    public double TimeSavedMs { get; set; }

    /// <summary>
    /// Number of entities processed in total
    /// </summary>
    public int TotalEntitiesProcessed { get; set; }

    /// <summary>
    /// Number of successful batch operations
    /// </summary>
    public int SuccessfulBatchOperations { get; set; }

    /// <summary>
    /// Number of failed batch operations
    /// </summary>
    public int FailedBatchOperations { get; set; }

    /// <summary>
    /// Success rate for batch operations as a percentage
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// Timeframe for these metrics in hours
    /// </summary>
    public int TimeframeHours { get; set; }

    /// <summary>
    /// When these metrics were generated
    /// </summary>
    public DateTime GeneratedAt { get; set; }
} 