using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Services.EntityCreation;
using BigCommerce.Migration.Activities.Services;

namespace BigCommerce.Migration.Tests.Services;

/// <summary>
/// Unit tests for VariantCreationStrategy focusing on the optimized cached lookup architecture
/// Tests verify the performance-optimized single-call approach for variant option mapping
/// </summary>
public class VariantCreationStrategyTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ISubBatchProcessor> _mockSubBatchProcessor;
    private readonly Mock<ISubBatchConfigurationService> _mockConfigService;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<ILogger<VariantCreationStrategy>> _mockLogger;
    private readonly VariantCreationStrategy _strategy;

    public VariantCreationStrategyTests()
    {
        _mockApiRequestHandler = new Mock<IApiRequestHandler>();
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockSubBatchProcessor = new Mock<ISubBatchProcessor>();
        _mockConfigService = new Mock<ISubBatchConfigurationService>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _mockLogger = new Mock<ILogger<VariantCreationStrategy>>();

        _strategy = new VariantCreationStrategy(
            _mockApiRequestHandler.Object,
            _mockStorageService.Object,
            _mockSubBatchProcessor.Object,
            _mockConfigService.Object,
            _mockErrorHandlingService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithValidProductMapping_UsesOptimizedSingleLookup()
    {
        // Arrange
        var migrationId = "test-migration-123";
        var sourceProductId = "15952";
        var destinationStore = new StoreConfiguration { StoreId = "dest-store" };
        
        var optionsMappingData = CreateValidOptionsMappingJson();
        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "99999",
            OptionsMappingData = optionsMappingData
        };

        var variants = new List<Dictionary<string, object>>
        {
            new()
            {
                ["id"] = "variant-1",
                ["product_id"] = sourceProductId,
                ["option_values"] = new List<Dictionary<string, object>>
                {
                    new() { ["option_id"] = "1876", ["id"] = "3726" }
                }
            }
        };

        // Setup single optimized call to GetEntityMappingAsync (not GetEntityMappingsAsync)
        _mockStorageService
            .Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(It.IsAny<List<Dictionary<string, object>>>(), It.IsAny<SubBatchConfiguration>(), It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>, string, string, CancellationToken>(
                async (entities, config, processor, entityType, migrationId, ct) =>
                {
                    // Execute the actual processor function to trigger the optimized lookup
                    return await processor(entities, ct);
                });

        // Act
        var result = await _strategy.CreateEntitiesAsync(variants, migrationId, destinationStore);

        // Assert
        Assert.NotNull(result);
        
        // Verify OPTIMIZED single lookup call (not multiple calls)
        _mockStorageService.Verify(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId), Times.Once);
        
        // Verify DEPRECATED methods are NOT called
        _mockStorageService.Verify(x => x.GetEntityMappingsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        
        // Verify cache clear logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("CACHE-CLEAR")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithMultipleVariantsSameProduct_UsesCaching()
    {
        // Arrange
        var migrationId = "test-migration-456";
        var sourceProductId = "15952";
        var destinationStore = new StoreConfiguration { StoreId = "dest-store" };
        
        var optionsMappingData = CreateValidOptionsMappingJson();
        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products", 
            SourceId = sourceProductId,
            DestinationId = "99999",
            OptionsMappingData = optionsMappingData
        };

        // Create multiple variants for the same product
        var variants = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "variant-1", ["product_id"] = sourceProductId, ["option_values"] = new List<Dictionary<string, object>>() },
            new() { ["id"] = "variant-2", ["product_id"] = sourceProductId, ["option_values"] = new List<Dictionary<string, object>>() },
            new() { ["id"] = "variant-3", ["product_id"] = sourceProductId, ["option_values"] = new List<Dictionary<string, object>>() }
        };

        _mockStorageService
            .Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(It.IsAny<List<Dictionary<string, object>>>(), It.IsAny<SubBatchConfiguration>(), It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>, string, string, CancellationToken>(
                async (entities, config, processor, entityType, migrationId, ct) =>
                {
                    // Execute the actual processor function to trigger the optimized lookup
                    return await processor(entities, ct);
                });

        // Act
        var result = await _strategy.CreateEntitiesAsync(variants, migrationId, destinationStore);

        // Assert - CRITICAL: Only ONE database call despite multiple variants
        _mockStorageService.Verify(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId), Times.Once);
        
        // Verify cache usage logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("CACHE-HIT") || v.ToString()!.Contains("CACHE-MISS")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithMissingProductMapping_ThrowsInvalidOperationException()
    {
        // Arrange
        var migrationId = "test-migration-789";
        var sourceProductId = "nonexistent-product";
        var destinationStore = new StoreConfiguration { StoreId = "dest-store" };
        
        var variants = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "variant-1", ["product_id"] = sourceProductId }
        };

        // Setup no mapping found
        _mockStorageService
            .Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync((EntityMapping?)null);

        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(It.IsAny<List<Dictionary<string, object>>>(), It.IsAny<SubBatchConfiguration>(), It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>, string, string, CancellationToken>(
                async (entities, config, processor, entityType, migrationId, ct) =>
                {
                    // Execute the actual processor function which should throw the exception
                    return await processor(entities, ct);
                });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _strategy.CreateEntitiesAsync(variants, migrationId, destinationStore));
        
        Assert.Contains($"Product mapping not found for source ID: {sourceProductId}", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Ensure Phase 1 (Products) completed successfully", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithEmptyVariantList_ReturnsEmptyList()
    {
        // Arrange
        var migrationId = "test-migration-empty";
        var destinationStore = new StoreConfiguration { StoreId = "dest-store" };
        var emptyVariants = new List<Dictionary<string, object>>();

        // Act
        var result = await _strategy.CreateEntitiesAsync(emptyVariants, migrationId, destinationStore);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
        
        // Verify no database calls made for empty list
        _mockStorageService.Verify(x => x.GetEntityMappingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithComplexOptionMapping_ParsesCorrectly()
    {
        // Arrange
        var migrationId = "test-migration-complex";
        var sourceProductId = "15952";
        var destinationStore = new StoreConfiguration { StoreId = "dest-store" };
        
        var complexOptionsMappingData = @"{
            ""options"": [
                {
                    ""sourceOptionId"": ""1876"",
                    ""destinationOptionId"": ""2139"",
                    ""optionValues"": [
                        {""sourceId"": ""3726"", ""destinationId"": ""15340""},
                        {""sourceId"": ""3727"", ""destinationId"": ""15341""},
                        {""sourceId"": ""3728"", ""destinationId"": ""15342""}
                    ]
                },
                {
                    ""sourceOptionId"": ""1877"",
                    ""destinationOptionId"": ""2140"",
                    ""optionValues"": [
                        {""sourceId"": ""3729"", ""destinationId"": ""15343""},
                        {""sourceId"": ""3730"", ""destinationId"": ""15344""}
                    ]
                }
            ]
        }";

        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "99999",
            OptionsMappingData = complexOptionsMappingData
        };

        var variants = new List<Dictionary<string, object>>
        {
            new()
            {
                ["id"] = "variant-complex",
                ["product_id"] = sourceProductId,
                ["option_values"] = new List<Dictionary<string, object>>
                {
                    new() { ["option_id"] = "1876", ["id"] = "3726" },
                    new() { ["option_id"] = "1877", ["id"] = "3729" }
                }
            }
        };

        _mockStorageService
            .Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(It.IsAny<List<Dictionary<string, object>>>(), It.IsAny<SubBatchConfiguration>(), It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>, string, string, CancellationToken>(
                async (entities, config, processor, entityType, migrationId, ct) =>
                {
                    // Execute the actual processor function to trigger the optimized lookup
                    return await processor(entities, ct);
                });

        // Act
        var result = await _strategy.CreateEntitiesAsync(variants, migrationId, destinationStore);

        // Assert
        Assert.NotNull(result);
        _mockStorageService.Verify(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId), Times.Once);
    }

    [Fact]
    public async Task CreateEntitiesAsync_PerformanceOptimization_LogsOptimizedMessage()
    {
        // Arrange
        var migrationId = "test-migration-perf";
        var sourceProductId = "15952";
        var destinationStore = new StoreConfiguration { StoreId = "dest-store" };
        
        var variants = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "variant-1", ["product_id"] = sourceProductId }
        };

        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "99999",
            OptionsMappingData = CreateValidOptionsMappingJson()
        };

        _mockStorageService
            .Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(It.IsAny<List<Dictionary<string, object>>>(), It.IsAny<SubBatchConfiguration>(), It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>, string, string, CancellationToken>(
                async (entities, config, processor, entityType, migrationId, ct) =>
                {
                    // Execute the actual processor function to trigger the optimized lookup
                    return await processor(entities, ct);
                });

        // Act
        await _strategy.CreateEntitiesAsync(variants, migrationId, destinationStore);

        // Assert - Verify performance optimization logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("optimized cached lookups")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Creates valid options mapping JSON data for testing
    /// Uses the same format expected by the optimized VariantCreationStrategy
    /// </summary>
    private static string CreateValidOptionsMappingJson()
    {
        return @"{
            ""options"": [
                {
                    ""sourceOptionId"": ""1876"",
                    ""destinationOptionId"": ""2139"",
                    ""optionValues"": [
                        {
                            ""sourceId"": ""3726"",
                            ""sourcePosition"": 0,
                            ""sourceLabel"": ""Small"",
                            ""destinationId"": ""15340"",
                            ""destinationLabel"": ""Small""
                        },
                        {
                            ""sourceId"": ""3727"",
                            ""sourcePosition"": 1,
                            ""sourceLabel"": ""Medium"",
                            ""destinationId"": ""15341"",
                            ""destinationLabel"": ""Medium""
                        },
                        {
                            ""sourceId"": ""3728"",
                            ""sourcePosition"": 2,
                            ""sourceLabel"": ""Large"",
                            ""destinationId"": ""15342"",
                            ""destinationLabel"": ""Large""
                        }
                    ]
                }
            ]
        }";
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithProductSkuVariant_SkipsDuplicateVariant()
    {
        // Arrange
        var migrationId = "test-migration-sku-filter";
        var sourceProductId = "15952";
        var destinationStore = new StoreConfiguration { StoreId = "dest-store" };
        var productSku = "SHAWL-A";
        
        // Create metadata with product SKU (as stored in Phase 1)
        var productMetadata = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Sku"] = productSku
        });
        
        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "99999",
            Metadata = productMetadata, // Contains the product SKU
            OptionsMappingData = CreateValidOptionsMappingJson()
        };

        var variants = new List<Dictionary<string, object>>
        {
            // This variant has the same SKU as the product - should be skipped
            new()
            {
                ["id"] = "variant-1",
                ["product_id"] = sourceProductId,
                ["sku"] = productSku, // Same as product SKU - should be filtered out
                ["option_values"] = new List<Dictionary<string, object>>
                {
                    new() { ["option_id"] = "1876", ["id"] = "3726" }
                }
            },
            // This variant has a different SKU - should be processed
            new()
            {
                ["id"] = "variant-2", 
                ["product_id"] = sourceProductId,
                ["sku"] = "SHAWL-B", // Different SKU - should be included
                ["option_values"] = new List<Dictionary<string, object>>
                {
                    new() { ["option_id"] = "1876", ["id"] = "3727" }
                }
            }
        };

        // Setup mocks
        _mockStorageService
            .Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        var processedVariants = new List<Dictionary<string, object>>();
        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(It.IsAny<List<Dictionary<string, object>>>(), It.IsAny<SubBatchConfiguration>(), It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>, string, string, CancellationToken>(
                async (entities, config, processor, entityType, migrationId, ct) =>
                {
                    // Execute the actual processor function and capture what gets processed
                    var result = await processor(entities, ct);
                    processedVariants.AddRange(result);
                    return result;
                });

        // Act
        var result = await _strategy.CreateEntitiesAsync(variants, migrationId, destinationStore);

        // Assert
        Assert.NotNull(result);
        
        // Verify that only the variant with different SKU was processed (1 variant instead of 2)
        Assert.Single(processedVariants);
        
        // Verify the processed variant is the one with different SKU
        var processedVariant = processedVariants.First();
        Assert.Equal("SHAWL-B", processedVariant["sku"].ToString());
        
        // Verify the duplicate SKU variant was skipped with appropriate logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"DUPLICATE-SKIP") && 
                                              v.ToString()!.Contains(productSku) && 
                                              v.ToString()!.Contains("matches product default SKU")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
            
        // Verify SKU extraction was logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SKU-EXTRACT") && 
                                              v.ToString()!.Contains(productSku)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithCaseInsensitiveSkuMatch_SkipsDuplicateVariant()
    {
        // Arrange - Test case-insensitive SKU matching
        var migrationId = "test-migration-case-insensitive";
        var sourceProductId = "15952";
        var destinationStore = new StoreConfiguration { StoreId = "dest-store" };
        var productSku = "SHAWL-A"; // Product SKU in uppercase
        
        var productMetadata = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Sku"] = productSku
        });
        
        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products", 
            SourceId = sourceProductId,
            DestinationId = "99999",
            Metadata = productMetadata,
            OptionsMappingData = CreateValidOptionsMappingJson()
        };

        var variants = new List<Dictionary<string, object>>
        {
            // Variant with lowercase SKU - should still be skipped due to case-insensitive match
            new()
            {
                ["id"] = "variant-1",
                ["product_id"] = sourceProductId,
                ["sku"] = "shawl-a", // Same SKU but different case - should be filtered out
                ["option_values"] = new List<Dictionary<string, object>>()
            }
        };

        // Setup mocks
        _mockStorageService
            .Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        var processedVariants = new List<Dictionary<string, object>>();
        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(It.IsAny<List<Dictionary<string, object>>>(), It.IsAny<SubBatchConfiguration>(), It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>, string, string, CancellationToken>(
                async (entities, config, processor, entityType, migrationId, ct) =>
                {
                    var result = await processor(entities, ct);
                    processedVariants.AddRange(result);
                    return result;
                });

        // Act
        var result = await _strategy.CreateEntitiesAsync(variants, migrationId, destinationStore);

        // Assert
        Assert.NotNull(result);
        
        // Verify no variants were processed (SKU match was case-insensitive)
        Assert.Empty(processedVariants);
        
        // Verify the duplicate SKU variant was skipped
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DUPLICATE-SKIP") && 
                                              v.ToString()!.Contains("shawl-a")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithNoProductMetadata_ProcessesAllVariants()
    {
        // Arrange - Test when product has no metadata (no SKU to filter against)
        var migrationId = "test-migration-no-metadata";
        var sourceProductId = "15952";
        var destinationStore = new StoreConfiguration { StoreId = "dest-store" };
        
        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "99999",
            Metadata = null, // No metadata, so no SKU filtering should occur
            OptionsMappingData = CreateValidOptionsMappingJson()
        };

        var variants = new List<Dictionary<string, object>>
        {
            new()
            {
                ["id"] = "variant-1",
                ["product_id"] = sourceProductId,
                ["sku"] = "ANY-SKU",
                ["option_values"] = new List<Dictionary<string, object>>
                {
                    new() { ["option_id"] = "1876", ["id"] = "3726" }
                }
            }
        };

        // Setup mocks
        _mockStorageService
            .Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        var processedVariants = new List<Dictionary<string, object>>();
        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(It.IsAny<List<Dictionary<string, object>>>(), It.IsAny<SubBatchConfiguration>(), It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>, string, string, CancellationToken>(
                async (entities, config, processor, entityType, migrationId, ct) =>
                {
                    var result = await processor(entities, ct);
                    processedVariants.AddRange(result);
                    return result;
                });

        // Act
        var result = await _strategy.CreateEntitiesAsync(variants, migrationId, destinationStore);

        // Assert
        Assert.NotNull(result);
        
        // Verify the variant was processed (no SKU filtering occurred)
        Assert.Single(processedVariants);
        
        // Verify no duplicate-skip logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DUPLICATE-SKIP")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}