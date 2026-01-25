using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using FluentAssertions;
using Azure;
using Azure.Data.Tables;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Infrastructure.Services;
using Xunit;

namespace BigCommerce.Migration.Tests.RateLimiting;

public class DistributedQuotaTrackerTests
{
    private readonly Mock<IRateLimitingTableStorageFactory> _tableStorageFactory;
    private readonly Mock<ISignalREventFactory> _signalREventFactory;
    private readonly Mock<IProgressEventPublisher> _progressEventPublisher;
    private readonly Mock<ILogger<DistributedQuotaTracker>> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;
    private readonly DistributedQuotaTracker _quotaTracker;
    private readonly Mock<TableClient> _tableClient;

    public DistributedQuotaTrackerTests()
    {
        _tableStorageFactory = new Mock<IRateLimitingTableStorageFactory>();
        _signalREventFactory = new Mock<ISignalREventFactory>();
        _progressEventPublisher = new Mock<IProgressEventPublisher>();
        _logger = new Mock<ILogger<DistributedQuotaTracker>>();
        _tableClient = new Mock<TableClient>();
        _configuration = new DynamicRateLimitingConfiguration
        {
            Features = new FeatureFlags
            {
                EnablePredictiveDistribution = true,
                EnableQuotaTracking = true
            },
            Predictive = new PredictiveSettings
            {
                SafetyBufferPercentage = 0.15,
                HealthyQuotaThreshold = 0.3,
                CriticalQuotaThreshold = 0.1
            }
        };

        _tableStorageFactory.Setup(x => x.GetQuotaTableClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_tableClient.Object);

        _quotaTracker = new DistributedQuotaTracker(
            _tableStorageFactory.Object,
            _signalREventFactory.Object,
            _progressEventPublisher.Object,
            Options.Create(_configuration),
            _logger.Object
        );
    }

    [Fact]
    public async Task UpdateQuotaFromBigCommerceInfo_ValidHeaders_UpdatesQuota()
    {
        // Arrange
        var storeId = "test-store";
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            StoreId = storeId,
            RequestsQuota = 1000,
            RequestsLeft = 800,
            TimeResetMs = 30000,
            TimeWindowMs = 60000
        };

        // Setup mock response
        var mockResponse = new Mock<Response>();
        var mockEntity = new StoreQuotaEntity
        {
            PartitionKey = storeId,
            RowKey = "quota",
            CurrentQuota = 1000,
            RemainingTokens = 800,
            LastUpdated = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        _tableClient.Setup(x => x.GetEntityIfExistsAsync<StoreQuotaEntity>(
                storeId,
                "quota",
                null,
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Response.FromValue(mockEntity, mockResponse.Object));

        _tableClient.Setup(x => x.UpdateEntityAsync(
                It.IsAny<StoreQuotaEntity>(),
                It.IsAny<ETag>(),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(storeId, rateLimitInfo).ConfigureAwait(false);

        // Assert
        result.Should().BeTrue();
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task UpdateQuotaFromBigCommerceInfo_NullInfo_ThrowsArgumentNullException()
    {
        // Arrange
        var storeId = "test-store";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(storeId, null!)
        ).ConfigureAwait(false);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task UpdateQuotaFromBigCommerceInfo_InvalidStoreId_ThrowsArgumentException(string storeId)
    {
        // Arrange
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            StoreId = "test-store",
            RequestsQuota = 1000,
            RequestsLeft = 800,
            TimeResetMs = 30000,
            TimeWindowMs = 60000
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(storeId, rateLimitInfo)
        ).ConfigureAwait(false);
    }

    [Fact]
    public async Task UpdateQuotaFromBigCommerceInfo_QuotaHealthChanges_RaisesSignalREvent()
    {
        // Arrange
        var storeId = "test-store";
        var rateLimitInfo = new BigCommerceRateLimitInfo
        {
            StoreId = storeId,
            RequestsQuota = 1000,
            RequestsLeft = 50, // Critical level
            TimeResetMs = 30000,
            TimeWindowMs = 60000
        };

        // Setup mock response
        var mockResponse = new Mock<Response>();
        var mockEntity = new StoreQuotaEntity
        {
            PartitionKey = storeId,
            RowKey = "quota",
            CurrentQuota = 1000,
            RemainingTokens = 50,
            LastUpdated = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        _tableClient.Setup(x => x.GetEntityIfExistsAsync<StoreQuotaEntity>(
                storeId,
                "quota",
                null,
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Response.FromValue(mockEntity, mockResponse.Object));

        _tableClient.Setup(x => x.UpdateEntityAsync(
                It.IsAny<StoreQuotaEntity>(),
                It.IsAny<ETag>(),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(storeId, rateLimitInfo).ConfigureAwait(false);

        // Assert
        result.Should().BeTrue();
        _signalREventFactory.Verify(
            x => x.CreateQuotaUpdate(
                storeId,
                It.Is<QuotaUpdateOptions>(o => 
                    o.TotalQuota == rateLimitInfo.RequestsQuota &&
                    o.RemainingTokens == rateLimitInfo.RequestsLeft &&
                    o.UtilizationPercent == 0.95 && // (1000-50)/1000
                    o.HealthStatus == "Critical"
                )
            ),
            Times.Once
        );
    }
}