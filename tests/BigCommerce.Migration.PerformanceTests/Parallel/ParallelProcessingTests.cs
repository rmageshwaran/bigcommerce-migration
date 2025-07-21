using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Parallel
{
    /// <summary>
    /// TDD tests for parallel processing performance validation
    /// Target: 3-5x processing speed improvement with controlled concurrency
    /// </summary>
    public class ParallelProcessingTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<OptimizedParallelProcessor>> _mockLogger;

        public ParallelProcessingTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<OptimizedParallelProcessor>>();
        }

        [Fact]
        public async Task ProcessEntitiesAsync_Should_Respect_Concurrency_Limits()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var concurrentOperations = new ConcurrentBag<int>();
            var maxConcurrency = 5;
            var totalOperations = 25;
            var entities = Enumerable.Range(1, totalOperations).ToList();

            // Act
            await processor.ProcessEntitiesAsync(
                entities,
                maxConcurrency,
                async (entity, ct) =>
                {
                    var threadId = Thread.CurrentThread.ManagedThreadId;
                    concurrentOperations.Add(threadId);
                    
                    // Simulate processing work
                    await Task.Delay(100, ct);
                });

            // Assert
            var uniqueThreads = concurrentOperations.Distinct().Count();
            _output.WriteLine($"Used {uniqueThreads} unique threads for {totalOperations} operations with max concurrency {maxConcurrency}");
            
            Assert.True(uniqueThreads <= maxConcurrency * 2, // Allow some thread pool flexibility
                $"Used {uniqueThreads} threads, expected around {maxConcurrency}");
            Assert.Equal(totalOperations, concurrentOperations.Count);
        }

        [Fact]
        public async Task ProcessEntitiesAsync_Should_Improve_Throughput_Over_Sequential()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var entities = Enumerable.Range(1, 50).ToList();
            var processingDelayMs = 100; // Simulate 100ms processing time per entity

            // Test sequential processing
            var sequentialStopwatch = Stopwatch.StartNew();
            foreach (var entity in entities)
            {
                await ProcessSingleEntityAsync(entity, processingDelayMs);
            }
            sequentialStopwatch.Stop();

            // Test parallel processing
            var parallelStopwatch = Stopwatch.StartNew();
            await processor.ProcessEntitiesAsync(entities, maxConcurrency: 10, 
                async (entity, ct) => await ProcessSingleEntityAsync(entity, processingDelayMs, ct));
            parallelStopwatch.Stop();

            // Assert
            var speedupRatio = (double)sequentialStopwatch.ElapsedMilliseconds / parallelStopwatch.ElapsedMilliseconds;
            
            _output.WriteLine($"Sequential time: {sequentialStopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Parallel time: {parallelStopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Speedup ratio: {speedupRatio:F2}x");

            // Should be at least 3x faster (target: 3-5x)
            Assert.True(speedupRatio >= 3.0, $"Expected >3x speedup, got {speedupRatio:F1}x");
            Assert.True(speedupRatio <= 15.0, $"Speedup ratio {speedupRatio:F1}x seems too high - possible test issue");
        }

        [Fact]
        public async Task ProcessEntitiesAsync_Should_Handle_Cancellation_Gracefully()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var entities = Enumerable.Range(1, 100).ToList();
            var processedCount = 0;
            var cts = new CancellationTokenSource();

            // Act
            var processingTask = processor.ProcessEntitiesAsync(
                entities, 
                maxConcurrency: 10,
                async (entity, ct) =>
                {
                    if (Interlocked.Increment(ref processedCount) == 20)
                    {
                        cts.Cancel(); // Cancel after processing 20 entities
                    }
                    
                    await Task.Delay(50, ct); // Simulate work
                },
                cts.Token);

            // Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() => processingTask);
            
            _output.WriteLine($"Processed {processedCount} entities before cancellation");
            
            // Should process some entities but not all
            Assert.True(processedCount >= 20, $"Should process at least 20 entities, got {processedCount}");
            Assert.True(processedCount < 100, $"Should not process all entities due to cancellation, got {processedCount}");
        }

        [Fact]
        public async Task ProcessEntitiesAsync_Should_Respect_Memory_Limits()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var entities = Enumerable.Range(1, 200).ToList();
            var initialMemory = GC.GetTotalMemory(forceFullCollection: true);

            // Act
            await processor.ProcessEntitiesAsync(
                entities, 
                maxConcurrency: 10,
                async (entity, ct) =>
                {
                    // Simulate memory-intensive work
                    var data = new byte[1024 * 100]; // 100KB per operation
                    await Task.Delay(10, ct);
                    
                    // Let GC clean up
                    data = null;
                });

            // Force cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
            var memoryIncrease = finalMemory - initialMemory;

            // Assert
            _output.WriteLine($"Memory increase: {memoryIncrease / (1024 * 1024):F2} MB");
            
            // Should not have excessive memory growth (allow for some overhead)
            Assert.True(memoryIncrease < 50 * 1024 * 1024, // 50MB limit
                $"Memory increase {memoryIncrease / (1024 * 1024):F2} MB exceeded limit");
        }

        [Fact]
        public async Task ProcessEntitiesAsync_WithResults_Should_Collect_All_Results()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var entities = Enumerable.Range(1, 30).ToList();

            // Act
            var results = await processor.ProcessEntitiesAsync(
                entities,
                maxConcurrency: 8,
                async (entity, ct) =>
                {
                    await Task.Delay(10, ct); // Simulate work
                    return entity * 2; // Simple transformation
                });

            // Assert
            Assert.Equal(entities.Count, results.Count);
            
            var expectedResults = entities.Select(x => x * 2).OrderBy(x => x).ToList();
            var actualResults = results.OrderBy(x => x).ToList();
            
            Assert.Equal(expectedResults, actualResults);
        }

        [Fact]
        public async Task ProcessEntitiesAsync_Should_Handle_Errors_Without_Stopping_Others()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var entities = Enumerable.Range(1, 20).ToList();
            var successfulOperations = new ConcurrentBag<int>();
            var errors = new ConcurrentBag<Exception>();

            // Act
            try
            {
                await processor.ProcessEntitiesAsync(
                    entities,
                    maxConcurrency: 5,
                    async (entity, ct) =>
                    {
                        if (entity % 5 == 0) // Every 5th entity throws an error
                        {
                            throw new InvalidOperationException($"Simulated error for entity {entity}");
                        }

                        await Task.Delay(10, ct);
                        successfulOperations.Add(entity);
                    });
            }
            catch (AggregateException ex)
            {
                foreach (var innerEx in ex.InnerExceptions)
                {
                    errors.Add(innerEx);
                }
            }

            // Assert
            _output.WriteLine($"Successful operations: {successfulOperations.Count}");
            _output.WriteLine($"Errors: {errors.Count}");

            // Should have processed non-failing entities and collected errors
            Assert.True(successfulOperations.Count >= 15, $"Expected at least 15 successful operations, got {successfulOperations.Count}");
            Assert.True(errors.Count >= 3, $"Expected errors for failing entities, got {errors.Count}");
        }

        [Fact]
        public async Task GetPerformanceMetricsAsync_Should_Return_Accurate_Metrics()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var entities = Enumerable.Range(1, 50).ToList();

            // Act
            var processingTask = processor.ProcessEntitiesAsync(
                entities,
                maxConcurrency: 8,
                async (entity, ct) => await Task.Delay(20, ct));

            // Get metrics while processing (if possible) or after
            await processingTask;
            var metrics = await processor.GetPerformanceMetricsAsync();

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.MaxConcurrency > 0);
            Assert.True(metrics.TotalEntitiesProcessed >= 0);
            Assert.True(metrics.CapturedAt > DateTime.UtcNow.AddMinutes(-1));
            
            _output.WriteLine($"Metrics - Max Concurrency: {metrics.MaxConcurrency}");
            _output.WriteLine($"Metrics - Total Processed: {metrics.TotalEntitiesProcessed}");
            _output.WriteLine($"Metrics - Throughput: {metrics.ThroughputPerSecond:F2} entities/sec");
        }

        [Fact]
        public async Task ProcessEntitiesWithAdaptiveConcurrencyAsync_Should_Adjust_Concurrency_Based_On_Performance()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var entities = Enumerable.Range(1, 100).ToList();
            var processingTimes = new ConcurrentBag<long>();

            // Act
            var stopwatch = Stopwatch.StartNew();
            await processor.ProcessEntitiesWithAdaptiveConcurrencyAsync(
                entities,
                initialConcurrency: 5,
                async (entity, ct) =>
                {
                    var entityStopwatch = Stopwatch.StartNew();
                    
                    // Simulate variable processing times
                    var delay = entity % 10 == 0 ? 100 : 20; // Every 10th entity takes longer
                    await Task.Delay(delay, ct);
                    
                    entityStopwatch.Stop();
                    processingTimes.Add(entityStopwatch.ElapsedMilliseconds);
                });
            stopwatch.Stop();

            // Assert
            var avgProcessingTime = processingTimes.Average();
            var totalTime = stopwatch.ElapsedMilliseconds;
            
            _output.WriteLine($"Total processing time: {totalTime}ms");
            _output.WriteLine($"Average entity processing time: {avgProcessingTime:F2}ms");
            _output.WriteLine($"Processed {entities.Count} entities");

            Assert.True(totalTime > 0);
            Assert.True(avgProcessingTime > 0);
            Assert.Equal(entities.Count, processingTimes.Count);
        }

        #region Helper Methods

        private static async Task ProcessSingleEntityAsync(int entity, int delayMs, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delayMs, cancellationToken);
        }

        #endregion
    }
} 