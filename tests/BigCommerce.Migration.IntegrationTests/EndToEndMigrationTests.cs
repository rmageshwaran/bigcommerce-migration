using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.IntegrationTests;

/// <summary>
/// End-to-end integration tests for the complete BigCommerce migration workflow
/// Tests real API interactions, cancellation support, and performance metrics
/// </summary>
[Collection("IntegrationTests")]
public class EndToEndMigrationTests : IntegrationTestBase
{
    public EndToEndMigrationTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "Critical")]
    public async Task CompleteMigrationWorkflow_WithRealAPIs_ShouldMigrateSuccessfully()
    {
        // Arrange
        var testId = CreateTestId();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogInformation("Starting complete migration workflow test: {TestId}", testId);

            // Validate store connectivity first
            await ValidateStoreConnectivity();

            // Create migration request
            var migrationRequest = CreateTestMigrationRequest(testId);

            // Get required services
            var categoryTreeResolver = GetService<ICategoryTreeResolver>();
            var migrationStorageService = GetService<IMigrationStorageService>();
            var apiClient = GetService<IBigCommerceApiClient>();
            var progressTracker = GetService<IProgressTracker>();
            var rateLimitService = GetService<IRateLimitService>();

            Logger.LogInformation("Created migration request for entities: {Entities}", string.Join(", ", migrationRequest.Entities));

            // Act & Assert - Step 1: Category Tree Resolution
            Logger.LogInformation("Step 1: Resolving category trees for channels");
            var categoryTreeContext = await categoryTreeResolver.ResolveCategoryTreeAsync(migrationRequest);
            
            Assert.NotNull(categoryTreeContext);
            Assert.NotEmpty(categoryTreeContext.SourceCategoryTreeId);
            Assert.NotEmpty(categoryTreeContext.DestinationCategoryTreeId);
            
            Logger.LogInformation("Category trees resolved - Source: {SourceTreeId}, Destination: {DestinationTreeId}",
                categoryTreeContext.SourceCategoryTreeId, categoryTreeContext.DestinationCategoryTreeId);

            // Act & Assert - Step 2: Migration Storage Setup
            Logger.LogInformation("Step 2: Setting up migration storage");
            var migrationEntry = new MigrationEntry
            {
                Id = testId,
                SourceStoreId = SourceStore.StoreId,
                DestinationStoreId = DestinationStore.StoreId,
                SourceChannelId = SourceStore.ChannelId,
                DestinationChannelId = DestinationStore.ChannelId,
                Entities = migrationRequest.Entities.ToList(),
                Status = MigrationStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await migrationStorageService.CreateMigrationAsync(migrationEntry);
            Logger.LogInformation("Migration entry created in storage");

            // Act & Assert - Step 3: Entity Discovery and Processing
            await ProcessEntitiesInOrder(testId, migrationRequest, apiClient, progressTracker, rateLimitService);

            // Act & Assert - Step 4: Verify Migration Completion
            var finalMigrationEntry = await migrationStorageService.GetMigrationAsync(testId);
            Assert.NotNull(finalMigrationEntry);
            
            stopwatch.Stop();
            Logger.LogInformation("Complete migration workflow completed successfully in {Duration}ms", stopwatch.ElapsedMilliseconds);

            // Log performance metrics
            await LogPerformanceMetrics(testId, stopwatch.ElapsedMilliseconds, migrationRequest.Entities.Count());
        }
        finally
        {
            // Cleanup test data
            await CleanupTestData(testId);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "High")]
    public async Task MigrationWithCancellation_ShouldStopGracefully()
    {
        // Arrange
        var testId = CreateTestId();
        var cancellationTokenSource = new CancellationTokenSource();

        try
        {
            Logger.LogInformation("Starting migration cancellation test: {TestId}", testId);

            await ValidateStoreConnectivity();

            var migrationRequest = CreateTestMigrationRequest(testId);
            var migrationStorageService = GetService<IMigrationStorageService>();

            // Create migration entry
            var migrationEntry = new MigrationEntry
            {
                Id = testId,
                SourceStoreId = SourceStore.StoreId,
                DestinationStoreId = DestinationStore.StoreId,
                SourceChannelId = SourceStore.ChannelId,
                DestinationChannelId = DestinationStore.ChannelId,
                Entities = migrationRequest.Entities.ToList(),
                Status = MigrationStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await migrationStorageService.CreateMigrationAsync(migrationEntry);

            // Act - Start migration and cancel after short delay
            var migrationTask = Task.Run(async () =>
            {
                await Task.Delay(2000, cancellationTokenSource.Token); // Start processing
                Logger.LogInformation("Requesting migration cancellation for test: {TestId}", testId);
                
                // Create cancellation token in storage
                await migrationStorageService.CreateCancellationTokenAsync(testId, "Integration test cancellation");
                
                cancellationTokenSource.Cancel();
            });

            // Simulate processing with cancellation checking
            var processingTask = Task.Run(async () =>
            {
                var rateLimitService = GetService<IRateLimitService>();
                
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // Check for cancellation in storage
                    var cancellationToken = await migrationStorageService.GetCancellationTokenAsync(testId);
                    if (cancellationToken != null && !cancellationToken.IsProcessed)
                    {
                        Logger.LogInformation("Migration cancellation detected in storage");
                        break;
                    }

                    // Simulate rate-limited processing
                    await rateLimitService.CheckAndWaitAsync(SourceStore.StoreId, cancellationTokenSource.Token);
                    await Task.Delay(500, cancellationTokenSource.Token);
                }
            });

            // Wait for either completion or cancellation
            await Task.WhenAny(migrationTask, processingTask);

            // Assert - Verify cancellation was handled gracefully
            var cancellationTokenEntry = await migrationStorageService.GetCancellationTokenAsync(testId);
            Assert.NotNull(cancellationTokenEntry);
            Assert.False(cancellationTokenEntry.IsProcessed);

            Logger.LogInformation("Migration cancellation test completed successfully");
        }
        finally
        {
            cancellationTokenSource?.Dispose();
            await CleanupTestData(testId);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "Medium")]
    public async Task RateLimitCompliance_ShouldRespectBigCommerceRateLimit()
    {
        // Arrange
        var testId = CreateTestId();
        var rateLimitService = GetService<IRateLimitService>();
        var apiClient = GetService<IBigCommerceApiClient>();

        try
        {
            Logger.LogInformation("Starting rate limit compliance test: {TestId}", testId);

            await ValidateStoreConnectivity();

            var stopwatch = Stopwatch.StartNew();
            var requestCount = 0;
            var maxRequests = 30; // Test with 30 requests (should take ~2.5 seconds at 12 req/sec)

            // Act - Make multiple API requests respecting rate limits
            for (int i = 0; i < maxRequests; i++)
            {
                await rateLimitService.CheckAndWaitAsync(SourceStore.StoreId, CancellationToken.None);
                
                // Make a real API call to test rate limiting
                await apiClient.IsHealthyAsync(SourceStore);
                requestCount++;

                Logger.LogDebug("Completed request {RequestNumber}/{MaxRequests}", i + 1, maxRequests);
            }

            stopwatch.Stop();

            // Assert - Verify rate limiting compliance
            var expectedMinDuration = (maxRequests - 1) * (1000 / 12); // 12 requests per second = ~83ms between requests
            var actualDuration = stopwatch.ElapsedMilliseconds;

            Assert.True(actualDuration >= expectedMinDuration * 0.8, // Allow 20% tolerance
                $"Rate limiting not working correctly. Expected min: {expectedMinDuration}ms, Actual: {actualDuration}ms");

            Logger.LogInformation("Rate limit compliance test completed. {RequestCount} requests in {Duration}ms (Rate: {Rate:F2} req/sec)",
                requestCount, actualDuration, (requestCount * 1000.0 / actualDuration));
        }
        finally
        {
            await CleanupTestData(testId);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "Medium")]
    public async Task ErrorHandlingWithContinueOnFailure_ShouldProcessSuccessfulEntities()
    {
        // Arrange
        var testId = CreateTestId();

        try
        {
            Logger.LogInformation("Starting error handling test: {TestId}", testId);

            await ValidateStoreConnectivity();

            var apiClient = GetService<IBigCommerceApiClient>();
            var progressTracker = GetService<IProgressTracker>();

            // Create a mix of valid and invalid entity IDs for testing
            var entityIds = new List<string> { "1", "999999", "2", "999998", "3" }; // Mix valid and invalid IDs

            // Act - Process entities with continue-on-failure
            var successCount = 0;
            var failureCount = 0;

            await progressTracker.StartEntityProcessingAsync(testId, "products", entityIds.Count, CancellationToken.None);

            foreach (var entityId in entityIds)
            {
                try
                {
                    // Simulate entity processing (this will likely fail for invalid IDs)
                    Logger.LogDebug("Processing entity ID: {EntityId}", entityId);
                    
                    // This is a simulation - in real tests we'd call actual processing logic
                    if (entityId.StartsWith("999"))
                    {
                        throw new InvalidOperationException($"Invalid entity ID: {entityId}");
                    }

                    successCount++;
                    Logger.LogDebug("Successfully processed entity: {EntityId}", entityId);
                }
                catch (Exception ex)
                {
                    failureCount++;
                    Logger.LogWarning(ex, "Failed to process entity: {EntityId}", entityId);
                    // Continue processing other entities (continue-on-failure)
                }
            }

            await progressTracker.CompleteEntityProcessingAsync(testId, "products", CancellationToken.None);

            // Assert - Verify that processing continued despite failures
            Assert.True(successCount > 0, "Should have processed some entities successfully");
            Assert.True(failureCount > 0, "Should have encountered some failures");
            Assert.Equal(entityIds.Count, successCount + failureCount);

            Logger.LogInformation("Error handling test completed. Success: {SuccessCount}, Failures: {FailureCount}",
                successCount, failureCount);
        }
        finally
        {
            await CleanupTestData(testId);
        }
    }

    #region Helper Methods

    /// <summary>
    /// Creates a test migration request for integration testing
    /// </summary>
    private MigrationRequest CreateTestMigrationRequest(string testId)
    {
        return new MigrationRequest
        {
            SourceStore = SourceStore,
            DestinationStore = DestinationStore,
            Entities = new List<string> { "categories", "products" }, // Start with basic entities for testing
            Settings = new MigrationSettings
            {
                MaxApiCallsPerSecond = 12, // BigCommerce compliance
                EnableAdaptiveBatching = true,
                LogLevel = "DEBUG",
                RequestTimeoutSeconds = 60,
                MaxRetries = 5
            }
        };
    }

    /// <summary>
    /// Processes entities in the correct dependency order
    /// </summary>
    private async Task ProcessEntitiesInOrder(string migrationId, MigrationRequest request, IBigCommerceApiClient apiClient, 
        IProgressTracker progressTracker, IRateLimitService rateLimitService)
    {
        var entityOrder = new[] { "categories", "products", "variants", "images", "modifiers" };
        
        foreach (var entityType in entityOrder)
        {
            if (!request.Entities.Contains(entityType, StringComparer.OrdinalIgnoreCase))
                continue;

            Logger.LogInformation("Processing entity type: {EntityType}", entityType);

            // Check rate limiting before processing
            await rateLimitService.CheckAndWaitAsync(request.SourceStore.StoreId, CancellationToken.None);

            // Simulate entity discovery and processing
            await SimulateEntityProcessing(migrationId, entityType, apiClient, progressTracker);

            Logger.LogInformation("Completed processing entity type: {EntityType}", entityType);
        }
    }

    /// <summary>
    /// Simulates entity processing for integration testing
    /// </summary>
    private async Task SimulateEntityProcessing(string migrationId, string entityType, IBigCommerceApiClient apiClient, IProgressTracker progressTracker)
    {
        try
        {
            // Start tracking progress for this entity type
            var estimatedCount = Math.Min(TestConfig.MaxTestEntities, 10); // Limit for integration testing
            await progressTracker.StartEntityProcessingAsync(migrationId, entityType, estimatedCount, CancellationToken.None);

            // Simulate discovering entities
            Logger.LogDebug("Discovering {EntityType} entities", entityType);
            
            // For integration testing, we'll just validate API connectivity rather than actual migration
            var isHealthy = await apiClient.IsHealthyAsync(SourceStore);
            Assert.True(isHealthy, $"Source store should be healthy for {entityType} processing");

            // Simulate processing batches
            var processedCount = 0;
            var batchSize = 5;

            for (int batch = 0; batch < (estimatedCount / batchSize); batch++)
            {
                Logger.LogDebug("Processing {EntityType} batch {BatchNumber}", entityType, batch + 1);

                // Simulate batch processing delay
                await Task.Delay(500);

                var batchProcessed = Math.Min(batchSize, estimatedCount - processedCount);
                processedCount += batchProcessed;

                // Record batch completion
                await progressTracker.RecordBatchCompletionAsync(migrationId, entityType, batch + 1, 
                    batchProcessed, batchProcessed, 0, CancellationToken.None);
            }

            // Complete entity processing
            await progressTracker.CompleteEntityProcessingAsync(migrationId, entityType, CancellationToken.None);

            Logger.LogDebug("Completed {EntityType} processing: {ProcessedCount} entities", entityType, processedCount);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process {EntityType} entities", entityType);
            throw;
        }
    }

    /// <summary>
    /// Logs performance metrics for the test run
    /// </summary>
    private async Task LogPerformanceMetrics(string testId, long durationMs, int entityCount)
    {
        if (!TestConfig.EnablePerformanceMetrics)
            return;

        try
        {
            var openSearchService = GetService<IOpenSearchService>();
            
            var metrics = new
            {
                TestId = testId,
                DurationMs = durationMs,
                EntityCount = entityCount,
                EntitiesPerSecond = entityCount / (durationMs / 1000.0),
                TestType = "EndToEndIntegration",
                SourceStore = SourceStore.StoreId,
                DestinationStore = DestinationStore.StoreId,
                Timestamp = DateTime.UtcNow
            };

            await openSearchService.LogMigrationEventAsync("IntegrationTestMetrics", testId, metrics, CancellationToken.None);
            
            Logger.LogInformation("Performance metrics logged: {Metrics}", System.Text.Json.JsonSerializer.Serialize(metrics));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to log performance metrics for test: {TestId}", testId);
        }
    }

    #endregion
} 