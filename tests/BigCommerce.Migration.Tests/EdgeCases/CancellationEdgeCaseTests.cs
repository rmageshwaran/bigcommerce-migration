using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Tests.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using System.Net;

namespace BigCommerce.Migration.Tests.EdgeCases;

/// <summary>
/// Edge case and error tests for cancellation functionality
/// Tests double cancellation, storage failures, race conditions, and other edge scenarios
/// </summary>
public class CancellationEdgeCaseTests : IDisposable
{
    private readonly Mock<ICancellationStore> _mockCancellationStore;
    private readonly Mock<ILogger<CancellationWorkflowValidator>> _mockLogger;
    private readonly CancellationWorkflowValidator _validator;
    private readonly string _testMigrationId;

    public CancellationEdgeCaseTests()
    {
        _testMigrationId = $"edge_test_{Guid.NewGuid():N}";
        _mockCancellationStore = new Mock<ICancellationStore>();
        _mockLogger = new Mock<ILogger<CancellationWorkflowValidator>>();
        _validator = new CancellationWorkflowValidator(_mockCancellationStore.Object, _mockLogger.Object);
    }

    #region Double Cancellation Tests

    [Fact]
    public async Task DoubleCancellation_MultipleCancellationRequests_HandledGracefully()
    {
        // Arrange: Setup double cancellation scenario
        var cancellationCount = 0;
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(_testMigrationId, It.IsAny<string>()))
            .Callback(() => Interlocked.Increment(ref cancellationCount))
            .Returns(Task.CompletedTask);
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(_testMigrationId))
            .ReturnsAsync("Double cancellation test");

        // Act: Perform multiple cancellation requests simultaneously
        var cancellationTasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await _mockCancellationStore.Object.SetCancellationFlagAsync(_testMigrationId, "Concurrent cancellation");
                    return true;
                }
                catch
                {
                    return false;
                }
            }))
            .ToArray();

        var results = await Task.WhenAll(cancellationTasks);

        // Assert: All cancellation requests should succeed or fail gracefully
        results.Should().OnlyContain(result => result == true, "All cancellation requests should be handled gracefully");
        cancellationCount.Should().Be(5, "All cancellation requests should be processed");

        // Verify workflow still works after double cancellation
        var workflowResult = await _validator.ValidateCompleteWorkflowAsync(_testMigrationId);
        workflowResult.IsSuccess.Should().BeTrue("Workflow should still work after multiple cancellation requests");
    }

    [Fact]
    public async Task DoubleCancellation_CancellationAfterCompletion_HandledCorrectly()
    {
        // Arrange: Setup scenario where cancellation is requested after workflow completion
        var isCompleted = false;
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ReturnsAsync(() => isCompleted); // Only cancelled after completion
        
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(_testMigrationId, It.IsAny<string>()))
            .Callback(() => isCompleted = true)
            .Returns(Task.CompletedTask);
        
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(_testMigrationId))
            .ReturnsAsync("Late cancellation test");

        // Act: Complete workflow first, then try to cancel
        var workflowTask = _validator.ValidateCompleteWorkflowAsync(_testMigrationId);
        
        // Simulate late cancellation request
        await Task.Delay(100);
        _ = Task.Run(async () => await _mockCancellationStore.Object.SetCancellationFlagAsync(_testMigrationId, "Too late cancellation"));
        
        var workflowResult = await workflowTask;

        // Assert: Workflow should handle late cancellation gracefully
        workflowResult.Should().NotBeNull("Workflow result should be returned");
        // The workflow might succeed or be cancelled depending on timing, but shouldn't crash
        workflowResult.ErrorMessage.Should().BeNullOrEmpty("No unexpected errors should occur");
    }

    #endregion

    #region Storage Failure Tests

    [Fact]
    public async Task StorageFailure_BlobStorageUnavailable_GracefulFailure()
    {
        // Arrange: Setup blob storage failure scenario
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Blob storage unavailable"));
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Network error"));
        
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(It.IsAny<string>()))
            .ThrowsAsync(new TimeoutException("Storage timeout"));

        // Act: Attempt workflow validation with storage failures
        var workflowResult = await _validator.ValidateCompleteWorkflowAsync(_testMigrationId);

        // Assert: Should fail gracefully with proper error handling
        workflowResult.IsSuccess.Should().BeFalse("Workflow should fail when storage is unavailable");
        workflowResult.ErrorMessage.Should().NotBeNullOrEmpty("Should provide meaningful error message");
        workflowResult.ErrorMessage.Should().Contain("Workflow validation failed", "Should indicate validation failure");
        
        // Verify no unhandled exceptions
        workflowResult.StepResults.Should().NotBeEmpty("Should still attempt to execute some steps");
    }

    [Fact]
    public async Task StorageFailure_IntermittentFailures_PartialRecovery()
    {
        // Arrange: Setup intermittent storage failures
        var failureCount = 0;
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(() =>
            {
                if (++failureCount % 3 == 0) // Fail every 3rd call
                    throw new InvalidOperationException("Intermittent failure");
                return true;
            });
        
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(It.IsAny<string>()))
            .ReturnsAsync("Intermittent failure test");
        
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act: Run cancellation checks with intermittent failures
        var processor = new EdgeCaseTestProcessor(_mockCancellationStore.Object);
        var result = await processor.ProcessWithIntermittentFailuresAsync(_testMigrationId, iterations: 10);

        // Assert: Should handle intermittent failures gracefully
        result.SuccessfulChecks.Should().BeGreaterThan(0, "Some checks should succeed");
        result.FailedChecks.Should().BeGreaterThan(0, "Some checks should fail as expected");
        result.TotalChecks.Should().Be(10, "All iterations should be attempted");
        result.UnhandledExceptions.Should().Be(0, "No unhandled exceptions should occur");
    }

    #endregion

    #region Race Condition Tests

    [Fact]
    public async Task RaceCondition_ConcurrentCancellationAndProcessing_ConsistentBehavior()
    {
        // Arrange: Setup race condition scenario
        var processingStarted = false;
        var cancellationRequested = false;
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ReturnsAsync(() => cancellationRequested);
        
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(_testMigrationId, It.IsAny<string>()))
            .Callback(() => cancellationRequested = true)
            .Returns(Task.CompletedTask);
        
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(_testMigrationId))
            .ReturnsAsync("Race condition test");

        // Act: Start processing and cancellation concurrently
        var processingTask = Task.Run(async () =>
        {
            processingStarted = true;
            var processor = new EdgeCaseTestProcessor(_mockCancellationStore.Object);
            return await processor.ProcessWithRaceConditionAsync(_testMigrationId, processingTimeMs: 200);
        });

        var cancellationTask = Task.Run(async () =>
        {
            await Task.Delay(50); // Start cancellation shortly after processing
            await _mockCancellationStore.Object.SetCancellationFlagAsync(_testMigrationId, "Concurrent cancellation");
        });

        await Task.WhenAll(processingTask, cancellationTask);
        var processingResult = processingTask.Result;

        // Assert: Race condition should be handled consistently
        processingStarted.Should().BeTrue("Processing should have started");
        cancellationRequested.Should().BeTrue("Cancellation should have been requested");
        
        // Either processing completes before cancellation or is cancelled - both are valid
        (processingResult.Completed || processingResult.Cancelled).Should().BeTrue("Should have a consistent final state");
        
        if (processingResult.Cancelled)
        {
            processingResult.CancellationDetected.Should().BeTrue("Should detect cancellation if cancelled");
        }
    }

    [Fact]
    public async Task RaceCondition_MultipleWorkflowsOnSameMigration_IsolatedCorrectly()
    {
        // Arrange: Setup multiple workflows on same migration ID (edge case scenario)
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(_testMigrationId))
            .ReturnsAsync("Multi-workflow test");
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(_testMigrationId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act: Run multiple workflows simultaneously on the same migration
        var workflowTasks = Enumerable.Range(0, 3)
            .Select(_ => _validator.ValidateCompleteWorkflowAsync(_testMigrationId))
            .ToArray();

        var results = await Task.WhenAll(workflowTasks);

        // Assert: All workflows should complete successfully despite sharing the same migration ID
        results.Should().OnlyContain(r => r.IsSuccess, "All workflows should complete successfully");
        results.Should().OnlyContain(r => r.MigrationId == _testMigrationId, "All workflows should reference the same migration");
        
        // Verify no interference between workflows
        var executionTimes = results.Select(r => r.TotalExecutionTime.TotalSeconds).ToArray();
        executionTimes.Should().OnlyContain(time => time < 5, "All workflows should meet timing requirements");
    }

    #endregion

    #region Invalid Input Tests

    [Fact]
    public async Task InvalidInput_NullOrEmptyMigrationId_HandledGracefully()
    {
        // Arrange: Setup for invalid migration ID tests
        var invalidMigrationIds = new[] { null, "", "   ", "\t", "\n" };

        foreach (var invalidId in invalidMigrationIds)
        {
            // Act & Assert: Should handle invalid IDs gracefully
            if (string.IsNullOrWhiteSpace(invalidId))
            {
                var act = () => _validator.ValidateCompleteWorkflowAsync(invalidId!);
                
                // The validator might throw an exception or return a failed result
                // Both behaviors are acceptable as long as the system doesn't crash
                try
                {
                    var result = await act();
                    result.IsSuccess.Should().BeFalse($"Should fail for invalid migration ID: '{invalidId}'");
                }
                catch (ArgumentException)
                {
                    // This is also acceptable behavior for invalid input
                }
                catch (Exception ex)
                {
                    ex.Should().BeOfType<ArgumentException>($"Should throw ArgumentException for invalid ID: '{invalidId}'");
                }
            }
        }
    }

    [Fact]
    public async Task InvalidInput_MalformedMigrationId_HandledCorrectly()
    {
        // Arrange: Setup malformed migration IDs
        var malformedIds = new[]
        {
            "migration/with/slashes",
            "migration:with:colons",
            "migration with spaces",
            "migration\nwith\nnewlines",
            "migration\twith\ttabs",
            new string('x', 1000), // Very long ID
            "migration|with|pipes",
            "migration<with>brackets",
            "migration{with}braces"
        };

        var results = new List<(string Id, bool Success, string? Error)>();

        foreach (var malformedId in malformedIds)
        {
            try
            {
                // Setup mock to accept any ID
                _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(malformedId))
                    .ReturnsAsync(true);
                _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(malformedId))
                    .ReturnsAsync("Malformed ID test");
                _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(malformedId, It.IsAny<string>()))
                    .Returns(Task.CompletedTask);

                // Act: Validate workflow with malformed ID
                var result = await _validator.ValidateCompleteWorkflowAsync(malformedId);
                results.Add((malformedId, result.IsSuccess, result.ErrorMessage));
            }
            catch (Exception ex)
            {
                results.Add((malformedId, false, ex.Message));
            }
        }

        // Assert: System should handle malformed IDs without crashing
        results.Should().NotBeEmpty("Should process all malformed IDs");
        results.Should().OnlyContain(r => !string.IsNullOrEmpty(r.Id), "All test IDs should be processed");
        
        // The system can either accept malformed IDs (and process them) or reject them gracefully
        // The important thing is that it doesn't crash
        foreach (var (id, success, error) in results)
        {
            if (!success && !string.IsNullOrEmpty(error))
            {
                error.Should().NotContain("unhandled", 
                    $"Error for ID '{id}' should be handled gracefully");
            }
        }
    }

    #endregion

    #region Network and Timeout Tests

    [Fact]
    public async Task NetworkFailure_TimeoutDuringBlobOperations_ProperTimeout()
    {
        // Arrange: Setup timeout scenario
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .Returns(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10)); // Simulate long timeout
                return true;
            });
        
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10)); // Simulate long timeout
            });

        // Act: Run with timeout
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var timeoutTask = Task.Run(async () =>
        {
            try
            {
                return await _validator.ValidateCompleteWorkflowAsync(_testMigrationId);
            }
            catch (OperationCanceledException)
            {
                return new CancellationWorkflowResult(_testMigrationId)
                {
                    IsSuccess = false,
                    ErrorMessage = "Operation timed out"
                };
            }
        }, cts.Token);

        CancellationWorkflowResult result;
        try
        {
            result = await timeoutTask;
        }
        catch (OperationCanceledException)
        {
            result = new CancellationWorkflowResult(_testMigrationId)
            {
                IsSuccess = false,
                ErrorMessage = "Test timed out"
            };
        }

        // Assert: Should handle timeouts gracefully
        result.Should().NotBeNull("Should return a result even on timeout");
        
        if (!result.IsSuccess)
        {
            result.ErrorMessage.Should().Contain("timeout",
                "Should indicate timeout in error message");
        }
    }

    #endregion

    #region Resource Exhaustion Tests

    [Fact]
    public async Task ResourceExhaustion_MemoryPressure_GracefulDegradation()
    {
        // Arrange: Setup scenario that could cause memory pressure
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false); // Keep processing to build up memory
        
        var processor = new EdgeCaseTestProcessor(_mockCancellationStore.Object);

        // Act: Process large number of operations
        var memoryBefore = GC.GetTotalMemory(false);
        
        var tasks = Enumerable.Range(0, 100)
            .Select(i => processor.ProcessWithMemoryAllocationAsync($"{_testMigrationId}_{i}"))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        
        var memoryAfter = GC.GetTotalMemory(false);
        var memoryIncrease = memoryAfter - memoryBefore;

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var memoryAfterGC = GC.GetTotalMemory(false);

        // Assert: Should handle memory pressure gracefully
        results.Should().OnlyContain(r => r.Success, "All operations should complete successfully");
        
        // Memory should be released after GC
        memoryAfterGC.Should().BeLessThan(memoryAfter, "Memory should be released after garbage collection");
        
        Console.WriteLine($"Memory before: {memoryBefore / 1024 / 1024:F2} MB");
        Console.WriteLine($"Memory after: {memoryAfter / 1024 / 1024:F2} MB");
        Console.WriteLine($"Memory after GC: {memoryAfterGC / 1024 / 1024:F2} MB");
        Console.WriteLine($"Memory increase: {memoryIncrease / 1024 / 1024:F2} MB");
    }

    #endregion

    #region Helper Classes

    private class EdgeCaseTestProcessor
    {
        private readonly ICancellationStore _cancellationStore;

        public EdgeCaseTestProcessor(ICancellationStore cancellationStore)
        {
            _cancellationStore = cancellationStore;
        }

        public async Task<IntermittentFailureResult> ProcessWithIntermittentFailuresAsync(string migrationId, int iterations)
        {
            var result = new IntermittentFailureResult();
            
            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                    result.SuccessfulChecks++;
                }
                catch (InvalidOperationException)
                {
                    result.FailedChecks++; // Expected failure
                }
                catch (Exception)
                {
                    result.UnhandledExceptions++; // Unexpected failure
                }
                
                result.TotalChecks++;
                await Task.Delay(1); // Simulate processing time
            }

            return result;
        }

        public async Task<RaceConditionResult> ProcessWithRaceConditionAsync(string migrationId, int processingTimeMs)
        {
            var result = new RaceConditionResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                while (stopwatch.ElapsedMilliseconds < processingTimeMs)
                {
                    var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                    if (isCancelled)
                    {
                        result.Cancelled = true;
                        result.CancellationDetected = true;
                        break;
                    }
                    
                    await Task.Delay(10); // Simulate work
                }
                
                if (!result.Cancelled)
                {
                    result.Completed = true;
                }
            }
            catch (Exception ex)
            {
                result.ExceptionOccurred = true;
                result.ExceptionMessage = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                result.ActualProcessingTime = stopwatch.Elapsed;
            }

            return result;
        }

        public async Task<MemoryAllocationResult> ProcessWithMemoryAllocationAsync(string migrationId)
        {
            var result = new MemoryAllocationResult();
            
            try
            {
                // Allocate some memory to simulate processing
                var data = new byte[1024 * 1024]; // 1 MB
                Array.Fill(data, (byte)42);
                
                // Perform cancellation check
                var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                
                result.Success = true;
                result.AllocatedMemory = data.Length;
                
                // Release reference
                data = null;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }
    }

    private class IntermittentFailureResult
    {
        public int SuccessfulChecks { get; set; }
        public int FailedChecks { get; set; }
        public int UnhandledExceptions { get; set; }
        public int TotalChecks { get; set; }
    }

    private class RaceConditionResult
    {
        public bool Completed { get; set; }
        public bool Cancelled { get; set; }
        public bool CancellationDetected { get; set; }
        public bool ExceptionOccurred { get; set; }
        public string? ExceptionMessage { get; set; }
        public TimeSpan ActualProcessingTime { get; set; }
    }

    private class MemoryAllocationResult
    {
        public bool Success { get; set; }
        public int AllocatedMemory { get; set; }
        public string? ErrorMessage { get; set; }
    }

    #endregion

    public void Dispose()
    {
        // Cleanup if needed
        GC.Collect();
    }
}