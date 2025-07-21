using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Repository interface for API call tracking operations following Interface Segregation Principle (ISP)
/// 
/// <para><strong>Responsibility:</strong> Handles only API call tracking and statistics</para>
/// <para><strong>Segregation:</strong> Extracted from IMigrationStorageService to achieve single responsibility</para>
/// <para><strong>Purpose:</strong> Manages BigCommerce API call tracking for rate limiting and monitoring</para>
/// 
/// <example>
/// Usage:
/// <code>
/// var apiCall = await repository.CreateAsync(new ApiCallTracking 
/// { 
///     StoreId = "store123",
///     Endpoint = "/v3/catalog/products",
///     Method = "GET",
///     ResponseTime = TimeSpan.FromMilliseconds(250)
/// });
/// </code>
/// </example>
/// </summary>
public interface IApiCallTrackingRepository
{
    /// <summary>
    /// Creates a new API call tracking entry for monitoring and rate limit analysis
    /// </summary>
    /// <param name="apiCall">API call tracking entry. Must contain valid StoreId, Endpoint, and Method</param>
    /// <returns>Created API call entry with generated ID and precise timestamp</returns>
    /// <exception cref="ArgumentNullException">Thrown when apiCall is null</exception>
    /// <exception cref="ArgumentException">Thrown when apiCall has invalid or missing required fields</exception>
    Task<ApiCallTracking> CreateAsync(ApiCallTracking apiCall);

    /// <summary>
    /// Retrieves API call statistics within a specified time window for rate limiting decisions
    /// </summary>
    /// <param name="storeId">BigCommerce store identifier to analyze</param>
    /// <param name="timeWindow">Time window to calculate statistics (e.g., last 1 hour, 1 day). Maximum 7 days</param>
    /// <returns>Aggregated statistics including call count, success rate, average response time</returns>
    /// <exception cref="ArgumentException">Thrown when storeId is null/empty or timeWindow exceeds maximum</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when timeWindow is negative or zero</exception>
    Task<ApiCallStatistics> GetStatisticsAsync(string storeId, TimeSpan timeWindow);

    /// <summary>
    /// Retrieves paginated API call history for a specific migration with filtering capabilities
    /// </summary>
    /// <param name="migrationId">Unique migration identifier to query API calls for</param>
    /// <param name="request">Query request with filters (status, endpoint, date range), sorting, and pagination</param>
    /// <returns>Paginated result containing API call history and continuation token</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null/empty or invalid format</exception>
    /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="ArgumentException">Thrown when request has invalid pagination or filter parameters</exception>
    Task<ApiCallListResult> GetHistoryAsync(string migrationId, ApiCallQueryRequest request);
} 