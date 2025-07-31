using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Implementation of IEntityErrorHandlingService for handling and logging entity processing errors
/// </summary>
public class EntityErrorHandlingService : IEntityErrorHandlingService
{
    private readonly IOpenSearchService _openSearchService;
    private readonly IBlobService _blobService;
    private readonly ILogger<EntityErrorHandlingService> _logger;

    public EntityErrorHandlingService(
        IOpenSearchService openSearchService, 
        IBlobService blobService, 
        ILogger<EntityErrorHandlingService> logger)
    {
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LogStructuredMigrationErrorAsync(
        Exception exception, 
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request, 
        string errorType,
        CancellationToken cancellationToken)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));
        
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            _logger.LogError(exception, "Structured migration error for {EntityType} in migration {MigrationId}, error type: {ErrorType}", 
                request.EntityType, request.MigrationId, errorType);

            // Extract response payload from exception
            var responsePayload = ExtractResponsePayloadFromException(exception);
            
            // Create request payload from entities data (if available)
            string? requestPayload = null;
            PayloadReference? requestPayloadRef = null;
            PayloadReference? responsePayloadRef = null;
            
            if (entities != null && entities.Any())
            {
                requestPayload = JsonSerializer.Serialize(entities, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                // Store error payloads to blob storage
                var requestId = $"{request.EntityType}_{errorType}_{DateTime.UtcNow:yyyyMMddHHmmss}";
                (requestPayloadRef, responsePayloadRef) = await StoreErrorPayloadsAsync(
                    request.MigrationId, 
                    requestId, 
                    requestPayload, 
                    responsePayload, 
                    $"{request.EntityType}_{errorType}",
                    cancellationToken);
            }

            var errorData = new
            {
                migrationId = request.MigrationId,
                entityType = request.EntityType,
                errorType = errorType,
                errorMessage = exception.Message,
                stackTrace = GetTruncatedStackTrace(exception.StackTrace),
                httpStatusCode = ExtractHttpStatusFromException(exception),
                // ✅ REMOVED: responsePayload = responsePayload, // Don't include raw payload, use blob URL instead
                requestPayloadBlobUrl = requestPayloadRef?.BlobUrl,
                responsePayloadBlobUrl = responsePayloadRef?.BlobUrl, // ✅ Only include blob URL for response
                entityCount = entities?.Count ?? 0,
                sourceStoreId = request.SourceStore?.StoreId,
                destinationStoreId = request.DestinationStore?.StoreId,
                batchNumber = request.BatchNumber,
                totalBatches = request.TotalBatches
            };

            // Log to OpenSearch for structured analysis
            var logResult = await _openSearchService.LogErrorAsync(
                $"MigrationError_{request.EntityType}", 
                exception, 
                errorData, 
                cancellationToken);
            
            if (!logResult)
            {
                _logger.LogWarning("Failed to log structured migration error to OpenSearch for {EntityType} in migration {MigrationId}", 
                    request.EntityType, request.MigrationId);
            }
            else
            {
                _logger.LogDebug("Successfully logged structured migration error to OpenSearch for {EntityType} in migration {MigrationId}", 
                    request.EntityType, request.MigrationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log structured migration error for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            // Don't re-throw to avoid masking the original error
        }
    }

    public async Task LogEntityErrorAsync(
        Exception exception, 
        Dictionary<string, object> entity, 
        BatchProcessingRequest request, 
        string entityId,
        CancellationToken cancellationToken)
    {
        await LogEntityErrorAsync(exception, entity, request, entityId, null, cancellationToken);
    }

    public async Task LogEntityErrorAsync(
        Exception exception, 
        Dictionary<string, object> entity, 
        BatchProcessingRequest request, 
        string entityId,
        string? responsePayload,
        CancellationToken cancellationToken)
    {
        await LogEntityErrorAsync(exception, entity, request, entityId, responsePayload, null, cancellationToken);
    }

    public async Task LogEntityErrorAsync(
        Exception exception, 
        Dictionary<string, object> entity, 
        BatchProcessingRequest request, 
        string entityId,
        string? responsePayload,
        string? simpleErrorMessage,
        CancellationToken cancellationToken)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));
        
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            _logger.LogError(exception, "Entity error for {EntityType} {EntityId} in migration {MigrationId}", 
                request.EntityType, entityId, request.MigrationId);

            // Extract response payload from exception if not provided
            var finalResponsePayload = responsePayload ?? ExtractResponsePayloadFromException(exception);
            
            // Create request payload from entity data
            var requestPayload = JsonSerializer.Serialize(entity, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Store error payloads to blob storage
            // Sanitize entityId to avoid URL encoding issues in blob names
            var sanitizedEntityId = SanitizeEntityIdForBlobName(entityId);
            var requestId = $"{request.EntityType}_{sanitizedEntityId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
            var (requestPayloadRef, responsePayloadRef) = await StoreErrorPayloadsAsync(
                request.MigrationId, 
                requestId, 
                requestPayload, 
                finalResponsePayload, 
                $"{request.EntityType}_{sanitizedEntityId}",
                cancellationToken);

            // Debug logging to see what fields are available in entity data
            _logger.LogDebug("Entity data fields for {EntityType}: {Fields}", 
                request.EntityType, string.Join(", ", entity.Keys));
            
            // Extract entity name and ID from entity data
            var entityName = ExtractEntityName(entity, request.EntityType);
            // ✅ Use the entityId parameter passed in (which was extracted before transformation)
            // Don't try to extract again from entity data which may have been modified
            var finalEntityId = entityId; // Use the correct ID passed in from ProcessEntityBatchActivity
            
            _logger.LogDebug("Using entity ID: {EntityId}, entity name: {EntityName}", 
                finalEntityId, entityName);
            
            var errorData = new
            {
                migrationId = request.MigrationId,
                entityType = request.EntityType,
                entityId = finalEntityId,
                entityName = entityName,
                errorMessage = simpleErrorMessage ?? exception.Message, // Use simple message for UI display
                detailedErrorMessage = exception.Message, // Store the full detailed error for expandable view
                stackTrace = GetTruncatedStackTrace(exception.StackTrace),
                httpStatusCode = ExtractHttpStatusFromException(exception),
                // ✅ REMOVED: responsePayload = responsePayload, // Don't include raw payload, use blob URL instead
                requestPayloadBlobUrl = requestPayloadRef.BlobUrl,
                responsePayloadBlobUrl = responsePayloadRef.BlobUrl, // ✅ Only include blob URL for response
                sourceStoreId = request.SourceStore?.StoreId,
                destinationStoreId = request.DestinationStore?.StoreId,
                batchNumber = request.BatchNumber
            };

            // Log to OpenSearch for structured analysis
            var logResult = await _openSearchService.LogErrorAsync(
                $"EntityError_{request.EntityType}", 
                exception, 
                errorData, 
                cancellationToken);
            
            if (!logResult)
            {
                _logger.LogWarning("Failed to log entity error to OpenSearch for {EntityType} {EntityId} in migration {MigrationId}", 
                    request.EntityType, entityId, request.MigrationId);
            }
            else
            {
                _logger.LogDebug("Successfully logged entity error to OpenSearch for {EntityType} {EntityId} in migration {MigrationId}", 
                    request.EntityType, entityId, request.MigrationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log entity error for {EntityType} {EntityId} in migration {MigrationId}", 
                request.EntityType, entityId, request.MigrationId);
            // Don't re-throw to avoid masking the original error
        }
    }

    public async Task<(PayloadReference requestPayloadRef, PayloadReference responsePayloadRef)> StoreErrorPayloadsAsync(
        string migrationId, 
        string requestId, 
        string requestPayload, 
        string? responsePayload, 
        string entityName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request ID cannot be null or empty", nameof(requestId));

        try
        {
            _logger.LogDebug("Storing error payloads for migration {MigrationId}, entity {EntityName}", 
                migrationId, entityName);

            PayloadReference requestRef = new PayloadReference(string.Empty, null, 0);
            PayloadReference responseRef = new PayloadReference(string.Empty, null, 0);

            // Store request payload if provided
            if (!string.IsNullOrWhiteSpace(requestPayload))
            {
                // ✅ Use blob service to store and get FULLY QUALIFIED URL
                var requestBlobUrl = await _blobService.StoreRequestPayloadAsync(
                    migrationId, requestId, requestPayload, "application/json");
                
                requestRef = new PayloadReference(requestPayload, requestBlobUrl, requestPayload.Length);
                
                _logger.LogDebug("Stored request payload for migration {MigrationId}, entity {EntityName} at {BlobUrl}", 
                    migrationId, entityName, requestBlobUrl);
            }

            // Store response payload if provided
            if (!string.IsNullOrWhiteSpace(responsePayload))
            {
                // ✅ Use blob service to store and get FULLY QUALIFIED URL
                var responseBlobUrl = await _blobService.StoreResponsePayloadAsync(
                    migrationId, requestId, responsePayload, "application/json");
                
                responseRef = new PayloadReference(responsePayload, responseBlobUrl, responsePayload.Length);
                
                _logger.LogDebug("Stored response payload for migration {MigrationId}, entity {EntityName} at {BlobUrl}", 
                    migrationId, entityName, responseBlobUrl);
            }

            _logger.LogInformation("Successfully stored error payloads for migration {MigrationId}, entity {EntityName}", 
                migrationId, entityName);

            return (requestRef, responseRef);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store error payloads for migration {MigrationId}, entity {EntityName}", 
                migrationId, entityName);
            throw new InvalidOperationException("Failed to store error payloads", ex);
        }
    }

    public string? ExtractResponsePayloadFromException(Exception exception)
    {
        if (exception == null)
            return null;

        try
        {
            _logger.LogDebug("🔍 Extracting response payload from exception: {ExceptionType}, Data keys: {DataKeys}", 
                exception.GetType().Name, string.Join(", ", exception.Data.Keys.Cast<object>()));

            // ✅ First check if response payload is stored in exception data (from enhanced exceptions)
            if (exception.Data.Contains("ResponsePayload"))
            {
                var responsePayload = exception.Data["ResponsePayload"]?.ToString();
                if (!string.IsNullOrEmpty(responsePayload))
                {
                    _logger.LogDebug("✅ Found response payload in exception.Data[\"ResponsePayload\"], length: {Length}", responsePayload.Length);
                    return responsePayload;
                }
            }

            // Try to extract response payload from common exception types
            if (exception is HttpRequestException httpEx)
            {
                // Check if the exception has response content
                if (httpEx.Data.Contains("ResponseContent"))
                {
                    return httpEx.Data["ResponseContent"]?.ToString();
                }
            }

            // Check for inner exceptions
            var innerException = exception.InnerException;
            while (innerException != null)
            {
                if (innerException is HttpRequestException innerHttpEx)
                {
                    if (innerHttpEx.Data.Contains("ResponseContent"))
                    {
                        return innerHttpEx.Data["ResponseContent"]?.ToString();
                    }
                }
                innerException = innerException.InnerException;
            }

            // Enhanced extraction from exception message for BigCommerce API errors
            var message = exception.Message;
            
            // Look for BigCommerce API error pattern: "API request failed with status XXX: {JSON_CONTENT}"
            if (message.Contains("API request failed with status"))
            {
                var colonIndex = message.LastIndexOf(':');
                if (colonIndex > 0 && colonIndex < message.Length - 1)
                {
                    var content = message.Substring(colonIndex + 1).Trim();
                    
                    // Validate it's actually JSON
                    try
                    {
                        JsonDocument.Parse(content);
                        return content;
                    }
                    catch (JsonException)
                    {
                        // Not valid JSON, continue to other extraction methods
                    }
                }
            }

            // Try to extract from exception message if it contains JSON-like content
            if (message.Contains("{") && message.Contains("}"))
            {
                var startIndex = message.IndexOf('{');
                var endIndex = message.LastIndexOf('}');
                if (startIndex >= 0 && endIndex > startIndex)
                {
                    var jsonContent = message.Substring(startIndex, endIndex - startIndex + 1);
                    // Validate it's actually JSON
                    try
                    {
                        JsonDocument.Parse(jsonContent);
                        return jsonContent;
                    }
                    catch (JsonException)
                    {
                        // Not valid JSON, continue
                    }
                }
            }

            _logger.LogDebug("❌ No response payload found in exception");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract response payload from exception");
            return null;
        }
    }

    public int? ExtractHttpStatusFromException(Exception exception)
    {
        if (exception == null)
            return null;

        try
        {
            // Try to extract HTTP status from common exception types
            if (exception is HttpRequestException httpEx)
            {
                // Check if the exception has status code
                if (httpEx.Data.Contains("StatusCode"))
                {
                    if (int.TryParse(httpEx.Data["StatusCode"]?.ToString(), out var statusCode))
                    {
                        return statusCode;
                    }
                }
            }

            // Check for inner exceptions
            var innerException = exception.InnerException;
            while (innerException != null)
            {
                if (innerException is HttpRequestException innerHttpEx)
                {
                    if (innerHttpEx.Data.Contains("StatusCode"))
                    {
                        if (int.TryParse(innerHttpEx.Data["StatusCode"]?.ToString(), out var statusCode))
                        {
                            return statusCode;
                        }
                    }
                }
                innerException = innerException.InnerException;
            }

            // Try to extract from exception message
            var message = exception.Message.ToLowerInvariant();
            if (message.Contains("404"))
                return (int)HttpStatusCode.NotFound;
            if (message.Contains("400"))
                return (int)HttpStatusCode.BadRequest;
            if (message.Contains("401"))
                return (int)HttpStatusCode.Unauthorized;
            if (message.Contains("403"))
                return (int)HttpStatusCode.Forbidden;
            if (message.Contains("429"))
                return (int)HttpStatusCode.TooManyRequests;
            if (message.Contains("500"))
                return (int)HttpStatusCode.InternalServerError;
            if (message.Contains("502"))
                return (int)HttpStatusCode.BadGateway;
            if (message.Contains("503"))
                return (int)HttpStatusCode.ServiceUnavailable;
            if (message.Contains("504"))
                return (int)HttpStatusCode.GatewayTimeout;

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract HTTP status from exception");
            return null;
        }
    }

    public string? GetTruncatedStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
            return null;

        // Truncate to first 1000 characters to avoid overly large logs
        const int maxLength = 1000;
        return stackTrace.Length > maxLength ? stackTrace[..maxLength] + "..." : stackTrace;
    }
    
    /// <summary>
    /// Extracts entity name from entity data based on entity type
    /// </summary>
    /// <param name="entity">Entity data dictionary</param>
    /// <param name="entityType">Type of entity</param>
    /// <returns>Entity name or null if not found</returns>
    private string? ExtractEntityName(Dictionary<string, object> entity, string entityType)
    {
        if (entity == null || entity.Count == 0)
            return null;

        try
        {
            // Extract name based on entity type
            return entityType.ToLowerInvariant() switch
            {
                "categories" or "category" => entity.TryGetValue("name", out var categoryName) ? categoryName?.ToString() : null,
                "products" or "product" => entity.TryGetValue("name", out var productName) ? productName?.ToString() : null,
                "brands" or "brand" => entity.TryGetValue("name", out var brandName) ? brandName?.ToString() : null,
                "variants" or "variant" => entity.TryGetValue("sku", out var variantSku) ? variantSku?.ToString() : null,
                "modifiers" or "modifier" => entity.TryGetValue("display_name", out var modifierName) ? modifierName?.ToString() : null,
                _ => entity.TryGetValue("name", out var genericName) ? genericName?.ToString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract entity name for entity type {EntityType}", entityType);
            return null;
        }
    }

    private string SanitizeEntityIdForBlobName(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return "unknown";
        }

        // Replace problematic characters with underscores
        return entityId.Replace(":", "_").Replace(".", "_").Replace("-", "_").Replace(" ", "_");
    }
} 
