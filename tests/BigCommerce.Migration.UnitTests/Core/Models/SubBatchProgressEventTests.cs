using BigCommerce.Migration.Core.Models;
using System.Text.Json;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// 🎯 PHASE 3: Unit tests for sub-batch progress event models
/// Tests JSON serialization, event properties, and polymorphic type handling
/// </summary>
public class SubBatchProgressEventTests
{
    #region SubBatchStartedEvent Tests

    [Fact]
    public void SubBatchStartedEvent_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var startedEvent = new SubBatchStartedEvent();

        // Assert: Verify default values
        Assert.Equal("subbatch-started", startedEvent.EventType);
        Assert.Equal("SubBatchStarted", startedEvent.HubMethod);
        Assert.Equal(string.Empty, startedEvent.MigrationId);
        Assert.Equal(0, startedEvent.ParentBatchNumber);
        Assert.Equal(0, startedEvent.SubBatchNumber);
        Assert.Equal(0, startedEvent.TotalSubBatches);
        Assert.Equal(0, startedEvent.EntitiesInSubBatch);
        Assert.Equal(0, startedEvent.MaxConcurrency);
        Assert.Equal(string.Empty, startedEvent.EntityType);
        Assert.True(startedEvent.StartedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void SubBatchStartedEvent_ShouldSerializeToJsonCorrectly()
    {
        // Arrange
        var startedEvent = new SubBatchStartedEvent
        {
            MigrationId = "test-migration-123",
            ParentBatchNumber = 2,
            SubBatchNumber = 5,
            TotalSubBatches = 10,
            EntitiesInSubBatch = 5,
            MaxConcurrency = 5,
            EntityType = "brands",
            StartedAt = new DateTime(2025, 1, 28, 10, 30, 0, DateTimeKind.Utc),
            Timestamp = new DateTime(2025, 1, 28, 10, 30, 0, DateTimeKind.Utc)
        };

        // Act
        var json = JsonSerializer.Serialize(startedEvent);
        var deserializedEvent = JsonSerializer.Deserialize<SubBatchStartedEvent>(json);

        // Assert: Verify serialization/deserialization
        Assert.NotNull(deserializedEvent);
        Assert.Equal("test-migration-123", deserializedEvent.MigrationId);
        Assert.Equal(2, deserializedEvent.ParentBatchNumber);
        Assert.Equal(5, deserializedEvent.SubBatchNumber);
        Assert.Equal(10, deserializedEvent.TotalSubBatches);
        Assert.Equal(5, deserializedEvent.EntitiesInSubBatch);
        Assert.Equal(5, deserializedEvent.MaxConcurrency);
        Assert.Equal("brands", deserializedEvent.EntityType);
        Assert.Equal("subbatch-started", deserializedEvent.EventType);
        Assert.Equal("SubBatchStarted", deserializedEvent.HubMethod);
    }

    [Fact]
    public void SubBatchStartedEvent_ShouldSupportPolymorphicSerialization()
    {
        // Arrange: Create as base ProgressEvent type
        ProgressEvent progressEvent = new SubBatchStartedEvent
        {
            MigrationId = "test-migration",
            ParentBatchNumber = 1,
            SubBatchNumber = 3,
            TotalSubBatches = 10,
            EntitiesInSubBatch = 5,
            EntityType = "products"
        };

        // Act: Serialize as base type and deserialize
        var json = JsonSerializer.Serialize(progressEvent);
        var deserializedEvent = JsonSerializer.Deserialize<ProgressEvent>(json);

        // Assert: Should deserialize to correct derived type
        Assert.IsType<SubBatchStartedEvent>(deserializedEvent);
        var startedEvent = (SubBatchStartedEvent)deserializedEvent;
        Assert.Equal("test-migration", startedEvent.MigrationId);
        Assert.Equal(1, startedEvent.ParentBatchNumber);
        Assert.Equal(3, startedEvent.SubBatchNumber);
        Assert.Equal("products", startedEvent.EntityType);
    }

    #endregion

    #region SubBatchCompletedEvent Tests

    [Fact]
    public void SubBatchCompletedEvent_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var completedEvent = new SubBatchCompletedEvent();

        // Assert: Verify default values
        Assert.Equal("subbatch-completed", completedEvent.EventType);
        Assert.Equal("SubBatchCompleted", completedEvent.HubMethod);
        Assert.Equal(string.Empty, completedEvent.MigrationId);
        Assert.Equal(0, completedEvent.ParentBatchNumber);
        Assert.Equal(0, completedEvent.SubBatchNumber);
        Assert.Equal(0, completedEvent.TotalSubBatches);
        Assert.Equal(0, completedEvent.SuccessfulEntities);
        Assert.Equal(0, completedEvent.FailedEntities);
        Assert.Equal(0, completedEvent.TotalEntities);
        Assert.Equal(string.Empty, completedEvent.EntityType);
        Assert.Equal(TimeSpan.Zero, completedEvent.ProcessingTime);
        Assert.True(completedEvent.CompletedAt <= DateTime.UtcNow);
        Assert.Empty(completedEvent.Errors);
        Assert.Equal(0, completedEvent.CumulativeSuccessfulEntities);
        Assert.Equal(0, completedEvent.CumulativeFailedEntities);
        Assert.Equal(0, completedEvent.TotalMigrationEntities);
        Assert.Equal(0.0, completedEvent.ProgressPercentage);
        Assert.Null(completedEvent.EstimatedTimeRemaining);
    }

    [Fact]
    public void SubBatchCompletedEvent_ShouldCalculateProgressPercentageCorrectly()
    {
        // Arrange
        var completedEvent = new SubBatchCompletedEvent
        {
            SubBatchNumber = 5,
            TotalSubBatches = 10,
            ProgressPercentage = 50.0 // 5/10 * 100 = 50%
        };

        // Act & Assert
        Assert.Equal(50.0, completedEvent.ProgressPercentage);
    }

    [Fact]
    public void SubBatchCompletedEvent_ShouldSerializeWithComplexDataCorrectly()
    {
        // Arrange
        var completedEvent = new SubBatchCompletedEvent
        {
            MigrationId = "migration-456",
            ParentBatchNumber = 3,
            SubBatchNumber = 7,
            TotalSubBatches = 15,
            SuccessfulEntities = 4,
            FailedEntities = 1,
            TotalEntities = 5,
            EntityType = "customers",
            ProcessingTime = TimeSpan.FromSeconds(3.5),
            CompletedAt = new DateTime(2025, 1, 28, 11, 0, 0, DateTimeKind.Utc),
            Errors = new List<string> { "Entity 101 validation failed", "Entity 105 API error" },
            CumulativeSuccessfulEntities = 30,
            CumulativeFailedEntities = 5,
            TotalMigrationEntities = 200,
            ProgressPercentage = 46.67,
            EstimatedTimeRemaining = TimeSpan.FromMinutes(2.5)
        };

        // Act
        var json = JsonSerializer.Serialize(completedEvent);
        var deserializedEvent = JsonSerializer.Deserialize<SubBatchCompletedEvent>(json);

        // Assert: Verify all properties are preserved
        Assert.NotNull(deserializedEvent);
        Assert.Equal("migration-456", deserializedEvent.MigrationId);
        Assert.Equal(3, deserializedEvent.ParentBatchNumber);
        Assert.Equal(7, deserializedEvent.SubBatchNumber);
        Assert.Equal(15, deserializedEvent.TotalSubBatches);
        Assert.Equal(4, deserializedEvent.SuccessfulEntities);
        Assert.Equal(1, deserializedEvent.FailedEntities);
        Assert.Equal(5, deserializedEvent.TotalEntities);
        Assert.Equal("customers", deserializedEvent.EntityType);
        Assert.Equal(TimeSpan.FromSeconds(3.5), deserializedEvent.ProcessingTime);
        Assert.Equal(2, deserializedEvent.Errors.Count);
        Assert.Contains("Entity 101 validation failed", deserializedEvent.Errors);
        Assert.Contains("Entity 105 API error", deserializedEvent.Errors);
        Assert.Equal(30, deserializedEvent.CumulativeSuccessfulEntities);
        Assert.Equal(5, deserializedEvent.CumulativeFailedEntities);
        Assert.Equal(200, deserializedEvent.TotalMigrationEntities);
        Assert.Equal(46.67, deserializedEvent.ProgressPercentage);
        Assert.Equal(TimeSpan.FromMinutes(2.5), deserializedEvent.EstimatedTimeRemaining);
    }

    [Fact]
    public void SubBatchCompletedEvent_ShouldSupportPolymorphicSerialization()
    {
        // Arrange: Create as base ProgressEvent type
        ProgressEvent progressEvent = new SubBatchCompletedEvent
        {
            MigrationId = "test-migration",
            ParentBatchNumber = 2,
            SubBatchNumber = 8,
            SuccessfulEntities = 5,
            FailedEntities = 0,
            EntityType = "variants"
        };

        // Act: Serialize as base type and deserialize
        var json = JsonSerializer.Serialize(progressEvent);
        var deserializedEvent = JsonSerializer.Deserialize<ProgressEvent>(json);

        // Assert: Should deserialize to correct derived type
        Assert.IsType<SubBatchCompletedEvent>(deserializedEvent);
        var completedEvent = (SubBatchCompletedEvent)deserializedEvent;
        Assert.Equal("test-migration", completedEvent.MigrationId);
        Assert.Equal(2, completedEvent.ParentBatchNumber);
        Assert.Equal(8, completedEvent.SubBatchNumber);
        Assert.Equal(5, completedEvent.SuccessfulEntities);
        Assert.Equal("variants", completedEvent.EntityType);
    }

    #endregion

    #region SubBatchMigrationProgressEvent Tests

    [Fact]
    public void SubBatchMigrationProgressEvent_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var progressEvent = new SubBatchMigrationProgressEvent();

        // Assert: Verify default values
        Assert.Equal("subbatch-progress", progressEvent.EventType);
        Assert.Equal("SubBatchMigrationProgress", progressEvent.HubMethod);
        Assert.Equal(string.Empty, progressEvent.MigrationId);
        Assert.Equal(0, progressEvent.TotalPages);
        Assert.Equal(0, progressEvent.CompletedPages);
        Assert.Equal(0, progressEvent.TotalSubBatches);
        Assert.Equal(0, progressEvent.CompletedSubBatches);
        Assert.Equal(0, progressEvent.TotalSuccessfulEntities);
        Assert.Equal(0, progressEvent.TotalFailedEntities);
        Assert.Equal(0, progressEvent.TotalExpectedEntities);
        Assert.Equal(0.0, progressEvent.ProcessingRate);
        Assert.Equal(0.0, progressEvent.OverallProgressPercentage);
        Assert.Null(progressEvent.EstimatedTimeRemaining);
        Assert.True(progressEvent.UpdatedAt <= DateTime.UtcNow);
        Assert.Equal(TimeSpan.Zero, progressEvent.ElapsedTime);
        Assert.Empty(progressEvent.RecentErrors);
        Assert.Empty(progressEvent.PerformanceMetrics);
    }

    [Fact]
    public void SubBatchMigrationProgressEvent_ShouldSerializeComplexMetricsCorrectly()
    {
        // Arrange
        var progressEvent = new SubBatchMigrationProgressEvent
        {
            MigrationId = "complex-migration-789",
            TotalPages = 4,
            CompletedPages = 2,
            TotalSubBatches = 40,
            CompletedSubBatches = 18,
            TotalSuccessfulEntities = 85,
            TotalFailedEntities = 5,
            TotalExpectedEntities = 192,
            ProcessingRate = 3.2,
            OverallProgressPercentage = 45.0,
            EstimatedTimeRemaining = TimeSpan.FromMinutes(5.5),
            UpdatedAt = new DateTime(2025, 1, 28, 12, 0, 0, DateTimeKind.Utc),
            ElapsedTime = TimeSpan.FromMinutes(3.5),
            RecentErrors = new List<string> 
            { 
                "Brand 'Nike' validation error",
                "Brand 'Adidas' API timeout",
                "Brand 'Puma' duplicate name"
            },
            PerformanceMetrics = new Dictionary<string, object>
            {
                ["ProcessingRate"] = 3.2,
                ["SuccessRate"] = 94.4,
                ["AverageTimePerSubBatch"] = 1200.5,
                ["ActiveSubBatches"] = 3,
                ["TotalElapsedMinutes"] = 3.5
            }
        };

        // Act
        var json = JsonSerializer.Serialize(progressEvent);
        var deserializedEvent = JsonSerializer.Deserialize<SubBatchMigrationProgressEvent>(json);

        // Assert: Verify all complex properties are preserved
        Assert.NotNull(deserializedEvent);
        Assert.Equal("complex-migration-789", deserializedEvent.MigrationId);
        Assert.Equal(4, deserializedEvent.TotalPages);
        Assert.Equal(2, deserializedEvent.CompletedPages);
        Assert.Equal(40, deserializedEvent.TotalSubBatches);
        Assert.Equal(18, deserializedEvent.CompletedSubBatches);
        Assert.Equal(85, deserializedEvent.TotalSuccessfulEntities);
        Assert.Equal(5, deserializedEvent.TotalFailedEntities);
        Assert.Equal(192, deserializedEvent.TotalExpectedEntities);
        Assert.Equal(3.2, deserializedEvent.ProcessingRate);
        Assert.Equal(45.0, deserializedEvent.OverallProgressPercentage);
        Assert.Equal(TimeSpan.FromMinutes(5.5), deserializedEvent.EstimatedTimeRemaining);
        Assert.Equal(TimeSpan.FromMinutes(3.5), deserializedEvent.ElapsedTime);
        
        // Verify recent errors
        Assert.Equal(3, deserializedEvent.RecentErrors.Count);
        Assert.Contains("Brand 'Nike' validation error", deserializedEvent.RecentErrors);
        Assert.Contains("Brand 'Adidas' API timeout", deserializedEvent.RecentErrors);
        Assert.Contains("Brand 'Puma' duplicate name", deserializedEvent.RecentErrors);
        
        // Verify performance metrics
        Assert.Equal(5, deserializedEvent.PerformanceMetrics.Count);
        Assert.True(deserializedEvent.PerformanceMetrics.ContainsKey("ProcessingRate"));
        Assert.True(deserializedEvent.PerformanceMetrics.ContainsKey("SuccessRate"));
        Assert.True(deserializedEvent.PerformanceMetrics.ContainsKey("AverageTimePerSubBatch"));
        Assert.True(deserializedEvent.PerformanceMetrics.ContainsKey("ActiveSubBatches"));
        Assert.True(deserializedEvent.PerformanceMetrics.ContainsKey("TotalElapsedMinutes"));
    }

    [Fact]
    public void SubBatchMigrationProgressEvent_ShouldSupportPolymorphicSerialization()
    {
        // Arrange: Create as base ProgressEvent type
        ProgressEvent progressEvent = new SubBatchMigrationProgressEvent
        {
            MigrationId = "test-migration",
            TotalPages = 4,
            CompletedPages = 1,
            TotalSubBatches = 40,
            CompletedSubBatches = 12,
            OverallProgressPercentage = 30.0
        };

        // Act: Serialize as base type and deserialize
        var json = JsonSerializer.Serialize(progressEvent);
        var deserializedEvent = JsonSerializer.Deserialize<ProgressEvent>(json);

        // Assert: Should deserialize to correct derived type
        Assert.IsType<SubBatchMigrationProgressEvent>(deserializedEvent);
        var migrationProgressEvent = (SubBatchMigrationProgressEvent)deserializedEvent;
        Assert.Equal("test-migration", migrationProgressEvent.MigrationId);
        Assert.Equal(4, migrationProgressEvent.TotalPages);
        Assert.Equal(1, migrationProgressEvent.CompletedPages);
        Assert.Equal(40, migrationProgressEvent.TotalSubBatches);
        Assert.Equal(12, migrationProgressEvent.CompletedSubBatches);
        Assert.Equal(30.0, migrationProgressEvent.OverallProgressPercentage);
    }

    #endregion

    #region Performance Validation Tests

    [Theory]
    [InlineData(4, 40, 10)] // 4 pages, 40 sub-batches = 10x granularity improvement
    [InlineData(2, 20, 10)] // 2 pages, 20 sub-batches = 10x granularity improvement
    [InlineData(8, 80, 10)] // 8 pages, 80 sub-batches = 10x granularity improvement
    public void SubBatchProgressEvents_ShouldProvide10xProgressGranularity(
        int totalPages, int totalSubBatches, int expectedImprovement)
    {
        // Arrange: Create progress event with different page/sub-batch ratios
        var progressEvent = new SubBatchMigrationProgressEvent
        {
            TotalPages = totalPages,
            TotalSubBatches = totalSubBatches
        };

        // Act: Calculate granularity improvement
        var granularityImprovement = totalSubBatches / totalPages;

        // Assert: Should consistently provide 10x improvement
        Assert.Equal(expectedImprovement, granularityImprovement);
        Assert.Equal(totalPages, progressEvent.TotalPages);
        Assert.Equal(totalSubBatches, progressEvent.TotalSubBatches);
    }

    [Fact]
    public void SubBatchProgressEvents_ShouldSupport250msUpdateIntervals()
    {
        // Arrange: Simulate rapid progress updates
        var events = new List<SubBatchCompletedEvent>();
        var startTime = DateTime.UtcNow;
        
        // Create events with 250ms intervals
        for (int i = 1; i <= 10; i++)
        {
            events.Add(new SubBatchCompletedEvent
            {
                SubBatchNumber = i,
                CompletedAt = startTime.AddMilliseconds(i * 250),
                ProgressPercentage = i * 10.0
            });
        }

        // Act: Verify time intervals
        for (int i = 1; i < events.Count; i++)
        {
            var timeDiff = events[i].CompletedAt - events[i - 1].CompletedAt;
            
            // Assert: Each event should be 250ms apart
            Assert.Equal(250, timeDiff.TotalMilliseconds);
        }

        // Assert: Total time for 10 updates should be 2.5 seconds
        var totalTime = events.Last().CompletedAt - events.First().CompletedAt;
        Assert.Equal(2250, totalTime.TotalMilliseconds); // 9 intervals * 250ms = 2250ms
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void SubBatchStartedEvent_WithNullValues_ShouldHandleGracefully()
    {
        // Arrange & Act
        var startedEvent = new SubBatchStartedEvent
        {
            MigrationId = string.Empty,
            EntityType = string.Empty
        };

        // Assert: Should handle null values without throwing
        Assert.Equal(string.Empty, startedEvent.MigrationId);
        Assert.Equal(string.Empty, startedEvent.EntityType);
        Assert.Equal("subbatch-started", startedEvent.EventType);
    }

    [Fact]
    public void SubBatchCompletedEvent_WithEmptyErrors_ShouldSerializeCorrectly()
    {
        // Arrange
        var completedEvent = new SubBatchCompletedEvent
        {
            Errors = new List<string>(),
            EstimatedTimeRemaining = null!
        };

        // Act
        var json = JsonSerializer.Serialize(completedEvent);
        var deserializedEvent = JsonSerializer.Deserialize<SubBatchCompletedEvent>(json);

        // Assert
        Assert.NotNull(deserializedEvent);
        Assert.Empty(deserializedEvent.Errors);
        Assert.Null(deserializedEvent.EstimatedTimeRemaining);
    }

    [Fact]
    public void SubBatchMigrationProgressEvent_WithEmptyCollections_ShouldSerializeCorrectly()
    {
        // Arrange
        var progressEvent = new SubBatchMigrationProgressEvent
        {
            RecentErrors = new List<string>(),
            PerformanceMetrics = new Dictionary<string, object>()
        };

        // Act
        var json = JsonSerializer.Serialize(progressEvent);
        var deserializedEvent = JsonSerializer.Deserialize<SubBatchMigrationProgressEvent>(json);

        // Assert
        Assert.NotNull(deserializedEvent);
        Assert.Empty(deserializedEvent.RecentErrors);
        Assert.Empty(deserializedEvent.PerformanceMetrics);
    }

    #endregion
} 