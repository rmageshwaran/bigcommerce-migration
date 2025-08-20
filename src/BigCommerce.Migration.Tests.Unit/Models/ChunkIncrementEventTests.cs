using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Models;

/// <summary>
/// Unit tests for ChunkIncrementEvent validation
/// Tests the fix for ChunkNumber validation (allowing 0, rejecting negative)
/// </summary>
public class ChunkIncrementEventTests
{
    [Fact]
    public void Validate_WithChunkNumberZero_ShouldBeValid()
    {
        // Arrange
        var chunkEvent = new ChunkIncrementEvent
        {
            MigrationId = "test-migration-id",
            EntityType = "products",
            ChunkNumber = 0, // This should be valid (zero-based indexing)
            ChunkSize = 50,
            SuccessfulEntities = 5,
            FailedEntities = 0,
            SkippedEntities = 0,
            CancelledEntities = 0,
            ProcessingTimeMs = 1000,
            ProcessingStartTime = DateTime.UtcNow.AddMinutes(-1),
            ProcessingEndTime = DateTime.UtcNow,
            SourceStore = "source-store",
            DestinationStore = "dest-store"
        };

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_WithChunkNumberOne_ShouldBeValid()
    {
        // Arrange
        var chunkEvent = new ChunkIncrementEvent
        {
            MigrationId = "test-migration-id",
            EntityType = "products",
            ChunkNumber = 1,
            ChunkSize = 50,
            SuccessfulEntities = 10,
            FailedEntities = 0,
            SkippedEntities = 0,
            CancelledEntities = 0,
            ProcessingTimeMs = 1500,
            ProcessingStartTime = DateTime.UtcNow.AddMinutes(-2),
            ProcessingEndTime = DateTime.UtcNow,
            SourceStore = "source-store",
            DestinationStore = "dest-store"
        };

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_WithNegativeChunkNumber_ShouldBeInvalid()
    {
        // Arrange
        var chunkEvent = new ChunkIncrementEvent
        {
            MigrationId = "test-migration-id",
            EntityType = "products",
            ChunkNumber = -1, // Invalid negative chunk number
            ChunkSize = 50,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            SkippedEntities = 0,
            CancelledEntities = 0,
            ProcessingTimeMs = 100,
            ProcessingStartTime = DateTime.UtcNow.AddMinutes(-1),
            ProcessingEndTime = DateTime.UtcNow,
            SourceStore = "source-store",
            DestinationStore = "dest-store"
        };

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        Assert.Single(errors);
        Assert.Contains("ChunkNumber cannot be negative", errors);
    }

    [Fact]
    public void Validate_WithMissingMigrationId_ShouldBeInvalid()
    {
        // Arrange
        var chunkEvent = new ChunkIncrementEvent
        {
            MigrationId = "", // Missing migration ID
            EntityType = "products",
            ChunkNumber = 0,
            ChunkSize = 50,
            SuccessfulEntities = 5,
            FailedEntities = 0,
            SkippedEntities = 0,
            CancelledEntities = 0,
            ProcessingTimeMs = 1000,
            ProcessingStartTime = DateTime.UtcNow.AddMinutes(-1),
            ProcessingEndTime = DateTime.UtcNow,
            SourceStore = "source-store",
            DestinationStore = "dest-store"
        };

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        Assert.Single(errors);
        Assert.Contains("MigrationId is required", errors);
    }

    [Fact]
    public void Validate_WithMissingEntityType_ShouldBeInvalid()
    {
        // Arrange
        var chunkEvent = new ChunkIncrementEvent
        {
            MigrationId = "test-migration-id",
            EntityType = "", // Missing entity type
            ChunkNumber = 0,
            ChunkSize = 50,
            SuccessfulEntities = 5,
            FailedEntities = 0,
            SkippedEntities = 0,
            CancelledEntities = 0,
            ProcessingTimeMs = 1000,
            ProcessingStartTime = DateTime.UtcNow.AddMinutes(-1),
            ProcessingEndTime = DateTime.UtcNow,
            SourceStore = "source-store",
            DestinationStore = "dest-store"
        };

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        Assert.Single(errors);
        Assert.Contains("EntityType is required", errors);
    }

    [Theory]
    [InlineData(0, 5, 0, 0, 0)]  // Chunk 0 with 5 successful
    [InlineData(1, 3, 2, 0, 0)]  // Chunk 1 with mixed results
    [InlineData(2, 0, 1, 1, 0)]  // Chunk 2 with failures and skips
    [InlineData(10, 12, 0, 0, 0)] // High chunk number
    public void Validate_WithValidChunkNumbers_ShouldAllBeValid(
        int chunkNumber, 
        int successful, 
        int failed, 
        int skipped, 
        int cancelled)
    {
        // Arrange
        var chunkEvent = new ChunkIncrementEvent
        {
            MigrationId = "test-migration-id",
            EntityType = "products",
            ChunkNumber = chunkNumber,
            ChunkSize = 50,
            SuccessfulEntities = successful,
            FailedEntities = failed,
            SkippedEntities = skipped,
            CancelledEntities = cancelled,
            ProcessingTimeMs = 1000,
            ProcessingStartTime = DateTime.UtcNow.AddMinutes(-1),
            ProcessingEndTime = DateTime.UtcNow,
            SourceStore = "source-store",
            DestinationStore = "dest-store"
        };

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ProgressiveDiscoveryScenario_ShouldBeValid()
    {
        // Arrange - Simulates progressive discovery where chunk 0 is the first chunk processed
        var chunkEvent = new ChunkIncrementEvent
        {
            MigrationId = "test-migration-id",
            EntityType = "images", // Component entity for progressive discovery
            ChunkNumber = 0,
            ChunkSize = 50,
            SuccessfulEntities = 3,
            FailedEntities = 0,
            SkippedEntities = 0,
            CancelledEntities = 0,
            ProcessingTimeMs = 800,
            ProcessingStartTime = DateTime.UtcNow.AddMinutes(-1),
            ProcessingEndTime = DateTime.UtcNow,
            SourceStore = "source-store",
            DestinationStore = "dest-store"
        };

        // Act
        var errors = chunkEvent.Validate();

        // Assert
        Assert.Empty(errors);
    }
}