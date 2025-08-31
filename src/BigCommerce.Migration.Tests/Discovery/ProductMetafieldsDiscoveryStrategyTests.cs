using BigCommerce.Migration.Activities.Strategies.Discovery;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Tests.Discovery;

/// <summary>
/// xUnit unit tests for ProductMetafieldsDiscoveryStrategy
/// Tests IApiRequestHandler integration for consistent rate limiting
/// Follows TDD approach with comprehensive coverage of critical scenarios
/// </summary>
public class ProductMetafieldsDiscoveryStrategyTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<ILogger<ProductMetafieldsDiscoveryStrategy>> _mockLogger;
    private readonly ProductMetafieldsDiscoveryStrategy _strategy;

    public ProductMetafieldsDiscoveryStrategyTests()
    {
        _mockApiRequestHandler = new Mock<IApiRequestHandler>();
        _mockLogger = new Mock<ILogger<ProductMetafieldsDiscoveryStrategy>>();
        _strategy = new ProductMetafieldsDiscoveryStrategy(_mockApiRequestHandler.Object, _mockLogger.Object);
    }

    /// <summary>
    /// Test: Valid configuration with metafields should return successful discovery result
    /// </summary>
    [Fact]
    public async Task DiscoverEntitiesAsync_ValidConfiguration_ReturnsMetafields()
    {
        // Arrange
        var request = CreateValidDiscoveryRequest();
        _mockApiRequestHandler
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(r => r.Url.Contains("/catalog/products/metafields") && r.Method == HttpMethod.Get), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMockApiResponse(156));

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(156, result.TotalCount);
        Assert.False(result.SkipDiscovery);
        Assert.Equal("product-metafields", result.EntityType);
        Assert.Equal(BigCommerceApiVersion.V3, result.ApiVersion);
        Assert.Empty(result.EntityIds); // Memory optimized - no IDs cached
        Assert.Empty(result.EntityData); // Memory optimized - no data cached
        Assert.NotNull(result.PaginationMetadata);
        Assert.True(result.PaginationMetadata.ContainsKey("Strategy"));
        Assert.Equal("ProductMetafieldsDiscovery", result.PaginationMetadata["Strategy"]);
    }

    /// <summary>
    /// Test: Store with no metafields should return empty result and skip discovery
    /// </summary>
    [Fact]
    public async Task DiscoverEntitiesAsync_EmptyStore_ReturnsEmptyList()
    {
        // Arrange
        var request = CreateValidDiscoveryRequest();
        _mockApiRequestHandler
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(r => r.Url.Contains("/catalog/products/metafields") && r.Method == HttpMethod.Get), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMockApiResponse(0));

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.TotalCount);
        Assert.True(result.SkipDiscovery);
        Assert.Equal("product-metafields", result.EntityType);
        Assert.Equal(BigCommerceApiVersion.V3, result.ApiVersion);
        Assert.Empty(result.EntityIds);
        Assert.NotNull(result.PaginationMetadata);
        Assert.True(result.PaginationMetadata.ContainsKey("SkipReason"));
        Assert.Equal("NoMetafieldsFound", result.PaginationMetadata["SkipReason"]);
    }

    /// <summary>
    /// Test: API error should be handled gracefully with continue-on-error policy
    /// </summary>
    [Fact]
    public async Task DiscoverEntitiesAsync_ApiError_HandlesGracefully()
    {
        // Arrange
        var request = CreateValidDiscoveryRequest();
        var apiException = new Exception("BigCommerce API error: Rate limit exceeded");
        
        _mockApiRequestHandler
            .Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.Is<ApiRequest>(r => r.Url.Contains("/catalog/products/metafields") && r.Method == HttpMethod.Get), 
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(apiException);

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.TotalCount);
        Assert.True(result.SkipDiscovery);
        Assert.Equal("product-metafields", result.EntityType);
        Assert.Equal(BigCommerceApiVersion.V3, result.ApiVersion);
        Assert.Single(result.Errors);
        Assert.Contains("Discovery failed", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: Cancellation should be handled gracefully with proper cleanup
    /// </summary>
    [Fact]
    public async Task DiscoverEntitiesAsync_CancellationRequested_StopsGracefully()
    {
        // Arrange
        var request = CreateValidDiscoveryRequest();
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel(); // Pre-cancelled token

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request, cancellationTokenSource.Token);

        // Assert
        Assert.Equal(0, result.TotalCount);
        Assert.True(result.SkipDiscovery);
        Assert.Equal("product-metafields", result.EntityType);
        Assert.Equal(BigCommerceApiVersion.V3, result.ApiVersion);
        Assert.Single(result.Errors);
        Assert.Contains("cancelled", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: Strategy should support only product-metafields entity type
    /// </summary>
    [Fact]
    public void GetSupportedEntityTypes_ReturnsOnlyProductMetafields()
    {
        // Act
        var supportedTypes = _strategy.GetSupportedEntityTypes();

        // Assert
        Assert.Single(supportedTypes);
        Assert.Contains("product-metafields", supportedTypes);
    }

    /// <summary>
    /// Test: Strategy should report V3 API version
    /// </summary>
    [Fact]
    public void SupportedApiVersion_ReturnsV3()
    {
        // Act & Assert
        Assert.Equal(BigCommerceApiVersion.V3, _strategy.SupportedApiVersion);
    }

    #region Helper Methods

    /// <summary>
    /// Creates a valid discovery request for testing
    /// </summary>
    private EntityDiscoveryRequest CreateValidDiscoveryRequest()
    {
        return new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-001",
            EntityType = "product-metafields",
            SourceStore = CreateValidStoreConfiguration(),
            EntityConfig = new EntityConfiguration
            {
                EntityType = "product-metafields",
                PageSize = 250,
                Settings = new Dictionary<string, object>()
            }
        };
    }

    /// <summary>
    /// Creates a valid store configuration for testing
    /// </summary>
    private StoreConfiguration CreateValidStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "test-store-123",
            AccessToken = "test-api-token",
            BaseUrl = "https://api.bigcommerce.com",
            ChannelId = "1"
        };
    }

    /// <summary>
    /// Creates mock BigCommerce API response for IApiRequestHandler (raw Dictionary format)
    /// This matches the actual response format from BigCommerce API that IApiRequestHandler returns
    /// </summary>
    private Dictionary<string, object> CreateMockApiResponse(int count)
    {
        var data = new List<Dictionary<string, object>>();
        
        // Only add sample data if count > 0 (simulate real API behavior)
        if (count > 0)
        {
            data.Add(new Dictionary<string, object>
            {
                ["id"] = 123,
                ["permission_set"] = "app_only",
                ["namespace"] = "Sales Department",
                ["key"] = "Staff Name",
                ["value"] = "Ronaldo",
                ["description"] = "order",
                ["resource_type"] = "product",
                ["resource_id"] = 42,
                ["date_created"] = "2022-06-16T18:39:00+00:00",
                ["date_modified"] = "2022-06-16T18:39:00+00:00"
            });
        }

        var totalPages = count > 0 ? (int)Math.Ceiling((double)count / 250) : 1;

        // Return raw BigCommerce API response format (what IApiRequestHandler returns)
        return new Dictionary<string, object>
        {
            ["data"] = data,
            ["meta"] = new Dictionary<string, object>
            {
                ["pagination"] = new Dictionary<string, object>
                {
                    ["total"] = count,
                    ["count"] = Math.Min(count, 1),
                    ["per_page"] = 1,
                    ["current_page"] = 1,
                    ["total_pages"] = totalPages,
                    ["links"] = new Dictionary<string, object>
                    {
                        ["previous"] = (object?)null,
                        ["current"] = "?page=1&limit=1",
                        ["next"] = count > 1 ? "?page=2&limit=1" : (object?)null
                    }
                }
            }
        };
    }

    #endregion
}
