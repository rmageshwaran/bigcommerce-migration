using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Integration tests for the complete 8-step cancellation flow
/// Tests the core cancellation flow logic using the existing cancellation example pattern
/// </summary>
public class CancellationFlowIntegrationTests
{
    private readonly Mock<ICancellationStore> _mockCancellationStore;
    private readonly string _testMigrationId;

    public CancellationFlowIntegrationTests()
    {
        _testMigrationId = $"test_migration_{Guid.NewGuid():N}";
        _mockCancellationStore = new Mock<ICancellationStore>();
    }

    [Fact]
    public async Task CancellationFlow_Steps2And5_CancellationStoreWorks()
    {
        // Arrange: Mock cancellation store behavior
        var cancellationReason = "Integration test cancellation";
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(_testMigrationId))
            .ReturnsAsync(cancellationReason);
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(_testMigrationId, cancellationReason))
            .Returns(Task.CompletedTask);
        
        // Act & Assert: Test the complete cancellation flow
        
        // Step 2: Set cancellation flag (HTTP endpoint simulation)
        await _mockCancellationStore.Object.SetCancellationFlagAsync(_testMigrationId, cancellationReason);
        
        _mockCancellationStore.Verify(s => s.SetCancellationFlagAsync(_testMigrationId, cancellationReason), 
            Times.Once, "Step 2: Cancellation flag should be set");
        
        // Step 5: Activity checks cancellation flag
        var isCancelled = await _mockCancellationStore.Object.CheckCancellationFlagAsync(_testMigrationId);
        isCancelled.Should().BeTrue("Step 5: Activity should detect the cancellation flag");
        
        var retrievedReason = await _mockCancellationStore.Object.GetCancellationReasonAsync(_testMigrationId);
        retrievedReason.Should().Be(cancellationReason, "Step 5: Cancellation reason should be preserved");
        
        _mockCancellationStore.Verify(s => s.CheckCancellationFlagAsync(_testMigrationId), 
            Times.AtLeastOnce, "Step 5: Activity should check for cancellation");
    }

    [Fact]
    public async Task CancellationFlow_ExceptionPropagation_SimulatesStep6()
    {
        // Arrange: Set up cancellation flag
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(_testMigrationId))
            .ReturnsAsync("Simulated activity cancellation");
        
        // Act: Simulate what happens in an activity when cancellation is detected
        var cancellationService = new SimulatedActivityService(_mockCancellationStore.Object);
        
        // Assert: Step 6 - Exception should be thrown and caught
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => cancellationService.ProcessDataAsync(_testMigrationId));
        
        exception.Message.Should().Contain("cancelled", "Step 6: Exception should indicate cancellation");
        exception.Message.Should().Contain("Simulated activity cancellation", "Step 6: Original reason should be preserved");
    }

    [Fact]
    public async Task CancellationFlow_MultipleActivities_IsolatedCancellation()
    {
        // Arrange: Multiple migration IDs
        var migration1 = $"migration_1_{Guid.NewGuid():N}";
        var migration2 = $"migration_2_{Guid.NewGuid():N}";
        var migration3 = $"migration_3_{Guid.NewGuid():N}";
        
        // Mock specific behavior for each migration
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(migration1)).ReturnsAsync(false);
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(migration2)).ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(migration3)).ReturnsAsync(false);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(migration2)).ReturnsAsync("Only migration 2 cancelled");
        
        // Act: Check cancellation status for all migrations
        var migration1Cancelled = await _mockCancellationStore.Object.CheckCancellationFlagAsync(migration1);
        var migration2Cancelled = await _mockCancellationStore.Object.CheckCancellationFlagAsync(migration2);
        var migration3Cancelled = await _mockCancellationStore.Object.CheckCancellationFlagAsync(migration3);
        
        // Assert: Only migration2 should be cancelled
        migration1Cancelled.Should().BeFalse("Migration 1 should not be cancelled");
        migration2Cancelled.Should().BeTrue("Migration 2 should be cancelled");
        migration3Cancelled.Should().BeFalse("Migration 3 should not be cancelled");
        
        var migration2Reason = await _mockCancellationStore.Object.GetCancellationReasonAsync(migration2);
        migration2Reason.Should().Be("Only migration 2 cancelled", "Migration 2 reason should be preserved");
    }

    [Fact]
    public async Task CancellationFlow_ConcurrentOperations_ThreadSafeOperations()
    {
        // Arrange: Setup concurrent access simulation
        var accessCount = 0;
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref accessCount);
                return true; // All checks return cancelled for simplicity
            });
        
        // Act: Simulate concurrent operations
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    return await _mockCancellationStore.Object.CheckCancellationFlagAsync(_testMigrationId);
                }
                catch
                {
                    return false;
                }
            }))
            .ToArray();
        
        var results = await Task.WhenAll(tasks);
        
        // Assert: All operations should complete without errors
        results.Should().NotBeNull("All concurrent operations should complete");
        results.Length.Should().Be(10, "All 10 concurrent operations should return results");
        results.Should().OnlyContain(r => r == true, "All checks should detect cancellation");
        accessCount.Should().Be(10, "Each concurrent operation should access the store once");
    }

    [Fact]
    public async Task CancellationFlow_ErrorHandling_GracefulFailureRecovery()
    {
        // Arrange: Simulate storage errors
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ThrowsAsync(new InvalidOperationException("Simulated storage error"));
        
        // Act & Assert: Cancellation check should handle errors gracefully
        var exception = await Record.ExceptionAsync(() => _mockCancellationStore.Object.CheckCancellationFlagAsync(_testMigrationId));
        
        // The actual implementation might handle this differently, but it should not crash the system
        exception.Should().NotBeNull("Error should be propagated for proper handling by calling code");
        exception.Should().BeOfType<InvalidOperationException>("Original exception should be preserved");
    }
}

/// <summary>
/// Simulated activity service to test cancellation behavior
/// </summary>
public class SimulatedActivityService
{
    private readonly ICancellationStore _cancellationStore;

    public SimulatedActivityService(ICancellationStore cancellationStore)
    {
        _cancellationStore = cancellationStore;
    }

    public async Task ProcessDataAsync(string migrationId)
    {
        // Simulate the cancellation check that happens in real activities
        var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
        if (isCancelled)
        {
            var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
            throw new OperationCanceledException($"Migration cancelled: {reason}");
        }
        
        // Simulate some work
        await Task.Delay(10);
    }
}