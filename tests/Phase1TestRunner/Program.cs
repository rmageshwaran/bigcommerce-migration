using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Extensions;

namespace BigCommerce.Migration.Tests;

/// <summary>
/// Simple test runner to validate Phase 1 deterministic cancellation implementation
/// without dependencies on the full test framework.
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("🚀 Starting Phase 1 Deterministic Cancellation Tests...\n");

        var testResults = new[]
        {
            await RunTest("DeterministicCancellationState Creation", TestStateCreation),
            await RunTest("DeterministicCancellationState Validation", TestStateValidation),
            await RunTest("DeterministicCancellationState Cloning", TestStateCloning),
            await RunTest("CheckExternalCancellationActivity - No Cancellation", TestExternalActivityNoCancellation),
            await RunTest("CheckExternalCancellationActivity - With Cancellation", TestExternalActivityWithCancellation),
            await RunTest("CheckExternalCancellationActivity - Error Handling", TestExternalActivityErrorHandling),
            await RunTest("DeterministicCancellationHelper", TestCancellationHelper),
            await RunTest("CancelledResultFactory", TestResultFactory),
            await RunTest("End-to-End Determinism", TestEndToEndDeterminism)
        };

        var passedTests = 0;
        var totalTests = testResults.Length;

        foreach (var passed in testResults)
        {
            if (passed) passedTests++;
        }

        Console.WriteLine($"\n📊 Test Results: {passedTests}/{totalTests} tests passed");
        
        if (passedTests == totalTests)
        {
            Console.WriteLine("✅ All Phase 1 tests PASSED! The deterministic cancellation foundation is working correctly.");
            return 0;
        }
        else
        {
            Console.WriteLine($"❌ {totalTests - passedTests} tests FAILED. The implementation needs fixes.");
            return 1;
        }
    }

    private static async Task<bool> RunTest(string testName, Func<Task> testMethod)
    {
        try
        {
            Console.Write($"🧪 {testName}... ");
            await testMethod();
            Console.WriteLine("✅ PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FAILED: {ex.Message}");
            return false;
        }
    }

    private static Task TestStateCreation()
    {
        var state = DeterministicCancellationState.Create("test-migration-123");
        
        Assert(state.MigrationId == "test-migration-123", "Migration ID should match");
        Assert(!state.IsCancelled, "Should not be cancelled initially");
        Assert(!state.StateChecked, "Should not be checked initially");
        Assert(state.Version == 1, "Initial version should be 1");
        Assert(state.IsValid(), "State should be valid");
        
        return Task.CompletedTask;
    }

    private static Task TestStateValidation()
    {
        var state = DeterministicCancellationState.Create("test-migration-456");
        
        // Test valid state
        Assert(state.IsValid(), "New state should be valid");
        
        // Test cancelled state
        state.MarkAsCancelled("Test reason");
        Assert(state.IsValid(), "Cancelled state should be valid");
        Assert(state.IsCancelled, "Should be cancelled");
        Assert(state.CancellationReason == "Test reason", "Reason should match");
        
        // Test state checking
        state.MarkStateAsChecked();
        Assert(state.StateChecked, "Should be marked as checked");
        
        return Task.CompletedTask;
    }

    private static Task TestStateCloning()
    {
        var original = DeterministicCancellationState.Create("test-migration-789");
        original.MarkAsCancelled("Original reason");
        original.MarkStateAsChecked();
        
        var clone = original.Clone();
        
        Assert(clone.MigrationId == original.MigrationId, "Migration IDs should match");
        Assert(clone.IsCancelled == original.IsCancelled, "Cancelled states should match");
        Assert(clone.CancellationReason == original.CancellationReason, "Reasons should match");
        Assert(clone.StateChecked == original.StateChecked, "State checked flags should match");
        Assert(clone.Version == original.Version, "Versions should match");
        
        // Ensure they're separate instances
        clone.MarkAsCancelled("Modified reason");
        Assert(original.CancellationReason == "Original reason", "Original should be unchanged");
        Assert(clone.CancellationReason == "Modified reason", "Clone should be modified");
        
        return Task.CompletedTask;
    }

    private static async Task TestExternalActivityNoCancellation()
    {
        var mockStorage = new Mock<IMigrationStorageService>();
        var mockLogger = new Mock<ILogger<CheckExternalCancellationActivity>>();
        
        mockStorage
            .Setup(s => s.GetCancellationTokenAsync("test-migration"))
            .ReturnsAsync((CancellationTokenEntry?)null);
        
        var activity = new CheckExternalCancellationActivity(mockStorage.Object, mockLogger.Object);
        var request = new CheckExternalCancellationRequest { MigrationId = "test-migration" };
        
        var result = await activity.CheckExternalCancellationOnceAsync(request);
        
        Assert(!result.IsCancelled, "Should not be cancelled");
        Assert(!result.IsProcessed, "Should not be processed");
        Assert(string.IsNullOrEmpty(result.CancellationReason), "Reason should be empty");
    }

    private static async Task TestExternalActivityWithCancellation()
    {
        var mockStorage = new Mock<IMigrationStorageService>();
        var mockLogger = new Mock<ILogger<CheckExternalCancellationActivity>>();
        
        var cancellationToken = new CancellationTokenEntry
        {
            MigrationId = "test-migration",
            Reason = "User requested",
            RequestedAt = DateTime.UtcNow.AddMinutes(-5),
            IsProcessed = false
        };
        
        mockStorage
            .Setup(s => s.GetCancellationTokenAsync("test-migration"))
            .ReturnsAsync(cancellationToken);
        
        var activity = new CheckExternalCancellationActivity(mockStorage.Object, mockLogger.Object);
        var request = new CheckExternalCancellationRequest { MigrationId = "test-migration" };
        
        var result = await activity.CheckExternalCancellationOnceAsync(request);
        
        Assert(result.IsCancelled, "Should be cancelled");
        Assert(!result.IsProcessed, "Should not be processed");
        Assert(result.CancellationReason == "User requested", "Reason should match");
        Assert(result.CancelledAt == cancellationToken.RequestedAt, "Times should match");
    }

    private static async Task TestExternalActivityErrorHandling()
    {
        var mockStorage = new Mock<IMigrationStorageService>();
        var mockLogger = new Mock<ILogger<CheckExternalCancellationActivity>>();
        
        mockStorage
            .Setup(s => s.GetCancellationTokenAsync("test-migration"))
            .ThrowsAsync(new InvalidOperationException("Storage error"));
        
        var activity = new CheckExternalCancellationActivity(mockStorage.Object, mockLogger.Object);
        var request = new CheckExternalCancellationRequest { MigrationId = "test-migration" };
        
        var result = await activity.CheckExternalCancellationOnceAsync(request);
        
        Assert(!result.IsCancelled, "Should not be cancelled on error");
        Assert(!result.IsProcessed, "Should not be processed on error");
        Assert(result.CancellationReason.Contains("Error checking cancellation"), "Should contain error message");
        Assert(result.CancellationReason.Contains("Storage error"), "Should contain original error");
    }

    private static Task TestCancellationHelper()
    {
        var externalResponse = new CheckExternalCancellationResponse
        {
            IsCancelled = true,
            CancellationReason = "Test cancellation",
            CancelledAt = DateTime.UtcNow.AddMinutes(-3),
            IsProcessed = false
        };
        
        var state = DeterministicCancellationHelper.CreateFromExternalCheck("test-migration", externalResponse);
        
        Assert(state.MigrationId == "test-migration", "Migration ID should match");
        Assert(state.IsCancelled, "Should be cancelled");
        Assert(state.CancellationReason == "Test cancellation", "Reason should match");
        Assert(state.StateChecked, "Should be marked as checked");
        Assert(state.IsValid(), "State should be valid");
        
        return Task.CompletedTask;
    }

    private static Task TestResultFactory()
    {
        var state = DeterministicCancellationState.Create("test-migration");
        state.MarkAsCancelled("Factory test");
        var endTime = DateTime.UtcNow;
        
        var migrationResult = CancelledResultFactory.CreateCancelledMigrationResult(state, endTime);
        var entityResult = CancelledResultFactory.CreateCancelledEntityResult(state, endTime, "products");
        
        // Basic validation that results are created
        Assert(migrationResult != null, "Migration result should not be null");
        Assert(entityResult != null, "Entity result should not be null");
        
        var migrationObj = migrationResult as dynamic;
        Assert(migrationObj != null, "Migration result should be dynamic object");
        Assert((string)migrationObj.Status == "Cancelled", "Status should be Cancelled");
        
        var entityObj = entityResult as dynamic;
        Assert(entityObj != null, "Entity result should be dynamic object");
        Assert((string)entityObj.EntityType == "products", "Entity type should match");
        
        return Task.CompletedTask;
    }

    private static async Task TestEndToEndDeterminism()
    {
        var mockStorage = new Mock<IMigrationStorageService>();
        var mockLogger = new Mock<ILogger<CheckExternalCancellationActivity>>();
        
        var cancellationToken = new CancellationTokenEntry
        {
            MigrationId = "determinism-test",
            Reason = "Determinism test",
            RequestedAt = DateTime.UtcNow.AddMinutes(-2),
            IsProcessed = false
        };
        
        mockStorage
            .Setup(s => s.GetCancellationTokenAsync("determinism-test"))
            .ReturnsAsync(cancellationToken);
        
        var activity = new CheckExternalCancellationActivity(mockStorage.Object, mockLogger.Object);
        var request = new CheckExternalCancellationRequest { MigrationId = "determinism-test" };
        
        // Multiple calls should return identical results
        var result1 = await activity.CheckExternalCancellationOnceAsync(request);
        var result2 = await activity.CheckExternalCancellationOnceAsync(request);
        var result3 = await activity.CheckExternalCancellationOnceAsync(request);
        
        var state1 = DeterministicCancellationHelper.CreateFromExternalCheck("determinism-test", result1);
        var state2 = DeterministicCancellationHelper.CreateFromExternalCheck("determinism-test", result2);
        var state3 = DeterministicCancellationHelper.CreateFromExternalCheck("determinism-test", result3);
        
        // All results should be identical
        Assert(state1.IsCancelled == state2.IsCancelled && state2.IsCancelled == state3.IsCancelled, "Cancelled states should be identical");
        Assert(state1.CancellationReason == state2.CancellationReason && state2.CancellationReason == state3.CancellationReason, "Reasons should be identical");
        Assert(state1.Version == state2.Version && state2.Version == state3.Version, "Versions should be identical");
        Assert(state1.StateChecked == state2.StateChecked && state2.StateChecked == state3.StateChecked, "State checked flags should be identical");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"Assertion failed: {message}");
    }
} 