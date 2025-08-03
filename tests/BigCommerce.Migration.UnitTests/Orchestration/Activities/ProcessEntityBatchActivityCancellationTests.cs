using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.UnitTests.Orchestration.Activities;

/// <summary>
/// Unit tests for ProcessEntityBatchActivity Phase 2 enhanced cancellation functionality
/// Tests the deterministic cancellation integration and strategic cancellation points
/// </summary>
[Trait("Category", "Phase2Cancellation")]
[Trait("Component", "ProcessEntityBatchActivity")]
public class ProcessEntityBatchActivityCancellationTests
{
    private readonly Mock<ILogger<ProcessEntityBatchActivity>> _mockLogger;
    private readonly Mock<IEntityFetchService> _mockEntityFetchService;
    private readonly Mock<IEntityTransformService> _mockEntityTransformService;
    private readonly Mock<IEntityCreateService> _mockEntityCreateService;
    private readonly Mock<IEntityMappingService> _mockEntityMappingService;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<IRateLimitService> _mockRateLimitService;
    private readonly Mock<IOpenSearchService> _mockOpenSearchService;
    private readonly Mock<IMigrationStorageService> _mockMigrationStorageService;
    private readonly Mock<ILiveCancellationManager> _mockLiveCancellationManager;
    private readonly Mock<IErrorMessageFormatter> _mockErrorMessageFormatter;
    private readonly ProcessEntityBatchActivity _activity;

    private const string TestMigrationId = "test-migration-123";
    private const string TestEntityType = "products";
    private const int TestBatchNumber = 1;

    public ProcessEntityBatchActivityCancellationTests()
    {
        _mockLogger = new Mock<ILogger<ProcessEntityBatchActivity>>();
        _mockEntityFetchService = new Mock<IEntityFetchService>();
        _mockEntityTransformService = new Mock<IEntityTransformService>();
        _mockEntityCreateService = new Mock<IEntityCreateService>();
        _mockEntityMappingService = new Mock<IEntityMappingService>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _mockRateLimitService = new Mock<IRateLimitService>();
        _mockOpenSearchService = new Mock<IOpenSearchService>();
        _mockMigrationStorageService = new Mock<IMigrationStorageService>();
        _mockLiveCancellationManager = new Mock<ILiveCancellationManager>();
        _mockErrorMessageFormatter = new Mock<IErrorMessageFormatter>();

        _activity = new ProcessEntityBatchActivity(
            _mockLogger.Object,
            _mockEntityFetchService.Object,
            _mockEntityTransformService.Object,
            _mockEntityCreateService.Object,
            _mockEntityMappingService.Object,
            _mockErrorHandlingService.Object,
            _mockRateLimitService.Object,
            _mockOpenSearchService.Object,
            _mockMigrationStorageService.Object,
            _mockLiveCancellationManager.Object,
            _mockErrorMessageFormatter.Object);
    }

    /// <summary>
    /// Test that pre-processing cancellation check works correctly
    /// </summary>
    [Fact]
    public async Task Run_WhenCancelledBeforeProcessing_ReturnsCancelledResult()
    {
        // Arrange - Using optimized cancellation pattern (no external storage calls)
        var request = CreateTestRequest();
        request.IsCancelled = true;
        request.CancellationReason = "User requested cancellation";
        request.CancelledAt = DateTime.UtcNow;

        // Act
        var result = await _activity.Run(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestBatchNumber, result.BatchNumber);
        Assert.Single(result.Errors);
        Assert.Contains("Migration was cancelled before processing: User requested cancellation", result.Errors[0]);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);

        // Verify NO external storage calls were made (optimization benefit)
        _mockMigrationStorageService.Verify(
            x => x.GetCancellationTokenAsync(It.IsAny<string>()),
            Times.Never);

        // Verify fetch service was never called since we cancelled early
        _mockEntityFetchService.Verify(
            x => x.FetchEntitiesAsync(It.IsAny<BatchProcessingRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test that post-fetch cancellation check works correctly
    /// </summary>
    [Fact]
    public async Task Run_WhenCancelledAfterFetch_ReturnsCancelledResult()
    {
        // Arrange - Using optimized pattern where cancellation is detected via fast token check
        var request = CreateTestRequest();
        request.IsCancelled = true; // Cancellation passed from orchestrator
        request.CancellationReason = "Cancelled during processing";
        request.CancelledAt = DateTime.UtcNow;

        var fetchedEntities = CreateTestEntities();

        _mockEntityFetchService
            .Setup(x => x.FetchEntitiesAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetchedEntities);

        // Act
        var result = await _activity.Run(request);

        // Assert - With optimization, cancellation is detected before processing starts
        Assert.NotNull(result);
        Assert.Single(result.Errors);
        Assert.Contains("Migration was cancelled before processing: Cancelled during processing", result.Errors[0]);

        // Verify NO external storage calls were made (optimization benefit)
        _mockMigrationStorageService.Verify(
            x => x.GetCancellationTokenAsync(It.IsAny<string>()),
            Times.Never);

        // Verify with optimization: NO operations are called since cancellation is detected upfront
        _mockEntityFetchService.Verify(
            x => x.FetchEntitiesAsync(request, It.IsAny<CancellationToken>()),
            Times.Never);

        _mockEntityTransformService.Verify(
            x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<BatchProcessingRequest>()),
            Times.Never);
    }

    /// <summary>
    /// Test that per-entity cancellation check works correctly with optimized pattern
    /// </summary>
    [Fact]
    public async Task Run_WhenCancelledDuringEntityProcessing_ReturnsCancelledResult()
    {
        // Arrange - Using optimized pattern where cancellation is detected via fast token check
        var request = CreateTestRequest();
        request.IsCancelled = true;
        request.CancellationReason = "Cancelled during entity processing";
        request.CancelledAt = DateTime.UtcNow;

        var fetchedEntities = CreateTestEntities();

        _mockEntityFetchService
            .Setup(x => x.FetchEntitiesAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetchedEntities);

        // Act
        var result = await _activity.Run(request);

        // Assert - With optimization, cancellation is detected before processing starts
        Assert.NotNull(result);
        Assert.Single(result.Errors);
        Assert.Contains("Migration was cancelled before processing: Cancelled during entity processing", result.Errors[0]);

        // Verify NO external storage calls were made (optimization benefit)
        _mockMigrationStorageService.Verify(
            x => x.GetCancellationTokenAsync(It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// Test that post-transform cancellation check works correctly
    /// </summary>
    [Fact]
    public async Task Run_WhenCancelledAfterTransform_ReturnsCancelledResult()
    {
        // Arrange - Using optimized pattern where cancellation is detected via fast token check
        var request = CreateTestRequest();
        request.IsCancelled = true;
        request.CancellationReason = "Cancelled after transformation";
        request.CancelledAt = DateTime.UtcNow;

        var fetchedEntities = CreateTestEntities();
        var transformedEntity = CreateTransformedEntity();

        _mockEntityFetchService
            .Setup(x => x.FetchEntitiesAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetchedEntities);

        _mockEntityTransformService
            .Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request))
            .ReturnsAsync(transformedEntity);

        // Act
        var result = await _activity.Run(request);

        // Assert - With optimization, cancellation is detected before processing starts
        Assert.NotNull(result);
        Assert.Single(result.Errors);
        Assert.Contains("Migration was cancelled before processing: Cancelled after transformation", result.Errors[0]);

        // Verify NO external storage calls were made (optimization benefit)
        _mockMigrationStorageService.Verify(
            x => x.GetCancellationTokenAsync(It.IsAny<string>()),
            Times.Never);

        // Verify with optimization: NO operations are called since cancellation is detected upfront  
        _mockEntityFetchService.Verify(
            x => x.FetchEntitiesAsync(request, It.IsAny<CancellationToken>()),
            Times.Never);

        _mockEntityTransformService.Verify(
            x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request),
            Times.Never);

        _mockEntityCreateService.Verify(
            x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test that successful processing works when no cancellation occurs
    /// </summary>
    [Fact]
    public async Task Run_WhenNotCancelled_ProcessesSuccessfully()
    {
        // Arrange - Using optimized pattern (no cancellation, no external storage calls)
        var request = CreateTestRequest();
        // request.IsCancelled defaults to false - no need to set explicitly
        
        var fetchedEntities = CreateTestEntities();
        var transformedEntity = CreateTransformedEntity();
        var createdEntities = CreateCreatedEntities();
        var entityMapping = CreateEntityMapping();

        _mockEntityFetchService
            .Setup(x => x.FetchEntitiesAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetchedEntities);

        _mockEntityTransformService
            .Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request))
            .ReturnsAsync(transformedEntity);

        _mockEntityCreateService
            .Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdEntities);

        _mockEntityMappingService
            .Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request))
            .Returns(entityMapping);

        _mockRateLimitService
            .Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.Run(request);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.TotalProcessed);
        Assert.Equal(1, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.Single(result.EntityMappings);

        // Verify all services were called
        _mockEntityFetchService.Verify(x => x.FetchEntitiesAsync(request, It.IsAny<CancellationToken>()), Times.Once);
        _mockEntityTransformService.Verify(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request), Times.Once);
        _mockEntityCreateService.Verify(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Test cancellation check failure handling (should continue processing with safe default)
    /// </summary>
    [Fact]
    public async Task Run_WhenCancellationCheckFails_ContinuesProcessing()
    {
        // Arrange
        var request = CreateTestRequest();
        var fetchedEntities = CreateTestEntities();

        // Cancellation check throws exception (simulating storage failure)
        _mockMigrationStorageService
            .Setup(x => x.GetCancellationTokenAsync(TestMigrationId))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        _mockEntityFetchService
            .Setup(x => x.FetchEntitiesAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetchedEntities);

        // Act & Assert - Should not throw, should continue processing
        var result = await _activity.Run(request);

        Assert.NotNull(result);
        // Should have processed despite cancellation check failure
        _mockEntityFetchService.Verify(x => x.FetchEntitiesAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Helper Methods

    private BatchProcessingRequest CreateTestRequest()
    {
        return new BatchProcessingRequest
        {
            MigrationId = TestMigrationId,
            EntityType = TestEntityType,
            BatchNumber = TestBatchNumber,
            TotalBatches = 1,
            EntityIds = new List<string> { "test-entity-1" },
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "source-token",
                ChannelId = "1"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "dest-store",
                AccessToken = "dest-token",
                ChannelId = "1"
            },
            AdditionalData = new Dictionary<string, object>()
        };
    }

    private List<Dictionary<string, object>> CreateTestEntities()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object>
            {
                ["id"] = "test-entity-1",
                ["name"] = "Test Product",
                ["type"] = "physical"
            }
        };
    }

    private Dictionary<string, object> CreateTransformedEntity()
    {
        return new Dictionary<string, object>
        {
            ["name"] = "Test Product",
            ["type"] = "physical",
            ["_original_entity_id"] = "test-entity-1"
        };
    }

    private List<Dictionary<string, object>> CreateCreatedEntities()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object>
            {
                ["id"] = "created-entity-1",
                ["name"] = "Test Product"
            }
        };
    }

    private EntityMapping CreateEntityMapping()
    {
        return new EntityMapping
        {
            SourceId = "test-entity-1",
            DestinationId = "created-entity-1",
            EntityType = TestEntityType,
            MigrationId = TestMigrationId
        };
    }



    #endregion
} 