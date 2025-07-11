using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Activities;

/// <summary>
/// Tests for the DiscoverEntities activity function
/// These tests define the expected behavior - implementation will follow (TDD)
/// </summary>
public class DiscoverEntitiesActivityTests
{
    private readonly Mock<IBigCommerceApiClient> _apiClientMock;
    private readonly Mock<ILogger<DiscoverEntitiesActivity>> _loggerMock;
    private readonly DiscoverEntitiesActivity _activity;

    public DiscoverEntitiesActivityTests()
    {
        _apiClientMock = new Mock<IBigCommerceApiClient>();
        _loggerMock = new Mock<ILogger<DiscoverEntitiesActivity>>();
        _activity = new DiscoverEntitiesActivity(_apiClientMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_ValidRequest_ReturnsSuccessResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-123",
            EntityType = "products",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "test-token"
            },
            EntityConfig = new EntityConfiguration
            {
                EntityType = "products",
                IncludeDeleted = false,
                IncludeDrafts = false
            }
        };

        var mockPaginationResponse = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = new List<Dictionary<string, object>>
            {
                new() { { "id", 1 }, { "name", "Product 1" } },
                new() { { "id", 2 }, { "name", "Product 2" } }
            },
            ApiVersion = BigCommerceApiVersion.V3,
            CurrentPage = 1,
            PerPage = 50,
            TotalItems = 2,
            TotalPages = 1,
            HasNextPage = false,
            IsLastPage = true
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<string>(),
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPaginationResponse);

        // Act
        var result = await _activity.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("products", result.EntityType);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.EntityIds.Count);
        Assert.Contains("1", result.EntityIds);
        Assert.Contains("2", result.EntityIds);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.PaginationMetadata);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_InvalidRequest_ReturnsErrorResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = "",
            EntityType = "products",
            SourceStore = new StoreConfiguration(),
            EntityConfig = new EntityConfiguration()
        };

        // Act
        var result = await _activity.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("products", result.EntityType);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.EntityIds);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_BrandsEntity_ReturnsSuccessResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-123",
            EntityType = "brands",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "test-token"
            },
            EntityConfig = new EntityConfiguration
            {
                EntityType = "brands"
            }
        };

        var mockPaginationResponse = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = new List<Dictionary<string, object>>
            {
                new() { { "id", 1 }, { "name", "Brand 1" } },
                new() { { "id", 2 }, { "name", "Brand 2" } }
            },
            ApiVersion = BigCommerceApiVersion.V3,
            CurrentPage = 1,
            PerPage = 100,
            TotalItems = 2,
            TotalPages = 1,
            HasNextPage = false,
            IsLastPage = true
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<string>(),
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPaginationResponse);

        // Act
        var result = await _activity.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("brands", result.EntityType);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.EntityIds.Count);
        Assert.Contains("1", result.EntityIds);
        Assert.Contains("2", result.EntityIds);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.PaginationMetadata);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_ApiException_ReturnsErrorResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-123",
            EntityType = "products",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "test-token"
            },
            EntityConfig = new EntityConfiguration
            {
                EntityType = "products"
            }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<string>(),
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BigCommerceApiException("API Error"));

        // Act
        var result = await _activity.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("products", result.EntityType);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.EntityIds);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("API Error", result.Errors[0]);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_UnsupportedEntityType_ReturnsErrorResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-123",
            EntityType = "unsupported",
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "test-token"
            },
            EntityConfig = new EntityConfiguration
            {
                EntityType = "unsupported"
            }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<string>(),
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Unsupported entity type: unsupported"));

        // Act
        var result = await _activity.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("unsupported", result.EntityType);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.EntityIds);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Unsupported entity type", result.Errors[0]);
    }
}

// All models are now in Core.Interfaces to avoid duplication 