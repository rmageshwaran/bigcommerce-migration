using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace BigCommerce.Migration.Functions.Middleware;

/// <summary>
/// Global exception handler middleware for consistent error responses
/// Catches unhandled exceptions and returns standardized error responses
/// </summary>
public class GlobalExceptionHandlerMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Handles exceptions and creates appropriate error responses
    /// </summary>
    private async Task HandleExceptionAsync(FunctionContext context, Exception exception)
    {
        var httpRequestData = await context.GetHttpRequestDataAsync();
        if (httpRequestData == null)
        {
            // Not an HTTP function, log and rethrow
            _logger.LogError(exception, "Unhandled exception in non-HTTP function");
            throw exception;
        }

        var requestPath = httpRequestData.Url.AbsolutePath;
        var requestMethod = httpRequestData.Method;
        var requestId = await GetRequestIdAsync(context);

        // Log the exception with context
        _logger.LogError(exception, 
            "Unhandled exception in {Method} {Path}. RequestId: {RequestId}",
            requestMethod, requestPath, requestId);

        // Determine appropriate response based on exception type
        var errorResponse = CreateErrorResponse(exception, requestId);
        
        await SetErrorResponse(context, errorResponse);
    }

    /// <summary>
    /// Creates standardized error response based on exception type
    /// </summary>
    private ErrorResponseInfo CreateErrorResponse(Exception exception, string requestId)
    {
        return exception switch
        {
            ArgumentNullException nullEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.BadRequest,
                ErrorCode = "NULL_ARGUMENT",
                Message = "Required parameter is null",
                Details = $"Parameter '{nullEx.ParamName}' cannot be null",
                RequestId = requestId
            },
            
            ArgumentException argEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.BadRequest,
                ErrorCode = "INVALID_ARGUMENT",
                Message = "Invalid argument provided",
                Details = argEx.Message,
                RequestId = requestId
            },
            
            UnauthorizedAccessException => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.Unauthorized,
                ErrorCode = "UNAUTHORIZED",
                Message = "Authentication required",
                Details = "Access denied. Please provide valid authentication credentials.",
                RequestId = requestId
            },
            
            InvalidOperationException invalidEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.Conflict,
                ErrorCode = "INVALID_OPERATION",
                Message = "Operation not allowed in current state",
                Details = invalidEx.Message,
                RequestId = requestId
            },
            
            NotSupportedException notSupportedEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.BadRequest,
                ErrorCode = "NOT_SUPPORTED",
                Message = "Operation not supported",
                Details = notSupportedEx.Message,
                RequestId = requestId
            },
            
            TimeoutException timeoutEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.RequestTimeout,
                ErrorCode = "TIMEOUT",
                Message = "Request timed out",
                Details = timeoutEx.Message,
                RequestId = requestId
            },
            
            OperationCanceledException canceledEx => new ErrorResponseInfo
            {
                StatusCode = canceledEx is TaskCanceledException && canceledEx.InnerException is TimeoutException 
                    ? HttpStatusCode.RequestTimeout 
                    : HttpStatusCode.BadRequest,
                ErrorCode = canceledEx is TaskCanceledException && canceledEx.InnerException is TimeoutException 
                    ? "TIMEOUT" 
                    : "OPERATION_CANCELLED",
                Message = canceledEx is TaskCanceledException && canceledEx.InnerException is TimeoutException 
                    ? "Request timed out" 
                    : "Operation was cancelled",
                Details = canceledEx is TaskCanceledException && canceledEx.InnerException is TimeoutException 
                    ? "The request took too long to process and was cancelled"
                    : "The operation was cancelled before completion",
                RequestId = requestId
            },
            
            JsonException jsonEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.BadRequest,
                ErrorCode = "INVALID_JSON",
                Message = "Invalid JSON format",
                Details = jsonEx.Message,
                RequestId = requestId
            },
            
            HttpRequestException httpEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.BadGateway,
                ErrorCode = "EXTERNAL_SERVICE_ERROR",
                Message = "External service error",
                Details = "Failed to communicate with external service",
                RequestId = requestId
            },
            
            // Security-related exceptions
            System.Security.SecurityException secEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.Forbidden,
                ErrorCode = "SECURITY_ERROR",
                Message = "Security violation",
                Details = "Access denied due to security policy",
                RequestId = requestId
            },
            
            // Database/Storage exceptions
            InvalidDataException dataEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.BadRequest,
                ErrorCode = "INVALID_DATA",
                Message = "Invalid data format",
                Details = dataEx.Message,
                RequestId = requestId
            },
            
            // File/IO exceptions
            FileNotFoundException fileEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "FILE_NOT_FOUND",
                Message = "Required file not found",
                Details = "A required file could not be located",
                RequestId = requestId
            },
            
            DirectoryNotFoundException dirEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "DIRECTORY_NOT_FOUND",
                Message = "Required directory not found",
                Details = "A required directory could not be located",
                RequestId = requestId
            },
            
            IOException ioEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.InternalServerError,
                ErrorCode = "IO_ERROR",
                Message = "Input/output error occurred",
                Details = "An I/O error occurred while processing the request",
                RequestId = requestId
            },
            
            // Network exceptions
            System.Net.Sockets.SocketException socketEx => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                ErrorCode = "NETWORK_ERROR",
                Message = "Network connectivity error",
                Details = "Failed to establish network connection",
                RequestId = requestId
            },
            
            // Memory exceptions
            OutOfMemoryException => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.InternalServerError,
                ErrorCode = "OUT_OF_MEMORY",
                Message = "Insufficient memory",
                Details = "The system is low on memory resources",
                RequestId = requestId
            },
            
            // Stack overflow (very rare but possible)
            StackOverflowException => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.InternalServerError,
                ErrorCode = "STACK_OVERFLOW",
                Message = "Stack overflow error",
                Details = "The request caused a stack overflow",
                RequestId = requestId
            },
            
            // Default case for unhandled exceptions
            _ => new ErrorResponseInfo
            {
                StatusCode = HttpStatusCode.InternalServerError,
                ErrorCode = "INTERNAL_ERROR",
                Message = "An unexpected error occurred",
                Details = GetSafeErrorMessage(exception),
                RequestId = requestId
            }
        };
    }

    /// <summary>
    /// Gets a safe error message that doesn't expose sensitive information
    /// </summary>
    private string GetSafeErrorMessage(Exception exception)
    {
        // In development, return full exception details
        // In production, return generic message to avoid information disclosure
        var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")?.Equals("Development", StringComparison.OrdinalIgnoreCase) == true;
        
        if (isDevelopment)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
        
        // Generic message for production to avoid exposing internal details
        return "An internal server error occurred. Please try again later or contact support if the issue persists.";
    }

    /// <summary>
    /// Sets error response on the HTTP context
    /// </summary>
    private async Task SetErrorResponse(FunctionContext context, ErrorResponseInfo errorInfo)
    {
        var response = context.GetHttpResponseData();
        if (response == null) return;

        response.StatusCode = errorInfo.StatusCode;
        response.Headers.Add("Content-Type", "application/json");
        
        // Add error tracking headers
        response.Headers.Add("X-Error-Code", errorInfo.ErrorCode);
        response.Headers.Add("X-Request-ID", errorInfo.RequestId);

        var errorResponse = new ApiErrorResponse
        {
            Error = new ErrorDetails
            {
                Code = errorInfo.ErrorCode,
                Message = errorInfo.Message,
                Details = string.IsNullOrEmpty(errorInfo.Details) 
                    ? new List<ValidationError>() 
                    : new List<ValidationError> 
                    {
                        new ValidationError
                        {
                            Field = "general",
                            Message = errorInfo.Details,
                            Code = errorInfo.ErrorCode
                        }
                    },
                Timestamp = DateTime.UtcNow,
                RequestId = errorInfo.RequestId,
                Documentation = GetDocumentationUrl(errorInfo.ErrorCode)
            }
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, jsonOptions));

        // Log error response for monitoring
        _logger.LogWarning(
            "Error response sent: {StatusCode} {ErrorCode} for {RequestId}", 
            errorInfo.StatusCode, errorInfo.ErrorCode, errorInfo.RequestId);
    }

    /// <summary>
    /// Gets documentation URL for specific error codes
    /// </summary>
    private string GetDocumentationUrl(string errorCode)
    {
        var baseUrl = "https://docs.bigcommerce-migration.com/api/errors";
        
        return errorCode.ToLowerInvariant() switch
        {
            "validation_error" => $"{baseUrl}/validation",
            "unauthorized" => $"{baseUrl}/authentication",
            "invalid_argument" => $"{baseUrl}/arguments",
            "timeout" => $"{baseUrl}/timeouts",
            "external_service_error" => $"{baseUrl}/external-services",
            "rate_limit_exceeded" => $"{baseUrl}/rate-limiting",
            _ => $"{baseUrl}/general"
        };
    }

    /// <summary>
    /// Gets request ID from context or generates a new one
    /// </summary>
    private async Task<string> GetRequestIdAsync(FunctionContext context)
    {
        // Try to get request ID from various sources
        if (context.Items.TryGetValue("RequestId", out var requestId) && requestId != null)
        {
            return requestId.ToString()!;
        }

        // Try to get from HTTP headers
        var httpRequest = await context.GetHttpRequestDataAsync();
        if (httpRequest?.Headers.TryGetValues("X-Request-ID", out var headerValues) == true)
        {
            var headerValue = headerValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(headerValue))
            {
                return headerValue;
            }
        }

        // Generate new request ID
        var newRequestId = Guid.NewGuid().ToString();
        context.Items["RequestId"] = newRequestId;
        return newRequestId;
    }
}

/// <summary>
/// Internal class for error response information
/// </summary>
internal class ErrorResponseInfo
{
    public HttpStatusCode StatusCode { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
} 