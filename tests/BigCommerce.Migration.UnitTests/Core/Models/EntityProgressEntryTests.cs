using System;
using BigCommerce.Migration.Core.Models;
using Xunit;
using FluentAssertions;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for EntityProgressEntry model
/// Following TDD principles - test the behavior we want before implementation
/// </summary>
public class EntityProgressEntryTests
{
    [Fact]
    public void EntityProgressEntry_WhenCreated_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var entry = new EntityProgressEntry();

        // Assert
        entry.MigrationId.Should().BeEmpty();
        entry.EntityType.Should().BeEmpty();
        entry.TotalCount.Should().Be(0);
        entry.ProcessedCount.Should().Be(0);
        entry.SuccessCount.Should().Be(0);
        entry.FailureCount.Should().Be(0);
        entry.ProgressPercentage.Should().Be(0.0);
        entry.Status.Should().BeEmpty();
        entry.ProcessingTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void EntityProgressEntry_WhenAllPropertiesSet_ShouldRetainValues()
    {
        // Arrange
        var migrationId = "test-migration-123";
        var entityType = "categories";
        var startTime = DateTime.UtcNow.AddMinutes(-10);
        var endTime = DateTime.UtcNow;
        var processingTime = endTime - startTime;

        // Act
        var entry = new EntityProgressEntry
        {
            MigrationId = migrationId,
            EntityType = entityType,
            TotalCount = 100,
            ProcessedCount = 75,
            SuccessCount = 70,
            FailureCount = 5,
            ProgressPercentage = 75.0,
            Status = "processing",
            StartTime = startTime,
            EndTime = endTime,
            ProcessingTime = processingTime,
            CreatedAt = startTime,
            UpdatedAt = endTime
        };

        // Assert
        entry.MigrationId.Should().Be(migrationId);
        entry.EntityType.Should().Be(entityType);
        entry.TotalCount.Should().Be(100);
        entry.ProcessedCount.Should().Be(75);
        entry.SuccessCount.Should().Be(70);
        entry.FailureCount.Should().Be(5);
        entry.ProgressPercentage.Should().Be(75.0);
        entry.Status.Should().Be("processing");
        entry.StartTime.Should().Be(startTime);
        entry.EndTime.Should().Be(endTime);
        entry.ProcessingTime.Should().Be(processingTime);
        entry.CreatedAt.Should().Be(startTime);
        entry.UpdatedAt.Should().Be(endTime);
    }

    [Theory]
    [InlineData(0, 100, 0.0)]
    [InlineData(25, 100, 25.0)]
    [InlineData(50, 100, 50.0)]
    [InlineData(100, 100, 100.0)]
    public void EntityProgressEntry_ProgressPercentageCalculation_ShouldBeAccurate(int processed, int total, double expectedPercentage)
    {
        // Arrange & Act
        var entry = new EntityProgressEntry
        {
            TotalCount = total,
            ProcessedCount = processed,
            ProgressPercentage = total > 0 ? (double)processed / total * 100.0 : 0.0
        };

        // Assert
        entry.ProgressPercentage.Should().Be(expectedPercentage);
    }

    [Fact]
    public void EntityProgressEntry_WhenProcessedCountExceedsTotal_ShouldStillBeValid()
    {
        // Arrange & Act
        var entry = new EntityProgressEntry
        {
            TotalCount = 100,
            ProcessedCount = 105, // Edge case: processed more than total
            SuccessCount = 100,
            FailureCount = 5
        };

        // Assert
        entry.ProcessedCount.Should().Be(105);
        entry.TotalCount.Should().Be(100);
        // Note: This tests that the model can handle edge cases gracefully
    }

    [Fact]
    public void EntityProgressEntry_ProcessingTimeCalculation_ShouldBeAccurate()
    {
        // Arrange
        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddMinutes(30);
        var expectedProcessingTime = TimeSpan.FromMinutes(30);

        // Act
        var entry = new EntityProgressEntry
        {
            StartTime = startTime,
            EndTime = endTime,
            ProcessingTime = endTime - startTime
        };

        // Assert
        entry.ProcessingTime.Should().Be(expectedProcessingTime);
        entry.ProcessingTime.TotalMinutes.Should().Be(30);
    }

    [Fact]
    public void EntityProgressEntry_WhenEndTimeIsNull_ProcessingTimeShouldHandleGracefully()
    {
        // Arrange & Act
        var entry = new EntityProgressEntry
        {
            StartTime = DateTime.UtcNow,
            EndTime = null,
            ProcessingTime = TimeSpan.Zero // Should be handled gracefully
        };

        // Assert
        entry.EndTime.Should().BeNull();
        entry.ProcessingTime.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pending")]
    [InlineData("processing")]
    [InlineData("completed")]
    [InlineData("failed")]
    public void EntityProgressEntry_StatusValues_ShouldAcceptValidStatuses(string status)
    {
        // Arrange & Act
        var entry = new EntityProgressEntry
        {
            Status = status
        };

        // Assert
        entry.Status.Should().Be(status);
    }
} 