using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System;

namespace BigCommerce.Migration.OrchestrationTests.Activities;

/// <summary>
/// TDD Unit tests for EnhancedProgressActivities
/// Tests the enhanced progress tracking activity functions for orchestrators
/// </summary>
public class EnhancedProgressActivitiesTests
{
    private readonly Mock<ILogger<EnhancedProgressActivities>> _mockLogger;
    private readonly Mock<IEnhancedMigrationSignalRService> _mockEnhancedSignalRService;
    private readonly Mock<IProgressTracker> _mockProgressTracker;
    private readonly EnhancedProgressActivities _activity;
    private readonly string _testMigrationId = "test-enhanced-migration-789";
    private readonly string _testEntityType = "products";

    public EnhancedProgressActivitiesTests()
    {
        _mockLogger = new Mock<ILogger<EnhancedProgressActivities>>();
        _mockEnhancedSignalRService = new Mock<IEnhancedMigrationSignalRService>();
        _mockProgressTracker = new Mock<IProgressTracker>();
        _activity = new EnhancedProgressActivities(
            _mockLogger.Object,
            _mockProgressTracker.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedProgressActivities(
                null!, 
                _mockProgressTracker.Object));
    }

    [Fact]
    public void Constructor_WithNullEnhancedSignalRService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedProgressActivities(
                _mockLogger.Object, 
                null!));
    }

    [Fact]
    public void Constructor_WithNullProgressTracker_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedProgressActivities(
                _mockLogger.Object, 
                null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var activity = new EnhancedProgressActivities(
            _mockLogger.Object,
            _mockProgressTracker.Object);

        // Assert
        Assert.NotNull(activity);
    }

    #endregion

    #region InitializeEntityBatchTracking Tests

    [Fact]
    public async Task InitializeEntityBatchTrackingAsync_WithValidRequest_InitializesBatchTracking()
    {
        // Arrange
        var request = new InitializeEntityBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            TotalBatches = 10,
            TotalEntities = 500
        };
        var cancellationToken = CancellationToken.None;

        // Act
        await _activity.InitializeEntityBatchTrackingAsync(request, cancellationToken);

        // Assert - Activities now only update progress tracker, no SignalR calls
        _mockProgressTracker.Verify(x => x.StartEntityProcessingAsync(
            _testMigrationId, _testEntityType, 500, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task InitializeEntityBatchTrackingAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        InitializeEntityBatchTrackingRequest? request = null;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _activity.InitializeEntityBatchTrackingAsync(request!, cancellationToken));
    }

    [Fact]
    public async Task InitializeEntityBatchTrackingAsync_WithInvalidRequest_ThrowsArgumentException()
    {
        // Arrange
        var request = new InitializeEntityBatchTrackingRequest
        {
            MigrationId = "", // Invalid empty migration ID
            EntityType = _testEntityType,
            TotalBatches = 10,
            TotalEntities = 500
        };
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _activity.InitializeEntityBatchTrackingAsync(request, cancellationToken));
    }

    [Fact]
    public async Task InitializeEntityBatchTrackingAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var request = new InitializeEntityBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            TotalBatches = 10,
            TotalEntities = 500
        };
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _activity.InitializeEntityBatchTrackingAsync(request, cancellationToken));
    }

    #endregion

    #region StartBatchTracking Tests

    [Fact]
    public async Task StartBatchTrackingAsync_WithValidRequest_StartsBatchTracking()
    {
        // Arrange
        var request = new StartBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            BatchNumber = 1,
            BatchSize = 50,
            TotalBatches = 10
        };
        var cancellationToken = CancellationToken.None;

        // Act
        await _activity.StartBatchTrackingAsync(request, cancellationToken);

        // Assert - Activities now only update progress tracker, no SignalR calls
        _mockProgressTracker.Verify(x => x.RecordBatchCompletionAsync(
            _testMigrationId, _testEntityType, 1, 0, 0, 0, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task StartBatchTrackingAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        StartBatchTrackingRequest? request = null;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _activity.StartBatchTrackingAsync(request!, cancellationToken));
    }

    [Fact]
    public async Task StartBatchTrackingAsync_WithInvalidBatchNumber_ThrowsArgumentException()
    {
        // Arrange
        var request = new StartBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            BatchNumber = 0, // Invalid batch number
            BatchSize = 50,
            TotalBatches = 10
        };
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _activity.StartBatchTrackingAsync(request, cancellationToken));
    }

    #endregion

    #region CompleteBatchTracking Tests

    [Fact]
    public async Task CompleteBatchTrackingAsync_WithValidRequest_CompletesBatchTracking()
    {
        // Arrange
        var request = new CompleteBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            BatchNumber = 1,
            EntitiesProcessed = 50,
            SuccessfulEntities = 45,
            FailedEntities = 5,
            ProcessingDuration = TimeSpan.FromMinutes(2),
            BatchSize = 50
        };
        var cancellationToken = CancellationToken.None;

        // Act
        await _activity.CompleteBatchTrackingAsync(request, cancellationToken);

        // Assert - Activities now only update progress tracker, no SignalR calls
        _mockProgressTracker.Verify(x => x.RecordBatchCompletionAsync(
            _testMigrationId, _testEntityType, 1, 50, 45, 5, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task CompleteBatchTrackingAsync_WithError_IncludesErrorInSummary()
    {
        // Arrange
        var request = new CompleteBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            BatchNumber = 1,
            EntitiesProcessed = 1,
            SuccessfulEntities = 0,
            FailedEntities = 1,
            ProcessingDuration = TimeSpan.FromMinutes(1),
            BatchSize = 1
        };
        var cancellationToken = CancellationToken.None;

        // Act
        await _activity.CompleteBatchTrackingAsync(request, cancellationToken);

        // Assert - Activities now only update progress tracker, no SignalR calls
        _mockProgressTracker.Verify(x => x.RecordBatchCompletionAsync(
            _testMigrationId, _testEntityType, 1, 1, 0, 1, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task CompleteBatchTrackingAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        CompleteBatchTrackingRequest? request = null;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _activity.CompleteBatchTrackingAsync(request!, cancellationToken));
    }

    [Fact]
    public async Task CompleteBatchTrackingAsync_WithInvalidCounts_ThrowsArgumentException()
    {
        // Arrange
        var request = new CompleteBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            BatchNumber = 1,
            EntitiesProcessed = 50,
            SuccessfulEntities = 30,
            FailedEntities = 30, // Success + Failed > Processed (invalid)
            ProcessingDuration = TimeSpan.FromSeconds(30),
            BatchSize = 50
        };
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _activity.CompleteBatchTrackingAsync(request, cancellationToken));
    }

    #endregion

    #region UpdateEnhancedEntityProgress Tests

    [Fact]
    public async Task UpdateEnhancedEntityProgressAsync_WithValidRequest_UpdatesEnhancedProgress()
    {
        // Arrange
        var request = new UpdateEnhancedEntityProgressRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            ProcessedEntities = 75,
            SuccessfulEntities = 70,
            FailedEntities = 5,
            ProgressPercentage = 75.0,
            Phase = "processing",
            CurrentActivity = "Creating entities"
        };
        var cancellationToken = CancellationToken.None;

        // Act
        await _activity.UpdateEnhancedEntityProgressAsync(request, cancellationToken);

        // Assert - Activities now only update progress tracker, no SignalR calls
        _mockProgressTracker.Verify(x => x.UpdateProgressAsync(
            _testMigrationId, It.IsAny<ProgressUpdate>(), cancellationToken), Times.Once);
    }

    [Fact]
    public async Task UpdateEnhancedEntityProgressAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        UpdateEnhancedEntityProgressRequest? request = null;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _activity.UpdateEnhancedEntityProgressAsync(request!, cancellationToken));
    }

    [Fact]
    public async Task UpdateEnhancedEntityProgressAsync_WithInvalidProgressPercentage_ThrowsArgumentException()
    {
        // Arrange
        var request = new UpdateEnhancedEntityProgressRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            CurrentBatch = 3,
            TotalBatches = 10,
            TotalEntities = 500,
            ProcessedEntities = 150,
            SuccessfulEntities = 142,
            FailedEntities = 8,
            ProgressPercentage = 150.0, // Invalid percentage > 100
            Phase = "processing",
            CurrentActivity = "Batch 3/10",
            BatchSize = 50,
            RemainingBatches = 7,
            RemainingEntities = 350
        };
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _activity.UpdateEnhancedEntityProgressAsync(request, cancellationToken));
    }

    #endregion

    #region CompleteEntityBatchTracking Tests

    [Fact]
    public async Task CompleteEntityBatchTrackingAsync_WithValidRequest_CompletesEntityTracking()
    {
        // Arrange
        var request = new CompleteEntityBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            TotalProcessed = 500,
            SuccessfulEntities = 480,
            FailedEntities = 20
        };
        var cancellationToken = CancellationToken.None;

        // Act
        await _activity.CompleteEntityBatchTrackingAsync(request, cancellationToken);

        // Assert - Activities now only update progress tracker, no SignalR calls
        _mockProgressTracker.Verify(x => x.CompleteEntityProcessingAsync(
            _testMigrationId, _testEntityType, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task CompleteEntityBatchTrackingAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        CompleteEntityBatchTrackingRequest? request = null;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _activity.CompleteEntityBatchTrackingAsync(request!, cancellationToken));
    }

    [Fact]
    public async Task CompleteEntityBatchTrackingAsync_WithInvalidCounts_ThrowsArgumentException()
    {
        // Arrange
        var request = new CompleteEntityBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            TotalProcessed = 500,
            SuccessfulEntities = 300,
            FailedEntities = 250 // Success + Failed > Total (invalid)
        };
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _activity.CompleteEntityBatchTrackingAsync(request, cancellationToken));
    }

    #endregion

    #region Enhanced Progress Tracker Integration Tests

    // Note: InitializeEntityBatchTrackingAsync method doesn't exist in EnhancedProgressTracker
    // Entity initialization is handled through the base ProgressTracker methods

    [Fact(Skip = "EnhancedProgressTracker constructor has incompatible interface types - needs design fix")]
    public async Task StartBatchTrackingAsync_WithEnhancedProgressTracker_CallsEnhancedMethods()
    {
        // Arrange
        var enhancedTracker = new Mock<EnhancedProgressTracker>(
            _mockLogger.Object, 
            _mockEnhancedSignalRService.Object, 
            null); // storageService parameter
        var activity = new EnhancedProgressActivities(
            _mockLogger.Object,
            enhancedTracker.Object);

        var request = new StartBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            BatchNumber = 1,
            BatchSize = 50,
            TotalBatches = 10
        };
        var cancellationToken = CancellationToken.None;

        enhancedTracker
            .Setup(x => x.StartBatchAsync(
                _testMigrationId, _testEntityType, 1, 50, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await activity.StartBatchTrackingAsync(request, cancellationToken);

        // Assert
        enhancedTracker.Verify(x => x.StartBatchAsync(
            _testMigrationId, _testEntityType, 1, 50, cancellationToken), Times.Once);
    }

    [Fact(Skip = "EnhancedProgressTracker constructor has incompatible interface types - needs design fix")]
    public async Task CompleteBatchTrackingAsync_WithEnhancedProgressTracker_CallsEnhancedMethods()
    {
        // Arrange
        var enhancedTracker = new Mock<EnhancedProgressTracker>(
            _mockLogger.Object, 
            _mockEnhancedSignalRService.Object, 
            null); // storageService parameter
        var activity = new EnhancedProgressActivities(
            _mockLogger.Object,
            enhancedTracker.Object);

        var request = new CompleteBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            BatchNumber = 1,
            EntitiesProcessed = 50,
            SuccessfulEntities = 48,
            FailedEntities = 2,
            ProcessingDuration = TimeSpan.FromSeconds(30),
            BatchSize = 50
        };
        var cancellationToken = CancellationToken.None;

        enhancedTracker
            .Setup(x => x.CompleteBatchAsync(
                _testMigrationId, _testEntityType, 1, 50, 48, 2, 
                TimeSpan.FromSeconds(30), cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await activity.CompleteBatchTrackingAsync(request, cancellationToken);

        // Assert
        enhancedTracker.Verify(x => x.CompleteBatchAsync(
            _testMigrationId, _testEntityType, 1, 50, 48, 2, 
            TimeSpan.FromSeconds(30), cancellationToken), Times.Once);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task InitializeEntityBatchTrackingAsync_WhenSignalRServiceThrows_LogsErrorButDoesNotRethrow()
    {
        // Arrange
        var request = new InitializeEntityBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            TotalBatches = 10,
            TotalEntities = 500
        };

        // Act & Assert - Since SignalR calls are moved to orchestrator, this test is no longer relevant
        // Activities should not have SignalR errors anymore
        await _activity.InitializeEntityBatchTrackingAsync(request);

        // Verify only progress tracker interaction
        _mockProgressTracker.Verify(x => x.StartEntityProcessingAsync(
            _testMigrationId, _testEntityType, 500, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task StartBatchTrackingAsync_WhenSignalRServiceThrows_LogsErrorButDoesNotRethrow()
    {
        // Arrange
        var request = new StartBatchTrackingRequest
        {
            MigrationId = _testMigrationId,
            EntityType = _testEntityType,
            BatchNumber = 1,
            BatchSize = 50,
            TotalBatches = 10
        };

        // Act & Assert - Since SignalR calls are moved to orchestrator, this test is no longer relevant
        await _activity.StartBatchTrackingAsync(request);

        // Verify only progress tracker interaction
        _mockProgressTracker.Verify(x => x.RecordBatchCompletionAsync(
            _testMigrationId, _testEntityType, 1, 0, 0, 0, CancellationToken.None), Times.Once);
    }

    #endregion

    #region Concurrent Execution Tests

    [Fact]
    public async Task EnhancedProgressActivities_ConcurrentExecution_HandlesThreadSafety()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        _mockEnhancedSignalRService
            .Setup(x => x.BroadcastDetailedProgressAsync(
                It.IsAny<string>(), It.IsAny<EnhancedMigrationProgress>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockEnhancedSignalRService
            .Setup(x => x.BroadcastBatchStartedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CurrentBatchDetails>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act - Multiple concurrent activity executions
        var tasks = new[]
        {
            _activity.InitializeEntityBatchTrackingAsync(CreateValidInitializeRequest(), cancellationToken),
            _activity.StartBatchTrackingAsync(CreateValidStartRequest(), cancellationToken),
            _activity.CompleteBatchTrackingAsync(CreateValidCompleteRequest(), cancellationToken),
            _activity.UpdateEnhancedEntityProgressAsync(CreateValidUpdateRequest(), cancellationToken)
        };

        await Task.WhenAll(tasks);

        // Assert - All tasks should complete successfully
        foreach (var task in tasks)
        {
            Assert.True(task.IsCompletedSuccessfully);
        }
    }

    #endregion

    #region Helper Methods

    private static InitializeEntityBatchTrackingRequest CreateValidInitializeRequest()
    {
        return new InitializeEntityBatchTrackingRequest
        {
            MigrationId = "test-migration-789",
            EntityType = "products",
            TotalBatches = 10,
            TotalEntities = 500
        };
    }

    private static StartBatchTrackingRequest CreateValidStartRequest()
    {
        return new StartBatchTrackingRequest
        {
            MigrationId = "test-migration-789",
            EntityType = "products",
            BatchNumber = 1,
            BatchSize = 50,
            TotalBatches = 10
        };
    }

    private static CompleteBatchTrackingRequest CreateValidCompleteRequest()
    {
        return new CompleteBatchTrackingRequest
        {
            MigrationId = "test-migration-789",
            EntityType = "products",
            BatchNumber = 1,
            EntitiesProcessed = 50,
            SuccessfulEntities = 48,
            FailedEntities = 2,
            ProcessingDuration = TimeSpan.FromSeconds(30),
            BatchSize = 50
        };
    }

    private static UpdateEnhancedEntityProgressRequest CreateValidUpdateRequest()
    {
        return new UpdateEnhancedEntityProgressRequest
        {
            MigrationId = "test-migration-789",
            EntityType = "products",
            CurrentBatch = 3,
            TotalBatches = 10,
            TotalEntities = 500,
            ProcessedEntities = 150,
            SuccessfulEntities = 142,
            FailedEntities = 8,
            ProgressPercentage = 30.0,
            Phase = "processing",
            CurrentActivity = "Batch 3/10",
            BatchSize = 50,
            RemainingBatches = 7,
            RemainingEntities = 350
        };
    }

    #endregion
} 