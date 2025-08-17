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
/// HTTP trigger functions for migration query operations
/// Follows Single Responsibility Principle - handles only migration retrieval and status queries
/// </summary>
public class MigrationQueryFunctions
{
    private readonly ILogger<MigrationQueryFunctions> _logger;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly IProgressTracker _progressTracker;

    /// <summary>
    /// Initializes a new instance of the MigrationQueryFunctions class
    /// </summary>
    /// <param name="logger">Logger instance for this specific function class</param>
    /// <param name="migrationStorageService">Migration storage service for data retrieval</param>
    /// <param name="progressTracker">Progress tracker for detailed migration progress</param>
    public MigrationQueryFunctions(
        ILogger<MigrationQueryFunctions> logger,
        IMigrationStorageService migrationStorageService,
        IProgressTracker progressTracker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));
    }

    /// <summary>
    /// Gets the status of a specific migration
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with migration status and progress</returns>
    [Function("GetMigrationStatus")]
    [OpenApiOperation(operationId: "GetMigrationStatus", tags: new[] { "Migration Queries" },
        Summary = "Get migration status",
        Description = "Retrieves the current status and detailed progress information for a specific migration.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration status retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration not found")]
    public async Task<HttpResponseData> GetMigrationStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "migrations/{migrationId}/status")] HttpRequestData req,
        FunctionContext context)
    {
        var migrationId = req.Url.Segments.LastOrDefault(s => s != "/")?.Trim('/') ?? string.Empty;
        
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

            var actualStatus = migrationEntry.Status.ToString().ToLower();

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
    [Function("QueryMigrations")]
    [OpenApiOperation(operationId: "QueryMigrations", tags: new[] { "Migration Queries" },
        Summary = "Query migrations list",
        Description = "Retrieves a list of migrations with optional filtering by status, store, and date range.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Migrations list retrieved successfully")]
    public async Task<HttpResponseData> GetMigrations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "query/migrations")] HttpRequestData req,
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
            var queryRequest = new MigrationQueryRequest
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
    /// Gets detailed information about a specific migration
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with detailed migration information</returns>
    [Function("GetMigration")]
    [OpenApiOperation(operationId: "GetMigration", tags: new[] { "Migration Queries" },
        Summary = "Get migration details",
        Description = "Retrieves detailed information about a specific migration including progress and configuration.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration details retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration not found")]
    public async Task<HttpResponseData> GetMigration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "migrations/{migrationId}/details")] HttpRequestData req,
        FunctionContext context)
    {
        var migrationId = req.Url.Segments.LastOrDefault(s => s != "/")?.Trim('/') ?? string.Empty;
        
        try
        {
            _logger.LogInformation("Getting migration details. MigrationId: {MigrationId}", migrationId);

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
            var detailedProgress = await GetDetailedProgressAsync(migrationId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var responseData = new
            {
                migrationId = migrationEntry.Id,
                status = migrationEntry.Status.ToString().ToLower(),
                sourceStore = new
                {
                    storeId = migrationEntry.SourceStoreId,
                    channelId = migrationEntry.SourceChannelId
                },
                destinationStore = new
                {
                    storeId = migrationEntry.DestinationStoreId,
                    channelId = migrationEntry.DestinationChannelId
                },
                entities = migrationEntry.Entities,
                createdAt = migrationEntry.CreatedAt,
                updatedAt = migrationEntry.UpdatedAt,
                progress = detailedProgress,
                message = "Migration details retrieved successfully"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migration details. MigrationId: {MigrationId}", migrationId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred", migrationId);
        }
    }

    /// <summary>
    /// Helper method to get detailed progress for a migration
    /// </summary>
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
                        SkippedCount = entry.SkippedCount, // 🚨 FIX: Include SkippedCount from storage
                        CancelledCount = entry.CancelledCount, // 🚨 CANCELLATION FIX: Include CancelledCount from storage
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
                    SuccessfulEntities = migrationEntry.ProcessedEntities - migrationEntry.FailedEntities - migrationEntry.SkippedEntities - migrationEntry.CancelledEntities,
                    FailedEntities = migrationEntry.FailedEntities,
                    SkippedEntities = migrationEntry.SkippedEntities, // 🚨 FIX: Include SkippedEntities in migration summary
                    CancelledEntities = migrationEntry.CancelledEntities, // 🚨 CANCELLATION FIX: Include CancelledEntities in migration summary
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
                Status = "error",
                StartTime = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                OverallProgressPercentage = 0,
                TotalEntities = 0,
                ProcessedEntities = 0,
                SuccessfulEntities = 0,
                FailedEntities = 0,
                CurrentPhase = "error",
                EntityProgress = new Dictionary<string, EntityProgress>()
            };
        }
    }

    /// <summary>
    /// Helper method to create error responses
    /// </summary>
    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string message, string migrationId)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        
        var errorResponse = new
        {
            error = message,
            migrationId = migrationId,
            timestamp = DateTime.UtcNow
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        return response;
    }

    /// <summary>
    /// Extracts a query parameter value from the query string
    /// </summary>
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
} 