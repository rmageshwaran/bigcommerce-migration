using System;
using Xunit;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Extensions;
using BigCommerce.Migration.Orchestration.Activities;

namespace BigCommerce.Migration.UnitTests.Orchestration.Extensions;

/// <summary>
/// Unit tests for DeterministicCancellationExtensions and related helper classes
/// to validate deterministic behavior patterns and factory methods.
/// </summary>
[Trait("Category", "DeterministicCancellation")]
[Trait("Component", "Extensions")]
public class DeterministicCancellationExtensionsTests
{
    private const string TestMigrationId = "test-migration-789";
    private const string TestReason = "User requested cancellation";
    private const string TestEntityType = "categories";

    #region DeterministicCancellationHelper Tests

    [Fact]
    public void CreateFromExternalCheck_WithValidInputs_ShouldCreateCorrectState()
    {
        // Arrange
        var externalResponse = new CheckExternalCancellationResponse
        {
            IsCancelled = true,
            CancellationReason = TestReason,
            CancelledAt = DateTime.UtcNow.AddMinutes(-5),
            IsProcessed = false
        };

        // Act
        var state = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalResponse);

        // Assert
        Assert.Equal(TestMigrationId, state.MigrationId);
        Assert.True(state.IsCancelled);
        Assert.Equal(TestReason, state.CancellationReason);
        Assert.Equal(externalResponse.CancelledAt, state.CancelledAt);
        Assert.True(state.StateChecked);
        Assert.True(state.IsValid());
    }

    [Fact]
    public void CreateFromExternalCheck_WithNotCancelled_ShouldCreateNotCancelledState()
    {
        // Arrange
        var externalResponse = new CheckExternalCancellationResponse
        {
            IsCancelled = false,
            IsProcessed = false
        };

        // Act
        var state = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalResponse);

        // Assert
        Assert.Equal(TestMigrationId, state.MigrationId);
        Assert.False(state.IsCancelled);
        Assert.Empty(state.CancellationReason);
        Assert.Null(state.CancelledAt);
        Assert.True(state.StateChecked);
        Assert.True(state.IsValid());
    }

    [Fact]
    public void CreateFromExternalCheck_WithNullMigrationId_ShouldThrowArgumentException()
    {
        // Arrange
        var externalResponse = new CheckExternalCancellationResponse();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            DeterministicCancellationHelper.CreateFromExternalCheck(null!, externalResponse));

        Assert.Contains("Migration ID cannot be null or empty", exception.Message);
    }

    [Fact]
    public void CreateFromExternalCheck_WithNullExternalResponse_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, null!));

        Assert.Equal("externalResponse", exception.ParamName);
    }

    [Fact]
    public void UpdateFromExternalCheck_WithExistingState_ShouldUpdateCorrectly()
    {
        // Arrange
        var existingState = DeterministicCancellationState.Create(TestMigrationId);
        var externalResponse = new CheckExternalCancellationResponse
        {
            IsCancelled = true,
            CancellationReason = TestReason,
            CancelledAt = DateTime.UtcNow.AddMinutes(-3),
            IsProcessed = false
        };

        // Act
        var updatedState = DeterministicCancellationHelper.UpdateFromExternalCheck(existingState, externalResponse);

        // Assert - Should be a new instance
        Assert.NotSame(existingState, updatedState);
        
        // Original state should be unchanged
        Assert.False(existingState.IsCancelled);
        Assert.False(existingState.StateChecked);
        
        // Updated state should reflect the external response
        Assert.True(updatedState.IsCancelled);
        Assert.Equal(TestReason, updatedState.CancellationReason);
        Assert.Equal(externalResponse.CancelledAt, updatedState.CancelledAt);
        Assert.True(updatedState.StateChecked);
    }

    [Fact]
    public void UpdateFromExternalCheck_WithAlreadyCancelledState_ShouldNotOverwrite()
    {
        // Arrange
        var existingState = DeterministicCancellationState.Create(TestMigrationId);
        existingState.MarkAsCancelled("Original reason", DateTime.UtcNow.AddMinutes(-10));
        
        var externalResponse = new CheckExternalCancellationResponse
        {
            IsCancelled = true,
            CancellationReason = "Different reason",
            CancelledAt = DateTime.UtcNow.AddMinutes(-1),
            IsProcessed = false
        };

        // Act
        var updatedState = DeterministicCancellationHelper.UpdateFromExternalCheck(existingState, externalResponse);

        // Assert - Should keep original cancellation details
        Assert.True(updatedState.IsCancelled);
        Assert.Equal("Original reason", updatedState.CancellationReason);
        Assert.NotEqual(externalResponse.CancelledAt, updatedState.CancelledAt);
        Assert.True(updatedState.StateChecked);
    }

    [Fact]
    public void UpdateFromExternalCheck_WithNullExistingState_ShouldThrowArgumentNullException()
    {
        // Arrange
        var externalResponse = new CheckExternalCancellationResponse();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            DeterministicCancellationHelper.UpdateFromExternalCheck(null!, externalResponse));

        Assert.Equal("existingState", exception.ParamName);
    }

    #endregion

    #region CancelledResultFactory Tests

    [Fact]
    public void CreateCancelledMigrationResult_ShouldCreateCorrectResult()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.MarkAsCancelled(TestReason);
        var endTime = DateTime.UtcNow;

        // Act
        var result = CancelledResultFactory.CreateCancelledMigrationResult(state, endTime);

        // Assert
        Assert.NotNull(result);
        var resultType = result.GetType();
        
        Assert.Equal(TestMigrationId, resultType.GetProperty("MigrationId")?.GetValue(result) as string);
        Assert.Equal("Cancelled", resultType.GetProperty("Status")?.GetValue(result) as string);
        Assert.True((bool)(resultType.GetProperty("IsCancelled")?.GetValue(result) ?? false));
        Assert.Equal(state.CancelledAt, resultType.GetProperty("CancelledAt")?.GetValue(result));
        Assert.Equal(TestReason, resultType.GetProperty("CancellationReason")?.GetValue(result) as string);
        Assert.Equal(endTime, resultType.GetProperty("EndTime")?.GetValue(result));
        var message = resultType.GetProperty("Message")?.GetValue(result) as string;
        Assert.Contains(TestReason, message ?? "");
    }

    [Fact]
    public void CreateCancelledEntityResult_ShouldCreateCorrectResult()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.MarkAsCancelled(TestReason);
        var endTime = DateTime.UtcNow;

        // Act
        var result = CancelledResultFactory.CreateCancelledEntityResult(state, endTime, TestEntityType);

        // Assert
        Assert.NotNull(result);
        var resultType = result.GetType();
        
        Assert.Equal(TestEntityType, resultType.GetProperty("EntityType")?.GetValue(result) as string);
        Assert.Equal(TestMigrationId, resultType.GetProperty("MigrationId")?.GetValue(result) as string);
        Assert.False((bool)(resultType.GetProperty("IsSuccess")?.GetValue(result) ?? true));
        Assert.True((bool)(resultType.GetProperty("IsCancelled")?.GetValue(result) ?? false));
        Assert.Equal(state.CancelledAt, resultType.GetProperty("CancelledAt")?.GetValue(result));
        Assert.Equal(TestReason, resultType.GetProperty("CancellationReason")?.GetValue(result) as string);
        Assert.Equal(endTime, resultType.GetProperty("EndTime")?.GetValue(result));
        var errorMessage = resultType.GetProperty("ErrorMessage")?.GetValue(result) as string;
        Assert.Contains(TestEntityType, errorMessage ?? "");
        Assert.Contains(TestReason, errorMessage ?? "");
        Assert.Equal(0, (int)(resultType.GetProperty("ProcessedEntities")?.GetValue(result) ?? -1));
        Assert.Equal(0, (int)(resultType.GetProperty("SuccessfulEntities")?.GetValue(result) ?? -1));
        Assert.Equal(0, (int)(resultType.GetProperty("FailedEntities")?.GetValue(result) ?? -1));
    }

    #endregion

    #region CheckExternalCancellationRequest Tests

    [Fact]
    public void CheckExternalCancellationRequest_IsValid_WithValidMigrationId_ShouldReturnTrue()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest
        {
            MigrationId = TestMigrationId
        };

        // Act & Assert
        Assert.True(request.IsValid());
    }

    [Fact]
    public void CheckExternalCancellationRequest_IsValid_WithEmptyMigrationId_ShouldReturnFalse()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest
        {
            MigrationId = string.Empty
        };

        // Act & Assert
        Assert.False(request.IsValid());
    }

    [Fact]
    public void CheckExternalCancellationRequest_IsValid_WithNullMigrationId_ShouldReturnFalse()
    {
        // Arrange
        var request = new CheckExternalCancellationRequest
        {
            MigrationId = null!
        };

        // Act & Assert
        Assert.False(request.IsValid());
    }

    [Fact]
    public void CheckExternalCancellationRequest_WithCurrentState_ShouldAcceptState()
    {
        // Arrange
        var currentState = DeterministicCancellationState.Create(TestMigrationId);
        var request = new CheckExternalCancellationRequest
        {
            MigrationId = TestMigrationId,
            CurrentState = currentState
        };

        // Act & Assert
        Assert.True(request.IsValid());
        Assert.Equal(currentState, request.CurrentState);
    }

    #endregion

    #region CheckExternalCancellationResponse Tests

    [Fact]
    public void CheckExternalCancellationResponse_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var response = new CheckExternalCancellationResponse();

        // Assert
        Assert.False(response.IsCancelled);
        Assert.Empty(response.CancellationReason);
        Assert.Null(response.CancelledAt);
        Assert.False(response.IsProcessed);
    }

    [Fact]
    public void CheckExternalCancellationResponse_WithCancellationData_ShouldSetCorrectly()
    {
        // Arrange
        var cancelTime = DateTime.UtcNow.AddMinutes(-7);
        
        // Act
        var response = new CheckExternalCancellationResponse
        {
            IsCancelled = true,
            CancellationReason = TestReason,
            CancelledAt = cancelTime,
            IsProcessed = true
        };

        // Assert
        Assert.True(response.IsCancelled);
        Assert.Equal(TestReason, response.CancellationReason);
        Assert.Equal(cancelTime, response.CancelledAt);
        Assert.True(response.IsProcessed);
    }

    #endregion

    #region Determinism Validation Tests

    [Fact]
    public void DeterministicCancellationHelper_CreateFromExternalCheck_WithSameInputs_ShouldProduceSameResults()
    {
        // Arrange
        var externalResponse = new CheckExternalCancellationResponse
        {
            IsCancelled = true,
            CancellationReason = TestReason,
            CancelledAt = DateTime.UtcNow.AddMinutes(-2),
            IsProcessed = false
        };

        // Act - Create multiple states with same inputs
        var state1 = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalResponse);
        var state2 = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalResponse);
        var state3 = DeterministicCancellationHelper.CreateFromExternalCheck(TestMigrationId, externalResponse);

        // Assert - All should be identical
        Assert.Equal(state1.MigrationId, state2.MigrationId);
        Assert.Equal(state2.MigrationId, state3.MigrationId);
        Assert.Equal(state1.IsCancelled, state2.IsCancelled);
        Assert.Equal(state2.IsCancelled, state3.IsCancelled);
        Assert.Equal(state1.CancellationReason, state2.CancellationReason);
        Assert.Equal(state2.CancellationReason, state3.CancellationReason);
        Assert.Equal(state1.CancelledAt, state2.CancelledAt);
        Assert.Equal(state2.CancelledAt, state3.CancelledAt);
        Assert.Equal(state1.StateChecked, state2.StateChecked);
        Assert.Equal(state2.StateChecked, state3.StateChecked);
        Assert.Equal(state1.Version, state2.Version);
        Assert.Equal(state2.Version, state3.Version);
    }

    [Fact]
    public void CancelledResultFactory_CreateCancelledMigrationResult_WithSameInputs_ShouldProduceSameResults()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.MarkAsCancelled(TestReason);
        var endTime = DateTime.UtcNow;

        // Act - Create multiple results with same inputs
        var result1 = CancelledResultFactory.CreateCancelledMigrationResult(state, endTime);
        var result2 = CancelledResultFactory.CreateCancelledMigrationResult(state, endTime);
        var result3 = CancelledResultFactory.CreateCancelledMigrationResult(state, endTime);

        // Assert - All should be identical (using reflection for comparison)
        var type1 = result1.GetType();
        var type2 = result2.GetType();
        var type3 = result3.GetType();

        Assert.Equal(type1.GetProperty("MigrationId")?.GetValue(result1), type2.GetProperty("MigrationId")?.GetValue(result2));
        Assert.Equal(type2.GetProperty("MigrationId")?.GetValue(result2), type3.GetProperty("MigrationId")?.GetValue(result3));
        Assert.Equal(type1.GetProperty("Status")?.GetValue(result1), type2.GetProperty("Status")?.GetValue(result2));
        Assert.Equal(type2.GetProperty("Status")?.GetValue(result2), type3.GetProperty("Status")?.GetValue(result3));
        Assert.Equal(type1.GetProperty("IsCancelled")?.GetValue(result1), type2.GetProperty("IsCancelled")?.GetValue(result2));
        Assert.Equal(type2.GetProperty("IsCancelled")?.GetValue(result2), type3.GetProperty("IsCancelled")?.GetValue(result3));
        Assert.Equal(type1.GetProperty("CancellationReason")?.GetValue(result1), type2.GetProperty("CancellationReason")?.GetValue(result2));
        Assert.Equal(type2.GetProperty("CancellationReason")?.GetValue(result2), type3.GetProperty("CancellationReason")?.GetValue(result3));
        Assert.Equal(type1.GetProperty("EndTime")?.GetValue(result1), type2.GetProperty("EndTime")?.GetValue(result2));
        Assert.Equal(type2.GetProperty("EndTime")?.GetValue(result2), type3.GetProperty("EndTime")?.GetValue(result3));
        Assert.Equal(type1.GetProperty("Message")?.GetValue(result1), type2.GetProperty("Message")?.GetValue(result2));
        Assert.Equal(type2.GetProperty("Message")?.GetValue(result2), type3.GetProperty("Message")?.GetValue(result3));
    }

    #endregion

    #region Edge Cases and Error Conditions

    [Fact]
    public void DeterministicCancellationHelper_UpdateFromExternalCheck_WithAlreadyCheckedState_ShouldNotChangeStateChecked()
    {
        // Arrange
        var existingState = DeterministicCancellationState.Create(TestMigrationId);
        existingState.MarkStateAsChecked(); // Already checked
        
        var externalResponse = new CheckExternalCancellationResponse
        {
            IsCancelled = false,
            IsProcessed = false
        };

        var originalVersion = existingState.Version;

        // Act
        var updatedState = DeterministicCancellationHelper.UpdateFromExternalCheck(existingState, externalResponse);

        // Assert
        Assert.True(updatedState.StateChecked);
        Assert.Equal(originalVersion, updatedState.Version); // Version should not change if already checked
    }

    [Fact]
    public void CancelledResultFactory_CreateCancelledEntityResult_WithLongEntityType_ShouldWork()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.MarkAsCancelled(TestReason);
        var endTime = DateTime.UtcNow;
        var longEntityType = new string('x', 200); // Very long entity type

        // Act
        var result = CancelledResultFactory.CreateCancelledEntityResult(state, endTime, longEntityType);

        // Assert
        Assert.NotNull(result);
        var resultType = result.GetType();
        Assert.Equal(longEntityType, resultType.GetProperty("EntityType")?.GetValue(result) as string);
        var errorMessage = resultType.GetProperty("ErrorMessage")?.GetValue(result) as string;
        Assert.Contains(longEntityType, errorMessage ?? "");
    }

    [Fact]
    public void CheckExternalCancellationRequest_WithVeryLongMigrationId_ShouldStillBeValid()
    {
        // Arrange
        var longMigrationId = new string('a', 1000);
        var request = new CheckExternalCancellationRequest
        {
            MigrationId = longMigrationId
        };

        // Act & Assert
        Assert.True(request.IsValid());
        Assert.Equal(longMigrationId, request.MigrationId);
    }

    #endregion
} 