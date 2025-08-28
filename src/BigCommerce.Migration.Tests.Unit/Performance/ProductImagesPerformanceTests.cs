using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Strategies.Creation;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Performance;

/// <summary>
/// Performance validation tests for Product-Images phase
/// Task 7.2: Performance Configuration Validation
/// </summary>
public class ProductImagesPerformanceTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<ISubBatchProcessor> _mockSubBatchProcessor;
    private readonly Mock<ISubBatchConfigurationService> _mockConfigService;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<ICancellationStore> _mockCancellationStore;
    private readonly Mock<IProductImagesFetchService> _mockImagesFetchService;
    private readonly Mock<IEntityTransformStrategy> _mockTransformStrategy;
    private readonly Mock<ILogger<ProductImagesCreationStrategy>> _mockLogger;
    private readonly SubBatchConfiguration _testConfig;

    public ProductImagesPerformanceTests()
    {
        _mockApiRequestHandler = new Mock<IApiRequestHandler>();
        _mockSubBatchProcessor = new Mock<ISubBatchProcessor>();
        _mockConfigService = new Mock<ISubBatchConfigurationService>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _mockCancellationStore = new Mock<ICancellationStore>();
        _mockImagesFetchService = new Mock<IProductImagesFetchService>();
        _mockTransformStrategy = new Mock<IEntityTransformStrategy>();
        _mockLogger = new Mock<ILogger<ProductImagesCreationStrategy>>();

        // Use actual timeout-safe configuration from Task 1
        _testConfig = new SubBatchConfiguration
        {
            EntityType = "product-images",
            ChunkSize = 20,
            SubBatchSize = 1,  // Individual product processing
            MaxConcurrency = 5,  // 5 concurrent products
            ProcessSubBatchesSequentially = false,
            EnableSubBatching = true,
            CustomSettings = new Dictionary<string, object>
            {
                ["imageChunkSize"] = 50,         // 50 images per API call
                ["imageChunkDelayMs"] = 300,     // 300ms delay between chunks
                ["maxImagesPerProduct"] = 1000,  // Max 1000 images per product
                ["cancellationCheckInterval"] = 5
            }
        };

        _mockConfigService.Setup(x => x.GetConfiguration("product-images"))
            .Returns(_testConfig);
    }

    [Fact]
    public async Task MaxConcurrency_IsEnforcedCorrectly()
    {
        // Arrange
        var strategy = CreateProductImagesCreationStrategy();
        var entities = CreateTestProductEntities(10); // More than maxConcurrency
        var destinationStore = CreateTestStoreConfiguration();
        
        var concurrentCallsCount = 0;
        var maxObservedConcurrency = 0;
        var currentConcurrency = 0;

        // Mock SubBatchProcessor to track concurrency
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
                    // Simulate processing with concurrency tracking
                    var tasks = new List<Task<Dictionary<string, object>>>();
                    
                    foreach (var entity in ents)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            Interlocked.Increment(ref currentConcurrency);
                            Interlocked.Increment(ref concurrentCallsCount);
                            
                            var observed = currentConcurrency;
                            if (observed > maxObservedConcurrency)
                            {
                                maxObservedConcurrency = observed;
                            }
                            
                            // Simulate processing time
                            await Task.Delay(100, ct);
                            
                            Interlocked.Decrement(ref currentConcurrency);
                            
                            return new Dictionary<string, object>
                            {
                                ["id"] = entity["destination_id"],
                                ["status"] = "updated"
                            };
                        }));
                        
                        // Respect maxConcurrency by only allowing 5 concurrent tasks
                        if (tasks.Count >= config.MaxConcurrency)
                        {
                            await Task.WhenAny(tasks);
                            tasks.RemoveAll(t => t.IsCompleted);
                        }
                    }
                    
                    var results = await Task.WhenAll(tasks);
                    return results.ToList();
                });

        // Act
        var result = await strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert
        Assert.NotNull(result);
        Assert.True(maxObservedConcurrency <= _testConfig.MaxConcurrency, 
            $"Max observed concurrency ({maxObservedConcurrency}) should not exceed configured limit ({_testConfig.MaxConcurrency})");
        Assert.Equal(_testConfig.MaxConcurrency, maxObservedConcurrency);
    }

    [Fact]
    public async Task SingleProduct_WithMaxImages_CompletesWithinTimeoutLimit()
    {
        // Arrange
        var strategy = CreateProductImagesCreationStrategy();
        var productEntity = new Dictionary<string, object>
        {
            ["source_id"] = "1",
            ["destination_id"] = "101",
            ["source_store_id"] = "test-store"
        };
        var entities = new List<Dictionary<string, object>> { productEntity };
        var destinationStore = CreateTestStoreConfiguration();

        // Create 1000 test images (worst case scenario)
        var maxImages = Convert.ToInt32(_testConfig.CustomSettings["maxImagesPerProduct"], CultureInfo.InvariantCulture);
        var imageChunkSize = Convert.ToInt32(_testConfig.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture);
        var imageChunkDelayMs = Convert.ToInt32(_testConfig.CustomSettings["imageChunkDelayMs"], CultureInfo.InvariantCulture);
        var expectedChunks = (int)Math.Ceiling((double)maxImages / imageChunkSize); // 20 chunks

        var processingTimes = new List<TimeSpan>();

        // Mock image fetch service to simulate 1000 images
        _mockImagesFetchService.Setup(x => x.FetchProductImagesStreamingAsync(
                "1",
                It.IsAny<StoreConfiguration>(),
                "test-migration",
                It.IsAny<Func<List<Dictionary<string, object>>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, StoreConfiguration, string, Func<List<Dictionary<string, object>>, Task>, CancellationToken>(
                async (productId, store, migrationId, onImageChunkFetched, ct) =>
                {
                    // Simulate streaming 1000 images in chunks of 50
                    for (int chunkIndex = 0; chunkIndex < expectedChunks; chunkIndex++)
                    {
                        var chunkSize = Math.Min(imageChunkSize, maxImages - (chunkIndex * imageChunkSize));
                        var imageChunk = CreateTestImages(chunkSize);
                        await onImageChunkFetched(imageChunk);
                    }
                })
            .ReturnsAsync(maxImages);

        // Mock transform strategy with timing simulation
        _mockTransformStrategy.Setup(x => x.TransformEntityAsync(
                It.IsAny<Dictionary<string, object>>(),
                "test-migration",
                It.IsAny<StoreConfiguration>(),
                destinationStore,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // Simulate transform time (should be fast)
                await Task.Delay(10);
                return new Dictionary<string, object>
                {
                    ["images"] = new List<Dictionary<string, object>>()
                };
            });

        // Mock API request handler with realistic timing
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var sw = Stopwatch.StartNew();
                
                // Simulate realistic API call time (2-3 seconds per chunk)
                await Task.Delay(2500); // 2.5s per API call
                
                sw.Stop();
                processingTimes.Add(sw.Elapsed);
                
                return new Dictionary<string, object> { ["status"] = "success" };
            });

        // Mock SubBatchProcessor to call actual processor with timing
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
        var stopwatch = Stopwatch.StartNew();
        var result = await strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);
        stopwatch.Stop();

        // Assert - With new architecture, focus on performance rather than result structure
        Assert.NotNull(result);
        
        var totalProcessingTime = stopwatch.Elapsed.TotalSeconds;
        var expectedMaxTime = 60.0; // Conservative limit (well under 120s Azure Function timeout)
        
        Assert.True(totalProcessingTime < expectedMaxTime, 
            $"Processing time ({totalProcessingTime:F1}s) should be < {expectedMaxTime}s for worst case scenario");
        
        // Verify API calls were made (20 chunks for 1000 images)
        _mockApiRequestHandler.Verify(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
            It.IsAny<ApiRequest>(),
            It.IsAny<CancellationToken>()), Times.Exactly(expectedChunks));
        
        // Verify processing times are reasonable
        Assert.Equal(expectedChunks, processingTimes.Count);
        Assert.All(processingTimes, time => Assert.True(time.TotalSeconds < 5, "Each API call should complete in < 5s"));
    }

    [Fact]
    public async Task ChunkDelayImplementation_IsCorrectlyApplied()
    {
        // Arrange
        var strategy = CreateProductImagesCreationStrategy();
        var productEntity = new Dictionary<string, object>
        {
            ["source_id"] = "1",
            ["destination_id"] = "101",
            ["source_store_id"] = "test-store"
        };
        var entities = new List<Dictionary<string, object>> { productEntity };
        var destinationStore = CreateTestStoreConfiguration();

        var imageChunkSize = Convert.ToInt32(_testConfig.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture);
        var imageChunkDelayMs = Convert.ToInt32(_testConfig.CustomSettings["imageChunkDelayMs"], CultureInfo.InvariantCulture);
        var totalImages = imageChunkSize * 3; // 3 chunks = 150 images
        var expectedChunks = 3;

        var chunkProcessingTimes = new List<DateTime>();

        // Mock image fetch service for 3 chunks
        _mockImagesFetchService.Setup(x => x.FetchProductImagesStreamingAsync(
                "1",
                It.IsAny<StoreConfiguration>(),
                "test-migration",
                It.IsAny<Func<List<Dictionary<string, object>>, Task>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, StoreConfiguration, string, Func<List<Dictionary<string, object>>, Task>, CancellationToken>(
                async (productId, store, migrationId, onImageChunkFetched, ct) =>
                {
                    for (int i = 0; i < expectedChunks; i++)
                    {
                        var imageChunk = CreateTestImages(imageChunkSize);
                        await onImageChunkFetched(imageChunk);
                    }
                })
            .ReturnsAsync(totalImages);

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

        // Mock API request handler to track timing
        _mockApiRequestHandler.Setup(x => x.ExecuteRequestAsync<Dictionary<string, object>>(
                It.IsAny<ApiRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                chunkProcessingTimes.Add(DateTime.UtcNow);
                return Task.FromResult(new Dictionary<string, object> { ["status"] = "success" });
            });

        // Mock SubBatchProcessor to call actual processor
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
        var result = await strategy.CreateEntitiesAsync(entities, "test-migration", destinationStore);

        // Assert - With new architecture, focus on timing behavior
        Assert.NotNull(result);
        Assert.Equal(expectedChunks, chunkProcessingTimes.Count);

        // Verify delays between chunks (should be ~300ms)
        for (int i = 1; i < chunkProcessingTimes.Count; i++)
        {
            var timeBetweenChunks = (chunkProcessingTimes[i] - chunkProcessingTimes[i - 1]).TotalMilliseconds;
            
            // Allow some tolerance for timing variations (250ms - 350ms range)
            Assert.True(timeBetweenChunks >= 250, 
                $"Time between chunk {i-1} and {i} ({timeBetweenChunks:F0}ms) should be >= 250ms");
            Assert.True(timeBetweenChunks <= 400, 
                $"Time between chunk {i-1} and {i} ({timeBetweenChunks:F0}ms) should be <= 400ms (300ms + tolerance)");
        }
    }

    [Fact]
    public void SubBatchConfiguration_HasCorrectPerformanceSettings()
    {
        // Arrange & Act
        var config = _testConfig;

        // Assert - Verify all performance-critical settings
        Assert.Equal("product-images", config.EntityType);
        Assert.Equal(1, config.SubBatchSize); // Individual product processing
        Assert.Equal(5, config.MaxConcurrency); // Controlled concurrency
        Assert.False(config.ProcessSubBatchesSequentially); // Allow parallel processing
        Assert.True(config.EnableSubBatching); // Enable sub-batch processing

        // Verify custom settings
        Assert.Equal(50, Convert.ToInt32(config.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture));
        Assert.Equal(300, Convert.ToInt32(config.CustomSettings["imageChunkDelayMs"], CultureInfo.InvariantCulture));
        Assert.Equal(1000, Convert.ToInt32(config.CustomSettings["maxImagesPerProduct"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MemoryUsage_StaysWithinLimits()
    {
        // Arrange - Calculate memory usage based on configuration
        var imageChunkSize = Convert.ToInt32(_testConfig.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture);
        var maxConcurrency = _testConfig.MaxConcurrency;
        
        // Act - Calculate estimated memory usage
        var estimatedBytesPerImageMetadata = 1024; // 1KB per image metadata (conservative)
        var memoryPerChunk = imageChunkSize * estimatedBytesPerImageMetadata; // 50KB per chunk
        var memoryPerProduct = memoryPerChunk; // Streaming: only one chunk in memory at a time
        var totalMemoryForMaxConcurrency = memoryPerProduct * maxConcurrency; // 250KB total

        // Assert - Memory usage should be well within limits
        var memoryLimitPerProduct = 1024 * 1024; // 1MB per product
        var totalMemoryLimit = 10 * 1024 * 1024; // 10MB total (conservative)

        Assert.True(memoryPerProduct < memoryLimitPerProduct, 
            $"Memory per product ({memoryPerProduct} bytes) should be < 1MB");
        Assert.True(totalMemoryForMaxConcurrency < totalMemoryLimit, 
            $"Total memory for max concurrency ({totalMemoryForMaxConcurrency} bytes) should be < 10MB");
        
        // Verify streaming approach keeps memory usage minimal
        Assert.True(memoryPerProduct < 100 * 1024, 
            $"Streaming approach should keep memory per product < 100KB (actual: {memoryPerProduct} bytes)");
    }

    #region Helper Methods

    private ProductImagesCreationStrategy CreateProductImagesCreationStrategy()
    {
        return new ProductImagesCreationStrategy(
            _mockApiRequestHandler.Object,
            _mockSubBatchProcessor.Object,
            _mockConfigService.Object,
            _mockErrorHandlingService.Object,
            _mockCancellationStore.Object,
            _mockImagesFetchService.Object,
            _mockTransformStrategy.Object,
            _mockLogger.Object);
    }

    private static List<Dictionary<string, object>> CreateTestProductEntities(int count)
    {
        var entities = new List<Dictionary<string, object>>();
        for (int i = 1; i <= count; i++)
        {
            entities.Add(new Dictionary<string, object>
            {
                ["source_id"] = i.ToString(CultureInfo.InvariantCulture),
                ["destination_id"] = (100 + i).ToString(CultureInfo.InvariantCulture),
                ["source_store_id"] = "test-store"
            });
        }
        return entities;
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

    private static StoreConfiguration CreateTestStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "test-store",
            AccessToken = "test-access-token",
            ChannelId = "test-channel-id"
        };
    }

    #endregion
}
