using System;
using Xunit;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.UnitTests.Orchestration.Models;

/// <summary>
/// Unit tests for DeterministicCancellationState to validate deterministic behavior,
/// state management, and consistency across orchestrator replay scenarios.
/// </summary>
[Trait("Category", "DeterministicCancellation")]
[Trait("Component", "Models")]
public class DeterministicCancellationStateTests
{
    private const string TestMigrationId = "test-migration-123";
    private const string TestReason = "User requested cancellation";

    #region Creation and Initialization Tests

    [Fact]
    public void Create_WithValidMigrationId_ShouldInitializeCorrectly()
    {
        // Act
        var state = DeterministicCancellationState.Create(TestMigrationId);

        // Assert
        Assert.Equal(TestMigrationId, state.MigrationId);
        Assert.False(state.IsCancelled);
        Assert.False(state.StateChecked);
        Assert.Null(state.CancelledAt);
        Assert.Empty(state.CancellationReason);
        Assert.Equal(1, state.Version);
        Assert.True(state.IsValid());
    }

    [Fact]
    public void Create_WithNullMigrationId_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            DeterministicCancellationState.Create(null!));
        
        Assert.Contains("Migration ID cannot be null or empty", exception.Message);
    }

    [Fact]
    public void Create_WithEmptyMigrationId_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            DeterministicCancellationState.Create(string.Empty));
        
        Assert.Contains("Migration ID cannot be null or empty", exception.Message);
    }

    [Fact]
    public void Create_WithWhitespaceMigrationId_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            DeterministicCancellationState.Create("   "));
        
        Assert.Contains("Migration ID cannot be null or empty", exception.Message);
    }

    #endregion

    #region State Modification Tests

    [Fact]
    public void MarkAsCancelled_WithReason_ShouldUpdateStateCorrectly()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        var beforeVersion = state.Version;
        var cancelTime = DateTime.UtcNow.AddMinutes(-1);

        // Act
        state.MarkAsCancelled(TestReason, cancelTime);

        // Assert
        Assert.True(state.IsCancelled);
        Assert.Equal(TestReason, state.CancellationReason);
        Assert.Equal(cancelTime, state.CancelledAt);
        Assert.Equal(beforeVersion + 1, state.Version);
        Assert.True(state.IsValid());
    }

    [Fact]
    public void MarkAsCancelled_WithoutCancelTime_ShouldUseCurrentTime()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        var beforeTime = DateTime.UtcNow;

        // Act
        state.MarkAsCancelled(TestReason);
        var afterTime = DateTime.UtcNow;

        // Assert
        Assert.True(state.IsCancelled);
        Assert.Equal(TestReason, state.CancellationReason);
        Assert.True(state.CancelledAt >= beforeTime);
        Assert.True(state.CancelledAt <= afterTime);
    }

    [Fact]
    public void MarkAsCancelled_WithNullReason_ShouldUseDefaultReason()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);

        // Act
        state.MarkAsCancelled(null!);

        // Assert
        Assert.True(state.IsCancelled);
        Assert.Equal("Unknown reason", state.CancellationReason);
    }

    [Fact]
    public void MarkStateAsChecked_ShouldUpdateVersionAndFlag()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        var beforeVersion = state.Version;

        // Act
        state.MarkStateAsChecked();

        // Assert
        Assert.True(state.StateChecked);
        Assert.Equal(beforeVersion + 1, state.Version);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void IsValid_WithValidState_ShouldReturnTrue()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);

        // Act & Assert
        Assert.True(state.IsValid());
    }

    [Fact]
    public void IsValid_WithCancelledState_ShouldReturnTrue()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.MarkAsCancelled(TestReason);

        // Act & Assert
        Assert.True(state.IsValid());
    }

    [Fact]
    public void IsValid_WithEmptyMigrationId_ShouldReturnFalse()
    {
        // Arrange
        var state = new DeterministicCancellationState
        {
            MigrationId = string.Empty
        };

        // Act & Assert
        Assert.False(state.IsValid());
    }

    [Fact]
    public void IsValid_WithCancelledButNoReason_ShouldReturnFalse()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.IsCancelled = true;
        state.CancelledAt = DateTime.UtcNow;
        // Reason remains empty

        // Act & Assert
        Assert.False(state.IsValid());
    }

    [Fact]
    public void IsValid_WithCancelledButNoTime_ShouldReturnFalse()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.IsCancelled = true;
        state.CancellationReason = TestReason;
        // CancelledAt remains null

        // Act & Assert
        Assert.False(state.IsValid());
    }

    #endregion

    #region Cloning Tests

    [Fact]
    public void Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var original = DeterministicCancellationState.Create(TestMigrationId);
        original.MarkAsCancelled(TestReason);
        original.MarkStateAsChecked();

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.MigrationId, clone.MigrationId);
        Assert.Equal(original.IsCancelled, clone.IsCancelled);
        Assert.Equal(original.CancelledAt, clone.CancelledAt);
        Assert.Equal(original.CancellationReason, clone.CancellationReason);
        Assert.Equal(original.StateChecked, clone.StateChecked);
        Assert.Equal(original.Version, clone.Version);
    }

    [Fact]
    public void Clone_ModifyingClone_ShouldNotAffectOriginal()
    {
        // Arrange
        var original = DeterministicCancellationState.Create(TestMigrationId);
        var clone = original.Clone();

        // Act
        clone.MarkAsCancelled("Modified reason");

        // Assert
        Assert.False(original.IsCancelled);
        Assert.True(clone.IsCancelled);
        Assert.NotEqual(original.Version, clone.Version);
    }

    #endregion

    #region Determinism Tests

    [Fact]
    public void MultipleCallsToSameState_ShouldProduceDeterministicResults()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.MarkAsCancelled(TestReason);

        // Act - Multiple calls to properties and methods
        var result1 = state.IsCancelled;
        var result2 = state.IsCancelled;
        var result3 = state.IsCancelled;
        
        var valid1 = state.IsValid();
        var valid2 = state.IsValid();
        var valid3 = state.IsValid();

        // Assert - All calls should return identical results
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
        Assert.Equal(valid1, valid2);
        Assert.Equal(valid2, valid3);
        Assert.True(result1); // Should be cancelled
        Assert.True(valid1);  // Should be valid
    }

    [Fact]
    public void StateCreation_WithSameInputs_ShouldProduceDeterministicResults()
    {
        // Act - Create multiple states with same inputs
        var state1 = DeterministicCancellationState.Create(TestMigrationId);
        var state2 = DeterministicCancellationState.Create(TestMigrationId);
        var state3 = DeterministicCancellationState.Create(TestMigrationId);

        // Assert - All states should have identical initial values
        Assert.Equal(state1.MigrationId, state2.MigrationId);
        Assert.Equal(state2.MigrationId, state3.MigrationId);
        Assert.Equal(state1.IsCancelled, state2.IsCancelled);
        Assert.Equal(state2.IsCancelled, state3.IsCancelled);
        Assert.Equal(state1.StateChecked, state2.StateChecked);
        Assert.Equal(state2.StateChecked, state3.StateChecked);
        Assert.Equal(state1.Version, state2.Version);
        Assert.Equal(state2.Version, state3.Version);
    }

    [Fact]
    public void StateModification_WithSameInputs_ShouldProduceDeterministicResults()
    {
        // Arrange
        var cancelTime = DateTime.UtcNow.AddMinutes(-5);
        var state1 = DeterministicCancellationState.Create(TestMigrationId);
        var state2 = DeterministicCancellationState.Create(TestMigrationId);

        // Act - Apply same modifications
        state1.MarkAsCancelled(TestReason, cancelTime);
        state2.MarkAsCancelled(TestReason, cancelTime);

        // Assert - Results should be identical
        Assert.Equal(state1.IsCancelled, state2.IsCancelled);
        Assert.Equal(state1.CancelledAt, state2.CancelledAt);
        Assert.Equal(state1.CancellationReason, state2.CancellationReason);
        Assert.Equal(state1.Version, state2.Version);
        Assert.Equal(state1.IsValid(), state2.IsValid());
    }

    #endregion

    #region String Representation Tests

    [Fact]
    public void ToString_ShouldContainKeyInformation()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        state.MarkAsCancelled(TestReason);

        // Act
        var result = state.ToString();

        // Assert
        Assert.Contains(TestMigrationId, result);
        Assert.Contains("IsCancelled=True", result);
        Assert.Contains("StateChecked=False", result);
        Assert.Contains(TestReason, result);
        Assert.Contains("Version=2", result); // Initial + MarkAsCancelled
    }

    [Fact]
    public void ToString_ForNonCancelledState_ShouldShowCorrectValues()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);

        // Act
        var result = state.ToString();

        // Assert
        Assert.Contains(TestMigrationId, result);
        Assert.Contains("IsCancelled=False", result);
        Assert.Contains("StateChecked=False", result);
        Assert.Contains("Version=1", result);
    }

    #endregion

    #region Edge Cases and Error Conditions

    [Fact]
    public void MarkAsCancelled_MultipleTimes_ShouldUpdateVersion()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);

        // Act
        state.MarkAsCancelled("First reason");
        var firstVersion = state.Version;
        
        state.MarkAsCancelled("Second reason");
        var secondVersion = state.Version;

        // Assert
        Assert.Equal(2, firstVersion);
        Assert.Equal(3, secondVersion);
        Assert.Equal("Second reason", state.CancellationReason);
    }

    [Fact]
    public void MarkStateAsChecked_MultipleTimes_ShouldUpdateVersion()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);

        // Act
        state.MarkStateAsChecked();
        var firstVersion = state.Version;
        
        state.MarkStateAsChecked();
        var secondVersion = state.Version;

        // Assert
        Assert.Equal(2, firstVersion);
        Assert.Equal(3, secondVersion);
        Assert.True(state.StateChecked);
    }

    [Fact]
    public void State_WithVeryLongMigrationId_ShouldHandleCorrectly()
    {
        // Arrange
        var longMigrationId = new string('a', 1000); // 1000 character ID

        // Act
        var state = DeterministicCancellationState.Create(longMigrationId);

        // Assert
        Assert.Equal(longMigrationId, state.MigrationId);
        Assert.True(state.IsValid());
    }

    [Fact]
    public void State_WithVeryLongCancellationReason_ShouldHandleCorrectly()
    {
        // Arrange
        var state = DeterministicCancellationState.Create(TestMigrationId);
        var longReason = new string('x', 2000); // 2000 character reason

        // Act
        state.MarkAsCancelled(longReason);

        // Assert
        Assert.Equal(longReason, state.CancellationReason);
        Assert.True(state.IsValid());
    }

    #endregion
} 