using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Tests.Unit.Services;

/// <summary>
/// Simple unit tests for RowNumberCounter model and basic validation
/// Tests the core RowNumber functionality without complex mocking
/// </summary>
public class RowNumberServiceSimpleTests
{
    [Fact]
    public void RowNumberCounter_Create_SetsCorrectInitialValues()
    {
        // Arrange
        var migrationId = "test-migration";
        var entityType = "products";

        // Act
        var counter = RowNumberCounter.Create(migrationId, entityType);

        // Assert
        Assert.Equal(migrationId, counter.MigrationId);
        Assert.Equal(entityType, counter.EntityType);
        Assert.Equal(0, counter.CurrentValue);
        Assert.Equal(0, counter.TotalAllocated);
        Assert.Equal($"{migrationId}_{entityType}", counter.PartitionKey);
        Assert.Equal("counter", counter.RowKey);
    }

    [Fact]
    public void RowNumberCounter_GetNextRowNumber_IncrementsCorrectly()
    {
        // Arrange
        var counter = RowNumberCounter.Create("migration1", "products");

        // Act
        var first = counter.GetNextRowNumber();
        var second = counter.GetNextRowNumber();
        var third = counter.GetNextRowNumber();

        // Assert
        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, third);
        Assert.Equal(3, counter.CurrentValue);
        Assert.Equal(3, counter.TotalAllocated);
    }

    [Fact]
    public void RowNumberCounter_AllocateRange_ReturnsCorrectRange()
    {
        // Arrange
        var counter = RowNumberCounter.Create("migration1", "products");
        var count = 10;

        // Act
        var (start, end) = counter.AllocateRange(count);

        // Assert
        Assert.Equal(1, start);
        Assert.Equal(10, end);
        Assert.Equal(10, counter.CurrentValue);
        Assert.Equal(10, counter.TotalAllocated);
        Assert.Equal(count, end - start + 1);
    }

    [Fact]
    public void RowNumberCounter_AllocateRange_AfterPreviousAllocations_ContinuesSequence()
    {
        // Arrange
        var counter = RowNumberCounter.Create("migration1", "products");
        counter.GetNextRowNumber(); // 1
        counter.GetNextRowNumber(); // 2

        // Act
        var (start, end) = counter.AllocateRange(5);

        // Assert
        Assert.Equal(3, start);
        Assert.Equal(7, end);
        Assert.Equal(7, counter.CurrentValue);
        Assert.Equal(7, counter.TotalAllocated);
    }

    [Fact]
    public void RowNumberCounter_IsValid_ValidatesCorrectly()
    {
        // Arrange
        var counter = RowNumberCounter.Create("migration1", "products");

        // Act & Assert
        Assert.True(counter.IsValid());

        // Test invalid cases
        counter.MigrationId = "";
        Assert.False(counter.IsValid());

        counter.MigrationId = "migration1";
        counter.EntityType = "";
        Assert.False(counter.IsValid());
    }

    [Fact]
    public void RowNumberCounter_AllocateRange_ThrowsForInvalidCount()
    {
        // Arrange
        var counter = RowNumberCounter.Create("migration1", "products");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => counter.AllocateRange(0));
        Assert.Throws<ArgumentException>(() => counter.AllocateRange(-5));
    }

    [Fact]
    public void RowNumberServiceMetrics_IsPerformanceAcceptable_ValidatesCorrectly()
    {
        // Arrange
        var goodMetrics = new RowNumberServiceMetrics
        {
            AverageLatencyMs = 50.0,
            P95LatencyMs = 100.0,
            RequestsPerSecond = 100.0,
            FailedRequestsAfterRetries = 0
        };

        var badMetrics = new RowNumberServiceMetrics
        {
            AverageLatencyMs = 200.0, // Too slow
            P95LatencyMs = 100.0,
            RequestsPerSecond = 100.0,
            FailedRequestsAfterRetries = 0
        };

        // Act & Assert
        Assert.True(goodMetrics.IsPerformanceAcceptable());
        Assert.False(badMetrics.IsPerformanceAcceptable());
    }
}
