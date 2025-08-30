using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Activities.Models;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Services.EntityCreation;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Strategies.Creation;

/// <summary>
/// Unit tests for ProductChannelAssignCreationStrategy
/// </summary>
public class ProductChannelAssignCreationStrategyTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<ILogger<ProductChannelAssignCreationStrategy>> _mockLogger;
    private readonly ProductChannelAssignCreationStrategy _strategy;

    public ProductChannelAssignCreationStrategyTests()
    {
        _mockApiRequestHandler = new Mock<IApiRequestHandler>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _mockLogger = new Mock<ILogger<ProductChannelAssignCreationStrategy>>();

        _strategy = new ProductChannelAssignCreationStrategy(
            _mockApiRequestHandler.Object,
            _mockErrorHandlingService.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        var strategy = new ProductChannelAssignCreationStrategy(
            _mockApiRequestHandler.Object,
            _mockErrorHandlingService.Object,
            _mockLogger.Object
        );

        // Assert
        Assert.NotNull(strategy);
        Assert.Equal("product-channel-assign", strategy.EntityType);
    }

    [Fact]
    public void Constructor_WithNullApiRequestHandler_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ProductChannelAssignCreationStrategy(
                null!,
                _mockErrorHandlingService.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullErrorHandlingService_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ProductChannelAssignCreationStrategy(
                _mockApiRequestHandler.Object,
                null!,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ProductChannelAssignCreationStrategy(
                _mockApiRequestHandler.Object,
                _mockErrorHandlingService.Object,
                null!));
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithValidChannelAssignments_ShouldCreateSuccessfully()
    {
        // Arrange
        var destinationStore = new StoreConfiguration
        {
            StoreId = "dest-store",
            AccessToken = "token",
            ChannelId = "1"
        };

        var channelAssignments = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object>
            {
                ["id"] = "123",
                ["status"] = "success",
                ["channel_assignments"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { ["product_id"] = 123, ["channel_id"] = 1 }
                }
            },
            new Dictionary<string, object>
            {
                ["id"] = "124", 
                ["status"] = "success",
                ["channel_assignments"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { ["product_id"] = 124, ["channel_id"] = 2 }
                }
            }
        };

        var apiResponse = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["product_id"] = 123, ["channel_id"] = 1 },
            new Dictionary<string, object> { ["product_id"] = 124, ["channel_id"] = 2 }
        };

        _mockApiRequestHandler
            .Setup(h => h.ExecuteRequestAsync<List<Dictionary<string, object>>>(
                It.IsAny<ApiRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiResponse);

        // Act
        var result = await _strategy.CreateEntitiesAsync(
            channelAssignments, 
            "migration-id", 
            destinationStore, 
            null, 
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);

        _mockApiRequestHandler.Verify(
            h => h.ExecuteRequestAsync<List<Dictionary<string, object>>>(
                It.Is<ApiRequest>(req => 
                    req.Method == HttpMethod.Put &&
                    req.Url.Contains("/v3/catalog/products/channel-assignments")),
                It.IsAny<CancellationToken>()),
            Times.Once); // ✅ OPTIMIZED: Now 1 API call for all products (multi-product batching)
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithEmptyList_ShouldReturnEmptyList()
    {
        // Arrange
        var destinationStore = new StoreConfiguration
        {
            StoreId = "dest-store",
            AccessToken = "token",
            ChannelId = "1"
        };

        var emptyList = new List<Dictionary<string, object>>();

        // Act
        var result = await _strategy.CreateEntitiesAsync(
            emptyList, 
            "migration-id", 
            destinationStore, 
            null, 
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);

        // Verify no API call was made
        _mockApiRequestHandler.Verify(
            h => h.ExecuteRequestAsync<List<Dictionary<string, object>>>(
                It.IsAny<ApiRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithApiException_ShouldReturnErrorStatus()
    {
        // Arrange
        var destinationStore = new StoreConfiguration
        {
            StoreId = "dest-store",
            AccessToken = "token",
            ChannelId = "1"
        };

        var channelAssignments = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object>
            {
                ["id"] = "123",
                ["status"] = "success",
                ["channel_assignments"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { ["product_id"] = 123, ["channel_id"] = 1 }
                }
            }
        };

        _mockApiRequestHandler
            .Setup(h => h.ExecuteRequestAsync<List<Dictionary<string, object>>>(
                It.IsAny<ApiRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API error"));

        // Act
        var result = await _strategy.CreateEntitiesAsync(
            channelAssignments, 
            "migration-id", 
            destinationStore, 
            null, 
            CancellationToken.None);

        // Assert - API exception should return result with error status
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("failed_api_error", result[0]["status"]);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithNullEntities_ShouldReturnEmptyList()
    {
        // Arrange
        var destinationStore = new StoreConfiguration
        {
            StoreId = "dest-store",
            AccessToken = "token",
            ChannelId = "1"
        };

        // Act
        var result = await _strategy.CreateEntitiesAsync(
            null!, 
            "migration-id", 
            destinationStore, 
            null, 
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithNullMigrationId_ShouldProcessNormally()
    {
        // Arrange
        var destinationStore = new StoreConfiguration
        {
            StoreId = "dest-store",
            AccessToken = "token",
            ChannelId = "1"
        };

        var channelAssignments = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["product_id"] = 123, ["channel_id"] = 1 }
        };

        // Act
        var result = await _strategy.CreateEntitiesAsync(
            channelAssignments, 
            null!, 
            destinationStore, 
            null, 
            CancellationToken.None);

        // Assert - The method doesn't validate null migrationId, so it processes normally
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithNullDestinationStore_ShouldReturnNull()
    {
        // Arrange
        var channelAssignments = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["product_id"] = 123, ["channel_id"] = 1 }
        };

        // Act
        var result = await _strategy.CreateEntitiesAsync(
            channelAssignments, 
            "migration-id", 
            null!, 
            null, 
            CancellationToken.None);

        // Assert - Invalid destination store returns null
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var destinationStore = new StoreConfiguration
        {
            StoreId = "dest-store",
            AccessToken = "token",
            ChannelId = "1"
        };

        var channelAssignments = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["product_id"] = 123, ["channel_id"] = 1 }
        };

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        _mockApiRequestHandler
            .Setup(h => h.ExecuteRequestAsync<List<Dictionary<string, object>>>(
                It.IsAny<ApiRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _strategy.CreateEntitiesAsync(
                channelAssignments, 
                "migration-id", 
                destinationStore, 
                null, 
                cancellationTokenSource.Token));
    }
}