using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for OpenSearchQuery model
/// Tests the new structured query model for optimized OpenSearch operations
/// </summary>
public class OpenSearchQueryTests
{
    [Fact]
    public void OpenSearchQuery_DefaultValues_ShouldHaveReasonableDefaults()
    {
        // Act
        var query = new OpenSearchQuery();

        // Assert
        query.FromDate.Should().BeCloseTo(DateTime.UtcNow.AddDays(-1), TimeSpan.FromMinutes(1));
        query.ToDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        query.Level.Should().BeNull();
        query.MigrationId.Should().BeNull();
        query.EntityType.Should().BeNull();
        query.SearchTerm.Should().BeNull();
        query.Size.Should().Be(50);
        query.From.Should().Be(0);
        query.SortField.Should().Be("timestamp");
        query.SortOrder.Should().Be(SortOrder.Descending);
        query.IncludeFields.Should().BeNull();
        query.EnableHighlighting.Should().BeFalse();
    }

    [Fact]
    public void OpenSearchQuery_WithCustomValues_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var fromDate = DateTime.UtcNow.AddDays(-7);
        var toDate = DateTime.UtcNow;
        var includeFields = new[] { "timestamp", "message", "level" };

        // Act
        var query = new OpenSearchQuery
        {
            FromDate = fromDate,
            ToDate = toDate,
            Level = "Error",
            MigrationId = "test-migration-123",
            EntityType = "Products",
            SearchTerm = "API timeout",
            Size = 100,
            From = 50,
            SortField = "level",
            SortOrder = SortOrder.Ascending,
            IncludeFields = includeFields,
            EnableHighlighting = true
        };

        // Assert
        query.FromDate.Should().Be(fromDate);
        query.ToDate.Should().Be(toDate);
        query.Level.Should().Be("Error");
        query.MigrationId.Should().Be("test-migration-123");
        query.EntityType.Should().Be("Products");
        query.SearchTerm.Should().Be("API timeout");
        query.Size.Should().Be(100);
        query.From.Should().Be(50);
        query.SortField.Should().Be("level");
        query.SortOrder.Should().Be(SortOrder.Ascending);
        query.IncludeFields.Should().BeEquivalentTo(includeFields);
        query.EnableHighlighting.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 50, true)]      // Valid: Normal size
    [InlineData(1000, 0, true)]    // Valid: Max size
    [InlineData(10, 100, true)]    // Valid: With offset
    [InlineData(0, 0, false)]      // Invalid: Zero size
    [InlineData(-1, 0, false)]     // Invalid: Negative size
    [InlineData(1001, 0, false)]   // Invalid: Size too large
    [InlineData(1, -1, false)]     // Invalid: Negative offset
    public void OpenSearchQuery_IsValid_ShouldValidateCorrectly(int size, int from, bool expectedValid)
    {
        // Arrange
        var query = new OpenSearchQuery
        {
            FromDate = DateTime.UtcNow.AddDays(-1),
            ToDate = DateTime.UtcNow,
            Size = size,
            From = from
        };

        // Act
        var isValid = query.IsValid();

        // Assert
        isValid.Should().Be(expectedValid);
    }

    [Fact]
    public void OpenSearchQuery_IsValid_WithInvalidDateRange_ShouldReturnFalse()
    {
        // Arrange
        var query = new OpenSearchQuery
        {
            FromDate = DateTime.UtcNow,
            ToDate = DateTime.UtcNow.AddDays(-1), // ToDate before FromDate
            Size = 50,
            From = 0
        };

        // Act
        var isValid = query.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void OpenSearchQuery_IsValid_WithEqualDates_ShouldReturnTrue()
    {
        // Arrange
        var dateTime = DateTime.UtcNow;
        var query = new OpenSearchQuery
        {
            FromDate = dateTime,
            ToDate = dateTime,
            Size = 50,
            From = 0
        };

        // Act
        var isValid = query.IsValid();

        // Assert
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 50, 1)]     // First page
    [InlineData(50, 50, 2)]    // Second page
    [InlineData(100, 50, 3)]   // Third page
    [InlineData(150, 50, 4)]   // Fourth page
    [InlineData(0, 100, 1)]    // First page with larger size
    [InlineData(200, 100, 3)]  // Third page with larger size
    public void OpenSearchQuery_CurrentPage_ShouldCalculateCorrectly(int from, int size, int expectedPage)
    {
        // Arrange
        var query = new OpenSearchQuery
        {
            From = from,
            Size = size
        };

        // Act
        var currentPage = query.CurrentPage;

        // Assert
        currentPage.Should().Be(expectedPage);
    }

    [Fact]
    public void OpenSearchQuery_WithComplexFilters_ShouldRetainAllValues()
    {
        // Arrange
        var query = new OpenSearchQuery
        {
            Level = "Warning",
            MigrationId = "migration-456",
            EntityType = "Categories",
            SearchTerm = "rate limit exceeded",
            IncludeFields = new[] { "timestamp", "message", "migrationId", "entityType" }
        };

        // Act & Assert - All filter values should be preserved
        query.Level.Should().Be("Warning");
        query.MigrationId.Should().Be("migration-456");
        query.EntityType.Should().Be("Categories");
        query.SearchTerm.Should().Be("rate limit exceeded");
        query.IncludeFields.Should().HaveCount(4);
        query.IncludeFields.Should().Contain("timestamp");
        query.IncludeFields.Should().Contain("message");
        query.IncludeFields.Should().Contain("migrationId");
        query.IncludeFields.Should().Contain("entityType");
    }

    [Fact]
    public void OpenSearchQuery_WithEmptyIncludeFields_ShouldHandleCorrectly()
    {
        // Arrange
        var query = new OpenSearchQuery
        {
            IncludeFields = new string[0]
        };

        // Act & Assert
        query.IncludeFields.Should().NotBeNull();
        query.IncludeFields.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Critical")]
    [InlineData("")]
    [InlineData(null)]
    public void OpenSearchQuery_Level_ShouldAcceptValidLogLevels(string? level)
    {
        // Arrange & Act
        var query = new OpenSearchQuery
        {
            Level = level
        };

        // Assert
        query.Level.Should().Be(level);
    }

    [Fact]
    public void SortOrder_Enum_ShouldHaveCorrectValues()
    {
        // Assert
        ((int)SortOrder.Ascending).Should().Be(0);
        ((int)SortOrder.Descending).Should().Be(1);
    }

    [Fact]
    public void OpenSearchQuery_PaginationScenario_ShouldCalculateCorrectly()
    {
        // Arrange - Simulate getting page 3 with 25 items per page
        var query = new OpenSearchQuery
        {
            Size = 25,
            From = 50  // Skip first 50 items (2 pages * 25 items)
        };

        // Act & Assert
        query.CurrentPage.Should().Be(3);
        query.IsValid().Should().BeTrue();
    }

    [Fact]
    public void OpenSearchQuery_MaxSizeBoundary_ShouldValidateCorrectly()
    {
        // Arrange
        var validQuery = new OpenSearchQuery { Size = 1000 };
        var invalidQuery = new OpenSearchQuery { Size = 1001 };

        // Act & Assert
        validQuery.IsValid().Should().BeTrue();
        invalidQuery.IsValid().Should().BeFalse();
    }

    [Fact]
    public void OpenSearchQuery_WithLongSearchTerm_ShouldHandleCorrectly()
    {
        // Arrange
        var longSearchTerm = new string('a', 1000); // Very long search term
        var query = new OpenSearchQuery
        {
            SearchTerm = longSearchTerm
        };

        // Act & Assert
        query.SearchTerm.Should().Be(longSearchTerm);
        query.IsValid().Should().BeTrue(); // Length validation is not part of IsValid()
    }
} 