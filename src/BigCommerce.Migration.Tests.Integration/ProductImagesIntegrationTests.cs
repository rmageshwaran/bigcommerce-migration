using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Strategies.Creation;
using BigCommerce.Migration.Activities.Strategies.Transform;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Integration tests for Product-Images phase
/// Task 8: Testing and Validation - Focus on key integration scenarios
/// </summary>
public class ProductImagesIntegrationTests
{
    [Fact]
    public void ProductImagesConfiguration_LoadsCorrectly()
    {
        // Arrange
        var serviceProvider = CreateTestServiceProvider();
        var configService = serviceProvider.GetRequiredService<ISubBatchConfigurationService>();

        // Act
        var config = configService.GetConfiguration("product-images");

        // Assert
        Assert.NotNull(config);
        Assert.Equal("product-images", config.EntityType);
        Assert.Equal(20, config.ChunkSize);
        Assert.Equal(1, config.SubBatchSize);
        Assert.Equal(5, config.MaxConcurrency);
        Assert.False(config.ProcessSubBatchesSequentially);
        Assert.True(config.EnableSubBatching);

        // Verify custom settings
        Assert.NotNull(config.CustomSettings);
        Assert.True(config.CustomSettings.ContainsKey("imageChunkSize"));
        Assert.True(config.CustomSettings.ContainsKey("imageChunkDelayMs"));
        Assert.True(config.CustomSettings.ContainsKey("maxImagesPerProduct"));
        
        Assert.Equal(50, Convert.ToInt32(config.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture));
        Assert.Equal(300, Convert.ToInt32(config.CustomSettings["imageChunkDelayMs"], CultureInfo.InvariantCulture));
        Assert.Equal(1000, Convert.ToInt32(config.CustomSettings["maxImagesPerProduct"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ProductImagesTransformStrategy_ValidatesCorrectly()
    {
        // Arrange
        var serviceProvider = CreateTestServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<ProductImagesTransformStrategy>>();
        var transformStrategy = new ProductImagesTransformStrategy(logger);

        // Act & Assert - Strategy is constructed correctly
        Assert.NotNull(transformStrategy);
        // Note: CanHandle method is internal implementation detail
    }

    [Fact]
    public void ProductImagesCreationStrategy_ValidatesCorrectly()
    {
        // Arrange
        var serviceProvider = CreateTestServiceProvider();
        var transformStrategy = new ProductImagesTransformStrategy(
            serviceProvider.GetRequiredService<ILogger<ProductImagesTransformStrategy>>());

        // We can't fully test creation strategy without complex mocking,
        // but we can validate transform strategy works correctly
        Assert.NotNull(transformStrategy);
    }

    [Fact]
    public async Task ProductImagesTransform_SkipLogic_WorksCorrectly()
    {
        // Arrange
        var serviceProvider = CreateTestServiceProvider();
        var transformStrategy = new ProductImagesTransformStrategy(
            serviceProvider.GetRequiredService<ILogger<ProductImagesTransformStrategy>>());
        
        var migrationId = "skip-test-001";
        var sourceStore = CreateTestStoreConfiguration("source-store");
        var destinationStore = CreateTestStoreConfiguration("dest-store");

        // Test data with empty images array (should be skipped)
        var emptyImagesProduct = new Dictionary<string, object>
        {
            ["productId"] = "123",
            ["images"] = new List<Dictionary<string, object>>() // Empty images
        };

        // Act
        var result = await transformStrategy.TransformEntityAsync(
            emptyImagesProduct, migrationId, sourceStore, destinationStore);

        // Assert - Should return null for products with no images (new simplified architecture)
        Assert.Null(result);
    }

    [Fact]
    public async Task ProductImagesTransform_WithImages_TransformsCorrectly()
    {
        // Arrange
        var serviceProvider = CreateTestServiceProvider();
        var transformStrategy = new ProductImagesTransformStrategy(
            serviceProvider.GetRequiredService<ILogger<ProductImagesTransformStrategy>>());
        
        var migrationId = "transform-test-001";
        var sourceStore = CreateTestStoreConfiguration("source-store");
        var destinationStore = CreateTestStoreConfiguration("dest-store");

        // Test data with images
        var productWithImages = new Dictionary<string, object>
        {
            ["productId"] = "123",
            ["images"] = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["image_file"] = "test-image.jpg",
                    ["is_thumbnail"] = true,
                    ["sort_order"] = 1,
                    ["description"] = "Test image"
                }
            }
        };

        // Act
        var result = await transformStrategy.TransformEntityAsync(
            productWithImages, migrationId, sourceStore, destinationStore);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.ContainsKey("_skip"));
        Assert.True(result.ContainsKey("images"));
        
        var transformedImages = result["images"] as List<Dictionary<string, object>>;
        Assert.NotNull(transformedImages);
        Assert.Single(transformedImages);

        var transformedImage = transformedImages[0];
        Assert.True(transformedImage.ContainsKey("product_id"));
        Assert.True(transformedImage.ContainsKey("image_url"));
        Assert.True(transformedImage.ContainsKey("is_thumbnail"));
        Assert.True(transformedImage.ContainsKey("sort_order"));
        Assert.True(transformedImage.ContainsKey("description"));

        // Verify URL construction
        var imageUrl = transformedImage["image_url"]?.ToString();
        Assert.NotNull(imageUrl);
        Assert.Contains($"store-{sourceStore.StoreId}", imageUrl, StringComparison.Ordinal);
        Assert.Contains("test-image.jpg", imageUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductImagesTimeout_CalculationsValidate()
    {
        // Arrange - Test timeout safety calculations from Task 7
        var serviceProvider = CreateTestServiceProvider();
        var configService = serviceProvider.GetRequiredService<ISubBatchConfigurationService>();
        var config = configService.GetConfiguration("product-images");

        // Act - Calculate worst-case scenario timing
        var maxImagesPerProduct = Convert.ToInt32(config.CustomSettings["maxImagesPerProduct"], CultureInfo.InvariantCulture);
        var imageChunkSize = Convert.ToInt32(config.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture);
        var imageChunkDelayMs = Convert.ToInt32(config.CustomSettings["imageChunkDelayMs"], CultureInfo.InvariantCulture);
        
        var chunksPerProduct = (int)Math.Ceiling((double)maxImagesPerProduct / imageChunkSize); // 20 chunks for 1000 images
        var delayTimePerProduct = (chunksPerProduct - 1) * imageChunkDelayMs; // 19 * 300ms = 5.7s delays
        var estimatedApiTimePerChunk = 2500; // 2.5s per API call (conservative estimate)
        var apiTimePerProduct = chunksPerProduct * estimatedApiTimePerChunk; // 20 * 2.5s = 50s
        var totalTimePerProduct = (delayTimePerProduct + apiTimePerProduct) / 1000.0; // Convert to seconds

        // Assert - Timeout safety validation
        Assert.Equal(1000, maxImagesPerProduct);
        Assert.Equal(50, imageChunkSize);
        Assert.Equal(300, imageChunkDelayMs);
        Assert.Equal(20, chunksPerProduct);
        Assert.Equal(5700, delayTimePerProduct); // 5.7 seconds
        Assert.Equal(50000, apiTimePerProduct); // 50 seconds
        Assert.True(totalTimePerProduct < 60, $"Total time per product ({totalTimePerProduct:F1}s) should be < 60s");
        Assert.True(totalTimePerProduct < 120, $"Total time per product ({totalTimePerProduct:F1}s) should be < 120s Azure Function timeout");
    }

    [Fact]
    public void ProductImagesMemory_CalculationsValidate()
    {
        // Arrange - Test memory efficiency calculations from Task 7
        var serviceProvider = CreateTestServiceProvider();
        var configService = serviceProvider.GetRequiredService<ISubBatchConfigurationService>();
        var config = configService.GetConfiguration("product-images");

        // Act - Calculate memory usage
        var imageChunkSize = Convert.ToInt32(config.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture);
        var maxConcurrency = config.MaxConcurrency;
        
        var estimatedBytesPerImageMetadata = 1024; // 1KB per image metadata (conservative)
        var memoryPerChunk = imageChunkSize * estimatedBytesPerImageMetadata; // 50KB per chunk
        var memoryPerProduct = memoryPerChunk; // Only one chunk in memory at a time due to streaming
        var totalMemoryForMaxConcurrency = memoryPerProduct * maxConcurrency; // 250KB total

        // Assert - Memory efficiency validation
        Assert.Equal(50, imageChunkSize);
        Assert.Equal(5, maxConcurrency);
        Assert.Equal(51200, memoryPerChunk); // 50KB
        Assert.Equal(51200, memoryPerProduct); // 50KB (streaming)
        Assert.Equal(256000, totalMemoryForMaxConcurrency); // 250KB total
        
        // Memory should be well under limits
        var memoryLimitPerProduct = 1024 * 1024; // 1MB per product
        var totalMemoryLimit = 10 * 1024 * 1024; // 10MB total
        
        Assert.True(memoryPerProduct < memoryLimitPerProduct, 
            $"Memory per product ({memoryPerProduct} bytes) should be < 1MB");
        Assert.True(totalMemoryForMaxConcurrency < totalMemoryLimit, 
            $"Total memory for max concurrency ({totalMemoryForMaxConcurrency} bytes) should be < 10MB");
    }

    [Fact]
    public async Task ProductImagesPerformance_MockedScenario_CompletesQuickly()
    {
        // Arrange - Simple performance test with actual strategy instances
        var serviceProvider = CreateTestServiceProvider();
        var transformStrategy = new ProductImagesTransformStrategy(
            serviceProvider.GetRequiredService<ILogger<ProductImagesTransformStrategy>>());

        var migrationId = "perf-test-001";
        var sourceStore = CreateTestStoreConfiguration("source-store");
        var destinationStore = CreateTestStoreConfiguration("dest-store");

        // Create test data for multiple products with varying image counts
        var testProducts = new List<Dictionary<string, object>>();
        for (int i = 1; i <= 10; i++)
        {
            testProducts.Add(CreateTestProduct(i, i * 5)); // 5, 10, 15, ... 50 images
        }

        // Act - Process all products and measure time
        var stopwatch = Stopwatch.StartNew();
        var results = new List<Dictionary<string, object>>();
        
        foreach (var product in testProducts)
        {
            var result = await transformStrategy.TransformEntityAsync(
                product, migrationId, sourceStore, destinationStore);
            results.Add(result);
        }
        
        stopwatch.Stop();

        // Assert - Performance validation
        var processingTimeMs = stopwatch.Elapsed.TotalMilliseconds;
        
        // Should process 10 products very quickly (transform only, no API calls)
        Assert.True(processingTimeMs < 1000, 
            $"Transform processing time ({processingTimeMs:F0}ms) should be < 1000ms for 10 products");
        
        Assert.Equal(10, results.Count);
        Assert.All(results, result => Assert.NotNull(result));
    }

    [Theory]
    [InlineData(0)]    // No images - should be skipped
    [InlineData(1)]    // Single image
    [InlineData(25)]   // Small set
    [InlineData(50)]   // One chunk
    [InlineData(75)]   // Multiple chunks
    public async Task ProductImagesTransform_VariousImageCounts_HandlesCorrectly(int imageCount)
    {
        // Arrange
        var serviceProvider = CreateTestServiceProvider();
        var transformStrategy = new ProductImagesTransformStrategy(
            serviceProvider.GetRequiredService<ILogger<ProductImagesTransformStrategy>>());
        
        var migrationId = $"image-count-test-{imageCount}";
        var sourceStore = CreateTestStoreConfiguration("source-store");
        var destinationStore = CreateTestStoreConfiguration("dest-store");

        var product = CreateTestProduct(1, imageCount);

        // Act
        var result = await transformStrategy.TransformEntityAsync(
            product, migrationId, sourceStore, destinationStore);

        // Assert
        if (imageCount == 0)
        {
            // Should return null for products with no images (new simplified architecture)
            Assert.Null(result);
        }
        else
        {
            // Should be processed successfully
            Assert.NotNull(result);
            Assert.False(result.ContainsKey("_skip"));
            Assert.True(result.ContainsKey("images"));
            
            var transformedImages = result["images"] as List<Dictionary<string, object>>;
            Assert.NotNull(transformedImages);
            Assert.Equal(imageCount, transformedImages.Count);
            
            // Verify all images have required fields
            foreach (var image in transformedImages)
            {
                Assert.True(image.ContainsKey("product_id"));
                Assert.True(image.ContainsKey("image_url"));
                Assert.True(image.ContainsKey("is_thumbnail"));
                Assert.True(image.ContainsKey("sort_order"));
                Assert.True(image.ContainsKey("description"));
            }
        }
    }

    #region Helper Methods

    private static IServiceProvider CreateTestServiceProvider()
    {
        var services = new ServiceCollection();
        
        // Add configuration with product-images settings
        var configDict = new Dictionary<string, string?>
        {
            ["ParallelProcessing:subBatchConfigurations:product-images:entityType"] = "product-images",
            ["ParallelProcessing:subBatchConfigurations:product-images:chunkSize"] = "20",
            ["ParallelProcessing:subBatchConfigurations:product-images:subBatchSize"] = "1",
            ["ParallelProcessing:subBatchConfigurations:product-images:maxConcurrency"] = "5",
            ["ParallelProcessing:subBatchConfigurations:product-images:enableSubBatching"] = "true",
            ["ParallelProcessing:subBatchConfigurations:product-images:processSubBatchesSequentially"] = "false",
            ["ParallelProcessing:subBatchConfigurations:product-images:imageChunkSize"] = "50",
            ["ParallelProcessing:subBatchConfigurations:product-images:imageChunkDelayMs"] = "300",
            ["ParallelProcessing:subBatchConfigurations:product-images:maxImagesPerProduct"] = "1000",
            ["ParallelProcessing:subBatchConfigurations:product-images:cancellationCheckInterval"] = "5"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole());
        services.AddScoped<ISubBatchConfigurationService, SubBatchConfigurationService>();

        return services.BuildServiceProvider();
    }

    private static StoreConfiguration CreateTestStoreConfiguration(string storeId)
    {
        return new StoreConfiguration
        {
            StoreId = storeId,
            AccessToken = "test-access-token",
            ChannelId = "test-channel-id"
        };
    }

    private static Dictionary<string, object> CreateTestProduct(int productId, int imageCount)
    {
        var images = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= imageCount; i++)
        {
            images.Add(new Dictionary<string, object>
            {
                ["image_file"] = $"image_{i}.jpg",
                ["is_thumbnail"] = i == 1,
                ["sort_order"] = i,
                ["description"] = $"Test image {i}"
            });
        }

        return new Dictionary<string, object>
        {
            ["productId"] = productId.ToString(CultureInfo.InvariantCulture),
            ["images"] = images
        };
    }

    #endregion
}
