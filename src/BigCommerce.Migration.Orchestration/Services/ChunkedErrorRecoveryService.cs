using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Error recovery and fallback mechanisms service for chunked category migration
/// Task 5.3.3: Implements fallback to individual creation, adaptive batch sizing, and error aggregation
/// CRITICAL: Follows continue-on-error policy and no-retry constraints
/// </summary>
public class ChunkedErrorRecoveryService
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly IEntityMappingService _entityMappingService;
    private readonly ChunkedErrorHandlingService _errorHandlingService;
    private readonly ILogger<ChunkedErrorRecoveryService> _logger;

    // Recovery configuration constants
    private const int MIN_BATCH_SIZE = 5;
    private const int FALLBACK_BATCH_SIZE = 10;
    private const int MAX_INDIVIDUAL_FALLBACK_COUNT = 50; // Limit individual fallbacks to prevent overwhelming API
    private const double BATCH_SIZE_REDUCTION_FACTOR = 0.6; // Reduce batch size by 40% on failure

    public ChunkedErrorRecoveryService(
        IBigCommerceApiClient apiClient,
        IEntityMappingService entityMappingService,
        ChunkedErrorHandlingService errorHandlingService,
        ILogger<ChunkedErrorRecoveryService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _entityMappingService = entityMappingService ?? throw new ArgumentNullException(nameof(entityMappingService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Attempts fallback to individual category creation for failed batches
    /// CRITICAL: Implements continue-on-error policy without retries
    /// </summary>
    /// <param name="failedBatch">Categories that failed in bulk creation</param>
    /// <param name="batchNumber">Original batch number that failed</param>
    /// <param name="level">Hierarchy level being processed</param>
    /// <param name="batchRequest">Original batch processing request</param>
    /// <param name="originalException">Original exception from bulk creation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recovery result with individual creation outcomes</returns>
    public async Task<IndividualFallbackResult> AttemptIndividualFallbackAsync(
        List<Dictionary<string, object>> failedBatch,
        int batchNumber,
        int level,
        BatchProcessingRequest batchRequest,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var result = new IndividualFallbackResult
        {
            OriginalBatchSize = failedBatch.Count,
            AttemptedIndividualCreations = 0,
            SuccessfulIndividualCreations = 0,
            FailedIndividualCreations = 0,
            IndividualResults = new List<IndividualCreationResult>()
        };

        _logger.LogWarning("🔄 FALLBACK INITIATED: Attempting individual creation for failed batch {BatchNumber} with {CategoryCount} categories. Original error: {OriginalError}",
            batchNumber, failedBatch.Count, originalException.Message);

        // ✅ NOTE: We deliberately DO NOT log the batch failure to OpenSearch here to avoid double-logging
        // Each category will be logged individually only when its individual creation attempt fails
        // This ensures exactly ONE OpenSearch log entry per category failure (not batch + individual)

        try
        {
            // ✅ CRITICAL: Limit individual fallbacks to prevent API overload
            var categoriesToRetry = failedBatch.Take(MAX_INDIVIDUAL_FALLBACK_COUNT).ToList();
            
            if (failedBatch.Count > MAX_INDIVIDUAL_FALLBACK_COUNT)
            {
                _logger.LogWarning("⚠️ FALLBACK LIMITATION: Only processing first {MaxCount} categories individually (original batch: {OriginalCount})",
                    MAX_INDIVIDUAL_FALLBACK_COUNT, failedBatch.Count);
                result.ExceededFallbackLimit = true;
                result.SkippedCategories = failedBatch.Count - MAX_INDIVIDUAL_FALLBACK_COUNT;
            }

            result.AttemptedIndividualCreations = categoriesToRetry.Count;

            // Process each category individually
            for (int i = 0; i < categoriesToRetry.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var category = categoriesToRetry[i];
                var categoryId = ExtractCategoryId(category);
                var individualResult = new IndividualCreationResult
                {
                    SourceCategoryId = categoryId,
                    Success = false,
                    AttemptNumber = i + 1
                };

                try
                {
                    var stopwatch = Stopwatch.StartNew();

                    _logger.LogDebug("🔍 INDIVIDUAL CREATION: Processing category {CategoryId} ({Index}/{Total})",
                        categoryId, i + 1, categoriesToRetry.Count);

                    // ✅ CRITICAL: Single API attempt only - NO RETRY LOGIC
                    var createdCategories = await _apiClient.CreateCategoriesAsync(
                        batchRequest.DestinationStore,
                        batchRequest.CategoryTreeContext?.DestinationCategoryTreeId ?? "1",
                        new List<Dictionary<string, object>> { category },
                        cancellationToken);

                    stopwatch.Stop();
                    individualResult.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;

                    if (createdCategories != null && createdCategories.Any())
                    {
                        var createdCategory = createdCategories.First();
                        var destinationId = createdCategory.GetValueOrDefault("id")?.ToString();

                        if (!string.IsNullOrEmpty(destinationId))
                        {
                            individualResult.Success = true;
                            individualResult.DestinationCategoryId = destinationId;
                            result.SuccessfulIndividualCreations++;

                            // Store ID mapping
                            await StoreIndividualCategoryMapping(categoryId, destinationId, batchRequest, cancellationToken);

                            _logger.LogDebug("✅ INDIVIDUAL SUCCESS: Category {CategoryId} created as {DestinationId}",
                                categoryId, destinationId);
                        }
                        else
                        {
                            individualResult.ErrorMessage = "Created category returned without ID";
                            result.FailedIndividualCreations++;
                            
                            await _errorHandlingService.LogIndividualCategoryFailureAsync(
                                new InvalidOperationException("Created category returned without ID"),
                                category, categoryId, batchNumber, level, batchRequest.MigrationId,
                                null, cancellationToken);
                        }
                    }
                    else
                    {
                        individualResult.ErrorMessage = "API returned empty result";
                        result.FailedIndividualCreations++;
                        
                        await _errorHandlingService.LogIndividualCategoryFailureAsync(
                            new InvalidOperationException("API returned empty result"),
                            category, categoryId, batchNumber, level, batchRequest.MigrationId,
                            null, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    // ✅ CRITICAL: Continue-on-error policy - log but don't stop processing
                    individualResult.Success = false;
                    individualResult.ErrorMessage = ex.Message;
                    result.FailedIndividualCreations++;

                    await _errorHandlingService.LogIndividualCategoryFailureAsync(
                        ex, category, categoryId, batchNumber, level, batchRequest.MigrationId,
                        null, cancellationToken);

                    _logger.LogError(ex, "❌ INDIVIDUAL FAILURE: Category {CategoryId} failed individual creation: {ErrorMessage}",
                        categoryId, ex.Message);
                }

                result.IndividualResults.Add(individualResult);

                // Small delay between individual creations to respect rate limits
                if (i < categoriesToRetry.Count - 1)
                {
                    await Task.Delay(100, cancellationToken); // 100ms delay = max 10 req/sec
                }
            }

            // Calculate recovery success rate
            result.RecoverySuccessRate = result.AttemptedIndividualCreations > 0
                ? (double)result.SuccessfulIndividualCreations / result.AttemptedIndividualCreations
                : 0.0;

            _logger.LogInformation("🎯 FALLBACK COMPLETE: {SuccessCount}/{AttemptCount} individual creations successful ({SuccessRate:P2} recovery rate)",
                result.SuccessfulIndividualCreations, result.AttemptedIndividualCreations, result.RecoverySuccessRate);
        }
        catch (Exception ex)
        {
            // ✅ CRITICAL: Continue-on-error policy - log but don't throw
            _logger.LogError(ex, "💥 FALLBACK ERROR: Failed to complete individual fallback for batch {BatchNumber}. Original error: {OriginalError}",
                batchNumber, originalException.Message);
            result.FallbackException = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Calculates adaptive batch size based on error history and failure patterns
    /// CRITICAL: Implements intelligent size reduction to minimize future failures
    /// </summary>
    /// <param name="originalBatchSize">Original batch size that failed</param>
    /// <param name="consecutiveFailures">Number of consecutive batch failures</param>
    /// <param name="errorType">Type of error that occurred</param>
    /// <returns>Recommended adaptive batch size</returns>
    public int CalculateAdaptiveBatchSize(int originalBatchSize, int consecutiveFailures, string errorType)
    {
        _logger.LogDebug("🧮 ADAPTIVE SIZING: Original={OriginalSize}, Failures={FailureCount}, ErrorType={ErrorType}",
            originalBatchSize, consecutiveFailures, errorType);

        var adaptiveBatchSize = originalBatchSize;

        // Apply reduction based on consecutive failures
        for (int i = 0; i < consecutiveFailures; i++)
        {
            adaptiveBatchSize = (int)(adaptiveBatchSize * BATCH_SIZE_REDUCTION_FACTOR);
        }

        // Apply error-type specific adjustments
        switch (errorType?.ToLowerInvariant())
        {
            case "rate_limit":
            case "too_many_requests":
                adaptiveBatchSize = Math.Max(adaptiveBatchSize / 2, MIN_BATCH_SIZE); // Aggressive reduction for rate limits
                break;
            case "timeout":
                adaptiveBatchSize = Math.Max((int)(adaptiveBatchSize * 0.7), MIN_BATCH_SIZE); // Moderate reduction for timeouts
                break;
            case "validation":
            case "bad_request":
                adaptiveBatchSize = FALLBACK_BATCH_SIZE; // Fixed smaller size for validation issues
                break;
            default:
                // General failure - use calculated reduction
                break;
        }

        // Ensure minimum batch size
        adaptiveBatchSize = Math.Max(adaptiveBatchSize, MIN_BATCH_SIZE);

        _logger.LogInformation("📊 ADAPTIVE SIZING RESULT: {OriginalSize} → {AdaptiveSize} (reduction applied: {ReductionFactor:P1})",
            originalBatchSize, adaptiveBatchSize, 1.0 - (double)adaptiveBatchSize / originalBatchSize);

        return adaptiveBatchSize;
    }

    /// <summary>
    /// Aggregates errors across multiple batches and levels for comprehensive reporting
    /// CRITICAL: Provides dashboard-ready error summaries
    /// </summary>
    /// <param name="levelResults">Results from all levels processed</param>
    /// <param name="migrationId">Migration ID for context</param>
    /// <returns>Aggregated error report for dashboard display</returns>
    public ErrorAggregationReport AggregateErrorsAcrossLevels(
        List<object> levelResults, // Generic to handle different result types
        string migrationId)
    {
        var report = new ErrorAggregationReport
        {
            MigrationId = migrationId,
            LevelErrorSummaries = new List<LevelErrorSummary>(),
            OverallErrorCategories = new Dictionary<string, int>(),
            RecommendedActions = new List<string>()
        };

        _logger.LogInformation("📊 AGGREGATING ERRORS: Processing results for migration {MigrationId}", migrationId);

        try
        {
            var totalErrors = 0;
            var totalBatches = 0;
            var totalFallbacks = 0;

            foreach (var levelResult in levelResults)
            {
                var levelSummary = ExtractLevelErrorSummary(levelResult);
                if (levelSummary != null)
                {
                    report.LevelErrorSummaries.Add(levelSummary);
                    totalErrors += levelSummary.TotalErrors;
                    totalBatches += levelSummary.TotalBatches;
                    totalFallbacks += levelSummary.FallbacksAttempted;

                    // Categorize errors
                    foreach (var errorCategory in levelSummary.ErrorCategories)
                    {
                        report.OverallErrorCategories[errorCategory.Key] = 
                            report.OverallErrorCategories.GetValueOrDefault(errorCategory.Key, 0) + errorCategory.Value;
                    }
                }
            }

            // Calculate overall metrics
            report.TotalErrors = totalErrors;
            report.TotalBatches = totalBatches;
            report.TotalFallbacksAttempted = totalFallbacks;
            report.OverallErrorRate = totalBatches > 0 ? (double)totalErrors / totalBatches : 0.0;

            // Generate recommendations based on error patterns
            GenerateErrorBasedRecommendations(report);

            _logger.LogInformation("📋 ERROR AGGREGATION COMPLETE: {TotalErrors} errors across {TotalBatches} batches ({ErrorRate:P2} error rate)",
                totalErrors, totalBatches, report.OverallErrorRate);
        }
        catch (Exception ex)
        {
            // ✅ CRITICAL: Continue-on-error policy
            _logger.LogError(ex, "💥 Failed to aggregate errors for migration {MigrationId}", migrationId);
            report.AggregationError = ex.Message;
        }

        return report;
    }

    #region Helper Methods

    /// <summary>
    /// Extracts category ID from category data
    /// </summary>
    private string ExtractCategoryId(Dictionary<string, object> category)
    {
        return category.GetValueOrDefault("_original_entity_id")?.ToString() ??
               category.GetValueOrDefault("id")?.ToString() ??
               category.GetValueOrDefault("source_id")?.ToString() ??
               $"unknown_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Stores ID mapping for individually created category
    /// </summary>
    private async Task StoreIndividualCategoryMapping(
        string sourceId, 
        string destinationId, 
        BatchProcessingRequest batchRequest, 
        CancellationToken cancellationToken)
    {
        try
        {
            var entityMapping = new EntityMapping
            {
                MigrationId = batchRequest.MigrationId,
                EntityType = "categories",
                SourceId = sourceId,
                DestinationId = destinationId,
                SourceStoreId = batchRequest.SourceStore.StoreId ?? "",
                DestinationStoreId = batchRequest.DestinationStore.StoreId ?? "",
                Status = "mapped_individual_fallback"
            };

            await _entityMappingService.StoreEntityMappingAsync(entityMapping, cancellationToken);
        }
        catch (Exception ex)
        {
            // ✅ CRITICAL: Continue-on-error policy - log but don't throw
            _logger.LogWarning(ex, "Failed to store individual category mapping for {SourceId} -> {DestinationId}",
                sourceId, destinationId);
        }
    }

    /// <summary>
    /// Extracts error summary from level result object
    /// </summary>
    private LevelErrorSummary? ExtractLevelErrorSummary(object levelResult)
    {
        try
        {
            // Use reflection to extract error information from various result types
            var type = levelResult.GetType();
            var summary = new LevelErrorSummary
            {
                ErrorCategories = new Dictionary<string, int>()
            };

            // Try to extract common properties
            summary.Level = ExtractIntProperty(type, levelResult, "Level") ?? 0;
            summary.TotalBatches = ExtractIntProperty(type, levelResult, "TotalBatches") ?? 0;
            summary.TotalErrors = ExtractIntProperty(type, levelResult, "TotalErrors") ?? 0;
            summary.FallbacksAttempted = ExtractIntProperty(type, levelResult, "FallbacksAttempted") ?? 0;

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract level error summary from result object");
            return null;
        }
    }

    /// <summary>
    /// Extracts integer property using reflection
    /// </summary>
    private int? ExtractIntProperty(Type type, object obj, string propertyName)
    {
        try
        {
            var property = type.GetProperty(propertyName);
            var value = property?.GetValue(obj);
            return value is int intValue ? intValue : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generates recommendations based on error patterns
    /// </summary>
    private void GenerateErrorBasedRecommendations(ErrorAggregationReport report)
    {
        if (report.OverallErrorRate > 0.5)
        {
            report.RecommendedActions.Add("🚨 High error rate detected - consider reviewing source data quality");
        }

        if (report.OverallErrorCategories.ContainsKey("rate_limit"))
        {
            report.RecommendedActions.Add("⏱️ Rate limit errors detected - consider reducing batch sizes");
        }

        if (report.OverallErrorCategories.ContainsKey("timeout"))
        {
            report.RecommendedActions.Add("⏰ Timeout errors detected - consider reducing batch sizes or checking network connectivity");
        }

        if (report.TotalFallbacksAttempted > report.TotalBatches * 0.3)
        {
            report.RecommendedActions.Add("🔄 High fallback rate - consider using smaller initial batch sizes");
        }

        if (report.RecommendedActions.Count == 0)
        {
            report.RecommendedActions.Add("✅ Error patterns within acceptable thresholds");
        }
    }

    #endregion
}

#region Data Models

/// <summary>
/// Result of individual fallback creation attempt
/// </summary>
public class IndividualFallbackResult
{
    public int OriginalBatchSize { get; set; }
    public int AttemptedIndividualCreations { get; set; }
    public int SuccessfulIndividualCreations { get; set; }
    public int FailedIndividualCreations { get; set; }
    public double RecoverySuccessRate { get; set; }
    public bool ExceededFallbackLimit { get; set; }
    public int SkippedCategories { get; set; }
    public string? FallbackException { get; set; }
    public List<IndividualCreationResult> IndividualResults { get; set; } = new();
}

/// <summary>
/// Result of individual category creation within fallback
/// </summary>
public class IndividualCreationResult
{
    public string SourceCategoryId { get; set; } = "";
    public string? DestinationCategoryId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptNumber { get; set; }
    public double ProcessingTimeMs { get; set; }
}

/// <summary>
/// Aggregated error report for dashboard display
/// </summary>
public class ErrorAggregationReport
{
    public string MigrationId { get; set; } = "";
    public int TotalErrors { get; set; }
    public int TotalBatches { get; set; }
    public int TotalFallbacksAttempted { get; set; }
    public double OverallErrorRate { get; set; }
    public List<LevelErrorSummary> LevelErrorSummaries { get; set; } = new();
    public Dictionary<string, int> OverallErrorCategories { get; set; } = new();
    public List<string> RecommendedActions { get; set; } = new();
    public string? AggregationError { get; set; }
}

/// <summary>
/// Error summary for a specific hierarchy level
/// </summary>
public class LevelErrorSummary
{
    public int Level { get; set; }
    public int TotalBatches { get; set; }
    public int TotalErrors { get; set; }
    public int FallbacksAttempted { get; set; }
    public Dictionary<string, int> ErrorCategories { get; set; } = new();
}

#endregion