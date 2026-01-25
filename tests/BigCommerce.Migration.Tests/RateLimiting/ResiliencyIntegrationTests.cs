using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Infrastructure.Services;
using Xunit;
using FluentAssertions;

namespace BigCommerce.Migration.Tests.RateLimiting;

/// <summary>
/// Integration tests to verify rate limiting system resilience and fallback behavior
/// These tests ensure that rate limiting failures never break core application functionality
/// </summary>
public class ResiliencyIntegrationTests
{
    [Fact]
    public async Task RateLimitingSystem_WhenTableStorageFails_ApplicationContinuesToFunction()
    {
        // Arrange - Set up mocks that will fail
        var logger = new Mock<ILogger<TokenConsensusManager>>();
        var quotaTracker = new Mock<IDistributedQuotaTracker>();
        var coordinationManager = new Mock<IInstanceCoordinationManager>();
        var tableStorageFactory = new Mock<IRateLimitingTableStorageFactory>();
        var configuration = Options.Create(new DynamicRateLimitingConfiguration
        {
            Predictive = new PredictiveSettings
            {
                TokenExpirySeconds = 300,
                MaxETagRetries = 3
            }
        });

        // Make quota tracker fail with exception
        quotaTracker.Setup(x => x.GetQuotaHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Storage is down!"));

        coordinationManager.Setup(x => x.GetCurrentInstanceId())
            .Returns("test-instance");

        var tokenConsensusManager = new TokenConsensusManager(
            tableStorageFactory.Object,
            quotaTracker.Object,
            coordinationManager.Object,
            configuration,
            logger.Object);

        // Act - Call the rate limiting method that should never throw
        var result = await tokenConsensusManager.ReserveTokensAsync("test-store", CancellationToken.None).ConfigureAwait(false);

        // Assert - System returns safe fallback value instead of crashing
        result.Should().BeGreaterOrEqualTo(0, "Rate limiting system should return safe fallback value, never crash the application");

        // Verify error was logged but application continued
        logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to reserve tokens")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        logger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Using safe fallback allocation")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task DistributedQuotaTracker_WhenTableStorageFails_ReturnsNullInsteadOfCrashing()
    {
        // Arrange
        var logger = new Mock<ILogger<DistributedQuotaTracker>>();
        var tableStorageFactory = new Mock<IRateLimitingTableStorageFactory>();
        var signalREventFactory = new Mock<ISignalREventFactory>();
        var progressEventPublisher = new Mock<IProgressEventPublisher>();
        var configuration = Options.Create(new DynamicRateLimitingConfiguration());

        // Make table storage factory throw exception
        tableStorageFactory.Setup(x => x.GetQuotaTableClientAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Azure Table Storage is unavailable!"));

        var quotaTracker = new DistributedQuotaTracker(
            tableStorageFactory.Object,
            signalREventFactory.Object,
            progressEventPublisher.Object,
            configuration,
            logger.Object);

        // Act - This should not throw
        var result = await quotaTracker.GetCurrentQuotaAsync("test-store", CancellationToken.None).ConfigureAwait(false);

        // Assert - Returns null instead of crashing
        result.Should().BeNull("When storage fails, quota tracker should return null gracefully");

        // Verify error was logged
        logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to get current quota")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task DistributedQuotaTracker_WhenGetQuotaHealthFails_ReturnsConservativeFallback()
    {
        // Arrange
        var logger = new Mock<ILogger<DistributedQuotaTracker>>();
        var tableStorageFactory = new Mock<IRateLimitingTableStorageFactory>();
        var signalREventFactory = new Mock<ISignalREventFactory>();
        var progressEventPublisher = new Mock<IProgressEventPublisher>();
        var configuration = Options.Create(new DynamicRateLimitingConfiguration());

        // Make table storage factory throw exception
        tableStorageFactory.Setup(x => x.GetQuotaTableClientAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network failure!"));

        var quotaTracker = new DistributedQuotaTracker(
            tableStorageFactory.Object,
            signalREventFactory.Object,
            progressEventPublisher.Object,
            configuration,
            logger.Object);

        // Act - This should not throw
        var result = await quotaTracker.GetQuotaHealthAsync("test-store", CancellationToken.None).ConfigureAwait(false);

        // Assert - Returns conservative fallback instead of crashing
        result.Should().NotBeNull("Should return conservative fallback health metrics");
        result.HealthStatus.Should().Be(QuotaHealthStatus.Critical, "Should indicate critical status for failed health check");
        result.SafeTokens.Should().BeGreaterOrEqualTo(0, "Should provide safe token allocation (may be 0 in critical scenarios)");
        result.StoreId.Should().Be("test-store", "Should preserve store ID in fallback response");

        // Verify error was logged
        logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to get current quota")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}