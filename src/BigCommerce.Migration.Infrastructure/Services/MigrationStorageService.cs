using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Azure Table Storage implementation for migration data management
/// </summary>
public class MigrationStorageService : IMigrationStorageService
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<MigrationStorageService> _logger;
    
    private const string MigrationsTableName = "migrations";
    private const string EntityMappingsTableName = "entitymappings";
    private const string ApiCallTrackingTableName = "apicalltracking";
    private const string CancellationTokensTableName = "cancellationtokens";
    private const string EntityProgressTableName = "entityprogress";

    /// <summary>
    /// Initializes a new instance of the MigrationStorageService class
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <param name="logger">Logger instance</param>
    public MigrationStorageService(IConfiguration configuration, ILogger<MigrationStorageService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
        
        // Try ConnectionStrings section first, then fall back to Values section (Azure Functions style)
        var connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
            ?? configuration["AzureWebJobsStorage"]
            ?? throw new ArgumentNullException("AzureWebJobsStorage connection string is required");
        
        _tableServiceClient = new TableServiceClient(connectionString);
    }

    #region Migration Configuration Operations

    /// <summary>
    /// Creates a new migration entry in Azure Table Storage
    /// </summary>
    /// <param name="migrationEntry">Migration entry to create</param>
    /// <returns>Created migration entry</returns>
    public async Task<MigrationEntry> CreateMigrationAsync(MigrationEntry migrationEntry)
    {
        try
        {
            _logger.LogInformation("Creating migration entry: {MigrationId}", migrationEntry.Id);

            var tableClient = await GetTableClientAsync(MigrationsTableName);
            var tableEntity = ConvertToTableEntity(migrationEntry);

            await tableClient.AddEntityAsync(tableEntity);
            
            _logger.LogInformation("Successfully created migration entry: {MigrationId}", migrationEntry.Id);
            return migrationEntry;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogWarning("Migration entry already exists: {MigrationId}", migrationEntry.Id);
            throw new InvalidOperationException($"Migration {migrationEntry.Id} already exists", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating migration entry: {MigrationId}", migrationEntry.Id);
            throw;
        }
    }

    /// <summary>
    /// Gets a migration entry by ID from Azure Table Storage
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Migration entry if found, null otherwise</returns>
    public async Task<MigrationEntry?> GetMigrationAsync(string migrationId)
    {
        try
        {
            _logger.LogInformation("Getting migration entry: {MigrationId}", migrationId);

            var tableClient = await GetTableClientAsync(MigrationsTableName);
            var response = await tableClient.GetEntityIfExistsAsync<TableEntity>("migration", migrationId);

            if (!response.HasValue)
            {
                _logger.LogWarning("Migration entry not found: {MigrationId}", migrationId);
                return null;
            }

            var migrationEntry = ConvertFromTableEntity(response.Value!);
            _logger.LogInformation("Successfully retrieved migration entry: {MigrationId}", migrationId);
            return migrationEntry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migration entry: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing migration entry in Azure Table Storage
    /// </summary>
    /// <param name="migrationEntry">Migration entry to update</param>
    /// <returns>Updated migration entry</returns>
    public async Task<MigrationEntry> UpdateMigrationAsync(MigrationEntry migrationEntry)
    {
        try
        {
            _logger.LogInformation("Updating migration entry: {MigrationId}", migrationEntry.Id);

            var tableClient = await GetTableClientAsync(MigrationsTableName);
            var tableEntity = ConvertToTableEntity(migrationEntry);

            await tableClient.UpdateEntityAsync(tableEntity, ETag.All);
            
            _logger.LogInformation("Successfully updated migration entry: {MigrationId}", migrationEntry.Id);
            return migrationEntry;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Migration entry not found for update: {MigrationId}", migrationEntry.Id);
            throw new InvalidOperationException($"Migration {migrationEntry.Id} not found", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating migration entry: {MigrationId}", migrationEntry.Id);
            throw;
        }
    }

    /// <summary>
    /// Gets a list of migrations with optional filtering and pagination
    /// </summary>
    /// <param name="request">Query request with filters and pagination parameters</param>
    /// <returns>Paginated list of migrations</returns>
    public async Task<MigrationListResult> GetMigrationsAsync(MigrationQueryRequest request)
    {
        try
        {
            _logger.LogInformation("Getting migrations list with filters");

            var tableClient = await GetTableClientAsync(MigrationsTableName);
            var filter = BuildMigrationQuery(request);

            var entities = new List<MigrationEntry>();
            await foreach (var entity in tableClient.QueryAsync<TableEntity>(filter))
            {
                entities.Add(ConvertFromTableEntity(entity));
            }

            // Apply sorting and pagination
            var sortedEntities = ApplySorting(entities, request.SortBy, request.SortDirection);
            var totalCount = sortedEntities.Count;
            var paginatedEntities = sortedEntities
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            var totalPages = (int)Math.Ceiling((double)totalCount / request.PageSize);

            return new MigrationListResult
            {
                Migrations = paginatedEntities,
                TotalCount = totalCount,
                CurrentPage = request.Page,
                PageSize = request.PageSize,
                TotalPages = totalPages,
                HasMorePages = request.Page < totalPages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migrations list");
            throw;
        }
    }

    /// <summary>
    /// Deletes a migration entry from Azure Table Storage
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>True if deleted successfully, false if not found</returns>
    public async Task<bool> DeleteMigrationAsync(string migrationId)
    {
        try
        {
            _logger.LogInformation("Deleting migration entry: {MigrationId}", migrationId);

            var tableClient = await GetTableClientAsync(MigrationsTableName);
            await tableClient.DeleteEntityAsync("migration", migrationId);
            
            _logger.LogInformation("Successfully deleted migration entry: {MigrationId}", migrationId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Migration entry not found for deletion: {MigrationId}", migrationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting migration entry: {MigrationId}", migrationId);
            throw;
        }
    }

    #endregion

    #region Entity Mapping Operations

    /// <summary>
    /// Creates a new entity mapping in Azure Table Storage
    /// </summary>
    /// <param name="mapping">Entity mapping to create</param>
    /// <returns>Created entity mapping</returns>
    public async Task<EntityMapping> CreateEntityMappingAsync(EntityMapping mapping)
    {
        try
        {
            _logger.LogInformation("Creating entity mapping: {MigrationId}, {EntityType}, {SourceId}", 
                mapping.MigrationId, mapping.EntityType, mapping.SourceId);

            var tableClient = await GetTableClientAsync(EntityMappingsTableName);
            var tableEntity = new TableEntity(mapping.MigrationId, $"{mapping.EntityType}_{mapping.SourceId}")
            {
                ["EntityType"] = mapping.EntityType,
                ["SourceId"] = mapping.SourceId,
                ["DestinationId"] = mapping.DestinationId,
                ["SourceStoreId"] = mapping.SourceStoreId,
                ["DestinationStoreId"] = mapping.DestinationStoreId,
                ["Status"] = mapping.Status,
                ["Metadata"] = mapping.Metadata,
                ["CreatedAt"] = mapping.CreatedAt,
                ["UpdatedAt"] = mapping.UpdatedAt
            };

            await tableClient.AddEntityAsync(tableEntity);
            
            _logger.LogInformation("Successfully created entity mapping: {MigrationId}", mapping.MigrationId);
            return mapping;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating entity mapping: {MigrationId}", mapping.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Gets an entity mapping by migration ID, entity type, and source ID
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Type of entity</param>
    /// <param name="sourceId">Source entity identifier</param>
    /// <returns>Entity mapping if found, null otherwise</returns>
    public async Task<EntityMapping?> GetEntityMappingAsync(string migrationId, string entityType, string sourceId)
    {
        try
        {
            _logger.LogInformation("Getting entity mapping: {MigrationId}, {EntityType}, {SourceId}", 
                migrationId, entityType, sourceId);

            var tableClient = await GetTableClientAsync(EntityMappingsTableName);
            var rowKey = $"{entityType}_{sourceId}";
            var response = await tableClient.GetEntityIfExistsAsync<TableEntity>(migrationId, rowKey);

            if (!response.HasValue)
            {
                _logger.LogWarning("Entity mapping not found: {MigrationId}, {EntityType}, {SourceId}", 
                    migrationId, entityType, sourceId);
                return null;
            }

            var entity = response.Value!;
            return new EntityMapping
            {
                MigrationId = entity.PartitionKey!,
                EntityType = entity.GetString("EntityType") ?? string.Empty,
                SourceId = entity.GetString("SourceId") ?? string.Empty,
                DestinationId = entity.GetString("DestinationId") ?? string.Empty,
                SourceStoreId = entity.GetString("SourceStoreId") ?? string.Empty,
                DestinationStoreId = entity.GetString("DestinationStoreId") ?? string.Empty,
                Status = entity.GetString("Status") ?? string.Empty,
                Metadata = entity.GetString("Metadata"),
                CreatedAt = entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
                UpdatedAt = entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting entity mapping: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Gets all entity mappings for a migration, optionally filtered by entity type
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Optional entity type filter</param>
    /// <returns>List of entity mappings</returns>
    public async Task<List<EntityMapping>> GetEntityMappingsAsync(string migrationId, string? entityType = null)
    {
        try
        {
            _logger.LogInformation("Getting entity mappings for migration: {MigrationId}, EntityType: {EntityType}", 
                migrationId, entityType);

            var tableClient = await GetTableClientAsync(EntityMappingsTableName);
            var filter = $"PartitionKey eq '{migrationId}'";
            
            if (!string.IsNullOrEmpty(entityType))
            {
                filter += $" and EntityType eq '{entityType}'";
            }

            var mappings = new List<EntityMapping>();
            await foreach (var entity in tableClient.QueryAsync<TableEntity>(filter))
            {
                mappings.Add(new EntityMapping
                {
                    MigrationId = entity.PartitionKey!,
                    EntityType = entity.GetString("EntityType") ?? string.Empty,
                    SourceId = entity.GetString("SourceId") ?? string.Empty,
                    DestinationId = entity.GetString("DestinationId") ?? string.Empty,
                    SourceStoreId = entity.GetString("SourceStoreId") ?? string.Empty,
                    DestinationStoreId = entity.GetString("DestinationStoreId") ?? string.Empty,
                    Status = entity.GetString("Status") ?? string.Empty,
                    Metadata = entity.GetString("Metadata"),
                    CreatedAt = entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
                    UpdatedAt = entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow
                });
            }

            return mappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting entity mappings: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Creates multiple entity mappings in batches for better performance
    /// </summary>
    /// <param name="mappings">List of entity mappings to create</param>
    /// <returns>List of created entity mappings</returns>
    public async Task<List<EntityMapping>> CreateEntityMappingsBatchAsync(List<EntityMapping> mappings)
    {
        try
        {
            _logger.LogInformation("Creating entity mappings batch: {Count} mappings", mappings.Count);

            var tableClient = await GetTableClientAsync(EntityMappingsTableName);
            var createdMappings = new List<EntityMapping>();

            // Process in batches (Azure Tables supports up to 100 operations per batch)
            var batchSize = 100;
            for (int i = 0; i < mappings.Count; i += batchSize)
            {
                var batch = mappings.Skip(i).Take(batchSize).ToList();
                var transaction = new List<TableTransactionAction>();

                foreach (var mapping in batch)
                {
                    var tableEntity = new TableEntity(mapping.MigrationId, $"{mapping.EntityType}_{mapping.SourceId}")
                    {
                        ["EntityType"] = mapping.EntityType,
                        ["SourceId"] = mapping.SourceId,
                        ["DestinationId"] = mapping.DestinationId,
                        ["SourceStoreId"] = mapping.SourceStoreId,
                        ["DestinationStoreId"] = mapping.DestinationStoreId,
                        ["Status"] = mapping.Status,
                        ["Metadata"] = mapping.Metadata,
                        ["CreatedAt"] = mapping.CreatedAt,
                        ["UpdatedAt"] = mapping.UpdatedAt
                    };

                    transaction.Add(new TableTransactionAction(TableTransactionActionType.Add, tableEntity));
                }

                try
                {
                    await tableClient.SubmitTransactionAsync(transaction);
                    createdMappings.AddRange(batch);
                }
                catch (TableTransactionFailedException ex) when (ex.ErrorCode == "EntityAlreadyExists")
                {
                    // This is expected behavior in our dual storage approach:
                    // 1. Individual storage succeeds immediately after entity creation  
                    // 2. Batch storage at end fails with EntityAlreadyExists (gracefully handled here)
                    _logger.LogDebug("Entity mappings already exist for batch (expected from dual storage approach). Batch size: {BatchSize}, Error: {ErrorCode}", 
                        batch.Count, ex.ErrorCode);
                    
                    // Still count these as "created" since they exist from individual storage
                    createdMappings.AddRange(batch);
                }
                catch (RequestFailedException ex) when (ex.Status == 409)
                {
                    // Handle general 409 Conflict errors (backup case)
                    _logger.LogDebug("Conflict detected during entity mapping batch creation (expected from dual storage). Batch size: {BatchSize}", batch.Count);
                    createdMappings.AddRange(batch);
                }
            }

            _logger.LogInformation("Successfully processed {Count} entity mappings batch (includes existing from dual storage)", createdMappings.Count);
            return createdMappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating entity mappings batch");
            throw;
        }
    }

    /// <summary>
    /// Updates an existing entity mapping in Azure Table Storage
    /// </summary>
    /// <param name="mapping">Entity mapping to update</param>
    /// <returns>Updated entity mapping</returns>
    public async Task<EntityMapping> UpdateEntityMappingAsync(EntityMapping mapping)
    {
        try
        {
            _logger.LogInformation("Updating entity mapping: {MigrationId}, {EntityType}, {SourceId}", 
                mapping.MigrationId, mapping.EntityType, mapping.SourceId);

            var tableClient = await GetTableClientAsync(EntityMappingsTableName);
            var rowKey = $"{mapping.EntityType}_{mapping.SourceId}";
            var tableEntity = new TableEntity(mapping.MigrationId, rowKey)
            {
                ["EntityType"] = mapping.EntityType,
                ["SourceId"] = mapping.SourceId,
                ["DestinationId"] = mapping.DestinationId,
                ["SourceStoreId"] = mapping.SourceStoreId,
                ["DestinationStoreId"] = mapping.DestinationStoreId,
                ["Status"] = mapping.Status,
                ["Metadata"] = mapping.Metadata,
                ["CreatedAt"] = mapping.CreatedAt,
                ["UpdatedAt"] = mapping.UpdatedAt
            };

            await tableClient.UpdateEntityAsync(tableEntity, ETag.All);
            
            _logger.LogInformation("Successfully updated entity mapping: {MigrationId}", mapping.MigrationId);
            return mapping;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating entity mapping: {MigrationId}", mapping.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Stores multiple entity mappings in batch (alias for CreateEntityMappingsBatchAsync)
    /// </summary>
    /// <param name="mappings">List of entity mappings to store</param>
    /// <returns>List of stored mappings</returns>
    public async Task<List<EntityMapping>> StoreEntityMappingsAsync(List<EntityMapping> mappings)
    {
        return await CreateEntityMappingsBatchAsync(mappings);
    }

    #endregion

    #region API Call Tracking Operations

    /// <summary>
    /// Creates a new API call tracking entry in Azure Table Storage
    /// </summary>
    /// <param name="apiCall">API call tracking data to create</param>
    /// <returns>Created API call tracking entry</returns>
    public async Task<ApiCallTracking> CreateApiCallTrackingAsync(ApiCallTracking apiCall)
    {
        try
        {
            _logger.LogInformation("Creating API call tracking: {MigrationId}", apiCall.MigrationId);

            var tableClient = await GetTableClientAsync(ApiCallTrackingTableName);
            var tableEntity = new TableEntity(apiCall.MigrationId, Guid.NewGuid().ToString())
            {
                ["StoreId"] = apiCall.StoreId,
                ["Endpoint"] = apiCall.Endpoint,
                ["Method"] = apiCall.Method,
                ["StatusCode"] = apiCall.StatusCode,
                ["ResponseTimeMs"] = apiCall.ResponseTimeMs,
                ["RequestTimestamp"] = apiCall.RequestTimestamp,
                ["ResponseTimestamp"] = apiCall.ResponseTimestamp,
                ["ErrorMessage"] = apiCall.ErrorMessage,
                ["IsSuccessful"] = apiCall.IsSuccessful
            };

            await tableClient.AddEntityAsync(tableEntity);
            
            _logger.LogInformation("Successfully created API call tracking: {MigrationId}", apiCall.MigrationId);
            return apiCall;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating API call tracking: {MigrationId}", apiCall.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Gets API call statistics for a store within a time window
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="timeWindow">Time window for statistics calculation</param>
    /// <returns>API call statistics</returns>
    public async Task<ApiCallStatistics> GetApiCallStatisticsAsync(string storeId, TimeSpan timeWindow)
    {
        try
        {
            _logger.LogInformation("Getting API call statistics for store: {StoreId}", storeId);

            var tableClient = await GetTableClientAsync(ApiCallTrackingTableName);
            var cutoffTime = DateTime.UtcNow - timeWindow;
            var filter = $"StoreId eq '{storeId}' and RequestTimestamp ge datetime'{cutoffTime:yyyy-MM-ddTHH:mm:ssZ}'";

            var callCount = 0;
            var totalResponseTime = 0L;
            var successfulCalls = 0;
            var failedCalls = 0;

            await foreach (var entity in tableClient.QueryAsync<TableEntity>(filter))
            {
                callCount++;
                totalResponseTime += entity.GetInt64("ResponseTimeMs") ?? 0;
                
                var statusCode = entity.GetInt32("StatusCode") ?? 0;
                if (statusCode >= 200 && statusCode < 300)
                {
                    successfulCalls++;
                }
                else if (statusCode >= 400)
                {
                    failedCalls++;
                }
            }

            return new ApiCallStatistics
            {
                StoreId = storeId,
                TimeWindow = timeWindow,
                TotalCalls = callCount,
                SuccessfulCalls = successfulCalls,
                FailedCalls = failedCalls,
                AverageResponseTimeMs = callCount > 0 ? (double)totalResponseTime / callCount : 0,
                CalculatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API call statistics for store: {StoreId}", storeId);
            throw;
        }
    }

    /// <summary>
    /// Gets API call history for a migration with optional filtering and pagination
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="request">Query request with filters and pagination parameters</param>
    /// <returns>Paginated list of API call tracking entries</returns>
    public async Task<ApiCallListResult> GetApiCallHistoryAsync(string migrationId, ApiCallQueryRequest request)
    {
        try
        {
            _logger.LogInformation("Getting API call history for migration: {MigrationId}", migrationId);

            var tableClient = await GetTableClientAsync(ApiCallTrackingTableName);
            var filter = $"PartitionKey eq '{migrationId}'";

            // Note: EntityType filter not available in ApiCallQueryRequest model

            if (request.RequestedAfter.HasValue)
            {
                filter += $" and RequestTimestamp ge datetime'{request.RequestedAfter.Value:yyyy-MM-ddTHH:mm:ssZ}'";
            }

            if (request.RequestedBefore.HasValue)
            {
                filter += $" and RequestTimestamp le datetime'{request.RequestedBefore.Value:yyyy-MM-ddTHH:mm:ssZ}'";
            }

            var apiCalls = new List<ApiCallTracking>();
            await foreach (var entity in tableClient.QueryAsync<TableEntity>(filter))
            {
                apiCalls.Add(new ApiCallTracking
                {
                    MigrationId = entity.PartitionKey,
                    StoreId = entity.GetString("StoreId") ?? string.Empty,
                    Endpoint = entity.GetString("Endpoint") ?? string.Empty,
                    Method = entity.GetString("Method") ?? string.Empty,
                    StatusCode = entity.GetInt32("StatusCode") ?? 0,
                    ResponseTimeMs = entity.GetInt32("ResponseTimeMs") ?? 0,
                    RequestTimestamp = entity.GetDateTime("RequestTimestamp") ?? DateTime.UtcNow,
                    ResponseTimestamp = entity.GetDateTime("ResponseTimestamp") ?? DateTime.UtcNow,
                    ErrorMessage = entity.GetString("ErrorMessage"),
                    IsSuccessful = entity.GetBoolean("IsSuccessful") ?? false
                });
            }

            var totalCount = apiCalls.Count;
            var paginatedCalls = apiCalls
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            var totalPages = (int)Math.Ceiling((double)totalCount / request.PageSize);

            return new ApiCallListResult
            {
                ApiCalls = paginatedCalls,
                TotalCount = totalCount,
                CurrentPage = request.Page,
                PageSize = request.PageSize,
                TotalPages = totalPages,
                HasMorePages = request.Page < totalPages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API call history for migration: {MigrationId}", migrationId);
            throw;
        }
    }

    #endregion

    #region Cancellation Token Operations

    /// <summary>
    /// Creates a new cancellation token for a migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="reason">Reason for cancellation</param>
    /// <returns>Created cancellation token entry</returns>
    public async Task<CancellationTokenEntry> CreateCancellationTokenAsync(string migrationId, string reason)
    {
        try
        {
            _logger.LogInformation("Creating cancellation token: {MigrationId}", migrationId);

            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = reason,
                RequestedAt = DateTime.UtcNow,
                IsProcessed = false,
                Status = "Pending"
            };

            var tableClient = await GetTableClientAsync(CancellationTokensTableName);
            var tableEntity = new TableEntity("cancellation", migrationId)
            {
                ["MigrationId"] = cancellationToken.MigrationId,
                ["Reason"] = cancellationToken.Reason,
                ["RequestedAt"] = cancellationToken.RequestedAt,
                ["IsProcessed"] = cancellationToken.IsProcessed,
                ["ProcessedAt"] = cancellationToken.ProcessedAt,
                ["Status"] = cancellationToken.Status
            };

            await tableClient.UpsertEntityAsync(tableEntity);
            
            _logger.LogInformation("Successfully created cancellation token: {MigrationId}", migrationId);
            return cancellationToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating cancellation token: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Gets a cancellation token for a migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Cancellation token entry if found, null otherwise</returns>
    public async Task<CancellationTokenEntry?> GetCancellationTokenAsync(string migrationId)
    {
        try
        {
            _logger.LogDebug("Checking for cancellation token: {MigrationId}", migrationId);

            var tableClient = await GetTableClientAsync(CancellationTokensTableName);
            var response = await tableClient.GetEntityIfExistsAsync<TableEntity>("cancellation", migrationId);

            if (!response.HasValue)
            {
                _logger.LogDebug("No cancellation token found for migration {MigrationId} - migration is not cancelled", migrationId);
                return null;
            }

            var entity = response.Value!;
            return new CancellationTokenEntry
            {
                MigrationId = entity.GetString("MigrationId") ?? string.Empty,
                Reason = entity.GetString("Reason") ?? string.Empty,
                RequestedAt = entity.GetDateTime("RequestedAt") ?? DateTime.UtcNow,
                IsProcessed = entity.GetBoolean("IsProcessed") ?? false,
                ProcessedAt = entity.GetDateTime("ProcessedAt"),
                Status = entity.GetString("Status") ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cancellation token: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing cancellation token in Azure Table Storage
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to update</param>
    /// <returns>Updated cancellation token entry</returns>
    public async Task<CancellationTokenEntry> UpdateCancellationTokenAsync(CancellationTokenEntry cancellationToken)
    {
        try
        {
            _logger.LogInformation("Updating cancellation token: {MigrationId}", cancellationToken.MigrationId);

            var tableClient = await GetTableClientAsync(CancellationTokensTableName);
            var tableEntity = new TableEntity("cancellation", cancellationToken.MigrationId)
            {
                ["MigrationId"] = cancellationToken.MigrationId,
                ["Reason"] = cancellationToken.Reason,
                ["RequestedAt"] = cancellationToken.RequestedAt,
                ["IsProcessed"] = cancellationToken.IsProcessed,
                ["ProcessedAt"] = cancellationToken.ProcessedAt,
                ["Status"] = cancellationToken.Status
            };

            await tableClient.UpdateEntityAsync(tableEntity, ETag.All);
            
            _logger.LogInformation("Successfully updated cancellation token: {MigrationId}", cancellationToken.MigrationId);
            return cancellationToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating cancellation token: {MigrationId}", cancellationToken.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Deletes a cancellation token from Azure Table Storage
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>True if deleted successfully, false if not found</returns>
    public async Task<bool> DeleteCancellationTokenAsync(string migrationId)
    {
        try
        {
            _logger.LogInformation("Deleting cancellation token: {MigrationId}", migrationId);

            var tableClient = await GetTableClientAsync(CancellationTokensTableName);
            await tableClient.DeleteEntityAsync("cancellation", migrationId);
            
            _logger.LogInformation("Successfully deleted cancellation token: {MigrationId}", migrationId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Cancellation token not found for deletion: {MigrationId}", migrationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting cancellation token: {MigrationId}", migrationId);
            throw;
        }
    }

    #endregion

    #region Entity Progress Operations

    /// <summary>
    /// Creates or updates an entity progress entry in Azure Table Storage
    /// </summary>
    /// <param name="progressEntry">Entity progress entry to create or update</param>
    /// <returns>Created or updated progress entry</returns>
    public async Task<EntityProgressEntry> CreateOrUpdateEntityProgressAsync(EntityProgressEntry progressEntry)
    {
        try
        {
            _logger.LogInformation("Creating/updating entity progress: {MigrationId}, {EntityType}", 
                progressEntry.MigrationId, progressEntry.EntityType);

            var tableClient = await GetTableClientAsync(EntityProgressTableName);
            var tableEntity = new TableEntity(progressEntry.MigrationId, progressEntry.EntityType)
            {
                ["EntityType"] = progressEntry.EntityType,
                ["TotalCount"] = progressEntry.TotalCount,
                ["ProcessedCount"] = progressEntry.ProcessedCount,
                ["SuccessCount"] = progressEntry.SuccessCount,
                ["FailureCount"] = progressEntry.FailureCount,
                ["ProgressPercentage"] = progressEntry.ProgressPercentage,
                ["Status"] = progressEntry.Status,
                ["StartTime"] = progressEntry.StartTime,
                ["EndTime"] = progressEntry.EndTime,
                ["ProcessingTime"] = progressEntry.ProcessingTime.TotalMilliseconds,
                ["CreatedAt"] = progressEntry.CreatedAt,
                ["UpdatedAt"] = progressEntry.UpdatedAt
            };

            await tableClient.UpsertEntityAsync(tableEntity);
            
            _logger.LogInformation("Successfully created/updated entity progress: {MigrationId}, {EntityType}", 
                progressEntry.MigrationId, progressEntry.EntityType);
            return progressEntry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/updating entity progress: {MigrationId}, {EntityType}", 
                progressEntry.MigrationId, progressEntry.EntityType);
            throw;
        }
    }

    /// <summary>
    /// Gets entity progress entries for a migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Optional entity type filter</param>
    /// <returns>List of entity progress entries</returns>
    public async Task<List<EntityProgressEntry>> GetEntityProgressAsync(string migrationId, string? entityType = null)
    {
        try
        {
            _logger.LogInformation("Getting entity progress for migration: {MigrationId}, EntityType: {EntityType}", 
                migrationId, entityType);

            var tableClient = await GetTableClientAsync(EntityProgressTableName);
            var filter = $"PartitionKey eq '{migrationId}'";
            
            if (!string.IsNullOrEmpty(entityType))
            {
                filter += $" and EntityType eq '{entityType}'";
            }

            var progressEntries = new List<EntityProgressEntry>();
            await foreach (var entity in tableClient.QueryAsync<TableEntity>(filter))
            {
                progressEntries.Add(new EntityProgressEntry
                {
                    MigrationId = entity.PartitionKey!,
                    EntityType = entity.GetString("EntityType") ?? string.Empty,
                    TotalCount = entity.GetInt32("TotalCount") ?? 0,
                    ProcessedCount = entity.GetInt32("ProcessedCount") ?? 0,
                    SuccessCount = entity.GetInt32("SuccessCount") ?? 0,
                    FailureCount = entity.GetInt32("FailureCount") ?? 0,
                    ProgressPercentage = entity.GetDouble("ProgressPercentage") ?? 0.0,
                    Status = entity.GetString("Status") ?? string.Empty,
                    StartTime = entity.GetDateTime("StartTime") ?? DateTime.UtcNow,
                    EndTime = entity.GetDateTime("EndTime"),
                    ProcessingTime = TimeSpan.FromMilliseconds(entity.GetDouble("ProcessingTime") ?? 0),
                    CreatedAt = entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
                    UpdatedAt = entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow
                });
            }

            return progressEntries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting entity progress: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Gets a specific entity progress entry
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type</param>
    /// <returns>Entity progress entry or null if not found</returns>
    public async Task<EntityProgressEntry?> GetEntityProgressByTypeAsync(string migrationId, string entityType)
    {
        try
        {
            _logger.LogInformation("Getting specific entity progress: {MigrationId}, {EntityType}", 
                migrationId, entityType);

            var tableClient = await GetTableClientAsync(EntityProgressTableName);
            var response = await tableClient.GetEntityIfExistsAsync<TableEntity>(migrationId, entityType);

            if (!response.HasValue)
            {
                _logger.LogWarning("Entity progress not found: {MigrationId}, {EntityType}", 
                    migrationId, entityType);
                return null;
            }

            var entity = response.Value!;
            return new EntityProgressEntry
            {
                MigrationId = entity.PartitionKey!,
                EntityType = entity.GetString("EntityType") ?? string.Empty,
                TotalCount = entity.GetInt32("TotalCount") ?? 0,
                ProcessedCount = entity.GetInt32("ProcessedCount") ?? 0,
                SuccessCount = entity.GetInt32("SuccessCount") ?? 0,
                FailureCount = entity.GetInt32("FailureCount") ?? 0,
                ProgressPercentage = entity.GetDouble("ProgressPercentage") ?? 0.0,
                Status = entity.GetString("Status") ?? string.Empty,
                StartTime = entity.GetDateTime("StartTime") ?? DateTime.UtcNow,
                EndTime = entity.GetDateTime("EndTime"),
                ProcessingTime = TimeSpan.FromMilliseconds(entity.GetDouble("ProcessingTime") ?? 0),
                CreatedAt = entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
                UpdatedAt = entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting specific entity progress: {MigrationId}, {EntityType}", 
                migrationId, entityType);
            throw;
        }
    }

    /// <summary>
    /// Deletes entity progress entries for a migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Optional entity type filter</param>
    /// <returns>True if deleted, false if not found</returns>
    public async Task<bool> DeleteEntityProgressAsync(string migrationId, string? entityType = null)
    {
        try
        {
            _logger.LogInformation("Deleting entity progress: {MigrationId}, EntityType: {EntityType}", 
                migrationId, entityType);

            var tableClient = await GetTableClientAsync(EntityProgressTableName);
            
            if (!string.IsNullOrEmpty(entityType))
            {
                // Delete specific entity type
                await tableClient.DeleteEntityAsync(migrationId, entityType);
                _logger.LogInformation("Successfully deleted entity progress: {MigrationId}, {EntityType}", 
                    migrationId, entityType);
                return true;
            }
            else
            {
                // Delete all entity types for the migration
                var filter = $"PartitionKey eq '{migrationId}'";
                var deletedCount = 0;
                
                await foreach (var entity in tableClient.QueryAsync<TableEntity>(filter))
                {
                    await tableClient.DeleteEntityAsync(entity.PartitionKey!, entity.RowKey!);
                    deletedCount++;
                }
                
                _logger.LogInformation("Successfully deleted {DeletedCount} entity progress entries for migration: {MigrationId}", 
                    deletedCount, migrationId);
                return deletedCount > 0;
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Entity progress not found for deletion: {MigrationId}, EntityType: {EntityType}", 
                migrationId, entityType);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting entity progress: {MigrationId}", migrationId);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private async Task<TableClient> GetTableClientAsync(string tableName)
    {
        var tableClient = _tableServiceClient.GetTableClient(tableName);
        await tableClient.CreateIfNotExistsAsync();
        return tableClient;
    }

    private static TableEntity ConvertToTableEntity(MigrationEntry migration)
    {
        return new TableEntity("migration", migration.Id)
        {
            ["SourceStoreId"] = migration.SourceStoreId,
            ["DestinationStoreId"] = migration.DestinationStoreId,
            ["SourceChannelId"] = migration.SourceChannelId,
            ["DestinationChannelId"] = migration.DestinationChannelId,
            ["Entities"] = JsonSerializer.Serialize(migration.Entities),
            ["Status"] = migration.Status.ToString(),
            ["CreatedAt"] = migration.CreatedAt,
            ["UpdatedAt"] = migration.UpdatedAt,
            ["ProgressPercentage"] = migration.ProgressPercentage,
            ["CurrentPhase"] = migration.CurrentPhase,
            ["ErrorMessage"] = migration.ErrorMessage,
            ["TotalEntities"] = migration.TotalEntities,
            ["ProcessedEntities"] = migration.ProcessedEntities,
            ["FailedEntities"] = migration.FailedEntities
        };
    }

    private static MigrationEntry ConvertFromTableEntity(TableEntity entity)
    {
        var entitiesJson = entity.GetString("Entities") ?? "[]";
        var entities = JsonSerializer.Deserialize<List<string>>(entitiesJson) ?? new List<string>();
        
        Enum.TryParse<MigrationStatus>(entity.GetString("Status"), out var status);

        return new MigrationEntry
        {
            Id = entity.RowKey,
            SourceStoreId = entity.GetString("SourceStoreId") ?? string.Empty,
            DestinationStoreId = entity.GetString("DestinationStoreId") ?? string.Empty,
            SourceChannelId = entity.GetString("SourceChannelId") ?? string.Empty,
            DestinationChannelId = entity.GetString("DestinationChannelId") ?? string.Empty,
            Entities = entities,
            Status = status,
            CreatedAt = entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
            UpdatedAt = entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow,
            ProgressPercentage = entity.GetInt32("ProgressPercentage") ?? 0,
            CurrentPhase = entity.GetString("CurrentPhase"),
            ErrorMessage = entity.GetString("ErrorMessage"),
            TotalEntities = entity.GetInt32("TotalEntities") ?? 0,
            ProcessedEntities = entity.GetInt32("ProcessedEntities") ?? 0,
            FailedEntities = entity.GetInt32("FailedEntities") ?? 0
        };
    }

    private static string BuildMigrationQuery(MigrationQueryRequest request)
    {
        var filter = "PartitionKey eq 'migration'";

        if (!string.IsNullOrEmpty(request.Status))
        {
            filter += $" and Status eq '{request.Status}'";
        }

        if (!string.IsNullOrEmpty(request.MigrationId))
        {
            filter += $" and RowKey eq '{request.MigrationId}'";
        }

        // Handle store filtering - if both source and destination are the same, use OR logic
        if (!string.IsNullOrEmpty(request.SourceStoreId) && !string.IsNullOrEmpty(request.DestinationStoreId))
        {
            if (request.SourceStoreId == request.DestinationStoreId)
            {
                // Same store - find migrations where this store appears as either source or destination
                filter += $" and (SourceStoreId eq '{request.SourceStoreId}' or DestinationStoreId eq '{request.DestinationStoreId}')";
            }
            else
            {
                // Different stores - find migrations with this specific source-destination pair
                filter += $" and SourceStoreId eq '{request.SourceStoreId}' and DestinationStoreId eq '{request.DestinationStoreId}'";
            }
        }
        else if (!string.IsNullOrEmpty(request.SourceStoreId))
        {
            filter += $" and SourceStoreId eq '{request.SourceStoreId}'";
        }
        else if (!string.IsNullOrEmpty(request.DestinationStoreId))
        {
            filter += $" and DestinationStoreId eq '{request.DestinationStoreId}'";
        }

        if (request.CreatedAfter.HasValue)
        {
            filter += $" and CreatedAt ge datetime'{request.CreatedAfter.Value:yyyy-MM-ddTHH:mm:ssZ}'";
        }

        if (request.CreatedBefore.HasValue)
        {
            filter += $" and CreatedAt le datetime'{request.CreatedBefore.Value:yyyy-MM-ddTHH:mm:ssZ}'";
        }

        return filter;
    }

    private static List<MigrationEntry> ApplySorting(List<MigrationEntry> migrations, string sortBy, string sortDirection)
    {
        var isDescending = sortDirection?.ToLower() == "desc";

        return sortBy?.ToLower() switch
        {
            "createdat" => isDescending 
                ? migrations.OrderByDescending(m => m.CreatedAt).ToList()
                : migrations.OrderBy(m => m.CreatedAt).ToList(),
            "updatedat" => isDescending 
                ? migrations.OrderByDescending(m => m.UpdatedAt).ToList()
                : migrations.OrderBy(m => m.UpdatedAt).ToList(),
            "status" => isDescending 
                ? migrations.OrderByDescending(m => m.Status).ToList()
                : migrations.OrderBy(m => m.Status).ToList(),
            "progress" => isDescending 
                ? migrations.OrderByDescending(m => m.ProgressPercentage).ToList()
                : migrations.OrderBy(m => m.ProgressPercentage).ToList(),
            _ => migrations.OrderByDescending(m => m.CreatedAt).ToList()
        };
    }

    #endregion
} 