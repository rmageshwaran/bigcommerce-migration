using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;

namespace BigCommerce.Migration.Functions.Functions;

/// <summary>
/// HTTP trigger functions for BigCommerce Migration API
/// Provides RESTful endpoints for migration operations
/// </summary>
public class MigrationHttpFunctions
{
    private readonly ILogger<MigrationHttpFunctions> _logger;
    private readonly IBigCommerceApiClient _bigCommerceApiClient;
    private readonly ICategoryTreeResolver _categoryTreeResolver;
    private readonly IOpenSearchService _openSearchService;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly IQueueService _queueService;
    private readonly IBlobService _blobService;

    /// <summary>
    /// Initializes a new instance of the MigrationHttpFunctions class
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="bigCommerceApiClient">BigCommerce API client</param>
    /// <param name="categoryTreeResolver">Category tree resolver</param>
    /// <param name="openSearchService">OpenSearch service</param>
    /// <param name="migrationStorageService">Migration storage service</param>
    /// <param name="queueService">Queue service</param>
    /// <param name="blobService">Blob service</param>
    public MigrationHttpFunctions(
        ILogger<MigrationHttpFunctions> logger,
        IBigCommerceApiClient bigCommerceApiClient,
        ICategoryTreeResolver categoryTreeResolver,
        IOpenSearchService openSearchService,
        IMigrationStorageService migrationStorageService,
        IQueueService queueService,
        IBlobService blobService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bigCommerceApiClient = bigCommerceApiClient ?? throw new ArgumentNullException(nameof(bigCommerceApiClient));
        _categoryTreeResolver = categoryTreeResolver ?? throw new ArgumentNullException(nameof(categoryTreeResolver));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
    }

    /// <summary>
    /// Starts a new migration operation
    /// </summary>
    /// <param name="req">HTTP request containing migration parameters</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with migration ID and status</returns>
    [Function("StartMigration")]
    [OpenApiOperation(operationId: "StartMigration", tags: new[] { "Migrations" },
        Summary = "Start a new migration",
        Description = "Creates and queues a new migration between BigCommerce stores. Validates store credentials and queues the migration for processing.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(MigrationRequest),
        Description = "Migration configuration specifying source store, destination store, and entities to migrate")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Accepted, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration created successfully and queued for processing")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json",
        bodyType: typeof(object), Description = "Invalid migration request or store configuration")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json",
        bodyType: typeof(object), Description = "Authentication required")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "application/json",
        bodyType: typeof(object), Description = "Internal server error")]
    public async Task<HttpResponseData> StartMigration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "migrations")] HttpRequestData req,
        FunctionContext context)
    {
        var migrationId = Guid.NewGuid().ToString();
        
        try
        {
            _logger.LogInformation("Starting migration request processing. MigrationId: {MigrationId}", migrationId);

            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                _logger.LogWarning("Empty request body received. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required", migrationId);
            }

            // Deserialize migration request
            MigrationRequest? migrationRequest;
            try
            {
                migrationRequest = JsonSerializer.Deserialize<MigrationRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON in request body. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid JSON format", migrationId);
            }

            // Validate migration request
            if (migrationRequest == null)
            {
                _logger.LogWarning("Failed to deserialize migration request. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid migration request", migrationId);
            }

            // Basic validation only - keep API response fast
            var validationResult = BasicValidateMigrationRequest(migrationRequest);
            if (!validationResult.IsValid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, validationResult.ErrorMessage, migrationId);
            }

            // Create migration entry for Azure Storage
            var migrationEntry = new Core.Models.MigrationEntry
            {
                Id = migrationId,
                SourceStoreId = migrationRequest.SourceStore?.StoreId ?? string.Empty,
                DestinationStoreId = migrationRequest.DestinationStore?.StoreId ?? string.Empty,
                SourceChannelId = migrationRequest.SourceStore?.ChannelId ?? string.Empty,
                DestinationChannelId = migrationRequest.DestinationStore?.ChannelId ?? string.Empty,
                Entities = migrationRequest.Entities,
                Status = Core.Models.MigrationStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Store migration entry in Azure Table Storage
            await _migrationStorageService.CreateMigrationAsync(migrationEntry);
            _logger.LogInformation("Migration entry stored successfully. MigrationId: {MigrationId}", migrationId);

            // Send migration start message to queue for processing (no category tree context - resolved later)
            await _queueService.SendMigrationStartMessageAsync(migrationId, migrationRequest, null);
            _logger.LogInformation("Migration start message sent to queue for processing. MigrationId: {MigrationId}", migrationId);

            // Log migration start event to OpenSearch
            await _openSearchService.LogMigrationEventAsync("MigrationStarted", migrationId, new
            {
                sourceStore = migrationRequest.SourceStore?.StoreId ?? string.Empty,
                destinationStore = migrationRequest.DestinationStore?.StoreId ?? string.Empty,
                sourceChannel = migrationRequest.SourceStore?.ChannelId ?? string.Empty,
                destinationChannel = migrationRequest.DestinationStore?.ChannelId ?? string.Empty,
                entities = migrationRequest.Entities,
                message = "Migration request received and prepared for processing"
            });

            // Create response
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var responseData = new
            {
                migrationId = migrationId,
                status = "queued",
                message = "Migration request accepted and sent to processing queue",
                sourceStore = new
                {
                    storeId = migrationRequest.SourceStore?.StoreId ?? string.Empty,
                    channelId = migrationRequest.SourceStore?.ChannelId ?? string.Empty
                },
                destinationStore = new
                {
                    storeId = migrationRequest.DestinationStore?.StoreId ?? string.Empty,
                    channelId = migrationRequest.DestinationStore?.ChannelId ?? string.Empty
                },
                entities = migrationRequest.Entities,
                createdAt = migrationEntry.CreatedAt,
                estimatedDuration = EstimateCompletionTime(migrationRequest.Entities),
                links = new
                {
                    self = $"/migrations/{migrationId}",
                    status = $"/migrations/{migrationId}/status",
                    cancel = $"/migrations/{migrationId}/cancel"
                }
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("Migration request successfully prepared. MigrationId: {MigrationId}", migrationId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing migration request. MigrationId: {MigrationId}", migrationId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred", migrationId);
        }
    }

    /// <summary>
    /// Gets the status of a specific migration
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with migration status</returns>
    [Function("GetMigrationStatus")]
    [OpenApiOperation(operationId: "GetMigrationStatus", tags: new[] { "Migrations" },
        Summary = "Get migration status",
        Description = "Retrieves the current status and progress information for a specific migration.")]
    [OpenApiParameter(name: "migrationId", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Unique identifier of the migration")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration status and progress information")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration not found")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json",
        bodyType: typeof(object), Description = "Authentication required")]
    public async Task<HttpResponseData> GetMigrationStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "migrations/{migrationId}")] HttpRequestData req,
        string migrationId,
        FunctionContext context)
    {
        try
        {
            _logger.LogInformation("Getting migration status. MigrationId: {MigrationId}", migrationId);

            // Validate migration ID
            if (string.IsNullOrWhiteSpace(migrationId))
            {
                _logger.LogWarning("Empty migration ID provided");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Migration ID is required", migrationId);
            }

            // Retrieve migration from Azure Storage
            var migrationEntry = await _migrationStorageService.GetMigrationAsync(migrationId);
            
            if (migrationEntry == null)
            {
                _logger.LogWarning("Migration not found. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "Migration not found", migrationId);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            // TODO: Calculate actual progress from entity mappings in next phase
            var responseData = new
            {
                migrationId = migrationEntry.Id,
                status = migrationEntry.Status.ToString().ToLower(),
                message = "Migration status retrieved successfully",
                entities = migrationEntry.Entities,
                createdAt = migrationEntry.CreatedAt,
                updatedAt = migrationEntry.UpdatedAt,
                progress = new
                {
                    overallProgress = migrationEntry.Status == Core.Models.MigrationStatus.Completed ? 100 : 0,
                    completedEntities = migrationEntry.Status == Core.Models.MigrationStatus.Completed ? migrationEntry.Entities.Count() : 0,
                    totalEntities = migrationEntry.Entities.Count(),
                    estimatedTimeRemaining = migrationEntry.Status == Core.Models.MigrationStatus.Completed ? "0 minutes" : "15-30 minutes"
                }
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migration status. MigrationId: {MigrationId}", migrationId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred", migrationId);
        }
    }

    /// <summary>
    /// Gets a list of migrations with optional filtering
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with list of migrations</returns>
    [Function("GetMigrations")]
    public async Task<HttpResponseData> GetMigrations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "migrations")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            _logger.LogInformation("Getting migrations list");

            // Parse query parameters for filtering
            var query = req.Url.Query;
            var statusFilter = ExtractQueryParameter(query, "status");
            var pageSize = int.TryParse(ExtractQueryParameter(query, "pageSize"), out var size) ? size : 10;
            var currentPage = int.TryParse(ExtractQueryParameter(query, "page"), out var page) ? page : 1;
            
            // Create migration query request
            var queryRequest = new Core.Models.MigrationQueryRequest
            {
                Status = statusFilter,
                PageSize = pageSize,
                Page = currentPage
            };

            // Retrieve migrations from Azure Storage
            var migrationListResult = await _migrationStorageService.GetMigrationsAsync(queryRequest);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var responseData = new
            {
                migrations = migrationListResult.Migrations.Select(m => new
                {
                    migrationId = m.Id,
                    status = m.Status.ToString().ToLower(),
                    sourceStore = m.SourceStoreId,
                    destinationStore = m.DestinationStoreId,
                    entities = m.Entities,
                    createdAt = m.CreatedAt,
                    updatedAt = m.UpdatedAt
                }),
                totalCount = migrationListResult.TotalCount,
                pageSize = migrationListResult.PageSize,
                currentPage = migrationListResult.CurrentPage,
                message = "Migrations list retrieved successfully"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migrations list");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred", string.Empty);
        }
    }

    /// <summary>
    /// Cancels a migration operation
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with cancellation status</returns>
    [Function("CancelMigration")]
    public async Task<HttpResponseData> CancelMigration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "migrations/{migrationId}/cancel")] HttpRequestData req,
        string migrationId,
        FunctionContext context)
    {
        try
        {
            _logger.LogInformation("Cancelling migration. MigrationId: {MigrationId}", migrationId);

            // Validate migration ID
            if (string.IsNullOrWhiteSpace(migrationId))
            {
                _logger.LogWarning("Empty migration ID provided for cancellation");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Migration ID is required", migrationId);
            }

            // Get current migration status from storage
            var migrationEntry = await _migrationStorageService.GetMigrationAsync(migrationId);
            
            if (migrationEntry == null)
            {
                _logger.LogWarning("Migration not found for cancellation. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "Migration not found", migrationId);
            }

            // Check if migration can be cancelled
            if (migrationEntry.Status == Core.Models.MigrationStatus.Completed)
            {
                _logger.LogWarning("Cannot cancel completed migration. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Cannot cancel completed migration", migrationId);
            }

            if (migrationEntry.Status == Core.Models.MigrationStatus.Cancelled)
            {
                _logger.LogWarning("Migration already cancelled. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Migration already cancelled", migrationId);
            }

            // Update migration status to cancelled
            migrationEntry.Status = Core.Models.MigrationStatus.Cancelled;
            migrationEntry.UpdatedAt = DateTime.UtcNow;
            await _migrationStorageService.UpdateMigrationAsync(migrationEntry);

            // Create cancellation token for tracking
            await _migrationStorageService.CreateCancellationTokenAsync(migrationId, "User requested cancellation");

            // Send cancellation message to queue for processing
            await _queueService.SendCancellationMessageAsync(migrationId, "User requested cancellation");
            _logger.LogInformation("Migration cancellation message sent to queue. MigrationId: {MigrationId}", migrationId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var responseData = new
            {
                migrationId = migrationId,
                status = "cancelled",
                message = "Migration cancelled successfully",
                cancelledAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling migration. MigrationId: {MigrationId}", migrationId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred", migrationId);
        }
    }

    /// <summary>
    /// Basic validation of migration request (lightweight, no API calls)
    /// </summary>
    /// <param name="migrationRequest">Migration request to validate</param>
    /// <returns>Validation result</returns>
    private ValidationResult BasicValidateMigrationRequest(MigrationRequest migrationRequest)
    {
        // Validate source store configuration (basic field validation only)
        if (migrationRequest.SourceStore == null || 
            string.IsNullOrWhiteSpace(migrationRequest.SourceStore.StoreId) ||
            string.IsNullOrWhiteSpace(migrationRequest.SourceStore.AccessToken))
        {
            return new ValidationResult { IsValid = false, ErrorMessage = "Source store configuration is required (storeId and accessToken)" };
        }

        // Validate destination store configuration (basic field validation only)
        if (migrationRequest.DestinationStore == null || 
            string.IsNullOrWhiteSpace(migrationRequest.DestinationStore.StoreId) ||
            string.IsNullOrWhiteSpace(migrationRequest.DestinationStore.AccessToken))
        {
            return new ValidationResult { IsValid = false, ErrorMessage = "Destination store configuration is required (storeId and accessToken)" };
        }

        // Validate entities list
        if (!migrationRequest.Entities.Any())
        {
            return new ValidationResult { IsValid = false, ErrorMessage = "At least one entity type must be specified" };
        }

        // Validate entity types
        var validEntityTypes = new[] { "categories", "products", "brands", "variants", "modifiers" };
        var invalidEntities = migrationRequest.Entities.Where(e => !validEntityTypes.Contains(e.ToLower())).ToList();
        if (invalidEntities.Any())
        {
            return new ValidationResult { IsValid = false, ErrorMessage = $"Invalid entity types: {string.Join(", ", invalidEntities)}" };
        }

        return new ValidationResult { IsValid = true };
    }

    /// <summary>
    /// DEPRECATED: Full validation with store connectivity testing (moved to orchestrator)
    /// Validates the migration request and store configurations
    /// </summary>
    /// <param name="migrationRequest">Migration request to validate</param>
    /// <param name="migrationId">Migration ID for logging</param>
    /// <returns>Validation result</returns>
    private async Task<ValidationResult> ValidateMigrationRequest(MigrationRequest migrationRequest, string migrationId)
    {
        try
        {
            // Validate source store configuration
            if (migrationRequest.SourceStore == null || !migrationRequest.SourceStore.IsValid())
            {
                return new ValidationResult { IsValid = false, ErrorMessage = "Invalid source store configuration" };
            }

            // Validate destination store configuration
            if (migrationRequest.DestinationStore == null || !migrationRequest.DestinationStore.IsValid())
            {
                return new ValidationResult { IsValid = false, ErrorMessage = "Invalid destination store configuration" };
            }

            // Validate entities list
            if (!migrationRequest.Entities.Any())
            {
                return new ValidationResult { IsValid = false, ErrorMessage = "At least one entity type must be specified" };
            }

            // Validate entity types
            var validEntityTypes = new[] { "categories", "products", "brands", "variants", "modifiers" };
            var invalidEntities = migrationRequest.Entities.Where(e => !validEntityTypes.Contains(e.ToLower())).ToList();
            if (invalidEntities.Any())
            {
                return new ValidationResult { IsValid = false, ErrorMessage = $"Invalid entity types: {string.Join(", ", invalidEntities)}" };
            }

            // Test source store connectivity
            _logger.LogInformation("Testing source store connectivity. MigrationId: {MigrationId}", migrationId);
            try
            {
                var sourceStoreHealthy = await _bigCommerceApiClient.IsHealthyAsync(migrationRequest.SourceStore);
                if (!sourceStoreHealthy)
                {
                    return new ValidationResult { IsValid = false, ErrorMessage = "Unable to connect to source store" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to source store. MigrationId: {MigrationId}", migrationId);
                return new ValidationResult { IsValid = false, ErrorMessage = "Unable to connect to source store: " + ex.Message };
            }

            // Test destination store connectivity
            _logger.LogInformation("Testing destination store connectivity. MigrationId: {MigrationId}", migrationId);
            try
            {
                var destinationStoreHealthy = await _bigCommerceApiClient.IsHealthyAsync(migrationRequest.DestinationStore);
                if (!destinationStoreHealthy)
                {
                    return new ValidationResult { IsValid = false, ErrorMessage = "Unable to connect to destination store" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to destination store. MigrationId: {MigrationId}", migrationId);
                return new ValidationResult { IsValid = false, ErrorMessage = "Unable to connect to destination store: " + ex.Message };
            }

            _logger.LogInformation("Migration request validation successful. MigrationId: {MigrationId}", migrationId);
            return new ValidationResult { IsValid = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during migration request validation. MigrationId: {MigrationId}", migrationId);
            return new ValidationResult { IsValid = false, ErrorMessage = "Validation failed due to internal error" };
        }
    }

    /// <summary>
    /// Estimates completion time based on entity types
    /// </summary>
    /// <param name="entities">List of entity types to migrate</param>
    /// <returns>Estimated completion time in minutes</returns>
    private string EstimateCompletionTime(IEnumerable<string> entities)
    {
        var entityList = entities.ToList();
        var estimatedMinutes = 0;

        // Base estimates per entity type
        if (entityList.Any(e => e.Equals("categories", StringComparison.OrdinalIgnoreCase)))
            estimatedMinutes += 5;
        if (entityList.Any(e => e.Equals("products", StringComparison.OrdinalIgnoreCase)))
            estimatedMinutes += 30;
        if (entityList.Any(e => e.Equals("brands", StringComparison.OrdinalIgnoreCase)))
            estimatedMinutes += 2;
        if (entityList.Any(e => e.Equals("variants", StringComparison.OrdinalIgnoreCase)))
            estimatedMinutes += 10;
        if (entityList.Any(e => e.Equals("modifiers", StringComparison.OrdinalIgnoreCase)))
            estimatedMinutes += 5;

        return estimatedMinutes switch
        {
            <= 5 => "2-5 minutes",
            <= 15 => "5-15 minutes",
            <= 30 => "15-30 minutes",
            <= 60 => "30-60 minutes",
            _ => "1-2 hours"
        };
    }

    /// <summary>
    /// Extracts a query parameter value from the query string
    /// </summary>
    /// <param name="query">Query string</param>
    /// <param name="parameterName">Parameter name</param>
    /// <returns>Parameter value or null if not found</returns>
    private string? ExtractQueryParameter(string query, string parameterName)
    {
        if (string.IsNullOrEmpty(query))
            return null;
            
        var cleanQuery = query.StartsWith("?") ? query.Substring(1) : query;
        var parameters = cleanQuery.Split('&');
        
        foreach (var parameter in parameters)
        {
            var keyValue = parameter.Split('=');
            if (keyValue.Length == 2 && keyValue[0].Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(keyValue[1]);
            }
        }
        
        return null;
    }

    /// <summary>
    /// Creates an error response with consistent format
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="statusCode">HTTP status code</param>
    /// <param name="message">Error message</param>
    /// <param name="migrationId">Migration ID</param>
    /// <returns>HTTP error response</returns>
    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string message, string migrationId)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        
        var errorResponse = new
        {
            error = new
            {
                code = statusCode.ToString(),
                message = message,
                migrationId = migrationId,
                timestamp = DateTime.UtcNow
            }
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        return response;
    }
}

/// <summary>
/// Validation result for migration request validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Migration entry for tracking migration status
/// </summary>
public class MigrationEntry
{
    public string Id { get; set; } = string.Empty;
    public string SourceStoreId { get; set; } = string.Empty;
    public string DestinationStoreId { get; set; } = string.Empty;
    public string SourceChannelId { get; set; } = string.Empty;
    public string DestinationChannelId { get; set; } = string.Empty;
    public IEnumerable<string> Entities { get; set; } = Array.Empty<string>();
    public MigrationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Migration status enumeration
/// </summary>
public enum MigrationStatus
{
    Queued,
    InProgress,
    Completed,
    Failed,
    Cancelled
} 