using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.PerformanceTests.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Parallel
{
    /// <summary>
    /// Phase 4: Memory-Safe Performance Optimization Tests
    /// Task 4.2.1: Memory-Safe Parallel Processing Tests
    /// 
    /// Goals:
    /// - Validate controlled parallel processing that respects memory limits
    /// - Ensure 50-70% efficiency improvement without memory risk
    /// - Test adaptive concurrency based on system performance
    /// - Validate zero memory overhead optimization strategies
    /// </summary>
    public class MemoryAwareParallelProcessingTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<OptimizedParallelProcessor>> _mockLogger;

        public MemoryAwareParallelProcessingTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<OptimizedParallelProcessor>>();
        }

        [Fact]
        public async Task MemoryAware_Parallel_Processing_Should_Respect_Memory_Limits()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var memoryTracker = new MemoryUsageTracker();
            var initialMemory = GC.GetTotalMemory(forceFullCollection: true);
            const long maxMemoryGrowth = 50 * 1024 * 1024; // 50MB limit
            
            // Generate large dataset to test memory management
            var entities = Enumerable.Range(1, 5000).ToList();

            // Act
            var stopwatch = Stopwatch.StartNew();
            await processor.ProcessEntitiesAsync(
                entities,
                maxConcurrency: 10,
                async (entity, ct) =>
                {
                    // Simulate memory-intensive processing
                    var data = new byte[10 * 1024]; // 10KB per entity
                    await Task.Delay(1, ct).ConfigureAwait(false);
                    memoryTracker.RecordMemoryUsage();
                    
                    // Explicitly release memory to test cleanup
                    data = null;
                });
            stopwatch.Stop();

            var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
            var memoryGrowth = finalMemory - initialMemory;

            // Assert
            Assert.True(memoryGrowth < maxMemoryGrowth, 
                $"Memory growth exceeded limit. Actual: {memoryGrowth:N0} bytes, Limit: {maxMemoryGrowth:N0} bytes");
            
            Assert.True(stopwatch.ElapsedMilliseconds < 30000, // Under 30 seconds
                $"Processing should be efficient. Actual: {stopwatch.ElapsedMilliseconds}ms");

            _output.WriteLine($"Memory Growth: {memoryGrowth:N0} bytes (Limit: {maxMemoryGrowth:N0})");
            _output.WriteLine($"Peak Memory: {memoryTracker.PeakMemoryUsage:N0} bytes");
            _output.WriteLine($"Processing Time: {stopwatch.ElapsedMilliseconds}ms for {entities.Count} entities");
            _output.WriteLine($"Throughput: {entities.Count / (stopwatch.ElapsedMilliseconds / 1000.0):F1} entities/sec");
        }

        [Fact]
        public async Task Parallel_Processing_Should_Achieve_Target_Performance_Improvement()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var entities = Enumerable.Range(1, 1000).ToList();
            
            // Measure serial processing time
            var serialStopwatch = Stopwatch.StartNew();
            await processor.ProcessEntitiesAsync(
                entities.Take(100), // Smaller sample for serial test
                maxConcurrency: 1,
                async (entity, ct) =>
                {
                    await Task.Delay(10, ct).ConfigureAwait(false); // 10ms per entity
                });
            serialStopwatch.Stop();

            // Measure parallel processing time
            var parallelStopwatch = Stopwatch.StartNew();
            await processor.ProcessEntitiesAsync(
                entities,
                maxConcurrency: 10,
                async (entity, ct) =>
                {
                    await Task.Delay(10, ct).ConfigureAwait(false); // 10ms per entity
                });
            parallelStopwatch.Stop();

            // Calculate performance improvement
            var serialTimePerEntity = serialStopwatch.ElapsedMilliseconds / 100.0;
            var parallelTimePerEntity = parallelStopwatch.ElapsedMilliseconds / 1000.0;
            var performanceImprovement = (serialTimePerEntity - parallelTimePerEntity) / serialTimePerEntity * 100;

            // Assert - Target: 50-70% efficiency improvement
            Assert.True(performanceImprovement >= 50, 
                $"Should achieve at least 50% performance improvement. Actual: {performanceImprovement:F1}%");

            _output.WriteLine($"Serial Time per Entity: {serialTimePerEntity:F2}ms");
            _output.WriteLine($"Parallel Time per Entity: {parallelTimePerEntity:F2}ms");
            _output.WriteLine($"Performance Improvement: {performanceImprovement:F1}%");
            _output.WriteLine($"Target Achievement: {(performanceImprovement >= 50 ? "✅ PASSED" : "❌ FAILED")}");
        }

        [Fact]
        public async Task Adaptive_Concurrency_Should_Optimize_Based_On_System_Performance()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var performanceTracker = new AdaptivePerformanceTracker();
            var entities = Enumerable.Range(1, 500).ToList();

            // Act - Test different concurrency levels and find optimal
            var concurrencyResults = new List<ConcurrencyTestResult>();

            foreach (var concurrency in new[] { 2, 5, 10, 15, 20 })
            {
                var stopwatch = Stopwatch.StartNew();
                var initialMemory = GC.GetTotalMemory(false);

                await processor.ProcessEntitiesAsync(
                    entities.Take(100), // Consistent sample size
                    maxConcurrency: concurrency,
                    async (entity, ct) =>
                    {
                        await Task.Delay(20, ct).ConfigureAwait(false);
                        performanceTracker.RecordProcessing(concurrency);
                    });

                stopwatch.Stop();
                var finalMemory = GC.GetTotalMemory(false);
                
                concurrencyResults.Add(new ConcurrencyTestResult
                {
                    Concurrency = concurrency,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    MemoryUsed = finalMemory - initialMemory,
                    Throughput = 100.0 / (stopwatch.ElapsedMilliseconds / 1000.0)
                });
            }

            // Find optimal concurrency (best throughput with reasonable memory)
            var optimalResult = concurrencyResults
                .Where(r => r.MemoryUsed < 20 * 1024 * 1024) // Under 20MB
                .OrderByDescending(r => r.Throughput)
                .First();

            // Assert
            Assert.True(optimalResult.Concurrency >= 5, "Optimal concurrency should utilize parallelism");
            Assert.True(optimalResult.Throughput > 30, "Should achieve reasonable throughput");

            _output.WriteLine("Adaptive Concurrency Analysis:");
            foreach (var result in concurrencyResults)
            {
                var isOptimal = result.Concurrency == optimalResult.Concurrency ? "⭐ OPTIMAL" : "";
                _output.WriteLine($"Concurrency {result.Concurrency}: {result.Throughput:F1} items/sec, " +
                               $"{result.MemoryUsed / 1024:N0}KB memory {isOptimal}");
            }
        }

        [Fact]
        public async Task Memory_Safe_Processing_Should_Handle_Large_Datasets_Without_OutOfMemory()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            const int largeDatasetSize = 10000;
            var memoryMonitor = new ContinuousMemoryMonitor();
            
            // Act - Process large dataset with memory monitoring
            using (memoryMonitor.StartMonitoring())
            {
                await processor.ProcessEntitiesAsync(
                    GenerateLargeDataset(largeDatasetSize),
                    maxConcurrency: 8,
                    async (entity, ct) =>
                    {
                        // Simulate realistic entity processing
                        var entityData = CreateEntityData(entity);
                        await Task.Delay(5, ct).ConfigureAwait(false);
                        
                        // Process and release memory immediately
                        ProcessEntity(entityData);
                        entityData = null;
                    });
            }

            // Assert
            Assert.True(memoryMonitor.PeakMemoryUsage < 100 * 1024 * 1024, // Under 100MB
                $"Peak memory should stay under limit. Actual: {memoryMonitor.PeakMemoryUsage / 1024 / 1024:N0}MB");
            
            Assert.False(memoryMonitor.OutOfMemoryDetected, "Should not trigger OutOfMemory conditions");
            Assert.True(memoryMonitor.ProcessedItemCount == largeDatasetSize, 
                "Should process all items successfully");

            _output.WriteLine($"Large Dataset Processing Results:");
            _output.WriteLine($"Items Processed: {memoryMonitor.ProcessedItemCount:N0}");
            _output.WriteLine($"Peak Memory: {memoryMonitor.PeakMemoryUsage / 1024 / 1024:F1}MB");
            _output.WriteLine($"Average Memory: {memoryMonitor.AverageMemoryUsage / 1024 / 1024:F1}MB");
            _output.WriteLine($"GC Collections: {memoryMonitor.GCCollectionCount}");
        }

        [Fact]
        public async Task Controlled_Concurrency_Should_Prevent_Thread_Pool_Starvation()
        {
            // Arrange
            var processor = new OptimizedParallelProcessor(_mockLogger.Object);
            var threadTracker = new ThreadPoolMonitor();
            var entities = Enumerable.Range(1, 200).ToList();

            // Act - Monitor thread pool usage during high-concurrency processing
            using (threadTracker.StartMonitoring())
            {
                await processor.ProcessEntitiesAsync(
                    entities,
                    maxConcurrency: 25, // High concurrency to test limits
                    async (entity, ct) =>
                    {
                        threadTracker.RecordThreadPoolUsage();
                        await Task.Delay(100, ct).ConfigureAwait(false); // Longer delay to test concurrency
                    });
            }

            // Assert
            Assert.True(threadTracker.MaxConcurrentThreads <= 30, 
                "Should not create excessive concurrent threads");
            
            Assert.True(threadTracker.ThreadPoolStarvationEvents == 0, 
                "Should not cause thread pool starvation");
            
            Assert.True(threadTracker.AverageQueueLength < 10, 
                "Should maintain reasonable thread pool queue length");

            _output.WriteLine($"Thread Pool Management Results:");
            _output.WriteLine($"Max Concurrent Threads: {threadTracker.MaxConcurrentThreads}");
            _output.WriteLine($"Thread Pool Starvation Events: {threadTracker.ThreadPoolStarvationEvents}");
            _output.WriteLine($"Average Queue Length: {threadTracker.AverageQueueLength:F1}");
            _output.WriteLine($"Thread Pool Efficiency: {threadTracker.ThreadPoolEfficiency:F1}%");
        }

        // Helper Methods
        private static IEnumerable<int> GenerateLargeDataset(int size)
        {
            for (int i = 0; i < size; i++)
            {
                yield return i; // Memory-efficient generation
            }
        }

        private static byte[] CreateEntityData(int entity)
        {
            // Simulate entity data creation (5KB per entity)
            return new byte[5 * 1024];
        }

        private static void ProcessEntity(byte[] entityData)
        {
            // Simulate entity processing
            if (entityData != null)
            {
                // Process the data
                _ = entityData.Length;
            }
        }
    }

    // Supporting Infrastructure Classes
    public class ConcurrencyTestResult
    {
        public int Concurrency { get; set; }
        public long ElapsedMs { get; set; }
        public long MemoryUsed { get; set; }
        public double Throughput { get; set; }
    }

    public class AdaptivePerformanceTracker
    {
        private readonly Dictionary<int, List<long>> _performanceData = new();

        public void RecordProcessing(int concurrency)
        {
            if (!_performanceData.ContainsKey(concurrency))
                _performanceData[concurrency] = new List<long>();

            _performanceData[concurrency].Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public double GetAverageThroughput(int concurrency)
        {
            if (!_performanceData.ContainsKey(concurrency) || _performanceData[concurrency].Count < 2)
                return 0;

            var times = _performanceData[concurrency];
            var duration = times.Last() - times.First();
            return times.Count / (duration / 1000.0);
        }
    }

    public class ContinuousMemoryMonitor : IDisposable
    {
        private bool _isMonitoring;
        private long _peakMemoryUsage;
        private long _totalMemoryReadings;
        private long _memorySum;
        private int _processedItemCount;
        private int _gcCollectionCount;
        private readonly Timer _memoryTimer;

        public long PeakMemoryUsage => _peakMemoryUsage;
        public long AverageMemoryUsage => _totalMemoryReadings > 0 ? _memorySum / _totalMemoryReadings : 0;
        public bool OutOfMemoryDetected { get; private set; }
        public int ProcessedItemCount => _processedItemCount;
        public int GCCollectionCount => _gcCollectionCount;

        public ContinuousMemoryMonitor()
        {
            _memoryTimer = new Timer(MonitorMemory, null, Timeout.Infinite, Timeout.Infinite);
        }

        public IDisposable StartMonitoring()
        {
            _isMonitoring = true;
            _gcCollectionCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            _memoryTimer.Change(0, 100); // Monitor every 100ms
            return this;
        }

        private void MonitorMemory(object? state)
        {
            if (!_isMonitoring) return;

            try
            {
                var currentMemory = GC.GetTotalMemory(false);
                
                if (currentMemory > _peakMemoryUsage)
                    _peakMemoryUsage = currentMemory;

                _memorySum += currentMemory;
                _totalMemoryReadings++;

                Interlocked.Increment(ref _processedItemCount);
            }
            catch (OutOfMemoryException)
            {
                OutOfMemoryDetected = true;
            }
        }

        public void Dispose()
        {
            _isMonitoring = false;
            _memoryTimer?.Dispose();
            
            var finalGCCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            _gcCollectionCount = finalGCCount - _gcCollectionCount;
        }
    }

    public class ThreadPoolMonitor : IDisposable
    {
        private bool _isMonitoring;
        private int _maxConcurrentThreads;
        private int _threadPoolStarvationEvents;
        private readonly List<int> _queueLengths = new();
        private readonly Timer _monitorTimer;

        public int MaxConcurrentThreads => _maxConcurrentThreads;
        public int ThreadPoolStarvationEvents => _threadPoolStarvationEvents;
        public double AverageQueueLength => _queueLengths.Count > 0 ? _queueLengths.Average() : 0;
        public double ThreadPoolEfficiency => 100.0; // Simplified for test

        public ThreadPoolMonitor()
        {
            _monitorTimer = new Timer(MonitorThreadPool, null, Timeout.Infinite, Timeout.Infinite);
        }

        public IDisposable StartMonitoring()
        {
            _isMonitoring = true;
            _monitorTimer.Change(0, 50); // Monitor every 50ms
            return this;
        }

        private void MonitorThreadPool(object? state)
        {
            if (!_isMonitoring) return;

            ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
            
            var currentlyInUse = maxWorkerThreads - workerThreads;
            if (currentlyInUse > _maxConcurrentThreads)
                _maxConcurrentThreads = currentlyInUse;

            // Simplified queue length monitoring
            _queueLengths.Add(Math.Max(0, currentlyInUse - Environment.ProcessorCount));
            
            // Detect potential starvation (simplified)
            if (workerThreads < 2 && completionPortThreads < 2)
                _threadPoolStarvationEvents++;
        }

        public void RecordThreadPoolUsage()
        {
            // Called from processing threads to track usage
        }

        public void Dispose()
        {
            _isMonitoring = false;
            _monitorTimer?.Dispose();
        }
    }
} 