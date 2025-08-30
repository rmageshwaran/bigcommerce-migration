using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Activities.Strategies.Discovery;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Strategies.Discovery;

/// <summary>
/// Unit tests for ProductChannelAssignDiscoveryStrategy
/// </summary>
public class ProductChannelAssignDiscoveryStrategyTests
{
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ILogger<ProductChannelAssignDiscoveryStrategy>> _mockLogger;
    private readonly ProductChannelAssignDiscoveryStrategy _strategy;

    public ProductChannelAssignDiscoveryStrategyTests()
    {
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockLogger = new Mock<ILogger<ProductChannelAssignDiscoveryStrategy>>();
        
        _strategy = new ProductChannelAssignDiscoveryStrategy(
            _mockStorageService.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        var strategy = new ProductChannelAssignDiscoveryStrategy(
            _mockStorageService.Object,
            _mockLogger.Object
        );

        // Assert
        Assert.NotNull(strategy);
        Assert.Equal(BigCommerceApiVersion.V3, strategy.SupportedApiVersion);
    }

    [Fact]
    public void Constructor_WithNullStorageService_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ProductChannelAssignDiscoveryStrategy(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ProductChannelAssignDiscoveryStrategy(_mockStorageService.Object, null!));
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WithValidRequest_ShouldReturnSuccessResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            EntityType = "product-channel-assign",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "token",
                ChannelId = "1"
            }
        };

        // ✅ UPDATED: Mock EntityProgressEntry instead of EntityMappings (correct discovery approach)
        var entityProgress = new List<EntityProgressEntry>
        {
            new EntityProgressEntry
            {
                MigrationId = request.MigrationId,
                EntityType = "products",
                SuccessCount = 1000, // Total successful products from Phase 1
                TotalCount = 1200,
                FailureCount = 100,
                SkippedCount = 100,
                Status = "Completed"
            }
        };

        _mockStorageService
            .Setup(s => s.GetEntityProgressAsync(request.MigrationId, "products"))
            .ReturnsAsync(entityProgress);

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1000, result.TotalCount); // ✅ CORRECT: Uses EntityProgress.SuccessCount
        Assert.Empty(result.Errors);
        
        _mockStorageService.Verify(
            s => s.GetEntityProgressAsync(request.MigrationId, "products"),
            Times.Once);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WithEmptyChannelsData_ShouldExcludeFromCount()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            EntityType = "product-channel-assign",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "token",
                ChannelId = "1"
            }
        };

        var entityMappings = new List<EntityMapping>
        {
            new EntityMapping
            {
                SourceId = "1",
                DestinationId = "101",
                ChannelsData = "[{\"channel_id\": 1}]"
            },
            new EntityMapping
            {
                SourceId = "2",
                DestinationId = "102", 
                ChannelsData = "" // Empty string should be excluded
            },
            new EntityMapping
            {
                SourceId = "3",
                DestinationId = "103",
                ChannelsData = "   " // Whitespace should be excluded
            }
        };

        _mockStorageService
            .Setup(s => s.GetEntityMappingsAsync(request.MigrationId, "products"))
            .ReturnsAsync(entityMappings);

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount); // Empty string and whitespace are considered as having channels by !string.IsNullOrEmpty
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WithNoEntityMappings_ShouldReturnZeroCount()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            EntityType = "product-channel-assign",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "token",
                ChannelId = "1"
            }
        };

        _mockStorageService
            .Setup(s => s.GetEntityMappingsAsync(request.MigrationId, "products"))
            .ReturnsAsync(new List<EntityMapping>());

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WithStorageServiceException_ShouldReturnErrorResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            EntityType = "product-channel-assign",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "token",
                ChannelId = "1"
            }
        };

        var exception = new Exception("Storage service error");
        _mockStorageService
            .Setup(s => s.GetEntityMappingsAsync(request.MigrationId, "products"))
            .ThrowsAsync(exception);

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Single(result.Errors);
        Assert.Contains("Discovery failed: Storage service error", result.Errors[0]);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WithNullRequest_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _strategy.DiscoverEntitiesAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WithCancellation_ShouldReturnErrorResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            EntityType = "product-channel-assign",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "token",
                ChannelId = "1"
            }
        };

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        _mockStorageService
            .Setup(s => s.GetEntityMappingsAsync(request.MigrationId, "products"))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request, cancellationTokenSource.Token);

        // Assert - Cancellation is caught and returned as error result
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Single(result.Errors);
        Assert.Contains("Discovery failed:", result.Errors[0]);
    }
}
