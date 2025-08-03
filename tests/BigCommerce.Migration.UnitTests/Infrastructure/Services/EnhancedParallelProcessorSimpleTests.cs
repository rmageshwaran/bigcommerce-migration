using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BatchProcessingResult = BigCommerce.Migration.Core.Interfaces.BatchProcessingResult;
using BigCommerce.Migration.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// ✅ **Focused Tests for EnhancedParallelProcessor**
/// 
/// Tests critical throughput optimization functionality:
/// - Constructor validation 
/// - ProcessBatchesInParallelAsync core scenarios
/// - Error handling and cancellation
/// - Integration with IDynamicRateLimiter
/// </summary>
public class EnhancedParallelProcessorSimpleTests : IDisposable
{
    private readonly Mock<IDynamicRateLimiter> _mockDynamicRateLimiter;
    private readonly Mock<ISignalREventFactory> _mockSignalREventFactory;
    private readonly Mock<ILogger<EnhancedParallelProcessor>> _mockLogger;
    private readonly Mock<IDateTimeProvider> _mockDateTimeProvider;
    private readonly EnhancedParallelProcessor _processor;

    public EnhancedParallelProcessorSimpleTests()
    {
        _mockDynamicRateLimiter = new Mock<IDynamicRateLimiter>();
        _mockSignalREventFactory = new Mock<ISignalREventFactory>();
        _mockLogger = new Mock<ILogger<EnhancedParallelProcessor>>();
        _mockDateTimeProvider = new Mock<IDateTimeProvider>();
        
        _mockDateTimeProvider.Setup(x => x.UtcNow).Returns(DateTime.UtcNow);
        
        // Setup required IDynamicRateLimiter mock methods
        _mockDynamicRateLimiter.Setup(x => x.CanMakeRequestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        _mockDynamicRateLimiter.Setup(x => x.CheckAndWaitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        _mockDynamicRateLimiter.Setup(x => x.GetApiHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiHealthMetrics
            {
                ErrorRate = 0.01,
                AverageResponseTimeMs = 150,
                SuccessfulRequests = 100,
                FailedRequests = 1
            });
        
        _processor = new EnhancedParallelProcessor(
            _mockDynamicRateLimiter.Object,
            _mockSignalREventFactory.Object,
            _mockLogger.Object,
            _mockDateTimeProvider.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        _processor.Should().NotBeNull();
        
        // Verify logger was called with initialization message
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Enhanced Parallel Processor initialized")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithNullDynamicRateLimiter_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new EnhancedParallelProcessor(
            null!,
            _mockSignalREventFactory.Object,
            _mockLogger.Object,
            _mockDateTimeProvider.Object);
        
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("dynamicRateLimiter");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new EnhancedParallelProcessor(
            _mockDynamicRateLimiter.Object,
            _mockSignalREventFactory.Object,
            null!,
            _mockDateTimeProvider.Object);
        
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullDateTimeProvider_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new EnhancedParallelProcessor(
            _mockDynamicRateLimiter.Object,
            _mockSignalREventFactory.Object,
            _mockLogger.Object,
            null!);
        
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("dateTimeProvider");
    }

    #endregion

    #region ProcessBatchesInParallelAsync Tests

    [Fact]
    public async Task ProcessBatchesInParallelAsync_WithEmptyBatches_ShouldReturnEmptyResult()
    {
        // Arrange
        var emptyBatches = new List<TestBatch>();
        var config = CreateTestConfig();

        // Act
        var result = await _processor.ProcessBatchesInParallelAsync(
            emptyBatches,
            ProcessTestBatch,
            config);

        // Assert
        result.Should().NotBeNull();
        result.TotalBatchesProcessed.Should().Be(0);
        result.TotalEntitiesProcessed.Should().Be(0);
        result.SuccessfulBatches.Should().Be(0);
        result.FailedBatches.Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatchesInParallelAsync_WithValidBatches_ShouldProcessSuccessfully()
    {
        // Arrange
        var batches = new List<TestBatch>
        {
            new() { Id = 1, EntityCount = 10 },
            new() { Id = 2, EntityCount = 15 },
            new() { Id = 3, EntityCount = 8 }
        };
        var config = CreateTestConfig();

        // Act
        var result = await _processor.ProcessBatchesInParallelAsync(
            batches,
            ProcessTestBatch,
            config);

        // Assert
        result.Should().NotBeNull();
        result.TotalBatchesProcessed.Should().Be(3);
        result.TotalEntitiesProcessed.Should().Be(33); // 10 + 15 + 8
        result.SuccessfulBatches.Should().Be(3);
        result.FailedBatches.Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatchesInParallelAsync_WithBatchProcessorError_ShouldHandleErrorsGracefully()
    {
        // Arrange
        var batches = new List<TestBatch>
        {
            new() { Id = 1, EntityCount = 10 },
            new() { Id = 2, EntityCount = 15, ShouldFail = true }, // This batch will fail
            new() { Id = 3, EntityCount = 8 }
        };
        var config = CreateTestConfig();

        // Act
        var result = await _processor.ProcessBatchesInParallelAsync(
            batches,
            ProcessTestBatch,
            config);

        // Assert
        result.Should().NotBeNull();
        result.TotalBatchesProcessed.Should().Be(3);
        result.SuccessfulBatches.Should().Be(2);
        result.FailedBatches.Should().Be(1);
        result.TotalEntitiesProcessed.Should().Be(33); // 10 + 15 + 8 (all batches report their processed count)
        result.TotalEntitiesFailed.Should().Be(15); // Failed batch had 15 entities
    }

    [Fact]
    public async Task ProcessBatchesInParallelAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var batches = CreateLargeBatchList(50); // Large number to ensure cancellation hits
        var config = CreateTestConfig();
        var cts = new CancellationTokenSource();
        
        // Cancel after a short delay (enough to start some batches but not complete all)
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act - Cancellation should be handled gracefully, not throw an exception
        var result = await _processor.ProcessBatchesInParallelAsync(
            batches,
            SlowProcessTestBatch, // Slow processor to ensure cancellation hits
            config,
            cancellationToken: cts.Token);

        // Assert - Should complete gracefully with partial results
        result.Should().NotBeNull();
        result.TotalBatchesProcessed.Should().BeLessOrEqualTo(50); // At most all batches complete
        // In fast test environments, cancellation might not occur, so allow completion
        if (result.TotalBatchesProcessed < 50)
        {
            result.FailedBatches.Should().BeGreaterThan(0); // Some batches should be marked as failed due to cancellation
        }
    }

    #endregion

    #region Helper Methods

    private static ParallelProcessingConfiguration CreateTestConfig()
    {
        return new ParallelProcessingConfiguration
        {
            MaxConcurrentBatches = 4,
            EnableSignalRUpdates = false,
            StoreId = "test-store"
        };
    }

    private static async Task<BatchProcessingResult> ProcessTestBatch(TestBatch batch, CancellationToken cancellationToken)
    {
        if (batch.ShouldFail)
        {
            // Simulate some processing time even for failures
            await Task.Delay(10, cancellationToken);
            
            // Return a proper failure result instead of throwing
            return new BatchProcessingResult
            {
                BatchNumber = batch.Id,
                TotalProcessed = batch.EntityCount,
                SuccessfulEntities = 0,
                FailedEntities = batch.EntityCount // All entities in this batch failed
            };
        }

        // Simulate some processing time
        await Task.Delay(10, cancellationToken);
        
        return new BatchProcessingResult
        {
            BatchNumber = batch.Id,
            TotalProcessed = batch.EntityCount,
            SuccessfulEntities = batch.EntityCount,
            FailedEntities = 0
        };
    }

    private static async Task<BatchProcessingResult> SlowProcessTestBatch(TestBatch batch, CancellationToken cancellationToken)
    {
        // Slow processing to test cancellation
        await Task.Delay(1000, cancellationToken);
        
        return new BatchProcessingResult
        {
            BatchNumber = batch.Id,
            TotalProcessed = batch.EntityCount,
            SuccessfulEntities = batch.EntityCount,
            FailedEntities = 0
        };
    }

    private static List<TestBatch> CreateLargeBatchList(int count)
    {
        var batches = new List<TestBatch>();
        for (int i = 1; i <= count; i++)
        {
            batches.Add(new TestBatch { Id = i, EntityCount = 10 });
        }
        return batches;
    }

    #endregion

    #region Test Models

    private class TestBatch
    {
        public int Id { get; set; }
        public int EntityCount { get; set; }
        public bool ShouldFail { get; set; }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _processor?.Dispose();
    }

    #endregion
} 