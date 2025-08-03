using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Extensions;

namespace BigCommerce.Migration.UnitTests.Orchestration.Integration;

/// <summary>
/// Integration tests for the deterministic cancellation solution to validate
/// end-to-end workflows and realistic usage scenarios.
/// </summary>
[Trait("Category", "DeterministicCancellation")]
[Trait("Component", "Integration")]
public class DeterministicCancellationIntegrationTests
{
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ILiveCancellationManager> _mockLiveCancellationManager;
    private readonly Mock<ILogger<CheckExternalCancellationActivity>> _mockLogger;
    private readonly CheckExternalCancellationActivity _externalActivity;
    private const string TestMigrationId = "integration-test-migration";

    public DeterministicCancellationIntegrationTests()
    {
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockLiveCancellationManager = new Mock<ILiveCancellationManager>();
        _mockLogger = new Mock<ILogger<CheckExternalCancellationActivity>>();
        _externalActivity = new CheckExternalCancellationActivity(_mockStorageService.Object, _mockLiveCancellationManager.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CompleteWorkflow_NoCancellation_ShouldReturnConsistentNotCancelledState()
    {
        // Arrange - No cancellation token exists
        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);

        // Act - Simulate multiple orchestrator calls (like during replay)
        var state1 = DeterministicCancellationState.Create(TestMigrationId);
        var state2 = DeterministicCancellationState.Create(TestMigrationId);
        var state3 = DeterministicCancellationState.Create(TestMigrationId);

        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        
        var externalCheck1 = await _externalActivity.CheckExternalCancellationOnceAsync(request);
        var externalCheck2 = await _externalActivity.CheckExternalCancellationOnceAsync(request);
        var externalCheck3 = await _externalActivity.CheckExternalCancellationOnceAsync(request);

        var finalState1 = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalCheck1);
        var finalState2 = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalCheck2);
        var finalState3 = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalCheck3);

        // Assert - All states should be identical (deterministic)
        Assert.False(finalState1.IsCancelled);
        Assert.False(finalState2.IsCancelled);
        Assert.False(finalState3.IsCancelled);
        
        Assert.True(finalState1.StateChecked);
        Assert.True(finalState2.StateChecked);
        Assert.True(finalState3.StateChecked);
        
        Assert.Equal(finalState1.Version, finalState2.Version);
        Assert.Equal(finalState2.Version, finalState3.Version);
        
        // Verify external service called multiple times but results are consistent
        _mockStorageService.Verify(s => s.GetCancellationTokenAsync(TestMigrationId), Times.Exactly(3));
    }

    [Fact]
    public async Task CompleteWorkflow_WithCancellation_ShouldReturnConsistentCancelledState()
    {
        // Arrange - Cancellation token exists and is unprocessed
        var cancellationToken = new CancellationTokenEntry
        {
            MigrationId = TestMigrationId,
            Reason = "User requested cancellation",
            RequestedAt = DateTime.UtcNow.AddMinutes(-5),
            IsProcessed = false
        };

        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync(cancellationToken);

        // Act - Simulate orchestrator processing with cancellation
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        
        // Multiple external checks (like during orchestrator replay)
        var externalCheck1 = await _externalActivity.CheckExternalCancellationOnceAsync(request);
        var externalCheck2 = await _externalActivity.CheckExternalCancellationOnceAsync(request);
        
        var state1 = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalCheck1);
        var state2 = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalCheck2);

        // Assert - Both states should be consistently cancelled
        Assert.True(state1.IsCancelled);
        Assert.True(state2.IsCancelled);
        
        Assert.Equal("User requested cancellation", state1.CancellationReason);
        Assert.Equal("User requested cancellation", state2.CancellationReason);
        
        Assert.Equal(cancellationToken.RequestedAt, state1.CancelledAt);
        Assert.Equal(cancellationToken.RequestedAt, state2.CancelledAt);
        
        Assert.True(state1.StateChecked);
        Assert.True(state2.StateChecked);
        
        Assert.True(state1.IsValid());
        Assert.True(state2.IsValid());
    }

    [Fact]
    public async Task CompleteWorkflow_WithProcessedCancellation_ShouldReturnNotCancelled()
    {
        // Arrange - Cancellation token exists but is already processed
        var cancellationToken = new CancellationTokenEntry
        {
            MigrationId = TestMigrationId,
            Reason = "User requested cancellation",
            RequestedAt = DateTime.UtcNow.AddMinutes(-10),
            IsProcessed = true,
            ProcessedAt = DateTime.UtcNow.AddMinutes(-8)
        };

        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync(cancellationToken);

        // Act - Check external state
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        var externalCheck = await _externalActivity.CheckExternalCancellationOnceAsync(request);
        var finalState = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalCheck);

        // Assert - Should not be cancelled because token is processed
        Assert.False(finalState.IsCancelled);
        Assert.True(finalState.StateChecked);
        Assert.True(finalState.IsValid());
        
        // External response should reflect the processed state
        Assert.False(externalCheck.IsCancelled);
        Assert.True(externalCheck.IsProcessed);
        Assert.Equal("User requested cancellation", externalCheck.CancellationReason);
    }

    [Fact]
    public async Task StateTransition_FromNotCancelledToCancelled_ShouldMaintainConsistency()
    {
        // Arrange - Start with no cancellation
        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);

        var initialState = DeterministicCancellationState.Create(TestMigrationId);
        
        // Act - Initial check (no cancellation)
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        var initialExternalCheck = await _externalActivity.CheckExternalCancellationOnceAsync(request);
        var stateAfterFirstCheck = DeterministicCancellationHelper.UpdateFromExternalCheck(initialState, initialExternalCheck);

        // Simulate cancellation token being created
        var cancellationToken = new CancellationTokenEntry
        {
            MigrationId = TestMigrationId,
            Reason = "User requested cancellation",
            RequestedAt = DateTime.UtcNow,
            IsProcessed = false
        };

        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync(cancellationToken);

        // Subsequent check (with cancellation) - but state should remain unchanged if already checked
        var stateAfterSecondCheck = DeterministicCancellationHelper.UpdateFromExternalCheck(stateAfterFirstCheck, initialExternalCheck);

        // Assert - State should remain consistent (not cancelled) because external was already checked
        Assert.False(stateAfterFirstCheck.IsCancelled);
        Assert.False(stateAfterSecondCheck.IsCancelled);
        Assert.True(stateAfterFirstCheck.StateChecked);
        Assert.True(stateAfterSecondCheck.StateChecked);
        
        // Version should not change if state was already checked
        Assert.Equal(stateAfterFirstCheck.Version, stateAfterSecondCheck.Version);
    }

    [Fact]
    public void CancelledResultFactory_CreateResults_ShouldProduceValidResults()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.MarkAsCancelled("Integration test cancellation");
        var endTime = DateTime.UtcNow;

        // Act
        var migrationResult = CancelledResultFactory.CreateCancelledMigrationResult(state, endTime);
        var entityResult = CancelledResultFactory.CreateCancelledEntityResult(state, endTime, "products");

        // Assert - Migration result
        Assert.NotNull(migrationResult);
        var migrationType = migrationResult.GetType();
        Assert.Equal(TestMigrationId, migrationType.GetProperty("MigrationId")?.GetValue(migrationResult) as string);
        Assert.Equal("Cancelled", migrationType.GetProperty("Status")?.GetValue(migrationResult) as string);
        Assert.True((bool)(migrationType.GetProperty("IsCancelled")?.GetValue(migrationResult) ?? false));
        
        // Assert - Entity result
        Assert.NotNull(entityResult);
        var entityType = entityResult.GetType();
        Assert.Equal("products", entityType.GetProperty("EntityType")?.GetValue(entityResult) as string);
        Assert.Equal(TestMigrationId, entityType.GetProperty("MigrationId")?.GetValue(entityResult) as string);
        Assert.False((bool)(entityType.GetProperty("IsSuccess")?.GetValue(entityResult) ?? true));
        Assert.True((bool)(entityType.GetProperty("IsCancelled")?.GetValue(entityResult) ?? false));
    }

    [Fact]
    public async Task ErrorRecovery_StorageFailure_ShouldReturnSafeDefault()
    {
        // Arrange - Storage throws exception
        _mockStorageService
            .Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        // Act
        var request = new CheckExternalCancellationRequest { MigrationId = TestMigrationId };
        var externalCheck = await _externalActivity.CheckExternalCancellationOnceAsync(request);
        var finalState = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalCheck);

        // Assert - Should return safe default (not cancelled)
        Assert.False(finalState.IsCancelled);
        Assert.False(externalCheck.IsCancelled);
        Assert.Contains("Error checking cancellation", externalCheck.CancellationReason);
        Assert.Contains("Storage unavailable", externalCheck.CancellationReason);
        
        // State should still be valid and marked as checked
        Assert.True(finalState.StateChecked);
        Assert.True(finalState.IsValid());
    }

    [Fact]
    public void DeterministicBehavior_MultipleCallsWithSameInput_ShouldAlwaysReturnSameResults()
    {
        // Arrange
        var migrationId = TestMigrationId;
        var externalResponse = new CheckExternalCancellationResponse
        {
            IsCancelled = true,
            CancellationReason = "Determinism test",
            CancelledAt = DateTime.UtcNow.AddMinutes(-3),
            IsProcessed = false
        };

        // Act - Multiple calls with identical inputs
        var results = new DeterministicCancellationState[10];
        for (int i = 0; i < 10; i++)
        {
            results[i] = DeterministicCancellationHelper.CreateFromExternalCheck(migrationId, externalResponse);
        }

        // Assert - All results should be identical
        for (int i = 1; i < 10; i++)
        {
            Assert.Equal(results[0].MigrationId, results[i].MigrationId);
            Assert.Equal(results[0].IsCancelled, results[i].IsCancelled);
            Assert.Equal(results[0].CancellationReason, results[i].CancellationReason);
            Assert.Equal(results[0].CancelledAt, results[i].CancelledAt);
            Assert.Equal(results[0].StateChecked, results[i].StateChecked);
            Assert.Equal(results[0].Version, results[i].Version);
            Assert.Equal(results[0].ToString(), results[i].ToString());
        }
    }
} 