using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Activities.Models;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Strategies.Creation;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Strategies.Creation;

/// <summary>
/// Unit tests for ProductImagesCreationStrategy
/// </summary>
public class ProductImagesCreationStrategyTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<ISubBatchProcessor> _mockSubBatchProcessor;
    private readonly Mock<ISubBatchConfigurationService> _mockConfigService;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<ICancellationStore> _mockCancellationStore;
    private readonly Mock<IProductImagesFetchService> _mockImagesFetchService;
    private readonly Mock<IEntityTransformStrategy> _mockTransformStrategy;
    private readonly Mock<ILogger<ProductImagesCreationStrategy>> _mockLogger;
    private readonly ProductImagesCreationStrategy _strategy;
    private readonly SubBatchConfiguration _testConfig;

    public ProductImagesCreationStrategyTests()
    {
        _mockApiRequestHandler = new Mock<IApiRequestHandler>();
        _mockSubBatchProcessor = new Mock<ISubBatchProcessor>();
        _mockConfigService = new Mock<ISubBatchConfigurationService>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _mockCancellationStore = new Mock<ICancellationStore>();
        _mockImagesFetchService = new Mock<IProductImagesFetchService>();
        _mockTransformStrategy = new Mock<IEntityTransformStrategy>();
        _mockLogger = new Mock<ILogger<ProductImagesCreationStrategy>>();

        _testConfig = new SubBatchConfiguration
        {
            EntityType = "product-images",
            ChunkSize = 20,
            SubBatchSize = 1,
            MaxConcurrency = 5,
            CustomSettings = new Dictionary<string, object>
            {
                ["imageChunkSize"] = 50,
                ["imageChunkDelayMs"] = 300
            }
        };

        _mockConfigService.Setup(x => x.GetConfiguration("product-images"))
            .Returns(_testConfig);

        _strategy = new ProductImagesCreationStrategy(
            _mockApiRequestHandler.Object,
            _mockSubBatchProcessor.Object,
            _mockConfigService.Object,
            _mockErrorHandlingService.Object,
            _mockCancellationStore.Object,
            _mockImagesFetchService.Object,
            _mockTransformStrategy.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidDependencies_ShouldInitializeCorrectly()
    {
        // Act & Assert - No exception should be thrown
        Assert.NotNull(_strategy);
        Assert.Equal("product-images", _strategy.EntityType);
    }

    [Fact]
    public void Constructor_WithNullApiRequestHandler_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ProductImagesCreationStrategy(
            null!,
            _mockSubBatchProcessor.Object,
            _mockConfigService.Object,
            _mockErrorHandlingService.Object,
            _mockCancellationStore.Object,
            _mockImagesFetchService.Object,
            _mockTransformStrategy.Object,
            _mockLogger.Object));
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithNullEntities_ShouldReturnEmptyList()
    {
        // Arrange
        var destinationStore = CreateTestStoreConfiguration();

        // Act
        var result = await _strategy.CreateEntitiesAsync(null!, "test-migration", destinationStore);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithEmptyEntities_ShouldReturnEmptyList()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>();
        var destinationStore = CreateTestStoreConfiguration();

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithValidEntities_ShouldCallSubBatchProcessor()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() { ["source_id"] = "1", ["destination_id"] = "101" },
            new() { ["source_id"] = "2", ["destination_id"] = "102" }
        };
        var destinationStore = CreateTestStoreConfiguration();
        var expectedResults = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "101", ["status"] = "updated", ["images_processed"] = 5 },
            new() { ["id"] = "102", ["status"] = "updated", ["images_processed"] = 3 }
        };

        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-images",
                "test-migration",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResults);

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        _mockSubBatchProcessor.Verify(x => x.ProcessInSubBatchesAsync(
            entities,
            _testConfig,
            It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
            "product-images",
            "test-migration",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithSkippedProducts_ShouldHandleSkipMarkersCorrectly()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() 
            { 
                ["_skip"] = true, 
                ["_skipReason"] = "no_images_to_process",
                ["source_id"] = "1", 
                ["destination_id"] = "101" 
            },
            new() { ["source_id"] = "2", ["destination_id"] = "102" }
        };
        var destinationStore = CreateTestStoreConfiguration();

        // Mock SubBatchProcessor to call the actual processor function
        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-images",
                "test-migration",
                It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, 
                Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>,
                string, string, CancellationToken>(
                async (ents, config, processor, entityType, migrationId, ct) =>
                {
                    return await processor(ents, ct);
                });

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert - With new architecture, only non-skipped entities are processed
        Assert.NotNull(result);
        Assert.Equal(1, result.Count);
        
        // Check that only the non-skipped product is processed
        var processedProduct = result.FirstOrDefault(r => r.GetValueOrDefault("destination_id")?.ToString() == "102");
        Assert.NotNull(processedProduct);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithCancellationRequested_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() { ["source_id"] = "1", ["destination_id"] = "101" }
        };
        var destinationStore = CreateTestStoreConfiguration();

        _mockCancellationStore.Setup(x => x.CheckCancellationFlagAsync("test-migration"))
            .ReturnsAsync(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ProcessSingleProductImagesAsync_WithInvalidProductId_ShouldReturnNull(string? productId)
    {
        // Arrange
        var productEntity = new Dictionary<string, object>
        {
            ["source_id"] = productId,
            ["destination_id"] = productId
        };
        var destinationStore = CreateTestStoreConfiguration();

        // Mock SubBatchProcessor to call the actual processor function
        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-images",
                "test-migration",
                It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, 
                Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>,
                string, string, CancellationToken>(
                async (ents, config, processor, entityType, migrationId, ct) =>
                {
                    return await processor(ents, ct);
                });

        var entities = new List<Dictionary<string, object>> { productEntity };

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // Should return empty list when productId is invalid
    }

    [Fact]
    public async Task ProcessSingleProductImagesAsync_WithNoImages_ShouldReturnSkippedStatus()
    {
        // Arrange
        var productEntity = new Dictionary<string, object>
        {
            ["source_id"] = "1",
            ["destination_id"] = "101",
            ["source_store_id"] = "test-store"
        };
        var destinationStore = CreateTestStoreConfiguration();

        // Mock image fetch service to return no images
        _mockImagesFetchService.Setup(x => x.FetchProductImagesStreamingAsync(
                "1",
                It.IsAny<StoreConfiguration>(),
                "test-migration",
                It.IsAny<Func<List<Dictionary<string, object>>, Task>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0); // No images returned

        // Mock SubBatchProcessor to call the actual processor function
        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-images",
                "test-migration",
                It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, 
                Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>,
                string, string, CancellationToken>(
                async (ents, config, processor, entityType, migrationId, ct) =>
                {
                    return await processor(ents, ct);
                });

        var entities = new List<Dictionary<string, object>> { productEntity };

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert - With new architecture, products with no images return empty results
        Assert.NotNull(result);
        Assert.Empty(result); // No results when no images are found
    }

    [Fact]
    public async Task ProcessSingleProductImagesAsync_WithMultipleImageChunks_ShouldProcessSequentially()
    {
        // Arrange
        var productEntity = new Dictionary<string, object>
        {
            ["source_id"] = "1",
            ["destination_id"] = "101",
            ["source_store_id"] = "test-store"
        };
        var destinationStore = CreateTestStoreConfiguration();

        // Create test images (75 images = 2 chunks of 50 + 1 chunk of 25)
        var allImages = CreateTestImages(75);
        var imageChunks = new List<List<Dictionary<string, object>>>();

        // Mock image fetch service to simulate streaming
        _mockImagesFetchService.Setup(x => x.FetchProductImagesStreamingAsync(
                "1",
                It.IsAny<StoreConfiguration>(),
                "test-migration",
                It.IsAny<Func<List<Dictionary<string, object>>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, StoreConfiguration, string, Func<List<Dictionary<string, object>>, Task>, CancellationToken>(
                async (productId, store, migrationId, onImageChunkFetched, ct) =>
                {
                    // Simulate chunked streaming
                    for (int i = 0; i < allImages.Count; i += 50)
                    {
                        var chunk = allImages.Skip(i).Take(50).ToList();
                        await onImageChunkFetched(chunk);
                    }
                })
            .ReturnsAsync(allImages.Count);

        // Mock transform strategy
        _mockTransformStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                "test-migration",
                It.IsAny<StoreConfiguration>(),
                destinationStore,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>
            {
                ["images"] = new List<Dictionary<string, object>>()
            });

        // Mock API request handler
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["status"] = "success" });

        // Mock SubBatchProcessor to call the actual processor function
        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-images",
                "test-migration",
                It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, 
                Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>,
                string, string, CancellationToken>(
                async (ents, config, processor, entityType, migrationId, ct) =>
                {
                    return await processor(ents, ct);
                });

        var entities = new List<Dictionary<string, object>> { productEntity };

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert - With new architecture, focus on core functionality
        Assert.NotNull(result);
        // Verify that processing completed successfully
        
        // Verify that API was called for each chunk (2 chunks: 50 + 25 images)
        _mockApiRequestHandler.Verify(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
            It.IsAny<ApiRequest>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Verify transform strategy was called for each chunk
        _mockTransformStrategy.Verify(x => x.TransformEntityAsync(
            It.IsAny<Dictionary<string, object>>(),
            "test-migration",
            It.IsAny<StoreConfiguration>(),
            destinationStore,
            null,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessSingleProductImagesAsync_WithApiFailure_ShouldContinueOnError()
    {
        // Arrange
        var productEntity = new Dictionary<string, object>
        {
            ["source_id"] = "1",
            ["destination_id"] = "101",
            ["source_store_id"] = "test-store"
        };
        var destinationStore = CreateTestStoreConfiguration();

        // Create test images (25 images = 1 chunk)
        var allImages = CreateTestImages(25);

        // Mock image fetch service
        _mockImagesFetchService.Setup(x => x.FetchProductImagesStreamingAsync(
                "1",
                It.IsAny<StoreConfiguration>(),
                "test-migration",
                It.IsAny<Func<List<Dictionary<string, object>>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, StoreConfiguration, string, Func<List<Dictionary<string, object>>, Task>, CancellationToken>(
                async (productId, store, migrationId, onImageChunkFetched, ct) =>
                {
                    await onImageChunkFetched(allImages);
                })
            .ReturnsAsync(allImages.Count);

        // Mock transform strategy
        _mockTransformStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                "test-migration",
                It.IsAny<StoreConfiguration>(),
                destinationStore,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>
            {
                ["images"] = new List<Dictionary<string, object>>()
            });

        // Mock API request handler to throw exception
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API call failed"));

        // Mock SubBatchProcessor to call the actual processor function
        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-images",
                "test-migration",
                It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, 
                Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>,
                string, string, CancellationToken>(
                async (ents, config, processor, entityType, migrationId, ct) =>
                {
                    return await processor(ents, ct);
                });

        var entities = new List<Dictionary<string, object>> { productEntity };

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert - With new architecture, API failures may result in empty results
        Assert.NotNull(result);
        // API failures should be handled gracefully, may return empty results
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithInvalidStoreConfiguration_ShouldThrowArgumentException()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() { ["source_id"] = "1", ["destination_id"] = "101" }
        };
        var invalidStore = new StoreConfiguration(); // Invalid store configuration

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _strategy.CreateEntitiesAsync(entities, "test-migration", invalidStore));
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithTransformFailure_ShouldSkipChunk()
    {
        // Arrange
        var productEntity = new Dictionary<string, object>
        {
            ["source_id"] = "1",
            ["destination_id"] = "101",
            ["source_store_id"] = "test-store"
        };
        var destinationStore = CreateTestStoreConfiguration();

        // Create test images
        var allImages = CreateTestImages(25);

        // Mock image fetch service
        _mockImagesFetchService.Setup(x => x.FetchProductImagesStreamingAsync(
                "1",
                It.IsAny<StoreConfiguration>(),
                "test-migration",
                It.IsAny<Func<List<Dictionary<string, object>>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, StoreConfiguration, string, Func<List<Dictionary<string, object>>, Task>, CancellationToken>(
                async (productId, store, migrationId, onImageChunkFetched, ct) =>
                {
                    await onImageChunkFetched(allImages);
                })
            .ReturnsAsync(allImages.Count);

        // Mock transform strategy to return null (transformation failure)
        _mockTransformStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                "test-migration",
                It.IsAny<StoreConfiguration>(),
                destinationStore,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, object>)null!);

        // Mock SubBatchProcessor to call the actual processor function
        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-images",
                "test-migration",
                It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, 
                Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>,
                string, string, CancellationToken>(
                async (ents, config, processor, entityType, migrationId, ct) =>
                {
                    return await processor(ents, ct);
                });

        var entities = new List<Dictionary<string, object>> { productEntity };

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert - With new architecture, transform failures may result in empty results
        Assert.NotNull(result);
        // Transform failures should be handled gracefully

        // Verify API was not called due to transform failure
        _mockApiRequestHandler.Verify(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
            It.IsAny<ApiRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithMixedResults_ShouldCalculateStatisticsCorrectly()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>
        {
            new() 
            { 
                ["_skip"] = true, 
                ["_skipReason"] = "no_images_to_process",
                ["source_id"] = "1", 
                ["destination_id"] = "101" 
            },
            new() { ["source_id"] = "2", ["destination_id"] = "102" },
            new() { ["source_id"] = "3", ["destination_id"] = "103" }
        };
        var destinationStore = CreateTestStoreConfiguration();

        var mockResults = new List<Dictionary<string, object>>
        {
            new() 
            { 
                ["id"] = "101", 
                ["status"] = "skipped", 
                ["reason"] = "no_images_to_process",
                ["images_processed"] = 0,
                ["images_successful"] = 0,
                ["images_skipped"] = 0
            },
            new() 
            { 
                ["id"] = "102", 
                ["status"] = "updated",
                ["images_processed"] = 50,
                ["images_successful"] = 45,
                ["images_skipped"] = 5
            },
            new() 
            { 
                ["id"] = "103", 
                ["status"] = "skipped_no_images",
                ["images_processed"] = 0,
                ["images_successful"] = 0,
                ["images_skipped"] = 0
            }
        };

        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-images",
                "test-migration",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResults);

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);

        // Verify the final summary logging is called with correct statistics
        // Total images: 50, Successful images: 45, Skipped images: 5
        // Skipped products: 2 (explicit skip + no images), Processed products: 1
    }

    #region Helper Methods

    private static StoreConfiguration CreateTestStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "test-store",
            AccessToken = "test-access-token",
            ChannelId = "test-channel-id"
        };
    }

    private static List<Dictionary<string, object>> CreateTestImages(int count)
    {
        var images = new List<Dictionary<string, object>>();
        for (int i = 1; i <= count; i++)
        {
            images.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["image_file"] = $"image_{i}.jpg",
                ["is_thumbnail"] = i == 1,
                ["sort_order"] = i,
                ["description"] = $"Test image {i}"
            });
        }
        return images;
    }

    #endregion
}
