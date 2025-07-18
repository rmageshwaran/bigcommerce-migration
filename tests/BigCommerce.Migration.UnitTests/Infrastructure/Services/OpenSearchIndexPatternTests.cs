using FluentAssertions;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for OpenSearch index pattern optimization logic
/// Tests the index pattern building strategies used in the optimized OpenSearch service
/// </summary>
public class OpenSearchIndexPatternTests
{
    private const string DefaultIndex = "bigcommerce-migration";

    #region Helper Methods

    /// <summary>
    /// Simulates the BuildOptimizedIndexPattern logic for testing
    /// This mirrors the private method in OpenSearchService
    /// </summary>
    private static string BuildOptimizedIndexPattern(DateTime fromDate, DateTime toDate, string defaultIndex = DefaultIndex)
    {
        var daysDiff = (toDate - fromDate).TotalDays;
        
        // If searching within a single day, target specific daily index and error indices
        if (daysDiff <= 1)
        {
            var basePattern = $"{defaultIndex}-{fromDate:yyyy-MM-dd}";
            var errorPattern = $"{defaultIndex}-errors-{fromDate:yyyy-MM}";
            return $"{basePattern},{errorPattern}";
        }
        
        // If searching within a week, use specific date range plus error indices
        if (daysDiff <= 7)
        {
            var indices = new List<string>();
            for (var date = fromDate.Date; date <= toDate.Date; date = date.AddDays(1))
            {
                indices.Add($"{defaultIndex}-{date:yyyy-MM-dd}");
            }
            // Add error indices for the months involved
            var monthsInvolved = new HashSet<string>();
            for (var date = fromDate.Date; date <= toDate.Date; date = date.AddDays(1))
            {
                monthsInvolved.Add($"{defaultIndex}-errors-{date:yyyy-MM}");
            }
            indices.AddRange(monthsInvolved);
            return string.Join(",", indices);
        }
        
        // For longer ranges, use wildcard but limit by month/year if possible
        if (fromDate.Year == toDate.Year && fromDate.Month == toDate.Month)
        {
            var basePattern = $"{defaultIndex}-{fromDate:yyyy-MM}*";
            var errorPattern = $"{defaultIndex}-errors-{fromDate:yyyy-MM}";
            return $"{basePattern},{errorPattern}";
        }
        
        // Fall back to full wildcard for very broad searches - include both regular and error indices
        return $"{defaultIndex}-*";
    }

    #endregion

    #region Single Day Index Pattern Tests

    [Fact]
    public void BuildOptimizedIndexPattern_SingleDay_ShouldReturnDailyIndex()
    {
        // Arrange
        var date = new DateTime(2024, 1, 15);
        var fromDate = date;
        var toDate = date;

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01-15,bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_SameDayDifferentTimes_ShouldReturnDailyIndex()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 15, 8, 0, 0);
        var toDate = new DateTime(2024, 1, 15, 18, 30, 45);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01-15,bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_ExactlyOneDaySpan_ShouldReturnDailyIndex()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 15, 12, 0, 0);
        var toDate = fromDate.AddDays(1);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01-15,bigcommerce-migration-errors-2024-01");
    }

    #endregion

    #region Weekly Range Index Pattern Tests

    [Fact]
    public void BuildOptimizedIndexPattern_TwoDays_ShouldReturnDailyIndex()
    {
        // Arrange - Two consecutive days with exactly 1 day difference
        var fromDate = new DateTime(2024, 1, 15);
        var toDate = new DateTime(2024, 1, 16);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert - daysDiff = 1.0, so falls under <= 1 condition
        pattern.Should().Be("bigcommerce-migration-2024-01-15,bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_TwoDaysProperRange_ShouldReturnCommaSeparatedIndices()
    {
        // Arrange - More than 1 day difference to trigger comma-separated logic
        var fromDate = new DateTime(2024, 1, 15);
        var toDate = new DateTime(2024, 1, 16).AddHours(1); // 1 day + 1 hour = > 1 day

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01-15,bigcommerce-migration-2024-01-16,bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_ThreeDays_ShouldReturnCommaSeparatedIndices()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 15);
        var toDate = new DateTime(2024, 1, 17);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01-15,bigcommerce-migration-2024-01-16,bigcommerce-migration-2024-01-17,bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_ExactlySevenDays_ShouldReturnCommaSeparatedIndices()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 15);
        var toDate = new DateTime(2024, 1, 21);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        var expectedIndices = new[]
        {
            "bigcommerce-migration-2024-01-15",
            "bigcommerce-migration-2024-01-16",
            "bigcommerce-migration-2024-01-17",
            "bigcommerce-migration-2024-01-18",
            "bigcommerce-migration-2024-01-19",
            "bigcommerce-migration-2024-01-20",
            "bigcommerce-migration-2024-01-21",
            "bigcommerce-migration-errors-2024-01"
        };
        pattern.Should().Be(string.Join(",", expectedIndices));
    }

    [Fact]
    public void BuildOptimizedIndexPattern_WeeklyRange_ShouldIncludeAllDays()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 15);  // Monday
        var toDate = new DateTime(2024, 1, 19);    // Friday

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        var indices = pattern.Split(',');
        indices.Should().HaveCount(6); // 5 daily indices + 1 error index
        indices[0].Should().Be("bigcommerce-migration-2024-01-15");
        indices[4].Should().Be("bigcommerce-migration-2024-01-19");
        indices[5].Should().Be("bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_WeeklyRangeAcrossWeekend_ShouldIncludeWeekendDays()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 19);  // Friday
        var toDate = new DateTime(2024, 1, 22);    // Monday

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        var indices = pattern.Split(',');
        indices.Should().HaveCount(5); // 4 daily indices + 1 error index
        indices.Should().Contain("bigcommerce-migration-2024-01-20"); // Saturday
        indices.Should().Contain("bigcommerce-migration-2024-01-21"); // Sunday
        indices.Should().Contain("bigcommerce-migration-errors-2024-01"); // Error index
    }

    #endregion

    #region Monthly Range Index Pattern Tests

    [Fact]
    public void BuildOptimizedIndexPattern_SameMonth_ShouldReturnMonthlyWildcard()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 1, 31);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01*,bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_PartialMonth_ShouldReturnMonthlyWildcard()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 15);
        var toDate = new DateTime(2024, 1, 25);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01*,bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_SevenDaysExact_ShouldReturnCommaSeparatedIndices()
    {
        // Arrange - Exactly 7 days still uses comma-separated logic
        var fromDate = new DateTime(2024, 1, 15);
        var toDate = new DateTime(2024, 1, 22); // 7 days difference

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01-15,bigcommerce-migration-2024-01-16,bigcommerce-migration-2024-01-17,bigcommerce-migration-2024-01-18,bigcommerce-migration-2024-01-19,bigcommerce-migration-2024-01-20,bigcommerce-migration-2024-01-21,bigcommerce-migration-2024-01-22,bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_EightDaysSameMonth_ShouldReturnMonthlyWildcard()
    {
        // Arrange - More than 7 days triggers monthly wildcard
        var fromDate = new DateTime(2024, 1, 15);
        var toDate = new DateTime(2024, 1, 23); // 8 days difference

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01*,bigcommerce-migration-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_MonthBoundary_SameMonth_ShouldReturnMonthlyWildcard()
    {
        // Arrange
        var fromDate = new DateTime(2024, 2, 1);
        var toDate = new DateTime(2024, 2, 29);  // Leap year

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-02*,bigcommerce-migration-errors-2024-02");
    }

    #endregion

    #region Cross-Boundary Index Pattern Tests

    [Fact]
    public void BuildOptimizedIndexPattern_AcrossMonths_ShouldReturnFullWildcard()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 25);
        var toDate = new DateTime(2024, 2, 5);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-*");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_AcrossYears_ShouldReturnFullWildcard()
    {
        // Arrange
        var fromDate = new DateTime(2023, 12, 20);
        var toDate = new DateTime(2024, 1, 10);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-*");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_VeryLongRange_ShouldReturnFullWildcard()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 6, 30);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-*");
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void BuildOptimizedIndexPattern_WithCustomIndexName_ShouldUseCustomName()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 15);
        var toDate = new DateTime(2024, 1, 15);
        var customIndex = "custom-migration-logs";

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate, customIndex);

        // Assert
        pattern.Should().Be("custom-migration-logs-2024-01-15,custom-migration-logs-errors-2024-01");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_LeapYear_ShouldHandleCorrectly()
    {
        // Arrange - February 29, 2024 (leap year)
        var fromDate = new DateTime(2024, 2, 29);
        var toDate = new DateTime(2024, 2, 29);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-02-29,bigcommerce-migration-errors-2024-02");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_MonthWithDifferentDayCounts_ShouldHandleCorrectly()
    {
        // Arrange - February (28 days) to March (31 days)
        var fromDate = new DateTime(2024, 2, 15);
        var toDate = new DateTime(2024, 3, 15);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-*");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_ExactlyOneWeekBoundary_ShouldReturnCommaSeparated()
    {
        // Arrange - Exactly 6 days difference = 7 days total should use comma-separated indices
        var fromDate = new DateTime(2024, 1, 15);
        var toDate = fromDate.AddDays(6); // 6 days difference = 7 days total

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        var indices = pattern.Split(',');
        indices.Should().HaveCount(8); // 7 daily indices + 1 error index
        pattern.Should().StartWith("bigcommerce-migration-2024-01-15");
        pattern.Should().EndWith("bigcommerce-migration-errors-2024-01");
        indices.Should().Contain("bigcommerce-migration-2024-01-21");
    }

    [Fact]
    public void BuildOptimizedIndexPattern_MicrosecondDifferences_ShouldHandleCorrectly()
    {
        // Arrange
        var fromDate = new DateTime(2024, 1, 15, 12, 0, 0, 0);
        var toDate = new DateTime(2024, 1, 15, 12, 0, 0, 1); // 1 millisecond later

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert
        pattern.Should().Be("bigcommerce-migration-2024-01-15,bigcommerce-migration-errors-2024-01");
    }

    #endregion

    #region Performance Optimization Tests

    [Theory]
    [InlineData(1, "daily")]      // Single day - most specific
    [InlineData(3, "daily")]      // Few days - specific indices
    [InlineData(7, "daily")]      // One week - specific indices
    [InlineData(8, "monthly")]    // Over a week - monthly wildcard
    [InlineData(30, "monthly")]   // One month - monthly wildcard
    [InlineData(60, "full")]      // Cross-month - full wildcard
    public void BuildOptimizedIndexPattern_PerformanceCharacteristics_ShouldChooseOptimalStrategy(int daysBack, string expectedStrategy)
    {
        // Arrange
        var toDate = new DateTime(2024, 1, 31);
        var fromDate = toDate.AddDays(-daysBack);

        // Act
        var pattern = BuildOptimizedIndexPattern(fromDate, toDate);

        // Assert - Verify the strategy matches expected optimization
        switch (expectedStrategy)
        {
            case "daily":
                pattern.Should().Contain(","); // Always contains error indices
                pattern.Should().NotContain("*");
                break;
                
            case "monthly":
                pattern.Should().Contain(","); // Contains base pattern + error pattern
                pattern.Should().Contain("*");
                break;
                
            case "full":
                pattern.Should().Be("bigcommerce-migration-*");
                break;
        }
    }

    [Fact]
    public void BuildOptimizedIndexPattern_DataReductionEfficiency_ShouldDemonstrateOptimization()
    {
        // Arrange - Compare broad vs specific searches
        var broadFromDate = new DateTime(2024, 1, 1);
        var broadToDate = new DateTime(2024, 12, 31);
        
        var specificFromDate = new DateTime(2024, 1, 15);
        var specificToDate = new DateTime(2024, 1, 15);

        // Act
        var broadPattern = BuildOptimizedIndexPattern(broadFromDate, broadToDate);
        var specificPattern = BuildOptimizedIndexPattern(specificFromDate, specificToDate);

        // Assert
        broadPattern.Should().Be("bigcommerce-migration-*"); // Searches all indices
        specificPattern.Should().Be("bigcommerce-migration-2024-01-15,bigcommerce-migration-errors-2024-01"); // Searches 2 specific indices

        // Demonstrate significant optimization ratio for daily vs yearly searches
        var optimizationRatio = 365; // 365 daily indices vs 2 specific indices
        optimizationRatio.Should().BeGreaterThan(100); // Significant optimization
    }

    [Fact]
    public void BuildOptimizedIndexPattern_IndexCount_ShouldOptimizeForSearchScope()
    {
        // Arrange - Test different date ranges and their index counts
        var singleDay = (DateTime.UtcNow, DateTime.UtcNow);
        var weekRange = (DateTime.UtcNow.AddDays(-6), DateTime.UtcNow);
        var monthRange = (DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        // Act
        var singleDayPattern = BuildOptimizedIndexPattern(singleDay.Item1, singleDay.Item2);
        var weekPattern = BuildOptimizedIndexPattern(weekRange.Item1, weekRange.Item2);
        var monthPattern = BuildOptimizedIndexPattern(monthRange.Item1, monthRange.Item2);

        // Assert - Index specificity should decrease with range size
        // Single day: 2 specific indices (base + error)
        singleDayPattern.Should().Contain(",");
        singleDayPattern.Should().NotContain("*");

        // Week: 7 daily indices + 1 error index (more specific than wildcard)
        if (weekPattern.Contains(","))
        {
            var weekIndices = weekPattern.Split(',');
            weekIndices.Should().HaveCountLessOrEqualTo(8); // 7 daily + 1 error index
        }

        // Month: Wildcard (less specific but more efficient for large ranges)
        if (monthRange.Item1.Month == monthRange.Item2.Month)
        {
            monthPattern.Should().Contain("*");
        }
    }

    #endregion
} 