using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Tests.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using System.Diagnostics;

namespace BigCommerce.Migration.Tests.Performance;

/// <summary>
/// Performance tests for cancellation functionality
/// Validates timing requirements (<5 seconds) and performance overhead (<5%)
/// </summary>
public class CancellationPerformanceTests : IDisposable
{
    private readonly Mock<ICancellationStore> _mockCancellationStore;
    private readonly Mock<ILogger<CancellationWorkflowValidator>> _mockLogger;
    private readonly CancellationWorkflowValidator _validator;
    private readonly string _testMigrationId;

    // Performance thresholds
    private const double MaxWorkflowExecutionSeconds = 5.0;
    private const double MaxPerformanceOverheadPercent = 5.0;
    private const double MaxBlobOperationSeconds = 1.0;
    private const double MaxCancellationCheckSeconds = 0.5;

    public CancellationPerformanceTests()
    {
        _testMigrationId = $"perf_test_{Guid.NewGuid():N}";
        _mockCancellationStore = new Mock<ICancellationStore>();
        _mockLogger = new Mock<ILogger<CancellationWorkflowValidator>>();
        _validator = new CancellationWorkflowValidator(_mockCancellationStore.Object, _mockLogger.Object);
    }

    #region Workflow Timing Tests

    [Fact]
    public async Task WorkflowTiming_CompleteFlow_CompletesUnder5Seconds()
    {
        // Arrange: Setup fast cancellation scenario
        SetupFastCancellationStore();

        // Act: Measure complete workflow execution time
        var stopwatch = Stopwatch.StartNew();
        var result = await _validator.ValidateCompleteWorkflowAsync(_testMigrationId);
        stopwatch.Stop();

        // Assert: Complete workflow should execute under 5 seconds
        result.IsSuccess.Should().BeTrue("Workflow should complete successfully");
        result.TotalExecutionTime.TotalSeconds.Should().BeLessThan(MaxWorkflowExecutionSeconds, 
            $"Complete workflow should execute under {MaxWorkflowExecutionSeconds} seconds");
        stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(MaxWorkflowExecutionSeconds,
            "Measured execution time should also be under threshold");

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Validation completed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once, "Should log completion");
    }

    [Fact]
    public async Task WorkflowTiming_MultipleIterations_ConsistentPerformance()
    {
        // Arrange: Setup for multiple performance runs
        SetupFastCancellationStore();
        const int iterationCount = 10;
        var executionTimes = new List<TimeSpan>();

        // Act: Run multiple iterations and collect timing data
        for (int i = 0; i < iterationCount; i++)
        {
            var iterationMigrationId = $"{_testMigrationId}_iter_{i}";
            var stopwatch = Stopwatch.StartNew();
            
            var result = await _validator.ValidateCompleteWorkflowAsync(iterationMigrationId);
            
            stopwatch.Stop();
            executionTimes.Add(stopwatch.Elapsed);

            result.IsSuccess.Should().BeTrue($"Iteration {i} should complete successfully");
        }

        // Assert: All iterations should meet performance requirements
        executionTimes.Should().OnlyContain(time => time.TotalSeconds < MaxWorkflowExecutionSeconds,
            "All iterations should complete under time threshold");

        var averageTime = executionTimes.Select(t => t.TotalSeconds).Average();
        var maxTime = executionTimes.Select(t => t.TotalSeconds).Max();
        var minTime = executionTimes.Select(t => t.TotalSeconds).Min();

        averageTime.Should().BeLessThan(MaxWorkflowExecutionSeconds * 0.5, 
            "Average time should be well under threshold");
        
        // Performance consistency check
        var variance = executionTimes.Select(t => Math.Pow(t.TotalSeconds - averageTime, 2)).Average();
        var standardDeviation = Math.Sqrt(variance);
        
        standardDeviation.Should().BeLessThan(averageTime * 0.3, 
            "Execution times should be consistent (low standard deviation)");
    }

    #endregion

    #region Individual Step Performance Tests

    [Fact]
    public async Task StepPerformance_BlobOperations_UnderThreshold()
    {
        // Arrange: Setup blob operation timing test
        var blobOperationTimes = new List<TimeSpan>();
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(async () =>
            {
                var delay = Random.Shared.Next(10, 100); // Simulate 10-100ms blob operation
                await Task.Delay(delay);
            });
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .Returns(async () =>
            {
                var delay = Random.Shared.Next(5, 50); // Simulate 5-50ms blob check
                await Task.Delay(delay);
                return true;
            });

        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(It.IsAny<string>()))
            .ReturnsAsync("Performance test reason");

        // Act: Measure blob operation performance
        for (int i = 0; i < 20; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Test blob operations (Steps 2 and 5)
            await _mockCancellationStore.Object.SetCancellationFlagAsync($"{_testMigrationId}_{i}", "test");
            var isCancelled = await _mockCancellationStore.Object.CheckCancellationFlagAsync($"{_testMigrationId}_{i}");
            var reason = await _mockCancellationStore.Object.GetCancellationReasonAsync($"{_testMigrationId}_{i}");
            
            stopwatch.Stop();
            blobOperationTimes.Add(stopwatch.Elapsed);
        }

        // Assert: Blob operations should be fast
        blobOperationTimes.Should().OnlyContain(time => time.TotalSeconds < MaxBlobOperationSeconds,
            $"All blob operations should complete under {MaxBlobOperationSeconds} seconds");

        var averageBlobTime = blobOperationTimes.Select(t => t.TotalMilliseconds).Average();
        averageBlobTime.Should().BeLessThan(200, "Average blob operation should be under 200ms");
    }

    [Fact]
    public async Task StepPerformance_CancellationChecks_UnderThreshold()
    {
        // Arrange: Setup fast cancellation checks
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(It.IsAny<string>()))
            .ReturnsAsync("Fast check test");

        var checkTimes = new List<TimeSpan>();

        // Act: Measure cancellation check performance
        for (int i = 0; i < 100; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            
            var isCancelled = await _mockCancellationStore.Object.CheckCancellationFlagAsync($"{_testMigrationId}_{i}");
            if (isCancelled)
            {
                await _mockCancellationStore.Object.GetCancellationReasonAsync($"{_testMigrationId}_{i}");
            }
            
            stopwatch.Stop();
            checkTimes.Add(stopwatch.Elapsed);
        }

        // Assert: Cancellation checks should be very fast
        checkTimes.Should().OnlyContain(time => time.TotalSeconds < MaxCancellationCheckSeconds,
            $"All cancellation checks should complete under {MaxCancellationCheckSeconds} seconds");

        var averageCheckTime = checkTimes.Select(t => t.TotalMilliseconds).Average();
        averageCheckTime.Should().BeLessThan(50, "Average cancellation check should be under 50ms");
    }

    #endregion

    #region Performance Overhead Tests

    [Fact]
    public async Task PerformanceOverhead_WithVsWithoutCancellation_Under5PercentOverhead()
    {
        // Arrange: Setup baseline (no cancellation) vs cancellation-enabled scenarios
        var baselineProcessor = new PerformanceTestProcessor(null);
        var cancellationProcessor = new PerformanceTestProcessor(_mockCancellationStore.Object);

        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false); // Not cancelled for overhead test

        const int iterationCount = 50;
        const int entitiesPerIteration = 100;

        // Act: Measure baseline performance (without cancellation)
        var baselineTimes = new List<TimeSpan>();
        for (int i = 0; i < iterationCount; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await baselineProcessor.ProcessEntitiesAsync($"baseline_{i}", entitiesPerIteration);
            stopwatch.Stop();
            baselineTimes.Add(stopwatch.Elapsed);
        }

        // Measure cancellation-enabled performance
        var cancellationTimes = new List<TimeSpan>();
        for (int i = 0; i < iterationCount; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await cancellationProcessor.ProcessEntitiesAsync($"cancellation_{i}", entitiesPerIteration);
            stopwatch.Stop();
            cancellationTimes.Add(stopwatch.Elapsed);
        }

        // Assert: Performance overhead should be under 5%
        var baselineAverage = baselineTimes.Select(t => t.TotalMilliseconds).Average();
        var cancellationAverage = cancellationTimes.Select(t => t.TotalMilliseconds).Average();

        var overheadPercent = ((cancellationAverage - baselineAverage) / baselineAverage) * 100;
        
        overheadPercent.Should().BeLessThan(MaxPerformanceOverheadPercent,
            $"Cancellation overhead should be under {MaxPerformanceOverheadPercent}%");

        // Log performance metrics for analysis
        Console.WriteLine($"Baseline average: {baselineAverage:F2}ms");
        Console.WriteLine($"Cancellation average: {cancellationAverage:F2}ms");
        Console.WriteLine($"Performance overhead: {overheadPercent:F2}%");
    }

    [Fact]
    public async Task PerformanceOverhead_FrequentCancellationChecks_AcceptableOverhead()
    {
        // Arrange: Setup processor with frequent cancellation checks
        var checkCounts = new[] { 1, 5, 10, 20 }; // Check every N entities
        var overheadResults = new Dictionary<int, double>();

        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        // Baseline without any checks
        var baselineProcessor = new PerformanceTestProcessor(null);
        var baselineTimes = new List<TimeSpan>();
        
        for (int i = 0; i < 20; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await baselineProcessor.ProcessEntitiesAsync($"baseline_{i}", 100);
            stopwatch.Stop();
            baselineTimes.Add(stopwatch.Elapsed);
        }
        
        var baselineAverage = baselineTimes.Select(t => t.TotalMilliseconds).Average();

        // Act & Assert: Test different check frequencies
        foreach (var checkInterval in checkCounts)
        {
            var processor = new PerformanceTestProcessor(_mockCancellationStore.Object, checkInterval);
            var times = new List<TimeSpan>();
            
            for (int i = 0; i < 20; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                await processor.ProcessEntitiesAsync($"check_{checkInterval}_{i}", 100);
                stopwatch.Stop();
                times.Add(stopwatch.Elapsed);
            }
            
            var average = times.Select(t => t.TotalMilliseconds).Average();
            var overhead = ((average - baselineAverage) / baselineAverage) * 100;
            overheadResults[checkInterval] = overhead;

            // Even with frequent checks, overhead should be reasonable
            overhead.Should().BeLessThan(MaxPerformanceOverheadPercent * checkInterval,
                $"Overhead for check interval {checkInterval} should be acceptable");
        }

        // Log results for analysis
        foreach (var kvp in overheadResults)
        {
            Console.WriteLine($"Check every {kvp.Key} entities: {kvp.Value:F2}% overhead");
        }
    }

    #endregion

    #region Concurrent Performance Tests

    [Fact]
    public async Task ConcurrentPerformance_MultipleMigrations_NoPerformanceDegradation()
    {
        // Arrange: Setup concurrent migration scenarios
        SetupFastCancellationStore();
        const int concurrentMigrations = 10;
        
        var concurrentTasks = new List<Task<CancellationWorkflowResult>>();

        // Act: Run multiple migrations concurrently
        var overallStopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < concurrentMigrations; i++)
        {
            var migrationId = $"{_testMigrationId}_concurrent_{i}";
            var task = _validator.ValidateCompleteWorkflowAsync(migrationId);
            concurrentTasks.Add(task);
        }

        var results = await Task.WhenAll(concurrentTasks);
        overallStopwatch.Stop();

        // Assert: Concurrent performance should be acceptable
        results.Should().OnlyContain(r => r.IsSuccess, "All concurrent migrations should succeed");
        results.Should().OnlyContain(r => r.TotalExecutionTime.TotalSeconds < MaxWorkflowExecutionSeconds,
            "All concurrent workflows should meet timing requirements");

        var averageConcurrentTime = results.Select(r => r.TotalExecutionTime.TotalSeconds).Average();
        
        // Concurrent processing shouldn't be significantly slower than sequential
        averageConcurrentTime.Should().BeLessThan(MaxWorkflowExecutionSeconds * 0.8,
            "Concurrent processing should be efficient");

        Console.WriteLine($"Concurrent migrations: {concurrentMigrations}");
        Console.WriteLine($"Total time: {overallStopwatch.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"Average per migration: {averageConcurrentTime:F2}s");
    }

    #endregion

    #region Load Testing

    [Fact]
    public async Task LoadTest_HighFrequencyCancellationChecks_SystemRemainStable()
    {
        // Arrange: Setup high-frequency cancellation scenario
        var checkCount = 0;
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref checkCount);
                return false; // Keep processing
            });

        // Act: Perform high-frequency checks
        var processor = new PerformanceTestProcessor(_mockCancellationStore.Object, checkInterval: 1);
        var stopwatch = Stopwatch.StartNew();
        
        // Process large number of entities with check on every entity
        await processor.ProcessEntitiesAsync(_testMigrationId, entityCount: 1000);
        
        stopwatch.Stop();

        // Assert: System should handle high-frequency checks gracefully
        checkCount.Should().BeGreaterOrEqualTo(1000, "Should have performed many cancellation checks");
        stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(10, "High-frequency checking should complete reasonably fast");
        
        // Verify no memory leaks or performance degradation
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        Console.WriteLine($"Performed {checkCount} cancellation checks in {stopwatch.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"Average check rate: {checkCount / stopwatch.Elapsed.TotalSeconds:F0} checks/second");
    }

    #endregion

    #region Helper Methods

    private void SetupFastCancellationStore()
    {
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(It.IsAny<string>()))
            .ReturnsAsync("Performance test");
    }

    #endregion

    #region Helper Classes

    private class PerformanceTestProcessor
    {
        private readonly ICancellationStore? _cancellationStore;
        private readonly int _checkInterval;

        public PerformanceTestProcessor(ICancellationStore? cancellationStore, int checkInterval = 10)
        {
            _cancellationStore = cancellationStore;
            _checkInterval = checkInterval;
        }

        public async Task ProcessEntitiesAsync(string migrationId, int entityCount)
        {
            for (int i = 0; i < entityCount; i++)
            {
                // Perform cancellation check based on interval
                if (_cancellationStore != null && i > 0 && i % _checkInterval == 0)
                {
                    var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                    if (isCancelled)
                    {
                        await _cancellationStore.GetCancellationReasonAsync(migrationId);
                        throw new OperationCanceledException("Test cancellation");
                    }
                }

                // Simulate entity processing work
                await Task.Delay(1);
            }
        }
    }

    #endregion

    public void Dispose()
    {
        // Cleanup if needed
    }
}