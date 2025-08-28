using System.Text.Json;
using BigCommerce.Migration.Activities.Strategies.Transform;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Strategies.Transform;

/// <summary>
/// Unit tests for ProductImagesTransformStrategy.
/// Tests the transformation of product image data into BigCommerce product update payloads.
/// </summary>
public class ProductImagesTransformStrategyTests
{
    private readonly Mock<ILogger<ProductImagesTransformStrategy>> _mockLogger;
    private readonly ProductImagesTransformStrategy _strategy;
    private readonly StoreConfiguration _sourceStore;
    private readonly StoreConfiguration _destinationStore;
    private const string TestMigrationId = "test-migration-123";
    private const string TestProductId = "456";

    public ProductImagesTransformStrategyTests()
    {
        _mockLogger = new Mock<ILogger<ProductImagesTransformStrategy>>();
        _strategy = new ProductImagesTransformStrategy(_mockLogger.Object);
        
        _sourceStore = new StoreConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com",
            AccessToken = "source-token",
            StoreId = "source-store-123",
            ChannelId = "1"
        };
        
        _destinationStore = new StoreConfiguration
        {
            BaseUrl = "https://api.bigcommerce.com",
            AccessToken = "dest-token", 
            StoreId = "dest-store-456",
            ChannelId = "1"
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ProductImagesTransformStrategy(null!));
    }

    [Fact]
    public void Constructor_WithValidLogger_ShouldSetEntityType()
    {
        // Act
        var strategy = new ProductImagesTransformStrategy(_mockLogger.Object);

        // Assert
        Assert.Equal("product-images", strategy.EntityType);
    }

    #endregion

    #region TransformEntityAsync Tests

    [Fact]
    public async Task TransformEntityAsync_WithNullEntity_ShouldReturnNull()
    {
        // Act
        var result = await _strategy.TransformEntityAsync(
            null!, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.Null(result);
        VerifyLoggerCalled(LogLevel.Warning, "Null entity provided");
    }

    [Fact]
    public async Task TransformEntityAsync_WithMissingProductId_ShouldReturnNull()
    {
        // Arrange
        var entity = new Dictionary<string, object>
        {
            ["images"] = new List<Dictionary<string, object>>()
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.Null(result);
        VerifyLoggerCalled(LogLevel.Warning, "Missing or invalid productId");
    }

    [Fact]
    public async Task TransformEntityAsync_WithEmptyImageArray_ShouldReturnSkipMarker()
    {
        // Arrange
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = new List<Dictionary<string, object>>()
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert - Should return null for empty images (new simplified architecture)
        Assert.Null(result);
        VerifyLoggerCalled(LogLevel.Warning, "No images provided for transformation");
    }

    [Fact]
    public async Task TransformEntityAsync_WithValidSingleImage_ShouldReturnTransformedPayload()
    {
        // Arrange
        var sourceImage = CreateValidSourceImage("o/123/test-image.jpg", true, 1, "Test Description");
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = new List<Dictionary<string, object>> { sourceImage }
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestProductId, result["id"].ToString());
        
        var images = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        Assert.Single(images);
        
        var transformedImage = images[0];
        Assert.Equal($"https://store-{_sourceStore.StoreId}.mybigcommerce.com/product_images/o/123/test-image.jpg", 
            transformedImage["image_url"]);
        Assert.Equal(TestProductId, transformedImage["product_id"]);
        Assert.Equal(true, transformedImage["is_thumbnail"]);
        Assert.Equal(1, transformedImage["sort_order"]);
        Assert.Equal("Test Description", transformedImage["description"]);
    }

    [Fact]
    public async Task TransformEntityAsync_WithMultipleImages_ShouldTransformAll()
    {
        // Arrange
        var images = new List<Dictionary<string, object>>
        {
            CreateValidSourceImage("o/123/image1.jpg", true, 1, "Image 1"),
            CreateValidSourceImage("o/124/image2.jpg", false, 2, "Image 2"),
            CreateValidSourceImage("o/125/image3.jpg", false, 3, "Image 3")
        };
        
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = images
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        Assert.Equal(3, transformedImages.Count);

        // Verify first image (thumbnail)
        Assert.Equal(true, transformedImages[0]["is_thumbnail"]);
        Assert.Equal(1, transformedImages[0]["sort_order"]);
        
        // Verify second image
        Assert.Equal(false, transformedImages[1]["is_thumbnail"]);
        Assert.Equal(2, transformedImages[1]["sort_order"]);
        
        // Verify third image  
        Assert.Equal(false, transformedImages[2]["is_thumbnail"]);
        Assert.Equal(3, transformedImages[2]["sort_order"]);
    }

    [Fact]
    public async Task TransformEntityAsync_WithMixedValidAndInvalidImages_ShouldSkipInvalid()
    {
        // Arrange
        var images = new List<Dictionary<string, object>>
        {
            CreateValidSourceImage("o/123/valid.jpg", true, 1, "Valid Image"),
            new Dictionary<string, object> { ["invalid"] = "missing image_file" }, // Invalid - missing image_file
            CreateValidSourceImage("o/125/valid2.jpg", false, 3, "Valid Image 2")
        };
        
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = images
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        Assert.Equal(2, transformedImages.Count); // Only valid images should be included
        
        // Verify skipped count was logged
        VerifyLoggerCalled(LogLevel.Information, "1 skipped");
    }

    [Fact]
    public async Task TransformEntityAsync_WithJsonElementArray_ShouldTransformCorrectly()
    {
        // Arrange
        var jsonArray = JsonSerializer.SerializeToElement(new[]
        {
            CreateValidSourceImage("o/123/json-image.jpg", true, 1, "JSON Image")
        });
        
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = jsonArray
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        Assert.Single(transformedImages);
        Assert.Equal($"https://store-{_sourceStore.StoreId}.mybigcommerce.com/product_images/o/123/json-image.jpg", 
            transformedImages[0]["image_url"]);
    }

    #endregion

    #region URL Construction Tests

    [Fact]
    public async Task TransformEntityAsync_ShouldConstructCorrectVirtualPathUrl()
    {
        // Arrange
        var sourceImage = CreateValidSourceImage("o/subfolder/my-product-image.png", true, 1, "Test");
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = new List<Dictionary<string, object>> { sourceImage }
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        var expectedUrl = $"https://store-{_sourceStore.StoreId}.mybigcommerce.com/product_images/o/subfolder/my-product-image.png";
        Assert.Equal(expectedUrl, transformedImages[0]["image_url"]);
    }

    [Fact]
    public async Task TransformEntityAsync_WithMissingImageFile_ShouldUseFallbackUrl()
    {
        // Arrange
        var sourceImage = new Dictionary<string, object>
        {
            ["url_standard"] = "https://cdn.bigcommerce.com/fallback-url.jpg",
            ["is_thumbnail"] = true,
            ["sort_order"] = 1,
            ["description"] = "Test"
            // Missing image_file field
        };
        
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = new List<Dictionary<string, object>> { sourceImage }
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        Assert.Equal("https://cdn.bigcommerce.com/fallback-url.jpg", transformedImages[0]["image_url"]);
        VerifyLoggerCalled(LogLevel.Warning, "Missing or empty 'image_file' field");
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public async Task TransformEntityAsync_WithMissingOptionalFields_ShouldSetDefaults()
    {
        // Arrange
        var sourceImage = new Dictionary<string, object>
        {
            ["image_file"] = "o/123/minimal-image.jpg"
            // Missing all optional fields
        };
        
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = new List<Dictionary<string, object>> { sourceImage }
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        var image = transformedImages[0];
        
        // Verify defaults are set
        Assert.Equal(TestProductId, image["product_id"]);
        Assert.Equal(1, image["sort_order"]); // Should default to image index
        Assert.Equal(true, image["is_thumbnail"]); // First image should be thumbnail
        Assert.Equal(string.Empty, image["description"]); // Should default to empty string
    }

    [Fact]
    public async Task TransformEntityAsync_WithMultipleImages_ShouldSetCorrectThumbnailDefaults()
    {
        // Arrange
        var images = new List<Dictionary<string, object>>
        {
            new() { ["image_file"] = "o/123/first.jpg" },
            new() { ["image_file"] = "o/124/second.jpg" },
            new() { ["image_file"] = "o/125/third.jpg" }
        };
        
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = images
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        
        // Only first image should be thumbnail by default
        Assert.Equal(true, transformedImages[0]["is_thumbnail"]);
        Assert.Equal(false, transformedImages[1]["is_thumbnail"]);
        Assert.Equal(false, transformedImages[2]["is_thumbnail"]);
        
        // Sort orders should be sequential
        Assert.Equal(1, transformedImages[0]["sort_order"]);
        Assert.Equal(2, transformedImages[1]["sort_order"]);
        Assert.Equal(3, transformedImages[2]["sort_order"]);
    }

    #endregion

    #region Field Transformation Tests

    [Theory]
    [InlineData("1", 1)]
    [InlineData("999", 999)]
    [InlineData(42, 42)]
    [InlineData(3.14, 3)]
    [InlineData("invalid", 1)] // Should fallback to image index
    public async Task TransformEntityAsync_ShouldTransformSortOrderCorrectly(object sortOrderValue, int expectedResult)
    {
        // Arrange
        var sourceImage = new Dictionary<string, object>
        {
            ["image_file"] = "o/123/test.jpg",
            ["sort_order"] = sortOrderValue,
            ["is_thumbnail"] = false,
            ["description"] = "Test"
        };
        
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = new List<Dictionary<string, object>> { sourceImage }
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        Assert.Equal(expectedResult, transformedImages[0]["sort_order"]);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("invalid", false)] // Should fallback to false for non-first image
    public async Task TransformEntityAsync_ShouldTransformIsThumbnailCorrectly(object isThumbnailValue, bool expectedResult)
    {
        // Arrange
        var sourceImage = new Dictionary<string, object>
        {
            ["image_file"] = "o/123/test.jpg",
            ["is_thumbnail"] = isThumbnailValue,
            ["sort_order"] = 2, // Not first image
            ["description"] = "Test"
        };
        
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = new List<Dictionary<string, object>> { sourceImage }
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        Assert.Equal(expectedResult, transformedImages[0]["is_thumbnail"]);
    }

    [Theory]
    [InlineData("  Test Description  ", "Test Description")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public async Task TransformEntityAsync_ShouldTransformDescriptionCorrectly(object descriptionValue, string expectedResult)
    {
        // Arrange
        var sourceImage = new Dictionary<string, object>
        {
            ["image_file"] = "o/123/test.jpg",
            ["is_thumbnail"] = false,
            ["sort_order"] = 1
        };
        
        if (descriptionValue != null)
        {
            sourceImage["description"] = descriptionValue;
        }
        
        var entity = new Dictionary<string, object>
        {
            ["productId"] = TestProductId,
            ["images"] = new List<Dictionary<string, object>> { sourceImage }
        };

        // Act
        var result = await _strategy.TransformEntityAsync(
            entity, TestMigrationId, _sourceStore, _destinationStore);

        // Assert
        Assert.NotNull(result);
        var transformedImages = Assert.IsType<List<Dictionary<string, object>>>(result["images"]);
        Assert.Equal(expectedResult, transformedImages[0]["description"]);
    }

    #endregion

    #region Helper Methods

    private Dictionary<string, object> CreateValidSourceImage(string imageFile, bool isThumbnail, int sortOrder, string description)
    {
        return new Dictionary<string, object>
        {
            ["image_file"] = imageFile,
            ["is_thumbnail"] = isThumbnail,
            ["sort_order"] = sortOrder,
            ["description"] = description,
            ["url_standard"] = "https://cdn.bigcommerce.com/test.jpg",
            // Source-only fields that should be stripped
            ["id"] = "999",
            ["product_id"] = "888",
            ["date_created"] = "2023-01-01",
            ["date_modified"] = "2023-01-02",
            ["alt_text"] = "Should be removed"
        };
    }

    private void VerifyLoggerCalled(LogLevel logLevel, string expectedMessage)
    {
        _mockLogger.Verify(
            x => x.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion
}
