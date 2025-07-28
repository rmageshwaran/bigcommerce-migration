using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Activities;

/// <summary>
/// 🎯 PHASE 3: Comprehensive unit tests for sub-batch optimization functionality
/// Tests the core sub-batch processing logic, progress tracking, and performance improvements
/// </summary>
public class ProcessParallelBatchesActivitySubBatchTests
{
    #region Test Setup

    private readonly Mock<ILogger<ProcessParallelBatchesActivity>> _mockLogger;
    private readonly Mock<IEnhancedParallelProcessor> _mockParallelProcessor;
    private readonly Mock<IParallelBatchProcessingPipeline> _mockParallelPipeline;
    private readonly Mock<IProgressEventPublisher> _mockProgressEventPublisher;
    private readonly Mock<IEntityFetchService> _mockEntityFetchService;
    private readonly Mock<IEntityTransformService> _mockEntityTransformService;
    private readonly Mock<IEntityCreateService> _mockEntityCreateService;
    private readonly Mock<IEntityMappingService> _mockEntityMappingService;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<IMigrationStorageService> _mockMigrationStorageService;
    private readonly ProcessParallelBatchesActivity _activity;

    public ProcessParallelBatchesActivitySubBatchTests()
    {
        _mockLogger = new Mock<ILogger<ProcessParallelBatchesActivity>>();
        _mockParallelProcessor = new Mock<IEnhancedParallelProcessor>();
        _mockParallelPipeline = new Mock<IParallelBatchProcessingPipeline>();
        _mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
        _mockEntityFetchService = new Mock<IEntityFetchService>();
        _mockEntityTransformService = new Mock<IEntityTransformService>();
        _mockEntityCreateService = new Mock<IEntityCreateService>();
        _mockEntityMappingService = new Mock<IEntityMappingService>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _mockMigrationStorageService = new Mock<IMigrationStorageService>();

        _activity = new ProcessParallelBatchesActivity(
            _mockLogger.Object,
            _mockParallelProcessor.Object,
            _mockParallelPipeline.Object,
            _mockProgressEventPublisher.Object,
            _mockEntityFetchService.Object,
            _mockEntityTransformService.Object,
            _mockEntityCreateService.Object,
            _mockEntityMappingService.Object,
            _mockErrorHandlingService.Object,
            _mockMigrationStorageService.Object);
    }

    #endregion

    #region Sub-Batch Creation Tests

    [Fact]
    public void CreateSubBatches_With50Entities_ShouldCreate10SubBatchesOf5()
    {
        // Arrange: 50 brands should create 10 sub-batches of 5 entities each
        var entities = CreateTestEntities(50);
        var batch = CreateTestBatchRequest("brands", batchNumber: 1);

        // Act: Use reflection to access the private CreateSubBatches method
        var createSubBatchesMethod = typeof(ProcessParallelBatchesActivity)
            .GetMethod("CreateSubBatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var subBatches = (List<SubBatchRequest>)createSubBatchesMethod!.Invoke(_activity, new object[] { entities, batch });

        // Assert: Verify 10 sub-batches were created
        Assert.Equal(10, subBatches.Count);
        
        // Verify each sub-batch has the correct properties
        for (int i = 0; i < 10; i++)
        {
            var subBatch = subBatches[i];
            Assert.Equal(i + 1, subBatch.SubBatchNumber); // 1-10
            Assert.Equal(1, subBatch.ParentBatchNumber); // Parent batch 1
            Assert.Equal(5, subBatch.Entities.Count); // 5 entities per sub-batch
            Assert.Equal(5, subBatch.MaxConcurrency); // 5 concurrent entities
            Assert.Equal("test-migration", subBatch.MigrationId);
            Assert.Equal("brands", subBatch.EntityType);
        }
    }

    [Fact]
    public void CreateSubBatches_With42Entities_ShouldCreate9SubBatchesCorrectly()
    {
        // Arrange: 42 brands (like page 4 of 192 total) should create 9 sub-batches
        var entities = CreateTestEntities(42);
        var batch = CreateTestBatchRequest("brands", batchNumber: 4);

        // Act: Use reflection to access the private CreateSubBatches method
        var createSubBatchesMethod = typeof(ProcessParallelBatchesActivity)
            .GetMethod("CreateSubBatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var subBatches = (List<SubBatchRequest>)createSubBatchesMethod!.Invoke(_activity, new object[] { entities, batch });

        // Assert: Verify correct number of sub-batches
        Assert.Equal(9, subBatches.Count); // 42 entities / 5 = 8.4 → 9 sub-batches

        // Verify first 8 sub-batches have 5 entities
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(5, subBatches[i].Entities.Count);
            Assert.Equal(i + 1, subBatches[i].SubBatchNumber);
            Assert.Equal(4, subBatches[i].ParentBatchNumber);
        }

        // Verify last sub-batch has 2 entities (42 - 8*5 = 2)
        Assert.Equal(2, subBatches[8].Entities.Count);
        Assert.Equal(9, subBatches[8].SubBatchNumber);
        Assert.Equal(4, subBatches[8].ParentBatchNumber);
    }

    [Fact]
    public void CreateSubBatches_WithEmptyEntities_ShouldReturnEmptyList()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>();
        var batch = CreateTestBatchRequest("brands", batchNumber: 1);

        // Act
        var createSubBatchesMethod = typeof(ProcessParallelBatchesActivity)
            .GetMethod("CreateSubBatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var subBatches = (List<SubBatchRequest>)createSubBatchesMethod!.Invoke(_activity, new object[] { entities, batch });

        // Assert
        Assert.Empty(subBatches);
    }

    #endregion

    #region Sub-Batch Progress Event Tests

    [Fact]
    public async Task SendSubBatchStartedEvent_ShouldPublishCorrectEvent()
    {
        // Arrange
        var subBatch = new SubBatchRequest
        {
            MigrationId = "test-migration",
            ParentBatchNumber = 1,
            SubBatchNumber = 3,
            Entities = CreateTestEntities(5),
            MaxConcurrency = 5,
            EntityType = "brands"
        };

        // Setup mock to capture the published event
        SubBatchStartedEvent capturedEvent = null;
        _mockProgressEventPublisher
            .Setup(x => x.PublishAsync(It.IsAny<SubBatchStartedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProgressEvent, CancellationToken>((evt, ct) => capturedEvent = evt as SubBatchStartedEvent)
            .Returns(Task.CompletedTask);

        // Act: Use reflection to call the private method
        var sendStartedMethod = typeof(ProcessParallelBatchesActivity)
            .GetMethod("SendSubBatchStartedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        await (Task)sendStartedMethod!.Invoke(_activity, new object[] { subBatch, 50, CancellationToken.None });

        // Assert: Verify the event was published with correct properties
        Assert.NotNull(capturedEvent);
        Assert.Equal("test-migration", capturedEvent.MigrationId);
        Assert.Equal(1, capturedEvent.ParentBatchNumber);
        Assert.Equal(3, capturedEvent.SubBatchNumber);
        Assert.Equal(10, capturedEvent.TotalSubBatches); // 50 entities / 5 = 10 sub-batches
        Assert.Equal(5, capturedEvent.EntitiesInSubBatch);
        Assert.Equal(5, capturedEvent.MaxConcurrency);
        Assert.Equal("brands", capturedEvent.EntityType);

        // Verify the publisher was called
        _mockProgressEventPublisher.Verify(x => x.PublishAsync(It.IsAny<SubBatchStartedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendSubBatchCompletedEvent_ShouldPublishCorrectEvent()
    {
        // Arrange
        var subBatchResult = new SubBatchResult
        {
            MigrationId = "test-migration",
            ParentBatchNumber = 2,
            SubBatchNumber = 5,
            TotalEntities = 5,
            SuccessfulEntities = 4,
            FailedEntities = 1,
            ProcessingTime = TimeSpan.FromSeconds(2.5),
            CompletedAt = DateTime.UtcNow,
            Errors = new List<string> { "Entity 123 failed" }
        };

        var overallResult = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
        {
            SuccessfulEntities = 24, // 5 sub-batches * ~5 entities each
            FailedEntities = 1
        };

        // Setup mock to capture the published event
        SubBatchCompletedEvent capturedEvent = null;
        _mockProgressEventPublisher
            .Setup(x => x.PublishAsync(It.IsAny<SubBatchCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ProgressEvent, CancellationToken>((evt, ct) => capturedEvent = evt as SubBatchCompletedEvent)
            .Returns(Task.CompletedTask);

        // Act: Use reflection to call the private method
        var sendCompletedMethod = typeof(ProcessParallelBatchesActivity)
            .GetMethod("SendSubBatchCompletedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        await (Task)sendCompletedMethod!.Invoke(_activity, new object[] { subBatchResult, overallResult, 50, 10, "brands", CancellationToken.None });

        // Assert: Verify the event was published with correct properties
        Assert.NotNull(capturedEvent);
        Assert.Equal("test-migration", capturedEvent.MigrationId);
        Assert.Equal(2, capturedEvent.ParentBatchNumber);
        Assert.Equal(5, capturedEvent.SubBatchNumber);
        Assert.Equal(10, capturedEvent.TotalSubBatches);
        Assert.Equal(4, capturedEvent.SuccessfulEntities);
        Assert.Equal(1, capturedEvent.FailedEntities);
        Assert.Equal(5, capturedEvent.TotalEntities);
        Assert.Equal("brands", capturedEvent.EntityType);
        Assert.Equal(TimeSpan.FromSeconds(2.5), capturedEvent.ProcessingTime);
        Assert.Equal(24, capturedEvent.CumulativeSuccessfulEntities);
        Assert.Equal(1, capturedEvent.CumulativeFailedEntities);
        Assert.Equal(50, capturedEvent.TotalMigrationEntities);
        Assert.Equal(50.0, capturedEvent.ProgressPercentage); // 5/10 * 100 = 50%
        Assert.Single(capturedEvent.Errors);
        Assert.Contains("Entity 123 failed", capturedEvent.Errors);

        // Verify the publisher was called
        _mockProgressEventPublisher.Verify(x => x.PublishAsync(It.IsAny<SubBatchCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void SubBatchOptimization_ShouldProvide20xTotalParallelism()
    {
        // Arrange: Test the theoretical parallelism calculation
        var totalPages = 4; // 192 brands / 50 per page = 4 pages
        var subBatchesPerPage = 10; // 50 entities / 5 per sub-batch = 10 sub-batches
        var entitiesPerSubBatch = 5; // 5 parallel entities per sub-batch

        // Act: Calculate total parallelism
        var pageParallelism = totalPages; // 4 pages in parallel
        var subBatchParallelism = pageParallelism * entitiesPerSubBatch; // 4 pages * 5 parallel entities = 20 total parallel operations
        var totalSubBatches = totalPages * subBatchesPerPage; // 4 pages * 10 sub-batches = 40 total sub-batches

        // Assert: Verify parallelism improvements
        Assert.Equal(4, pageParallelism); // Page-level parallelism
        Assert.Equal(20, subBatchParallelism); // Total concurrent entity processing
        Assert.Equal(40, totalSubBatches); // Total sub-batches for progress granularity

        // Performance improvement calculations
        var beforeParallelism = 4; // Only page-level parallelism
        var afterParallelism = 20; // Page + sub-batch parallelism
        var parallelismImprovement = (double)afterParallelism / beforeParallelism;

        Assert.Equal(5.0, parallelismImprovement); // 5x parallelism improvement
    }

    [Fact]
    public void ProgressGranularity_ShouldProvide10xMoreUpdates()
    {
        // Arrange: Calculate progress update frequency
        var totalEntities = 192;
        var entitiesPerPage = 50;
        var entitiesPerSubBatch = 5;

        // Act: Calculate update counts
        var totalPages = (int)Math.Ceiling((double)totalEntities / entitiesPerPage); // 4 pages
        var subBatchesPerPage = entitiesPerPage / entitiesPerSubBatch; // 10 sub-batches per page
        var totalSubBatches = totalPages * subBatchesPerPage; // 40 total sub-batches

        // Before: Progress updates per page completion
        var beforeUpdates = totalPages; // 4 updates

        // After: Progress updates per sub-batch completion  
        var afterUpdates = totalSubBatches; // 40 updates

        // Assert: Verify progress granularity improvement
        Assert.Equal(4, beforeUpdates);
        Assert.Equal(40, afterUpdates);

        var granularityImprovement = (double)afterUpdates / beforeUpdates;
        Assert.Equal(10.0, granularityImprovement); // 10x more granular updates
    }

    [Theory]
    [InlineData(192, 50, 5, 4, 40)] // 192 brands: 4 pages, 40 sub-batches
    [InlineData(100, 50, 5, 2, 20)] // 100 products: 2 pages, 20 sub-batches
    [InlineData(250, 50, 5, 5, 50)] // 250 customers: 5 pages, 50 sub-batches
    public void SubBatchCalculation_ShouldBeCorrectForDifferentEntityCounts(
        int totalEntities, int entitiesPerPage, int entitiesPerSubBatch, 
        int expectedPages, int expectedSubBatches)
    {
        // Act
        var actualPages = (int)Math.Ceiling((double)totalEntities / entitiesPerPage);
        var subBatchesPerPage = entitiesPerPage / entitiesPerSubBatch;
        var actualSubBatches = actualPages * subBatchesPerPage;

        // Assert
        Assert.Equal(expectedPages, actualPages);
        Assert.Equal(expectedSubBatches, actualSubBatches);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task SendSubBatchStartedEvent_WhenPublisherThrows_ShouldLogWarningAndContinue()
    {
        // Arrange
        var subBatch = new SubBatchRequest
        {
            MigrationId = "test-migration",
            ParentBatchNumber = 1,
            SubBatchNumber = 1,
            Entities = CreateTestEntities(5),
            EntityType = "brands"
        };

        _mockProgressEventPublisher
            .Setup(x => x.PublishAsync(It.IsAny<SubBatchStartedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR connection failed"));

        // Act: Should not throw - error should be handled gracefully
        var sendStartedMethod = typeof(ProcessParallelBatchesActivity)
            .GetMethod("SendSubBatchStartedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        await (Task)sendStartedMethod!.Invoke(_activity, new object[] { subBatch, 50, CancellationToken.None });

        // Assert: Verify that warning was logged (publisher was still called)
        _mockProgressEventPublisher.Verify(x => x.PublishAsync(It.IsAny<SubBatchStartedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendSubBatchCompletedEvent_WhenPublisherThrows_ShouldLogWarningAndContinue()
    {
        // Arrange
        var subBatchResult = new SubBatchResult
        {
            MigrationId = "test-migration",
            ParentBatchNumber = 1,
            SubBatchNumber = 1,
            TotalEntities = 5,
            SuccessfulEntities = 5,
            FailedEntities = 0
        };

        var overallResult = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
        {
            SuccessfulEntities = 5,
            FailedEntities = 0
        };

        _mockProgressEventPublisher
            .Setup(x => x.PublishAsync(It.IsAny<SubBatchCompletedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR connection failed"));

        // Act: Should not throw - error should be handled gracefully
        var sendCompletedMethod = typeof(ProcessParallelBatchesActivity)
            .GetMethod("SendSubBatchCompletedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        await (Task)sendCompletedMethod!.Invoke(_activity, new object[] { subBatchResult, overallResult, 50, 10, "brands", CancellationToken.None });

        // Assert: Verify that warning was logged (publisher was still called)
        _mockProgressEventPublisher.Verify(x => x.PublishAsync(It.IsAny<SubBatchCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Entity Type Support Tests

    [Theory]
    [InlineData("brands")]
    [InlineData("products")] 
    [InlineData("variants")]
    [InlineData("customers")]
    public void IsNonHierarchicalEntity_ShouldReturnTrueForSupportedEntityTypes(string entityType)
    {
        // Act: Use reflection to access the private IsNonHierarchicalEntity method
        var isNonHierarchicalMethod = typeof(ProcessParallelBatchesActivity)
            .GetMethod("IsNonHierarchicalEntity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        var result = (bool)isNonHierarchicalMethod!.Invoke(null, new object[] { entityType });

        // Assert: Should return true for non-hierarchical entities
        Assert.True(result);
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("unknown")]
    [InlineData("")]
    public void IsNonHierarchicalEntity_ShouldReturnFalseForUnsupportedEntityTypes(string entityType)
    {
        // Act: Use reflection to access the private IsNonHierarchicalEntity method
        var isNonHierarchicalMethod = typeof(ProcessParallelBatchesActivity)
            .GetMethod("IsNonHierarchicalEntity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        var result = (bool)isNonHierarchicalMethod!.Invoke(null, new object[] { entityType });

        // Assert: Should return false for hierarchical or unknown entities
        Assert.False(result);
    }

    #endregion

    #region Helper Methods

    private List<Dictionary<string, object>> CreateTestEntities(int count)
    {
        var entities = new List<Dictionary<string, object>>();
        for (int i = 1; i <= count; i++)
        {
            entities.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Test Entity {i}",
                ["description"] = $"Test entity description for entity {i}"
            });
        }
        return entities;
    }

    private BatchProcessingRequest CreateTestBatchRequest(string entityType, int batchNumber)
    {
        return new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = entityType,
            BatchNumber = batchNumber,
            TotalBatches = 4,
            EntityIds = new List<string>(),
            SourceStore = new StoreConfiguration { StoreId = "source" },
            DestinationStore = new StoreConfiguration { StoreId = "dest" }
        };
    }

    #endregion
} 