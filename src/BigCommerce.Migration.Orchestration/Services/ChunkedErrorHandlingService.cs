using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Specialized error handling service for chunked category migration operations
/// Task 5.3.1: Extends EntityErrorHandlingService patterns for bulk creation errors and batch-level failures
/// CRITICAL: Implements continue-on-error policy, OpenSearch logging, and blob storage for large payloads
/// </summary>
public class ChunkedErrorHandlingService
{
    private readonly IOpenSearchService _openSearchService;
    private readonly IBlobService _blobService;
    private readonly IEntityErrorHandlingService _baseErrorHandlingService;
    private readonly ILogger<ChunkedErrorHandlingService> _logger;

    public ChunkedErrorHandlingService(
        IOpenSearchService openSearchService,
        IBlobService blobService,
        IEntityErrorHandlingService baseErrorHandlingService,
        ILogger<ChunkedErrorHandlingService> logger)
    {
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
        _baseErrorHandlingService = baseErrorHandlingService ?? throw new ArgumentNullException(nameof(baseErrorHandlingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Logs batch-level API failures with detailed context for bulk category creation
    /// CRITICAL: Uses OpenSearch for structured logging, blob storage for large payloads
    /// </summary>
    /// <param name="exception">The exception that occurred during batch processing</param>
    /// <param name="bulkRequest">Bulk creation request that failed</param>
    /// <param name="batchNumber">Current batch number in the level</param>
    /// <param name="totalBatches">Total batches in the level</param>
    /// <param name="categoriesInBatch">Categories that were being processed in this batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task LogBatchLevelApiFailureAsync(
        Exception exception,
        object bulkRequest,
        int batchNumber,
        int totalBatches,
        List<Dictionary<string, object>> categoriesInBatch,
        CancellationToken cancellationToken)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        try
        {
            _logger.LogError(exception, "🚨 BATCH-LEVEL API FAILURE: Batch {BatchNumber}/{TotalBatches} failed with {CategoryCount} categories. Error: {ErrorMessage}",
                batchNumber, totalBatches, categoriesInBatch?.Count ?? 0, exception.Message);

            // Extract context from bulk request
            var migrationId = ExtractMigrationIdFromRequest(bulkRequest);
            var level = ExtractLevelFromRequest(bulkRequest);
            var sourceStoreId = ExtractSourceStoreIdFromRequest(bulkRequest);
            var destinationStoreId = ExtractDestinationStoreIdFromRequest(bulkRequest);

            // Extract response payload from exception
            var responsePayload = _baseErrorHandlingService.ExtractResponsePayloadFromException(exception);

            // Create request payload from bulk request and categories
            string? requestPayload = null;
            PayloadReference? requestPayloadRef = null;
            PayloadReference? responsePayloadRef = null;

            if (bulkRequest != null || (categoriesInBatch != null && categoriesInBatch.Any()))
            {
                var requestData = new
                {
                    BulkRequest = bulkRequest,
                    Categories = categoriesInBatch,
                    BatchInfo = new
                    {
                        BatchNumber = batchNumber,
                        TotalBatches = totalBatches,
                        CategoriesInBatch = categoriesInBatch?.Count ?? 0
                    }
                };

                requestPayload = JsonSerializer.Serialize(requestData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                // Store error payloads to blob storage
                var requestId = $"chunked_batch_{level}_{batchNumber}_{DateTime.UtcNow:yyyyMMddHHmmss}";
                (requestPayloadRef, responsePayloadRef) = await _baseErrorHandlingService.StoreErrorPayloadsAsync(
                    migrationId ?? "unknown",
                    requestId,
                    requestPayload,
                    responsePayload,
                    $"categories_batch_{level}_{batchNumber}",
                    cancellationToken);
            }

            // Structured error data for OpenSearch
            var errorData = new
            {
                migrationId = migrationId,
                entityType = "categories",
                errorType = "batch_level_api_failure", // 🎯 SPECIFIC ERROR TYPE for chunked operations
                errorMessage = exception.Message,
                stackTrace = GetTruncatedStackTrace(exception.StackTrace),
                httpStatusCode = ExtractHttpStatusFromException(exception),
                
                // 🎯 BATCH-SPECIFIC CONTEXT
                batchNumber = batchNumber,
                totalBatches = totalBatches,
                categoriesInBatch = categoriesInBatch?.Count ?? 0,
                level = level,
                
                // ✅ BLOB URLS (not raw payloads)
                requestPayloadBlobUrl = requestPayloadRef?.BlobUrl,
                responsePayloadBlobUrl = responsePayloadRef?.BlobUrl,
                
                // Store context
                sourceStoreId = sourceStoreId,
                destinationStoreId = destinationStoreId,
                
                // Additional metadata for chunked processing
                processingType = "chunked_hierarchical",
                failureContext = "bulk_category_creation"
            };

            // 🎯 CRITICAL: Log to OpenSearch for structured analysis
            var logResult = await _openSearchService.LogErrorAsync(
                "MigrationError_ChunkedCategories_BatchLevel",
                exception,
                errorData,
                cancellationToken);

            if (!logResult)
            {
                _logger.LogWarning("⚠️ Failed to log batch-level error to OpenSearch for batch {BatchNumber}/{TotalBatches}, migration {MigrationId}",
                    batchNumber, totalBatches, migrationId);
            }
            else
            {
                _logger.LogDebug("✅ Successfully logged batch-level error to OpenSearch for batch {BatchNumber}/{TotalBatches}, migration {MigrationId}",
                    batchNumber, totalBatches, migrationId);
            }
        }
        catch (Exception ex)
        {
            // 🎯 CRITICAL: Continue-on-error policy - log but don't throw
            _logger.LogError(ex, "💥 Failed to log batch-level API failure for batch {BatchNumber}/{TotalBatches}. Original error: {OriginalError}",
                batchNumber, totalBatches, exception.Message);
            // Don't re-throw to avoid masking the original error
        }
    }

    /// <summary>
    /// Logs individual category failures within a bulk creation batch
    /// CRITICAL: Maintains continue-on-error policy for individual failures
    /// </summary>
    /// <param name="exception">Exception that occurred for individual category</param>
    /// <param name="category">Category data that failed</param>
    /// <param name="categoryId">ID of the failed category</param>
    /// <param name="batchNumber">Batch number where failure occurred</param>
    /// <param name="level">Hierarchy level being processed</param>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="responsePayload">Optional API response payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task LogIndividualCategoryFailureAsync(
        Exception exception,
        Dictionary<string, object> category,
        string categoryId,
        int batchNumber,
        int level,
        string migrationId,
        string? responsePayload = null,
        CancellationToken cancellationToken = default)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        try
        {
            _logger.LogError(exception, "🔍 INDIVIDUAL CATEGORY FAILURE: Category {CategoryId} failed in batch {BatchNumber}, level {Level}. Error: {ErrorMessage}",
                categoryId, batchNumber, level, exception.Message);

            // Extract or use provided response payload
            var finalResponsePayload = responsePayload ?? _baseErrorHandlingService.ExtractResponsePayloadFromException(exception);

            // Create request payload from category data
            var requestPayload = JsonSerializer.Serialize(category, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Store error payloads to blob storage
            var sanitizedCategoryId = SanitizeEntityIdForBlobName(categoryId);
            var requestId = $"chunked_category_{level}_{batchNumber}_{sanitizedCategoryId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
            var (requestPayloadRef, responsePayloadRef) = await _baseErrorHandlingService.StoreErrorPayloadsAsync(
                migrationId,
                requestId,
                requestPayload,
                finalResponsePayload,
                $"category_{level}_{sanitizedCategoryId}",
                cancellationToken);

            // Extract category name for better error tracking
            var categoryName = ExtractCategoryName(category);

            // Structured error data for OpenSearch
            var errorData = new
            {
                migrationId = migrationId,
                entityType = "categories",
                errorType = "individual_category_failure", // 🎯 SPECIFIC ERROR TYPE
                errorMessage = exception.Message,
                stackTrace = GetTruncatedStackTrace(exception.StackTrace),
                httpStatusCode = ExtractHttpStatusFromException(exception),
                
                // 🎯 CATEGORY-SPECIFIC CONTEXT
                categoryId = categoryId,
                categoryName = categoryName,
                level = level,
                batchNumber = batchNumber,
                
                // ✅ BLOB URLS (not raw payloads)
                requestPayloadBlobUrl = requestPayloadRef?.BlobUrl,
                responsePayloadBlobUrl = responsePayloadRef?.BlobUrl,
                
                // Additional metadata
                processingType = "chunked_hierarchical",
                failureContext = "individual_category_creation"
            };

            // 🎯 CRITICAL: Log to OpenSearch for structured analysis
            var logResult = await _openSearchService.LogErrorAsync(
                "MigrationError_ChunkedCategories_Individual",
                exception,
                errorData,
                cancellationToken);

            if (!logResult)
            {
                _logger.LogWarning("⚠️ Failed to log individual category error to OpenSearch for category {CategoryId}, migration {MigrationId}",
                    categoryId, migrationId);
            }
            else
            {
                _logger.LogDebug("✅ Successfully logged individual category error to OpenSearch for category {CategoryId}, migration {MigrationId}",
                    categoryId, migrationId);
            }
        }
        catch (Exception ex)
        {
            // 🎯 CRITICAL: Continue-on-error policy - log but don't throw
            _logger.LogError(ex, "💥 Failed to log individual category failure for category {CategoryId}. Original error: {OriginalError}",
                categoryId, exception.Message);
            // Don't re-throw to avoid masking the original error
        }
    }

    /// <summary>
    /// Logs level completion errors when an entire hierarchy level fails
    /// CRITICAL: Provides context for level-by-level processing failures
    /// </summary>
    /// <param name="exception">Exception that caused level failure</param>
    /// <param name="level">Hierarchy level that failed</param>
    /// <param name="totalCategories">Total categories in the level</param>
    /// <param name="processedCategories">Categories processed before failure</param>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task LogLevelCompletionErrorAsync(
        Exception exception,
        int level,
        int totalCategories,
        int processedCategories,
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        try
        {
            _logger.LogError(exception, "🌊 LEVEL COMPLETION ERROR: Level {Level} failed after processing {ProcessedCategories}/{TotalCategories} categories. Error: {ErrorMessage}",
                level, processedCategories, totalCategories, exception.Message);

            // Extract response payload from exception
            var responsePayload = _baseErrorHandlingService.ExtractResponsePayloadFromException(exception);

            // Create contextual request payload
            var requestData = new
            {
                Level = level,
                TotalCategories = totalCategories,
                ProcessedCategories = processedCategories,
                FailurePoint = new
                {
                    ProcessedPercentage = totalCategories > 0 ? (double)processedCategories / totalCategories * 100 : 0,
                    RemainingCategories = totalCategories - processedCategories
                }
            };

            var requestPayload = JsonSerializer.Serialize(requestData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Store error payloads to blob storage
            var requestId = $"chunked_level_{level}_completion_{DateTime.UtcNow:yyyyMMddHHmmss}";
            var (requestPayloadRef, responsePayloadRef) = await _baseErrorHandlingService.StoreErrorPayloadsAsync(
                migrationId,
                requestId,
                requestPayload,
                responsePayload,
                $"level_{level}_completion",
                cancellationToken);

            // Structured error data for OpenSearch
            var errorData = new
            {
                migrationId = migrationId,
                entityType = "categories",
                errorType = "level_completion_failure", // 🎯 SPECIFIC ERROR TYPE
                errorMessage = exception.Message,
                stackTrace = GetTruncatedStackTrace(exception.StackTrace),
                httpStatusCode = ExtractHttpStatusFromException(exception),
                
                // 🎯 LEVEL-SPECIFIC CONTEXT
                level = level,
                totalCategories = totalCategories,
                processedCategories = processedCategories,
                remainingCategories = totalCategories - processedCategories,
                processedPercentage = totalCategories > 0 ? (double)processedCategories / totalCategories * 100 : 0,
                
                // ✅ BLOB URLS (not raw payloads)
                requestPayloadBlobUrl = requestPayloadRef?.BlobUrl,
                responsePayloadBlobUrl = responsePayloadRef?.BlobUrl,
                
                // Additional metadata
                processingType = "chunked_hierarchical",
                failureContext = "level_completion"
            };

            // 🎯 CRITICAL: Log to OpenSearch for structured analysis
            var logResult = await _openSearchService.LogErrorAsync(
                "MigrationError_ChunkedCategories_LevelCompletion",
                exception,
                errorData,
                cancellationToken);

            if (!logResult)
            {
                _logger.LogWarning("⚠️ Failed to log level completion error to OpenSearch for level {Level}, migration {MigrationId}",
                    level, migrationId);
            }
            else
            {
                _logger.LogDebug("✅ Successfully logged level completion error to OpenSearch for level {Level}, migration {MigrationId}",
                    level, migrationId);
            }
        }
        catch (Exception ex)
        {
            // 🎯 CRITICAL: Continue-on-error policy - log but don't throw
            _logger.LogError(ex, "💥 Failed to log level completion error for level {Level}. Original error: {OriginalError}",
                level, exception.Message);
            // Don't re-throw to avoid masking the original error
        }
    }

    #region Helper Methods

    /// <summary>
    /// Extracts migration ID from bulk request object
    /// </summary>
    private string? ExtractMigrationIdFromRequest(object? request)
    {
        if (request == null) return null;

        try
        {
            // Use reflection to find migration ID property
            var type = request.GetType();
            var migrationIdProperty = type.GetProperty("MigrationId") ?? type.GetProperty("migrationId");
            return migrationIdProperty?.GetValue(request)?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract migration ID from request");
            return null;
        }
    }

    /// <summary>
    /// Extracts hierarchy level from bulk request object
    /// </summary>
    private int? ExtractLevelFromRequest(object? request)
    {
        if (request == null) return null;

        try
        {
            var type = request.GetType();
            var levelProperty = type.GetProperty("Level") ?? type.GetProperty("level") ?? type.GetProperty("HierarchyLevel");
            var levelValue = levelProperty?.GetValue(request);
            return levelValue is int intLevel ? intLevel : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract level from request");
            return null;
        }
    }

    /// <summary>
    /// Extracts source store ID from bulk request object
    /// </summary>
    private string? ExtractSourceStoreIdFromRequest(object? request)
    {
        if (request == null) return null;

        try
        {
            var type = request.GetType();
            var sourceStoreProperty = type.GetProperty("SourceStore") ?? type.GetProperty("sourceStore");
            var sourceStore = sourceStoreProperty?.GetValue(request);
            if (sourceStore != null)
            {
                var storeIdProperty = sourceStore.GetType().GetProperty("StoreId") ?? sourceStore.GetType().GetProperty("storeId");
                return storeIdProperty?.GetValue(sourceStore)?.ToString();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract source store ID from request");
            return null;
        }
    }

    /// <summary>
    /// Extracts destination store ID from bulk request object
    /// </summary>
    private string? ExtractDestinationStoreIdFromRequest(object? request)
    {
        if (request == null) return null;

        try
        {
            var type = request.GetType();
            var destStoreProperty = type.GetProperty("DestinationStore") ?? type.GetProperty("destinationStore");
            var destStore = destStoreProperty?.GetValue(request);
            if (destStore != null)
            {
                var storeIdProperty = destStore.GetType().GetProperty("StoreId") ?? destStore.GetType().GetProperty("storeId");
                return storeIdProperty?.GetValue(destStore)?.ToString();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract destination store ID from request");
            return null;
        }
    }

    /// <summary>
    /// Extracts category name from category data for better error tracking
    /// </summary>
    private string? ExtractCategoryName(Dictionary<string, object> category)
    {
        if (category == null) return null;

        try
        {
            // Try common category name fields
            var nameKeys = new[] { "name", "Name", "category_name", "categoryName", "title", "Title" };
            foreach (var key in nameKeys)
            {
                if (category.TryGetValue(key, out var nameValue) && nameValue != null)
                {
                    return nameValue.ToString();
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract category name from category data");
            return null;
        }
    }

    /// <summary>
    /// Sanitizes entity ID for blob name usage (same as EntityErrorHandlingService)
    /// </summary>
    private string SanitizeEntityIdForBlobName(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return "unknown";

        // Replace characters that are problematic in blob names
        return entityId.Replace("/", "_")
                       .Replace("\\", "_")
                       .Replace(":", "_")
                       .Replace("?", "_")
                       .Replace("#", "_")
                       .Replace("[", "_")
                       .Replace("]", "_")
                       .Replace("@", "_")
                       .Replace("!", "_")
                       .Replace("$", "_")
                       .Replace("&", "_")
                       .Replace("'", "_")
                       .Replace("(", "_")
                       .Replace(")", "_")
                       .Replace("*", "_")
                       .Replace("+", "_")
                       .Replace(",", "_")
                       .Replace(";", "_")
                       .Replace("=", "_")
                       .Replace(" ", "_");
    }

    /// <summary>
    /// Truncates stack trace to reasonable length for OpenSearch storage
    /// </summary>
    private string? GetTruncatedStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
            return null;

        const int maxLength = 5000; // Reasonable limit for OpenSearch
        return stackTrace.Length > maxLength 
            ? stackTrace.Substring(0, maxLength) + "... [truncated]"
            : stackTrace;
    }

    /// <summary>
    /// Extracts HTTP status code from exception (same pattern as EntityErrorHandlingService)
    /// </summary>
    private int? ExtractHttpStatusFromException(Exception exception)
    {
        if (exception == null)
            return null;

        try
        {
            // Check exception data for status code
            if (exception.Data.Contains("StatusCode"))
            {
                var statusCodeValue = exception.Data["StatusCode"];
                if (statusCodeValue is int intStatusCode)
                    return intStatusCode;
                if (statusCodeValue is HttpStatusCode httpStatusCode)
                    return (int)httpStatusCode;
                if (int.TryParse(statusCodeValue?.ToString(), out var parsedStatusCode))
                    return parsedStatusCode;
            }

            // Check for HttpRequestException patterns
            if (exception is HttpRequestException)
            {
                var message = exception.Message;
                if (message.Contains("400")) return 400;
                if (message.Contains("401")) return 401;
                if (message.Contains("403")) return 403;
                if (message.Contains("404")) return 404;
                if (message.Contains("429")) return 429;
                if (message.Contains("500")) return 500;
                if (message.Contains("502")) return 502;
                if (message.Contains("503")) return 503;
                if (message.Contains("504")) return 504;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract HTTP status from exception");
            return null;
        }
    }

    #endregion
}