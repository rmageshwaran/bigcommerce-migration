using Xunit;
using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using System;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// TDD unit tests for enhanced cancellation models
/// Tests define the expected behavior - implementation will follow (TDD)
/// Validates multi-level cancellation scopes and enhanced cancellation token entries
/// </summary>
public class CancellationModelsTests
{
    #region CancellationScope Enum Tests

    [Fact]
    public void CancellationScope_ShouldHave_AllRequiredValues()
    {
        // Arrange & Act
        var scopeValues = Enum.GetValues<CancellationScope>();

        // Assert
        scopeValues.Should().Contain(CancellationScope.Migration);
        scopeValues.Should().Contain(CancellationScope.EntityType);
        scopeValues.Should().Contain(CancellationScope.Batch);
        scopeValues.Should().Contain(CancellationScope.Store);
        scopeValues.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(CancellationScope.Migration, 0)]
    [InlineData(CancellationScope.EntityType, 1)]
    [InlineData(CancellationScope.Batch, 2)]
    [InlineData(CancellationScope.Store, 3)]
    public void CancellationScope_ShouldHave_CorrectEnumValues(CancellationScope scope, int expectedValue)
    {
        // Act & Assert
        ((int)scope).Should().Be(expectedValue);
    }

    #endregion

    #region EnhancedCancellationTokenEntry Tests

    [Fact]
    public void EnhancedCancellationTokenEntry_WithValidData_ShouldCreateInstance()
    {
        // Arrange
        var migrationId = "migration-123";
        var scope = CancellationScope.EntityType;
        var entityType = "categories";
        var requestedAt = DateTime.UtcNow;
        var requestedBy = "user@example.com";
        var reason = "User requested cancellation";

        // Act
        var entry = new EnhancedCancellationTokenEntry
        {
            MigrationId = migrationId,
            Scope = scope,
            EntityType = entityType,
            RequestedAt = requestedAt,
            RequestedBy = requestedBy,
            Reason = reason,
            IsActive = true
        };

        // Assert
        entry.MigrationId.Should().Be(migrationId);
        entry.Scope.Should().Be(scope);
        entry.EntityType.Should().Be(entityType);
        entry.RequestedAt.Should().Be(requestedAt);
        entry.RequestedBy.Should().Be(requestedBy);
        entry.Reason.Should().Be(reason);
        entry.IsActive.Should().BeTrue();
        entry.BatchId.Should().BeNull();
        entry.StoreId.Should().BeNull();
        entry.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void EnhancedCancellationTokenEntry_WithBatchScope_ShouldAllowBatchId()
    {
        // Arrange & Act
        var entry = new EnhancedCancellationTokenEntry
        {
            MigrationId = "migration-123",
            Scope = CancellationScope.Batch,
            BatchId = "batch-456",
            RequestedAt = DateTime.UtcNow,
            RequestedBy = "system",
            Reason = "Batch timeout",
            IsActive = true
        };

        // Assert
        entry.Scope.Should().Be(CancellationScope.Batch);
        entry.BatchId.Should().Be("batch-456");
        entry.EntityType.Should().BeNull();
        entry.StoreId.Should().BeNull();
    }

    [Fact]
    public void EnhancedCancellationTokenEntry_WithStoreScope_ShouldAllowStoreId()
    {
        // Arrange & Act
        var entry = new EnhancedCancellationTokenEntry
        {
            MigrationId = "migration-123",
            Scope = CancellationScope.Store,
            StoreId = "store-789",
            RequestedAt = DateTime.UtcNow,
            RequestedBy = "admin",
            Reason = "Store maintenance",
            IsActive = true
        };

        // Assert
        entry.Scope.Should().Be(CancellationScope.Store);
        entry.StoreId.Should().Be("store-789");
        entry.EntityType.Should().BeNull();
        entry.BatchId.Should().BeNull();
    }

    [Fact]
    public void EnhancedCancellationTokenEntry_WhenProcessed_ShouldSetProcessedAt()
    {
        // Arrange
        var entry = new EnhancedCancellationTokenEntry
        {
            MigrationId = "migration-123",
            Scope = CancellationScope.Migration,
            RequestedAt = DateTime.UtcNow,
            RequestedBy = "user@example.com",
            Reason = "Test cancellation",
            IsActive = true
        };

        var processedAt = DateTime.UtcNow.AddMinutes(5);

        // Act
        entry.ProcessedAt = processedAt;
        entry.IsActive = false;

        // Assert
        entry.ProcessedAt.Should().Be(processedAt);
        entry.IsActive.Should().BeFalse();
    }

    #endregion

    #region CancellationResult Tests

    [Fact]
    public void CancellationResult_WithSuccessfulCancellation_ShouldIndicateSuccess()
    {
        // Arrange & Act
        var result = new CancellationResult
        {
            Success = true,
            MigrationId = "migration-123",
            Scope = CancellationScope.EntityType,
            RequestedAt = DateTime.UtcNow,
            Message = "Cancellation request processed successfully"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.MigrationId.Should().Be("migration-123");
        result.Scope.Should().Be(CancellationScope.EntityType);
        result.Message.Should().Be("Cancellation request processed successfully");
        result.ErrorDetails.Should().BeNull();
    }

    [Fact]
    public void CancellationResult_WithFailedCancellation_ShouldIndicateFailure()
    {
        // Arrange & Act
        var result = new CancellationResult
        {
            Success = false,
            MigrationId = "migration-123",
            Scope = CancellationScope.Migration,
            RequestedAt = DateTime.UtcNow,
            Message = "Cancellation request failed",
            ErrorDetails = "Migration not found"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Cancellation request failed");
        result.ErrorDetails.Should().Be("Migration not found");
    }

    #endregion

    #region CancellationStatus Tests

    [Fact]
    public void CancellationStatus_WithActiveCancellations_ShouldShowCorrectStatus()
    {
        // Arrange & Act
        var status = new CancellationStatus
        {
            MigrationId = "migration-123",
            HasActiveCancellation = true,
            ActiveScopes = new List<CancellationScope> { CancellationScope.EntityType, CancellationScope.Batch },
            TotalCancellationRequests = 3,
            LastCancellationAt = DateTime.UtcNow.AddMinutes(-2)
        };

        // Assert
        status.MigrationId.Should().Be("migration-123");
        status.HasActiveCancellation.Should().BeTrue();
        status.ActiveScopes.Should().Contain(CancellationScope.EntityType);
        status.ActiveScopes.Should().Contain(CancellationScope.Batch);
        status.ActiveScopes.Should().HaveCount(2);
        status.TotalCancellationRequests.Should().Be(3);
        status.LastCancellationAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(-2), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CancellationStatus_WithNoCancellations_ShouldShowInactiveStatus()
    {
        // Arrange & Act
        var status = new CancellationStatus
        {
            MigrationId = "migration-123",
            HasActiveCancellation = false,
            ActiveScopes = new List<CancellationScope>(),
            TotalCancellationRequests = 0
        };

        // Assert
        status.HasActiveCancellation.Should().BeFalse();
        status.ActiveScopes.Should().BeEmpty();
        status.TotalCancellationRequests.Should().Be(0);
        status.LastCancellationAt.Should().BeNull();
    }

    #endregion
}