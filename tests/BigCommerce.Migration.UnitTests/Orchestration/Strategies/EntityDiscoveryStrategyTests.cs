  
using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.UnitTests.TestHelpers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Orchestration.Strategies;

/// <summary>
/// Unit tests for Entity Discovery Strategy Pattern interfaces
/// Tests define the expected behavior - implementations will follow (TDD)
/// Validates Single Responsibility Principle by separating discovery strategies
/// </summary>
public class EntityDiscoveryStrategyTests
{
    private readonly Mock<IBigCommerceApiClient> _apiClientMock;
    private readonly Mock<ILogger<IEntityDiscoveryStrategy>> _loggerMock;
    private readonly StoreConfiguration _testStoreConfig;
    private readonly EntityDiscoveryRequest _testRequest;

    public EntityDiscoveryStrategyTests()
    {
        _apiClientMock = new Mock<IBigCommerceApiClient>();
        _loggerMock = new Mock<ILogger<IEntityDiscoveryStrategy>>();
        _testStoreConfig = TestDataFactory.CreateSourceStoreConfiguration();
        _testRequest = CreateTestDiscoveryRequest();
    }

    #region Strategy Interface Tests

    [Fact]
    public void IEntityDiscoveryStrategy_ShouldHave_RequiredProperties()
    {
        // This test defines the interface contract
        // Implementation will be validated when strategies are created
        
        // Verify interface exists and has expected properties
        var strategyType = typeof(IEntityDiscoveryStrategy);
        strategyType.Should().NotBeNull();
        
        // Verify SupportedApiVersion property exists
        strategyType.GetProperty("SupportedApiVersion").Should().NotBeNull();
        
        // Verify DiscoverEntitiesAsync method exists
        var discoverMethod = strategyType.GetMethod("DiscoverEntitiesAsync");
        discoverMethod.Should().NotBeNull();
        discoverMethod!.ReturnType.Should().Be(typeof(Task<EntityDiscoveryResult>));
    }

    [Fact]
    public void IEntityDiscoveryStrategy_ShouldHave_CorrectMethodSignature()
    {
        // Verify DiscoverEntitiesAsync method signature
        var strategyType = typeof(IEntityDiscoveryStrategy);
        var discoverMethod = strategyType.GetMethod("DiscoverEntitiesAsync");
        
        discoverMethod.Should().NotBeNull();
        
        var parameters = discoverMethod!.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(EntityDiscoveryRequest));
        parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
    }

    #endregion

    #region Strategy Factory Interface Tests

    [Fact]
    public void IEntityDiscoveryStrategyFactory_ShouldHave_GetStrategyMethod()
    {
        // This test defines the factory interface contract
        
        var factoryType = typeof(IEntityDiscoveryStrategyFactory);
        factoryType.Should().NotBeNull();
        
        var getStrategyMethod = factoryType.GetMethod("GetStrategyAsync");
        getStrategyMethod.Should().NotBeNull();
        getStrategyMethod!.ReturnType.Should().Be(typeof(Task<IEntityDiscoveryStrategy>));
        
        var parameters = getStrategyMethod.GetParameters();
        parameters.Should().HaveCount(3);
        parameters[0].ParameterType.Should().Be(typeof(StoreConfiguration));
        parameters[1].ParameterType.Should().Be(typeof(string)); // entityType
        parameters[2].ParameterType.Should().Be(typeof(CancellationToken));
    }

    #endregion

    #region V2DirectPaginationStrategy Tests

    [Fact]
    public async Task V2DirectPaginationStrategy_ShouldReturn_SkipDiscoveryResult()
    {
        // Arrange
        var strategy = CreateV2DirectPaginationStrategy();
        var request = CreateTestDiscoveryRequest("products");

        // Act
        var result = await strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.EntityType.Should().Be("products");
        result.SkipDiscovery.Should().BeTrue();
        result.ApiVersion.Should().Be(BigCommerceApiVersion.V2);
        result.EntityIds.Should().BeEmpty();
        result.EntityData.Should().BeEmpty();
        result.PaginationMetadata.Should().ContainKey("Strategy");
        result.PaginationMetadata["Strategy"].Should().Be("DirectPagination");
    }

    [Fact]
    public async Task V2DirectPaginationStrategy_ShouldSupport_AllEntityTypes()
    {
        // Arrange
        var strategy = CreateV2DirectPaginationStrategy();
        var entityTypes = new[] { "products", "categories", "brands", "variants" };

        foreach (var entityType in entityTypes)
        {
            // Arrange
            var request = CreateTestDiscoveryRequest(entityType);

            // Act
            var result = await strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.EntityType.Should().Be(entityType);
            result.SkipDiscovery.Should().BeTrue();
            result.ApiVersion.Should().Be(BigCommerceApiVersion.V2);
        }
    }

    #endregion

    #region V3EfficientPaginationStrategy Tests

    [Fact]
    public async Task V3EfficientPaginationStrategy_ShouldReturn_MetadataOnly()
    {
        // Arrange
        var strategy = CreateV3EfficientPaginationStrategy();
        var request = CreateTestDiscoveryRequest("products");
        var mockProducts = TestDataFactory.CreateMockProducts(3);

        SetupApiClientForPaginatedResponse(mockProducts, totalCount: 1000, totalPages: 4);

        // Act
        var result = await strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.EntityType.Should().Be("products");
        result.ApiVersion.Should().Be(BigCommerceApiVersion.V3);
        result.SkipDiscovery.Should().BeFalse();
        result.EntityIds.Should().BeEmpty(); // No entity IDs for efficient pagination
        result.EntityData.Should().BeEmpty(); // No data caching for scalability
        result.TotalCount.Should().Be(1000);
        result.PaginationMetadata.Should().ContainKey("Strategy");
        result.PaginationMetadata["Strategy"].Should().Be("EfficientPagination");
        result.PaginationMetadata.Should().ContainKey("MemoryOptimized");
        result.PaginationMetadata["MemoryOptimized"].Should().Be(true);
    }

    [Fact]
    public async Task V3EfficientPaginationStrategy_ShouldSupport_NonHierarchicalEntities()
    {
        // Arrange
        var strategy = CreateV3EfficientPaginationStrategy();
        var entityTypes = new[] { "products", "brands", "variants", "images", "modifiers" };

        foreach (var entityType in entityTypes)
        {
            // Arrange
            var request = CreateTestDiscoveryRequest(entityType);
            SetupApiClientForPaginatedResponse(TestDataFactory.CreateMockProducts(2), totalCount: 500);

            // Act
            var result = await strategy.DiscoverEntitiesAsync(request, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.EntityType.Should().Be(entityType);
            result.ApiVersion.Should().Be(BigCommerceApiVersion.V3);
            result.EntityIds.Should().BeEmpty(); // Efficient pagination - no IDs cached
            result.EntityData.Should().BeEmpty(); // No data caching
        }
    }

    #endregion

    #region Strategy Factory Tests

    [Fact]
    public async Task EntityDiscoveryStrategyFactory_ShouldReturn_V2Strategy_ForV2Api()
    {
        // Arrange
        var factory = CreateStrategyFactory();
        _apiClientMock.Setup(x => x.DetectApiVersionAsync(_testStoreConfig, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(BigCommerceApiVersion.V2);

        // Act
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "products", CancellationToken.None);

        // Assert
        strategy.Should().NotBeNull();
        strategy.SupportedApiVersion.Should().Be(BigCommerceApiVersion.V2);
    }

    [Fact]
    public async Task EntityDiscoveryStrategyFactory_ShouldReturn_V3EfficientStrategy_ForProducts()
    {
        // Arrange
        var factory = CreateStrategyFactory();
        _apiClientMock.Setup(x => x.DetectApiVersionAsync(_testStoreConfig, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(BigCommerceApiVersion.V3);

        // Act
        var strategy = await factory.GetStrategyAsync(_testStoreConfig, "products", CancellationToken.None);

        // Assert
        strategy.Should().NotBeNull();
        strategy.SupportedApiVersion.Should().Be(BigCommerceApiVersion.V3);
        // Additional validation that it's the efficient strategy would be done in implementation
    }

    [Fact]
    public async Task EntityDiscoveryStrategyFactory_ShouldThrow_ForUnsupportedApiVersion()
    {
        // Arrange
        var factory = CreateStrategyFactory();
        _apiClientMock.Setup(x => x.DetectApiVersionAsync(_testStoreConfig, It.IsAny<CancellationToken>()))
                     .ReturnsAsync((BigCommerceApiVersion)999); // Unsupported version

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(() => 
            factory.GetStrategyAsync(_testStoreConfig, "products", CancellationToken.None));
    }

    #endregion

    #region Helper Methods

    private EntityDiscoveryRequest CreateTestDiscoveryRequest(string entityType = "products")
    {
        return new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-123",
            EntityType = entityType,
            SourceStore = _testStoreConfig,
            EntityConfig = new EntityConfiguration
            {
                EntityType = entityType,
                IncludeDeleted = false,
                IncludeDrafts = false
            },
            CategoryTreeContext = new CategoryTreeContext
            {
                SourceCategoryTreeId = "source-tree-123",
                DestinationCategoryTreeId = "dest-tree-456"
            }
        };
    }

    private void SetupApiClientForPaginatedResponse(List<Dictionary<string, object>> mockData, int totalCount = 0, int totalPages = 1)
    {
        var response = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = mockData,
            TotalItems = totalCount > 0 ? totalCount : mockData.Count,
            TotalPages = totalPages,
            CurrentPage = 1,
            PerPage = 250,
            HasNextPage = false,
            IsLastPage = true,
            ApiVersion = BigCommerceApiVersion.V3,
            Meta = new BigCommerceV3Meta
            {
                Pagination = new BigCommerceV3Pagination
                {
                    Total = totalCount > 0 ? totalCount : mockData.Count,
                    Count = mockData.Count,
                    PerPage = 250,
                    CurrentPage = 1,
                    TotalPages = totalPages
                }
            }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
            It.IsAny<StoreConfiguration>(),
            It.IsAny<string>(),
            It.IsAny<BigCommercePaginationRequest>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);
    }

    // Strategy implementations using actual classes (GREEN phase)
    private IEntityDiscoveryStrategy CreateV2DirectPaginationStrategy()
    {
        var v2Logger = new Mock<ILogger<BigCommerce.Migration.Orchestration.Strategies.V2DirectPaginationStrategy>>();
        return new BigCommerce.Migration.Orchestration.Strategies.V2DirectPaginationStrategy(v2Logger.Object);
    }

    private IEntityDiscoveryStrategy CreateV3EfficientPaginationStrategy()
    {
        var v3EfficientLogger = new Mock<ILogger<BigCommerce.Migration.Orchestration.Strategies.V3EfficientPaginationStrategy>>();
        return new BigCommerce.Migration.Orchestration.Strategies.V3EfficientPaginationStrategy(_apiClientMock.Object, v3EfficientLogger.Object);
    }

    private IEntityDiscoveryStrategyFactory CreateStrategyFactory()
    {
        var factoryLogger = new Mock<ILogger<BigCommerce.Migration.Orchestration.Strategies.EntityDiscoveryStrategyFactory>>();
        var v2Logger = new Mock<ILogger<BigCommerce.Migration.Orchestration.Strategies.V2DirectPaginationStrategy>>();
        var v3EfficientLogger = new Mock<ILogger<BigCommerce.Migration.Orchestration.Strategies.V3EfficientPaginationStrategy>>();
        
        return new BigCommerce.Migration.Orchestration.Strategies.EntityDiscoveryStrategyFactory(
            _apiClientMock.Object,
            factoryLogger.Object,
            v2Logger.Object,
            v3EfficientLogger.Object);
    }

    #endregion
} 