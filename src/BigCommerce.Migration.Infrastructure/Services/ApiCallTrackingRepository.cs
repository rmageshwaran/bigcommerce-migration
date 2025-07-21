using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

#pragma warning disable CS1998 // Async method lacks 'await' operators (in-memory storage)

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Repository implementation for API call tracking operations
/// Follows Interface Segregation Principle - handles only API call tracking and statistics
/// Uses in-memory storage for prototype/development (can be replaced with persistent storage)
/// </summary>
public class ApiCallTrackingRepository : IApiCallTrackingRepository
{
    private readonly ILogger<ApiCallTrackingRepository> _logger;
    private readonly ConcurrentQueue<ApiCallTracking> _apiCalls;

    /// <summary>
    /// Initializes a new instance of the ApiCallTrackingRepository
    /// </summary>
    /// <param name="logger">Logger instance for API call tracking operations</param>
    public ApiCallTrackingRepository(ILogger<ApiCallTrackingRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiCalls = new ConcurrentQueue<ApiCallTracking>();
    }

    /// <summary>
    /// Creates a new API call tracking entry for monitoring and rate limit analysis
    /// </summary>
    public async Task<ApiCallTracking> CreateAsync(ApiCallTracking apiCall)
    {
        if (apiCall == null)
            throw new ArgumentNullException(nameof(apiCall));

        if (string.IsNullOrEmpty(apiCall.StoreId))
            throw new ArgumentException("StoreId is required", nameof(apiCall));

        if (string.IsNullOrEmpty(apiCall.Endpoint))
            throw new ArgumentException("Endpoint is required", nameof(apiCall));

        if (string.IsNullOrEmpty(apiCall.Method))
            throw new ArgumentException("Method is required", nameof(apiCall));

        // Generate timestamps if not provided
        if (apiCall.RequestTimestamp == default)
        {
            apiCall.RequestTimestamp = DateTime.UtcNow;
        }
        if (apiCall.ResponseTimestamp == default)
        {
            apiCall.ResponseTimestamp = DateTime.UtcNow;
        }

        // Store in-memory (replace with persistent storage)
        _apiCalls.Enqueue(apiCall);

        _logger.LogDebug("Tracked API call: {Method} {Endpoint} for store {StoreId} - {StatusCode} in {ResponseTime}ms", 
            apiCall.Method, apiCall.Endpoint, apiCall.StoreId, apiCall.StatusCode, apiCall.ResponseTimeMs);

        return apiCall;
    }

    /// <summary>
    /// Retrieves API call statistics within a specified time window for rate limiting decisions
    /// </summary>
    public async Task<ApiCallStatistics> GetStatisticsAsync(string storeId, TimeSpan timeWindow)
    {
        if (string.IsNullOrEmpty(storeId))
            throw new ArgumentException("StoreId cannot be null or empty", nameof(storeId));

        if (timeWindow.TotalDays > 7)
            throw new ArgumentException("Time window cannot exceed 7 days", nameof(timeWindow));

        if (timeWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeWindow), "Time window must be positive");

        var cutoffTime = DateTime.UtcNow - timeWindow;
        var relevantCalls = _apiCalls.ToArray()
            .Where(call => call.StoreId == storeId && call.RequestTimestamp >= cutoffTime)
            .ToList();

        var statistics = new ApiCallStatistics
        {
            StoreId = storeId,
            TimeWindow = timeWindow,
            TotalCalls = relevantCalls.Count,
            SuccessfulCalls = relevantCalls.Count(call => call.IsSuccessful),
            FailedCalls = relevantCalls.Count(call => !call.IsSuccessful),
            AverageResponseTimeMs = relevantCalls.Any() 
                ? relevantCalls.Select(call => (double)call.ResponseTimeMs).Average()
                : 0.0,
            CallsPerMinute = timeWindow.TotalMinutes > 0 ? relevantCalls.Count / timeWindow.TotalMinutes : 0.0,
            CalculatedAt = DateTime.UtcNow
        };

        // Calculate success rate locally for logging
        var successRate = statistics.TotalCalls > 0 
            ? (double)statistics.SuccessfulCalls / statistics.TotalCalls 
            : 0.0;

        _logger.LogDebug("Calculated API statistics for store {StoreId}: {TotalCalls} calls, {SuccessRate:P1} success rate", 
            storeId, statistics.TotalCalls, successRate);

        return statistics;
    }

    /// <summary>
    /// Retrieves paginated API call history for a specific migration with filtering capabilities
    /// </summary>
    public async Task<ApiCallListResult> GetHistoryAsync(string migrationId, ApiCallQueryRequest request)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("MigrationId cannot be null or empty", nameof(migrationId));

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (request.Page < 1)
            throw new ArgumentException("Page must be 1 or greater", nameof(request));

        if (request.PageSize < 1 || request.PageSize > 1000)
            throw new ArgumentException("PageSize must be between 1 and 1000", nameof(request));

        var allCalls = _apiCalls.ToArray()
            .Where(call => call.MigrationId == migrationId)
            .AsEnumerable();

        // Apply filters
        if (!string.IsNullOrEmpty(request.StoreId))
        {
            allCalls = allCalls.Where(call => call.StoreId == request.StoreId);
        }

        if (!string.IsNullOrEmpty(request.Endpoint))
        {
            allCalls = allCalls.Where(call => call.Endpoint.Contains(request.Endpoint, StringComparison.OrdinalIgnoreCase));
        }

        if (request.IsSuccessful.HasValue)
        {
            allCalls = allCalls.Where(call => call.IsSuccessful == request.IsSuccessful);
        }

        if (request.RequestedAfter.HasValue)
        {
            allCalls = allCalls.Where(call => call.RequestTimestamp >= request.RequestedAfter);
        }

        if (request.RequestedBefore.HasValue)
        {
            allCalls = allCalls.Where(call => call.RequestTimestamp <= request.RequestedBefore);
        }

        // Apply sorting
        allCalls = request.SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? allCalls.OrderBy(call => call.RequestTimestamp)
            : allCalls.OrderByDescending(call => call.RequestTimestamp);

        var totalCount = allCalls.Count();

        // Apply pagination
        var skip = (request.Page - 1) * request.PageSize;
        var paginatedCalls = allCalls.Skip(skip).Take(request.PageSize).ToList();

        var result = new ApiCallListResult
        {
            ApiCalls = paginatedCalls,
            TotalCount = totalCount,
            CurrentPage = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize),
            HasMorePages = totalCount > skip + paginatedCalls.Count
        };

        _logger.LogDebug("Retrieved {Count} API calls for migration {MigrationId} (page {Page})", 
            paginatedCalls.Count, migrationId, request.Page);

        return result;
    }
} 