using System;
using System.Collections.Generic;
using System.Globalization;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Services;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Input model for FetchCategoriesForLevelActivity
/// Contains all parameters needed for level-based category fetching in Azure Functions
/// </summary>
public class LevelFetchActivityInput
{
    /// <summary>
    /// Level-specific fetch request with hierarchy level and batch configuration
    /// </summary>
    public LevelFetchRequest? LevelRequest { get; set; }

    /// <summary>
    /// Overall discovery request with source store and migration context
    /// </summary>
    public EntityDiscoveryRequest? DiscoveryRequest { get; set; }

    /// <summary>
    /// Timeout in minutes for the activity (max 4 minutes for Azure Functions)
    /// </summary>
    public double TimeoutMinutes { get; set; } = 4.0;

    /// <summary>
    /// Validates the input parameters for Azure Functions constraints
    /// </summary>
    /// <returns>Validation result with any errors</returns>
    public Services.ValidationResult Validate()
    {
        var result = new Services.ValidationResult();

        if (LevelRequest == null)
        {
            result.Errors.Add("LevelRequest is required for level fetching activity");
        }

        if (DiscoveryRequest == null)
        {
            result.Errors.Add("DiscoveryRequest is required for level fetching activity");
        }

        if (TimeoutMinutes <= 0)
        {
            result.Errors.Add("TimeoutMinutes must be positive");
        }

        if (TimeoutMinutes > 5.0) // Azure Functions timeout limit
        {
            result.Errors.Add("TimeoutMinutes cannot exceed 5 minutes (Azure Functions limit)");
        }

        return result;
    }

    /// <summary>
    /// Gets a string representation of the input for logging
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, 
            $"LevelFetchActivityInput[Level={LevelRequest?.Level ?? -1}, Migration={DiscoveryRequest?.MigrationId ?? "null"}, Timeout={TimeoutMinutes:F1}min]");
    }
}

/// <summary>
/// Result model for FetchCategoriesForLevelActivity
/// Contains processing results, performance metrics, and error information
/// </summary>
public class LevelFetchActivityResult
{
    /// <summary>
    /// Whether the level fetching completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The hierarchy level that was processed
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Number of categories successfully fetched
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of categories that failed to fetch
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Total number of categories attempted
    /// </summary>
    public int TotalCategories { get; set; }

    /// <summary>
    /// Processing time in minutes
    /// </summary>
    public double ProcessingTimeMinutes { get; set; }

    /// <summary>
    /// Peak memory usage during processing (MB)
    /// </summary>
    public double MemoryUsageMB { get; set; }

    /// <summary>
    /// List of errors encountered during processing
    /// </summary>
    public List<ProcessingError> Errors { get; set; } = new List<ProcessingError>();

    /// <summary>
    /// Fetched category data (optional, for debugging)
    /// </summary>
    public List<string>? FetchedCategoryIds { get; set; }

    /// <summary>
    /// Calculates success rate as a percentage
    /// </summary>
    public double SuccessRatePercent => TotalCategories > 0 ? (SuccessCount / (double)TotalCategories) * 100.0 : 0.0;

    /// <summary>
    /// Determines if the result is acceptable based on continue-on-error policy
    /// </summary>
    public bool IsAcceptable => Success || (SuccessCount > 0 && SuccessRatePercent >= 50.0);

    /// <summary>
    /// Gets a summary of the processing result
    /// </summary>
    public string GetSummary()
    {
        var summary = new System.Text.StringBuilder();
        summary.AppendLine(CultureInfo.InvariantCulture, $"Level {Level} Processing Summary:");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Success: {Success}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Categories: {SuccessCount}/{TotalCategories} ({SuccessRatePercent:F1}%)");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Processing Time: {ProcessingTimeMinutes:F2} minutes");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Memory Usage: {MemoryUsageMB:F2} MB");
        
        if (Errors.Count > 0)
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"  Errors: {Errors.Count}");
        }

        return summary.ToString();
    }

    /// <summary>
    /// Gets a string representation for logging
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"LevelFetchResult[Level={Level}, Success={Success}, {SuccessCount}/{TotalCategories}, {ProcessingTimeMinutes:F2}min, {MemoryUsageMB:F2}MB]");
    }
}

/// <summary>
/// Input model for ProcessCategoryLevelActivity
/// Contains all parameters needed for bulk category creation processing in Azure Functions
/// </summary>
public class LevelProcessingActivityInput
{
    /// <summary>
    /// Level-specific processing request with category IDs and bulk configuration
    /// </summary>
    public LevelProcessingRequest? ProcessingRequest { get; set; }

    /// <summary>
    /// Overall discovery request with source store and migration context
    /// </summary>
    public EntityDiscoveryRequest? DiscoveryRequest { get; set; }

    /// <summary>
    /// Timeout in minutes for the activity (max 4 minutes for Azure Functions)
    /// </summary>
    public double TimeoutMinutes { get; set; } = 4.0;

    /// <summary>
    /// Validates the input parameters for Azure Functions constraints
    /// </summary>
    /// <returns>Validation result with any errors</returns>
    public Services.ValidationResult Validate()
    {
        var result = new Services.ValidationResult();

        if (ProcessingRequest == null)
        {
            result.Errors.Add("ProcessingRequest is required for level processing activity");
        }

        if (DiscoveryRequest == null)
        {
            result.Errors.Add("DiscoveryRequest is required for level processing activity");
        }

        if (TimeoutMinutes <= 0)
        {
            result.Errors.Add("TimeoutMinutes must be positive");
        }

        if (TimeoutMinutes > 5.0) // Azure Functions timeout limit
        {
            result.Errors.Add("TimeoutMinutes cannot exceed 5 minutes (Azure Functions limit)");
        }

        return result;
    }

    /// <summary>
    /// Gets a string representation of the input for logging
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, 
            $"LevelProcessingActivityInput[Level={ProcessingRequest?.Level ?? -1}, Categories={ProcessingRequest?.CategoryIds?.Count ?? 0}, Migration={DiscoveryRequest?.MigrationId ?? "null"}]");
    }
}

/// <summary>
/// Result model for ProcessCategoryLevelActivity
/// Contains bulk processing results, performance metrics, and error information
/// </summary>
public class LevelProcessingActivityResult
{
    /// <summary>
    /// Whether the level processing completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The hierarchy level that was processed
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Total number of categories attempted for processing
    /// </summary>
    public int TotalCategories { get; set; }

    /// <summary>
    /// Number of categories successfully processed
    /// </summary>
    public int ProcessedCategories { get; set; }

    /// <summary>
    /// Number of categories that failed to process
    /// </summary>
    public int FailedCategories { get; set; }

    /// <summary>
    /// Number of batches processed during bulk operations
    /// </summary>
    public int BatchesProcessed { get; set; }

    /// <summary>
    /// Processing time in minutes
    /// </summary>
    public double ProcessingTimeMinutes { get; set; }

    /// <summary>
    /// Memory usage during processing (MB)
    /// </summary>
    public double MemoryUsageMB { get; set; }

    /// <summary>
    /// Whether bulk creation was enabled and used
    /// </summary>
    public bool BulkCreationEnabled { get; set; }

    /// <summary>
    /// Performance improvement factor (e.g., 7.5 = 7.5x faster than individual creation)
    /// </summary>
    public double PerformanceImprovement { get; set; }

    /// <summary>
    /// Peak memory usage during processing (MB)
    /// </summary>
    public double MemoryPeakMB { get; set; }

    /// <summary>
    /// Baseline processing time (what individual creation would have taken)
    /// </summary>
    public double BaselineProcessingTimeMinutes { get; set; }

    /// <summary>
    /// Actual bulk processing time
    /// </summary>
    public double BulkProcessingTimeMinutes { get; set; }

    /// <summary>
    /// List of batch-level errors encountered during processing
    /// </summary>
    public List<BatchProcessingError> BatchErrors { get; set; } = new List<BatchProcessingError>();

    /// <summary>
    /// List of memory warnings during processing
    /// </summary>
    public List<string> MemoryWarnings { get; set; } = new List<string>();

    /// <summary>
    /// Category ID mappings (source ID -> target ID) from bulk creation
    /// </summary>
    public Dictionary<string, string> CategoryIdMappings { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Calculates success rate as a percentage
    /// </summary>
    public double SuccessRatePercent => TotalCategories > 0 ? (ProcessedCategories / (double)TotalCategories) * 100.0 : 0.0;

    /// <summary>
    /// Determines if the result is acceptable based on continue-on-error policy
    /// </summary>
    public bool IsAcceptable => Success || (ProcessedCategories > 0 && SuccessRatePercent >= 70.0);

    /// <summary>
    /// Gets a summary of the bulk processing result
    /// </summary>
    public string GetSummary()
    {
        var summary = new System.Text.StringBuilder();
        summary.AppendLine(CultureInfo.InvariantCulture, $"Level {Level} Bulk Processing Summary:");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Success: {Success}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Categories: {ProcessedCategories}/{TotalCategories} ({SuccessRatePercent:F1}%)");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Batches: {BatchesProcessed}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Processing Time: {ProcessingTimeMinutes:F2} minutes");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Memory Usage: {MemoryUsageMB:F2} MB (Peak: {MemoryPeakMB:F2} MB)");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Bulk Creation: {BulkCreationEnabled}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Performance: {PerformanceImprovement:F1}x improvement");
        
        if (BatchErrors.Count > 0)
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"  Batch Errors: {BatchErrors.Count}");
        }

        return summary.ToString();
    }

    /// <summary>
    /// Gets a string representation for logging
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"LevelProcessingResult[Level={Level}, Success={Success}, {ProcessedCategories}/{TotalCategories}, {BatchesProcessed}batches, {PerformanceImprovement:F1}x, {ProcessingTimeMinutes:F2}min, {MemoryUsageMB:F2}MB]");
    }
}

/// <summary>
/// Request model for level processing with bulk creation capabilities
/// </summary>
public class LevelProcessingRequest
{
    /// <summary>
    /// The hierarchy level being processed
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// List of category IDs to process at this level
    /// </summary>
    public List<string> CategoryIds { get; set; } = new List<string>();

    /// <summary>
    /// Target store configuration for bulk creation
    /// </summary>
    public StoreConfiguration? TargetStore { get; set; }

    /// <summary>
    /// Batch size for bulk processing operations
    /// </summary>
    public int BatchSize { get; set; } = 25;

    /// <summary>
    /// Whether to enable bulk creation API for performance improvement
    /// </summary>
    public bool EnableBulkCreation { get; set; } = true;

    /// <summary>
    /// Gets a string representation for logging
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"LevelProcessingRequest[Level={Level}, Categories={CategoryIds.Count}, BatchSize={BatchSize}, BulkEnabled={EnableBulkCreation}]");
    }
}

/// <summary>
/// Batch processing error model for tracking bulk creation failures
/// </summary>
public class BatchProcessingError
{
    /// <summary>
    /// The batch number that encountered the error
    /// </summary>
    public int BatchNumber { get; set; }

    /// <summary>
    /// The type of error that occurred
    /// </summary>
    public ProcessingErrorType ErrorType { get; set; }

    /// <summary>
    /// Error message description
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// List of category IDs that failed in this batch
    /// </summary>
    public List<string> FailedCategoryIds { get; set; } = new List<string>();

    /// <summary>
    /// Timestamp when the error occurred
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Exception details if available
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Gets a string representation for logging
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"BatchError[Batch={BatchNumber}, Type={ErrorType}, Failed={FailedCategoryIds.Count}, Message={ErrorMessage}]");
    }
}

/// <summary>
/// Result model for BulkCategoryTransformService
/// Contains comprehensive metrics and results for bulk category transformation operations
/// </summary>
public class BulkCategoryTransformationResult
{
    /// <summary>
    /// Whether the bulk transformation completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Total number of categories attempted for transformation
    /// </summary>
    public int TotalCategories { get; set; }

    /// <summary>
    /// Number of categories successfully transformed
    /// </summary>
    public int ProcessedCategories { get; set; }

    /// <summary>
    /// Number of categories that failed to transform
    /// </summary>
    public int FailedCategories { get; set; }

    /// <summary>
    /// Number of batches processed during bulk transformation
    /// </summary>
    public int BatchesProcessed { get; set; }

    /// <summary>
    /// Total processing time in minutes
    /// </summary>
    public double ProcessingTimeMinutes { get; set; }

    /// <summary>
    /// Memory usage during transformation (MB)
    /// </summary>
    public double MemoryUsageMB { get; set; }

    /// <summary>
    /// Number of parent ID mappings applied
    /// </summary>
    public int ParentIdMappingsApplied { get; set; }

    /// <summary>
    /// Number of tree ID assignments applied
    /// </summary>
    public int TreeIdAssignmentsApplied { get; set; }

    /// <summary>
    /// List of successfully transformed categories
    /// </summary>
    public List<Dictionary<string, object>> TransformedCategories { get; set; } = new List<Dictionary<string, object>>();

    /// <summary>
    /// List of transformation errors encountered
    /// </summary>
    public List<string> TransformationErrors { get; set; } = new List<string>();

    /// <summary>
    /// Category ID mappings (source ID -> transformed data) for tracking
    /// </summary>
    public Dictionary<string, Dictionary<string, object>> CategoryMappings { get; set; } = new Dictionary<string, Dictionary<string, object>>();

    /// <summary>
    /// Calculates success rate as a percentage
    /// </summary>
    public double SuccessRatePercent => TotalCategories > 0 ? (ProcessedCategories / (double)TotalCategories) * 100.0 : 0.0;

    /// <summary>
    /// Calculates throughput in categories per minute
    /// </summary>
    public double ThroughputCategoriesPerMinute => ProcessingTimeMinutes > 0 ? ProcessedCategories / ProcessingTimeMinutes : 0.0;

    /// <summary>
    /// Calculates average transformation time per category in milliseconds
    /// </summary>
    public double AverageTransformationTimeMs => ProcessedCategories > 0 ? (ProcessingTimeMinutes * 60 * 1000) / ProcessedCategories : 0.0;

    /// <summary>
    /// Determines if the result is acceptable based on continue-on-error policy
    /// </summary>
    public bool IsAcceptable => Success || (ProcessedCategories > 0 && SuccessRatePercent >= 80.0);

    /// <summary>
    /// Gets a comprehensive summary of the bulk transformation result
    /// </summary>
    public string GetSummary()
    {
        var summary = new System.Text.StringBuilder();
        summary.AppendLine(CultureInfo.InvariantCulture, $"Bulk Category Transformation Summary:");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Success: {Success}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Categories: {ProcessedCategories}/{TotalCategories} ({SuccessRatePercent:F1}%)");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Batches: {BatchesProcessed}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Processing Time: {ProcessingTimeMinutes:F2} minutes");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Memory Usage: {MemoryUsageMB:F2} MB");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Throughput: {ThroughputCategoriesPerMinute:F1} categories/min");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Tree ID Assignments: {TreeIdAssignmentsApplied}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Parent ID Mappings: {ParentIdMappingsApplied}");
        
        if (TransformationErrors.Count > 0)
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"  Transformation Errors: {TransformationErrors.Count}");
        }

        return summary.ToString();
    }

    /// <summary>
    /// Gets a string representation for logging
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"BulkTransformResult[Success={Success}, {ProcessedCategories}/{TotalCategories}, {BatchesProcessed}batches, {SuccessRatePercent:F1}%, {ProcessingTimeMinutes:F2}min, {MemoryUsageMB:F2}MB]");
    }
}

/// <summary>
/// Result model for BulkCategoryCreationService
/// Contains comprehensive metrics and results for optimized bulk category creation with BigCommerce API
/// </summary>
public class BulkCategoryCreationResult
{
    /// <summary>
    /// Whether the bulk creation completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Total number of categories attempted for creation
    /// </summary>
    public int TotalCategories { get; set; }

    /// <summary>
    /// Number of categories successfully created
    /// </summary>
    public int CreatedCategoriesCount => CreatedCategories.Count;

    /// <summary>
    /// Number of categories that failed to create
    /// </summary>
    public int FailedCategories { get; set; }

    /// <summary>
    /// Total number of API calls made (vs individual calls for comparison)
    /// </summary>
    public int TotalApiCalls { get; set; }

    /// <summary>
    /// Optimal batch size calculated for this operation
    /// </summary>
    public int OptimalBatchSize { get; set; }

    /// <summary>
    /// Total processing time in minutes
    /// </summary>
    public double ProcessingTimeMinutes { get; set; }

    /// <summary>
    /// Memory usage during bulk creation (MB)
    /// </summary>
    public double MemoryUsageMB { get; set; }

    /// <summary>
    /// Performance improvement factor (e.g., 7.5 = 7.5x faster than individual creation)
    /// </summary>
    public double PerformanceImprovement { get; set; }

    /// <summary>
    /// Average API calls per second (for rate limit compliance)
    /// </summary>
    public double AverageApiCallsPerSecond { get; set; }

    /// <summary>
    /// Whether rate limit compliance was maintained
    /// </summary>
    public bool RateLimitCompliance { get; set; }

    /// <summary>
    /// Network overhead reduction percentage
    /// </summary>
    public double NetworkOverheadReduction { get; set; }



    /// <summary>
    /// List of successfully created categories with destination IDs
    /// </summary>
    public List<Dictionary<string, object>> CreatedCategories { get; set; } = new List<Dictionary<string, object>>();

    /// <summary>
    /// Source ID -> Destination ID mappings collected from bulk responses
    /// </summary>
    public Dictionary<string, string> IdMappingsCollected { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Results for each batch processed
    /// </summary>
    public List<BatchCreationResult> BatchResults { get; set; } = new List<BatchCreationResult>();

    /// <summary>
    /// List of API errors encountered during processing
    /// </summary>
    public List<string> ApiErrors { get; set; } = new List<string>();

    /// <summary>
    /// Calculates success rate as a percentage
    /// </summary>
    public double SuccessRatePercent => TotalCategories > 0 ? (CreatedCategoriesCount / (double)TotalCategories) * 100.0 : 0.0;

    /// <summary>
    /// Calculates throughput in categories per minute
    /// </summary>
    public double ThroughputCategoriesPerMinute => ProcessingTimeMinutes > 0 ? CreatedCategoriesCount / ProcessingTimeMinutes : 0.0;

    /// <summary>
    /// Calculates individual call comparison (how many individual calls would have been needed)
    /// </summary>
    public int IndividualCallsEquivalent => TotalCategories;

    /// <summary>
    /// Calculates API call reduction percentage
    /// </summary>
    public double ApiCallReductionPercent => TotalCategories > 0 ? ((TotalCategories - TotalApiCalls) / (double)TotalCategories) * 100.0 : 0.0;

    /// <summary>
    /// Determines if the result is acceptable based on continue-on-error policy
    /// </summary>
    public bool IsAcceptable => Success || (CreatedCategoriesCount > 0 && SuccessRatePercent >= 80.0);

    /// <summary>
    /// Gets a comprehensive summary of the bulk creation result
    /// </summary>
    public string GetSummary()
    {
        var summary = new System.Text.StringBuilder();
        summary.AppendLine(CultureInfo.InvariantCulture, $"Bulk Category Creation Summary:");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Success: {Success}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Categories: {CreatedCategoriesCount}/{TotalCategories} ({SuccessRatePercent:F1}%)");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  API Calls: {TotalApiCalls} (vs {IndividualCallsEquivalent} individual calls)");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  API Call Reduction: {ApiCallReductionPercent:F1}%");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Processing Time: {ProcessingTimeMinutes:F2} minutes");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Memory Usage: {MemoryUsageMB:F2} MB");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Performance: {PerformanceImprovement:F1}x improvement");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Throughput: {ThroughputCategoriesPerMinute:F1} categories/min");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Optimal Batch Size: {OptimalBatchSize}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Rate Limit Compliance: {RateLimitCompliance}");
        
        if (ApiErrors.Count > 0)
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"  API Errors: {ApiErrors.Count}");
        }



        return summary.ToString();
    }

    /// <summary>
    /// Gets a string representation for logging
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"BulkCreationResult[Success={Success}, {CreatedCategoriesCount}/{TotalCategories}, {TotalApiCalls}calls, {PerformanceImprovement:F1}x, {ProcessingTimeMinutes:F2}min, {MemoryUsageMB:F2}MB]");
    }
}

/// <summary>
/// Result model for individual batch creation within bulk operation
/// </summary>
public class BatchCreationResult
{
    /// <summary>
    /// Batch number in the sequence
    /// </summary>
    public int BatchNumber { get; set; }

    /// <summary>
    /// Whether this batch was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of categories in this batch
    /// </summary>
    public int CategoriesInBatch { get; set; }

    /// <summary>
    /// Number of categories successfully created in this batch
    /// </summary>
    public int CreatedCount { get; set; }

    /// <summary>
    /// Number of categories that failed in this batch
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Processing time for this batch in milliseconds
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Error message if batch failed
    /// </summary>
    public string? ErrorMessage { get; set; }



    /// <summary>
    /// Categories created in this batch
    /// </summary>
    public List<Dictionary<string, object>> CreatedCategories { get; set; } = new List<Dictionary<string, object>>();

    /// <summary>
    /// Gets a string representation for logging
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"BatchResult[Batch={BatchNumber}, Success={Success}, {CreatedCount}/{CategoriesInBatch}, {ProcessingTimeMs:F0}ms]");
    }
}