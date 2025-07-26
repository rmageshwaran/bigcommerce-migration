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
    private readonly IProgressTracker _progressTracker;

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
        IBlobService blobService,
        IProgressTracker progressTracker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bigCommerceApiClient = bigCommerceApiClient ?? throw new ArgumentNullException(nameof(bigCommerceApiClient));
        _categoryTreeResolver = categoryTreeResolver ?? throw new ArgumentNullException(nameof(categoryTreeResolver));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));
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
    [Function("GetMigrationStatusHttp")]
    [OpenApiOperation(operationId: "GetMigrationStatusHttp", tags: new[] { "Migrations" },
        Summary = "Get migration status (HTTP API)",
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "migrations/{migrationId}/status-http")] HttpRequestData req,
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

            // Get detailed progress information
            MigrationProgress? detailedProgress = null;
            try
            {
                detailedProgress = await _progressTracker.GetProgressAsync(migrationId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get detailed progress for migration {MigrationId}, using basic status", migrationId);
            }

            // Determine the actual status based on both storage and progress data
            var actualStatus = migrationEntry.Status.ToString().ToLower();
            
            // Check if progress tracker has meaningful data (not just default values after restart)
            var hasMeaningfulProgress = detailedProgress != null && 
                (detailedProgress.TotalEntities > 0 || 
                 detailedProgress.ProcessedEntities > 0 || 
                 detailedProgress.SuccessfulEntities > 0 || 
                 detailedProgress.FailedEntities > 0 ||
                 (detailedProgress.EntityProgress?.Any() == true));
            
            // If progress tracker has no meaningful data (after restart), reconstruct from storage
            if (!hasMeaningfulProgress && migrationEntry.Status == Core.Models.MigrationStatus.Completed)
            {
                _logger.LogInformation("Migration {MigrationId} completed but progress tracker has no data (after restart). Reconstructing from storage.", migrationId);
                
                // For completed migrations, reconstruct progress data from storage
                var reconstructedProgress = new MigrationProgress
                {
                    MigrationId = migrationId,
                    Status = "completed",
                    OverallProgressPercentage = 100,
                    TotalEntities = migrationEntry.Entities?.Count() ?? 0,
                    ProcessedEntities = migrationEntry.Entities?.Count() ?? 0,
                    SuccessfulEntities = migrationEntry.Entities?.Count() ?? 0, // Assume all succeeded for completed migrations
                    FailedEntities = 0, // Could be refined if we store failure counts
                    StartTime = migrationEntry.CreatedAt,
                    LastUpdated = migrationEntry.UpdatedAt,
                    ElapsedTime = migrationEntry.UpdatedAt - migrationEntry.CreatedAt,
                    EstimatedTimeRemaining = TimeSpan.Zero,
                    EntitiesPerSecond = 0,
                    ErrorRate = 0,
                    CurrentPhase = "completed",
                    CurrentEntity = "",
                    EntityProgress = migrationEntry.Entities?.ToDictionary(
                        entity => entity,
                        entity => new EntityProgress
                        {
                            EntityType = entity,
                            TotalCount = 1, // Could be refined if we store actual counts
                            ProcessedCount = 1,
                            SuccessCount = 1,
                            FailureCount = 0,
                            ProgressPercentage = 100,
                            Status = "completed",
                            StartTime = migrationEntry.CreatedAt,
                            EndTime = migrationEntry.UpdatedAt,
                            ProcessingTime = migrationEntry.UpdatedAt - migrationEntry.CreatedAt
                        }) ?? new Dictionary<string, EntityProgress>()
                };
                
                detailedProgress = reconstructedProgress;
            }
            else if (hasMeaningfulProgress && detailedProgress != null)
            {
                // If storage shows completed but progress shows in-progress, check if all entities are actually done
                if (migrationEntry.Status == Core.Models.MigrationStatus.Completed && detailedProgress.Status == "inprogress")
                {
                    // Check if all entities are completed
                    var allEntitiesCompleted = detailedProgress.EntityProgress?.All(ep => ep.Value.Status == "completed") ?? false;
                    if (!allEntitiesCompleted)
                    {
                        _logger.LogWarning("Migration {MigrationId} shows completed in storage but entities are still in progress. Updating status.", migrationId);
                        actualStatus = "inprogress";
                    }
                }
                
                // If progress shows completed but storage shows in-progress, update the status
                if (migrationEntry.Status == Core.Models.MigrationStatus.InProgress && detailedProgress.Status == "completed")
                {
                    var allEntitiesCompleted = detailedProgress.EntityProgress?.All(ep => ep.Value.Status == "completed") ?? false;
                    if (allEntitiesCompleted)
                    {
                        _logger.LogInformation("Migration {MigrationId} shows in-progress in storage but all entities are completed. Status should be completed.", migrationId);
                        actualStatus = "completed";
                    }
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var responseData = new
            {
                migrationId = migrationEntry.Id,
                status = actualStatus,
                message = "Migration status retrieved successfully",
                entities = migrationEntry.Entities,
                createdAt = migrationEntry.CreatedAt,
                updatedAt = migrationEntry.UpdatedAt,
                progress = new
                {
                    overallProgress = detailedProgress?.OverallProgressPercentage ?? (actualStatus == "completed" ? 100 : 0),
                    completedEntities = detailedProgress?.SuccessfulEntities ?? 0,
                    totalEntities = detailedProgress?.TotalEntities ?? 0,
                    failedEntities = detailedProgress?.FailedEntities ?? 0,
                    processedEntities = detailedProgress?.ProcessedEntities ?? 0,
                    estimatedTimeRemaining = detailedProgress?.EstimatedTimeRemaining.TotalMinutes > 0 
                        ? $"{Math.Ceiling(detailedProgress.EstimatedTimeRemaining.TotalMinutes)} minutes" 
                        : (actualStatus == "completed" ? "0 minutes" : "15-30 minutes")
                },
                entityProgress = detailedProgress?.EntityProgress?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)new
                    {
                        entityType = kvp.Value.EntityType,
                        totalCount = kvp.Value.TotalCount,
                        processedCount = kvp.Value.ProcessedCount,
                        successCount = kvp.Value.SuccessCount,
                        failureCount = kvp.Value.FailureCount,
                        progressPercentage = kvp.Value.ProgressPercentage,
                        status = kvp.Value.Status,
                        startTime = kvp.Value.StartTime,
                        endTime = kvp.Value.EndTime,
                        processingTime = kvp.Value.ProcessingTime
                    }) ?? new Dictionary<string, object>()
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
    /// Gets the latest migration for a specific store
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="storeId">Store ID</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with latest migration details</returns>
    [Function("GetLatestMigrationForStore")]
    [OpenApiOperation(operationId: "GetLatestMigrationForStore", tags: new[] { "Migrations" },
        Summary = "Get latest migration for store",
        Description = "Retrieves the most recent migration for a specific store with detailed progress information.")]
    [OpenApiParameter(name: "storeId", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Store identifier")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Latest migration details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
        bodyType: typeof(object), Description = "No migration found for store")]
    public async Task<HttpResponseData> GetLatestMigrationForStore(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "migrations/latest/{storeId}")] HttpRequestData req,
        string storeId,
        FunctionContext context)
    {
        try
        {
            _logger.LogInformation("Getting latest migration for store. StoreId: {StoreId}", storeId);

            // Query for migrations involving this store (as source or destination)
            var queryRequest = new Core.Models.MigrationQueryRequest
            {
                SourceStoreId = storeId,
                DestinationStoreId = storeId,
                PageSize = 1,
                Page = 1,
                SortBy = "CreatedAt",
                SortDirection = "desc"
            };

            var migrationListResult = await _migrationStorageService.GetMigrationsAsync(queryRequest);
            
            if (!migrationListResult.Migrations.Any())
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "No migration found for store", storeId);
            }

            var latestMigration = migrationListResult.Migrations.First();
            
            // Get detailed progress information
            var detailedProgress = await GetDetailedProgressAsync(latestMigration.Id);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var responseData = new
            {
                storeId = storeId,
                migrationId = latestMigration.Id,
                startDateTime = latestMigration.CreatedAt,
                endDateTime = latestMigration.UpdatedAt,
                status = latestMigration.Status.ToString().ToLower(),
                percentageCompleted = detailedProgress.OverallProgressPercentage,
                sourceStore = latestMigration.SourceStoreId,
                destinationStore = latestMigration.DestinationStoreId,
                totalEntities = detailedProgress.TotalEntities,
                processedEntities = detailedProgress.ProcessedEntities,
                successfulEntities = detailedProgress.SuccessfulEntities,
                failedEntities = detailedProgress.FailedEntities,
                message = "Latest migration retrieved successfully"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest migration for store {StoreId}", storeId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred", storeId);
        }
    }

    /// <summary>
    /// Gets migration history with date range filtering
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with migration history</returns>
    [Function("GetMigrationHistory")]
    [OpenApiOperation(operationId: "GetMigrationHistory", tags: new[] { "Migrations" },
        Summary = "Get migration history",
        Description = "Retrieves migration history with date range filtering and detailed entity counts.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration history")]
    public async Task<HttpResponseData> GetMigrationHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "migrations/history")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            _logger.LogInformation("Getting migration history");

            // Parse query parameters for filtering
            var query = req.Url.Query;
            var startDate = ExtractQueryParameter(query, "startDate");
            var endDate = ExtractQueryParameter(query, "endDate");
            var statusFilter = ExtractQueryParameter(query, "status");
            var sourceStore = ExtractQueryParameter(query, "sourceStore");
            var destinationStore = ExtractQueryParameter(query, "destinationStore");
            var migrationId = ExtractQueryParameter(query, "migrationId");
            var pageSize = int.TryParse(ExtractQueryParameter(query, "pageSize"), out var size) ? size : 50;
            var currentPage = int.TryParse(ExtractQueryParameter(query, "page"), out var page) ? page : 1;
            
            // Create migration query request
            var queryRequest = new Core.Models.MigrationQueryRequest
            {
                Status = statusFilter,
                SourceStoreId = sourceStore,
                DestinationStoreId = destinationStore,
                MigrationId = migrationId,
                PageSize = pageSize,
                Page = currentPage,
                SortBy = "CreatedAt",
                SortDirection = "desc"
            };

            // Add date range filtering if provided
            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var start))
            {
                queryRequest.CreatedAfter = start;
            }
            if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var end))
            {
                queryRequest.CreatedBefore = end;
            }

            // Retrieve migrations from Azure Storage
            var migrationListResult = await _migrationStorageService.GetMigrationsAsync(queryRequest);
            
            // Get detailed progress for each migration
            var migrationsWithDetails = new List<object>();
            foreach (var migration in migrationListResult.Migrations)
            {
                var detailedProgress = await GetDetailedProgressAsync(migration.Id);
                
                migrationsWithDetails.Add(new
                {
                    migrationId = migration.Id,
                    sourceStore = migration.SourceStoreId,
                    destinationStore = migration.DestinationStoreId,
                    startedAt = migration.CreatedAt,
                    completedAt = migration.UpdatedAt,
                    status = migration.Status.ToString().ToLower(),
                    totalEntities = detailedProgress.TotalEntities,
                    processedEntities = detailedProgress.ProcessedEntities,
                    successfulEntities = detailedProgress.SuccessfulEntities,
                    failedEntities = detailedProgress.FailedEntities,
                    percentageCompleted = detailedProgress.OverallProgressPercentage,
                    entities = migration.Entities
                });
            }
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var responseData = new
            {
                migrations = migrationsWithDetails,
                totalCount = migrationListResult.TotalCount,
                pageSize = migrationListResult.PageSize,
                currentPage = migrationListResult.CurrentPage,
                totalPages = migrationListResult.TotalPages,
                hasMorePages = migrationListResult.HasMorePages,
                message = "Migration history retrieved successfully"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migration history");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred", string.Empty);
        }
    }

    /// <summary>
    /// Gets detailed entity breakdown for a specific migration
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with entity breakdown</returns>
    [Function("GetMigrationEntityBreakdown")]
    [OpenApiOperation(operationId: "GetMigrationEntityBreakdown", tags: new[] { "Migrations" },
        Summary = "Get migration entity breakdown",
        Description = "Retrieves detailed entity-level breakdown for a specific migration.")]
    [OpenApiParameter(name: "migrationId", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Migration identifier")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Entity breakdown")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration not found")]
    public async Task<HttpResponseData> GetMigrationEntityBreakdown(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "migrations/{migrationId}/entities")] HttpRequestData req,
        string migrationId,
        FunctionContext context)
    {
        try
        {
            _logger.LogInformation("Getting entity breakdown for migration. MigrationId: {MigrationId}", migrationId);

            // Get migration details
            var migrationEntry = await _migrationStorageService.GetMigrationAsync(migrationId);
            if (migrationEntry == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "Migration not found", migrationId);
            }

            // Get detailed progress
            var detailedProgress = await GetDetailedProgressAsync(migrationId);
            
            // Build entity breakdown
            var entityBreakdown = new List<object>();
            if (detailedProgress.EntityProgress != null)
            {
                foreach (var entityProgress in detailedProgress.EntityProgress)
                {
                    entityBreakdown.Add(new
                    {
                        entity = entityProgress.Key,
                        source = $"{migrationEntry.SourceStoreId} ({entityProgress.Value.TotalCount})",
                        destination = $"{migrationEntry.DestinationStoreId} ({entityProgress.Value.SuccessCount})",
                        status = entityProgress.Value.Status,
                        totalEntities = entityProgress.Value.TotalCount,
                        successfulEntities = entityProgress.Value.SuccessCount,
                        failedEntities = entityProgress.Value.FailureCount,
                        skippedEntities = 0, // EntityProgress doesn't have SkippedEntities
                        percentageCompleted = entityProgress.Value.ProgressPercentage,
                        hasErrors = entityProgress.Value.FailureCount > 0
                    });
                }
            }
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var responseData = new
            {
                migrationId = migrationId,
                sourceStore = migrationEntry.SourceStoreId,
                destinationStore = migrationEntry.DestinationStoreId,
                status = migrationEntry.Status.ToString().ToLower(),
                startTime = migrationEntry.CreatedAt,
                endTime = migrationEntry.UpdatedAt,
                entities = entityBreakdown,
                message = "Entity breakdown retrieved successfully"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting entity breakdown for migration {MigrationId}", migrationId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred", migrationId);
        }
    }

    /// <summary>
    /// Gets errors for a specific entity type in a migration
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with entity errors</returns>
    [Function("GetMigrationEntityErrors")]
    [OpenApiOperation(operationId: "GetMigrationEntityErrors", tags: new[] { "Migrations" },
        Summary = "Get migration entity errors",
        Description = "Retrieves detailed error information for a specific entity type in a migration.")]
    [OpenApiParameter(name: "migrationId", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Migration identifier")]
    [OpenApiParameter(name: "entityType", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Entity type")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Entity errors")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration or entity not found")]
    public async Task<HttpResponseData> GetMigrationEntityErrors(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "migrations/{migrationId}/entities/{entityType}/errors")] HttpRequestData req,
        string migrationId,
        string entityType,
        FunctionContext context)
    {
        try
        {
            _logger.LogInformation("Getting entity errors for migration. MigrationId: {MigrationId}, EntityType: {EntityType}", migrationId, entityType);

            // Parse query parameters for pagination
            var query = req.Url.Query;
            var pageSize = int.TryParse(ExtractQueryParameter(query, "pageSize"), out var size) ? size : 50;
            var currentPage = int.TryParse(ExtractQueryParameter(query, "page"), out var page) ? page : 1;

            // Get errors from logging service
            var from = DateTime.UtcNow.AddDays(-30); // Default to last 30 days
            var to = DateTime.UtcNow;
            
            // ✅ SIMPLIFIED: Use individual fields instead of complex SearchTerm to avoid conflicts
            var queryRequest = new Core.Models.OpenSearchQuery
            {
                FromDate = from,
                ToDate = to,
                MigrationId = migrationId,
                Level = "Error", // Use Level field for category:Error
                EntityType = entityType, // Use EntityType field 
                SearchTerm = null, // ✅ REMOVED: Don't use SearchTerm to avoid conflicts with individual filters
                Size = pageSize,
                From = (currentPage - 1) * pageSize,
                SortField = "timestamp", // ✅ FIXED: Use lowercase timestamp to match OpenSearch field name
                SortOrder = Core.Models.SortOrder.Descending,
                IncludeFields = null // Include all fields
            };

            // ✅ DEBUG: Log the search parameters being used
            _logger.LogWarning("🔍 DEBUG: Error search parameters - MigrationId: {MigrationId}, EntityType: {EntityType}, Level: {Level}, DateRange: {From} to {To}", 
                queryRequest.MigrationId, queryRequest.EntityType, queryRequest.Level, queryRequest.FromDate, queryRequest.ToDate);
            
            var (searchResults, totalCount) = await _openSearchService.SearchLogsOptimizedAsync(queryRequest);
            
            // ✅ DEBUG: Always check what document types exist for this migration (regardless of search results)
            _logger.LogWarning("🔍 DEBUG: Searching for ALL document types for this migration ID to understand available categories...");
            
            var debugQueryRequest = new Core.Models.OpenSearchQuery
            {
                FromDate = from,
                ToDate = to,
                MigrationId = migrationId,
                Level = null, // ✅ Remove Level filter to see all document types
                EntityType = entityType,
                SearchTerm = null,
                Size = 10, // Get more documents to see variety
                From = 0,
                SortField = "timestamp",
                SortOrder = Core.Models.SortOrder.Descending,
                IncludeFields = null
            };
            
            var (debugResults, debugCount) = await _openSearchService.SearchLogsOptimizedAsync(debugQueryRequest);
            _logger.LogWarning("🔍 DEBUG: All document types search returned {ResultCount} results, available categories:", 
                debugResults?.Count() ?? 0);
            
            if (debugResults?.Any() == true)
            {
                // Log the first few documents to see different types
                for (int i = 0; i < Math.Min(5, debugResults.Count()); i++)
                {
                    var doc = debugResults.ElementAt(i);
                    if (doc is System.Text.Json.JsonElement element)
                    {
                        var categoryField = element.TryGetProperty("category", out var cat) ? cat.GetString() : "unknown";
                        var levelField = element.TryGetProperty("level", out var lev) ? lev.GetString() : "unknown";
                        var timestampField = element.TryGetProperty("timestamp", out var ts) ? ts.GetString() : "unknown";
                        _logger.LogWarning("🔍 DEBUG: Document {Index} - category: {Category}, level: {Level}, timestamp: {Timestamp}", 
                            i + 1, categoryField, levelField, timestampField);
                    }
                    else if (doc is Dictionary<string, object> dict)
                    {
                        var categoryField = dict.ContainsKey("category") ? dict["category"]?.ToString() : "unknown";
                        var levelField = dict.ContainsKey("level") ? dict["level"]?.ToString() : "unknown";
                        _logger.LogWarning("🔍 DEBUG: Document {Index} - category: {Category}, level: {Level}", 
                            i + 1, categoryField, levelField);
                    }
                    else
                    {
                        _logger.LogWarning("🔍 DEBUG: Document {Index} - Unknown document type: {Type}", 
                            i + 1, doc?.GetType().Name ?? "null");
                    }
                }
                
                // ✅ Additional diagnostic: Check if ANY Error category documents exist at all
                var errorCategoryCount = debugResults.Count(doc => {
                    if (doc is System.Text.Json.JsonElement element)
                    {
                        return element.TryGetProperty("category", out var cat) && 
                               cat.GetString()?.Equals("Error", StringComparison.OrdinalIgnoreCase) == true;
                    }
                    return false;
                });
                
                _logger.LogWarning("🔍 DEBUG: Found {ErrorCount} documents with category='Error' out of {TotalCount} total documents", 
                    errorCategoryCount, debugResults.Count());
            }
            
            // ✅ DEBUG: Log the raw search results
            _logger.LogWarning("🔍 DEBUG: OpenSearch returned {ResultCount} results, TotalCount: {TotalCount}", 
                searchResults?.Count() ?? 0, totalCount);
            
            if (searchResults?.Any() == true)
            {
                _logger.LogWarning("🔍 DEBUG: First result structure: {FirstResult}", 
                    System.Text.Json.JsonSerializer.Serialize(searchResults.First()));
                
                // ✅ DEBUG: Analyze document structure to understand available fields
                var firstDoc = searchResults.First();
                if (firstDoc is System.Text.Json.JsonElement element)
                {
                    _logger.LogWarning("🔍 DEBUG: Available fields in document: {Fields}", 
                        string.Join(", ", element.EnumerateObject().Select(p => $"{p.Name}={p.Value}")));
                }
                else if (firstDoc is Dictionary<string, object> dict)
                {
                    _logger.LogWarning("🔍 DEBUG: Available fields in document: {Fields}", 
                        string.Join(", ", dict.Keys));
                }
            }
            else
            {
                // ✅ DEBUG: If no results found, try a broader search without EntityType filter
                _logger.LogWarning("🔍 DEBUG: No results found with EntityType filter. Trying broader search...");
                
                var fallbackQueryRequest = new Core.Models.OpenSearchQuery
                {
                    FromDate = from,
                    ToDate = to,
                    MigrationId = migrationId,
                    Level = "Error", // Keep the Error level filter
                    EntityType = null, // ✅ Remove EntityType filter for broader search
                    SearchTerm = null,
                    Size = 10, // Just a few results for debugging
                    From = 0,
                    SortField = "timestamp", // ✅ FIXED: Use lowercase timestamp field name
                    SortOrder = Core.Models.SortOrder.Descending,
                    IncludeFields = null
                };
                
                var (fallbackResults, fallbackCount) = await _openSearchService.SearchLogsOptimizedAsync(fallbackQueryRequest);
                _logger.LogWarning("🔍 DEBUG: Fallback search (no EntityType filter) returned {ResultCount} results", 
                    fallbackResults?.Count() ?? 0);
                
                if (fallbackResults?.Any() == true)
                {
                    _logger.LogWarning("🔍 DEBUG: Fallback result structure: {FallbackResult}", 
                        System.Text.Json.JsonSerializer.Serialize(fallbackResults.First()));
                }
                else
                {
                    // ✅ DEBUG: Try the broadest possible search - any errors at all
                    _logger.LogWarning("🔍 DEBUG: No results even without EntityType filter. Trying broadest search for ANY errors...");
                    
                    var broadestQueryRequest = new Core.Models.OpenSearchQuery
                    {
                        FromDate = from,
                        ToDate = to,
                        MigrationId = null, // ✅ Remove MigrationId filter 
                        Level = "Error", // Keep just the Error level filter
                        EntityType = null, 
                        SearchTerm = null,
                        Size = 5, // Just a few results for debugging
                        From = 0,
                        SortField = "timestamp", // ✅ FIXED: Use lowercase timestamp field name
                        SortOrder = Core.Models.SortOrder.Descending,
                        IncludeFields = null
                    };
                    
                    var (broadestResults, broadestCount) = await _openSearchService.SearchLogsOptimizedAsync(broadestQueryRequest);
                    _logger.LogWarning("🔍 DEBUG: Broadest search (any errors) returned {ResultCount} results", 
                        broadestResults?.Count() ?? 0);
                    
                    if (broadestResults?.Any() == true)
                    {
                        _logger.LogWarning("🔍 DEBUG: Broadest result structure: {BroadestResult}", 
                            System.Text.Json.JsonSerializer.Serialize(broadestResults.First()));
                    }
                    else
                    {
                        _logger.LogError("🔍 DEBUG: NO ERRORS FOUND IN ENTIRE SYSTEM! OpenSearch may be empty or connection issue.");
                    }
                }
                
                // ✅ DEBUG: Try finding ALL document types for this migration (no Level filter)
                _logger.LogWarning("🔍 DEBUG: Searching for ALL document types for this migration ID...");
                
                var allDocsQueryRequest = new Core.Models.OpenSearchQuery
                {
                    FromDate = from,
                    ToDate = to,
                    MigrationId = migrationId,
                    Level = null, // ✅ Remove Level filter to see all document types
                    EntityType = entityType,
                    SearchTerm = null,
                    Size = 10, // Get more documents to see variety
                    From = 0,
                    SortField = "timestamp",
                    SortOrder = Core.Models.SortOrder.Descending,
                    IncludeFields = null
                };
                
                var (allDocsResults, allDocsCount) = await _openSearchService.SearchLogsOptimizedAsync(allDocsQueryRequest);
                _logger.LogWarning("🔍 DEBUG: All document types search returned {ResultCount} results", 
                    allDocsResults?.Count() ?? 0);
                
                if (allDocsResults?.Any() == true)
                {
                    // Log the first few documents to see different types
                    for (int i = 0; i < Math.Min(3, allDocsResults.Count()); i++)
                    {
                        var doc = allDocsResults.ElementAt(i);
                        _logger.LogWarning("🔍 DEBUG: Document {Index} structure: {DocStructure}", 
                            i + 1, System.Text.Json.JsonSerializer.Serialize(doc));
                    }
                }
            }
            
            // Convert OpenSearch results to proper format - handle JsonElement results
            var errorLogs = new List<Dictionary<string, object>>();
            foreach (var result in searchResults)
            {
                if (result is JsonElement jsonElement)
                {
                    errorLogs.Add(ParseJsonElementToDictionary(jsonElement));
                }
                else if (result is Dictionary<string, object> dict)
                {
                    errorLogs.Add(dict);
                }
                else
                {
                    _logger.LogWarning("Unexpected result type from OpenSearch: {Type}", result?.GetType()?.Name ?? "null");
                }
            }
            
            // If no results with structured query, try broader search without entity type filter
            if (!errorLogs.Any())
            {
                _logger.LogInformation("No entity-specific errors found, trying broader search for migration {MigrationId}", migrationId);
                
                queryRequest.EntityType = null; // Remove entity type filter
                queryRequest.SearchTerm = entityType; // Use as text search instead
                
                var (broadResults, broadTotalCount) = await _openSearchService.SearchLogsOptimizedAsync(queryRequest);
                
                // Convert broader search results
                foreach (var result in broadResults)
                {
                    if (result is JsonElement jsonElement)
                    {
                        var dict = ParseJsonElementToDictionary(jsonElement);
                        if (IsLogEntityRelated(dict, entityType))
                        {
                            errorLogs.Add(dict);
                        }
                    }
                    else if (result is Dictionary<string, object> dict)
                    {
                        if (IsLogEntityRelated(dict, entityType))
                        {
                            errorLogs.Add(dict);
                        }
                    }
                }
                totalCount = errorLogs.Count;
            }

            _logger.LogInformation("Found {Count} error logs for entity type {EntityType} in migration {MigrationId}", errorLogs.Count, entityType, migrationId);
            
            // Debug: Log the structure of the first result to understand what we're getting
            if (errorLogs.Any())
            {
                var firstLog = errorLogs.First();
                _logger.LogInformation("First error log structure: {Keys}", string.Join(", ", firstLog.Keys));
                _logger.LogInformation("First error log category: {Category}", firstLog.TryGetValue("category", out var cat) ? cat : "null");
                _logger.LogInformation("First error log context: {Context}", firstLog.TryGetValue("context", out var ctx) ? ctx : "null");
            }

            // Process Error category logs only - they have all the detailed error information we need
            var errors = new List<object>();
            
            foreach (var logDict in errorLogs)
            {
                // ✅ Additional safety filter - ensure this error belongs to the requested migration
                var logMigrationId = GetMigrationIdFromLog(logDict);
                if (!string.IsNullOrEmpty(logMigrationId) && !logMigrationId.Equals(migrationId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("🚫 Skipping error from different migration: {LogMigrationId} (requested: {RequestedMigrationId})", 
                        logMigrationId, migrationId);
                    continue; // Skip errors from other migrations
                }
                
                var category = logDict.TryGetValue("category", out var cat) ? cat?.ToString() : null;
                
                // Only process Error category logs
                if (!"Error".Equals(category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                string errorMessage = "Unknown error";
                string? stackTrace = null;
                
                // Extract error message from exception
                if (logDict.TryGetValue("exception", out var exception))
                {
                    Dictionary<string, object>? exceptionDict = null;
                    if (exception is JsonElement exceptionJsonElement)
                    {
                        exceptionDict = ParseJsonElementToDictionary(exceptionJsonElement);
                    }
                    else if (exception is Dictionary<string, object> exceptionDictDirect)
                    {
                        exceptionDict = exceptionDictDirect;
                    }
                    
                    if (exceptionDict != null)
                    {
                        if (exceptionDict.TryGetValue("message", out var messageValue))
                        {
                            errorMessage = messageValue?.ToString() ?? "Unknown error";
                        }
                        if (exceptionDict.TryGetValue("stackTrace", out var stackValue))
                        {
                            stackTrace = stackValue?.ToString();
                        }
                    }
                }
                
                // Extract additional error details from additionalData
                string? detailedErrorMessage = null;
                if (logDict.TryGetValue("additionalData", out var errorAdditionalData))
                {
                    Dictionary<string, object>? errorAdditionalDataDict = null;
                    if (errorAdditionalData is JsonElement additionalJsonElement)
                    {
                        errorAdditionalDataDict = ParseJsonElementToDictionary(additionalJsonElement);
                    }
                    else if (errorAdditionalData is Dictionary<string, object> additionalDictDirect)
                    {
                        errorAdditionalDataDict = additionalDictDirect;
                    }
                    
                    // Use errorMessage from additionalData if available (more detailed)
                    if (errorAdditionalDataDict?.TryGetValue("errorMessage", out var additionalErrorMessage) == true)
                    {
                        errorMessage = additionalErrorMessage?.ToString() ?? errorMessage;
                    }
                    
                    // Extract detailedErrorMessage from additionalData if available
                    if (errorAdditionalDataDict?.TryGetValue("detailedErrorMessage", out var additionalDetailedErrorMessage) == true)
                    {
                        detailedErrorMessage = additionalDetailedErrorMessage?.ToString();
                    }
                }
                
                var errorInfo = new
                {
                    timestamp = logDict.TryGetValue("timestamp", out var ts) ? ts : null,
                    category = category,
                    entityType = GetEntityTypeFromLog(logDict),
                    errorMessage = errorMessage,
                    detailedErrorMessage = detailedErrorMessage, // Add the detailed error message
                    context = logDict.TryGetValue("context", out var ctx) ? ctx : null,
                    stackTrace = stackTrace,
                    requestPayloadBlobUrl = GetRequestPayloadBlobUrl(logDict),
                    responsePayloadBlobUrl = GetResponsePayloadBlobUrl(logDict),
                    entityId = GetEntityIdFromLog(logDict),
                    entityName = GetEntityNameFromLog(logDict),
                    sourceStoreId = GetSourceStoreIdFromLog(logDict),
                    destinationStoreId = GetDestinationStoreIdFromLog(logDict)
                };
                errors.Add(errorInfo);
            }
            
            // Deduplicate by entity - keep only the most recent error for each entity
            var deduplicatedErrors = errors
                .GroupBy(e => {
                    var errorDict = e as dynamic;
                    return errorDict?.entityId?.ToString() ?? errorDict?.entityName?.ToString() ?? "unknown";
                })
                .Select(g => g.OrderByDescending(e => {
                    var errorDict = e as dynamic;
                    return DateTime.TryParse(errorDict?.timestamp?.ToString(), out DateTime ts) ? ts : DateTime.MinValue;
                }).First())
                .Cast<object>()
                .ToList();
            
            // Apply pagination
            var finalCount = deduplicatedErrors.Count;
            errors = deduplicatedErrors.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var responseData = new
            {
                migrationId = migrationId,
                entityType = entityType,
                errors = errors,
                pagination = new
                {
                    page = currentPage,
                    pageSize = pageSize,
                    totalItems = finalCount,
                    totalPages = (int)Math.Ceiling((double)finalCount / pageSize)
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
            _logger.LogError(ex, "Error getting entity errors for migration {MigrationId}, entity {EntityType}", migrationId, entityType);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred", $"{migrationId}/{entityType}");
        }
    }

    /// <summary>
    /// Determines if a log entry is related to a specific entity type
    /// </summary>
    /// <param name="logDict">Log dictionary</param>
    /// <param name="entityType">Entity type to check for</param>
    /// <returns>True if the log is related to the entity type</returns>
    private bool IsLogEntityRelated(Dictionary<string, object> logDict, string entityType)
    {
        // Check entityType field at root level
        var logEntityType = logDict.TryGetValue("entityType", out var et) ? et?.ToString() : null;
        if (IsEntityTypeMatch(logEntityType, entityType))
        {
            return true;
        }
        
        // Check additionalData.entityType field (where entity type is often stored)
        if (logDict.TryGetValue("additionalData", out var additionalData) && 
            additionalData is Dictionary<string, object> additionalDataDict)
        {
            var additionalEntityType = additionalDataDict.TryGetValue("entityType", out var aet) ? aet?.ToString() : null;
            if (IsEntityTypeMatch(additionalEntityType, entityType))
            {
                return true;
            }
        }
        
        // Check context field
        if (logDict.ContainsKey("context") && 
            logDict["context"]?.ToString()?.Contains(entityType, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }
        
        // Check if any field contains the entity type
        foreach (var kvp in logDict)
        {
            if (kvp.Value?.ToString()?.Contains(entityType, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if two entity types match (handles singular/plural variations)
    /// </summary>
    /// <param name="logEntityType">Entity type from log</param>
    /// <param name="requestedEntityType">Requested entity type</param>
    /// <returns>True if they match</returns>
    private bool IsEntityTypeMatch(string? logEntityType, string requestedEntityType)
    {
        if (string.IsNullOrEmpty(logEntityType))
            return false;
            
        return logEntityType.Equals(requestedEntityType, StringComparison.OrdinalIgnoreCase) ||
               logEntityType.Equals($"{requestedEntityType}s", StringComparison.OrdinalIgnoreCase) ||
               logEntityType.Equals(requestedEntityType.TrimEnd('s'), StringComparison.OrdinalIgnoreCase) ||
               (requestedEntityType.Equals("category", StringComparison.OrdinalIgnoreCase) && logEntityType.Equals("categories", StringComparison.OrdinalIgnoreCase)) ||
               (requestedEntityType.Equals("categories", StringComparison.OrdinalIgnoreCase) && logEntityType.Equals("category", StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Gets the entity type from a log dictionary
    /// </summary>
    /// <param name="logDict">Log dictionary</param>
    /// <returns>Entity type or null if not found</returns>
    private string? GetEntityTypeFromLog(Dictionary<string, object> logDict)
    {
        // Check entityType field at root level
        var logEntityType = logDict.TryGetValue("entityType", out var et) ? et?.ToString() : null;
        if (!string.IsNullOrEmpty(logEntityType))
        {
            return logEntityType;
        }
        
        // Check additionalData.entityType field
        if (logDict.TryGetValue("additionalData", out var additionalData) && 
            additionalData is Dictionary<string, object> additionalDataDict)
        {
            var additionalEntityType = additionalDataDict.TryGetValue("entityType", out var aet) ? aet?.ToString() : null;
            if (!string.IsNullOrEmpty(additionalEntityType))
            {
                return additionalEntityType;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the request payload blob URL from a log dictionary
    /// </summary>
    /// <param name="logDict">Log dictionary</param>
    /// <returns>Request payload blob URL or null if not found</returns>
    private string? GetRequestPayloadBlobUrl(Dictionary<string, object> logDict)
    {
        // Check additionalData.requestPayloadBlobUrl field
        if (logDict.TryGetValue("additionalData", out var additionalData) && 
            additionalData is Dictionary<string, object> additionalDataDict)
        {
            var blobUrl = additionalDataDict.TryGetValue("requestPayloadBlobUrl", out var rpb) ? rpb?.ToString() : null;
            if (!string.IsNullOrEmpty(blobUrl))
            {
                return blobUrl;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the response payload blob URL from a log dictionary
    /// </summary>
    /// <param name="logDict">Log dictionary</param>
    /// <returns>Response payload blob URL or null if not found</returns>
    private string? GetResponsePayloadBlobUrl(Dictionary<string, object> logDict)
    {
        // Check additionalData.responsePayloadBlobUrl field
        if (logDict.TryGetValue("additionalData", out var additionalData) && 
            additionalData is Dictionary<string, object> additionalDataDict)
        {
            var blobUrl = additionalDataDict.TryGetValue("responsePayloadBlobUrl", out var rpb) ? rpb?.ToString() : null;
            if (!string.IsNullOrEmpty(blobUrl))
            {
                return blobUrl;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the entity ID from a log dictionary
    /// </summary>
    /// <param name="logDict">Log dictionary</param>
    /// <returns>Entity ID or null if not found</returns>
    private string? GetEntityIdFromLog(Dictionary<string, object> logDict)
    {
        // Check additionalData.entityId field
        if (logDict.TryGetValue("additionalData", out var additionalData) && 
            additionalData is Dictionary<string, object> additionalDataDict)
        {
            var entityId = additionalDataDict.TryGetValue("entityId", out var eid) ? eid?.ToString() : null;
            if (!string.IsNullOrEmpty(entityId))
            {
                return entityId;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the migration ID from a log dictionary
    /// </summary>
    /// <param name="logDict">Log dictionary</param>
    /// <returns>Migration ID or null if not found</returns>
    private string? GetMigrationIdFromLog(Dictionary<string, object> logDict)
    {
        // Check top-level migrationId field
        if (logDict.TryGetValue("migrationId", out var migrationId))
        {
            var migrationIdStr = migrationId?.ToString();
            if (!string.IsNullOrEmpty(migrationIdStr))
            {
                return migrationIdStr;
            }
        }
        
        // Check top-level MigrationId field (capitalized)
        if (logDict.TryGetValue("MigrationId", out var migrationIdCap))
        {
            var migrationIdStr = migrationIdCap?.ToString();
            if (!string.IsNullOrEmpty(migrationIdStr))
            {
                return migrationIdStr;
            }
        }
        
        // Check additionalData.migrationId field
        if (logDict.TryGetValue("additionalData", out var additionalData) && 
            additionalData is Dictionary<string, object> additionalDataDict)
        {
            var migrationIdFromAdditional = additionalDataDict.TryGetValue("migrationId", out var mid) ? mid?.ToString() : null;
            if (!string.IsNullOrEmpty(migrationIdFromAdditional))
            {
                return migrationIdFromAdditional;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the entity name from a log dictionary
    /// </summary>
    /// <param name="logDict">Log dictionary</param>
    /// <returns>Entity name or null if not found</returns>
    private string? GetEntityNameFromLog(Dictionary<string, object> logDict)
    {
        // Check additionalData.entityName field
        if (logDict.TryGetValue("additionalData", out var additionalData) && 
            additionalData is Dictionary<string, object> additionalDataDict)
        {
            var entityName = additionalDataDict.TryGetValue("entityName", out var en) ? en?.ToString() : null;
            if (!string.IsNullOrEmpty(entityName))
            {
                return entityName;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the source store ID from a log dictionary
    /// </summary>
    /// <param name="logDict">Log dictionary</param>
    /// <returns>Source store ID or null if not found</returns>
    private string? GetSourceStoreIdFromLog(Dictionary<string, object> logDict)
    {
        // Check additionalData.sourceStoreId field
        if (logDict.TryGetValue("additionalData", out var additionalData) && 
            additionalData is Dictionary<string, object> additionalDataDict)
        {
            var sourceStoreId = additionalDataDict.TryGetValue("sourceStoreId", out var ssi) ? ssi?.ToString() : null;
            if (!string.IsNullOrEmpty(sourceStoreId))
            {
                return sourceStoreId;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the destination store ID from a log dictionary
    /// </summary>
    /// <param name="logDict">Log dictionary</param>
    /// <returns>Destination store ID or null if not found</returns>
    private string? GetDestinationStoreIdFromLog(Dictionary<string, object> logDict)
    {
        // Check additionalData.destinationStoreId field
        if (logDict.TryGetValue("additionalData", out var additionalData) && 
            additionalData is Dictionary<string, object> additionalDataDict)
        {
            var destinationStoreId = additionalDataDict.TryGetValue("destinationStoreId", out var dsi) ? dsi?.ToString() : null;
            if (!string.IsNullOrEmpty(destinationStoreId))
            {
                return destinationStoreId;
            }
        }
        
        return null;
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

    /// <summary>
    /// Helper method to get detailed progress for a migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <returns>Detailed progress information</returns>
    private async Task<MigrationProgress> GetDetailedProgressAsync(string migrationId)
    {
        try
        {
            // Try to get from progress tracker first
            var progress = await _progressTracker.GetProgressAsync(migrationId, CancellationToken.None);
            
            // If progress tracker has meaningful data, use it
            if (progress != null && progress.TotalEntities > 0)
            {
                return progress;
            }
            
            // Otherwise, reconstruct from storage
            var migrationEntry = await _migrationStorageService.GetMigrationAsync(migrationId);
            if (migrationEntry != null)
            {
                // Get entity-level progress from storage
                var entityProgressEntries = await _migrationStorageService.GetEntityProgressAsync(migrationId);
                var entityProgress = new Dictionary<string, EntityProgress>();
                
                foreach (var entry in entityProgressEntries)
                {
                    entityProgress[entry.EntityType] = new EntityProgress
                    {
                        EntityType = entry.EntityType,
                        TotalCount = entry.TotalCount,
                        ProcessedCount = entry.ProcessedCount,
                        SuccessCount = entry.SuccessCount,
                        FailureCount = entry.FailureCount,
                        ProgressPercentage = entry.ProgressPercentage,
                        Status = entry.Status,
                        StartTime = entry.StartTime,
                        EndTime = entry.EndTime,
                        ProcessingTime = entry.ProcessingTime
                    };
                }
                
                return new MigrationProgress
                {
                    MigrationId = migrationId,
                    Status = migrationEntry.Status.ToString().ToLower(),
                    StartTime = migrationEntry.CreatedAt,
                    LastUpdated = migrationEntry.UpdatedAt,
                    OverallProgressPercentage = migrationEntry.ProgressPercentage,
                    TotalEntities = migrationEntry.TotalEntities,
                    ProcessedEntities = migrationEntry.ProcessedEntities,
                    SuccessfulEntities = migrationEntry.ProcessedEntities - migrationEntry.FailedEntities,
                    FailedEntities = migrationEntry.FailedEntities,
                    CurrentPhase = migrationEntry.CurrentPhase ?? "completed",
                    EntityProgress = entityProgress
                };
            }
            
            return new MigrationProgress
            {
                MigrationId = migrationId,
                Status = "unknown",
                StartTime = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                OverallProgressPercentage = 0,
                TotalEntities = 0,
                ProcessedEntities = 0,
                SuccessfulEntities = 0,
                FailedEntities = 0,
                CurrentPhase = "unknown",
                EntityProgress = new Dictionary<string, EntityProgress>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get detailed progress for migration {MigrationId}", migrationId);
            return new MigrationProgress
            {
                MigrationId = migrationId,
                Status = "unknown",
                StartTime = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                OverallProgressPercentage = 0,
                TotalEntities = 0,
                ProcessedEntities = 0,
                SuccessfulEntities = 0,
                FailedEntities = 0,
                CurrentPhase = "unknown",
                EntityProgress = new Dictionary<string, EntityProgress>()
            };
        }
    }

    /// <summary>
    /// Parses a JsonElement into a Dictionary<string, object>
    /// </summary>
    /// <param name="jsonElement">The JsonElement to parse</param>
    /// <returns>A Dictionary<string, object> representation of the JSON</returns>
    private Dictionary<string, object> ParseJsonElementToDictionary(JsonElement jsonElement)
    {
        var dictionary = new Dictionary<string, object>();
        foreach (var property in jsonElement.EnumerateObject())
        {
            var value = ParseJsonElement(property.Value);
            if (value != null)
            {
                dictionary[property.Name] = value;
            }
        }
        return dictionary;
    }

    /// <summary>
    /// Recursively parses JsonElement values into a Dictionary<string, object>
    /// </summary>
    /// <param name="jsonElement">The JsonElement to parse</param>
    /// <returns>A Dictionary<string, object> representation of the JSON</returns>
    private object? ParseJsonElement(JsonElement jsonElement)
    {
        switch (jsonElement.ValueKind)
        {
            case JsonValueKind.Object:
                return ParseJsonElementToDictionary(jsonElement);
            case JsonValueKind.Array:
                return jsonElement.EnumerateArray().Select(ParseJsonElement).ToList();
            case JsonValueKind.String:
                return jsonElement.GetString() ?? string.Empty;
            case JsonValueKind.Number:
                if (jsonElement.TryGetInt32(out int intValue)) return intValue;
                if (jsonElement.TryGetInt64(out long longValue)) return longValue;
                if (jsonElement.TryGetDouble(out double doubleValue)) return doubleValue;
                return jsonElement.GetDecimal();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                return jsonElement.ToString();
        }
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
