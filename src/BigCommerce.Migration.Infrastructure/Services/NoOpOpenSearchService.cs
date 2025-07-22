using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// No-operation implementation of IOpenSearchService for testing or when OpenSearch is disabled
/// </summary>
public class NoOpOpenSearchService : IOpenSearchService
{
    private readonly ILogger<NoOpOpenSearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the NoOpOpenSearchService
    /// </summary>
    /// <param name="logger">Logger instance for service operations</param>
    public NoOpOpenSearchService(ILogger<NoOpOpenSearchService> logger)
    {
        _logger = logger ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<NoOpOpenSearchService>();
    }

    /// <summary>
    /// Logs a migration event (no-op implementation)
    /// </summary>
    /// <param name="eventType">Type of migration event</param>
    /// <param name="entityId">Unique identifier for the entity being migrated</param>
    /// <param name="eventData">Additional structured data for the event</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Always returns true for no-op implementation</returns>
    public Task<bool> LogMigrationEventAsync(string eventType, string entityId, object eventData, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Logs performance metrics (no-op implementation)
    /// </summary>
    /// <param name="operationName">Name of the operation being measured</param>
    /// <param name="duration">Duration of the operation</param>
    /// <param name="metrics">Additional performance metrics</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Always returns true for no-op implementation</returns>
    public Task<bool> LogPerformanceMetricsAsync(string operationName, TimeSpan duration, object metrics, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Logs error information (no-op implementation)
    /// </summary>
    /// <param name="context">Context where the error occurred</param>
    /// <param name="exception">Exception that occurred</param>
    /// <param name="additionalData">Additional context data</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Always returns true for no-op implementation</returns>
    public Task<bool> LogErrorAsync(string context, Exception exception, object? additionalData = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Searches logs (no-op implementation)
    /// </summary>
    /// <param name="searchQuery">OpenSearch query string</param>
    /// <param name="fromDate">Start date for search range</param>
    /// <param name="toDate">End date for search range</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Empty collection for no-op implementation</returns>
    public Task<IEnumerable<object>> SearchLogsAsync(string searchQuery, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Enumerable.Empty<object>());
    }

    /// <summary>
    /// Optimized structured search (no-op implementation)
    /// </summary>
    /// <param name="queryRequest">Structured query request with filters and pagination</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Empty results and zero count for no-op implementation</returns>
    public Task<(IEnumerable<object> Results, long TotalCount)> SearchLogsOptimizedAsync(OpenSearchQuery queryRequest, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((Enumerable.Empty<object>(), 0L));
    }

    /// <summary>
    /// Batch search for multiple migration IDs (no-op implementation)
    /// </summary>
    /// <param name="migrationIds">Collection of migration IDs to search for</param>
    /// <param name="fromDate">Start date for search range</param>
    /// <param name="toDate">End date for search range</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Empty dictionary for no-op implementation</returns>
    public Task<Dictionary<string, object>> SearchLogsBatchAsync(IEnumerable<string> migrationIds, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, object>());
    }

    /// <summary>
    /// Checks if the service is healthy (no-op implementation)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Always returns true for no-op implementation</returns>
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Logs entity batch processing events (no-op implementation)
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type being processed</param>
    /// <param name="batchNumber">Batch number</param>
    /// <param name="batchData">Batch processing data</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Always returns true for no-op implementation</returns>
    public Task<bool> LogEntityBatchProcessingAsync(string migrationId, string entityType, int batchNumber, object batchData, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
} 