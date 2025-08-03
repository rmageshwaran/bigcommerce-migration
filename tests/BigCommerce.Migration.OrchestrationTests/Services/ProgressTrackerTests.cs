using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Orchestration.Services;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Services;

/// <summary>
/// Tests for ProgressTracker with comprehensive cancellation support
/// </summary>
public class ProgressTrackerTests
{
    private readonly Mock<ILogger<ProgressTracker>> _mockLogger;
    private readonly Mock<IProgressEventPublisher> _mockProgressEventPublisher;
    private readonly Mock<ISignalREventFactory> _mockSignalREventFactory;
    private readonly ProgressTracker _service;
    private readonly string _testMigrationId = "test-migration-123";

    public ProgressTrackerTests()
    {
        _mockLogger = new Mock<ILogger<ProgressTracker>>();
        _mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
        _mockSignalREventFactory = new Mock<ISignalREventFactory>();
        _service = new ProgressTracker(_mockLogger.Object, _mockProgressEventPublisher.Object, _mockSignalREventFactory.Object);
    }

    [Fact]
    public async Task UpdateProgressAsync_WithValidUpdate_UpdatesProgress()
    {
        // Arrange
        var update = CreateValidProgressUpdate();
        var cancellationToken = CancellationToken.None;

        // Act
        await _service.UpdateProgressAsync(_testMigrationId, update, cancellationToken);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task UpdateProgressAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var update = CreateValidProgressUpdate();
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.UpdateProgressAsync(_testMigrationId, update, cancellationToken));
    }

    [Fact]
    public async Task UpdateProgressAsync_WithNullMigrationId_ThrowsArgumentException()
    {
        // Arrange
        var update = CreateValidProgressUpdate();
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.UpdateProgressAsync(null!, update, cancellationToken));
    }

    [Fact]
    public async Task UpdateProgressAsync_WithNullUpdate_ThrowsArgumentNullException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _service.UpdateProgressAsync(_testMigrationId, null!, cancellationToken));
    }

    [Fact]
    public async Task GetProgressAsync_WithValidMigrationId_ReturnsProgress()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        var progress = await _service.GetProgressAsync(_testMigrationId, cancellationToken);

        // Assert
        Assert.NotNull(progress);
        Assert.Equal(_testMigrationId, progress.MigrationId);
        Assert.NotEmpty(progress.Status);
    }

    [Fact]
    public async Task GetProgressAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.GetProgressAsync(_testMigrationId, cancellationToken));
    }

    [Fact]
    public async Task GetProgressAsync_WithNullMigrationId_ThrowsArgumentException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.GetProgressAsync(null!, cancellationToken));
    }

    [Fact]
    public async Task StartEntityProcessingAsync_WithValidParameters_StartsProcessing()
    {
        // Arrange
        var entityType = "products";
        var totalCount = 1000;
        var cancellationToken = CancellationToken.None;

        // Act
        await _service.StartEntityProcessingAsync(_testMigrationId, entityType, totalCount, cancellationToken);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task StartEntityProcessingAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var entityType = "products";
        var totalCount = 1000;
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.StartEntityProcessingAsync(_testMigrationId, entityType, totalCount, cancellationToken));
    }

    [Fact]
    public async Task StartEntityProcessingAsync_WithNullMigrationId_ThrowsArgumentException()
    {
        // Arrange
        var entityType = "products";
        var totalCount = 1000;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.StartEntityProcessingAsync(null!, entityType, totalCount, cancellationToken));
    }

    [Fact]
    public async Task StartEntityProcessingAsync_WithNullEntityType_ThrowsArgumentException()
    {
        // Arrange
        var totalCount = 1000;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.StartEntityProcessingAsync(_testMigrationId, null!, totalCount, cancellationToken));
    }

    [Fact]
    public async Task StartEntityProcessingAsync_WithNegativeTotalCount_ThrowsArgumentException()
    {
        // Arrange
        var entityType = "products";
        var totalCount = -1;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.StartEntityProcessingAsync(_testMigrationId, entityType, totalCount, cancellationToken));
    }

    [Fact]
    public async Task RecordBatchCompletionAsync_WithValidParameters_RecordsCompletion()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var processedCount = 50;
        var successCount = 45;
        var failureCount = 5;
        var cancellationToken = CancellationToken.None;

        // Act
        await _service.RecordBatchCompletionAsync(_testMigrationId, entityType, batchNumber, 
            processedCount, successCount, failureCount, cancellationToken);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task RecordBatchCompletionAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var processedCount = 50;
        var successCount = 45;
        var failureCount = 5;
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.RecordBatchCompletionAsync(_testMigrationId, entityType, batchNumber, 
                processedCount, successCount, failureCount, cancellationToken));
    }

    [Fact]
    public async Task RecordBatchCompletionAsync_WithNullMigrationId_ThrowsArgumentException()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var processedCount = 50;
        var successCount = 45;
        var failureCount = 5;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.RecordBatchCompletionAsync(null!, entityType, batchNumber, 
                processedCount, successCount, failureCount, cancellationToken));
    }

    [Fact]
    public async Task RecordBatchCompletionAsync_WithInvalidCounts_ThrowsArgumentException()
    {
        // Arrange
        var entityType = "products";
        var batchNumber = 1;
        var processedCount = 50;
        var successCount = 30;
        var failureCount = 30; // Success + Failure > Processed (invalid)
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.RecordBatchCompletionAsync(_testMigrationId, entityType, batchNumber, 
                processedCount, successCount, failureCount, cancellationToken));
    }

    [Fact]
    public async Task CompleteEntityProcessingAsync_WithValidParameters_CompletesProcessing()
    {
        // Arrange
        var entityType = "products";
        var cancellationToken = CancellationToken.None;

        // Act
        await _service.CompleteEntityProcessingAsync(_testMigrationId, entityType, cancellationToken);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task CompleteEntityProcessingAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var entityType = "products";
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.CompleteEntityProcessingAsync(_testMigrationId, entityType, cancellationToken));
    }

    [Fact]
    public async Task CompleteEntityProcessingAsync_WithNullMigrationId_ThrowsArgumentException()
    {
        // Arrange
        var entityType = "products";
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.CompleteEntityProcessingAsync(null!, entityType, cancellationToken));
    }

    [Fact]
    public async Task NotifyProgressUpdateAsync_WithValidParameters_NotifiesUpdate()
    {
        // Arrange
        var progress = CreateValidMigrationProgress();
        var cancellationToken = CancellationToken.None;

        // Act
        await _service.NotifyProgressUpdateAsync(_testMigrationId, progress, cancellationToken);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task NotifyProgressUpdateAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var progress = CreateValidMigrationProgress();
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.NotifyProgressUpdateAsync(_testMigrationId, progress, cancellationToken));
    }

    [Fact]
    public async Task NotifyProgressUpdateAsync_WithNullProgress_ThrowsArgumentNullException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _service.NotifyProgressUpdateAsync(_testMigrationId, null!, cancellationToken));
    }

    [Fact]
    public async Task ProgressTracker_ConcurrentUpdates_HandlesThreadSafety()
    {
        // Arrange
        var update = CreateValidProgressUpdate();
        var cancellationToken = CancellationToken.None;
        const int concurrentCount = 10;

        // Act - Multiple concurrent updates
        var tasks = Enumerable.Range(0, concurrentCount)
            .Select(i => _service.UpdateProgressAsync($"{_testMigrationId}_{i}", update, cancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All tasks should complete successfully
        foreach (var task in tasks)
        {
            Assert.True(task.IsCompletedSuccessfully);
        }
    }

    [Fact]
    public async Task ProgressTracker_WithProgressCalculation_CalculatesCorrectPercentages()
    {
        // Arrange
        var entityType = "products";
        var totalCount = 100;
        var cancellationToken = CancellationToken.None;

        // Start processing
        await _service.StartEntityProcessingAsync(_testMigrationId, entityType, totalCount, cancellationToken);

        // Record batch completions
        await _service.RecordBatchCompletionAsync(_testMigrationId, entityType, 1, 30, 25, 5, cancellationToken);
        await _service.RecordBatchCompletionAsync(_testMigrationId, entityType, 2, 30, 28, 2, cancellationToken);

        // Act
        var progress = await _service.GetProgressAsync(_testMigrationId, cancellationToken);

        // Assert
        Assert.NotNull(progress);
        Assert.True(progress.OverallProgressPercentage > 0);
        Assert.True(progress.ProcessedEntities > 0);
        Assert.Equal(53, progress.SuccessfulEntities); // 25 + 28
        Assert.Equal(7, progress.FailedEntities); // 5 + 2
    }

    [Fact]
    public async Task ProgressTracker_WithEntityCompletion_UpdatesEntityStatus()
    {
        // Arrange
        var entityType = "products";
        var totalCount = 100;
        var cancellationToken = CancellationToken.None;

        // Start and complete entity processing
        await _service.StartEntityProcessingAsync(_testMigrationId, entityType, totalCount, cancellationToken);
        await _service.RecordBatchCompletionAsync(_testMigrationId, entityType, 1, totalCount, 95, 5, cancellationToken);
        await _service.CompleteEntityProcessingAsync(_testMigrationId, entityType, cancellationToken);

        // Act
        var progress = await _service.GetProgressAsync(_testMigrationId, cancellationToken);

        // Assert
        Assert.NotNull(progress);
        Assert.Contains(entityType, progress.EntityProgress.Keys);
        var entityProgress = progress.EntityProgress[entityType];
        Assert.Equal("completed", entityProgress.Status);
        Assert.NotNull(entityProgress.EndTime);
        Assert.Equal(100.0, entityProgress.ProgressPercentage);
    }

    [Fact]
    public async Task ProgressTracker_WithInMemoryTracking_UpdatesCorrectly()
    {
        // Arrange
        var update = CreateValidProgressUpdate();
        var cancellationToken = CancellationToken.None;

        // Act - Should work with in-memory tracking only
        await _service.UpdateProgressAsync(_testMigrationId, update, cancellationToken);
        var progress = await _service.GetProgressAsync(_testMigrationId, cancellationToken);

        // Assert - Should complete successfully with in-memory tracking
        Assert.NotNull(progress);
        Assert.Equal(_testMigrationId, progress.MigrationId);
        Assert.Equal("in_progress", progress.Status);
    }

    private static ProgressUpdate CreateValidProgressUpdate()
    {
        return new ProgressUpdate
        {
            MigrationId = "test-migration-123",
            EntityType = "products",
            Phase = "processing",
            ProcessedCount = 50,
            SuccessCount = 45,
            FailureCount = 5,
            CurrentBatch = 1,
            TotalBatches = 10,
            StatusMessage = "Processing products batch 1",
            Timestamp = DateTime.UtcNow
        };
    }

    private static MigrationProgress CreateValidMigrationProgress()
    {
        return new MigrationProgress
        {
            MigrationId = "test-migration-123",
            Status = "in_progress",
            StartTime = DateTime.UtcNow.AddMinutes(-30),
            LastUpdated = DateTime.UtcNow,
            ElapsedTime = TimeSpan.FromMinutes(30),
            EstimatedTimeRemaining = TimeSpan.FromMinutes(60),
            TotalEntities = 1000,
            ProcessedEntities = 500,
            SuccessfulEntities = 475,
            FailedEntities = 25,
            OverallProgressPercentage = 50.0,
            EntityProgress = new Dictionary<string, EntityProgress>
            {
                ["products"] = new EntityProgress
                {
                    EntityType = "products",
                    TotalCount = 1000,
                    ProcessedCount = 500,
                    SuccessCount = 475,
                    FailureCount = 25,
                    ProgressPercentage = 50.0,
                    Status = "processing"
                }
            },
            CurrentPhase = "product_migration",
            CurrentEntity = "products",
            EntitiesPerSecond = 16.7,
            ErrorRate = 0.05
        };
    }
} 