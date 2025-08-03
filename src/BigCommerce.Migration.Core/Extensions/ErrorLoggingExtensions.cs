using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace BigCommerce.Migration.Core.Extensions;

/// <summary>
/// Extension methods for standardized error logging across the BigCommerce Migration system.
/// Ensures consistent OpenSearch metadata structure and proper user visibility categorization.
/// </summary>
public static class ErrorLoggingExtensions
{
    /// <summary>
    /// Logs an error with standardized metadata structure to OpenSearch.
    /// Automatically categorizes errors for proper user visibility.
    /// </summary>
    /// <param name="openSearchService">OpenSearch service instance</param>
    /// <param name="logger">Logger for console output</param>
    /// <param name="exception">Exception to log</param>
    /// <param name="component">Service/Activity name (e.g., "ApiKeyService")</param>
    /// <param name="operationContext">Specific operation being performed (e.g., "ValidateApiKey")</param>
    /// <param name="migrationId">Migration ID for correlation (optional)</param>
    /// <param name="entityType">Entity type being processed (optional)</param>
    /// <param name="batchNumber">Batch number for batch operations (optional)</param>
    /// <param name="entityId">Specific entity ID (optional)</param>
    /// <param name="forceUserVisible">Force this error to be user-visible even if it's infrastructure-related</param>
    /// <returns>Task representing the async operation</returns>
    public static async Task LogStructuredErrorAsync(
        this IOpenSearchService openSearchService,
        ILogger logger,
        Exception exception,
        string component,
        string operationContext,
        string? migrationId = null,
        string? entityType = null,
        int? batchNumber = null,
        string? entityId = null,
        bool forceUserVisible = false)
    {
        // Console logging first (always happens)
        logger.LogError(exception, "{Component} operation failed: {Context}", component, operationContext);
        
        var errorType = CategorizeErrorType(exception);
        var severity = DetermineSeverity(exception);
        var isInfrastructure = IsInfrastructureException(exception);
        
        // Determine user visibility based on error type and override
        var category = ShouldBeUserVisible(exception, forceUserVisible) ? "Error" : "Internal";
        
        var metadata = new {
            migrationId,
            entityType,
            errorType,
            severity,
            component,
            operationContext,
            retryable = IsRetryable(exception),
            httpStatusCode = ExtractHttpStatusCode(exception),
            batchNumber,
            entityId,
            category = category,  // Controls user visibility
            isInfrastructure,
            timestamp = DateTime.UtcNow
        };
        
        try
        {
            await openSearchService.LogErrorAsync($"{component}.{operationContext}", exception, metadata).ConfigureAwait(false);
        }
        catch (Exception logEx)
        {
            // If OpenSearch logging fails, ensure we don't lose the error information
            logger.LogCritical(logEx, "🚨 FAILED TO LOG TO OPENSEARCH: Original error was {OriginalError}", exception.Message);
        }
    }

    /// <summary>
    /// Determines if an error should be visible to end users.
    /// Only BigCommerce API errors should be user-visible by default.
    /// </summary>
    private static bool ShouldBeUserVisible(Exception exception, bool forceUserVisible)
    {
        if (forceUserVisible) return true;
        
        // Only BigCommerce API errors should be user-visible
        return IsBigCommerceApiError(exception);
    }
    
    /// <summary>
    /// Determines if an exception is a BigCommerce API error that users should see.
    /// </summary>
    private static bool IsBigCommerceApiError(Exception exception)
    {
        if (exception is HttpRequestException httpEx)
        {
            // Check if it's a BigCommerce API response error
            var statusCode = ExtractHttpStatusCode(exception);
            return statusCode.HasValue && (
                statusCode == 400 ||  // Bad Request
                statusCode == 401 ||  // Unauthorized  
                statusCode == 403 ||  // Forbidden
                statusCode == 404 ||  // Not Found
                statusCode == 409 ||  // Conflict
                statusCode == 422 ||  // Unprocessable Entity
                statusCode == 429 ||  // Too Many Requests
                statusCode >= 500     // Server Errors
            );
        }
        
        // Data validation errors from BigCommerce
        if (exception is ArgumentException || exception is InvalidOperationException)
        {
            var message = exception.Message.ToLowerInvariant();
            return message.Contains("bigcommerce", StringComparison.OrdinalIgnoreCase) || 
                   message.Contains("api", StringComparison.OrdinalIgnoreCase) || 
                   message.Contains("category", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("product", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("validation", StringComparison.OrdinalIgnoreCase);
        }
        
        return false;
    }
    
    /// <summary>
    /// Determines if an exception is infrastructure-related and should stop migration.
    /// </summary>
    private static bool IsInfrastructureException(Exception exception)
    {
        return exception.Message.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("service", StringComparison.OrdinalIgnoreCase) ||
               exception is TimeoutException ||
               exception is SocketException ||
               exception is UnauthorizedAccessException ||
               (exception is InvalidOperationException && exception.Message.Contains("storage", StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Categorizes the exception type for error analysis.
    /// </summary>
    private static string CategorizeErrorType(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException _ => "ValidationError",
            ArgumentException _ => "ValidationError",
            HttpRequestException http when http.Message.Contains("429", StringComparison.OrdinalIgnoreCase) => "RateLimitError",
            HttpRequestException http when ExtractHttpStatusCode(http) >= 500 => "ApiServerError",
            HttpRequestException _ => "ApiError",
            TimeoutException _ => "TimeoutError",
            SocketException _ => "NetworkError",
            UnauthorizedAccessException _ => "AuthenticationError",
            InvalidOperationException inv when inv.Message.Contains("storage", StringComparison.OrdinalIgnoreCase) => "StorageError",
            InvalidOperationException _ => "OperationError",
            _ => "SystemError"
        };
    }
    
    /// <summary>
    /// Determines the severity level of the exception.
    /// </summary>
    private static string DetermineSeverity(Exception exception)
    {
        if (IsInfrastructureException(exception))
            return "Critical";
            
        if (exception is HttpRequestException httpEx)
        {
            var statusCode = ExtractHttpStatusCode(httpEx);
            if (statusCode >= 500) return "Error";
            if (statusCode == 429) return "Warning";
            return "Error";
        }
        
        if (exception is ArgumentException || exception is ArgumentNullException)
            return "Warning";
            
        return "Error";
    }
    
    /// <summary>
    /// Determines if the error type is retryable.
    /// </summary>
    private static bool IsRetryable(Exception exception)
    {
        return exception switch
        {
            TimeoutException _ => true,
            SocketException _ => true,
            HttpRequestException http when http.Message.Contains("429", StringComparison.OrdinalIgnoreCase) => true,
            HttpRequestException http when ExtractHttpStatusCode(http) >= 500 => true,
            _ => false
        };
    }
    
    /// <summary>
    /// Extracts HTTP status code from exception data or message.
    /// </summary>
    private static int? ExtractHttpStatusCode(Exception exception)
    {
        if (exception?.Data?.Contains("StatusCode") == true)
        {
            if (int.TryParse(exception.Data["StatusCode"]?.ToString(), out var statusCode))
            {
                return statusCode;
            }
        }
        
        // Check inner exceptions
        var innerException = exception?.InnerException;
        while (innerException != null)
        {
            if (innerException.Data?.Contains("StatusCode") == true)
            {
                if (int.TryParse(innerException.Data["StatusCode"]?.ToString(), out var innerStatusCode))
                {
                    return innerStatusCode;
                }
            }
            innerException = innerException.InnerException;
        }
        
        // Parse from message
        var message = exception?.Message?.ToLowerInvariant() ?? "";
        if (message.Contains("404", StringComparison.OrdinalIgnoreCase)) return 404;
        if (message.Contains("400", StringComparison.OrdinalIgnoreCase)) return 400;
        if (message.Contains("401", StringComparison.OrdinalIgnoreCase)) return 401;
        if (message.Contains("403", StringComparison.OrdinalIgnoreCase)) return 403;
        if (message.Contains("429", StringComparison.OrdinalIgnoreCase)) return 429;
        if (message.Contains("500", StringComparison.OrdinalIgnoreCase)) return 500;
        
        return null;
    }
}