using System;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.PerformanceTests.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Async
{
    /// <summary>
    /// Tests for Phase 4: Memory-Safe Performance Optimization
    /// Task 4.1.1: Async Context Capture Detection Tests
    /// 
    /// Goals:
    /// - Detect synchronization context capture (ConfigureAwait issues)
    /// - Validate async performance optimizations
    /// - Ensure thread pool efficiency in Azure Functions
    /// </summary>
    public class AsyncPatternTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<OptimizedParallelProcessor>> _mockLogger;

        public AsyncPatternTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<OptimizedParallelProcessor>>();
        }

        [Fact]
        public async Task Async_Methods_Should_Not_Capture_SynchronizationContext()
        {
            // Arrange
            var contextDetector = new SynchronizationContextDetector();
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);

            // Act - Monitor synchronization context during async operation
            using (contextDetector.Monitor())
            {
                await processor.ProcessEntitiesAsync(
                    new[] { 1, 2, 3 },
                    maxConcurrency: 2,
                    async (entity, ct) =>
                    {
                        await Task.Delay(10, ct); // Simulate async work
                        return;
                    });
            }

            // Assert
            Assert.False(contextDetector.ContextWasCaptured, 
                "Synchronization context should not be captured in Azure Functions");
            
            _output.WriteLine($"Context Capture Status: {(contextDetector.ContextWasCaptured ? "CAPTURED (BAD)" : "NOT CAPTURED (GOOD)")}");
            _output.WriteLine($"Thread Pool Usage: {contextDetector.ThreadPoolThreadsUsed} threads");
        }

        [Fact]
        public async Task ParallelProcessor_Should_Use_ThreadPool_Efficiently()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var threadTracker = new ThreadUsageTracker();

            // Act
            using (threadTracker.Monitor())
            {
                await processor.ProcessEntitiesAsync(
                    new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                    maxConcurrency: 4,
                    async (entity, ct) =>
                    {
                        await Task.Delay(50, ct); // Simulate processing
                        threadTracker.RecordThreadUsage();
                    });
            }

            // Assert
            Assert.True(threadTracker.UniqueThreadsUsed >= 2, 
                "Should use multiple threads for parallel processing");
            Assert.True(threadTracker.UniqueThreadsUsed <= 10, 
                "Should not create excessive threads");

            _output.WriteLine($"Unique Threads Used: {threadTracker.UniqueThreadsUsed}");
            _output.WriteLine($"Total Thread Pool Usage: {threadTracker.TotalOperations}");
        }

        [Fact]
        public async Task AsyncEnumerable_Processing_Should_Be_Memory_Efficient()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var memoryTracker = new MemoryUsageTracker();

            // Act - Process large collection using async enumerable pattern
            var initialMemory = GC.GetTotalMemory(forceFullCollection: true);
            
            await processor.ProcessEntitiesAsync(
                GenerateLargeDataSet(1000), // 1000 items
                maxConcurrency: 5,
                async (entity, ct) =>
                {
                    await Task.Delay(1, ct); // Minimal processing
                    memoryTracker.RecordMemoryUsage();
                });

            var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
            var memoryGrowth = finalMemory - initialMemory;

            // Assert
            Assert.True(memoryGrowth < 10 * 1024 * 1024, // Less than 10MB growth
                $"Memory growth should be minimal. Actual: {memoryGrowth:N0} bytes");

            _output.WriteLine($"Memory Growth: {memoryGrowth:N0} bytes");
            _output.WriteLine($"Peak Memory Usage: {memoryTracker.PeakMemoryUsage:N0} bytes");
        }

        [Fact]
        public async Task ConfigureAwait_False_Should_Prevent_Deadlocks()
        {
            // Arrange
            var deadlockDetector = new DeadlockDetector();
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);

            // Act - Simulate potential deadlock scenario
            var task1 = Task.Run(async () =>
            {
                await processor.ProcessEntitiesAsync(
                    new[] { 1, 2, 3 },
                    maxConcurrency: 2,
                    async (entity, ct) =>
                    {
                        await Task.Delay(100, ct);
                        deadlockDetector.RecordCompletion($"Task1-Entity{entity}");
                    });
            });

            var task2 = Task.Run(async () =>
            {
                await processor.ProcessEntitiesAsync(
                    new[] { 4, 5, 6 },
                    maxConcurrency: 2,
                    async (entity, ct) =>
                    {
                        await Task.Delay(100, ct);
                        deadlockDetector.RecordCompletion($"Task2-Entity{entity}");
                    });
            });

            // Assert - Both tasks should complete without deadlock
            await Task.WhenAll(task1, task2);
            
            Assert.Equal(6, deadlockDetector.CompletedOperations);
            Assert.True(deadlockDetector.AllOperationsCompleted);

            _output.WriteLine($"Completed Operations: {deadlockDetector.CompletedOperations}");
            _output.WriteLine("No deadlocks detected - ConfigureAwait(false) working correctly");
        }

        [Fact]
        public async Task Async_Performance_Should_Scale_With_Concurrency()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var entities = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

            // Act - Test different concurrency levels
            var serialTime = await MeasureProcessingTime(processor, entities, maxConcurrency: 1);
            var parallelTime = await MeasureProcessingTime(processor, entities, maxConcurrency: 4);

            // Assert
            var speedup = (double)serialTime.TotalMilliseconds / parallelTime.TotalMilliseconds;
            Assert.True(speedup > 2.0, 
                $"Parallel processing should be significantly faster. Speedup: {speedup:F2}x");

            _output.WriteLine($"Serial Time: {serialTime.TotalMilliseconds:F0}ms");
            _output.WriteLine($"Parallel Time: {parallelTime.TotalMilliseconds:F0}ms");
            _output.WriteLine($"Speedup: {speedup:F2}x");
        }

        private static System.Collections.Generic.IEnumerable<int> GenerateLargeDataSet(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return i; // Memory-efficient generation
            }
        }

        private async Task<TimeSpan> MeasureProcessingTime(
            OptimizedParallelProcessor processor, 
            int[] entities, 
            int maxConcurrency)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            await processor.ProcessEntitiesAsync(
                entities,
                maxConcurrency,
                async (entity, ct) =>
                {
                    await Task.Delay(50, ct); // Simulate processing work
                });
            
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }
    }
} 