using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BigCommerce.Migration.Functions.Middleware;

/// <summary>
/// Middleware for standardized request validation
/// Validates request bodies, headers, and parameters before reaching function logic
/// </summary>
public class RequestValidationMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<RequestValidationMiddleware> _logger;

    public RequestValidationMiddleware(ILogger<RequestValidationMiddleware> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpRequestData = await context.GetHttpRequestDataAsync();
        if (httpRequestData == null)
        {
            // Not an HTTP function, continue without validation
            await next(context);
            return;
        }

        try
        {
            // Perform request validation
            var validationResult = await ValidateRequestAsync(httpRequestData, context);
            
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Request validation failed for {Path}: {Errors}", 
                    httpRequestData.Url.AbsolutePath, 
                    string.Join(", ", validationResult.Errors.Select(e => e.Message)));

                await SetValidationErrorResponse(context, validationResult);
                return;
            }

            // Store validation context for downstream use
            context.Items["ValidationResult"] = validationResult;
            context.Items["RequestMetrics"] = new RequestMetrics
            {
                StartTime = DateTime.UtcNow,
                Path = httpRequestData.Url.AbsolutePath,
                Method = httpRequestData.Method,
                ContentLength = GetContentLength(httpRequestData)
            };

            // Continue to next middleware/function
            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during request validation for {Path}", 
                httpRequestData.Url.AbsolutePath);
            
            await SetInternalServerErrorResponse(context, "Request validation failed");
        }
    }

    /// <summary>
    /// Validates the HTTP request
    /// </summary>
    private async Task<ValidationResult> ValidateRequestAsync(HttpRequestData request, FunctionContext context)
    {
        var result = new ValidationResult();
        var path = request.Url.AbsolutePath.ToLowerInvariant();

        // Validate HTTP method
        ValidateHttpMethod(request, result);

        // Validate content type for POST/PUT requests
        if (IsBodyRequired(request.Method))
        {
            ValidateContentType(request, result);
            await ValidateRequestBody(request, result);
        }

        // Validate headers
        ValidateHeaders(request, result);

        // Validate query parameters
        ValidateQueryParameters(request, result);

        // Validate route parameters
        ValidateRouteParameters(request, context, result);

        // Validate request size
        ValidateRequestSize(request, result);

        return result;
    }

    /// <summary>
    /// Validates HTTP method is allowed
    /// </summary>
    private void ValidateHttpMethod(HttpRequestData request, ValidationResult result)
    {
        var allowedMethods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };
        
        if (!allowedMethods.Contains(request.Method.ToUpperInvariant()))
        {
            result.Errors.Add(new ValidationError
            {
                Field = "method",
                Message = $"HTTP method '{request.Method}' is not allowed",
                Code = "INVALID_METHOD"
            });
        }
    }

    /// <summary>
    /// Validates content type for requests with body
    /// </summary>
    private void ValidateContentType(HttpRequestData request, ValidationResult result)
    {
        if (!request.Headers.TryGetValues("Content-Type", out var contentTypeValues))
        {
            result.Errors.Add(new ValidationError
            {
                Field = "content-type",
                Message = "Content-Type header is required for requests with body",
                Code = "MISSING_CONTENT_TYPE"
            });
            return;
        }

        var contentType = contentTypeValues.FirstOrDefault()?.ToLowerInvariant();
        var allowedContentTypes = new[]
        {
            "application/json",
            "application/json; charset=utf-8",
            "text/json"
        };

        if (!allowedContentTypes.Any(allowed => contentType?.StartsWith(allowed) == true))
        {
            result.Errors.Add(new ValidationError
            {
                Field = "content-type",
                Message = $"Content-Type '{contentType}' is not supported. Supported types: {string.Join(", ", allowedContentTypes)}",
                Code = "UNSUPPORTED_CONTENT_TYPE"
            });
        }
    }

    /// <summary>
    /// Validates request body format and size
    /// </summary>
    private async Task ValidateRequestBody(HttpRequestData request, ValidationResult result)
    {
        try
        {
            if (request.Body.CanSeek)
            {
                var originalPosition = request.Body.Position;
                
                // Read body content
                var bodyContent = await new StreamReader(request.Body).ReadToEndAsync();
                
                // Reset stream position for downstream consumption
                request.Body.Position = originalPosition;

                // Validate JSON format if not empty
                if (!string.IsNullOrWhiteSpace(bodyContent))
                {
                    try
                    {
                        JsonDocument.Parse(bodyContent);
                    }
                    catch (JsonException ex)
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Field = "body",
                            Message = $"Invalid JSON format: {ex.Message}",
                            Code = "INVALID_JSON"
                        });
                    }

                    // Validate body size
                    if (bodyContent.Length > 1024 * 1024) // 1MB limit
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Field = "body",
                            Message = "Request body exceeds maximum size limit (1MB)",
                            Code = "BODY_TOO_LARGE"
                        });
                    }
                }
                else if (IsBodyRequired(request.Method))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Field = "body",
                        Message = "Request body is required",
                        Code = "MISSING_BODY"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ValidationError
            {
                Field = "body",
                Message = $"Error reading request body: {ex.Message}",
                Code = "BODY_READ_ERROR"
            });
        }
    }

    /// <summary>
    /// Validates required headers
    /// </summary>
    private void ValidateHeaders(HttpRequestData request, ValidationResult result)
    {
        // Validate User-Agent (optional but recommended)
        if (!request.Headers.TryGetValues("User-Agent", out var userAgentValues) || 
            !userAgentValues.Any())
        {
            // Not an error, just a warning
            _logger.LogDebug("Request missing User-Agent header");
        }

        // Validate Accept header for GET requests
        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Headers.TryGetValues("Accept", out var acceptValues))
            {
                var accept = acceptValues.FirstOrDefault();
                if (!string.IsNullOrEmpty(accept) && 
                    !accept.Contains("application/json") && 
                    !accept.Contains("*/*"))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Field = "accept",
                        Message = "Accept header must include 'application/json' or '*/*'",
                        Code = "UNSUPPORTED_ACCEPT_TYPE"
                    });
                }
            }
        }

        // Validate custom headers if present
        ValidateCustomHeaders(request, result);
    }

    /// <summary>
    /// Validates custom application headers
    /// </summary>
    private void ValidateCustomHeaders(HttpRequestData request, ValidationResult result)
    {
        // Validate X-Request-ID if present
        if (request.Headers.TryGetValues("X-Request-ID", out var requestIdValues))
        {
            var requestId = requestIdValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(requestId))
            {
                if (!Guid.TryParse(requestId, out _) && !IsValidRequestId(requestId))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Field = "x-request-id",
                        Message = "X-Request-ID must be a valid GUID or alphanumeric string",
                        Code = "INVALID_REQUEST_ID"
                    });
                }
            }
        }

        // Validate X-Client-Version if present
        if (request.Headers.TryGetValues("X-Client-Version", out var versionValues))
        {
            var version = versionValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(version) && !IsValidVersion(version))
            {
                result.Errors.Add(new ValidationError
                {
                    Field = "x-client-version",
                    Message = "X-Client-Version must be in format 'major.minor.patch'",
                    Code = "INVALID_CLIENT_VERSION"
                });
            }
        }
    }

    /// <summary>
    /// Validates query parameters
    /// </summary>
    private void ValidateQueryParameters(HttpRequestData request, ValidationResult result)
    {
        var query = request.Url.Query;
        if (string.IsNullOrEmpty(query)) return;

        // Parse query parameters
        var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query);

        // Validate pagination parameters
        if (queryParams.TryGetValue("page", out var pageValues))
        {
            var pageValue = pageValues.FirstOrDefault();
            if (!int.TryParse(pageValue, out var page) || page < 1)
            {
                result.Errors.Add(new ValidationError
                {
                    Field = "page",
                    Message = "Page parameter must be a positive integer",
                    Code = "INVALID_PAGE"
                });
            }
        }

        if (queryParams.TryGetValue("pageSize", out var pageSizeValues))
        {
            var pageSizeValue = pageSizeValues.FirstOrDefault();
            if (!int.TryParse(pageSizeValue, out var pageSize) || pageSize < 1 || pageSize > 1000)
            {
                result.Errors.Add(new ValidationError
                {
                    Field = "pageSize",
                    Message = "PageSize parameter must be between 1 and 1000",
                    Code = "INVALID_PAGE_SIZE"
                });
            }
        }

        // Validate status filter if present
        if (queryParams.TryGetValue("status", out var statusValues))
        {
            var status = statusValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(status))
            {
                var validStatuses = new[] { "queued", "inprogress", "completed", "failed", "cancelled" };
                if (!validStatuses.Contains(status.ToLowerInvariant()))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Field = "status",
                        Message = $"Status must be one of: {string.Join(", ", validStatuses)}",
                        Code = "INVALID_STATUS"
                    });
                }
            }
        }
    }

    /// <summary>
    /// Validates route parameters
    /// </summary>
    private void ValidateRouteParameters(HttpRequestData request, FunctionContext context, ValidationResult result)
    {
        var path = request.Url.AbsolutePath;

        // Validate migration ID in routes
        if (path.Contains("/migrations/"))
        {
            var migrationIdMatch = Regex.Match(path, @"/migrations/([^/]+)");
            if (migrationIdMatch.Success)
            {
                var migrationId = migrationIdMatch.Groups[1].Value;
                if (!Guid.TryParse(migrationId, out _) && !IsValidId(migrationId))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Field = "migrationId",
                        Message = "Migration ID must be a valid GUID or identifier",
                        Code = "INVALID_MIGRATION_ID"
                    });
                }
            }
        }

        // Validate store ID in routes
        if (path.Contains("/stores/"))
        {
            var storeIdMatch = Regex.Match(path, @"/stores/([^/]+)");
            if (storeIdMatch.Success)
            {
                var storeId = storeIdMatch.Groups[1].Value;
                if (!IsValidStoreId(storeId))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Field = "storeId",
                        Message = "Store ID must be alphanumeric and 3-50 characters",
                        Code = "INVALID_STORE_ID"
                    });
                }
            }
        }
    }

    /// <summary>
    /// Validates request size limits
    /// </summary>
    private void ValidateRequestSize(HttpRequestData request, ValidationResult result)
    {
        var contentLength = GetContentLength(request);
        const long maxRequestSize = 10 * 1024 * 1024; // 10MB

        if (contentLength > maxRequestSize)
        {
            result.Errors.Add(new ValidationError
            {
                Field = "request",
                Message = "Request size exceeds maximum limit (10MB)",
                Code = "REQUEST_TOO_LARGE"
            });
        }
    }

    /// <summary>
    /// Determines if body is required for the HTTP method
    /// </summary>
    private static bool IsBodyRequired(string method) =>
        method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("PATCH", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets content length from request
    /// </summary>
    private static long GetContentLength(HttpRequestData request)
    {
        if (request.Headers.TryGetValues("Content-Length", out var values))
        {
            var value = values.FirstOrDefault();
            if (long.TryParse(value, out var length))
                return length;
        }
        return 0;
    }

    /// <summary>
    /// Validates request ID format
    /// </summary>
    private static bool IsValidRequestId(string requestId) =>
        !string.IsNullOrEmpty(requestId) && 
        requestId.Length <= 50 && 
        Regex.IsMatch(requestId, @"^[a-zA-Z0-9\-_]+$");

    /// <summary>
    /// Validates version format
    /// </summary>
    private static bool IsValidVersion(string version) =>
        !string.IsNullOrEmpty(version) &&
        Regex.IsMatch(version, @"^\d+\.\d+\.\d+(-[a-zA-Z0-9\-\.]+)?$");

    /// <summary>
    /// Validates generic ID format
    /// </summary>
    private static bool IsValidId(string id) =>
        !string.IsNullOrEmpty(id) &&
        id.Length <= 100 &&
        Regex.IsMatch(id, @"^[a-zA-Z0-9\-_]+$");

    /// <summary>
    /// Validates store ID format
    /// </summary>
    private static bool IsValidStoreId(string storeId) =>
        !string.IsNullOrEmpty(storeId) &&
        storeId.Length >= 3 &&
        storeId.Length <= 50 &&
        Regex.IsMatch(storeId, @"^[a-zA-Z0-9\-_]+$");

    /// <summary>
    /// Sets validation error response
    /// </summary>
    private static async Task SetValidationErrorResponse(FunctionContext context, ValidationResult validationResult)
    {
        var response = context.GetHttpResponseData();
        if (response != null)
        {
            response.StatusCode = HttpStatusCode.BadRequest;
            response.Headers.Add("Content-Type", "application/json");

            var errorResponse = new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "VALIDATION_ERROR",
                    Message = "Request validation failed",
                    Details = validationResult.Errors.ToList(),
                    Timestamp = DateTime.UtcNow,
                    RequestId = GetRequestId(context),
                    Documentation = "https://docs.bigcommerce-migration.com/api/validation"
                }
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }

    /// <summary>
    /// Sets internal server error response
    /// </summary>
    private static async Task SetInternalServerErrorResponse(FunctionContext context, string message)
    {
        var response = context.GetHttpResponseData();
        if (response != null)
        {
            response.StatusCode = HttpStatusCode.InternalServerError;
            response.Headers.Add("Content-Type", "application/json");

            var errorResponse = new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "VALIDATION_MIDDLEWARE_ERROR",
                    Message = message,
                    Timestamp = DateTime.UtcNow,
                    RequestId = GetRequestId(context)
                }
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }

    /// <summary>
    /// Gets request ID from context
    /// </summary>
    private static string GetRequestId(FunctionContext context)
    {
        return context.Items.TryGetValue("RequestId", out var requestId) 
            ? requestId?.ToString() ?? Guid.NewGuid().ToString()
            : Guid.NewGuid().ToString();
    }
}

/// <summary>
/// Validation result container
/// </summary>
public class ValidationResult
{
    public bool IsValid => !Errors.Any();
    public List<ValidationError> Errors { get; set; } = new();
}

/// <summary>
/// Individual validation error
/// </summary>
public class ValidationError
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Request metrics for monitoring
/// </summary>
public class RequestMetrics
{
    public DateTime StartTime { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public long ContentLength { get; set; }
}

/// <summary>
/// Standard API error response
/// </summary>
public class ApiErrorResponse
{
    public ErrorDetails Error { get; set; } = new();
}

/// <summary>
/// Error details structure
/// </summary>
public class ErrorDetails
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<ValidationError> Details { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string Documentation { get; set; } = string.Empty;
} 