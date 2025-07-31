using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System.Net;
using System.Text;
using System.Text.Json;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Service for handling HTTP API requests with rate limiting and authentication
/// Follows Single Responsibility Principle - handles only HTTP request concerns
/// </summary>
public class ApiRequestHandler : IApiRequestHandler
{
    private readonly HttpClient _httpClient;
    private readonly IRateLimitService _rateLimitService;
    private readonly IOpenSearchService _openSearchService;
    private readonly ILogger<ApiRequestHandler> _logger;
    private readonly IDynamicRateLimiter? _dynamicRateLimiter;

    /// <summary>
    /// Initializes a new instance of the ApiRequestHandler
    /// </summary>
    /// <param name="httpClient">HTTP client for making requests</param>
    /// <param name="rateLimitService">Rate limit service for request throttling</param>
    /// <param name="openSearchService">OpenSearch service for logging</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="dynamicRateLimiter">Optional dynamic rate limiter for BigCommerce health updates</param>
    public ApiRequestHandler(
        HttpClient httpClient,
        IRateLimitService rateLimitService,
        IOpenSearchService openSearchService,
        ILogger<ApiRequestHandler> logger,
        IDynamicRateLimiter? dynamicRateLimiter = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dynamicRateLimiter = dynamicRateLimiter;
    }

    /// <summary>
    /// Executes an HTTP request with rate limiting, authentication, and error handling
    /// </summary>
    public async Task<T> ExecuteRequestAsync<T>(ApiRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (!request.IsValid()) throw new ArgumentException("Invalid API request", nameof(request));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // Apply rate limiting before making the request
            await _rateLimitService.CheckAndWaitAsync(request.StoreConfiguration.StoreId ?? string.Empty, cancellationToken);

            // Create HTTP request message
            using var httpRequest = CreateHttpRequestMessage(request);

            // Execute the HTTP request
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            // Check for cancellation after HTTP request but before processing
            cancellationToken.ThrowIfCancellationRequested();

            // Record the API call for rate limiting tracking
            await _rateLimitService.RecordApiCallAsync(
                request.StoreConfiguration.StoreId ?? string.Empty,
                request.Url,
                stopwatch.Elapsed.TotalMilliseconds,
                response.IsSuccessStatusCode,
                cancellationToken);

            // Handle the response
            return await ProcessResponseAsync<T>(response, request, stopwatch.Elapsed, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("API request was cancelled for URL: {Url}", request.Url);
            throw; // Infrastructure service - let caller handle cancellation appropriately
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing request to {Url}", request.Url);
            
            // Log error to OpenSearch (use separate cancellation token to avoid cancellation during error logging)
            await LogErrorToOpenSearch(request, ex, stopwatch.Elapsed, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Executes an HTTP request and returns raw string response
    /// </summary>
    public async Task<string> ExecuteRequestAsync(ApiRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (!request.IsValid()) throw new ArgumentException("Invalid API request", nameof(request));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // Apply rate limiting before making the request
            await _rateLimitService.CheckAndWaitAsync(request.StoreConfiguration.StoreId ?? string.Empty, cancellationToken);

            // Create HTTP request message
            using var httpRequest = CreateHttpRequestMessage(request);

            // Execute the HTTP request
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            // Check for cancellation after HTTP request but before processing
            cancellationToken.ThrowIfCancellationRequested();

            // Record the API call for rate limiting tracking
            await _rateLimitService.RecordApiCallAsync(
                request.StoreConfiguration.StoreId ?? string.Empty,
                request.Url,
                stopwatch.Elapsed.TotalMilliseconds,
                response.IsSuccessStatusCode,
                cancellationToken);

            // Handle the response - BigCommerce API specific logic
            if (response.StatusCode == HttpStatusCode.OK || 
                response.StatusCode == HttpStatusCode.Created || 
                response.StatusCode == HttpStatusCode.Accepted)
            {
                // Full success - all items processed successfully
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            else if (response.StatusCode == HttpStatusCode.MultiStatus) // 207
            {
                // Partial success - some items succeeded, some failed
                // BigCommerce returns 207 when batch operations have mixed results
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                
                _logger.LogWarning("⚠️ [API-207] PARTIAL SUCCESS: Received 207 Multi-Status response for URL: {Url}", request.Url);
                _logger.LogWarning("📊 [API-207] RESPONSE CONTENT: {ResponseContent}", responseContent);
                
                // Try to parse the response to get success/failure counts
                try
                {
                    var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
                    if (jsonResponse != null)
                    {
                        // Look for meta information that shows success/failure counts
                        if (jsonResponse.TryGetValue("meta", out var metaValue))
                        {
                            var metaString = metaValue?.ToString() ?? "";
                            _logger.LogWarning("📈 [API-207] META INFO: {MetaInfo}", metaString);
                        }
                        
                        // Look for data array to see what was actually created
                        if (jsonResponse.TryGetValue("data", out var dataValue))
                        {
                            if (dataValue is JsonElement dataElement && dataElement.ValueKind == JsonValueKind.Array)
                            {
                                var createdCount = dataElement.GetArrayLength();
                                _logger.LogWarning("📋 [API-207] CREATED COUNT: {CreatedCount} entities were successfully created", createdCount);
                            }
                        }
                        
                        // Look for errors array to see what failed
                        if (jsonResponse.TryGetValue("errors", out var errorsValue))
                        {
                            var errorsString = errorsValue?.ToString() ?? "";
                            _logger.LogWarning("❌ [API-207] ERRORS: {ErrorInfo}", errorsString);
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "⚠️ [API-207] Failed to parse 207 response for detailed analysis");
                }
                
                // Return the content so calling code can parse successes vs failures
                return responseContent;
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateHttpException(response.StatusCode, errorContent, request.Url);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Request was cancelled for URL: {Url}", request.Url);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing request to {Url}", request.Url);
            
            // Log error to OpenSearch (use separate cancellation token to avoid cancellation during error logging)
            await LogErrorToOpenSearch(request, ex, stopwatch.Elapsed, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Executes a GET request with rate limiting and authentication
    /// </summary>
    public async Task<T> ExecuteGetRequestAsync<T>(string url, StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        var request = ApiRequest.CreateGet(url, storeConfig);
        return await ExecuteRequestAsync<T>(request, cancellationToken);
    }

    /// <summary>
    /// Executes a POST request with JSON content, rate limiting, and authentication
    /// </summary>
    public async Task<T> ExecutePostRequestAsync<T>(string url, string content, StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        var request = ApiRequest.CreatePost(url, content, storeConfig);
        return await ExecuteRequestAsync<T>(request, cancellationToken);
    }

    /// <summary>
    /// Creates an HTTP request message from the API request configuration
    /// </summary>
    private HttpRequestMessage CreateHttpRequestMessage(ApiRequest request)
    {
        var httpRequest = new HttpRequestMessage(request.Method, request.Url);

        // Add authentication headers from store configuration
        var authHeaders = request.StoreConfiguration.GetAuthHeaders();
        foreach (var header in authHeaders)
        {
            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Add custom headers
        foreach (var header in request.Headers)
        {
            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Add content for POST/PUT requests
        if (!string.IsNullOrEmpty(request.Content) && 
            (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put))
        {
            httpRequest.Content = new StringContent(request.Content, Encoding.UTF8, request.ContentType);
        }

        return httpRequest;
    }

    /// <summary>
    /// Processes the HTTP response and deserializes it to the expected type
    /// </summary>
    private async Task<T> ProcessResponseAsync<T>(HttpResponseMessage response, ApiRequest request, TimeSpan elapsed, CancellationToken cancellationToken = default)
    {
        // Extract BigCommerce rate limit headers for dynamic rate limiting
        var rateLimitInfo = ExtractBigCommerceRateLimitHeaders(response, request.StoreConfiguration.StoreId ?? string.Empty);

        // Update dynamic rate limiter with BigCommerce health data if available
        if (rateLimitInfo != null && _dynamicRateLimiter != null)
        {
            try
            {
                await _dynamicRateLimiter.UpdateApiHealthAsync(
                    request.StoreConfiguration.StoreId ?? string.Empty, 
                    rateLimitInfo, 
                    cancellationToken);
                
                _logger.LogTrace("Updated dynamic rate limiter with BigCommerce health data for store {StoreId}", 
                    request.StoreConfiguration.StoreId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update dynamic rate limiter health data for store {StoreId}", 
                    request.StoreConfiguration.StoreId);
                // Don't rethrow - health updates shouldn't break the main request flow
            }
        }

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Check for cancellation before expensive deserialization
            cancellationToken.ThrowIfCancellationRequested();
            
            // Log enhanced performance metrics with BigCommerce rate limit data
            await LogEnhancedPerformanceMetrics(request, elapsed, true, rateLimitInfo, cancellationToken);

            // Handle different response types
            if (typeof(T) == typeof(string))
            {
                return (T)(object)content;
            }

            // Try to deserialize JSON response
            try
            {
                // Check for cancellation before JSON deserialization (can be expensive for large responses)
                cancellationToken.ThrowIfCancellationRequested();
                
                var result = JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return result!;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize response from {Url} to type {Type}", request.Url, typeof(T).Name);
                throw new InvalidOperationException($"Failed to deserialize response to type {typeof(T).Name}", ex);
            }
        }

        // Handle error responses
        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
        await LogEnhancedPerformanceMetrics(request, elapsed, false, rateLimitInfo, cancellationToken);
        
        throw CreateHttpException(response.StatusCode, errorContent, request.Url);
    }

    /// <summary>
    /// Creates an appropriate HTTP exception based on the status code
    /// </summary>
    private HttpRequestException CreateHttpException(HttpStatusCode statusCode, string content, string url)
    {
        HttpRequestException exception = statusCode switch
        {
            HttpStatusCode.Unauthorized => new HttpRequestException($"Authentication failed for {url}"),
            HttpStatusCode.Forbidden => new HttpRequestException($"Access forbidden for {url}"),
            HttpStatusCode.TooManyRequests => new HttpRequestException($"Rate limit exceeded for {url}"),
            HttpStatusCode.NotFound => new HttpRequestException($"Resource not found: {url}"),
            HttpStatusCode.BadRequest => new HttpRequestException($"Bad request to {url}: {content}"),
            _ => new HttpRequestException($"API request failed with status {statusCode} for {url}: {content}")
        };

        // ✅ FIX: Store response payload in exception data for later extraction by EntityErrorHandlingService
        if (!string.IsNullOrEmpty(content))
        {
            exception.Data["ResponsePayload"] = content;
            exception.Data["ResponseContent"] = content; // Also store as ResponseContent for compatibility
            exception.Data["StatusCode"] = statusCode.ToString();
            exception.Data["RequestUrl"] = url;
        }

        return exception;
    }

    /// <summary>
    /// Logs performance metrics to OpenSearch
    /// </summary>
    private async Task LogPerformanceMetrics(ApiRequest request, TimeSpan elapsed, bool isSuccessful, CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = new
            {
                url = request.Url,
                method = request.Method.Method,
                storeId = request.StoreConfiguration.StoreId,
                duration_ms = elapsed.TotalMilliseconds,
                isSuccessful = isSuccessful,
                timestamp = DateTime.UtcNow
            };

            await _openSearchService.LogPerformanceMetricsAsync("api_request", elapsed, metrics, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log performance metrics for {Url}", request.Url);
        }
    }

    /// <summary>
    /// Logs errors to OpenSearch for monitoring
    /// </summary>
    private async Task LogErrorToOpenSearch(ApiRequest request, Exception ex, TimeSpan elapsed, CancellationToken cancellationToken = default)
    {
        try
        {
            var errorData = new
            {
                url = request.Url,
                method = request.Method.Method,
                storeId = request.StoreConfiguration.StoreId,
                duration_ms = elapsed.TotalMilliseconds,
                error = ex.Message,
                stackTrace = ex.StackTrace,
                timestamp = DateTime.UtcNow
            };

            await _openSearchService.LogMigrationEventAsync("ApiRequestError", request.StoreConfiguration.StoreId ?? string.Empty, errorData);
        }
        catch (Exception loggingEx)
        {
            _logger.LogWarning(loggingEx, "Failed to log error to OpenSearch for {Url}", request.Url);
        }
    }

    /// <summary>
    /// Extracts BigCommerce rate limit information from HTTP response headers
    /// Maps X-Rate-Limit-* headers to BigCommerceRateLimitInfo for dynamic rate limiting
    /// Follows Single Responsibility Principle - only handles header extraction
    /// </summary>
    /// <param name="response">HTTP response containing potential rate limit headers</param>
    /// <param name="storeId">Store identifier for context</param>
    /// <returns>BigCommerceRateLimitInfo if all required headers are present and valid, null otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when response is null</exception>
    /// <exception cref="ArgumentException">Thrown when storeId is null or empty</exception>
    public BigCommerceRateLimitInfo? ExtractBigCommerceRateLimitHeaders(HttpResponseMessage response, string storeId)
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(storeId));

        try
        {
            // Check if all required BigCommerce rate limit headers are present
            var requestsLeftHeader = response.Headers.GetValues("X-Rate-Limit-Requests-Left").FirstOrDefault();
            var requestsQuotaHeader = response.Headers.GetValues("X-Rate-Limit-Requests-Quota").FirstOrDefault();
            var timeResetMsHeader = response.Headers.GetValues("X-Rate-Limit-Time-Reset-Ms").FirstOrDefault();
            var timeWindowMsHeader = response.Headers.GetValues("X-Rate-Limit-Time-Window-Ms").FirstOrDefault();

            // Return null if any required header is missing
            if (string.IsNullOrEmpty(requestsLeftHeader) || 
                string.IsNullOrEmpty(requestsQuotaHeader) ||
                string.IsNullOrEmpty(timeResetMsHeader) || 
                string.IsNullOrEmpty(timeWindowMsHeader))
            {
                return null;
            }

            // Parse header values with validation
            if (!int.TryParse(requestsLeftHeader, out var requestsLeft) ||
                !int.TryParse(requestsQuotaHeader, out var requestsQuota) ||
                !long.TryParse(timeResetMsHeader, out var timeResetMs) ||
                !long.TryParse(timeWindowMsHeader, out var timeWindowMs))
            {
                _logger.LogWarning("Failed to parse BigCommerce rate limit headers for store {StoreId}. " +
                                   "RequestsLeft: {RequestsLeft}, RequestsQuota: {RequestsQuota}, " +
                                   "TimeResetMs: {TimeResetMs}, TimeWindowMs: {TimeWindowMs}",
                                   storeId, requestsLeftHeader, requestsQuotaHeader, timeResetMsHeader, timeWindowMsHeader);
                return null;
            }

            // Create and validate BigCommerceRateLimitInfo
            var rateLimitInfo = new BigCommerceRateLimitInfo(storeId, requestsLeft, requestsQuota, timeResetMs, timeWindowMs);

            if (!rateLimitInfo.IsValid())
            {
                _logger.LogWarning("Invalid BigCommerce rate limit info extracted for store {StoreId}: {RateLimitInfo}",
                                   storeId, rateLimitInfo);
                return null;
            }

            _logger.LogDebug("Successfully extracted BigCommerce rate limit headers for store {StoreId}: {RateLimitInfo}",
                             storeId, rateLimitInfo);

            return rateLimitInfo;
        }
        catch (InvalidOperationException)
        {
            // Headers collection doesn't contain the requested headers
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error extracting BigCommerce rate limit headers for store {StoreId}", storeId);
            return null;
        }
    }

    /// <summary>
    /// Logs enhanced performance metrics including BigCommerce rate limit information
    /// Extends base performance logging with dynamic rate limiting data
    /// </summary>
    /// <param name="request">Original API request</param>
    /// <param name="elapsed">Request execution time</param>
    /// <param name="isSuccess">Whether the request was successful</param>
    /// <param name="rateLimitInfo">BigCommerce rate limit information if available</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task LogEnhancedPerformanceMetrics(
        ApiRequest request, 
        TimeSpan elapsed, 
        bool isSuccess, 
        BigCommerceRateLimitInfo? rateLimitInfo, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Always log base performance metrics
            await LogPerformanceMetrics(request, elapsed, isSuccess, cancellationToken);

            // Log enhanced metrics if BigCommerce rate limit info is available
            if (rateLimitInfo != null)
            {
                var enhancedMetrics = new
                {
                    StoreId = request.StoreConfiguration.StoreId,
                    Url = request.Url,
                    Method = request.Method.ToString(),
                    ElapsedMs = elapsed.TotalMilliseconds,
                    IsSuccess = isSuccess,
                    RateLimit = new
                    {
                        RequestsLeft = rateLimitInfo.RequestsLeft,
                        RequestsQuota = rateLimitInfo.RequestsQuota,
                        UtilizationPercentage = rateLimitInfo.GetUtilizationPercentage() * 100,
                        IsCritical = rateLimitInfo.IsCritical(),
                        TimeResetMs = rateLimitInfo.TimeResetMs,
                        TimeWindowMs = rateLimitInfo.TimeWindowMs,
                        EffectiveRateLimit = rateLimitInfo.GetEffectiveRateLimit()
                    },
                    Timestamp = DateTime.UtcNow
                };

                // Log to OpenSearch for advanced analytics
                await _openSearchService.LogPerformanceMetricsAsync(
                    "EnhancedApiPerformance",
                    elapsed,
                    enhancedMetrics,
                    cancellationToken);

                // Log summary to structured logging
                _logger.LogInformation("Enhanced API performance: {Method} {Url} completed in {ElapsedMs}ms. " +
                                       "BigCommerce Rate Limit: {RequestsLeft}/{RequestsQuota} " +
                                       "({UtilizationPercentage:F1}% used, Critical: {IsCritical})",
                                       request.Method, request.Url, elapsed.TotalMilliseconds,
                                       rateLimitInfo.RequestsLeft, rateLimitInfo.RequestsQuota,
                                       rateLimitInfo.GetUtilizationPercentage() * 100, rateLimitInfo.IsCritical());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging enhanced performance metrics for {Url}", request.Url);
            // Don't rethrow - logging errors shouldn't break the main request flow
        }
    }
} 