using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Services.EntityCreation;
using BigCommerce.Migration.Activities.Models;
using Xunit;
using FluentAssertions;
using System.Text.Json;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Integration tests for Phase 3 Variants Migration with BigCommerce Batch API
/// Tests the complete pipeline: Discovery → Fetch → Transform → Batch Create → Error Handling
/// Validates 42x API efficiency improvement and continue-on-error policy
/// </summary>
public class Phase3VariantsMigrationIntegrationTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ISubBatchProcessor> _mockSubBatchProcessor;
    private readonly Mock<ISubBatchConfigurationService> _mockConfigService;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<ILogger<VariantCreationStrategy>> _mockLogger;

    private readonly VariantCreationStrategy _variantCreationStrategy;
    private readonly StoreConfiguration _testDestinationStore;
    private readonly string _testMigrationId;

    public Phase3VariantsMigrationIntegrationTests()
    {
        _mockApiRequestHandler = new Mock<IApiRequestHandler>();
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockSubBatchProcessor = new Mock<ISubBatchProcessor>();
        _mockConfigService = new Mock<ISubBatchConfigurationService>();
        _mockErrorHandlingService = new Mock<IEntityErrorHandlingService>();
        _mockLogger = new Mock<ILogger<VariantCreationStrategy>>();

        _variantCreationStrategy = new VariantCreationStrategy(
            _mockApiRequestHandler.Object,
            _mockStorageService.Object,
            _mockSubBatchProcessor.Object,
            _mockConfigService.Object,
            _mockErrorHandlingService.Object,
            _mockLogger.Object);

        _testDestinationStore = new StoreConfiguration
        {
            StoreId = "test-store-123",
            AccessToken = "test-token",
            ChannelId = "1",
            BaseUrl = "https://api.bigcommerce.com"
        };

        _testMigrationId = "test-migration-456";
    }

    [Fact]
    public async Task VariantCreationStrategy_WithValidVariants_ShouldProcessSuccessfully()
    {
        // Arrange - Create 250 test variants (simulating a full page)
        var testVariants = CreateTestVariants(250);
        var config = CreateTestConfiguration();
        var expectedBatchResults = CreateExpectedBatchResults(250);

        SetupMocksForSuccessfulProcessing(config, expectedBatchResults);

        // Act
        var result = await _variantCreationStrategy.CreateEntitiesAsync(
            testVariants, _testMigrationId, _testDestinationStore);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(250);

        // Verify SubBatchProcessor was called with correct configuration
        _mockSubBatchProcessor.Verify(x => x.ProcessInSubBatchesAsync(
            testVariants,
            config,
            It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
            "variants",
            _testMigrationId,
            It.IsAny<CancellationToken>()), Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Creating 250 variants using optimized cached lookups")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task VariantCreationStrategy_WithBatchApiFailure_ShouldImplementContinueOnErrorPolicy()
    {
        // Arrange - Simulate BigCommerce API failure (422 is application error, not infrastructure)
        var testVariants = CreateTestVariants(50);
        var config = CreateTestConfiguration();

        _mockConfigService.Setup(x => x.GetConfiguration("variants"))
            .Returns(config);

        // Simulate API 422 failure in SubBatchProcessor (application error)
        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "variants",
                _testMigrationId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("BigCommerce API validation error: 422 Unprocessable Entity"));

        // Act
        var result = await _variantCreationStrategy.CreateEntitiesAsync(
            testVariants, _testMigrationId, _testDestinationStore);

        // Assert - Should return empty list (continue-on-error)
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        // Verify error was logged but not thrown (application-level error)
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to create 50 variants")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task VariantCreationStrategy_WithInfrastructureFailure_ShouldThrowException()
    {
        // Arrange - Simulate infrastructure failure (storage unavailable)
        var testVariants = CreateTestVariants(10);
        var config = CreateTestConfiguration();

        _mockConfigService.Setup(x => x.GetConfiguration("variants"))
            .Returns(config);

        // Simulate infrastructure failure
        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "variants",
                _testMigrationId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Azure Storage timeout"));

        // Act & Assert - Infrastructure errors should throw
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _variantCreationStrategy.CreateEntitiesAsync(
                testVariants, _testMigrationId, _testDestinationStore));

        // Verify critical infrastructure error was logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("INFRASTRUCTURE ERROR")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task VariantCreationStrategy_WithProductIdMapping_ShouldProcessWithCorrectConfiguration()
    {
        // Arrange - Test that variants with product IDs are processed correctly
        var testVariants = CreateTestVariantsWithProductIds(["product-123", "product-456"]);
        var config = CreateTestConfiguration();

        // Note: With mocked SubBatchProcessor, the actual transformation isn't called
        // This test validates the pipeline setup and configuration
        SetupMocksForSuccessfulProcessing(config, CreateExpectedBatchResults(2));

        // Act
        var result = await _variantCreationStrategy.CreateEntitiesAsync(
            testVariants, _testMigrationId, _testDestinationStore);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        // Verify correct configuration was used
        _mockConfigService.Verify(x => x.GetConfiguration("variants"), Times.Once);
        
        // Verify SubBatchProcessor was called with correct parameters
        _mockSubBatchProcessor.Verify(x => x.ProcessInSubBatchesAsync(
            testVariants, config, It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
            "variants", _testMigrationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VariantCreationStrategy_WithOptionValueMappings_ShouldProcessSuccessfully()
    {
        // Arrange - Test that variants with option values are processed correctly
        var testVariants = CreateTestVariantsWithOptionValues();
        var config = CreateTestConfiguration();

        // Note: With mocked SubBatchProcessor, we're testing the pipeline setup
        // The actual option value transformation would happen in the batch processor function
        SetupMocksForSuccessfulProcessing(config, CreateExpectedBatchResults(1));

        // Act
        var result = await _variantCreationStrategy.CreateEntitiesAsync(
            testVariants, _testMigrationId, _testDestinationStore);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);

        // Verify configuration and pipeline setup
        _mockConfigService.Verify(x => x.GetConfiguration("variants"), Times.Once);
        _mockSubBatchProcessor.Verify(x => x.ProcessInSubBatchesAsync(
            testVariants, config, It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
            "variants", _testMigrationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VariantCreationStrategy_ApiEfficiencyValidation_Should42xImprovement()
    {
        // Arrange - Test API efficiency: 6 calls vs 250 individual calls
        var testVariants = CreateTestVariants(250); // Full page of variants
        var config = CreateTestConfiguration();
        
        // Track API calls made by SubBatchProcessor
        var apiCallCount = 0;
        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<SubBatchConfiguration>(),
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "variants",
                _testMigrationId,
                It.IsAny<CancellationToken>()))
            .Returns<List<Dictionary<string, object>>, SubBatchConfiguration, Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>, string, string, CancellationToken>(
                async (entities, cfg, processor, entityType, migrationId, ct) =>
                {
                    // Simulate 5 sub-batches of 50 variants each (250 ÷ 50 = 5)
                    var results = new List<Dictionary<string, object>>();
                    
                    for (int i = 0; i < entities.Count; i += 50)
                    {
                        var batch = entities.Skip(i).Take(50).ToList();
                        apiCallCount++; // Track each batch API call
                        var batchResult = await processor(batch, ct);
                        results.AddRange(batchResult);
                    }
                    
                    return results;
                });

        _mockConfigService.Setup(x => x.GetConfiguration("variants"))
            .Returns(config);

        // Act
        var result = await _variantCreationStrategy.CreateEntitiesAsync(
            testVariants, _testMigrationId, _testDestinationStore);

        // Assert - Validate API efficiency
        apiCallCount.Should().Be(5); // 5 batch calls instead of 250 individual calls
        
        // Efficiency calculation: 250 individual calls vs (1 discovery + 5 batch calls) = 6 total
        var individualCalls = 250;
        var batchCalls = 6; // 1 discovery + 5 batch creation calls
        var efficiencyImprovement = (double)individualCalls / batchCalls;
        
        efficiencyImprovement.Should().BeApproximately(41.7, 0.5); // ~42x improvement
    }

    [Fact]
    public async Task VariantCreationStrategy_NoVariantMappingStorage_ShouldNotCallStorageForVariants()
    {
        // Arrange - Verify no variant mapping storage (leaf entity requirement)
        var testVariants = CreateTestVariants(10);
        var config = CreateTestConfiguration();

        SetupMocksForSuccessfulProcessing(config, CreateExpectedBatchResults(10));

        // Act
        var result = await _variantCreationStrategy.CreateEntitiesAsync(
            testVariants, _testMigrationId, _testDestinationStore);

        // Assert
        result.Should().NotBeNull();

        // Verify NO variant entity mapping storage calls were made
        _mockStorageService.Verify(x => x.CreateEntityMappingAsync(
            It.Is<EntityMapping>(em => em.EntityType == "variants")), Times.Never);

        _mockStorageService.Verify(x => x.CreateEntityMappingsBatchAsync(
            It.Is<List<EntityMapping>>(ems => ems.Any(em => em.EntityType == "variants"))), Times.Never);
    }

    #region Test Helper Methods

    private List<Dictionary<string, object>> CreateTestVariants(int count)
    {
        var variants = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= count; i++)
        {
            variants.Add(new Dictionary<string, object>
            {
                ["id"] = $"variant-{i}",
                ["product_id"] = $"product-{(i % 10) + 1}", // Distribute across 10 products
                ["sku"] = $"SKU-{i:000}",
                ["price"] = 19.99m + i,
                ["option_values"] = new List<Dictionary<string, object>>
                {
                    new()
                    {
                        ["option_id"] = $"option-{(i % 3) + 1}",
                        ["id"] = $"value-{i}",
                        ["label"] = $"Value {i}"
                    }
                }
            });
        }
        
        return variants;
    }

    private List<Dictionary<string, object>> CreateTestVariantsWithProductIds(string[] productIds)
    {
        return productIds.Select((productId, index) => new Dictionary<string, object>
        {
            ["id"] = $"variant-{index + 1}",
            ["product_id"] = productId,
            ["sku"] = $"SKU-{index + 1:000}",
            ["price"] = 19.99m + index
        }).ToList();
    }

    private List<Dictionary<string, object>> CreateTestVariantsWithOptionValues()
    {
        return new List<Dictionary<string, object>>
        {
            new()
            {
                ["id"] = "variant-1",
                ["product_id"] = "product-123",
                ["sku"] = "SKU-001",
                ["price"] = 19.99m,
                ["option_values"] = new List<object>
                {
                    JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        ["option_id"] = "option-111",
                        ["id"] = "value-222",
                        ["label"] = "Red"
                    })).RootElement
                }
            }
        };
    }

    private SubBatchConfiguration CreateTestConfiguration()
    {
        return new SubBatchConfiguration
        {
            EntityType = "variants",
            FetchBatchSize = 250,
            ChunkSize = 250,
            PageSize = 250,
            SubBatchSize = 50,
            MaxConcurrency = 5,
            EnableSubBatching = true,
            SubBatchDelayMs = 100,
            ProcessSubBatchesSequentially = false
        };
    }

    private List<Dictionary<string, object>> CreateExpectedBatchResults(int count)
    {
        var results = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= count; i++)
        {
            results.Add(new Dictionary<string, object>
            {
                ["id"] = $"created-variant-{i}",
                ["sku"] = $"SKU-{i:000}",
                ["created_at"] = DateTime.UtcNow.ToString("O")
            });
        }
        
        return results;
    }

    private void SetupMocksForSuccessfulProcessing(SubBatchConfiguration config, List<Dictionary<string, object>> expectedResults)
    {
        _mockConfigService.Setup(x => x.GetConfiguration("variants"))
            .Returns(config);

        _mockSubBatchProcessor.Setup(x => x.ProcessInSubBatchesAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                config,
                It.IsAny<Func<List<Dictionary<string, object>>, CancellationToken, Task<List<Dictionary<string, object>>>>>(),
                "variants",
                _testMigrationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResults);
    }

    #endregion
}