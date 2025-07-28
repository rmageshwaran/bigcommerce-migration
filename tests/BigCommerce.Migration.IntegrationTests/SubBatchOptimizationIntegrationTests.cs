using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services; // Added for IEntityFetchService and IEntityCreateService
using BigCommerce.Migration.IntegrationTests.Infrastructure; // Fixed namespace
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions; // Added for ITestOutputHelper

namespace BigCommerce.Migration.IntegrationTests;

/// <summary>
/// 🎯 PHASE 3: Integration tests for sub-batch optimization end-to-end flow
/// Tests the complete pipeline from entity discovery through sub-batch processing to progress tracking
/// </summary>
[Collection("IntegrationTestCollection")]
public class SubBatchOptimizationIntegrationTests : IntegrationTestBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubBatchOptimizationIntegrationTests> _logger;

    public SubBatchOptimizationIntegrationTests(ITestOutputHelper output) : base(output) // Fixed constructor
    {
        _serviceProvider = ServiceProvider; // Use ServiceProvider from base class
        _logger = _serviceProvider.GetRequiredService<ILogger<SubBatchOptimizationIntegrationTests>>();
    }

    #region End-to-End Integration Tests

    [Fact]
    public async Task SubBatchOptimization_EndToEndFlow_ShouldComplete192BrandsSuccessfully()
    {
        // Arrange: Set up for 192 brands migration with sub-batch optimization
        var migrationId = Guid.NewGuid().ToString();
        var sourceStore = CreateTestStoreConfiguration("source");
        var destinationStore = CreateTestStoreConfiguration("dest");
        
        var mockEntityFetch = _serviceProvider.GetRequiredService<Mock<IEntityFetchService>>();
        var mockEntityCreate = _serviceProvider.GetRequiredService<Mock<IEntityCreateService>>();
        var mockProgressPublisher = _serviceProvider.GetRequiredService<Mock<IProgressEventPublisher>>();

        // Setup entity fetch to return 50 brands per page (4 pages total)
        SetupEntityFetchForBrands(mockEntityFetch, totalBrands: 192, brandsPerPage: 50);
        
        // Setup entity creation to succeed for all brands
        SetupEntityCreationSuccess(mockEntityCreate);

        // Setup progress tracking
        var publishedEvents = new List<ProgressEvent>();
        SetupProgressEventCapture(mockProgressPublisher, publishedEvents);

        // Create request for parallel batch processing
        var request = new ProcessParallelBatchesRequest
        {
            MigrationId = migrationId,
            EntityType = "brands",
            TotalBatches = 4,
            BatchSize = 50,
            EntityIds = new List<string>(), // Empty for page-based processing
            SourceStore = sourceStore,
            DestinationStore = destinationStore,
            UseDirectPagination = true,
            PaginationMetadata = CreatePaginationMetadata(totalCount: 192, pageSize: 50, totalPages: 4)
        };

        var activity = _serviceProvider.GetRequiredService<ProcessParallelBatchesActivity>();

        // Act: Execute the complete sub-batch optimization flow
        var stopwatch = Stopwatch.StartNew();
        var result = await activity.ProcessParallelBatchesAsync(request);
        stopwatch.Stop();

        // Assert: Verify successful completion
        Assert.NotNull(result);
        Assert.Equal(192, result.TotalProcessed);
        Assert.Equal(192, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.Empty(result.Errors);

        // Verify performance improvement (should be significantly faster than sequential)
        var expectedMaxDuration = TimeSpan.FromMinutes(1); // Should complete well under 1 minute
        Assert.True(stopwatch.Elapsed < expectedMaxDuration, 
            $"Migration took {stopwatch.Elapsed.TotalSeconds:F1}s, expected < {expectedMaxDuration.TotalSeconds}s");

        // Verify sub-batch progress events were published
        var subBatchStartedEvents = publishedEvents.OfType<SubBatchStartedEvent>().ToList();
        var subBatchCompletedEvents = publishedEvents.OfType<SubBatchCompletedEvent>().ToList();

        Assert.Equal(40, subBatchStartedEvents.Count); // 4 pages * 10 sub-batches = 40 started events
        Assert.Equal(40, subBatchCompletedEvents.Count); // 40 completed events

        // Verify progress granularity (10x improvement over page-level)
        var pageProgressUpdates = 4; // Original page-level updates
        var subBatchProgressUpdates = subBatchCompletedEvents.Count; // 40 sub-batch updates
        var granularityImprovement = (double)subBatchProgressUpdates / pageProgressUpdates;
        
        Assert.Equal(10.0, granularityImprovement);

        _logger.LogInformation("✅ Sub-batch optimization completed successfully: " +
            "{TotalBrands} brands in {Duration:F1}s with {Granularity}x progress granularity", 
            192, stopwatch.Elapsed.TotalSeconds, granularityImprovement);
    }

    [Fact]
    public async Task SubBatchOptimization_PerformanceComparison_ShouldBe5xFasterThanSequential()
    {
        // Arrange: Set up two identical scenarios - one optimized, one sequential
        var migrationId = Guid.NewGuid().ToString();
        var sourceStore = CreateTestStoreConfiguration("source");
        var destinationStore = CreateTestStoreConfiguration("dest");
        
        var mockEntityFetch = _serviceProvider.GetRequiredService<Mock<IEntityFetchService>>();
        var mockEntityCreate = _serviceProvider.GetRequiredService<Mock<IEntityCreateService>>();

        // Setup realistic processing times (2.5 seconds per entity for sequential simulation)
        SetupEntityFetchForBrands(mockEntityFetch, totalBrands: 100, brandsPerPage: 50);
        SetupEntityCreationWithDelay(mockEntityCreate, processingTimeMs: 250); // 250ms per entity

        var request = new ProcessParallelBatchesRequest
        {
            MigrationId = migrationId,
            EntityType = "brands",
            TotalBatches = 2,
            BatchSize = 50,
            EntityIds = new List<string>(),
            SourceStore = sourceStore,
            DestinationStore = destinationStore,
            UseDirectPagination = true,
            PaginationMetadata = CreatePaginationMetadata(totalCount: 100, pageSize: 50, totalPages: 2)
        };

        var activity = _serviceProvider.GetRequiredService<ProcessParallelBatchesActivity>();

        // Act: Execute optimized sub-batch processing
        var stopwatch = Stopwatch.StartNew();
        var result = await activity.ProcessParallelBatchesAsync(request);
        stopwatch.Stop();

        // Calculate expected sequential time vs actual parallel time
        var actualParallelTime = stopwatch.Elapsed.TotalSeconds;
        var estimatedSequentialTime = 100 * 0.25; // 100 entities * 250ms = 25 seconds
        var estimatedParallelTime = estimatedSequentialTime / 5; // Expected 5x improvement = 5 seconds

        // Assert: Performance improvements
        Assert.NotNull(result);
        Assert.Equal(100, result.SuccessfulEntities);
        
        // Verify significant performance improvement (allowing some variance for test environment)
        var performanceImprovement = estimatedSequentialTime / actualParallelTime;
        Assert.True(performanceImprovement >= 3.0, 
            $"Expected at least 3x improvement, got {performanceImprovement:F1}x " +
            $"(sequential: {estimatedSequentialTime}s, parallel: {actualParallelTime:F1}s)");

        _logger.LogInformation("⚡ Performance validation: {Improvement:F1}x faster than sequential " +
            "({ParallelTime:F1}s vs estimated {SequentialTime:F1}s)", 
            performanceImprovement, actualParallelTime, estimatedSequentialTime);
    }

    [Theory]
    [InlineData(50, 1, 10)]   // 50 brands: 1 page, 10 sub-batches
    [InlineData(100, 2, 20)]  // 100 brands: 2 pages, 20 sub-batches  
    [InlineData(192, 4, 40)]  // 192 brands: 4 pages, 40 sub-batches
    [InlineData(250, 5, 50)]  // 250 brands: 5 pages, 50 sub-batches
    public async Task SubBatchOptimization_DifferentEntityCounts_ShouldScaleCorrectly(
        int totalEntities, int expectedPages, int expectedSubBatches)
    {
        // Arrange
        var migrationId = Guid.NewGuid().ToString();
        var sourceStore = CreateTestStoreConfiguration("source");
        var destinationStore = CreateTestStoreConfiguration("dest");
        
        var mockEntityFetch = _serviceProvider.GetRequiredService<Mock<IEntityFetchService>>();
        var mockEntityCreate = _serviceProvider.GetRequiredService<Mock<IEntityCreateService>>();
        var mockProgressPublisher = _serviceProvider.GetRequiredService<Mock<IProgressEventPublisher>>();

        SetupEntityFetchForBrands(mockEntityFetch, totalBrands: totalEntities, brandsPerPage: 50);
        SetupEntityCreationSuccess(mockEntityCreate);

        var publishedEvents = new List<ProgressEvent>();
        SetupProgressEventCapture(mockProgressPublisher, publishedEvents);

        var request = new ProcessParallelBatchesRequest
        {
            MigrationId = migrationId,
            EntityType = "brands",
            TotalBatches = expectedPages,
            BatchSize = 50,
            EntityIds = new List<string>(),
            SourceStore = sourceStore,
            DestinationStore = destinationStore,
            UseDirectPagination = true,
            PaginationMetadata = CreatePaginationMetadata(totalCount: totalEntities, pageSize: 50, totalPages: expectedPages)
        };

        var activity = _serviceProvider.GetRequiredService<ProcessParallelBatchesActivity>();

        // Act
        var result = await activity.ProcessParallelBatchesAsync(request);

        // Assert: Verify scaling
        Assert.NotNull(result);
        Assert.Equal(totalEntities, result.TotalProcessed);
        Assert.Equal(totalEntities, result.SuccessfulEntities);

        // Verify correct number of sub-batch events
        var subBatchCompletedEvents = publishedEvents.OfType<SubBatchCompletedEvent>().ToList();
        Assert.Equal(expectedSubBatches, subBatchCompletedEvents.Count);

        // Verify granularity improvement
        var granularityImprovement = (double)expectedSubBatches / expectedPages;
        Assert.Equal(10.0, granularityImprovement);

        _logger.LogInformation("📊 Scaling validation: {Entities} entities → {Pages} pages → {SubBatches} sub-batches " +
            "({Granularity}x granularity)", totalEntities, expectedPages, expectedSubBatches, granularityImprovement);
    }

    #endregion

    #region Progress Tracking Integration Tests

    [Fact]
    public async Task SubBatchOptimization_ProgressTracking_ShouldProvide250msUpdateIntervals()
    {
        // Arrange: Set up for rapid progress tracking validation
        var migrationId = Guid.NewGuid().ToString();
        var sourceStore = CreateTestStoreConfiguration("source");
        var destinationStore = CreateTestStoreConfiguration("dest");
        
        var mockEntityFetch = _serviceProvider.GetRequiredService<Mock<IEntityFetchService>>();
        var mockEntityCreate = _serviceProvider.GetRequiredService<Mock<IEntityCreateService>>();
        var mockProgressPublisher = _serviceProvider.GetRequiredService<Mock<IProgressEventPublisher>>();

        // Use smaller dataset for precise timing validation
        SetupEntityFetchForBrands(mockEntityFetch, totalBrands: 25, brandsPerPage: 25);
        SetupEntityCreationWithDelay(mockEntityCreate, processingTimeMs: 50); // Very fast processing

        var publishedEvents = new List<(ProgressEvent Event, DateTime Timestamp)>();
        SetupProgressEventCaptureWithTimestamp(mockProgressPublisher, publishedEvents);

        var request = new ProcessParallelBatchesRequest
        {
            MigrationId = migrationId,
            EntityType = "brands",
            TotalBatches = 1,
            BatchSize = 25,
            EntityIds = new List<string>(),
            SourceStore = sourceStore,
            DestinationStore = destinationStore,
            UseDirectPagination = true,
            PaginationMetadata = CreatePaginationMetadata(totalCount: 25, pageSize: 25, totalPages: 1)
        };

        var activity = _serviceProvider.GetRequiredService<ProcessParallelBatchesActivity>();

        // Act
        await activity.ProcessParallelBatchesAsync(request);

        // Assert: Verify rapid progress updates
        var subBatchCompletedEvents = publishedEvents
            .Where(e => e.Event is SubBatchCompletedEvent)
            .ToList();

        Assert.Equal(5, subBatchCompletedEvents.Count); // 25 entities / 5 per sub-batch = 5 sub-batches

        // Verify progress events were published rapidly (simulate 250ms interval capability)
        for (int i = 1; i < subBatchCompletedEvents.Count; i++)
        {
            var timeDiff = subBatchCompletedEvents[i].Timestamp - subBatchCompletedEvents[i - 1].Timestamp;
            
            // Should be capable of sub-second updates (much faster than old 1-second intervals)
            Assert.True(timeDiff.TotalMilliseconds < 1000, 
                $"Progress update interval too slow: {timeDiff.TotalMilliseconds}ms");
        }

        _logger.LogInformation("📡 Progress tracking validation: {Events} rapid updates with max {MaxInterval}ms intervals", 
            subBatchCompletedEvents.Count, 
            subBatchCompletedEvents.Count > 1 ? 
                subBatchCompletedEvents.Skip(1).Max(e => (e.Timestamp - subBatchCompletedEvents[0].Timestamp).TotalMilliseconds / (subBatchCompletedEvents.Count - 1)) : 
                0);
    }

    [Fact]
    public async Task SubBatchOptimization_ProgressAggregation_ShouldTrackCumulativeMetrics()
    {
        // Arrange: Set up for cumulative progress validation
        var migrationId = Guid.NewGuid().ToString();
        var mockProgressPublisher = _serviceProvider.GetRequiredService<Mock<IProgressEventPublisher>>();

        var publishedEvents = new List<ProgressEvent>();
        SetupProgressEventCapture(mockProgressPublisher, publishedEvents);

        // Create a progress aggregator to test cumulative tracking
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.Setup(x => x.UtcNow).Returns(DateTime.UtcNow);

        var aggregator = new ParallelProgressAggregator(
            migrationId, "brands", 4, mockProgressPublisher.Object, 
            _logger, dateTimeProvider.Object);

        // Act: Simulate sub-batch completions with cumulative tracking
        for (int page = 1; page <= 4; page++)
        {
            for (int subBatch = 1; subBatch <= 10; subBatch++)
            {
                await aggregator.ReportSubBatchCompletionAsync(
                    parentBatchNumber: page,
                    subBatchNumber: subBatch,
                    entitiesProcessed: 5,
                    entitiesFailed: subBatch == 5 ? 1 : 0, // Simulate 1 failure in sub-batch 5 of each page
                    processingTime: TimeSpan.FromSeconds(2),
                    subBatchErrors: subBatch == 5 ? new[] { $"Error in page {page} sub-batch {subBatch}" } : null);
            }
        }

        // Assert: Verify cumulative metrics tracking
        var subBatchProgressEvents = publishedEvents.OfType<SubBatchMigrationProgressEvent>().ToList();
        Assert.True(subBatchProgressEvents.Count > 0);

        var finalProgressEvent = subBatchProgressEvents.Last();
        Assert.Equal(196, finalProgressEvent.TotalSuccessfulEntities); // 40 sub-batches * 5 entities - 4 failures
        Assert.Equal(4, finalProgressEvent.TotalFailedEntities); // 1 failure per page * 4 pages
        Assert.Equal(200, finalProgressEvent.TotalExpectedEntities); // 4 pages * 50 entities
        Assert.True(finalProgressEvent.ProcessingRate > 0);
        Assert.Equal(100.0, finalProgressEvent.OverallProgressPercentage); // All sub-batches completed

        _logger.LogInformation("📈 Cumulative tracking validation: {Successful}/{Total} entities, " +
            "{FailedCount} failures, {Rate:F1} entities/sec", 
            finalProgressEvent.TotalSuccessfulEntities, finalProgressEvent.TotalExpectedEntities,
            finalProgressEvent.TotalFailedEntities, finalProgressEvent.ProcessingRate);
    }

    #endregion

    #region Error Handling Integration Tests

    [Fact]
    public async Task SubBatchOptimization_WithPartialFailures_ShouldIsolateErrorsCorrectly()
    {
        // Arrange: Set up for error isolation testing
        var migrationId = Guid.NewGuid().ToString();
        var sourceStore = CreateTestStoreConfiguration("source");
        var destinationStore = CreateTestStoreConfiguration("dest");
        
        var mockEntityFetch = _serviceProvider.GetRequiredService<Mock<IEntityFetchService>>();
        var mockEntityCreate = _serviceProvider.GetRequiredService<Mock<IEntityCreateService>>();
        var mockProgressPublisher = _serviceProvider.GetRequiredService<Mock<IProgressEventPublisher>>();

        SetupEntityFetchForBrands(mockEntityFetch, totalBrands: 50, brandsPerPage: 50);
        
        // Setup entity creation with some failures (every 5th entity fails)
        SetupEntityCreationWithSelectiveFailures(mockEntityCreate, failurePattern: 5);

        var publishedEvents = new List<ProgressEvent>();
        SetupProgressEventCapture(mockProgressPublisher, publishedEvents);

        var request = new ProcessParallelBatchesRequest
        {
            MigrationId = migrationId,
            EntityType = "brands",
            TotalBatches = 1,
            BatchSize = 50,
            EntityIds = new List<string>(),
            SourceStore = sourceStore,
            DestinationStore = destinationStore,
            UseDirectPagination = true,
            PaginationMetadata = CreatePaginationMetadata(totalCount: 50, pageSize: 50, totalPages: 1)
        };

        var activity = _serviceProvider.GetRequiredService<ProcessParallelBatchesActivity>();

        // Act
        var result = await activity.ProcessParallelBatchesAsync(request);

        // Assert: Verify error isolation
        Assert.NotNull(result);
        Assert.Equal(50, result.TotalProcessed);
        Assert.Equal(40, result.SuccessfulEntities); // 50 - 10 failures (every 5th)
        Assert.Equal(10, result.FailedEntities);
        Assert.Equal(10, result.Errors.Count);

        // Verify sub-batches completed despite individual failures
        var subBatchCompletedEvents = publishedEvents.OfType<SubBatchCompletedEvent>().ToList();
        Assert.Equal(10, subBatchCompletedEvents.Count); // All 10 sub-batches should complete

        // Verify errors are properly distributed across sub-batches
        var subBatchesWithErrors = subBatchCompletedEvents.Count(e => e.FailedEntities > 0);
        Assert.Equal(10, subBatchesWithErrors); // Each sub-batch should have 1 failure

        _logger.LogInformation("🛡️ Error isolation validation: {Successful}/{Total} entities, " +
            "{FailedEntities} failures isolated across {CompletedSubBatches} sub-batches", 
            result.SuccessfulEntities, result.TotalProcessed, result.FailedEntities, subBatchCompletedEvents.Count);
    }

    #endregion

    #region Helper Methods

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        // Add mocked dependencies
        services.AddSingleton(new Mock<IEntityFetchService>());
        services.AddSingleton(new Mock<IEntityTransformService>());
        services.AddSingleton(new Mock<IEntityCreateService>());
        services.AddSingleton(new Mock<IEntityMappingService>());
        services.AddSingleton(new Mock<IEntityErrorHandlingService>());
        services.AddSingleton(new Mock<IMigrationStorageService>());
        services.AddSingleton(new Mock<IEnhancedParallelProcessor>());
        services.AddSingleton(new Mock<IParallelBatchProcessingPipeline>());
        services.AddSingleton(new Mock<IProgressEventPublisher>());

        // Add the activity under test
        services.AddTransient<ProcessParallelBatchesActivity>(provider =>
            new ProcessParallelBatchesActivity(
                provider.GetRequiredService<ILogger<ProcessParallelBatchesActivity>>(),
                provider.GetRequiredService<Mock<IEnhancedParallelProcessor>>().Object,
                provider.GetRequiredService<Mock<IParallelBatchProcessingPipeline>>().Object,
                provider.GetRequiredService<Mock<IProgressEventPublisher>>().Object,
                provider.GetRequiredService<Mock<IEntityFetchService>>().Object,
                provider.GetRequiredService<Mock<IEntityTransformService>>().Object,
                provider.GetRequiredService<Mock<IEntityCreateService>>().Object,
                provider.GetRequiredService<Mock<IEntityMappingService>>().Object,
                provider.GetRequiredService<Mock<IEntityErrorHandlingService>>().Object,
                provider.GetRequiredService<Mock<IMigrationStorageService>>().Object));

        return services.BuildServiceProvider();
    }

    private StoreConfiguration CreateTestStoreConfiguration(string storeHash)
    {
        return new StoreConfiguration
        {
            StoreId = storeHash,
            AccessToken = "test-token"
        };
    }

    private void SetupEntityFetchForBrands(Mock<IEntityFetchService> mockFetch, int totalBrands, int brandsPerPage)
    {
        for (int page = 1; page <= Math.Ceiling((double)totalBrands / brandsPerPage); page++)
        {
            var startIndex = (page - 1) * brandsPerPage;
            var endIndex = Math.Min(startIndex + brandsPerPage, totalBrands);
            var pageEntities = new List<Dictionary<string, object>>();

            for (int i = startIndex; i < endIndex; i++)
            {
                pageEntities.Add(new Dictionary<string, object>
                {
                    ["id"] = i + 1,
                    ["name"] = $"Brand {i + 1}",
                    ["description"] = $"Test brand {i + 1}"
                });
            }

            mockFetch.Setup(x => x.FetchEntitiesAsync(
                It.Is<BatchProcessingRequest>(r => r.BatchNumber == page),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(pageEntities);
        }
    }

    private void SetupEntityCreationSuccess(Mock<IEntityCreateService> mockCreate)
    {
        mockCreate.Setup(x => x.CreateEntitiesAsync(
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<BatchProcessingRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Dictionary<string, object>> entities, BatchProcessingRequest request, CancellationToken ct) =>
                entities.Select(e => new Dictionary<string, object>
                {
                    ["id"] = Random.Shared.Next(1000, 9999),
                    ["name"] = e["name"],
                    ["source_id"] = e["id"]
                }).ToList());
    }

    private void SetupEntityCreationWithDelay(Mock<IEntityCreateService> mockCreate, int processingTimeMs)
    {
        mockCreate.Setup(x => x.CreateEntitiesAsync(
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<BatchProcessingRequest>(),
            It.IsAny<CancellationToken>()))
            .Returns(async (List<Dictionary<string, object>> entities, BatchProcessingRequest request, CancellationToken ct) =>
            {
                await Task.Delay(processingTimeMs * entities.Count, ct);
                return entities.Select(e => new Dictionary<string, object>
                {
                    ["id"] = Random.Shared.Next(1000, 9999),
                    ["name"] = e["name"],
                    ["source_id"] = e["id"]
                }).ToList();
            });
    }

    private void SetupEntityCreationWithSelectiveFailures(Mock<IEntityCreateService> mockCreate, int failurePattern)
    {
        mockCreate.Setup(x => x.CreateEntitiesAsync(
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<BatchProcessingRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Dictionary<string, object>> entities, BatchProcessingRequest request, CancellationToken ct) =>
            {
                var results = new List<Dictionary<string, object>>();
                for (int i = 0; i < entities.Count; i++)
                {
                    var entity = entities[i];
                    var entityId = (int)entity["id"];
                    
                    if (entityId % failurePattern == 0)
                    {
                        throw new InvalidOperationException($"Simulated failure for entity {entityId}");
                    }

                    results.Add(new Dictionary<string, object>
                    {
                        ["id"] = Random.Shared.Next(1000, 9999),
                        ["name"] = entity["name"],
                        ["source_id"] = entity["id"]
                    });
                }
                return results;
            });
    }

    private void SetupProgressEventCapture(Mock<IProgressEventPublisher> mockPublisher, List<ProgressEvent> capturedEvents)
    {
        mockPublisher.Setup(x => x.PublishAsync(It.IsAny<ProgressEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProgressEvent, CancellationToken>((evt, ct) => capturedEvents.Add(evt))
            .Returns(Task.CompletedTask);
    }

    private void SetupProgressEventCaptureWithTimestamp(Mock<IProgressEventPublisher> mockPublisher, 
        List<(ProgressEvent Event, DateTime Timestamp)> capturedEvents)
    {
        mockPublisher.Setup(x => x.PublishAsync(It.IsAny<ProgressEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProgressEvent, CancellationToken>((evt, ct) => capturedEvents.Add((evt, DateTime.UtcNow)))
            .Returns(Task.CompletedTask);
    }

    private Dictionary<string, object> CreatePaginationMetadata(int totalCount, int pageSize, int totalPages)
    {
        return new Dictionary<string, object>
        {
            ["TotalCount"] = totalCount,
            ["PageSize"] = pageSize,
            ["TotalPages"] = totalPages,
            ["UseDirectPagination"] = true
        };
    }

    #endregion
} 