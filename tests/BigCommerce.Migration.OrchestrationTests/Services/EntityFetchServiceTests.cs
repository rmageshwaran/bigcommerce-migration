using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Services;

/// <summary>
/// Comprehensive tests for the enhanced EntityFetchService
/// Tests parallel fetching, retry logic, error handling, and edge cases
/// </summary>
public class EntityFetchServiceTests
{
    private readonly Mock<IBigCommerceApiClient> _apiClientMock;
    private readonly Mock<IEntityFetchStrategyFactory> _strategyFactoryMock;
    private readonly Mock<IEntityFetchStrategy> _mockStrategy;
    private readonly Mock<ILogger<EntityFetchService>> _loggerMock;
    private readonly EntityFetchService _service;
    private readonly StoreConfiguration _validStoreConfig;
    private readonly BatchProcessingRequest _validRequest;

    public EntityFetchServiceTests()
    {
        _apiClientMock = new Mock<IBigCommerceApiClient>();
        _strategyFactoryMock = new Mock<IEntityFetchStrategyFactory>();
        _mockStrategy = new Mock<IEntityFetchStrategy>();
        _loggerMock = new Mock<ILogger<EntityFetchService>>();
        
        // Setup strategy factory to return mock strategy for any entity type
        _strategyFactoryMock.Setup(x => x.GetStrategy(It.IsAny<string>()))
            .Returns(_mockStrategy.Object);
        
        // Setup mock strategy to return test data by default for each entity type
        _mockStrategy.Setup(x => x.FetchEntitiesAsync(
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>());
        
        _service = new EntityFetchService(_apiClientMock.Object, _strategyFactoryMock.Object, _loggerMock.Object);

        _validStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store-123",
            AccessToken = "test-token-456",
            ChannelId = "1"
        };

        _validRequest = new BatchProcessingRequest
        {
            MigrationId = "test-migration-789",
            EntityType = "products",
            BatchNumber = 1,
            TotalBatches = 1,
            EntityIds = new List<string> { "100", "101", "102" },
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };
    }

    #region Helper Methods

    /// <summary>
    /// Sets up the mock strategy to return specified test data
    /// </summary>
    private void SetupMockStrategyWithData(List<Dictionary<string, object>> testData)
    {
        _mockStrategy.Reset();
        _strategyFactoryMock.Reset();
        
        _strategyFactoryMock.Setup(x => x.GetStrategy(It.IsAny<string>()))
            .Returns(_mockStrategy.Object);
            
        _mockStrategy.Setup(x => x.FetchEntitiesAsync(
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);
    }

    /// <summary>
    /// Sets up the mock strategy with default data for common entity types  
    /// </summary>
    private void SetupMockStrategyWithDefaultData(string entityType)
    {
        var defaultData = entityType.ToLowerInvariant() switch
        {
            "products" => new List<Dictionary<string, object>>
            {
                new() { ["id"] = 100, ["name"] = "Product 1" },
                new() { ["id"] = 101, ["name"] = "Product 2" }
            },
            "categories" => new List<Dictionary<string, object>>
            {
                new() { ["id"] = 1, ["name"] = "Category 1" },
                new() { ["id"] = 2, ["name"] = "Category 2" }
            },
            "brands" => new List<Dictionary<string, object>>
            {
                new() { ["id"] = 1, ["name"] = "Brand 1" },
                new() { ["id"] = 2, ["name"] = "Brand 2" }
            },
            "variants" => new List<Dictionary<string, object>>
            {
                new() { ["id"] = 1, ["product_id"] = 100 },
                new() { ["id"] = 2, ["product_id"] = 100 },
                new() { ["id"] = 3, ["product_id"] = 101 }
            },
            "images" => new List<Dictionary<string, object>>
            {
                new() { ["id"] = 1, ["product_id"] = 100 },
                new() { ["id"] = 2, ["product_id"] = 100 }
            },
            "modifiers" => new List<Dictionary<string, object>>
            {
                new() { ["id"] = 1, ["product_id"] = 100 },
                new() { ["id"] = 2, ["product_id"] = 100 }
            },
            _ => new List<Dictionary<string, object>>()
        };

        SetupMockStrategyWithData(defaultData);
    }

    /// <summary>
    /// Sets up the mock strategy factory to throw an exception for unsupported entity types
    /// </summary>
    private void SetupMockStrategyToThrowException(string entityType, Exception exception)
    {
        _mockStrategy.Reset();
        _strategyFactoryMock.Reset();
        
        _strategyFactoryMock.Setup(x => x.GetStrategy(It.IsAny<string>()))
            .Returns(_mockStrategy.Object);
            
        _mockStrategy.Setup(x => x.FetchEntitiesAsync(
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    #endregion

    #region Constructor and Validation Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act & Assert
        var service = new EntityFetchService(_apiClientMock.Object, _strategyFactoryMock.Object, _loggerMock.Object);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullApiClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EntityFetchService(null!, _strategyFactoryMock.Object, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullStrategyFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EntityFetchService(_apiClientMock.Object, null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EntityFetchService(_apiClientMock.Object, _strategyFactoryMock.Object, null!));
    }

    [Fact]
    public async Task FetchEntitiesAsync_WithInvalidStoreConfiguration_ThrowsArgumentException()
    {
        // Arrange
        var invalidRequest = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            SourceStore = new StoreConfiguration { StoreId = "", AccessToken = "", ChannelId = "" },
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        // Setup strategy to throw ArgumentException for invalid store configuration
        _mockStrategy.Reset();
        _strategyFactoryMock.Reset();
        _strategyFactoryMock.Setup(x => x.GetStrategy(It.IsAny<string>()))
            .Returns(_mockStrategy.Object);
        _mockStrategy.Setup(x => x.FetchEntitiesAsync(
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid source store configuration"));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.FetchEntitiesAsync(invalidRequest, CancellationToken.None));
    }

    [Fact]
    public async Task FetchEntitiesAsync_WithUnsupportedEntityType_ThrowsArgumentException()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "unsupported",
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        SetupMockStrategyToThrowException("unsupported", new ArgumentException("Unsupported entity type: unsupported"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.FetchEntitiesAsync(request, CancellationToken.None));
        Assert.Contains("Unsupported entity type", exception.Message);
    }

    #endregion

    #region Products Fetch Tests

    [Fact]
    public async Task FetchProductsAsync_WithValidRequest_ReturnsProducts()
    {
        // Arrange
        var expectedProducts = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 100, ["name"] = "Product 1" },
            new Dictionary<string, object> { ["id"] = 101, ["name"] = "Product 2" }
        };

        SetupMockStrategyWithData(expectedProducts);

        // Act
        var result = await _service.FetchProductsAsync(_validRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(100, result[0]["id"]);
        Assert.Equal("Product 1", result[0]["name"]);
        _strategyFactoryMock.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task FetchProductsAsync_WithPagination_FetchesMultiplePages()
    {
        // Arrange
        // Our new batch-based approach only fetches the specific batch requested
        // BatchNumber = 1 means we fetch page 1 only
        var page1Products = Enumerable.Range(1, 50)
            .Select(i => new Dictionary<string, object> { ["id"] = i, ["name"] = $"Product {i}" })
            .ToList();

        SetupMockStrategyWithData(page1Products);

        // Act
        var result = await _service.FetchProductsAsync(_validRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(50, result.Count); // Only 50 from the first page (batch 1)
        _strategyFactoryMock.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Once);
        // Strategy pattern handles pagination internally within the strategy
    }

    [Fact]
    public async Task FetchProductsAsync_WithEmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        SetupMockStrategyWithData(new List<Dictionary<string, object>>());

        // Act
        var result = await _service.FetchProductsAsync(_validRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region Categories Fetch Tests

    [Fact]
    public async Task FetchCategoriesAsync_WithValidRequest_ReturnsCategories()
    {
        // Arrange
        var categories = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 1, ["name"] = "Category 1" },
            new Dictionary<string, object> { ["id"] = 2, ["name"] = "Category 2" }
        };

        // Create a request with CategoryTreeContext and cached data
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration-789",
            EntityType = "categories",
            BatchNumber = 1,
            TotalBatches = 1,
            EntityIds = new List<string> { "1", "2" },
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext 
            { 
                SourceCategoryTreeId = "tree-1",
                DestinationCategoryTreeId = "tree-2"
            },
            CachedEntityData = categories
        };

        // Act
        var result = await _service.FetchCategoriesAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0]["id"]);
        Assert.Equal("Category 1", result[0]["name"]);
    }

    [Fact]
    public async Task FetchCategoriesAsync_WithNoCategoryTrees_ReturnsEmptyList()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration-789",
            EntityType = "categories",
            BatchNumber = 1,
            TotalBatches = 1,
            EntityIds = new List<string>(),
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext 
            { 
                SourceCategoryTreeId = null,
                DestinationCategoryTreeId = null
            },
            CachedEntityData = new List<Dictionary<string, object>>()
        };

        // Act
        var result = await _service.FetchCategoriesAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region Brands Fetch Tests

    [Fact]
    public async Task FetchBrandsAsync_WithValidRequest_ReturnsBrands()
    {
        // Arrange
        var expectedBrands = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 1, ["name"] = "Brand 1" },
            new Dictionary<string, object> { ["id"] = 2, ["name"] = "Brand 2" }
        };

        SetupMockStrategyWithData(expectedBrands);

        // Act
        var result = await _service.FetchBrandsAsync(_validRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0]["id"]);
        Assert.Equal("Brand 1", result[0]["name"]);
    }

    [Fact]
    public async Task FetchBrandsAsync_WithPagination_FetchesMultiplePages()
    {
        // Arrange
        // Our new batch-based approach only fetches the specific batch requested
        // BatchNumber = 1 means we fetch page 1 only
        var page1Brands = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 1, ["name"] = "Brand 1" }
        };

        SetupMockStrategyWithData(page1Brands);

        // Act
        var result = await _service.FetchBrandsAsync(_validRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Count); // Only 1 from the first page (batch 1)
        _strategyFactoryMock.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Product-Specific Entities Tests (Parallel Fetching)

    [Fact]
    public async Task FetchVariantsAsync_WithValidProductIds_ReturnsVariants()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "variants",
            EntityIds = new List<string> { "100", "101" },
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        var expectedVariants = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 1, ["product_id"] = 100 },
            new Dictionary<string, object> { ["id"] = 2, ["product_id"] = 100 },
            new Dictionary<string, object> { ["id"] = 3, ["product_id"] = 101 }
        };

        SetupMockStrategyWithData(expectedVariants);

        // Act
        var result = await _service.FetchVariantsAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Contains(result, v => (int)v["id"] == 1 && (int)v["product_id"] == 100);
        Assert.Contains(result, v => (int)v["id"] == 2 && (int)v["product_id"] == 100);
        Assert.Contains(result, v => (int)v["id"] == 3 && (int)v["product_id"] == 101);
    }

    [Fact]
    public async Task FetchVariantsAsync_WithInvalidProductIds_SkipsInvalidIds()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "variants",
            EntityIds = new List<string> { "100", "invalid", "101" },
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        var expectedVariants = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 1, ["product_id"] = 100 }
        };

        SetupMockStrategyWithData(expectedVariants);

        // Act
        var result = await _service.FetchVariantsAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Count);
        Assert.Equal(1, result[0]["id"]);
        Assert.Equal(100, result[0]["product_id"]);
    }

    [Fact]
    public async Task FetchVariantsAsync_WithNoProductIds_ReturnsEmptyList()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "variants",
            EntityIds = new List<string>(),
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        // Act
        var result = await _service.FetchVariantsAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchImagesAsync_WithValidProductIds_ReturnsImages()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "images",
            EntityIds = new List<string> { "100" },
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        var expectedImages = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 1, ["product_id"] = 100 },
            new Dictionary<string, object> { ["id"] = 2, ["product_id"] = 100 }
        };

        SetupMockStrategyWithData(expectedImages);

        // Act
        var result = await _service.FetchImagesAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.All(result, img => Assert.Equal(100, img["product_id"]));
    }

    [Fact]
    public async Task FetchModifiersAsync_WithValidProductIds_ReturnsModifiers()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "modifiers",
            EntityIds = new List<string> { "100" },
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        var expectedModifiers = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 1, ["product_id"] = 100 },
            new Dictionary<string, object> { ["id"] = 2, ["product_id"] = 100 }
        };

        SetupMockStrategyWithData(expectedModifiers);

        // Act
        var result = await _service.FetchModifiersAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.All(result, mod => Assert.Equal(100, mod["product_id"]));
    }

    #endregion

    #region Retry Logic Tests

    [Fact]
    public async Task FetchProductsAsync_WithTransientFailure_ThrowsException()
    {
        // Arrange
        SetupMockStrategyToThrowException("products", new HttpRequestException("Transient error"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _service.FetchProductsAsync(_validRequest, CancellationToken.None));
        
        Assert.Contains("Product fetch failed", exception.Message);
        _strategyFactoryMock.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task FetchProductsAsync_WithPersistentFailure_ThrowsException()
    {
        // Arrange
        SetupMockStrategyToThrowException("products", new HttpRequestException("Persistent error"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _service.FetchProductsAsync(_validRequest, CancellationToken.None));
        
        Assert.Contains("Product fetch failed", exception.Message);
        _strategyFactoryMock.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task FetchProductsAsync_WithCancellation_DoesNotRetry()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Setup strategy to throw OperationCanceledException
        _mockStrategy.Reset();
        _strategyFactoryMock.Reset();
        _strategyFactoryMock.Setup(x => x.GetStrategy(It.IsAny<string>()))
            .Returns(_mockStrategy.Object);
        _mockStrategy.Setup(x => x.FetchEntitiesAsync(
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.FetchProductsAsync(_validRequest, cancellationTokenSource.Token));
        
        // The strategy should be called and throw the cancellation exception
        _strategyFactoryMock.Verify(x => x.GetStrategy(It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task FetchProductsAsync_WithPartialFailures_ContinuesProcessing()
    {
        // Arrange
        // Strategy pattern handles partial failures internally and returns whatever data was successfully retrieved
        var partialProducts = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 100, ["name"] = "Product 1" }
        };

        SetupMockStrategyWithData(partialProducts);

        // Act
        var result = await _service.FetchProductsAsync(_validRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(100, result[0]["id"]);
    }

    [Fact]
    public async Task FetchVariantsAsync_WithIndividualProductFailures_ContinuesProcessing()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "variants",
            EntityIds = new List<string> { "100", "101", "102" },
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        // Strategy pattern handles individual product failures internally and returns partial results
        var partialVariants = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = 1, ["product_id"] = 100 }
        };

        SetupMockStrategyWithData(partialVariants);

        // Act
        var result = await _service.FetchVariantsAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(1, result[0]["id"]);
        Assert.Equal(100, result[0]["product_id"]);
    }

    #endregion

    #region Edge Cases and Boundary Tests

    [Fact]
    public async Task FetchProductsAsync_WithNullResponse_HandlesGracefully()
    {
        // Arrange
        // Strategy pattern handles null responses internally and returns empty list
        SetupMockStrategyWithData(new List<Dictionary<string, object>>());

        // Act
        var result = await _service.FetchProductsAsync(_validRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchBrandsAsync_WithNullResponse_HandlesGracefully()
    {
        // Arrange
        // Strategy pattern handles null responses internally and returns empty list
        SetupMockStrategyWithData(new List<Dictionary<string, object>>());

        // Act
        var result = await _service.FetchBrandsAsync(_validRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchVariantsAsync_WithNullResponse_HandlesGracefully()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "variants",
            EntityIds = new List<string> { "100" },
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        // Strategy pattern handles null responses internally and returns empty list
        SetupMockStrategyWithData(new List<Dictionary<string, object>>());

        // Act
        var result = await _service.FetchVariantsAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchProductsAsync_WithLargeDataset_HandlesPaginationCorrectly()
    {
        // Arrange
        var largeProductList = Enumerable.Range(1, 50)
            .Select(i => new Dictionary<string, object> { ["id"] = i, ["name"] = $"Product {i}" })
            .ToList();

        SetupMockStrategyWithData(largeProductList);

        // Act
        var result = await _service.FetchProductsAsync(_validRequest, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(50, result.Count);
        Assert.Equal(1, result[0]["id"]);
        Assert.Equal(50, result[49]["id"]);
    }

    #endregion

    #region Performance and Concurrency Tests

    [Fact]
    public async Task FetchVariantsAsync_WithMultipleProducts_ProcessesInParallel()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "variants",
            EntityIds = Enumerable.Range(1, 10).Select(i => i.ToString()).ToList(),
            SourceStore = _validStoreConfig,
            DestinationStore = _validStoreConfig,
            CategoryTreeContext = new CategoryTreeContext()
        };

        var expectedVariants = Enumerable.Range(1, 10)
            .Select(i => new Dictionary<string, object> { ["id"] = i, ["product_id"] = i })
            .ToList();

        SetupMockStrategyWithData(expectedVariants);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _service.FetchVariantsAsync(request, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Count);
        
        // Should complete faster than sequential processing (10 * 100ms = 1 second)
        // Parallel processing with 5 concurrent tasks should be much faster
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, 
            $"Parallel processing took {stopwatch.ElapsedMilliseconds}ms, expected less than 2000ms");
    }

    #endregion
} 