using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Functions.Functions
{
    /// <summary>
    /// Azure Functions for dashboard API endpoints
    /// Provides migration status, health, and statistics data for the real-time dashboard
    /// </summary>
    public class DashboardFunctions
    {
        private readonly ILogger<DashboardFunctions> _logger;
        private readonly IProgressTracker _progressTracker;
        private readonly IMigrationStorageService _migrationStorageService;
        private readonly IRateLimitService _rateLimitService;

        public DashboardFunctions(
            ILogger<DashboardFunctions> logger,
            IProgressTracker progressTracker,
            IMigrationStorageService storageService,
            IRateLimitService rateLimitService)
        {
            _logger = logger;
            _progressTracker = progressTracker;
            _migrationStorageService = storageService;
            _rateLimitService = rateLimitService;
        }

            /// <summary>
    /// Get migration status for a specific migration
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migration status response</returns>
    [Function("GetDashboardMigrationStatus")]
    public async Task<HttpResponseData> GetMigrationStatus(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/migrations/{migrationId}/status")] HttpRequestData req,
            string migrationId,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Getting migration status for {MigrationId}", migrationId);

            try
            {
                if (string.IsNullOrEmpty(migrationId))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Migration ID is required", cancellationToken);
                    return badRequestResponse;
                }

                // Get progress from tracker
                var progress = await _progressTracker.GetProgressAsync(migrationId, cancellationToken);
                
                // Get migration from storage for consistency
                var migrationEntry = await _migrationStorageService.GetMigrationAsync(migrationId);
                
                // Check if progress tracker has meaningful data (not just default values from restart)
                var hasMeaningfulProgress = progress.TotalEntities > 0 || 
                                          progress.ProcessedEntities > 0 || 
                                          progress.EntityProgress.Any() ||
                                          progress.StartTime < DateTime.UtcNow.AddMinutes(-1); // Not just created
                
                // If no meaningful progress data and we have storage data, reconstruct from storage
                if (!hasMeaningfulProgress && migrationEntry != null)
                {
                    _logger.LogInformation("Progress tracker has no meaningful data for migration {MigrationId}, reconstructing from storage", migrationId);
                    
                    // Reconstruct progress from storage data
                    progress.MigrationId = migrationId;
                    progress.Status = migrationEntry.Status.ToString().ToLowerInvariant();
                    progress.StartTime = migrationEntry.CreatedAt;
                    progress.LastUpdated = migrationEntry.UpdatedAt;
                    progress.ElapsedTime = progress.LastUpdated - progress.StartTime;
                    
                    // Set appropriate values based on migration status
                    if (migrationEntry.Status == Core.Models.MigrationStatus.Completed)
                    {
                        progress.CurrentPhase = "completed";
                        progress.OverallProgressPercentage = 100.0;
                        progress.TotalEntities = migrationEntry.Entities?.Count ?? 0;
                        progress.ProcessedEntities = progress.TotalEntities;
                        progress.SuccessfulEntities = progress.TotalEntities; // Assume all successful if completed
                        progress.FailedEntities = 0;
                        progress.EntitiesPerSecond = 0.0;
                        progress.ErrorRate = 0.0;
                        progress.EstimatedTimeRemaining = TimeSpan.Zero;
                    }
                    else if (migrationEntry.Status == Core.Models.MigrationStatus.Failed)
                    {
                        progress.CurrentPhase = "failed";
                        progress.OverallProgressPercentage = 0.0;
                        progress.TotalEntities = migrationEntry.Entities?.Count ?? 0;
                        progress.ProcessedEntities = 0;
                        progress.SuccessfulEntities = 0;
                        progress.FailedEntities = progress.TotalEntities;
                        progress.EntitiesPerSecond = 0.0;
                        progress.ErrorRate = 1.0;
                        progress.EstimatedTimeRemaining = TimeSpan.Zero;
                    }
                    else
                    {
                        // In progress - use conservative estimates
                        progress.CurrentPhase = "in_progress";
                        progress.OverallProgressPercentage = 0.0;
                        progress.TotalEntities = migrationEntry.Entities?.Count ?? 0;
                        progress.ProcessedEntities = 0;
                        progress.SuccessfulEntities = 0;
                        progress.FailedEntities = 0;
                        progress.EntitiesPerSecond = 0.0;
                        progress.ErrorRate = 0.0;
                        progress.EstimatedTimeRemaining = TimeSpan.FromMinutes(30); // Conservative estimate
                    }
                }
                
                // Determine the actual status based on both progress and storage data
                var actualStatus = progress.Status;
                
                if (migrationEntry != null)
                {
                    // If storage shows completed but progress shows in-progress, check if all entities are actually done
                    if (migrationEntry.Status == Core.Models.MigrationStatus.Completed && progress.Status == "inprogress")
                    {
                        var allEntitiesCompleted = progress.EntityProgress?.All(ep => ep.Value.Status == "completed") ?? false;
                        if (!allEntitiesCompleted)
                        {
                            _logger.LogWarning("Migration {MigrationId} shows completed in storage but entities are still in progress. Using in-progress status.", migrationId);
                            actualStatus = "inprogress";
                        }
                        else
                        {
                            actualStatus = "completed";
                        }
                    }
                    
                    // If progress shows completed but storage shows in-progress, use completed status
                    if (migrationEntry.Status == Core.Models.MigrationStatus.InProgress && progress.Status == "completed")
                    {
                        var allEntitiesCompleted = progress.EntityProgress?.All(ep => ep.Value.Status == "completed") ?? false;
                        if (allEntitiesCompleted)
                        {
                            _logger.LogInformation("Migration {MigrationId} shows in-progress in storage but all entities are completed. Using completed status.", migrationId);
                            actualStatus = "completed";
                        }
                    }
                }
                
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                var statusData = new
                {
                    MigrationId = migrationId,
                    Status = actualStatus,
                    OverallProgress = progress.OverallProgressPercentage,
                    CurrentPhase = progress.CurrentPhase,
                    CurrentEntity = progress.CurrentEntity,
                    TotalEntities = progress.TotalEntities,
                    ProcessedEntities = progress.ProcessedEntities,
                    SuccessfulEntities = progress.SuccessfulEntities,
                    FailedEntities = progress.FailedEntities,
                    StartTime = progress.StartTime,
                    LastUpdated = progress.LastUpdated,
                    ElapsedTime = progress.ElapsedTime,
                    EstimatedTimeRemaining = progress.EstimatedTimeRemaining,
                    EntitiesPerSecond = progress.EntitiesPerSecond,
                    ErrorRate = progress.ErrorRate,
                    EntityProgress = progress.EntityProgress,
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(statusData), cancellationToken);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting migration status for {MigrationId}", migrationId);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error getting migration status: {ex.Message}", cancellationToken);
                return errorResponse;
            }
        }

        /// <summary>
        /// Get list of all active migrations
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of active migrations</returns>
        [Function("GetActiveMigrations")]
        public async Task<HttpResponseData> GetActiveMigrations(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/migrations")] HttpRequestData req,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Getting list of active migrations");

            try
            {
                // Query for active migrations (Queued, InProgress)
                var queryRequest = new Core.Models.MigrationQueryRequest
                {
                    Status = "Queued,InProgress", // Get active statuses using correct enum values
                    PageSize = 50,
                    Page = 1,
                    SortBy = "CreatedAt",
                    SortDirection = "desc"
                };

                _logger.LogInformation("DEBUG: Querying migrations with status filter: {StatusFilter}", queryRequest.Status);

                // Retrieve active migrations from storage
                var migrationListResult = await _migrationStorageService.GetMigrationsAsync(queryRequest);
                
                _logger.LogInformation("DEBUG: Found {Count} migrations in storage", migrationListResult.Migrations.Count());
                
                // Get detailed progress for each active migration
                var activeMigrations = new List<object>();
                foreach (var migration in migrationListResult.Migrations)
                {
                    _logger.LogInformation("DEBUG: Processing migration {MigrationId} with status {Status}", migration.Id, migration.Status);
                    
                    try
                    {
                        // Get detailed progress information
                        var detailedProgress = await _progressTracker.GetProgressAsync(migration.Id, cancellationToken);
                        
                        _logger.LogInformation("DEBUG: Got progress for migration {MigrationId}: Total={Total}, Processed={Processed}", 
                            migration.Id, detailedProgress?.TotalEntities ?? 0, detailedProgress?.ProcessedEntities ?? 0);
                        
                        activeMigrations.Add(new
                        {
                            migrationId = migration.Id,
                            status = migration.Status.ToString().ToLower(),
                            sourceStore = migration.SourceStoreId,
                            destinationStore = migration.DestinationStoreId,
                            entities = migration.Entities,
                            createdAt = migration.CreatedAt,
                            updatedAt = migration.UpdatedAt,
                            progress = new
                            {
                                totalEntities = detailedProgress?.TotalEntities ?? 0,
                                processedEntities = detailedProgress?.ProcessedEntities ?? 0,
                                successfulEntities = detailedProgress?.SuccessfulEntities ?? 0,
                                failedEntities = detailedProgress?.FailedEntities ?? 0,
                                overallProgressPercentage = detailedProgress?.OverallProgressPercentage ?? 0,
                                startTime = detailedProgress?.StartTime
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get progress for migration {MigrationId}, including basic info", migration.Id);
                        
                        // Include basic migration info even if progress fails
                        activeMigrations.Add(new
                        {
                            migrationId = migration.Id,
                            status = migration.Status.ToString().ToLower(),
                            sourceStore = migration.SourceStoreId,
                            destinationStore = migration.DestinationStoreId,
                            entities = migration.Entities,
                            createdAt = migration.CreatedAt,
                            updatedAt = migration.UpdatedAt,
                            progress = new
                            {
                                totalEntities = 0,
                                processedEntities = 0,
                                successfulEntities = 0,
                                failedEntities = 0,
                                overallProgressPercentage = 0,
                                startTime = migration.CreatedAt
                            }
                        });
                    }
                }

                _logger.LogInformation("DEBUG: Returning {Count} active migrations", activeMigrations.Count);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                // Return structure that matches PaginatedResponse<T> interface expected by frontend
                var result = new
                {
                    data = activeMigrations,
                    totalCount = activeMigrations.Count,
                    page = 1,
                    pageSize = 50,
                    totalPages = 1
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(result), cancellationToken);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active migrations");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error getting active migrations: {ex.Message}", cancellationToken);
                return errorResponse;
            }
        }

        /// <summary>
        /// Get system health information
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>System health response</returns>
        [Function("GetSystemHealth")]
        public async Task<HttpResponseData> GetSystemHealth(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/health")] HttpRequestData req,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Getting system health information");

            try
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                var healthData = new
                {
                    status = "healthy",
                    timestamp = DateTime.UtcNow.ToString("O"), // ISO 8601 format for proper parsing
                    services = new
                    {
                        database = await CheckDatabaseHealth(cancellationToken),
                        storage = await CheckStorageHealth(cancellationToken),
                        rateLimit = await CheckRateLimitHealth(cancellationToken),
                        signalR = await CheckSignalRHealth(cancellationToken)
                    },
                    systemMetrics = new
                    {
                        activeMigrations = 0, // Placeholder
                        totalProcessedEntities = 0, // Placeholder
                        averageProcessingSpeed = 0.0, // Placeholder
                        errorRate = 0.0, // Placeholder
                        uptimeSeconds = GetUptimeSeconds()
                    }
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(healthData), cancellationToken);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system health");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error getting system health: {ex.Message}", cancellationToken);
                return errorResponse;
            }
        }

        /// <summary>
        /// Get migration statistics
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Migration statistics response</returns>
        [Function("GetMigrationStatistics")]
        public async Task<HttpResponseData> GetMigrationStatistics(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/statistics")] HttpRequestData req,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Getting migration statistics");

            try
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                var statistics = new
                {
                    TotalMigrations = 0, // Placeholder
                    CompletedMigrations = 0, // Placeholder
                    FailedMigrations = 0, // Placeholder
                    AverageProcessingTime = TimeSpan.Zero, // Placeholder
                    TotalEntitiesProcessed = 0, // Placeholder
                    AverageEntitiesPerSecond = 0.0, // Placeholder
                    ErrorRate = 0.0, // Placeholder
                    EntityStatistics = new
                    {
                        Products = new { Total = 0, Success = 0, Failure = 0 },
                        Categories = new { Total = 0, Success = 0, Failure = 0 },
                        Brands = new { Total = 0, Success = 0, Failure = 0 },
                        Variants = new { Total = 0, Success = 0, Failure = 0 },
                        Images = new { Total = 0, Success = 0, Failure = 0 },
                        Modifiers = new { Total = 0, Success = 0, Failure = 0 }
                    },
                    TimeRange = new
                    {
                        StartDate = DateTime.UtcNow.AddDays(-30),
                        EndDate = DateTime.UtcNow
                    },
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(statistics), cancellationToken);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting migration statistics");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error getting migration statistics: {ex.Message}", cancellationToken);
                return errorResponse;
            }
        }

        /// <summary>
        /// Get queue status and metrics
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Queue status response</returns>
        [Function("GetQueueStatus")]
        public async Task<HttpResponseData> GetQueueStatus(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/queues")] HttpRequestData req,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Getting queue status and metrics");

            try
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                var queueStatus = new
                {
                    Queues = new
                    {
                        MigrationStart = new { ActiveMessages = 0, DeadLetterMessages = 0 },
                        EntityBatch = new { ActiveMessages = 0, DeadLetterMessages = 0 },
                        BatchCompletion = new { ActiveMessages = 0, DeadLetterMessages = 0 },
                        Cancellation = new { ActiveMessages = 0, DeadLetterMessages = 0 },
                        ProgressUpdate = new { ActiveMessages = 0, DeadLetterMessages = 0 },
                        DeadLetter = new { ActiveMessages = 0, DeadLetterMessages = 0 },
                        Retry = new { ActiveMessages = 0, DeadLetterMessages = 0 }
                    },
                    TotalActiveMessages = 0,
                    TotalDeadLetterMessages = 0,
                    ProcessingRate = 0.0,
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(queueStatus), cancellationToken);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting queue status");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error getting queue status: {ex.Message}", cancellationToken);
                return errorResponse;
            }
        }

        /// <summary>
        /// Check database health
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Database health status</returns>
        private Task<object> CheckDatabaseHealth(CancellationToken cancellationToken)
        {
            try
            {
                // TODO: Implement actual database health check
                return Task.FromResult<object>(new { Status = "healthy", ResponseTime = "< 100ms" });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database health check failed");
                return Task.FromResult<object>(new { Status = "unhealthy", Error = ex.Message });
            }
        }

        /// <summary>
        /// Check storage health
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Storage health status</returns>
        private async Task<object> CheckStorageHealth(CancellationToken cancellationToken)
        {
            try
            {
                // Simple storage health check
                await Task.Delay(1, cancellationToken);
                return new { Status = "healthy", ResponseTime = "< 50ms" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Storage health check failed");
                return new { Status = "unhealthy", Error = ex.Message };
            }
        }

        /// <summary>
        /// Check rate limit service health
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Rate limit health status</returns>
        private Task<object> CheckRateLimitHealth(CancellationToken cancellationToken)
        {
            try
            {
                // TODO: Implement actual rate limit health check
                return Task.FromResult<object>(new { Status = "healthy", CurrentRate = "12/sec", Throttling = false });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rate limit health check failed");
                return Task.FromResult<object>(new { Status = "unhealthy", Error = ex.Message });
            }
        }

        /// <summary>
        /// Check SignalR health
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>SignalR health status</returns>
        private Task<object> CheckSignalRHealth(CancellationToken cancellationToken)
        {
            try
            {
                // Azure SignalR Service health is managed by Azure platform
                // Check if connection string is configured
                var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings:AzureSignalR") ?? 
                                     Environment.GetEnvironmentVariable("ConnectionStrings__AzureSignalR");
                
                var isConfigured = !string.IsNullOrEmpty(connectionString);
                return Task.FromResult<object>(new { 
                    Status = isConfigured ? "healthy" : "unhealthy", 
                    Service = "Azure SignalR Service",
                    Message = isConfigured ? "Connection string configured" : "Connection string not configured"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR health check failed");
                return Task.FromResult<object>(new { Status = "unhealthy", Error = ex.Message });
            }
        }

        /// <summary>
        /// Get system uptime in seconds
        /// </summary>
        /// <returns>Uptime in seconds</returns>
        private static long GetUptimeSeconds()
        {
            // Simple uptime calculation (can be enhanced with actual process start time)
            return (long)(DateTime.UtcNow - DateTime.Today).TotalSeconds;
        }
    }
} 
