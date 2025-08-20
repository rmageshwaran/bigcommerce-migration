using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using System.Text.Json;
using System.Net;

namespace BigCommerce.Migration.Functions.Functions
{
    /// <summary>
    /// Testing functions for Phase 5 validation
    /// </summary>
    public class TestingFunctions
    {
        private readonly ILogger<TestingFunctions> _logger;
        private readonly ICentralizedProgressBroadcastService _centralizedBroadcastService;
        private readonly ISignalREventFactory _eventFactory;
        private readonly IProgressEventPublisher _progressEventPublisher;

        public TestingFunctions(
            ILogger<TestingFunctions> logger,
            ICentralizedProgressBroadcastService centralizedBroadcastService,
            ISignalREventFactory eventFactory,
            IProgressEventPublisher progressEventPublisher)
        {
            _logger = logger;
            _centralizedBroadcastService = centralizedBroadcastService;
            _eventFactory = eventFactory;
            _progressEventPublisher = progressEventPublisher;
        }

        /// <summary>
        /// Generate test SignalR events for Phase 5 validation
        /// </summary>
        [Function("GenerateTestEvents")]
        public async Task<HttpResponseData> GenerateTestEvents(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "test/events")] HttpRequestData req)
        {
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var testRequest = JsonSerializer.Deserialize<TestEventRequest>(requestBody);

                if (testRequest == null)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await errorResponse.WriteStringAsync("Invalid request body");
                    return errorResponse;
                }

                _logger.LogInformation("Generating test events for Phase 5 validation: {EventType}", testRequest.EventType);

                switch (testRequest.EventType.ToLower())
                {
                    case "migration-started":
                        await GenerateMigrationStartedEvent(testRequest.MigrationId);
                        break;

                    case "chunk-progress":
                        await GenerateChunkProgressEvent(testRequest.MigrationId, testRequest.ChunkNumber ?? 1);
                        break;

                    case "migration-completed":
                        await GenerateMigrationCompletedEvent(testRequest.MigrationId, testRequest.Status ?? "Completed");
                        break;

                    case "error":
                        await GenerateErrorEvent(testRequest.MigrationId, testRequest.ErrorMessage ?? "Test error");
                        break;

                    case "full-migration-flow":
                        await GenerateFullMigrationFlow(testRequest.MigrationId);
                        break;

                    default:
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        await badResponse.WriteStringAsync($"Unknown event type: {testRequest.EventType}");
                        return badResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                var result = new { 
                    success = true, 
                    message = $"Generated {testRequest.EventType} event successfully",
                    migrationId = testRequest.MigrationId
                };
                await response.WriteAsJsonAsync(result);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating test events");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Internal server error");
                return errorResponse;
            }
        }

        private async Task GenerateMigrationStartedEvent(string migrationId)
        {
            var entities = new List<EntityInfo>
            {
                new EntityInfo { EntityType = "products", TotalCount = 0 },
                new EntityInfo { EntityType = "categories", TotalCount = 0 },
                new EntityInfo { EntityType = "brands", TotalCount = 0 }
            };

            await _centralizedBroadcastService.BroadcastMigrationStartedAsync(
                migrationId, 
                "test-source-store", 
                "test-destination-store", 
                entities);
            _logger.LogInformation("Generated migration-started event for {MigrationId}", migrationId);
        }

        private async Task GenerateChunkProgressEvent(string migrationId, int chunkNumber)
        {
            var entityChunkProgress = new EntityChunkProgress
            {
                EntityType = "products",
                ChunkNumber = chunkNumber,
                ChunkSize = 20,
                ProcessedInChunk = 20,
                FailedInChunk = 0,
                CumulativeProcessed = chunkNumber * 20,
                CumulativeFailed = 0,
                TotalEntitiesForType = 100,
                ProgressPercentage = (chunkNumber * 20),
                Status = "processing",
                Message = $"Processing products chunk {chunkNumber} of 5",
                ProcessingTimeMs = 2000
            };

            await _centralizedBroadcastService.BroadcastEntityChunkProgressAsync(migrationId, entityChunkProgress);
            _logger.LogInformation("Generated chunk-progress event for {MigrationId}, chunk {ChunkNumber}", migrationId, chunkNumber);
        }

        private async Task GenerateMigrationCompletedEvent(string migrationId, string status)
        {
            var migrationCompletionInfo = new MigrationCompletionInfo
            {
                Status = status,
                Message = $"Migration {status.ToLower()} successfully",
                TotalProcessedEntities = 100,
                TotalFailedEntities = 0,
                DurationMs = 30000
            };

            await _centralizedBroadcastService.BroadcastMigrationCompletedAsync(migrationId, migrationCompletionInfo);
            _logger.LogInformation("Generated migration-completed event for {MigrationId} with status {Status}", migrationId, status);
        }

        private async Task GenerateErrorEvent(string migrationId, string errorMessage)
        {
            var errorInfo = new ErrorInfo
            {
                ErrorType = "TestError",
                ErrorMessage = errorMessage,
                EntityType = "products",
                EntityId = "test-product-123"
            };

            await _centralizedBroadcastService.BroadcastErrorAsync(migrationId, errorInfo);
            _logger.LogInformation("Generated error event for {MigrationId}: {ErrorMessage}", migrationId, errorMessage);
        }

        private async Task GenerateFullMigrationFlow(string migrationId)
        {
            _logger.LogInformation("Starting full migration flow simulation for {MigrationId}", migrationId);

            // 1. Migration Started
            await GenerateMigrationStartedEvent(migrationId);
            await Task.Delay(2000); // 2 second delay to test rate limiting

            // 2. Progress Updates (respecting rate limiting)
            for (int chunk = 1; chunk <= 5; chunk++)
            {
                await GenerateChunkProgressEvent(migrationId, chunk);
                await Task.Delay(2000); // 2 second delay to test rate limiting
            }

            // 3. Migration Completed
            await GenerateMigrationCompletedEvent(migrationId, "Completed");

            _logger.LogInformation("Completed full migration flow simulation for {MigrationId}", migrationId);
        }

        /// <summary>
        /// Get test event metrics for validation
        /// </summary>
        [Function("GetTestMetrics")]
        public async Task<HttpResponseData> GetTestMetrics(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "test/metrics")] HttpRequestData req)
        {
            try
            {
                var metrics = new
                {
                    timestamp = DateTime.UtcNow,
                    centralizedBroadcastingEnabled = true,
                    rateLimitingEnabled = true,
                    simplifiedEventTypes = new[] { "migration-started", "chunk-progress", "migration-completed", "error" },
                    phase4Implementation = "active",
                    testingCapabilities = new[] { "event-generation", "flow-simulation", "rate-limit-testing" }
                };

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(metrics);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting test metrics");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Internal server error");
                return errorResponse;
            }
        }
    }

    /// <summary>
    /// Request model for test event generation
    /// </summary>
    public class TestEventRequest
    {
        public string MigrationId { get; set; } = "test-migration-001";
        public string EventType { get; set; } = string.Empty;
        public int? ChunkNumber { get; set; }
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
    }
}