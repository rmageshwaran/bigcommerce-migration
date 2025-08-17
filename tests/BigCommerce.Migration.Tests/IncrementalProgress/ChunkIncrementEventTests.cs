using BigCommerce.Migration.Core.Models;
using Xunit;
using FluentAssertions;

namespace BigCommerce.Migration.Tests.IncrementalProgress;

/// <summary>
/// Unit tests for ChunkIncrementEvent model class
/// Tests data validation, factory methods, and computed properties
/// </summary>
public class ChunkIncrementEventTests
{
    #region Test Data Setup

    private static ChunkIncrementEvent CreateValidChunkEvent()
    {
        return new ChunkIncrementEvent
        {
            PartitionKey = "test-migration-id",
            RowKey = "products-chunk-001-20250117102345123-000001",
            MigrationId = "test-migration-id",
            EntityType = "products",
            ChunkNumber = 1,
            ChunkStartIndex = 0,
            ChunkSize = 250,
            SuccessfulEntities = 200,
            FailedEntities = 30,
            SkippedEntities = 15,
            CancelledEntities = 5,
            ProcessingStartTime = DateTime.UtcNow.AddMinutes(-5),
            ProcessingEndTime = DateTime.UtcNow,
            ProcessingTimeMs = 300000, // 5 minutes
            SourceStore = "source-store",
            DestinationStore = "dest-store",
            HasErrors = false,
            ErrorSummary = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "ProcessEntityChunkActivity"
        };
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Validate_ValidData_ReturnsNoErrors()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().BeEmpty("Valid chunk event should have no validation errors");
    }

    [Fact]
    public void Validate_MissingMigrationId_ReturnsError()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.MigrationId = "";

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().Contain("MigrationId is required");
    }

    [Fact]
    public void Validate_MissingEntityType_ReturnsError()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.EntityType = "";

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().Contain("EntityType is required");
    }

    [Fact]
    public void Validate_NegativeChunkNumber_ReturnsError()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.ChunkNumber = -1;

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().Contain("ChunkNumber must be positive");
    }

    [Fact]
    public void Validate_NegativeChunkStartIndex_ReturnsError()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.ChunkStartIndex = -1;

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().Contain("ChunkStartIndex cannot be negative");
    }

    [Fact]
    public void Validate_NegativeChunkSize_ReturnsError()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.ChunkSize = -1;

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().Contain("ChunkSize must be positive");
    }

    [Fact]
    public void Validate_AllNegativeCounts_ReturnsMultipleErrors()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.SuccessfulEntities = -1;
        chunkEvent.FailedEntities = -1;
        chunkEvent.SkippedEntities = -1;
        chunkEvent.CancelledEntities = -1;

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().Contain("SuccessfulEntities cannot be negative");
        errors.Should().Contain("FailedEntities cannot be negative");
        errors.Should().Contain("SkippedEntities cannot be negative");
        errors.Should().Contain("CancelledEntities cannot be negative");
    }

    [Fact]
    public void Validate_InvalidTimeRange_ReturnsError()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.ProcessingStartTime = DateTime.UtcNow;
        chunkEvent.ProcessingEndTime = DateTime.UtcNow.AddMinutes(-5);

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().Contain("ProcessingStartTime cannot be after ProcessingEndTime");
    }

    [Fact]
    public void Validate_MissingSourceStore_ReturnsError()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.SourceStore = "";

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().Contain("SourceStore is required");
    }

    [Fact]
    public void Validate_MissingDestinationStore_ReturnsError()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.DestinationStore = "";

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        errors.Should().Contain("DestinationStore is required");
    }

    #endregion

    #region Computed Properties Tests

    [Fact]
    public void TotalProcessed_ReturnsCorrectSum()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();

        // Act
        var total = chunkEvent.TotalProcessed;

        // Assert
        total.Should().Be(250); // 200 + 30 + 15 + 5
    }

    [Fact]
    public void ProcessingDuration_ReturnsCorrectTimeSpan()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.ProcessingTimeMs = 5000; // 5 seconds

        // Act
        var duration = chunkEvent.ProcessingDuration;

        // Assert
        duration.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SuccessRate_ReturnsCorrectPercentage()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();

        // Act
        var successRate = chunkEvent.SuccessRate;

        // Assert
        successRate.Should().Be(80.0); // 200 / 250 * 100
    }

    [Fact]
    public void SuccessRate_WithZeroTotal_ReturnsZero()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();
        chunkEvent.SuccessfulEntities = 0;
        chunkEvent.FailedEntities = 0;
        chunkEvent.SkippedEntities = 0;
        chunkEvent.CancelledEntities = 0;

        // Act
        var successRate = chunkEvent.SuccessRate;

        // Assert
        successRate.Should().Be(0.0);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void Create_WithValidParameters_ReturnsConfiguredEvent()
    {
        // Arrange
        var migrationId = "test-migration";
        var entityType = "products";
        var chunkNumber = 1;
        var chunkStartIndex = 0;
        var chunkSize = 250;
        var successful = 200;
        var failed = 30;
        var skipped = 15;
        var cancelled = 5;
        var startTime = DateTime.UtcNow.AddMinutes(-5);
        var endTime = DateTime.UtcNow;
        var sourceStore = "source";
        var destStore = "dest";

        // Act
        var chunkEvent = ChunkIncrementEvent.Create(
            migrationId, entityType, chunkNumber, chunkStartIndex, chunkSize,
            successful, failed, skipped, cancelled,
            startTime, endTime, sourceStore, destStore);

        // Assert
        chunkEvent.Should().NotBeNull();
        chunkEvent.PartitionKey.Should().Be(migrationId);
        chunkEvent.MigrationId.Should().Be(migrationId);
        chunkEvent.EntityType.Should().Be(entityType);
        chunkEvent.ChunkNumber.Should().Be(chunkNumber);
        chunkEvent.ChunkStartIndex.Should().Be(chunkStartIndex);
        chunkEvent.ChunkSize.Should().Be(chunkSize);
        chunkEvent.SuccessfulEntities.Should().Be(successful);
        chunkEvent.FailedEntities.Should().Be(failed);
        chunkEvent.SkippedEntities.Should().Be(skipped);
        chunkEvent.CancelledEntities.Should().Be(cancelled);
        chunkEvent.ProcessingStartTime.Should().Be(startTime);
        chunkEvent.ProcessingEndTime.Should().Be(endTime);
        chunkEvent.SourceStore.Should().Be(sourceStore);
        chunkEvent.DestinationStore.Should().Be(destStore);
        chunkEvent.HasErrors.Should().BeFalse();
        chunkEvent.ErrorSummary.Should().BeNull();
        chunkEvent.CreatedBy.Should().Be("ProcessEntityChunkActivity");
    }

    [Fact]
    public void Create_WithErrors_SetsErrorFields()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2", "Error 3" };

        // Act
        var chunkEvent = ChunkIncrementEvent.Create(
            "migration-id", "products", 1, 0, 250,
            100, 50, 25, 10,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            "source", "dest", errors);

        // Assert
        chunkEvent.HasErrors.Should().BeTrue();
        chunkEvent.ErrorSummary.Should().NotBeNullOrEmpty();
        chunkEvent.ErrorSummary.Should().Contain("Error 1");
        chunkEvent.ErrorSummary.Should().Contain("Error 2");
        chunkEvent.ErrorSummary.Should().Contain("Error 3");
    }

    [Fact]
    public void Create_WithManyErrors_LimitsToFirstFive()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2", "Error 3", "Error 4", "Error 5", "Error 6", "Error 7" };

        // Act
        var chunkEvent = ChunkIncrementEvent.Create(
            "migration-id", "products", 1, 0, 250,
            100, 50, 25, 10,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            "source", "dest", errors);

        // Assert
        chunkEvent.HasErrors.Should().BeTrue();
        chunkEvent.ErrorSummary.Should().NotBeNullOrEmpty();
        chunkEvent.ErrorSummary.Should().Contain("Error 1");
        chunkEvent.ErrorSummary.Should().Contain("Error 5");
        chunkEvent.ErrorSummary.Should().NotContain("Error 6"); // Should be limited to 5
        chunkEvent.ErrorSummary.Should().NotContain("Error 7"); // Should be limited to 5
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var chunkEvent = CreateValidChunkEvent();

        // Act
        var result = chunkEvent.ToString();

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain(chunkEvent.MigrationId);
        result.Should().Contain(chunkEvent.EntityType);
        result.Should().Contain("Chunk1");
        result.Should().Contain("Success=200");
        result.Should().Contain("Failed=30");
        result.Should().Contain("Skipped=15");
        result.Should().Contain("Cancelled=5");
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void ProcessingTimeMs_IsCalculatedCorrectly()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddMinutes(-5);
        var endTime = DateTime.UtcNow;

        // Act
        var chunkEvent = ChunkIncrementEvent.Create(
            "migration-id", "products", 1, 0, 250,
            100, 50, 25, 10,
            startTime, endTime,
            "source", "dest");

        // Assert
        chunkEvent.ProcessingTimeMs.Should().BeGreaterThan(0);
        var expectedMs = (long)(endTime - startTime).TotalMilliseconds;
        chunkEvent.ProcessingTimeMs.Should().BeInRange(expectedMs - 1000, expectedMs + 1000);
    }

    [Fact]
    public void CreatedAt_IsSetToCurrentTime()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var chunkEvent = CreateValidChunkEvent();
        var afterCreation = DateTime.UtcNow;

        // Assert
        chunkEvent.CreatedAt.Should().BeOnOrAfter(beforeCreation);
        chunkEvent.CreatedAt.Should().BeOnOrBefore(afterCreation);
    }

    #endregion
}