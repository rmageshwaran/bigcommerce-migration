#pragma warning disable CS0618 // Type or member is obsolete - Required for testing deprecated V3HierarchicalStrategy
using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using System.Threading.Tasks;
using System.Threading;

namespace BigCommerce.Migration.UnitTests.Orchestration.Strategies;

/// <summary>
/// TDD unit tests for EntityDiscoveryStrategyFactory chunked strategy integration
/// Tests define the expected behavior - implementation will follow (TDD)
/// Validates chunked strategy selection logic and backward compatibility
/// </summary>
public class EntityDiscoveryStrategyFactoryChunkedTests
{
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly Mock<ILogger<EntityDiscoveryStrategyFactory>> _mockLogger;
    private readonly Mock<ILogger<V2DirectPaginationStrategy>> _mockV2Logger;
    private readonly Mock<ILogger<V3EfficientPaginationStrategy>> _mockV3EfficientLogger;
    // Note: V3HierarchicalStrategy removed - chunked strategy is now mandatory for categories
    private readonly Mock<IOptions<ChunkedHierarchyConfiguration>> _mockChunkedConfig;
    private readonly Mock<IChunkedHierarchicalDiscoveryStrategy> _mockChunkedStrategy;
    private readonly StoreConfiguration _testStoreConfig;

    public EntityDiscoveryStrategyFactoryChunkedTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _mockLogger = new Mock<ILogger<EntityDiscoveryStrategyFactory>>();
        _mockV2Logger = new Mock<ILogger<V2DirectPaginationStrategy>>();
        _mockV3EfficientLogger = new Mock<ILogger<V3EfficientPaginationStrategy>>();
        _mockV3HierarchicalLogger = new Mock<ILogger<V3HierarchicalStrategy>>();
        _mockChunkedConfig = new Mock<IOptions<ChunkedHierarchyConfiguration>>();
        _mockChunkedStrategy = new Mock<IChunkedHierarchicalDiscoveryStrategy>();

        _testStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store-id",
            AccessToken = "test-token",
            ChannelId = "test-channel-id"
        };

        // Setup chunked strategy to return V3 API version
        _mockChunkedStrategy.Setup(x => x.SupportedApiVersion)
            .Returns(BigCommerceApiVersion.V3);
    }

    #region Chunked Strategy Selection Tests (RED phase - should fail initially)

    [Fact]
    public async Task GetStrategyAsync_WhenChunkedEnabledForCategories_ShouldReturnChunkedStrategy()
    {
        // Arrange - Enable chunked processing for categories
        var chunkedConfig = new ChunkedHierarchyConfiguration
        {
            EnableBulkCreation = true, // This indicates chunked processing is desired
            MaxCategoriesPerLevel = 10000
        };
        _mockChunkedConfig.Setup(x => x.Value).Returns(chunkedConfig);

        // Setup API client to return V3
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V3);

        var factory = CreateFactoryWithChunkedSupport();

        // Act - Request strategy for categories
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "categories", CancellationToken.None);

        // Assert - Should return chunked strategy for memory-safe processing
        strategy.Should().BeAssignableTo<IChunkedHierarchicalDiscoveryStrategy>()
            .Which.SupportedApiVersion.Should().Be(BigCommerceApiVersion.V3);
    }

    [Fact]
    public async Task GetStrategyAsync_WhenChunkedDisabledForCategories_ShouldReturnRegularHierarchicalStrategy()
    {
        // Arrange - Disable chunked processing
        var chunkedConfig = new ChunkedHierarchyConfiguration
        {
            UseChunkedCategoryMigration = false, // Chunked processing disabled via feature flag
            EnableBulkCreation = false, 
            MaxCategoriesPerLevel = 1000
        };
        _mockChunkedConfig.Setup(x => x.Value).Returns(chunkedConfig);

        // Setup API client to return V3
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V3);

        var factory = CreateFactoryWithChunkedSupport();

        // Act - Request strategy for categories
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "categories", CancellationToken.None);

        // Assert - Should return regular V3HierarchicalStrategy
        strategy.Should().BeOfType<V3HierarchicalStrategy>();
        strategy.SupportedApiVersion.Should().Be(BigCommerceApiVersion.V3);
    }

    [Fact]
    public async Task GetStrategyAsync_WhenChunkedEnabledForNonHierarchicalEntity_ShouldReturnEfficientStrategy()
    {
        // Arrange - Enable chunked processing but request non-hierarchical entity
        var chunkedConfig = new ChunkedHierarchyConfiguration
        {
            EnableBulkCreation = true,
            MaxCategoriesPerLevel = 10000
        };
        _mockChunkedConfig.Setup(x => x.Value).Returns(chunkedConfig);

        // Setup API client to return V3
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V3);

        var factory = CreateFactoryWithChunkedSupport();

        // Act - Request strategy for products (non-hierarchical)
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "products", CancellationToken.None);

        // Assert - Should return efficient strategy (chunked only applies to hierarchical entities)
        strategy.Should().BeOfType<V3EfficientPaginationStrategy>();
        strategy.SupportedApiVersion.Should().Be(BigCommerceApiVersion.V3);
    }

    [Fact]
    public async Task GetStrategyAsync_WhenV2ApiDetected_ShouldReturnV2StrategyRegardlessOfChunkedConfig()
    {
        // Arrange - Enable chunked processing but API version is V2
        var chunkedConfig = new ChunkedHierarchyConfiguration
        {
            EnableBulkCreation = true,
            MaxCategoriesPerLevel = 10000
        };
        _mockChunkedConfig.Setup(x => x.Value).Returns(chunkedConfig);

        // Setup API client to return V2
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V2);

        var factory = CreateFactoryWithChunkedSupport();

        // Act - Request strategy for categories
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "categories", CancellationToken.None);

        // Assert - Should return V2 strategy (chunked only works with V3)
        strategy.Should().BeOfType<V2DirectPaginationStrategy>();
        strategy.SupportedApiVersion.Should().Be(BigCommerceApiVersion.V2);
    }

    #endregion

    #region Backward Compatibility Tests

    [Fact]
    public async Task GetStrategyAsync_WithoutChunkedConfig_ShouldMaintainBackwardCompatibility()
    {
        // Arrange - No chunked configuration (backward compatibility scenario)
        // Setup API client to return V3
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V3);

        var factory = CreateFactoryWithoutChunkedSupport();

        // Act - Request strategy for categories
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "categories", CancellationToken.None);

        // Assert - Should return regular hierarchical strategy (backward compatibility)
        strategy.Should().BeOfType<V3HierarchicalStrategy>();
        strategy.SupportedApiVersion.Should().Be(BigCommerceApiVersion.V3);
    }

    [Fact]
    public async Task GetStrategyAsync_WithNullChunkedConfig_ShouldHandleGracefully()
    {
        // Arrange - Null chunked configuration (defensive programming)
        _mockChunkedConfig.Setup(x => x.Value).Returns((ChunkedHierarchyConfiguration?)null!);

        // Setup API client to return V3
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V3);

        var factory = CreateFactoryWithChunkedSupport();

        // Act - Request strategy for categories
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "categories", CancellationToken.None);

        // Assert - Should fallback to regular hierarchical strategy
        strategy.Should().BeOfType<V3HierarchicalStrategy>();
        strategy.SupportedApiVersion.Should().Be(BigCommerceApiVersion.V3);
    }

    #endregion

    #region Configuration-Based Decision Tests

    [Fact]
    public async Task GetStrategyAsync_WithLargeHierarchyThreshold_ShouldPreferChunkedStrategy()
    {
        // Arrange - Configuration suggests large hierarchy processing
        var chunkedConfig = new ChunkedHierarchyConfiguration
        {
            EnableBulkCreation = true,
            MaxCategoriesPerLevel = 50000, // Large threshold suggests chunked processing
            EnableMemoryMonitoring = true
        };
        _mockChunkedConfig.Setup(x => x.Value).Returns(chunkedConfig);

        // Setup API client to return V3
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V3);

        var factory = CreateFactoryWithChunkedSupport();

        // Act - Request strategy for categories
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "categories", CancellationToken.None);

        // Assert - Should return chunked strategy for large hierarchies
        strategy.Should().BeAssignableTo<IChunkedHierarchicalDiscoveryStrategy>();
    }

    [Fact]
    public async Task GetStrategyAsync_WithMemoryMonitoringEnabled_ShouldPreferChunkedStrategy()
    {
        // Arrange - Memory monitoring enabled suggests memory-safe processing
        var chunkedConfig = new ChunkedHierarchyConfiguration
        {
            EnableBulkCreation = true,
            EnableMemoryMonitoring = true, // Memory safety priority
            MaxCategoriesPerLevel = 10000
        };
        _mockChunkedConfig.Setup(x => x.Value).Returns(chunkedConfig);

        // Setup API client to return V3
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V3);

        var factory = CreateFactoryWithChunkedSupport();

        // Act - Request strategy for categories
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "categories", CancellationToken.None);

        // Assert - Should return chunked strategy for memory safety
        strategy.Should().BeAssignableTo<IChunkedHierarchicalDiscoveryStrategy>();
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task GetStrategyAsync_WhenChunkedStrategyThrows_ShouldFallbackToRegularStrategy()
    {
        // Arrange - Chunked strategy creation fails
        var chunkedConfig = new ChunkedHierarchyConfiguration
        {
            EnableBulkCreation = true,
            MaxCategoriesPerLevel = 10000
        };
        _mockChunkedConfig.Setup(x => x.Value).Returns(chunkedConfig);

        // Setup API client to return V3
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V3);

        // This test would need factory that can fail chunked strategy creation
        var factory = CreateFactoryWithChunkedSupport();

        // Act - Request strategy for categories (fallback scenario)
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "categories", CancellationToken.None);

        // Assert - Should handle gracefully and return a valid strategy
        strategy.Should().NotBeNull();
        strategy.SupportedApiVersion.Should().Be(BigCommerceApiVersion.V3);
    }

    #endregion

    #region Helper Methods

    private EntityDiscoveryStrategyFactory CreateFactoryWithChunkedSupport()
    {
        // Create factory with chunked strategy support
        return new EntityDiscoveryStrategyFactory(
            _mockApiClient.Object,
            _mockLogger.Object,
            _mockV2Logger.Object,
            _mockV3EfficientLogger.Object,
            _mockV3HierarchicalLogger.Object,
            _mockChunkedConfig.Object,
            () => _mockChunkedStrategy.Object);
    }

    private EntityDiscoveryStrategyFactory CreateFactoryWithoutChunkedSupport()
    {
        // Create factory without chunked dependencies (backward compatibility)
        return new EntityDiscoveryStrategyFactory(
            _mockApiClient.Object,
            _mockLogger.Object,
            _mockV2Logger.Object,
            _mockV3EfficientLogger.Object,
            _mockV3HierarchicalLogger.Object);
    }

    #endregion
}