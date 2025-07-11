using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// No-op implementation of IOpenSearchService for local development when OpenSearch is disabled
/// This prevents connection errors while maintaining the same interface
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
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Logs a migration event (no-op implementation)
    /// </summary>
    public Task<bool> LogMigrationEventAsync(string eventType, string entityId, object eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OpenSearch disabled - skipping migration event log: {EventType} for {EntityId}", eventType, entityId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Logs performance metrics (no-op implementation)
    /// </summary>
    public Task<bool> LogPerformanceMetricsAsync(string operationName, TimeSpan duration, object metrics, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OpenSearch disabled - skipping performance metrics log: {OperationName} took {Duration}ms", operationName, duration.TotalMilliseconds);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Logs error information (no-op implementation)
    /// </summary>
    public Task<bool> LogErrorAsync(string context, Exception exception, object? additionalData = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OpenSearch disabled - skipping error log: {Context} - {ExceptionType}", context, exception.GetType().Name);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Searches logs (no-op implementation)
    /// </summary>
    public Task<IEnumerable<object>> SearchLogsAsync(string searchQuery, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OpenSearch disabled - skipping log search: {SearchQuery}", searchQuery);
        return Task.FromResult(Enumerable.Empty<object>());
    }

    /// <summary>
    /// Checks health (always returns true for no-op)
    /// </summary>
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Logs entity batch processing (no-op implementation)
    /// </summary>
    public Task<bool> LogEntityBatchProcessingAsync(string migrationId, string entityType, int batchNumber, object batchData, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OpenSearch disabled - skipping batch processing log: {MigrationId} - {EntityType} batch {BatchNumber}", migrationId, entityType, batchNumber);
        return Task.FromResult(true);
    }
} 