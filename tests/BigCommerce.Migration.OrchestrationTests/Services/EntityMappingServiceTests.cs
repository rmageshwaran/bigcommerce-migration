using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BigCommerce.Migration.OrchestrationTests.Services;

public class EntityMappingServiceTests
{
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ILogger<EntityMappingService>> _mockLogger;
    private readonly EntityMappingService _service;

    public EntityMappingServiceTests()
    {
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockLogger = new Mock<ILogger<EntityMappingService>>();
        
        _service = new EntityMappingService(
            _mockStorageService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void CreateEntityMapping_ForCategories_ShouldExtractCategoryId()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["category_id"] = "123",
            ["name"] = "Test Category",
            ["parent_id"] = "0"
        };
        
        var destinationEntity = new Dictionary<string, object>
        {
            ["category_id"] = "456",
            ["name"] = "Test Category",
            ["parent_id"] = "0"
        };
        
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" }
        };

        // Act
        var mapping = _service.CreateEntityMapping(sourceEntity, destinationEntity, request);

        // Assert
        Assert.NotNull(mapping);
        Assert.Equal("123", mapping.SourceId);
        Assert.Equal("456", mapping.DestinationId);
        Assert.Equal("categories", mapping.EntityType);
        Assert.Equal("test-migration", mapping.MigrationId);
    }

    [Fact]
    public void CreateEntityMapping_ForProducts_ShouldExtractProductId()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["product_id"] = "789",
            ["name"] = "Test Product",
            ["price"] = 99.99m
        };
        
        var destinationEntity = new Dictionary<string, object>
        {
            ["product_id"] = "101",
            ["name"] = "Test Product",
            ["price"] = 99.99m
        };
        
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" }
        };

        // Act
        var mapping = _service.CreateEntityMapping(sourceEntity, destinationEntity, request);

        // Assert
        Assert.NotNull(mapping);
        Assert.Equal("789", mapping.SourceId);
        Assert.Equal("101", mapping.DestinationId);
        Assert.Equal("products", mapping.EntityType);
    }

    [Fact]
    public void CreateEntityMapping_ForBrands_ShouldExtractBrandId()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["brand_id"] = "202",
            ["name"] = "Test Brand"
        };
        
        var destinationEntity = new Dictionary<string, object>
        {
            ["brand_id"] = "303",
            ["name"] = "Test Brand"
        };
        
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "brands",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" }
        };

        // Act
        var mapping = _service.CreateEntityMapping(sourceEntity, destinationEntity, request);

        // Assert
        Assert.NotNull(mapping);
        Assert.Equal("202", mapping.SourceId);
        Assert.Equal("303", mapping.DestinationId);
        Assert.Equal("brands", mapping.EntityType);
    }

    [Fact]
    public void CreateEntityMapping_ForUnknownEntityType_ShouldFallbackToGenericId()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["id"] = "404",
            ["name"] = "Test Entity"
        };
        
        var destinationEntity = new Dictionary<string, object>
        {
            ["id"] = "505",
            ["name"] = "Test Entity"
        };
        
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "unknown_entity",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" }
        };

        // Act
        var mapping = _service.CreateEntityMapping(sourceEntity, destinationEntity, request);

        // Assert
        Assert.NotNull(mapping);
        Assert.Equal("404", mapping.SourceId);
        Assert.Equal("505", mapping.DestinationId);
        Assert.Equal("unknown_entity", mapping.EntityType);
    }

    [Fact]
    public void CreateEntityMapping_WithMissingId_ShouldThrowException()
    {
        // Arrange
        var sourceEntity = new Dictionary<string, object>
        {
            ["name"] = "Test Category",
            ["parent_id"] = "0"
        };
        
        var destinationEntity = new Dictionary<string, object>
        {
            ["name"] = "Test Category",
            ["parent_id"] = "0"
        };
        
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" }
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            _service.CreateEntityMapping(sourceEntity, destinationEntity, request));
        
        Assert.Contains("Failed to create entity mapping for categories", exception.Message);
        Assert.Contains("Entity is missing ID field for entity type 'categories'", exception.InnerException?.Message);
        Assert.Contains("name", exception.InnerException?.Message);
        Assert.Contains("parent_id", exception.InnerException?.Message);
    }

    [Fact]
    public void CreateEntityMapping_WithNullEntities_ShouldThrowException()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" }
        };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _service.CreateEntityMapping(null!, new Dictionary<string, object>(), request));
        
        Assert.Throws<ArgumentNullException>(() => 
            _service.CreateEntityMapping(new Dictionary<string, object>(), null!, request));
        
        Assert.Throws<ArgumentNullException>(() => 
            _service.CreateEntityMapping(new Dictionary<string, object>(), new Dictionary<string, object>(), null!));
    }

    [Fact]
    public async Task StoreEntityMappingAsync_ShouldCallStorageService()
    {
        // Arrange
        var mapping = new EntityMapping
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceId = "123",
            DestinationId = "456",
            SourceStoreId = "source-store",
            DestinationStoreId = "dest-store",
            Status = "completed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _mockStorageService.Setup(x => x.StoreEntityMappingsAsync(
            It.Is<List<EntityMapping>>(list => list.Count == 1 && list[0] == mapping)))
            .ReturnsAsync(new List<EntityMapping> { mapping });

        // Act
        await _service.StoreEntityMappingAsync(mapping, CancellationToken.None);

        // Assert
        _mockStorageService.Verify(x => x.StoreEntityMappingsAsync(
            It.Is<List<EntityMapping>>(list => list.Count == 1 && list[0] == mapping)), Times.Once);
    }

    [Fact]
    public async Task StoreEntityMappingsAsync_ShouldCallStorageService()
    {
        // Arrange
        var mappings = new List<EntityMapping>
        {
            new EntityMapping
            {
                MigrationId = "test-migration",
                EntityType = "categories",
                SourceId = "123",
                DestinationId = "456",
                SourceStoreId = "source-store",
                DestinationStoreId = "dest-store",
                Status = "completed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new EntityMapping
            {
                MigrationId = "test-migration",
                EntityType = "products",
                SourceId = "789",
                DestinationId = "101",
                SourceStoreId = "source-store",
                DestinationStoreId = "dest-store",
                Status = "completed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        _mockStorageService.Setup(x => x.StoreEntityMappingsAsync(mappings))
            .ReturnsAsync(mappings);

        // Act
        await _service.StoreEntityMappingsAsync(mappings, CancellationToken.None);

        // Assert
        _mockStorageService.Verify(x => x.StoreEntityMappingsAsync(mappings), Times.Once);
    }
} 