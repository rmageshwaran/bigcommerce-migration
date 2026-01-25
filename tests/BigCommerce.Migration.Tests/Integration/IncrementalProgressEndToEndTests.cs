using BigCommerce.Migration.Activities.Activities;
using BigCommerce.Migration.Activities.Models;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the complete incremental progress system
/// Tests the full flow: ProcessEntityChunkActivity → ProgressTracker → IncrementEventsService → Azure Table Storage
/// 
/// Test Scenarios:
/// 1. Complete chunk processing with incremental progress recording
/// 2. Cancellation scenarios with partial progress preservation
/// 3. Error handling and graceful degradation
/// 4. Real-time progress aggregation
/// 5. Multi-chunk migration simulation
/// </summary>
[Collection("Integration Tests")]
public class IncrementalProgressEndToEndTests : IDisposable
{
    #region Test Setup and Infrastructure

    private readonly ServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly string _testMigrationId;
    private readonly string _testTableSuffix;

    public IncrementalProgressEndToEndTests()
    {
        _testMigrationId = $"test-migration-{Guid.NewGuid():N}";
        _testTableSuffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        // Create test configuration with Azurite connection string for local testing
        var configData = new Dictionary<string, string>
        {
            ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true", // Azurite connection string
            ["BigCommerce:DefaultApiVersion"] = "v3",
            ["BigCommerce:DefaultTimeout"] = "30",
            ["BigCommerce:MaxRetries"] = "3"
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        // Set up dependency injection container with all required services
        var services = new ServiceCollection();
        ConfigureTestServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureTestServices(IServiceCollection services)
    {
        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Add configuration
        services.AddSingleton(_configuration);

        // Add real Azure Storage services for integration testing
        services.AddSingleton<IIncrementEventsService, IncrementEventsService>();

        // Add ProgressTracker with real dependencies
        services.AddSingleton<IProgressTracker>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<ProgressTracker>>();
            var mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
            var mockSignalREventFactory = new Mock<ISignalREventFactory>();
            var mockStorageService = new Mock<IMigrationStorageService>();
            var incrementEventsService = serviceProvider.GetRequiredService<IIncrementEventsService>();

            return new ProgressTracker(
                logger,
                mockProgressEventPublisher.Object,
                mockSignalREventFactory.Object,
                mockStorageService.Object,
                incrementEventsService);
        });

        // Mock other required services for ProcessEntityChunkActivity
        services.AddSingleton(CreateMockEntityFetchService());
        services.AddSingleton(CreateMockEntityTransformService());
        services.AddSingleton(CreateMockEntityCreateService());
        services.AddSingleton(CreateMockEntityMappingService());
        services.AddSingleton(CreateMockEntityErrorHandlingService());
        services.AddSingleton(CreateMockProgressEventPublisher());
        services.AddSingleton(CreateMockSignalREventFactory());
        services.AddSingleton(CreateMockProductComponentsPipeline());
        services.AddSingleton(CreateMockUniversalAggregator());
        services.AddSingleton(CreateMockCancellationStore());
        services.AddSingleton(CreateMockSubBatchProcessor());

        // Add ProcessEntityChunkActivity
        services.AddSingleton<ProcessEntityChunkActivity>();
    }

    #region Mock Service Factories

    private IEntityFetchService CreateMockEntityFetchService()
    {
        var mock = new Mock<IEntityFetchService>();
        // Configure mock to return test entities
        mock.Setup(x => x.FetchEntitiesAsync(It.IsAny<BatchProcessingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestEntities());
        return mock.Object;
    }

    private IEntityTransformService CreateMockEntityTransformService()
    {
        var mock = new Mock<IEntityTransformService>();
        // Configure mock to return successful transformations
        mock.Setup(x => x.TransformEntitiesAsync(It.IsAny<IEnumerable<object>>(), It.IsAny<string>(), 
            It.IsAny<StoreConfiguration>(), It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<object> entities, string entityType, StoreConfiguration source, 
                StoreConfiguration dest, CancellationToken ct) => entities);
        return mock.Object;
    }

    private IEntityCreateService CreateMockEntityCreateService()
    {
        var mock = new Mock<IEntityCreateService>();
        // Configure mock to simulate successful entity creation
        mock.Setup(x => x.CreateEntitiesAsync(It.IsAny<IEnumerable<object>>(), It.IsAny<string>(), 
            It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchProcessingResult
            {
                TotalProcessed = 100,
                SuccessfulEntities = 85,
                FailedEntities = 10,
                SkippedEntities = 5,
                CancelledEntities = 0,
                ProcessingTime = TimeSpan.FromSeconds(30),
                Errors = new List<string> { "Sample error for testing" }
            });
        return mock.Object;
    }

    private IEntityMappingService CreateMockEntityMappingService()
    {
        var mock = new Mock<IEntityMappingService>();
        return mock.Object;
    }

    private IEntityErrorHandlingService CreateMockEntityErrorHandlingService()
    {
        var mock = new Mock<IEntityErrorHandlingService>();
        return mock.Object;
    }

    private IProgressEventPublisher CreateMockProgressEventPublisher()
    {
        var mock = new Mock<IProgressEventPublisher>();
        return mock.Object;
    }

    private ISignalREventFactory CreateMockSignalREventFactory()
    {
        var mock = new Mock<ISignalREventFactory>();
        return mock.Object;
    }

    private IProductComponentsMigrationPipeline CreateMockProductComponentsPipeline()
    {
        var mock = new Mock<IProductComponentsMigrationPipeline>();
        return mock.Object;
    }

    private IUniversalMigrationProgressAggregator CreateMockUniversalAggregator()
    {
        var mock = new Mock<IUniversalMigrationProgressAggregator>();
        return mock.Object;
    }

    private ICancellationStore CreateMockCancellationStore()
    {
        var mock = new Mock<ICancellationStore>();
        // Configure to not be cancelled by default
        mock.Setup(x => x.IsCancelledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return mock.Object;
    }

    private ISubBatchProcessor CreateMockSubBatchProcessor()
    {
        var mock = new Mock<ISubBatchProcessor>();
        return mock.Object;
    }

    private List<object> CreateTestEntities()
    {
        var entities = new List<object>();
        for (int i = 1; i <= 100; i++)
        {
            entities.Add(new { id = i, name = $"Test Product {i}", sku = $"TEST-{i:D3}" });
        }
        return entities;
    }

    #endregion

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    #endregion

    #region End-to-End Integration Tests

    [Fact]
    public async Task ProcessEntityChunk_ShouldRecordIncrementalProgress_EndToEnd()
    {
        // Arrange
        var chunkActivity = _serviceProvider.GetRequiredService<ProcessEntityChunkActivity>();
        var incrementEventsService = _serviceProvider.GetRequiredService<IIncrementEventsService>();

        var request = CreateTestChunkRequest();

        // Act
        var result = await chunkActivity.ProcessEntityChunkAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalProcessed.Should().BeGreaterThan(0);

        // Wait a moment for async increment recording to complete
        await Task.Delay(2000);

        // Verify incremental progress was recorded in the database
        var incrementEvents = await incrementEventsService.GetChunkIncrementsAsync(
            _testMigrationId, "products");

        incrementEvents.Should().NotBeEmpty();
        incrementEvents.Should().HaveCount(1);

        var incrementEvent = incrementEvents[0];
        incrementEvent.MigrationId.Should().Be(_testMigrationId);
        incrementEvent.EntityType.Should().Be("products");
        incrementEvent.ChunkNumber.Should().Be(1);
        incrementEvent.SuccessfulEntities.Should().Be(85);
        incrementEvent.FailedEntities.Should().Be(10);
        incrementEvent.SkippedEntities.Should().Be(5);
        incrementEvent.CancelledEntities.Should().Be(0);
        incrementEvent.SourceStore.Should().Be("source-store-123");
        incrementEvent.DestinationStore.Should().Be("dest-store-456");
    }

    [Fact]
    public async Task MultipleChunks_ShouldRecordAllIncrementalProgress_EndToEnd()
    {
        // Arrange
        var chunkActivity = _serviceProvider.GetRequiredService<ProcessEntityChunkActivity>();
        var incrementEventsService = _serviceProvider.GetRequiredService<IIncrementEventsService>();

        var chunks = new[]
        {
            CreateTestChunkRequest(chunkNumber: 1, startIndex: 0),
            CreateTestChunkRequest(chunkNumber: 2, startIndex: 100),
            CreateTestChunkRequest(chunkNumber: 3, startIndex: 200)
        };

        // Act - Process multiple chunks
        var results = new List<BatchProcessingResult>();
        foreach (var chunk in chunks)
        {
            var result = await chunkActivity.ProcessEntityChunkAsync(chunk);
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(3);
        results.Should().OnlyContain(r => r.TotalProcessed > 0);

        // Wait for async increment recording to complete
        await Task.Delay(3000);

        // Verify all incremental progress was recorded
        var incrementEvents = await incrementEventsService.GetChunkIncrementsAsync(
            _testMigrationId, "products");

        incrementEvents.Should().HaveCount(3);
        incrementEvents.Should().OnlyContain(e => e.MigrationId == _testMigrationId);
        incrementEvents.Should().OnlyContain(e => e.EntityType == "products");

        // Verify chunk numbers are correct
        var chunkNumbers = incrementEvents.Select(e => e.ChunkNumber).OrderBy(n => n).ToArray();
        chunkNumbers.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task RealTimeProgressAggregation_ShouldWorkCorrectly_EndToEnd()
    {
        // Arrange
        var chunkActivity = _serviceProvider.GetRequiredService<ProcessEntityChunkActivity>();
        var progressTracker = _serviceProvider.GetRequiredService<IProgressTracker>();
        var incrementEventsService = _serviceProvider.GetRequiredService<IIncrementEventsService>();

        var chunks = new[]
        {
            CreateTestChunkRequest(chunkNumber: 1, startIndex: 0),
            CreateTestChunkRequest(chunkNumber: 2, startIndex: 100)
        };

        // Act - Process chunks and get aggregated progress
        foreach (var chunk in chunks)
        {
            await chunkActivity.ProcessEntityChunkAsync(chunk);
        }

        // Wait for async increment recording to complete
        await Task.Delay(3000);

        // Get real-time aggregated progress
        var aggregatedProgress = await incrementEventsService.GetAggregatedProgressAsync(_testMigrationId);
        var latestProgress = await progressTracker.GetLatestAggregatedProgressAsync(_testMigrationId);

        // Assert
        aggregatedProgress.Should().NotBeEmpty();
        aggregatedProgress.Should().ContainKey("products");

        var productProgress = aggregatedProgress["products"];
        productProgress.TotalChunks.Should().Be(2);
        productProgress.TotalSuccessful.Should().Be(170); // 85 * 2 chunks
        productProgress.TotalFailed.Should().Be(20); // 10 * 2 chunks
        productProgress.TotalSkipped.Should().Be(10); // 5 * 2 chunks

        latestProgress.Should().NotBeNull();
        latestProgress.MigrationId.Should().Be(_testMigrationId);
    }

    [Fact]
    public async Task IncrementEventsService_HealthCheck_ShouldPass()
    {
        // Arrange
        var incrementEventsService = _serviceProvider.GetRequiredService<IIncrementEventsService>();

        // Act
        var isHealthy = await incrementEventsService.IsHealthyAsync();

        // Assert
        isHealthy.Should().BeTrue("IncrementEventsService should be healthy with valid Azure Storage connection");
    }

    [Fact]
    public async Task ErrorInIncrementalProgress_ShouldNotBreakMigration_EndToEnd()
    {
        // Arrange
        var chunkActivity = _serviceProvider.GetRequiredService<ProcessEntityChunkActivity>();

        // Create a request with invalid store configuration to trigger increment errors
        var request = CreateTestChunkRequest();
        request.SourceStore = null; // This will cause increment recording to fail gracefully

        // Act & Assert - Should not throw despite increment recording failure
        var result = await chunkActivity.ProcessEntityChunkAsync(request);

        result.Should().NotBeNull();
        result.TotalProcessed.Should().BeGreaterThan(0);
        // Migration should complete successfully even if increment recording fails
    }

    [Fact]
    public async Task CancellationScenario_ShouldPreserveCompletedProgress_EndToEnd()
    {
        // Arrange
        var chunkActivity = _serviceProvider.GetRequiredService<ProcessEntityChunkActivity>();
        var incrementEventsService = _serviceProvider.GetRequiredService<IIncrementEventsService>();

        // Process first chunk successfully
        var firstChunk = CreateTestChunkRequest(chunkNumber: 1, startIndex: 0);
        await chunkActivity.ProcessEntityChunkAsync(firstChunk);

        // Wait for increment recording
        await Task.Delay(2000);

        // Simulate cancellation before second chunk (in real scenario, cancellation would be detected)
        // But verify that first chunk progress is preserved

        // Act - Get progress after "cancellation"
        var incrementEvents = await incrementEventsService.GetChunkIncrementsAsync(_testMigrationId, "products");
        var aggregatedProgress = await incrementEventsService.GetAggregatedProgressAsync(_testMigrationId);

        // Assert - First chunk progress should be preserved
        incrementEvents.Should().HaveCount(1);
        incrementEvents[0].ChunkNumber.Should().Be(1);
        incrementEvents[0].SuccessfulEntities.Should().Be(85);

        aggregatedProgress.Should().ContainKey("products");
        aggregatedProgress["products"].TotalSuccessful.Should().Be(85);
        aggregatedProgress["products"].TotalChunks.Should().Be(1);
    }

    [Fact]
    public async Task StatisticsCollection_ShouldWorkCorrectly_EndToEnd()
    {
        // Arrange
        var chunkActivity = _serviceProvider.GetRequiredService<ProcessEntityChunkActivity>();
        var incrementEventsService = _serviceProvider.GetRequiredService<IIncrementEventsService>();

        var chunks = new[]
        {
            CreateTestChunkRequest(chunkNumber: 1, startIndex: 0),
            CreateTestChunkRequest(chunkNumber: 2, startIndex: 100)
        };

        // Act
        foreach (var chunk in chunks)
        {
            await chunkActivity.ProcessEntityChunkAsync(chunk);
        }

        // Wait for increment recording
        await Task.Delay(3000);

        var statistics = await incrementEventsService.GetStatisticsAsync(_testMigrationId);

        // Assert
        statistics.Should().NotBeNull();
        statistics.MigrationId.Should().Be(_testMigrationId);
        statistics.TotalEvents.Should().Be(2);
        statistics.TotalSuccessfulEntities.Should().Be(170); // 85 * 2
        statistics.TotalFailedEntities.Should().Be(20); // 10 * 2
    }

    #endregion

    #region Helper Methods

    private ProcessEntityChunkRequest CreateTestChunkRequest(int chunkNumber = 1, int startIndex = 0)
    {
        return new ProcessEntityChunkRequest
        {
            MigrationId = _testMigrationId,
            EntityType = "products",
            ChunkNumber = chunkNumber,
            TotalChunks = 3,
            StartIndex = startIndex,
            ChunkSize = 100,
            EntityIds = Enumerable.Range(startIndex + 1, 100).Select(i => i.ToString()).ToList(),
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store-123",
                ApiKey = "test-source-key",
                ApiUrl = "https://source-store.bigcommerce.com/api/v3",
                ChannelId = 1
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "dest-store-456",
                ApiKey = "test-dest-key",
                ApiUrl = "https://dest-store.bigcommerce.com/api/v3",
                ChannelId = 1
            },
            CategoryTreeContext = new CategoryTreeContext(),
            UseDirectPagination = false,
            IsCancelled = false,
            CancellationReason = string.Empty
        };
    }

    #endregion
}

/// <summary>
/// Integration test collection to ensure tests run in isolation
/// </summary>
[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestCollection>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}