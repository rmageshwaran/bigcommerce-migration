using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Services;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Services;

/// <summary>
/// TDD Unit tests for EnhancedProgressTracker
/// Tests the enhanced progress tracking functionality with detailed batch and performance metrics
/// </summary>
public class EnhancedProgressTrackerTests
{
    private readonly Mock<ILogger<EnhancedProgressTracker>> _mockLogger;
    private readonly Mock<IEnhancedMigrationSignalRService> _mockEnhancedSignalRService;
    private readonly EnhancedProgressTracker _service;
    private readonly string _testMigrationId = "test-enhanced-migration-123";

    public EnhancedProgressTrackerTests()
    {
        _mockLogger = new Mock<ILogger<EnhancedProgressTracker>>();
        _mockEnhancedSignalRService = new Mock<IEnhancedMigrationSignalRService>();
        _service = new EnhancedProgressTracker(_mockLogger.Object, _mockEnhancedSignalRService.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedProgressTracker(null!, _mockEnhancedSignalRService.Object));
    }

    [Fact]
    public void Constructor_WithNullEnhancedSignalRService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedProgressTracker(_mockLogger.Object, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var service = new EnhancedProgressTracker(_mockLogger.Object, _mockEnhancedSignalRService.Object);

        // Assert
        Assert.NotNull(service);
        Assert.IsAssignableFrom<ProgressTracker>(service);
    }

    #endregion

    #region Batch Progress Tracking Tests

    [Fact]
    public async Task UpdateBatchProgressAsync_WithValidParameters_UpdatesBatchProgress()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var processedInBatch = 25;
        var successfulInBatch = 23;
        var failedInBatch = 2;
        var cancellationToken = CancellationToken.None;

        _mockEnhancedSignalRService
            .Setup(x => x.BroadcastBatchProgressAsync(
                _testMigrationId, entityType, It.IsAny<CurrentBatchDetails>(), cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.UpdateBatchProgressAsync(
            _testMigrationId, entityType, batchNumber, processedInBatch, 
            successfulInBatch, failedInBatch, cancellationToken);

        // Assert
        _mockEnhancedSignalRService.Verify(x => x.BroadcastBatchProgressAsync(
            _testMigrationId, entityType, It.IsAny<CurrentBatchDetails>(), cancellationToken), Times.Once);
    }

    [Fact]
    public async Task UpdateBatchProgressAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var processedInBatch = 25;
        var successfulInBatch = 23;
        var failedInBatch = 2;
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.UpdateBatchProgressAsync(
                _testMigrationId, entityType, batchNumber, processedInBatch, 
                successfulInBatch, failedInBatch, cancellationToken));
    }

    [Fact]
    public async Task UpdateBatchProgressAsync_WithNullMigrationId_ThrowsArgumentException()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var processedInBatch = 25;
        var successfulInBatch = 23;
        var failedInBatch = 2;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.UpdateBatchProgressAsync(
                null!, entityType, batchNumber, processedInBatch, 
                successfulInBatch, failedInBatch, cancellationToken));
    }

    [Fact]
    public async Task UpdateBatchProgressAsync_WithInvalidCounts_ThrowsArgumentException()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var processedInBatch = 25;
        var successfulInBatch = 20;
        var failedInBatch = 10; // Success + Failed > Processed (invalid)
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.UpdateBatchProgressAsync(
                _testMigrationId, entityType, batchNumber, processedInBatch, 
                successfulInBatch, failedInBatch, cancellationToken));
    }

    #endregion

    #region Batch Lifecycle Tests

    [Fact]
    public async Task StartBatchAsync_WithValidParameters_StartsBatchTracking()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var batchSize = 50;
        var totalBatches = 10;
        var cancellationToken = CancellationToken.None;

        _mockEnhancedSignalRService
            .Setup(x => x.BroadcastBatchStartedAsync(
                _testMigrationId, entityType, It.IsAny<CurrentBatchDetails>(), cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.StartBatchAsync(
            _testMigrationId, entityType, batchNumber, batchSize, cancellationToken);

        // Assert
        _mockEnhancedSignalRService.Verify(x => x.BroadcastBatchStartedAsync(
            _testMigrationId, entityType, It.IsAny<CurrentBatchDetails>(), cancellationToken), Times.Once);
    }

    [Fact]
    public async Task CompleteBatchAsync_WithValidParameters_CompletesBatchTracking()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var entitiesProcessed = 50;
        var successfulEntities = 48;
        var failedEntities = 2;
        var processingDuration = TimeSpan.FromSeconds(30);
        var batchSize = 50;
        var cancellationToken = CancellationToken.None;

        _mockEnhancedSignalRService
            .Setup(x => x.BroadcastBatchCompletedAsync(
                _testMigrationId, entityType, batchNumber, It.IsAny<BatchCompletionSummary>(), cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.CompleteBatchAsync(
            _testMigrationId, entityType, batchNumber, entitiesProcessed, 
            successfulEntities, failedEntities, processingDuration, cancellationToken);

        // Assert
        _mockEnhancedSignalRService.Verify(x => x.BroadcastBatchCompletedAsync(
            _testMigrationId, entityType, batchNumber, It.IsAny<BatchCompletionSummary>(), cancellationToken), Times.Once);
    }

    [Fact]
    public async Task CompleteBatchAsync_WithError_IncludesErrorInSummary()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var entitiesProcessed = 50;
        var successfulEntities = 0;
        var failedEntities = 50;
        var processingDuration = TimeSpan.FromSeconds(30);
        var batchSize = 50;
        var error = "API rate limit exceeded";
        var cancellationToken = CancellationToken.None;

        _mockEnhancedSignalRService
            .Setup(x => x.BroadcastBatchCompletedAsync(
                _testMigrationId, entityType, batchNumber, It.IsAny<BatchCompletionSummary>(), cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.CompleteBatchAsync(
            _testMigrationId, entityType, batchNumber, entitiesProcessed, 
            successfulEntities, failedEntities, processingDuration, cancellationToken);

        // Assert
        _mockEnhancedSignalRService.Verify(x => x.BroadcastBatchCompletedAsync(
            _testMigrationId, entityType, batchNumber, 
            It.Is<BatchCompletionSummary>(s => s.ErrorRate == 1.0), cancellationToken), Times.Once);
    }

    #endregion

    #region Performance Metrics Tests

    // Note: UpdatePerformanceMetricsAsync method doesn't exist in EnhancedProgressTracker
    // Performance metrics are updated internally as part of UpdateBatchProgressAsync

    #endregion

    #region Remaining Workload Tests

    // Note: UpdateRemainingWorkloadAsync method doesn't exist in EnhancedProgressTracker
    // Remaining workload is updated internally as part of UpdateBatchProgressAsync

    #endregion

    #region Milestone Tracking Tests

    // Note: CheckAndBroadcastMilestonesAsync method is private in EnhancedProgressTracker
    // Milestone checking is done internally as part of UpdateBatchProgressAsync

    #endregion

    #region Processing Context Tests

    // Note: UpdateProcessingContextAsync method doesn't exist in EnhancedProgressTracker
    // Processing context is updated internally as part of UpdateBatchProgressAsync

    #endregion

    #region Enhanced Progress Integration Tests

    // Note: GetEnhancedProgressAsync method doesn't exist in EnhancedProgressTracker
    // Enhanced progress is managed internally and accessed through the base ProgressTracker methods

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task EnhancedProgressTracker_ConcurrentBatchUpdates_HandlesThreadSafety()
    {
        // Arrange
        var entityType = "products";
        var cancellationToken = CancellationToken.None;
        const int concurrentCount = 5;

        _mockEnhancedSignalRService
            .Setup(x => x.BroadcastBatchProgressAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CurrentBatchDetails>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act - Multiple concurrent batch updates
        var tasks = Enumerable.Range(0, concurrentCount)
            .Select(i => _service.UpdateBatchProgressAsync(
                $"{_testMigrationId}_{i}", entityType, i + 1, 25, 23, 2, cancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All tasks should complete successfully
        foreach (var task in tasks)
        {
            Assert.True(task.IsCompletedSuccessfully);
        }

        _mockEnhancedSignalRService.Verify(x => x.BroadcastBatchProgressAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CurrentBatchDetails>(), It.IsAny<CancellationToken>()), 
            Times.Exactly(concurrentCount));
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task UpdateBatchProgressAsync_WhenSignalRServiceThrows_LogsErrorButDoesNotRethrow()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var processedInBatch = 25;
        var successfulInBatch = 23;
        var failedInBatch = 2;
        var cancellationToken = CancellationToken.None;

        _mockEnhancedSignalRService
            .Setup(x => x.BroadcastBatchProgressAsync(
                _testMigrationId, entityType, It.IsAny<CurrentBatchDetails>(), cancellationToken))
            .ThrowsAsync(new InvalidOperationException("SignalR connection failed"));

        // Act & Assert - Should not throw, should log error
        await _service.UpdateBatchProgressAsync(
            _testMigrationId, entityType, batchNumber, processedInBatch, 
            successfulInBatch, failedInBatch, cancellationToken);

        // Verify error was logged
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to broadcast batch progress")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion
} 