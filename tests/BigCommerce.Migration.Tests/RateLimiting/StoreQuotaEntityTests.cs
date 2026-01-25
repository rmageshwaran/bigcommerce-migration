using FluentAssertions;
using BigCommerce.Migration.Core.Models.RateLimiting;
using Xunit;

namespace BigCommerce.Migration.Tests.RateLimiting;

public class StoreQuotaEntityTests
{
    [Theory]
    [InlineData(1000, 900, 0.3, 0.1, QuotaHealthStatus.Healthy)] // 90% remaining
    [InlineData(1000, 250, 0.3, 0.1, QuotaHealthStatus.Warning)] // 25% remaining
    [InlineData(1000, 50, 0.3, 0.1, QuotaHealthStatus.Critical)] // 5% remaining
    public void GetHealthStatus_VariousQuotaLevels_ReturnsCorrectStatus(
        int totalQuota,
        int remainingTokens,
        double healthyThreshold,
        double criticalThreshold,
        QuotaHealthStatus expectedStatus)
    {
        // Arrange
        var entity = new StoreQuotaEntity
        {
            CurrentQuota = totalQuota,
            RemainingTokens = remainingTokens
        };

        // Act
        var status = entity.GetHealthStatus(healthyThreshold, criticalThreshold);

        // Assert
        status.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData(1000, 900, 0.15, 0.3, 0.1, 765)] // Healthy: 15% buffer
    [InlineData(1000, 250, 0.15, 0.3, 0.1, 187)] // Warning: 25% buffer
    [InlineData(1000, 50, 0.15, 0.3, 0.1, 25)]   // Critical: 50% buffer
    public void CalculateSafeTokens_AdaptiveBuffer_ReturnsCorrectAmount(
        int totalQuota,
        int remainingTokens,
        double safetyBuffer,
        double healthyThreshold,
        double criticalThreshold,
        int expectedSafeTokens)
    {
        // Arrange
        var entity = new StoreQuotaEntity
        {
            CurrentQuota = totalQuota,
            RemainingTokens = remainingTokens
        };

        // Act
        var safeTokens = entity.CalculateSafeTokens(safetyBuffer, healthyThreshold, criticalThreshold);

        // Assert
        safeTokens.Should().Be(expectedSafeTokens);
    }

    [Fact]
    public void CalculateSafeTokens_NoRemainingTokens_ReturnsZero()
    {
        // Arrange
        var entity = new StoreQuotaEntity
        {
            CurrentQuota = 1000,
            RemainingTokens = 0
        };

        // Act
        var safeTokens = entity.CalculateSafeTokens(0.15);

        // Assert
        safeTokens.Should().Be(0);
    }

    [Fact]
    public void GetQuotaUtilization_CalculatesCorrectPercentage()
    {
        // Arrange
        var entity = new StoreQuotaEntity
        {
            CurrentQuota = 1000,
            RemainingTokens = 250 // 75% utilized
        };

        // Act
        var utilization = entity.GetQuotaUtilization();

        // Assert
        utilization.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public void IsStale_DataOlderThanMaxAge_ReturnsTrue()
    {
        // Arrange
        var entity = new StoreQuotaEntity
        {
            LastUpdated = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var maxAge = TimeSpan.FromMinutes(1);

        // Act
        var isStale = entity.IsStale(maxAge);

        // Assert
        isStale.Should().BeTrue();
    }

    [Fact]
    public void IsStale_FreshData_ReturnsFalse()
    {
        // Arrange
        var entity = new StoreQuotaEntity
        {
            LastUpdated = DateTimeOffset.UtcNow.AddSeconds(-30)
        };
        var maxAge = TimeSpan.FromMinutes(1);

        // Act
        var isStale = entity.IsStale(maxAge);

        // Assert
        isStale.Should().BeFalse();
    }

    [Fact]
    public void UpdateFromBigCommerceHeaders_UpdatesAllFields()
    {
        // Arrange
        var entity = new StoreQuotaEntity();
        var quota = 1000;
        var remaining = 800;
        var resetTime = DateTimeOffset.UtcNow.AddSeconds(30);
        var windowSeconds = 60;
        var source = "test";

        // Act
        entity.UpdateFromBigCommerceHeaders(quota, remaining, resetTime, windowSeconds, source);

        // Assert
        entity.CurrentQuota.Should().Be(quota);
        entity.RemainingTokens.Should().Be(remaining);
        entity.QuotaResetTime.Should().Be(resetTime);
        entity.WindowSeconds.Should().Be(windowSeconds);
        entity.LastUpdateSource.Should().Be(source);
        entity.ConsecutiveFailures.Should().Be(0);
        entity.LastUpdated.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RecordFailure_IncrementsCounter()
    {
        // Arrange
        var entity = new StoreQuotaEntity
        {
            ConsecutiveFailures = 1
        };

        // Act
        entity.RecordFailure();

        // Assert
        entity.ConsecutiveFailures.Should().Be(2);
        entity.LastUpdated.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }
}