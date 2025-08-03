using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Result model for level-by-level category processing
/// Implements continue-on-error policy and comprehensive performance tracking
/// Designed for Azure Durable Functions deterministic operations
/// </summary>
/// <remarks>
/// This class tracks processing results for individual hierarchy levels in chunked category migration.
/// Supports the continue-on-error policy by allowing processing to complete despite individual failures.
/// 
/// Performance Optimization:
/// - Tracks throughput metrics for BigCommerce bulk creation API optimization
/// - Monitors memory usage for Azure Functions constraint compliance
/// - Provides detailed error categorization for debugging and retry logic
/// - Enables performance analysis for 5-10x improvement validation
/// </remarks>
public class LevelProcessingResult
{
    /// <summary>
    /// The hierarchy level this result represents (0-based)
    /// Level 0 = root categories, Level 1 = first-level children, etc.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Number of categories successfully processed at this level
    /// Used for success rate calculation and progress tracking
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of categories that failed to process at this level
    /// Used for failure rate calculation and error analysis
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Total number of categories attempted at this level
    /// Should equal SuccessCount + FailureCount for complete processing
    /// </summary>
    public int TotalCategories { get; set; }

    /// <summary>
    /// Collection of detailed error information for failed categories
    /// Preserves full context for debugging and retry logic
    /// </summary>
    public List<ProcessingError> Errors { get; set; } = new List<ProcessingError>();

    /// <summary>
    /// List of category IDs discovered at this level
    /// Used for passing discovered categories to processing activities
    /// </summary>
    public List<string> CategoryIds { get; set; } = new List<string>();

    /// <summary>
    /// Total processing time for this level in minutes
    /// Used for throughput calculation and performance analysis
    /// </summary>
    public double ProcessingTimeMinutes { get; set; }

    /// <summary>
    /// Peak memory usage during level processing in MB
    /// Used for Azure Functions memory constraint validation
    /// </summary>
    public double PeakMemoryUsageMB { get; set; }

    /// <summary>
    /// Success rate as a percentage (0.0 to 1.0)
    /// Calculated as SuccessCount / TotalCategories
    /// </summary>
    public double SuccessRate
    {
        get
        {
            if (TotalCategories == 0)
                return 1.0; // 100% success rate for empty processing (no failures)
            
            return (double)SuccessCount / TotalCategories;
        }
    }

    /// <summary>
    /// Failure rate as a percentage (0.0 to 1.0)
    /// Calculated as FailureCount / TotalCategories
    /// </summary>
    public double FailureRate
    {
        get
        {
            if (TotalCategories == 0)
                return 0.0; // 0% failure rate for empty processing
            
            return (double)FailureCount / TotalCategories;
        }
    }

    /// <summary>
    /// Processing throughput in categories per minute
    /// Used for performance analysis and optimization validation
    /// </summary>
    public double ThroughputCategoriesPerMinute
    {
        get
        {
            if (ProcessingTimeMinutes <= 0)
                return 0.0;
            
            return TotalCategories / ProcessingTimeMinutes;
        }
    }

    /// <summary>
    /// Whether processing for this level is completed
    /// True when all categories have been processed (success or failure)
    /// </summary>
    public bool IsCompleted
    {
        get
        {
            return (SuccessCount + FailureCount) >= TotalCategories;
        }
    }

    /// <summary>
    /// Whether this level had any processing failures
    /// Used for continue-on-error policy validation
    /// </summary>
    public bool HasFailures
    {
        get
        {
            return FailureCount > 0;
        }
    }

    /// <summary>
    /// Whether this result meets performance targets for chunked processing
    /// Validates throughput, memory usage, and error rate thresholds
    /// </summary>
    public bool IsWithinPerformanceTargets
    {
        get
        {
            // Performance targets for chunked category migration:
            // - Throughput: >= 300 categories/minute (with bulk creation optimization)
            // - Memory usage: <= 50MB (Azure Functions constraint)
            // - Failure rate: <= 5% (continue-on-error tolerance)
            
            var throughputTarget = ThroughputCategoriesPerMinute >= 300.0;
            var memoryTarget = PeakMemoryUsageMB <= 50.0;
            var errorRateTarget = FailureRate <= 0.05; // 5% max failure rate
            
            return throughputTarget && memoryTarget && errorRateTarget;
        }
    }

    /// <summary>
    /// Adds an error to the processing result and updates failure count
    /// </summary>
    /// <param name="error">The processing error to add</param>
    /// <remarks>
    /// This method supports the continue-on-error policy by tracking errors
    /// without stopping the overall processing workflow.
    /// </remarks>
    public void AddError(ProcessingError error)
    {
        if (error == null)
            throw new ArgumentNullException(nameof(error));

        Errors.Add(error);
        FailureCount = Errors.Count;
    }

    /// <summary>
    /// Increments the success count for a successfully processed category
    /// </summary>
    public void AddSuccess()
    {
        SuccessCount++;
    }

    /// <summary>
    /// Increments the failure count (used when adding errors in bulk)
    /// </summary>
    public void AddFailure()
    {
        FailureCount++;
    }

    /// <summary>
    /// Gets errors grouped by error type for analysis
    /// </summary>
    /// <returns>Dictionary mapping error types to lists of errors</returns>
    public Dictionary<ProcessingErrorType, List<ProcessingError>> GetErrorsByType()
    {
        return Errors.GroupBy(e => e.ErrorType)
                    .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Gets a summary of errors by type with counts
    /// </summary>
    /// <returns>Formatted error summary string</returns>
    public string GetErrorSummary()
    {
        var errorsByType = GetErrorsByType();
        var summary = new StringBuilder();

        foreach (var kvp in errorsByType)
        {
            if (summary.Length > 0)
                summary.Append(", ");
            
            var displayName = kvp.Key switch
            {
                ProcessingErrorType.ApiError => "API Error",
                ProcessingErrorType.ValidationError => "Validation Error",
                ProcessingErrorType.SystemError => "System Error",
                ProcessingErrorType.TimeoutError => "Timeout Error",
                ProcessingErrorType.NetworkError => "Network Error",
                _ => kvp.Key.ToString()
            };
            
            summary.Append(CultureInfo.InvariantCulture, $"{displayName}: {kvp.Value.Count}");
        }

        return summary.ToString();
    }

    /// <summary>
    /// Gets a comprehensive summary of the processing result
    /// </summary>
    /// <returns>Formatted summary string for logging and reporting</returns>
    public string GetSummary()
    {
        var summary = new StringBuilder();
        summary.AppendLine(CultureInfo.InvariantCulture, $"Level {Level} Processing Result:");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Total Categories: {TotalCategories}");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Successful: {SuccessCount} ({SuccessRate:P1})");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Failed: {FailureCount} ({FailureRate:P1})");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Processing Time: {ProcessingTimeMinutes:F2} minutes");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Throughput: {ThroughputCategoriesPerMinute:F1} categories/minute");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Peak Memory: {PeakMemoryUsageMB:F1} MB");
        summary.AppendLine(CultureInfo.InvariantCulture, $"  Performance Targets Met: {IsWithinPerformanceTargets}");
        
        if (HasFailures)
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"  Error Summary: {GetErrorSummary()}");
        }

        return summary.ToString();
    }
}

/// <summary>
/// Detailed error information for category processing failures
/// Preserves comprehensive context for debugging and retry logic
/// Supports error categorization for automated handling strategies
/// </summary>
/// <remarks>
/// This class provides complete error context for failed category operations in chunked migration.
/// Designed to support the continue-on-error policy by preserving all necessary information
/// for debugging, retry logic, and error analysis without stopping the overall process.
/// 
/// Error categorization enables:
/// - Automated retry strategies for transient errors
/// - Detailed debugging for persistent failures
/// - Performance analysis and optimization insights
/// - Compliance with BigCommerce API error handling best practices
/// </remarks>
public class ProcessingError
{
    /// <summary>
    /// The unique identifier of the category that failed to process
    /// Used for retry operations and error correlation
    /// </summary>
    public string? CategoryId { get; set; }

    /// <summary>
    /// Human-readable error message describing what went wrong
    /// Used for logging and debugging purposes
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Categorized error type for automated handling strategies
    /// Determines retry eligibility and debugging approach
    /// </summary>
    public ProcessingErrorType ErrorType { get; set; }

    /// <summary>
    /// Timestamp when the error occurred (UTC)
    /// Used for temporal analysis and debugging
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The original exception that caused the processing failure
    /// Preserves full stack trace and exception details for debugging
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Raw API response that led to the error (if applicable)
    /// Preserves BigCommerce API error details for analysis
    /// </summary>
    public string? ApiResponse { get; set; }

    /// <summary>
    /// The category data that was being processed when the error occurred
    /// Enables reproduction of the error and data validation analysis
    /// </summary>
    public Dictionary<string, object>? CategoryData { get; set; }

    /// <summary>
    /// Number of retry attempts made for this category
    /// Used for retry logic and exponential backoff strategies
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Whether this error type is eligible for retry
    /// Based on error categorization and transient failure patterns
    /// </summary>
    public bool IsRetryable
    {
        get
        {
            return ErrorType switch
            {
                ProcessingErrorType.ApiError => true,      // API errors (rate limits, timeouts) are retryable
                ProcessingErrorType.NetworkError => true,  // Network issues are retryable
                ProcessingErrorType.TimeoutError => true,  // Timeout errors are retryable
                ProcessingErrorType.ValidationError => false, // Validation errors are not retryable
                ProcessingErrorType.SystemError => false,  // System errors are not retryable
                _ => false
            };
        }
    }

    /// <summary>
    /// Gets detailed error information for debugging and logging
    /// </summary>
    /// <returns>Comprehensive error details as formatted string</returns>
    public string GetDetailedInfo()
    {
        var info = new StringBuilder();
        info.AppendLine(CultureInfo.InvariantCulture, $"Processing Error Details:");
        info.AppendLine(CultureInfo.InvariantCulture, $"  Category ID: {CategoryId ?? "Unknown"}");
        info.AppendLine(CultureInfo.InvariantCulture, $"  Error Type: {ErrorType}");
        info.AppendLine(CultureInfo.InvariantCulture, $"  Message: {ErrorMessage ?? "No message"}");
        info.AppendLine(CultureInfo.InvariantCulture, $"  Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        info.AppendLine(CultureInfo.InvariantCulture, $"  Retry Count: {RetryCount}");
        info.AppendLine(CultureInfo.InvariantCulture, $"  Is Retryable: {IsRetryable}");

        if (Exception != null)
        {
            info.AppendLine(CultureInfo.InvariantCulture, $"  Exception: {Exception.GetType().Name}");
            info.AppendLine(CultureInfo.InvariantCulture, $"  Exception Message: {Exception.Message}");
        }

        if (!string.IsNullOrEmpty(ApiResponse))
        {
            info.AppendLine(CultureInfo.InvariantCulture, $"  API Response: {ApiResponse}");
        }

        if (CategoryData != null && CategoryData.Count > 0)
        {
            info.AppendLine(CultureInfo.InvariantCulture, $"  Category Data: {CategoryData.Count} fields");
            foreach (var kvp in CategoryData.Take(5)) // Limit to first 5 fields for readability
            {
                info.AppendLine(CultureInfo.InvariantCulture, $"    {kvp.Key}: {kvp.Value}");
            }
        }

        return info.ToString();
    }

    /// <summary>
    /// Returns a concise string representation of the error
    /// </summary>
    /// <returns>Formatted error summary for logging</returns>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{ErrorType}: {ErrorMessage} (Category: {CategoryId ?? "Unknown"}, Retries: {RetryCount})");
    }
}

/// <summary>
/// Enumeration of processing error types for categorization and handling strategies
/// Enables automated retry logic and debugging workflows
/// </summary>
/// <remarks>
/// Error categorization supports:
/// - Retry eligibility determination (transient vs permanent errors)
/// - Debugging workflow optimization (API vs validation vs system issues)
/// - Performance analysis (error patterns and frequencies)
/// - BigCommerce API-specific error handling strategies
/// </remarks>
public enum ProcessingErrorType
{
    /// <summary>
    /// API-related errors (rate limits, authentication, server errors)
    /// Generally retryable with exponential backoff
    /// </summary>
    ApiError,

    /// <summary>
    /// Data validation errors (invalid category data, constraint violations)
    /// Generally not retryable without data correction
    /// </summary>
    ValidationError,

    /// <summary>
    /// System-level errors (out of memory, infrastructure failures)
    /// Generally not retryable without system intervention
    /// </summary>
    SystemError,

    /// <summary>
    /// Request timeout errors (Azure Functions timeout, API timeout)
    /// Generally retryable with adjusted timeout settings
    /// </summary>
    TimeoutError,

    /// <summary>
    /// Network connectivity errors (DNS, connection failures)
    /// Generally retryable with exponential backoff
    /// </summary>
    NetworkError
}