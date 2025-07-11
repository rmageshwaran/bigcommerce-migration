using System.Text.Json;
using Xunit;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.OrchestrationTests.Models;

/// <summary>
/// Tests for orchestration models to validate structure, serialization, and validation
/// These tests define the expected behavior - implementation will follow (TDD)
/// </summary>
public class OrchestrationModelsTests
{
    #region MigrationOrchestrationRequest Tests

    [Fact]
    public void MigrationOrchestrationRequest_ShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var request = new MigrationOrchestrationRequest();

        // Assert - Verify all required properties exist
        Assert.NotNull(request.MigrationId);
        Assert.NotNull(request.OriginalRequest);
        Assert.NotNull(request.CategoryTreeContext);
        Assert.NotNull(request.Metadata);
        Assert.True(request.StartTime != default);
    }

    [Fact]
    public void MigrationOrchestrationRequest_ShouldSerializeToJson()
    {
        // Arrange
        var request = new MigrationOrchestrationRequest
        {
            MigrationId = "test-migration-123",
            OriginalRequest = new MigrationRequest
            {
                SourceStore = new StoreConfiguration
                {
                    StoreId = "source-store",
                    ChannelId = "source-channel",
                    AccessToken = "source-token"
                },
                DestinationStore = new StoreConfiguration
                {
                    StoreId = "dest-store",
                    ChannelId = "dest-channel",
                    AccessToken = "dest-token"
                },
                Entities = new List<string> { "products", "categories" }
            },
            CategoryTreeContext = new CategoryTreeContext
            {
                SourceChannelId = "source-channel",
                DestinationChannelId = "dest-channel"
            },
            StartTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Metadata = new Dictionary<string, object> { ["key"] = "value" }
        };

        // Act
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<MigrationOrchestrationRequest>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(request.MigrationId, deserialized.MigrationId);
        Assert.Equal(request.OriginalRequest.SourceStore?.StoreId, deserialized.OriginalRequest.SourceStore?.StoreId);
        Assert.Equal(request.StartTime, deserialized.StartTime);
    }

    [Fact]
    public void MigrationOrchestrationRequest_ShouldValidateRequiredFields()
    {
        // Arrange
        var request = new MigrationOrchestrationRequest();

        // Act
        var isValid = request.IsValid();
        var validationErrors = request.GetValidationErrors();

        // Assert
        Assert.False(isValid);
        Assert.Contains("MigrationId is required", validationErrors);
    }

    #endregion

    #region EntityMigrationRequest Tests

    [Fact]
    public void EntityMigrationRequest_ShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var request = new EntityMigrationRequest();

        // Assert
        Assert.NotNull(request.MigrationId);
        Assert.NotNull(request.EntityType);
        Assert.NotNull(request.SourceStore);
        Assert.NotNull(request.DestinationStore);
        Assert.NotNull(request.CategoryTreeContext);
        Assert.NotNull(request.EntityConfig);
        Assert.True(request.BatchSize > 0);
        Assert.True(request.MaxRetries >= 0);
    }

    [Fact]
    public void EntityMigrationRequest_ShouldHaveDefaultBatchSizes()
    {
        // Arrange & Act
        var request = new EntityMigrationRequest();

        // Assert
        Assert.Equal(10, request.BatchSize); // Default batch size
        Assert.Equal(3, request.MaxRetries); // Default max retries
    }

    [Fact]
    public void EntityMigrationRequest_ShouldValidateEntityType()
    {
        // Arrange
        var request = new EntityMigrationRequest
        {
            MigrationId = "test-123",
            EntityType = "invalid-entity"
        };

        // Act
        var isValid = request.IsValid();
        var validationErrors = request.GetValidationErrors();

        // Assert
        Assert.False(isValid);
        Assert.Contains("EntityType must be one of: categories, products, brands, variants, images, modifiers", validationErrors);
    }

    #endregion

    #region BatchProcessingRequest Tests

    [Fact]
    public void BatchProcessingRequest_ShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var request = new BatchProcessingRequest();

        // Assert
        Assert.NotNull(request.MigrationId);
        Assert.NotNull(request.EntityType);
        Assert.NotNull(request.EntityIds);
        Assert.NotNull(request.SourceStore);
        Assert.NotNull(request.DestinationStore);
        Assert.NotNull(request.CategoryTreeContext);
        Assert.True(request.BatchNumber >= 0);
        Assert.True(request.TotalBatches >= 0);
    }

    [Fact]
    public void BatchProcessingRequest_ShouldValidateBatchNumbers()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            BatchNumber = 5,
            TotalBatches = 3
        };

        // Act
        var isValid = request.IsValid();
        var validationErrors = request.GetValidationErrors();

        // Assert
        Assert.False(isValid);
        Assert.Contains("BatchNumber cannot be greater than TotalBatches", validationErrors);
    }

    #endregion

    #region MigrationResult Tests

    [Fact]
    public void MigrationResult_ShouldCalculateDurationCorrectly()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddHours(-2);
        var endTime = DateTime.UtcNow;
        var result = new MigrationResult
        {
            StartTime = startTime,
            EndTime = endTime
        };

        // Act
        result.CalculateDuration();

        // Assert
        Assert.Equal(endTime - startTime, result.Duration);
    }

    [Fact]
    public void MigrationResult_ShouldProvideStatisticsSummary()
    {
        // Arrange
        var result = new MigrationResult();
        result.EntityResults["products"] = new EntityMigrationResult
        {
            TotalEntities = 100,
            SuccessfulEntities = 95,
            FailedEntities = 5
        };
        result.EntityResults["categories"] = new EntityMigrationResult
        {
            TotalEntities = 50,
            SuccessfulEntities = 50,
            FailedEntities = 0
        };

        // Act
        var summary = result.GetStatisticsSummary();

        // Assert
        Assert.Equal(150, summary.TotalEntities);
        Assert.Equal(145, summary.SuccessfulEntities);
        Assert.Equal(5, summary.FailedEntities);
        Assert.Equal(96.67, summary.SuccessRate, 2); // 145/150 * 100
    }

    #endregion

    #region EntityMigrationResult Tests

    [Fact]
    public void EntityMigrationResult_ShouldCalculateSuccessRate()
    {
        // Arrange
        var result = new EntityMigrationResult
        {
            TotalEntities = 100,
            SuccessfulEntities = 85,
            FailedEntities = 15
        };

        // Act
        var successRate = result.GetSuccessRate();

        // Assert
        Assert.Equal(85.0, successRate);
    }

    [Fact]
    public void EntityMigrationResult_ShouldValidateEntityCounts()
    {
        // Arrange
        var result = new EntityMigrationResult
        {
            TotalEntities = 100,
            SuccessfulEntities = 60,
            FailedEntities = 50 // Total = 110, which exceeds TotalEntities
        };

        // Act
        var isValid = result.IsValid();
        var validationErrors = result.GetValidationErrors();

        // Assert
        Assert.False(isValid);
        Assert.Contains("SuccessfulEntities + FailedEntities cannot exceed TotalEntities", validationErrors);
    }

    #endregion

    #region Model Integration Tests

    [Fact]
    public void OrchestrationModels_ShouldIntegrateWithCoreModels()
    {
        // Arrange
        var migrationRequest = new MigrationRequest
        {
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-123",
                ChannelId = "channel-1",
                AccessToken = "token-123"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "dest-456",
                ChannelId = "channel-2",
                AccessToken = "token-456"
            }
        };

        var orchestrationRequest = new MigrationOrchestrationRequest
        {
            MigrationId = "migration-789",
            OriginalRequest = migrationRequest
        };

        // Act
        var sourceStoreId = orchestrationRequest.OriginalRequest.SourceStore?.StoreId;

        // Assert
        Assert.Equal("source-123", sourceStoreId);
        Assert.Equal(migrationRequest, orchestrationRequest.OriginalRequest);
    }

    [Fact]
    public void OrchestrationModels_ShouldWorkWithCategoryTreeContext()
    {
        // Arrange
        var categoryTreeContext = new CategoryTreeContext
        {
            SourceChannelId = "channel-1",
            DestinationChannelId = "channel-2",
            SourceCategoryTreeId = "tree-1",
            DestinationCategoryTreeId = "tree-2"
        };

        var entityRequest = new EntityMigrationRequest
        {
            CategoryTreeContext = categoryTreeContext
        };

        // Act & Assert
        Assert.Equal("channel-1", entityRequest.CategoryTreeContext.SourceChannelId);
        Assert.Equal("tree-1", entityRequest.CategoryTreeContext.SourceCategoryTreeId);
    }

    #endregion
} 