using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Service responsible for handling and logging entity processing errors
/// Single Responsibility: Error handling and logging only
/// </summary>
public interface IEntityErrorHandlingService
{
    /// <summary>
    /// Logs structured migration errors with full context
    /// </summary>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="entities">List of entities being processed</param>
    /// <param name="request">Batch processing request</param>
    /// <param name="errorType">Type of error (fetch, transform, create, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogStructuredMigrationErrorAsync(
        Exception exception, 
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request, 
        string errorType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Logs individual entity errors with context
    /// </summary>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="entity">Entity data that caused the error</param>
    /// <param name="request">Batch processing request</param>
    /// <param name="entityId">ID of the entity that failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogEntityErrorAsync(
        Exception exception, 
        Dictionary<string, object> entity, 
        BatchProcessingRequest request, 
        string entityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores request and response payloads for error analysis
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="requestId">Request ID</param>
    /// <param name="requestPayload">Request payload</param>
    /// <param name="responsePayload">Response payload (may be null)</param>
    /// <param name="entityName">Name of the entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of request and response payload references</returns>
    Task<(PayloadReference requestPayloadRef, PayloadReference responsePayloadRef)> StoreErrorPayloadsAsync(
        string migrationId, 
        string requestId, 
        string requestPayload, 
        string? responsePayload, 
        string entityName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extracts response payload from exception for debugging
    /// </summary>
    /// <param name="exception">Exception to extract from</param>
    /// <returns>Response payload or null if not available</returns>
    string? ExtractResponsePayloadFromException(Exception exception);

    /// <summary>
    /// Extracts HTTP status code from exception
    /// </summary>
    /// <param name="exception">Exception to extract from</param>
    /// <returns>HTTP status code or null if not available</returns>
    int? ExtractHttpStatusFromException(Exception exception);

    /// <summary>
    /// Truncates stack trace for storage efficiency
    /// </summary>
    /// <param name="stackTrace">Full stack trace</param>
    /// <returns>Truncated stack trace</returns>
    string? GetTruncatedStackTrace(string? stackTrace);
}

/// <summary>
/// Reference to stored payload data
/// </summary>
public record PayloadReference(string? PayloadData, string? BlobUrl, int Size); 