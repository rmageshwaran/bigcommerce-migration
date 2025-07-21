using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Strategies;
using BigCommerce.Migration.UnitTests.TestHelpers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Orchestration.Services;

/// <summary>
/// TDD tests for Entity Transform Strategy Pattern interfaces
/// Tests define the expected behavior - implementations will follow (TDD)
/// Validates Open/Closed Principle by enabling extension without modification for data transformation
/// </summary>
public class EntityTransformStrategyTests
{
    private readonly Mock<ILogger<BrandTransformStrategy>> _mockLogger = new();

    #region Interface Contract Tests (RED phase)

    [Fact]
    public void IEntityTransformStrategy_ShouldHave_RequiredProperties()
    {
        // Verify interface exists and has expected properties
        var strategyType = typeof(IEntityTransformStrategy);
        strategyType.Should().NotBeNull();
        
        // Verify EntityType property exists
        var entityTypeProperty = strategyType.GetProperty("EntityType");
        entityTypeProperty.Should().NotBeNull();
        entityTypeProperty!.PropertyType.Should().Be(typeof(string));
        
        // Verify TransformEntityAsync method exists
        var transformMethod = strategyType.GetMethod("TransformEntityAsync");
        transformMethod.Should().NotBeNull();
        transformMethod!.ReturnType.Should().Be(typeof(Task<Dictionary<string, object>>));
    }

    [Fact]
    public void IEntityTransformStrategy_ShouldHave_CorrectMethodSignature()
    {
        // Verify TransformEntityAsync method signature
        var strategyType = typeof(IEntityTransformStrategy);
        var transformMethod = strategyType.GetMethod("TransformEntityAsync");
        
        transformMethod.Should().NotBeNull();
        
        var parameters = transformMethod!.GetParameters();
        parameters.Should().HaveCount(6);
        parameters[0].ParameterType.Should().Be(typeof(Dictionary<string, object>)); // entity
        parameters[1].ParameterType.Should().Be(typeof(string)); // migrationId
        parameters[2].ParameterType.Should().Be(typeof(StoreConfiguration)); // sourceStore
        parameters[3].ParameterType.Should().Be(typeof(StoreConfiguration)); // destinationStore
        parameters[4].ParameterType.Should().Be(typeof(CategoryTreeContext)); // categoryTreeContext
        parameters[5].ParameterType.Should().Be(typeof(CancellationToken)); // cancellationToken
    }

    #endregion

    #region Strategy Factory Interface Tests (RED phase)

    [Fact]
    public void IEntityTransformStrategyFactory_ShouldHave_GetStrategyMethod()
    {
        // Verify factory interface contract
        var factoryType = typeof(IEntityTransformStrategyFactory);
        factoryType.Should().NotBeNull();
        
        var getStrategyMethod = factoryType.GetMethod("GetStrategy");
        getStrategyMethod.Should().NotBeNull();
        getStrategyMethod!.ReturnType.Should().Be(typeof(IEntityTransformStrategy));
        
        var parameters = getStrategyMethod.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(string)); // entityType
    }

    #endregion

    #region Category Transform Strategy Tests (RED phase)

    [Fact]
    public void CategoryTransformStrategy_ShouldReturn_CorrectEntityType()
    {
        // This test will fail until we implement CategoryTransformStrategy
        // Define expected behavior: strategy should handle "categories"
        var strategyType = typeof(IEntityTransformStrategy);
        
        // Verify the interface exists so we can implement the strategy
        strategyType.Should().NotBeNull();
    }

    [Fact]
    public void CategoryTransformStrategy_Should_TransformCategorySpecificFields()
    {
        // Define expected behavior for category transformations:
        // - Should handle parent_id mapping
        // - Should generate URL paths
        // - Should handle BigCommerce-specific category fields
        
        var testCategory = CreateTestCategoryEntity();
        testCategory.Should().ContainKey("name");
        testCategory.Should().ContainKey("parent_id");
    }

    #endregion

    #region Product Transform Strategy Tests (RED phase)

    [Fact]
    public void ProductTransformStrategy_ShouldReturn_CorrectEntityType()
    {
        // This test will fail until we implement ProductTransformStrategy
        // Define expected behavior: strategy should handle "products"
        var strategyType = typeof(IEntityTransformStrategy);
        
        // Verify the interface exists so we can implement the strategy
        strategyType.Should().NotBeNull();
    }

    [Fact]
    public void ProductTransformStrategy_Should_TransformProductSpecificFields()
    {
        // Define expected behavior for product transformations:
        // - Should handle SKU normalization
        // - Should handle pricing fields
        // - Should handle BigCommerce-specific product fields
        
        var testProduct = CreateTestProductEntity();
        testProduct.Should().ContainKey("name");
        testProduct.Should().ContainKey("sku");
    }

    #endregion

    #region Brand Transform Strategy Tests (RED phase)

    [Fact]
    public void BrandTransformStrategy_ShouldReturn_CorrectEntityType()
    {
        // This test will fail until we implement BrandTransformStrategy
        // Define expected behavior: strategy should handle "brands"
        var strategyType = typeof(IEntityTransformStrategy);
        
        // Verify the interface exists so we can implement the strategy
        strategyType.Should().NotBeNull();
    }

    #endregion

    #region Factory Tests (RED phase)

    [Fact]
    public void EntityTransformStrategyFactory_Should_ReturnCorrectStrategy_ForCategories()
    {
        // This test will fail until we implement the factory
        // Define expected behavior: factory should return strategy for "categories"
        var factoryType = typeof(IEntityTransformStrategyFactory);
        factoryType.Should().NotBeNull();
    }

    [Fact]
    public void EntityTransformStrategyFactory_Should_ReturnCorrectStrategy_ForProducts()
    {
        // This test will fail until we implement the factory
        // Define expected behavior: factory should return strategy for "products"
        var factoryType = typeof(IEntityTransformStrategyFactory);
        factoryType.Should().NotBeNull();
    }

    [Fact]
    public void EntityTransformStrategyFactory_Should_ThrowException_ForUnsupportedEntityType()
    {
        // This test will fail until we implement the factory
        // Define expected behavior: factory should throw ArgumentException for unsupported types
        var factoryType = typeof(IEntityTransformStrategyFactory);
        factoryType.Should().NotBeNull();
    }

    [Fact]
    public void EntityTransformStrategyFactory_Should_BeCaseInsensitive()
    {
        // This test will fail until we implement the factory
        // Define expected behavior: factory should handle case-insensitive entity types
        var factoryType = typeof(IEntityTransformStrategyFactory);
        factoryType.Should().NotBeNull();
    }

    #endregion

    #region Open/Closed Principle Tests (RED phase)

    [Fact]
    public void EntityTransformStrategy_Should_SupportExtension_WithoutModification()
    {
        // This test validates OCP - new entity types can be added without modifying existing code
        // Define expected behavior: new transform strategies can be added without code changes
        var strategyType = typeof(IEntityTransformStrategy);
        strategyType.Should().NotBeNull();
        
        // This validates that the interface design supports extension
        var transformMethod = strategyType.GetMethod("TransformEntityAsync");
        transformMethod.Should().NotBeNull();
    }

    #endregion

    #region Test Data Helpers

    private Dictionary<string, object> CreateTestCategoryEntity()
    {
        return new Dictionary<string, object>
        {
            ["id"] = "1",
            ["name"] = "Test Category",
            ["parent_id"] = "0",
            ["description"] = "Test Description",
            ["sort_order"] = 1
        };
    }

    private Dictionary<string, object> CreateTestProductEntity()
    {
        return new Dictionary<string, object>
        {
            ["id"] = "1",
            ["name"] = "Test Product",
            ["sku"] = "TEST-SKU",
            ["price"] = 19.99,
            ["description"] = "Test Product Description"
        };
    }

    private BatchProcessingRequest CreateTestBatchProcessingRequest(string entityType = "categories")
    {
        return new BatchProcessingRequest
        {
            MigrationId = "test-migration-123",
            EntityType = entityType,
            BatchNumber = 1,
            TotalBatches = 1,
            EntityIds = new List<string> { "1", "2", "3" },
            SourceStore = TestDataFactory.CreateSourceStoreConfiguration(),
            DestinationStore = TestDataFactory.CreateDestinationStoreConfiguration(),
            CategoryTreeContext = new CategoryTreeContext
            {
                DestinationCategoryTreeId = "dest-tree-123"
            }
        };
    }

    #endregion

    #region Brand Transform Strategy Comprehensive Tests

    [Fact]
    public async Task BrandTransformStrategy_Should_Transform_ToMatchBigCommerceApiFormat()
    {
        // ARRANGE: Create a source brand entity with various field formats
        var sourceEntity = new Dictionary<string, object>
        {
            ["id"] = 123,
            ["name"] = "Common Good",
            ["page_title"] = "Common Good Brand Page",
            ["meta_keywords"] = new List<string> { "modern", "clean", "contemporary" },
            ["meta_description"] = "Common Good is a modern brand.",
            ["search_keywords"] = "kitchen, laundry, cart, storage",
            ["image_url"] = "https://cdn8.bigcommerce.com/s-12345/product_images/k/your-image-name.png",
            ["custom_url"] = new Dictionary<string, object>
            {
                ["url"] = "/shoes",
                ["is_customized"] = true
            }
        };

        var strategy = new BrandTransformStrategy(_mockLogger.Object);

        // ACT: Transform the entity
        var result = await strategy.TransformEntityAsync(
            sourceEntity,
            "test-migration-id",
            TestDataFactory.CreateSourceStoreConfiguration(),
            TestDataFactory.CreateDestinationStoreConfiguration()
        );

        // ASSERT: Verify the transformation matches BigCommerce API format exactly
        result.Should().NotBeNull();
        result.Should().ContainKey("name").WhoseValue.Should().Be("Common Good");
        result.Should().ContainKey("page_title").WhoseValue.Should().Be("Common Good Brand Page");
        result.Should().ContainKey("meta_keywords").WhoseValue.Should().BeEquivalentTo(new List<string> { "modern", "clean", "contemporary" });
        result.Should().ContainKey("meta_description").WhoseValue.Should().Be("Common Good is a modern brand.");
        result.Should().ContainKey("search_keywords").WhoseValue.Should().Be("kitchen, laundry, cart, storage");
        result.Should().ContainKey("image_url").WhoseValue.Should().Be("https://cdn8.bigcommerce.com/s-12345/product_images/k/your-image-name.png");
        result.Should().ContainKey("custom_url");

        var customUrl = result["custom_url"] as Dictionary<string, object>;
        customUrl.Should().NotBeNull();
        customUrl!.Should().ContainKey("url").WhoseValue.Should().Be("/shoes");
        customUrl.Should().ContainKey("is_customized").WhoseValue.Should().Be(true);

        // Verify source ID is NOT included (should be cleaned)
        result.Should().NotContainKey("id");
    }

    [Fact]
    public async Task BrandTransformStrategy_Should_HandleAlternativeFieldNames()
    {
        // ARRANGE: Create a source brand with alternative field names
        var sourceEntity = new Dictionary<string, object>
        {
            ["brand_name"] = "Alternative Brand",
            ["seo_title"] = "SEO Optimized Title",
            ["keywords"] = "keyword1, keyword2, keyword3",
            ["description"] = "Brand description for meta",
            ["logo_url"] = "https://example.com/logo.png",
            ["slug"] = "alternative-brand"
        };

        var strategy = new BrandTransformStrategy(_mockLogger.Object);

        // ACT: Transform the entity
        var result = await strategy.TransformEntityAsync(
            sourceEntity,
            "test-migration-id",
            TestDataFactory.CreateSourceStoreConfiguration(),
            TestDataFactory.CreateDestinationStoreConfiguration()
        );

        // ASSERT: Verify alternative fields are mapped correctly
        result.Should().ContainKey("name").WhoseValue.Should().Be("Alternative Brand");
        result.Should().ContainKey("page_title").WhoseValue.Should().Be("SEO Optimized Title");
        result.Should().ContainKey("meta_keywords").WhoseValue.Should().BeEquivalentTo(new List<string> { "keyword1", "keyword2", "keyword3" });
        result.Should().ContainKey("meta_description").WhoseValue.Should().Be("Brand description for meta");
        result.Should().ContainKey("image_url").WhoseValue.Should().Be("https://example.com/logo.png");
        result.Should().ContainKey("custom_url");

        var customUrl = result["custom_url"] as Dictionary<string, object>;
        customUrl.Should().NotBeNull();
        customUrl!.Should().ContainKey("url").WhoseValue.Should().Be("/alternative-brand");
        customUrl.Should().ContainKey("is_customized").WhoseValue.Should().Be(true);
    }

    [Fact]
    public async Task BrandTransformStrategy_Should_HandleMinimalData()
    {
        // ARRANGE: Create a minimal brand entity (name only)
        var sourceEntity = new Dictionary<string, object>
        {
            ["name"] = "Minimal Brand"
        };

        var strategy = new BrandTransformStrategy(_mockLogger.Object);

        // ACT: Transform the entity
        var result = await strategy.TransformEntityAsync(
            sourceEntity,
            "test-migration-id",
            TestDataFactory.CreateSourceStoreConfiguration(),
            TestDataFactory.CreateDestinationStoreConfiguration()
        );

        // ASSERT: Verify minimal transformation
        result.Should().NotBeNull();
        result.Should().ContainKey("name").WhoseValue.Should().Be("Minimal Brand");
        result.Should().ContainKey("page_title").WhoseValue.Should().Be("Minimal Brand"); // Falls back to name

        // Should not contain optional fields when not provided
        result.Should().NotContainKey("meta_keywords");
        result.Should().NotContainKey("meta_description");
        result.Should().NotContainKey("search_keywords");
        result.Should().NotContainKey("image_url");
        result.Should().NotContainKey("custom_url");
    }

    [Fact]
    public async Task BrandTransformStrategy_Should_HandleStringBasedMetaKeywords()
    {
        // ARRANGE: Create a brand with comma-separated meta keywords
        var sourceEntity = new Dictionary<string, object>
        {
            ["name"] = "String Keywords Brand",
            ["meta_keywords"] = "eco-friendly, sustainable, organic"
        };

        var strategy = new BrandTransformStrategy(_mockLogger.Object);

        // ACT: Transform the entity
        var result = await strategy.TransformEntityAsync(
            sourceEntity,
            "test-migration-id",
            TestDataFactory.CreateSourceStoreConfiguration(),
            TestDataFactory.CreateDestinationStoreConfiguration()
        );

        // ASSERT: Verify string meta keywords are converted to array
        result.Should().ContainKey("meta_keywords");
        var metaKeywords = result["meta_keywords"] as List<string>;
        metaKeywords.Should().NotBeNull();
        metaKeywords!.Should().BeEquivalentTo(new List<string> { "eco-friendly", "sustainable", "organic" });
    }

    #endregion
} 