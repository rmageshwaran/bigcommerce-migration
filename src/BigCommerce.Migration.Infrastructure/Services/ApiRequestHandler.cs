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

    /// <summary>
    /// Initializes a new instance of the ApiRequestHandler
    /// </summary>
    /// <param name="httpClient">HTTP client for making requests</param>
    /// <param name="rateLimitService">Rate limit service for request throttling</param>
    /// <param name="openSearchService">OpenSearch service for logging</param>
    /// <param name="logger">Logger instance</param>
    public ApiRequestHandler(
        HttpClient httpClient,
        IRateLimitService rateLimitService,
        IOpenSearchService openSearchService,
        ILogger<ApiRequestHandler> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // Record the API call for rate limiting tracking
            await _rateLimitService.RecordApiCallAsync(
                request.StoreConfiguration.StoreId ?? string.Empty,
                request.Url,
                stopwatch.Elapsed.TotalMilliseconds,
                response.IsSuccessStatusCode,
                cancellationToken);

            // Handle the response
            return await ProcessResponseAsync<T>(response, request, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Request was cancelled for URL: {Url}", request.Url);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing request to {Url}", request.Url);
            
            // Log error to OpenSearch
            await LogErrorToOpenSearch(request, ex, stopwatch.Elapsed);
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

            // Record the API call for rate limiting tracking
            await _rateLimitService.RecordApiCallAsync(
                request.StoreConfiguration.StoreId ?? string.Empty,
                request.Url,
                stopwatch.Elapsed.TotalMilliseconds,
                response.IsSuccessStatusCode,
                cancellationToken);

            // Handle the response
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
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
            
            // Log error to OpenSearch
            await LogErrorToOpenSearch(request, ex, stopwatch.Elapsed);
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
    private async Task<T> ProcessResponseAsync<T>(HttpResponseMessage response, ApiRequest request, TimeSpan elapsed)
    {
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            
            // Log performance metrics
            await LogPerformanceMetrics(request, elapsed, true);

            // Handle different response types
            if (typeof(T) == typeof(string))
            {
                return (T)(object)content;
            }

            // Try to deserialize JSON response
            try
            {
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
        var errorContent = await response.Content.ReadAsStringAsync();
        await LogPerformanceMetrics(request, elapsed, false);
        
        throw CreateHttpException(response.StatusCode, errorContent, request.Url);
    }

    /// <summary>
    /// Creates an appropriate HTTP exception based on the status code
    /// </summary>
    private HttpRequestException CreateHttpException(HttpStatusCode statusCode, string content, string url)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => new HttpRequestException($"Authentication failed for {url}"),
            HttpStatusCode.Forbidden => new HttpRequestException($"Access forbidden for {url}"),
            HttpStatusCode.TooManyRequests => new HttpRequestException($"Rate limit exceeded for {url}"),
            HttpStatusCode.NotFound => new HttpRequestException($"Resource not found: {url}"),
            HttpStatusCode.BadRequest => new HttpRequestException($"Bad request to {url}: {content}"),
            _ => new HttpRequestException($"API request failed with status {statusCode} for {url}: {content}")
        };
    }

    /// <summary>
    /// Logs performance metrics to OpenSearch
    /// </summary>
    private async Task LogPerformanceMetrics(ApiRequest request, TimeSpan elapsed, bool isSuccessful)
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

            await _openSearchService.LogPerformanceMetricsAsync("api_request", elapsed, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log performance metrics for {Url}", request.Url);
        }
    }

    /// <summary>
    /// Logs errors to OpenSearch for monitoring
    /// </summary>
    private async Task LogErrorToOpenSearch(ApiRequest request, Exception ex, TimeSpan elapsed)
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
} 