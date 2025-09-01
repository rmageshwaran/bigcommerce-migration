using BigCommerce.Migration.Activities.Services.EntityCreation;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Tests.Creation;

/// <summary>
/// xUnit unit tests for ProductMetafieldsCreationStrategy
/// Tests sub-batch processing, product ID mapping, IApiRequestHandler integration, and error handling
/// Follows TDD approach with comprehensive coverage of complex creation scenarios
/// </summary>
public class ProductMetafieldsCreationStrategyTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<IMigrationStorageService> _mockMigrationStorageService;
    private readonly Mock<ISubBatchProcessor> _mockSubBatchProcessor;
    private readonly Mock<ISubBatchConfigurationService> _mockConfigService;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<ILogger<ProductMetafieldsCreationStrategy>> _mockLogger;
    private readonly ProductMetafieldsCreationStrategy _strategy;

    public ProductMetafieldsCreationStrategyTests()
    {
        _mockApiRequestHandler = new Mock<IApiRequestHandler>();
        _mockMigrationStorageService = new Mock<IMigrationStorageService>();
        _mockSubBatchProcessor = new Mock<ISubBatchProcessor>();
        _mockConfigService = new Mock<ISubBatchConfigurationService>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _mockLogger = new Mock<ILogger<ProductMetafieldsCreationStrategy>>();
        
        _strategy = new ProductMetafieldsCreationStrategy(
            _mockApiRequestHandler.Object,
            _mockMigrationStorageService.Object,
            _mockSubBatchProcessor.Object,
            _mockConfigService.Object,
            _mockErrorHandlingService.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Test: Valid metafields should be created successfully with product ID mapping
    /// </summary>
    [Fact]
    public async Task CreateEntitiesAsync_ValidMetafields_CreatesSuccessfully()
    {
        // Arrange
        var sourceMetafields = CreateValidSourceMetafields(3);
        var migrationId = "test-migration-001";
        var destinationStore = CreateValidStoreConfiguration();
        var config = CreateValidSubBatchConfiguration();
        var expectedResults = CreateMockCreationResults(3);

        // Setup mocks
        _mockConfigService
            .Setup(x => x.GetConfiguration("product-metafields"))
            .Returns(config);

        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-metafields",
                migrationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResults);

        // Act
        var result = await _strategy.CreateEntitiesAsync(
            sourceMetafields, migrationId, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("product-metafields", _strategy.EntityType);
        
        // Verify sub-batch processor was called with correct parameters
        _mockSubBatchProcessor.Verify(x => x.ProcessInSubBatchesAsync(
            It.Is<List<Dictionary<string, object>>>(entities => entities.Count == 3),
            config,
            It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
            "product-metafields",
            migrationId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Test: Empty metafields list should return empty result without errors
    /// </summary>
    [Fact]
    public async Task CreateEntitiesAsync_EmptyList_ReturnsEmptyResult()
    {
        // Arrange
        var emptyMetafields = new List<Dictionary<string, object>>();
        var migrationId = "test-migration-002";
        var destinationStore = CreateValidStoreConfiguration();

        // Act
        var result = await _strategy.CreateEntitiesAsync(
            emptyMetafields, migrationId, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
        
        // Verify no sub-batch processing was called
        _mockSubBatchProcessor.Verify(x => x.ProcessInSubBatchesAsync(
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<SubBatchConfiguration>(),
            It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Test: Infrastructure error should stop migration (throw exception)
    /// </summary>
    [Fact]
    public async Task CreateEntitiesAsync_InfrastructureError_ThrowsException()
    {
        // Arrange
        var sourceMetafields = CreateValidSourceMetafields(2);
        var migrationId = "test-migration-003";
        var destinationStore = CreateValidStoreConfiguration();
        var config = CreateValidSubBatchConfiguration();
        var infrastructureException = new System.Net.Http.HttpRequestException("Network failure");

        _mockConfigService
            .Setup(x => x.GetConfiguration("product-metafields"))
            .Returns(config);

        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-metafields",
                migrationId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(infrastructureException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(
            () => _strategy.CreateEntitiesAsync(sourceMetafields, migrationId, destinationStore, null, CancellationToken.None));
        
        Assert.Equal("Network failure", exception.Message);
    }

    /// <summary>
    /// Test: Application error should continue on error (return empty list)
    /// </summary>
    [Fact]
    public async Task CreateEntitiesAsync_ApplicationError_ContinuesOnError()
    {
        // Arrange
        var sourceMetafields = CreateValidSourceMetafields(2);
        var migrationId = "test-migration-004";
        var destinationStore = CreateValidStoreConfiguration();
        var config = CreateValidSubBatchConfiguration();
        var applicationException = new Exception("BigCommerce API validation error");

        _mockConfigService
            .Setup(x => x.GetConfiguration("product-metafields"))
            .Returns(config);

        _mockSubBatchProcessor
            .Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "product-metafields",
                migrationId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(applicationException);

        // Act
        var result = await _strategy.CreateEntitiesAsync(
            sourceMetafields, migrationId, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // Continue-on-error returns empty list
        
        // Verify error was logged to OpenSearch
        _mockErrorHandlingService.Verify(x => x.LogStructuredMigrationErrorAsync(
            applicationException,
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<BatchProcessingRequest>(),
            "migration-level",
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Test: Invalid store configuration should throw ArgumentException
    /// </summary>
    [Fact]
    public async Task CreateEntitiesAsync_InvalidStoreConfiguration_ThrowsArgumentException()
    {
        // Arrange
        var sourceMetafields = CreateValidSourceMetafields(1);
        var migrationId = "test-migration-005";
        var invalidStore = new StoreConfiguration(); // Invalid - missing required fields

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _strategy.CreateEntitiesAsync(sourceMetafields, migrationId, invalidStore, null, CancellationToken.None));
    }

    /// <summary>
    /// Test: Entity type should return correct value
    /// </summary>
    [Fact]
    public void EntityType_ReturnsCorrectValue()
    {
        // Act & Assert
        Assert.Equal("product-metafields", _strategy.EntityType);
    }

    #region Helper Methods

    /// <summary>
    /// Creates valid source metafields for testing
    /// </summary>
    private List<Dictionary<string, object>> CreateValidSourceMetafields(int count)
    {
        var metafields = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= count; i++)
        {
            metafields.Add(new Dictionary<string, object>
            {
                ["id"] = 100 + i,
                ["resource_id"] = 40 + i, // Source product IDs
                ["key"] = $"Test Key {i}",
                ["value"] = $"Test Value {i}",
                ["permission_set"] = "app_only",
                ["namespace"] = "Test Namespace",
                ["description"] = $"Test Description {i}",
                ["resource_type"] = "product",
                ["date_created"] = "2022-06-16T18:39:00+00:00",
                ["date_modified"] = "2022-06-16T18:39:00+00:00"
            });
        }
        
        return metafields;
    }

    /// <summary>
    /// Creates a valid store configuration for testing
    /// </summary>
    private StoreConfiguration CreateValidStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "test-store-123",
            AccessToken = "test-api-token",
            BaseUrl = "https://api.bigcommerce.com",
            ChannelId = "1"
        };
    }

    /// <summary>
    /// Creates valid sub-batch configuration for testing
    /// </summary>
    private SubBatchConfiguration CreateValidSubBatchConfiguration()
    {
        return new SubBatchConfiguration
        {
            EntityType = "product-metafields",
            ChunkSize = 250,
            FetchBatchSize = 250,
            PageSize = 250,
            SubBatchSize = 50,
            MaxConcurrency = 5,
            EnableSubBatching = true,
            SubBatchDelayMs = 100,
            ProcessSubBatchesSequentially = false
        };
    }

    /// <summary>
    /// Creates mock creation results for testing
    /// </summary>
    private List<Dictionary<string, object>> CreateMockCreationResults(int count)
    {
        var results = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= count; i++)
        {
            results.Add(new Dictionary<string, object>
            {
                ["id"] = 200 + i, // Destination metafield IDs
                ["resource_id"] = 80 + i, // Destination product IDs (mapped)
                ["key"] = $"Test Key {i}",
                ["value"] = $"Test Value {i}",
                ["permission_set"] = "app_only",
                ["namespace"] = "Test Namespace",
                ["description"] = $"Test Description {i}",
                ["date_created"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
                ["date_modified"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            });
        }
        
        return results;
    }

    #endregion
}
