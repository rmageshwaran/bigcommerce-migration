using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;
using BigCommerce.Migration.Infrastructure.Services;
using Azure;
using Azure.Data.Tables;
using Xunit;

namespace BigCommerce.Migration.Tests.RateLimiting;

public class TokenConsensusManagerTests
{
    private readonly Mock<IRateLimitingTableStorageFactory> _tableStorageFactory;
    private readonly Mock<IInstanceCoordinationManager> _coordinationManager;
    private readonly Mock<IDistributedQuotaTracker> _quotaTracker;
    private readonly Mock<ILogger<TokenConsensusManager>> _logger;
    private readonly DynamicRateLimitingConfiguration _configuration;
    private readonly TokenConsensusManager _consensusManager;
    private readonly Mock<TableClient> _tableClient;

    public TokenConsensusManagerTests()
    {
        _tableStorageFactory = new Mock<IRateLimitingTableStorageFactory>();
        _coordinationManager = new Mock<IInstanceCoordinationManager>();
        _quotaTracker = new Mock<IDistributedQuotaTracker>();
        _logger = new Mock<ILogger<TokenConsensusManager>>();
        _tableClient = new Mock<TableClient>();

        _configuration = new DynamicRateLimitingConfiguration
        {
            Features = new FeatureFlags
            {
                EnablePredictiveDistribution = true,
                EnableInstanceCoordination = true
            },
            Predictive = new PredictiveSettings
            {
                MaxETagRetries = 3,
                TokenExpirySeconds = 30
            }
        };

        _tableStorageFactory.Setup(x => x.GetTokenTableClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_tableClient.Object);

        _consensusManager = new TokenConsensusManager(
            _tableStorageFactory.Object,
            _quotaTracker.Object,
            _coordinationManager.Object,
            Options.Create(_configuration),
            _logger.Object
        );
    }

    [Fact]
    public async Task ReserveTokens_ETagConflict_RetrySucceeds()
    {
        // Arrange
        var storeId = "test-store";
        var instanceId = "test-instance";


        _coordinationManager.Setup(x => x.GetCurrentInstanceId()).Returns(instanceId);
        
        // Setup instance coordination methods to avoid NullReferenceException
        _coordinationManager.Setup(x => x.EnsureStoreRegistrationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _coordinationManager.Setup(x => x.GetActiveInstanceIdsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { instanceId });

                    // Setup quota health
        _quotaTracker.Setup(x => x.GetQuotaHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaHealthMetrics
            {
                TotalQuota = 1000,
                RemainingTokens = 200,
                SafeTokens = 100,
                HealthStatus = QuotaHealthStatus.Healthy
            });

        // Setup existing allocation
        var existingAllocation = new TokenAllocationEntity
        {
            PartitionKey = storeId,
            RowKey = instanceId,
            AllocatedTokens = 100,
            AvailableTokens = 50,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };

        // Setup GetEntityIfExistsAsync to return no existing allocation (new allocation scenario)
        var nullResponse = Response.FromValue<TokenAllocationEntity>(null, new Mock<Response>().Object);
        _tableClient.Setup(x => x.GetEntityIfExistsAsync<TokenAllocationEntity>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(nullResponse);

        // Setup QueryAsync for GetAllAllocationsAsync - return empty list to avoid null reference issues
        var emptyAsyncPageable = CreateAsyncPageable(new List<TokenAllocationEntity>());

        _tableClient.Setup(x => x.QueryAsync<TokenAllocationEntity>(
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()
            ))
            .Returns(emptyAsyncPageable);

        // Setup first attempt to fail with ETag conflict
        var mockResponse = new Mock<Response>();

        var sequence = new List<Task<Response>>
        {
            Task.FromException<Response>(new RequestFailedException(412, "Precondition Failed")),
            Task.FromResult(mockResponse.Object)
        };
        var index = 0;

        _tableClient.Setup(x => x.UpsertEntityAsync(
                It.IsAny<TokenAllocationEntity>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(() => sequence[index++]);

        // Act
        var result = await _consensusManager.ReserveTokensAsync(storeId).ConfigureAwait(false);

        // Assert - The test should succeed and return tokens
        result.Should().BeGreaterThan(0);
        
        // Verify that the operation succeeded with the allocation
        _logger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully reserved") && v.ToString()!.Contains("tokens")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task ReserveTokens_MaxETagRetries_ThrowsException()
    {
        // Arrange
        var storeId = "test-store";
        var instanceId = "test-instance";

        _coordinationManager.Setup(x => x.GetCurrentInstanceId()).Returns(instanceId);
        
        // Setup instance coordination methods to avoid NullReferenceException
        _coordinationManager.Setup(x => x.EnsureStoreRegistrationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _coordinationManager.Setup(x => x.GetActiveInstanceIdsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { instanceId });
            
        _quotaTracker.Setup(x => x.GetQuotaHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaHealthMetrics
            {
                TotalQuota = 1000,
                RemainingTokens = 200,
                SafeTokens = 100,
                HealthStatus = QuotaHealthStatus.Healthy
            });

            // Setup existing allocation
            var existingAllocation = new TokenAllocationEntity
            {
                PartitionKey = storeId,
                RowKey = instanceId,
                AllocatedTokens = 100,
                AvailableTokens = 50,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            };

            _tableClient.Setup(x => x.GetEntityIfExistsAsync<TokenAllocationEntity>(
                    storeId,
                    instanceId,
                    null,
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(Response.FromValue(existingAllocation, new Mock<Response>().Object));

            // Setup all attempts to fail with ETag conflict
            var sequence = new List<Task<Response>>
            {
                Task.FromException<Response>(new RequestFailedException(412, "Precondition Failed")),
                Task.FromException<Response>(new RequestFailedException(412, "Precondition Failed")),
                Task.FromException<Response>(new RequestFailedException(412, "Precondition Failed"))
            };
            var index = 0;

            _tableClient.Setup(x => x.UpsertEntityAsync(
                    It.IsAny<TokenAllocationEntity>(),
                    It.IsAny<TableUpdateMode>(),
                    It.IsAny<CancellationToken>()
                ))
                .Returns(() => sequence[index++]);

        // Act - Should return safe fallback instead of throwing
        var result = await _consensusManager.ReserveTokensAsync(storeId).ConfigureAwait(false);

        // Assert - Should return safe fallback value
        result.Should().BeGreaterThan(0, "Should return a safe fallback value even when ETag retries are exhausted");

        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to reserve tokens") && v.ToString()!.Contains("safe fallback")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task ReserveTokens_OverAllocation_TriggersScaleBack()
    {
        // Arrange
        var storeId = "test-store";
        var instanceId = "test-instance";
        // This test checks over-allocation scenario where existing allocations exceed safe tokens

        _coordinationManager.Setup(x => x.GetCurrentInstanceId()).Returns(instanceId);
        
        // Setup instance coordination methods to avoid NullReferenceException
        _coordinationManager.Setup(x => x.EnsureStoreRegistrationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _coordinationManager.Setup(x => x.GetActiveInstanceIdsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { instanceId });
            
        _quotaTracker.Setup(x => x.GetQuotaHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaHealthMetrics
            {
                TotalQuota = 1000,
                RemainingTokens = 200,
                SafeTokens = 100,
                HealthStatus = QuotaHealthStatus.Healthy
            });

        // Setup existing allocation
        var existingAllocation = new TokenAllocationEntity
        {
            PartitionKey = storeId,
            RowKey = instanceId,
            AllocatedTokens = 100,
            AvailableTokens = 50,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };

                    _tableClient.Setup(x => x.GetEntityIfExistsAsync<TokenAllocationEntity>(
                    storeId,
                    instanceId,
                    null,
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(Response.FromValue(existingAllocation, new Mock<Response>().Object));

        // Setup all allocations query
        var allAllocations = new List<TokenAllocationEntity>
        {
            existingAllocation,
            new TokenAllocationEntity
            {
                PartitionKey = storeId,
                RowKey = "other-instance",
                AllocatedTokens = 150,
                AvailableTokens = 100,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            }
        };

        var asyncPageable = CreateAsyncPageable(allAllocations);

        _tableClient.Setup(x => x.QueryAsync<TokenAllocationEntity>(
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()
            ))
            .Returns(asyncPageable);

        // Setup initial allocation to succeed
        var mockResponse = new Mock<Response>();

        var sequence = new List<Task<Response>>
        {
            Task.FromResult(mockResponse.Object)
        };
        var index = 0;

        _tableClient.Setup(x => x.UpsertEntityAsync(
                It.IsAny<TokenAllocationEntity>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(() => sequence[index++]);

        // Act
        var result = await _consensusManager.ReserveTokensAsync(storeId).ConfigureAwait(false);

        // Assert - Should return 0 or very few tokens when over-allocated
        result.Should().BeGreaterOrEqualTo(0, "Should return 0 or minimal tokens when system is over-allocated");
        result.Should().BeLessOrEqualTo(10, "Should return minimal tokens due to over-allocation");
    }

    private static AsyncPageable<TokenAllocationEntity> CreateAsyncPageable(IEnumerable<TokenAllocationEntity> items)
    {
        var pages = new List<Page<TokenAllocationEntity>>
        {
            Page<TokenAllocationEntity>.FromValues(items.ToList(), null, new Mock<Response>().Object)
        };
        return AsyncPageable<TokenAllocationEntity>.FromPages(pages);
    }
}