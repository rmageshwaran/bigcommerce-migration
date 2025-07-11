namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service for calculating optimal batch sizes for entity processing
/// </summary>
public interface IBatchSizeCalculator
{
    /// <summary>
    /// Calculates the optimal batch size for a specific entity type and store
    /// </summary>
    /// <param name="entityType">Type of entity (products, categories, etc.)</param>
    /// <param name="storeId">Store identifier</param>
    /// <returns>Optimal batch size</returns>
    Task<int> CalculateOptimalBatchSizeAsync(string entityType, string storeId);
    
    /// <summary>
    /// Records batch performance metrics for future calculations
    /// </summary>
    /// <param name="entityType">Type of entity</param>
    /// <param name="storeId">Store identifier</param>
    /// <param name="batchSize">Size of the batch</param>
    /// <param name="processingTimeMs">Time taken to process the batch in milliseconds</param>
    /// <param name="successCount">Number of successful entities</param>
    /// <param name="failureCount">Number of failed entities</param>
    Task RecordBatchPerformanceAsync(string entityType, string storeId, int batchSize, 
        double processingTimeMs, int successCount, int failureCount);
    
    /// <summary>
    /// Gets the default batch size for an entity type
    /// </summary>
    /// <param name="entityType">Type of entity</param>
    /// <returns>Default batch size</returns>
    int GetDefaultBatchSize(string entityType);
    
    /// <summary>
    /// Gets the maximum allowed batch size for an entity type
    /// </summary>
    /// <param name="entityType">Type of entity</param>
    /// <returns>Maximum batch size</returns>
    int GetMaxBatchSize(string entityType);
    
    /// <summary>
    /// Gets batch size recommendation based on recent performance
    /// </summary>
    /// <param name="entityType">Type of entity</param>
    /// <param name="storeId">Store identifier</param>
    /// <returns>Batch size recommendation</returns>
    Task<BatchSizeRecommendation> GetBatchSizeRecommendationAsync(string entityType, string storeId);
}

/// <summary>
/// Batch size recommendation with performance metrics
/// </summary>
public class BatchSizeRecommendation
{
    /// <summary>
    /// Entity type for the recommendation
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Store identifier
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Recommended batch size
    /// </summary>
    public int RecommendedBatchSize { get; set; }
    
    /// <summary>
    /// Current batch size being used
    /// </summary>
    public int CurrentBatchSize { get; set; }
    
    /// <summary>
    /// Confidence score for the recommendation (0.0 to 1.0)
    /// </summary>
    public double ConfidenceScore { get; set; }
    
    /// <summary>
    /// Reason for the recommendation
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Average processing time per entity in milliseconds
    /// </summary>
    public double AvgProcessingTimePerEntity { get; set; }
    
    /// <summary>
    /// Error rate for recent batches (0.0 to 1.0)
    /// </summary>
    public double ErrorRate { get; set; }
} 