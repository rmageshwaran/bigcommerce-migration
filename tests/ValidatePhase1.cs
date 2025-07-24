using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Extensions;

/// <summary>
/// Simple validation script to test Phase 1 deterministic cancellation components
/// </summary>
public class ValidatePhase1
{
    public static async Task<bool> Main(string[] args)
    {
        Console.WriteLine("🧪 Validating Phase 1 Deterministic Cancellation Components...\n");

        try
        {
            // Test 1: DeterministicCancellationState
            Console.Write("✓ Testing DeterministicCancellationState... ");
            var state = DeterministicCancellationState.Create("test-migration");
            if (state.MigrationId != "test-migration" || state.IsCancelled || !state.IsValid())
                throw new Exception("State creation failed");
            
            state.MarkAsCancelled("Test reason");
            if (!state.IsCancelled || state.CancellationReason != "Test reason" || !state.IsValid())
                throw new Exception("State modification failed");
                
            var clone = state.Clone();
            if (clone.MigrationId != state.MigrationId || clone.IsCancelled != state.IsCancelled)
                throw new Exception("State cloning failed");
            Console.WriteLine("PASS");

            // Test 2: CheckExternalCancellationActivity
            Console.Write("✓ Testing CheckExternalCancellationActivity... ");
            var mockStorage = new Mock<IMigrationStorageService>();
            var mockLogger = new Mock<ILogger<CheckExternalCancellationActivity>>();
            
            // Test no cancellation
            mockStorage.Setup(s => s.GetCancellationTokenAsync("test")).ReturnsAsync((CancellationTokenEntry?)null);
            var activity = new CheckExternalCancellationActivity(mockStorage.Object, mockLogger.Object);
            var request = new CheckExternalCancellationRequest { MigrationId = "test" };
            var result = await activity.CheckExternalCancellationOnceAsync(request);
            
            if (result.IsCancelled)
                throw new Exception("Should not be cancelled when no token exists");

            // Test with cancellation
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = "test",
                Reason = "User requested",
                RequestedAt = DateTime.UtcNow.AddMinutes(-5),
                IsProcessed = false
            };
            mockStorage.Setup(s => s.GetCancellationTokenAsync("test")).ReturnsAsync(cancellationToken);
            
            result = await activity.CheckExternalCancellationOnceAsync(request);
            if (!result.IsCancelled || result.CancellationReason != "User requested")
                throw new Exception("Should be cancelled when token exists");
            Console.WriteLine("PASS");

            // Test 3: DeterministicCancellationHelper
            Console.Write("✓ Testing DeterministicCancellationHelper... ");
            var externalResponse = new CheckExternalCancellationResponse
            {
                IsCancelled = true,
                CancellationReason = "Test cancellation",
                CancelledAt = DateTime.UtcNow.AddMinutes(-3),
                IsProcessed = false
            };
            
            var helperState = DeterministicCancellationHelper.CreateFromExternalCheck("test", externalResponse);
            if (!helperState.IsCancelled || !helperState.StateChecked || !helperState.IsValid())
                throw new Exception("Helper state creation failed");
            Console.WriteLine("PASS");

            // Test 4: CancelledResultFactory
            Console.Write("✓ Testing CancelledResultFactory... ");
            var factoryState = DeterministicCancellationState.Create("test");
            factoryState.MarkAsCancelled("Factory test");
            
            var migrationResult = CancelledResultFactory.CreateCancelledMigrationResult(factoryState, DateTime.UtcNow);
            var entityResult = CancelledResultFactory.CreateCancelledEntityResult(factoryState, DateTime.UtcNow, "products");
            
            if (migrationResult == null || entityResult == null)
                throw new Exception("Result factory creation failed");
                
            var migrationObj = migrationResult as dynamic;
            if (migrationObj == null || (string)migrationObj.Status != "Cancelled")
                throw new Exception("Migration result validation failed");
            Console.WriteLine("PASS");

            // Test 5: End-to-End Integration
            Console.Write("✓ Testing End-to-End Integration... ");
            var integrationRequest = new CheckExternalCancellationRequest { MigrationId = "integration-test" };
            var integrationToken = new CancellationTokenEntry
            {
                MigrationId = "integration-test",
                Reason = "Integration test",
                RequestedAt = DateTime.UtcNow.AddMinutes(-2),
                IsProcessed = false
            };
            
            mockStorage.Setup(s => s.GetCancellationTokenAsync("integration-test")).ReturnsAsync(integrationToken);
            var integrationResult = await activity.CheckExternalCancellationOnceAsync(integrationRequest);
            var finalState = DeterministicCancellationHelper.CreateFromExternalCheck("integration-test", integrationResult);
            
            if (!finalState.IsCancelled || finalState.CancellationReason != "Integration test" || !finalState.IsValid())
                throw new Exception("End-to-end integration failed");
            Console.WriteLine("PASS");

            Console.WriteLine("\n🎉 ALL PHASE 1 COMPONENTS VALIDATED SUCCESSFULLY!");
            Console.WriteLine("✅ DeterministicCancellationState: Working correctly");
            Console.WriteLine("✅ CheckExternalCancellationActivity: Working correctly");
            Console.WriteLine("✅ DeterministicCancellationHelper: Working correctly");
            Console.WriteLine("✅ CancelledResultFactory: Working correctly");
            Console.WriteLine("✅ End-to-End Integration: Working correctly");
            Console.WriteLine("\n🚀 Phase 1 foundation is ready for production use!");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
            Console.WriteLine("\n❌ Phase 1 validation failed. Check implementation.");
            return false;
        }
    }
} 