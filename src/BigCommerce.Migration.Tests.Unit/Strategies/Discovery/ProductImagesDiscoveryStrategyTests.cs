using System;
using System.Collections.Generic;
using System.Linq;
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
/// Unit tests for ProductImagesDiscoveryStrategy
/// </summary>
public class ProductImagesDiscoveryStrategyTests
{
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ICancellationStore> _mockCancellationStore;
    private readonly Mock<ILogger<ProductImagesDiscoveryStrategy>> _mockLogger;
    private readonly ProductImagesDiscoveryStrategy _strategy;

    public ProductImagesDiscoveryStrategyTests()
    {
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockCancellationStore = new Mock<ICancellationStore>();
        _mockLogger = new Mock<ILogger<ProductImagesDiscoveryStrategy>>();
        
        _strategy = new ProductImagesDiscoveryStrategy(
            _mockStorageService.Object,
            _mockCancellationStore.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        var strategy = new ProductImagesDiscoveryStrategy(
            _mockStorageService.Object,
            _mockCancellationStore.Object,
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
        Assert.Throws<ArgumentNullException>(() => new ProductImagesDiscoveryStrategy(
            null!,
            _mockCancellationStore.Object,
            _mockLogger.Object
        ));
    }

    [Fact]
    public void Constructor_WithNullCancellationStore_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ProductImagesDiscoveryStrategy(
            _mockStorageService.Object,
            null!,
            _mockLogger.Object
        ));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ProductImagesDiscoveryStrategy(
            _mockStorageService.Object,
            _mockCancellationStore.Object,
            null!
        ));
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WithWrongEntityType_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "wrong-entity-type",
            MigrationId = "test-migration-123"
        };

        // Setup cancellation to NOT be cancelled so we get to the entity type validation
        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _strategy.DiscoverEntitiesAsync(request));
        
        Assert.Contains("ProductImagesDiscoveryStrategy only supports 'product-images' entity type", exception.Message);
        Assert.Contains("received 'wrong-entity-type'", exception.Message);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WhenCancelled_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync("test-migration-123"))
            .ReturnsAsync(true);
        
        _mockCancellationStore
            .Setup(x => x.GetCancellationReasonAsync("test-migration-123"))
            .ReturnsAsync("User cancelled migration");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _strategy.DiscoverEntitiesAsync(request));
        
        Assert.Contains("Migration cancelled: User cancelled migration", exception.Message);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WhenCancelledWithoutReason_ShouldUseDefaultReason()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync("test-migration-123"))
            .ReturnsAsync(true);
        
        _mockCancellationStore
            .Setup(x => x.GetCancellationReasonAsync("test-migration-123"))
            .ReturnsAsync((string?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _strategy.DiscoverEntitiesAsync(request));
        
        Assert.Contains("Migration cancelled: Migration cancelled", exception.Message);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WhenNoProductsProgressFound_ShouldReturnSkipDiscoveryResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _mockStorageService
            .Setup(x => x.GetEntityProgressAsync("test-migration-123", "products"))
            .ReturnsAsync(new List<EntityProgressEntry>());

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("product-images", result.EntityType);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.EntityIds);
        Assert.True(result.SkipDiscovery);
        Assert.Equal(BigCommerceApiVersion.V3, result.ApiVersion);
        Assert.Single(result.Errors);
        Assert.Contains("No products progress found", result.Errors.First());
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WhenZeroSuccessfulProducts_ShouldReturnSkipDiscoveryResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        var productsProgress = new EntityProgressEntry
        {
            EntityType = "products",
            TotalCount = 100,
            SuccessCount = 0,
            FailureCount = 50,
            SkippedCount = 50,
            Status = "completed"
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _mockStorageService
            .Setup(x => x.GetEntityProgressAsync("test-migration-123", "products"))
            .ReturnsAsync(new List<EntityProgressEntry> { productsProgress });

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("product-images", result.EntityType);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.EntityIds);
        Assert.True(result.SkipDiscovery);
        Assert.Equal(BigCommerceApiVersion.V3, result.ApiVersion);
        
        // Verify metadata includes the reason for skipping
        Assert.NotNull(result.PaginationMetadata);
        Assert.Equal("ProductImagesDiscovery", result.PaginationMetadata["Strategy"]);
        Assert.Equal("NoSuccessfulProducts", result.PaginationMetadata["SkipReason"]);
        Assert.Equal(100, result.PaginationMetadata["ProductsPhaseTotal"]);
        Assert.Equal(0, result.PaginationMetadata["ProductsPhaseSuccess"]);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WithSuccessfulProducts_ShouldReturnValidDiscoveryResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        var productsProgress = new EntityProgressEntry
        {
            EntityType = "products",
            TotalCount = 500,
            SuccessCount = 450,
            FailureCount = 30,
            SkippedCount = 20,
            Status = "completed"
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _mockStorageService
            .Setup(x => x.GetEntityProgressAsync("test-migration-123", "products"))
            .ReturnsAsync(new List<EntityProgressEntry> { productsProgress });

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("product-images", result.EntityType);
        Assert.Equal(450, result.TotalCount); // Should use SuccessCount
        Assert.Empty(result.EntityIds); // Should be empty for EntityMappings pagination
        Assert.Empty(result.EntityData); // Should be empty for memory efficiency
        Assert.False(result.SkipDiscovery);
        Assert.Equal(BigCommerceApiVersion.V3, result.ApiVersion);
        Assert.Empty(result.Errors);

        // Verify metadata includes proper configuration
        Assert.NotNull(result.PaginationMetadata);
        Assert.Equal("V3", result.PaginationMetadata["ApiVersion"]);
        Assert.Equal("ProductImagesDiscovery", result.PaginationMetadata["Strategy"]);
        Assert.Equal("EntityProgress", result.PaginationMetadata["DataSource"]);
        Assert.Equal(true, result.PaginationMetadata["UseEntityMappingsPagination"]);
        Assert.Equal(true, result.PaginationMetadata["MemoryOptimized"]);
        Assert.Equal(true, result.PaginationMetadata["CachingDisabled"]);
        
        // Verify timeout-safe configuration metadata
        Assert.Equal(1000, result.PaginationMetadata["MaxImagesPerProduct"]);
        Assert.Equal(50, result.PaginationMetadata["ImageChunkSize"]);
        Assert.Equal(300, result.PaginationMetadata["ImageChunkDelayMs"]);
        Assert.Equal(5, result.PaginationMetadata["MaxConcurrency"]);
        
        // Verify products phase data
        Assert.Equal(500, result.PaginationMetadata["ProductsPhaseTotal"]);
        Assert.Equal(450, result.PaginationMetadata["ProductsPhaseSuccess"]);
        Assert.Equal(30, result.PaginationMetadata["ProductsPhaseFailure"]);
        Assert.Equal(20, result.PaginationMetadata["ProductsPhaseSkipped"]);
        Assert.Equal("completed", result.PaginationMetadata["ProductsPhaseStatus"]);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WhenCancellationCheckFails_ShouldContinueProcessing()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        var productsProgress = new EntityProgressEntry
        {
            EntityType = "products",
            TotalCount = 100,
            SuccessCount = 90,
            FailureCount = 10,
            SkippedCount = 0,
            Status = "completed"
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        _mockStorageService
            .Setup(x => x.GetEntityProgressAsync("test-migration-123", "products"))
            .ReturnsAsync(new List<EntityProgressEntry> { productsProgress });

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("product-images", result.EntityType);
        Assert.Equal(90, result.TotalCount); // Should still process despite cancellation check failure
        Assert.False(result.SkipDiscovery);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WhenStorageServiceFails_ShouldReturnErrorResult()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _mockStorageService
            .Setup(x => x.GetEntityProgressAsync("test-migration-123", "products"))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("product-images", result.EntityType);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.EntityIds);
        Assert.True(result.SkipDiscovery);
        Assert.Equal(BigCommerceApiVersion.V3, result.ApiVersion);
        Assert.Single(result.Errors);
        Assert.Contains("EntityProgress query failed: Database connection failed", result.Errors.First());
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_WithMultipleProductsEntries_ShouldUseFirstOne()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        var productsProgress1 = new EntityProgressEntry
        {
            EntityType = "products",
            TotalCount = 100,
            SuccessCount = 90,
            FailureCount = 10,
            SkippedCount = 0
        };

        var productsProgress2 = new EntityProgressEntry
        {
            EntityType = "products",
            TotalCount = 200,
            SuccessCount = 180,
            FailureCount = 20,
            SkippedCount = 0
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _mockStorageService
            .Setup(x => x.GetEntityProgressAsync("test-migration-123", "products"))
            .ReturnsAsync(new List<EntityProgressEntry> { productsProgress1, productsProgress2 });

        // Act
        var result = await _strategy.DiscoverEntitiesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(90, result.TotalCount); // Should use first entry's SuccessCount
        Assert.False(result.SkipDiscovery);
    }

    [Fact]
    public void SupportedApiVersion_ShouldReturnV3()
    {
        // Act
        var apiVersion = _strategy.SupportedApiVersion;

        // Assert
        Assert.Equal(BigCommerceApiVersion.V3, apiVersion);
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_ShouldCallCancellationCheckTwice()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        var productsProgress = new EntityProgressEntry
        {
            EntityType = "products",
            SuccessCount = 50
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync("test-migration-123"))
            .ReturnsAsync(false);

        _mockStorageService
            .Setup(x => x.GetEntityProgressAsync("test-migration-123", "products"))
            .ReturnsAsync(new List<EntityProgressEntry> { productsProgress });

        // Act
        await _strategy.DiscoverEntitiesAsync(request);

        // Assert
        _mockCancellationStore.Verify(
            x => x.CheckCancellationFlagAsync("test-migration-123"), 
            Times.Exactly(2));
    }

    [Fact]
    public async Task DiscoverEntitiesAsync_ShouldLogInformationMessages()
    {
        // Arrange
        var request = new EntityDiscoveryRequest
        {
            EntityType = "product-images",
            MigrationId = "test-migration-123"
        };

        var productsProgress = new EntityProgressEntry
        {
            EntityType = "products",
            TotalCount = 100,
            SuccessCount = 90,
            FailureCount = 5,
            SkippedCount = 5
        };

        _mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _mockStorageService
            .Setup(x => x.GetEntityProgressAsync("test-migration-123", "products"))
            .ReturnsAsync(new List<EntityProgressEntry> { productsProgress });

        // Act
        await _strategy.DiscoverEntitiesAsync(request);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[PRODUCT-IMAGES-DISCOVERY] Starting EntityProgress-based discovery")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[PRODUCT-IMAGES-DISCOVERY] EntityProgress discovery completed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
