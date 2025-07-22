using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Functions.Services;
using BigCommerce.Migration.Functions.Hubs;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System;

namespace BigCommerce.Migration.UnitTests.Functions;

/// <summary>
/// TDD Unit tests for EnhancedMigrationSignalRService
/// Tests the enhanced SignalR broadcasting functionality for real-time progress updates
/// </summary>
public class EnhancedMigrationSignalRServiceTests
{
    private readonly Mock<ILogger<EnhancedMigrationSignalRService>> _mockLogger;
    private readonly Mock<IMigrationSignalRService> _mockBaseSignalRService;
    private readonly Mock<IMigrationHub> _mockMigrationHub;
    private readonly EnhancedMigrationSignalRService _service;
    private readonly string _testMigrationId = "test-enhanced-migration-456";
    private readonly string _testEntityType = "products";

    public EnhancedMigrationSignalRServiceTests()
    {
        _mockLogger = new Mock<ILogger<EnhancedMigrationSignalRService>>();
        _mockBaseSignalRService = new Mock<IMigrationSignalRService>();
        _mockMigrationHub = new Mock<IMigrationHub>();
        
        _service = new EnhancedMigrationSignalRService(
            _mockBaseSignalRService.Object, 
            _mockMigrationHub.Object, 
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullBaseSignalRService_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedMigrationSignalRService(
                null!, 
                _mockMigrationHub.Object, 
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullMigrationHub_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedMigrationSignalRService(
                _mockBaseSignalRService.Object, 
                null!, 
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedMigrationSignalRService(
                _mockBaseSignalRService.Object, 
                _mockMigrationHub.Object, 
                null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var service = new EnhancedMigrationSignalRService(
            _mockBaseSignalRService.Object, 
            _mockMigrationHub.Object, 
            _mockLogger.Object);

        // Assert
        Assert.NotNull(service);
        Assert.IsAssignableFrom<IEnhancedMigrationSignalRService>(service);
    }

    #endregion

    #region Enhanced Progress Broadcasting Tests

    [Fact]
    public async Task BroadcastDetailedProgressAsync_WithValidProgress_BroadcastsToMigrationGroup()
    {
        // Arrange
        var progress = CreateValidEnhancedMigrationProgress();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "DetailedProgress", progress, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastDetailedProgressAsync(_testMigrationId, progress, cancellationToken);

        // Assert
        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            _testMigrationId, "DetailedProgress", It.IsAny<object>(), cancellationToken), Times.Once);
        
        // Verify debug logging
        _mockLogger.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Broadcasting detailed progress")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        
        _mockLogger.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Successfully broadcasted detailed progress")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task BroadcastDetailedProgressAsync_WithNullProgress_LogsErrorAndContinues()
    {
        // Arrange
        EnhancedMigrationProgress? progress = null;
        var cancellationToken = CancellationToken.None;

        // Act
        await _service.BroadcastDetailedProgressAsync(_testMigrationId, progress!, cancellationToken);

        // Assert - Should not throw, just log and continue
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Failed to broadcast detailed progress")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion

    #region Processing Context Broadcasting Tests

    [Fact]
    public async Task BroadcastCurrentProcessingContextAsync_WithValidContext_BroadcastsToMigrationGroup()
    {
        // Arrange
        var context = CreateValidProcessingContext();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "ProcessingContext", context, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastCurrentProcessingContextAsync(_testMigrationId, context, cancellationToken);

        // Assert
        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            _testMigrationId, "ProcessingContext", It.IsAny<object>(), cancellationToken), Times.Once);
        
        // Verify debug logging
        _mockLogger.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Broadcasting current processing context")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion

    #region Batch Event Broadcasting Tests

    [Fact]
    public async Task BroadcastBatchStartedAsync_WithValidBatchDetails_BroadcastsToMigrationGroup()
    {
        // Arrange
        var batchDetails = CreateValidCurrentBatchDetails();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "BatchStarted", batchDetails, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastBatchStartedAsync(_testMigrationId, _testEntityType, batchDetails, cancellationToken);

        // Assert
        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            _testMigrationId, "BatchStarted", It.IsAny<object>(), cancellationToken), Times.Once);
        
        // Verify debug logging
        _mockLogger.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Broadcasting batch started")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task BroadcastBatchProgressAsync_WithValidBatchProgress_BroadcastsToMigrationGroup()
    {
        // Arrange
        var batchProgress = CreateValidCurrentBatchDetails();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "BatchProgress", batchProgress, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastBatchProgressAsync(_testMigrationId, _testEntityType, batchProgress, cancellationToken);

        // Assert
        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            _testMigrationId, "BatchProgress", It.IsAny<object>(), cancellationToken), Times.Once);
    }

    [Fact]
    public async Task BroadcastBatchCompletedAsync_WithValidBatchSummary_BroadcastsToMigrationGroup()
    {
        // Arrange
        var batchNumber = 1;
        var batchSummary = CreateValidBatchCompletionSummary();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "BatchCompleted", batchSummary, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastBatchCompletedAsync(_testMigrationId, _testEntityType, batchNumber, batchSummary, cancellationToken);

        // Assert
        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            _testMigrationId, "BatchCompleted", It.IsAny<object>(), cancellationToken), Times.Once);
        
        // Verify debug logging
        _mockLogger.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Broadcasting batch completed")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion

    #region Remaining Workload Broadcasting Tests

    [Fact]
    public async Task BroadcastRemainingWorkloadAsync_WithValidWorkload_BroadcastsToMigrationGroup()
    {
        // Arrange
        var remainingWork = CreateValidRemainingWorkload();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "RemainingWorkload", remainingWork, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastRemainingWorkloadAsync(_testMigrationId, remainingWork, cancellationToken);

        // Assert
        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            _testMigrationId, "RemainingWorkload", It.IsAny<object>(), cancellationToken), Times.Once);
    }

    #endregion

    #region Performance Metrics Broadcasting Tests

    [Fact]
    public async Task BroadcastPerformanceMetricsAsync_WithValidMetrics_BroadcastsToMigrationGroup()
    {
        // Arrange
        var metrics = CreateValidRealTimeMetrics();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "PerformanceMetrics", metrics, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastPerformanceMetricsAsync(_testMigrationId, metrics, cancellationToken);

        // Assert
        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            _testMigrationId, "PerformanceMetrics", It.IsAny<object>(), cancellationToken), Times.Once);
    }

    #endregion

    #region Entity Phase Transition Broadcasting Tests

    [Fact]
    public async Task BroadcastEntityPhaseTransitionAsync_WithValidTransition_BroadcastsToMigrationGroup()
    {
        // Arrange
        var fromPhase = "fetching";
        var toPhase = "transforming";
        var phaseData = new { EntityCount = 50, BatchSize = 25 };
        var cancellationToken = CancellationToken.None;

        var transitionData = new
        {
            EntityType = _testEntityType,
            FromPhase = fromPhase,
            ToPhase = toPhase,
            PhaseData = phaseData
        };

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "EntityPhaseTransition", transitionData, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastEntityPhaseTransitionAsync(
            _testMigrationId, _testEntityType, fromPhase, toPhase, phaseData, cancellationToken);

        // Assert
        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            _testMigrationId, "EntityPhaseTransition", It.IsAny<object>(), cancellationToken), Times.Once);
        
        // Verify debug logging
        _mockLogger.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Broadcasting entity phase transition")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion

    #region Migration Milestone Broadcasting Tests

    [Fact]
    public async Task BroadcastMigrationMilestoneAsync_WithValidMilestone_BroadcastsToMigrationGroup()
    {
        // Arrange
        var milestone = 50;
        var milestoneData = CreateValidMilestoneData();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "MigrationMilestone", milestoneData, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastMigrationMilestoneAsync(_testMigrationId, milestone, milestoneData, cancellationToken);

        // Assert
        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            _testMigrationId, "MigrationMilestone", It.IsAny<object>(), cancellationToken), Times.Once);
        
        // Verify information logging (milestone uses LogLevel.Information)
        _mockLogger.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Broadcasting migration milestone")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }



    #endregion

    #region Base Service Delegation Tests

    [Fact]
    public async Task BroadcastProgressUpdateAsync_DelegatesToBaseService()
    {
        // Arrange
        var progress = CreateValidMigrationProgress();
        var cancellationToken = CancellationToken.None;

        _mockBaseSignalRService
            .Setup(x => x.BroadcastProgressUpdateAsync(_testMigrationId, progress, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastProgressUpdateAsync(_testMigrationId, progress, cancellationToken);

        // Assert
        _mockBaseSignalRService.Verify(x => x.BroadcastProgressUpdateAsync(
            _testMigrationId, progress, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task BroadcastStatusUpdateAsync_DelegatesToBaseService()
    {
        // Arrange
        var statusData = new { Status = "in_progress", Progress = 45.5 };
        var cancellationToken = CancellationToken.None;

        _mockBaseSignalRService
            .Setup(x => x.BroadcastStatusUpdateAsync(_testMigrationId, statusData, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _service.BroadcastStatusUpdateAsync(_testMigrationId, statusData, cancellationToken);

        // Assert
        _mockBaseSignalRService.Verify(x => x.BroadcastStatusUpdateAsync(
            _testMigrationId, statusData, cancellationToken), Times.Once);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task BroadcastDetailedProgressAsync_WhenMigrationHubThrows_LogsErrorButDoesNotRethrow()
    {
        // Arrange
        var progress = CreateValidEnhancedMigrationProgress();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "DetailedProgress", It.IsAny<object>(), cancellationToken))
            .ThrowsAsync(new InvalidOperationException("SignalR connection failed"));

        // Act & Assert - Should not throw, should log error
        await _service.BroadcastDetailedProgressAsync(_testMigrationId, progress, cancellationToken);

        // Verify error was logged
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Failed to broadcast detailed progress")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task BroadcastBatchStartedAsync_WhenMigrationHubThrows_LogsErrorButDoesNotRethrow()
    {
        // Arrange
        var batchDetails = CreateValidCurrentBatchDetails();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "BatchStarted", It.IsAny<object>(), cancellationToken))
            .ThrowsAsync(new InvalidOperationException("SignalR connection failed"));

        // Act & Assert - Should not throw, should log error
        await _service.BroadcastBatchStartedAsync(_testMigrationId, _testEntityType, batchDetails, cancellationToken);

        // Verify error was logged
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Failed to broadcast batch started")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion

    #region Cancellation Token Tests

    [Fact]
    public async Task BroadcastDetailedProgressAsync_WithCancellation_LogsErrorAndContinues()
    {
        // Arrange
        var progress = CreateValidEnhancedMigrationProgress();
        var cancellationToken = new CancellationToken(true);

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "DetailedProgress", It.IsAny<object>(), cancellationToken))
            .ThrowsAsync(new OperationCanceledException("Operation was cancelled"));

        // Act
        await _service.BroadcastDetailedProgressAsync(_testMigrationId, progress, cancellationToken);

        // Assert - Should not throw, just log and continue
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Failed to broadcast detailed progress")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task BroadcastBatchStartedAsync_WithCancellation_LogsErrorAndContinues()
    {
        // Arrange
        var batchDetails = CreateValidCurrentBatchDetails();
        var cancellationToken = new CancellationToken(true);

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                _testMigrationId, "BatchStarted", It.IsAny<object>(), cancellationToken))
            .ThrowsAsync(new OperationCanceledException("Operation was cancelled"));

        // Act
        await _service.BroadcastBatchStartedAsync(_testMigrationId, _testEntityType, batchDetails, cancellationToken);

        // Assert - Should not throw, just log and continue
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => $"{v}".Contains("Failed to broadcast batch started")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion

    #region Concurrent Broadcasting Tests

    [Fact]
    public async Task EnhancedMigrationSignalRService_ConcurrentBroadcasts_HandlesThreadSafety()
    {
        // Arrange
        var progress = CreateValidEnhancedMigrationProgress();
        var context = CreateValidProcessingContext();
        var cancellationToken = CancellationToken.None;

        _mockMigrationHub
            .Setup(x => x.SendToMigrationGroup(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act - Multiple concurrent broadcasts
        var tasks = new[]
        {
            _service.BroadcastDetailedProgressAsync(_testMigrationId, progress, cancellationToken),
            _service.BroadcastCurrentProcessingContextAsync(_testMigrationId, context, cancellationToken),
            _service.BroadcastBatchStartedAsync(_testMigrationId, _testEntityType, CreateValidCurrentBatchDetails(), cancellationToken),
            _service.BroadcastPerformanceMetricsAsync(_testMigrationId, CreateValidRealTimeMetrics(), cancellationToken)
        };

        await Task.WhenAll(tasks);

        // Assert - All tasks should complete successfully
        foreach (var task in tasks)
        {
            Assert.True(task.IsCompletedSuccessfully);
        }

        _mockMigrationHub.Verify(x => x.SendToMigrationGroup(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), 
            Times.Exactly(4));
    }

    #endregion

    #region Helper Methods

    private static EnhancedMigrationProgress CreateValidEnhancedMigrationProgress()
    {
        return new EnhancedMigrationProgress
        {
            MigrationId = "test-migration-456",
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
            CurrentProcessing = CreateValidProcessingContext(),
            BatchProgress = CreateValidBatchProgressSummary(),
            RemainingWork = CreateValidRemainingWorkload(),
            Performance = CreateValidRealTimeMetrics()
        };
    }

    private static ProcessingContext CreateValidProcessingContext()
    {
        return new ProcessingContext
        {
            CurrentEntity = "product-123",
            CurrentBatchNumber = 3,
            CurrentBatch = new CurrentBatchDetails
            {
                BatchNumber = 3,
                BatchSize = 50,
                ProcessedInBatch = 25,
                BatchProgressPercentage = 50.0,
                BatchProcessingSpeed = 10.0,
                BatchElapsedTime = TimeSpan.FromMinutes(2),
                EstimatedBatchTimeRemaining = TimeSpan.FromMinutes(3)
            },
            CurrentPhase = "processing",
            CurrentActivity = "Creating product variants",
            CurrentBatchStartTime = DateTime.UtcNow.AddMinutes(-5),
            EstimatedBatchCompletion = DateTime.UtcNow.AddMinutes(3)
        };
    }

    private static CurrentBatchDetails CreateValidCurrentBatchDetails()
    {
        return new CurrentBatchDetails
        {
            BatchNumber = 1,
            BatchSize = 50,
            ProcessedInBatch = 25,
            BatchProgressPercentage = 50.0,
            BatchProcessingSpeed = 10.0,
            BatchElapsedTime = TimeSpan.FromMinutes(2),
            EstimatedBatchTimeRemaining = TimeSpan.FromMinutes(8)
        };
    }

    private static BatchCompletionSummary CreateValidBatchCompletionSummary()
    {
        return new BatchCompletionSummary
        {
            BatchNumber = 1,
            EntitiesProcessed = 50,
            SuccessfulEntities = 48,
            FailedEntities = 2,
            ProcessingDuration = TimeSpan.FromSeconds(30),
            ProcessingSpeed = 1.67,
            ErrorRate = 0.04,
            RemainingBatches = 9,
            RemainingEntities = 450
        };
    }

    private static BatchProgressSummary CreateValidBatchProgressSummary()
    {
        return new BatchProgressSummary
        {
            TotalBatches = 10,
            CompletedBatches = 3,
            ProcessingBatches = 1,
            RemainingBatches = 7,
            BatchCompletionPercentage = 30.0,
            EntityBatches = new Dictionary<string, EntityBatchProgress>
            {
                ["products"] = new EntityBatchProgress
                {
                    EntityType = "products",
                    TotalBatches = 5,
                    CompletedBatches = 2,
                    CurrentBatch = 3,
                    RemainingBatches = 3,
                    BatchSize = 50,
                    CompletionPercentage = 40.0
                }
            }
        };
    }

    private static RemainingWorkload CreateValidRemainingWorkload()
    {
        return new RemainingWorkload
        {
            RemainingEntities = 250,
            RemainingBatches = 5,
            EstimatedTimeRemaining = TimeSpan.FromMinutes(30),
            RemainingByEntityType = new Dictionary<string, int>
            {
                ["products"] = 150,
                ["categories"] = 100
            },
            RemainingBatchesByEntityType = new Dictionary<string, int>
            {
                ["products"] = 3,
                ["categories"] = 2
            }
        };
    }

    private static RealTimeMetrics CreateValidRealTimeMetrics()
    {
        return new RealTimeMetrics
        {
            CurrentProcessingSpeed = 15.5,
            AverageProcessingSpeed = 12.3,
            PeakProcessingSpeed = 20.0,
            CurrentApiCallRate = 5.2,
            CurrentErrorRate = 0.02,
            PerformanceTrend = "improving",
            LastCalculation = DateTime.UtcNow
        };
    }

    private static MigrationProgress CreateValidMigrationProgress()
    {
        return new MigrationProgress
        {
            MigrationId = "test-migration-456",
            Status = "in_progress",
            StartTime = DateTime.UtcNow.AddMinutes(-30),
            LastUpdated = DateTime.UtcNow,
            ElapsedTime = TimeSpan.FromMinutes(30),
            EstimatedTimeRemaining = TimeSpan.FromMinutes(60),
            TotalEntities = 1000,
            ProcessedEntities = 500,
            SuccessfulEntities = 475,
            FailedEntities = 25,
            OverallProgressPercentage = 50.0
        };
    }

    private static MilestoneData CreateValidMilestoneData()
    {
        return new MilestoneData
        {
            Percentage = 50,
            TimeToMilestone = TimeSpan.FromMinutes(30),
            EntitiesProcessed = 500,
            AverageSpeed = 16.7,
            EstimatedTimeToCompletion = TimeSpan.FromMinutes(30),
            Message = "Halfway there!",
            AdditionalData = new Dictionary<string, object>
            {
                ["entityType"] = "products",
                ["milestoneType"] = "halfway"
            }
        };
    }

    #endregion
} 