using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Functions.Base;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;

namespace BigCommerce.Migration.Functions.Functions;

/// <summary>
/// HTTP trigger functions for migration lifecycle management operations
/// Follows Single Responsibility Principle - handles only migration creation, cancellation, and lifecycle control
/// Now inherits from BaseFunction to eliminate duplicate code (Task 2.1.2)
/// </summary>
public class MigrationManagementFunctions : BaseFunction
{
    private readonly IQueueService _queueService;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly IOpenSearchService _openSearchService;

    /// <summary>
    /// Initializes a new instance of the MigrationManagementFunctions class
    /// </summary>
    /// <param name="logger">Logger instance for this specific function class</param>
    /// <param name="queueService">Queue service for starting migration processing</param>
    /// <param name="migrationStorageService">Migration storage service for state management</param>
    /// <param name="openSearchService">OpenSearch service for event logging</param>
    public MigrationManagementFunctions(
        ILogger<MigrationManagementFunctions> logger,
        IQueueService queueService,
        IMigrationStorageService migrationStorageService,
        IOpenSearchService openSearchService) : base(logger)
    {
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
    }

    /// <summary>
    /// Starts a new migration operation
    /// </summary>
    /// <param name="req">HTTP request containing migration parameters</param>
    /// <param name="context">Function execution context</param>
    /// <returns>HTTP response with migration ID and status</returns>
    [Function("StartMigrationManagement")]
    [OpenApiOperation(operationId: "StartMigrationManagement", tags: new[] { "Migration Management" },
        Summary = "Start a new migration (Management API)",
        Description = "Creates and queues a new migration between BigCommerce stores. Validates store credentials and queues the migration for processing.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(MigrationRequest),
        Description = "Migration configuration specifying source store, destination store, and entities to migrate")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Accepted, contentType: "application/json",
        bodyType: typeof(object), Description = "Migration created successfully and queued for processing")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json",
        bodyType: typeof(object), Description = "Invalid migration request or store configuration")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "application/json",
        bodyType: typeof(object), Description = "Internal server error")]
    public async Task<HttpResponseData> StartMigration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/migrations")] HttpRequestData req,
        FunctionContext context)
    {
        var migrationId = Guid.NewGuid().ToString();
        
        try
        {
            Logger.LogInformation("Starting migration request processing. MigrationId: {MigrationId}", migrationId);

            // Deserialize migration request using base class method
            MigrationRequest? migrationRequest;
            try
            {
                migrationRequest = await DeserializeRequestAsync<MigrationRequest>(req);
            }
            catch (ArgumentException ex)
            {
                Logger.LogWarning(ex, "Empty request body received. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Request body is required", migrationId);
            }
            catch (JsonException ex)
            {
                Logger.LogWarning(ex, "Invalid JSON in request body. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Invalid JSON format", migrationId);
            }

            // Validate migration request
            if (migrationRequest == null)
            {
                Logger.LogWarning("Failed to deserialize migration request. MigrationId: {MigrationId}", migrationId);
                return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Invalid migration request", migrationId);
            }

            // Basic validation
            var validationResult = BasicValidateMigrationRequest(migrationRequest);
            if (!validationResult.IsValid)
            {
                return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, validationResult.ErrorMessage, migrationId);
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
            Logger.LogInformation("Migration entry stored successfully. MigrationId: {MigrationId}", migrationId);

            // Send migration start message to queue for processing
            await _queueService.SendMigrationStartMessageAsync(migrationId, migrationRequest, null);
            Logger.LogInformation("Migration start message sent to queue for processing. MigrationId: {MigrationId}", migrationId);

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

            // Create response using BaseFunction method
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

            Logger.LogInformation("Migration request successfully prepared. MigrationId: {MigrationId}", migrationId);
            return await CreateSuccessResponseAsync(req, responseData, HttpStatusCode.Accepted);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error processing migration request. MigrationId: {MigrationId}", migrationId);
            return await CreateErrorResponseAsync(req, HttpStatusCode.InternalServerError, "Internal server error occurred", migrationId);
        }
    }

    // CreateErrorResponse method removed - now using BaseFunction.CreateErrorResponseAsync()

    /// <summary>
    /// Validates the migration request (basic validation only)
    /// </summary>
    private ValidationResult BasicValidateMigrationRequest(MigrationRequest request)
    {
        if (request.SourceStore == null)
            return new ValidationResult { IsValid = false, ErrorMessage = "Source store configuration is required" };

        if (request.DestinationStore == null)
            return new ValidationResult { IsValid = false, ErrorMessage = "Destination store configuration is required" };

        if (string.IsNullOrEmpty(request.SourceStore.StoreId))
            return new ValidationResult { IsValid = false, ErrorMessage = "Source store ID is required" };

        if (string.IsNullOrEmpty(request.DestinationStore.StoreId))
            return new ValidationResult { IsValid = false, ErrorMessage = "Destination store ID is required" };

        if (request.Entities == null || !request.Entities.Any())
            return new ValidationResult { IsValid = false, ErrorMessage = "At least one entity type must be specified" };

        return new ValidationResult { IsValid = true };
    }

    /// <summary>
    /// Estimates completion time based on entities
    /// </summary>
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
} 
