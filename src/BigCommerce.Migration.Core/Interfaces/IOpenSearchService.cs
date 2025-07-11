namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for OpenSearch logging and performance tracking
/// Provides structured logging, metrics tracking, and search capabilities for migration operations
/// </summary>
public interface IOpenSearchService
{
    /// <summary>
    /// Logs a migration event with structured data
    /// </summary>
    /// <param name="eventType">Type of migration event (e.g., "ProductMigration", "CategoryMigration")</param>
    /// <param name="entityId">Unique identifier for the entity being migrated</param>
    /// <param name="eventData">Additional structured data for the event</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if logging succeeded, false otherwise</returns>
    Task<bool> LogMigrationEventAsync(
        string eventType,
        string entityId,
        object eventData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs performance metrics for migration operations
    /// </summary>
    /// <param name="operationName">Name of the operation being measured</param>
    /// <param name="duration">Duration of the operation</param>
    /// <param name="metrics">Additional performance metrics</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if logging succeeded, false otherwise</returns>
    Task<bool> LogPerformanceMetricsAsync(
        string operationName,
        TimeSpan duration,
        object metrics,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs error information with exception details
    /// </summary>
    /// <param name="context">Context where the error occurred</param>
    /// <param name="exception">Exception that occurred</param>
    /// <param name="additionalData">Additional context data</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if logging succeeded, false otherwise</returns>
    Task<bool> LogErrorAsync(
        string context,
        Exception exception,
        object? additionalData = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches logs based on query criteria
    /// </summary>
    /// <param name="searchQuery">OpenSearch query string</param>
    /// <param name="fromDate">Start date for search range</param>
    /// <param name="toDate">End date for search range</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>List of matching log entries</returns>
    Task<IEnumerable<object>> SearchLogsAsync(
        string searchQuery,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the OpenSearch service is healthy and available
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if service is healthy, false otherwise</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs entity batch processing events
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type being processed</param>
    /// <param name="batchNumber">Batch number</param>
    /// <param name="batchData">Batch processing data</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if logging succeeded, false otherwise</returns>
    Task<bool> LogEntityBatchProcessingAsync(
        string migrationId,
        string entityType,
        int batchNumber,
        object batchData,
        CancellationToken cancellationToken = default);
} 