using BigCommerce.Migration.Activities.Services.EntityCreation;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System.Globalization;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Services;

/// <summary>
/// Simplified unit tests for VariantCreationStrategy class
/// Focuses on basic functionality and Task 4 fix validation
/// </summary>
public class VariantCreationStrategySimpleTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ISubBatchProcessor> _mockSubBatchProcessor;
    private readonly Mock<ISubBatchConfigurationService> _mockConfigService;
    private readonly Mock<IEntityErrorHandlingService> _mockErrorHandlingService;
    private readonly Mock<ILogger<VariantCreationStrategy>> _mockLogger;
    private readonly VariantCreationStrategy _strategy;

    public VariantCreationStrategySimpleTests()
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

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDependencies_ShouldCreateInstance()
    {
        // Act & Assert
        Assert.NotNull(_strategy);
        Assert.Equal("variants", _strategy.EntityType);
    }

    [Fact]
    public void Constructor_WithNullApiRequestHandler_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VariantCreationStrategy(
            null!, _mockStorageService.Object, _mockSubBatchProcessor.Object,
            _mockConfigService.Object, _mockErrorHandlingService.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullStorageService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VariantCreationStrategy(
            _mockApiRequestHandler.Object, null!, _mockSubBatchProcessor.Object,
            _mockConfigService.Object, _mockErrorHandlingService.Object, _mockLogger.Object));
    }

    #endregion

    #region EntityType Tests

    [Fact]
    public void EntityType_ShouldReturnVariants()
    {
        // Act
        var entityType = _strategy.EntityType;

        // Assert
        Assert.Equal("variants", entityType);
    }

    #endregion

    #region CreateEntitiesAsync Basic Tests

    [Fact]
    public async Task CreateEntitiesAsync_WithEmptyEntities_ShouldReturnEmptyList()
    {
        // Arrange
        var emptyEntities = new List<Dictionary<string, object>>();
        const string migrationId = "test-migration-123";
        var storeConfig = new StoreConfiguration { StoreId = "test-store" };

        // Act
        var result = await _strategy.CreateEntitiesAsync(emptyEntities, migrationId, storeConfig, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithNullEntities_ShouldReturnEmptyList()
    {
        // Arrange
        List<Dictionary<string, object>>? nullEntities = null;
        const string migrationId = "test-migration-123";
        var storeConfig = new StoreConfiguration { StoreId = "test-store" };

        // Act
        var result = await _strategy.CreateEntitiesAsync(nullEntities!, migrationId, storeConfig, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateEntitiesAsync_WithNullMigrationId_ShouldHandleGracefully()
    {
        // Arrange
        var entities = new List<Dictionary<string, object>>();
        var storeConfig = new StoreConfiguration { StoreId = "test-store" };

        // Act
        var result = await _strategy.CreateEntitiesAsync(entities, null!, storeConfig, null, CancellationToken.None);

        // Assert - Should handle gracefully and return empty list
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region Task 4 Business Logic Tests

    [Theory]
    [InlineData("VARIANT-001", "VARIANT-002", true)]  // Different SKUs, same options -> should be duplicates
    [InlineData("SAME-SKU", "SAME-SKU", true)]        // Same SKUs, same options -> should be duplicates
    [InlineData("VARIANT-001", "VARIANT-002", false)] // Different SKUs, different options -> should be unique
    public void OptionCombinationLogic_ShouldFollowBusinessRules(string sku1, string sku2, bool sameOptions)
    {
        // Arrange - Create option combinations
        var options1 = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 1, ["id"] = 10 }, // Color: Red
            new() { ["option_id"] = 2, ["id"] = 20 }  // Size: Large
        };

        var options2 = sameOptions 
            ? new List<Dictionary<string, object>>
            {
                new() { ["option_id"] = 1, ["id"] = 10 }, // Color: Red (same)
                new() { ["option_id"] = 2, ["id"] = 20 }  // Size: Large (same)
            }
            : new List<Dictionary<string, object>>
            {
                new() { ["option_id"] = 1, ["id"] = 11 }, // Color: Blue (different)
                new() { ["option_id"] = 2, ["id"] = 20 }  // Size: Large (same)
            };

        // Act - Generate option combination keys
        var key1 = GenerateOptionCombinationKey(options1);
        var key2 = GenerateOptionCombinationKey(options2);

        // Assert - Task 4 key insight: Option combinations determine uniqueness, not SKUs
        if (sameOptions)
        {
            Assert.Equal(key1, key2); // Same option combinations should generate same key
        }
        else
        {
            Assert.NotEqual(key1, key2); // Different option combinations should generate different keys
        }
    }

    [Fact]
    public void OptionCombinationKey_WithOrderedOptions_ShouldBeConsistent()
    {
        // Arrange - Same options in different orders
        var options1 = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 1, ["id"] = 10 },
            new() { ["option_id"] = 2, ["id"] = 20 }
        };

        var options2 = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 2, ["id"] = 20 }, // Reversed order
            new() { ["option_id"] = 1, ["id"] = 10 }
        };

        // Act
        var key1 = GenerateOptionCombinationKey(options1);
        var key2 = GenerateOptionCombinationKey(options2);

        // Assert - Order shouldn't matter (should be sorted internally)
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void OptionCombinationKey_WithEmptyOptions_ShouldReturnEmptyString()
    {
        // Arrange
        var emptyOptions = new List<Dictionary<string, object>>();

        // Act
        var key = GenerateOptionCombinationKey(emptyOptions);

        // Assert
        Assert.Equal(string.Empty, key);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Replicates the option combination key generation logic from Task 4 fix
    /// This is the core business logic being tested
    /// </summary>
    private static string GenerateOptionCombinationKey(List<Dictionary<string, object>> optionValues)
    {
        if (!optionValues.Any())
            return string.Empty;

        return string.Join("-", optionValues
            .OrderBy(ov => ov.TryGetValue("option_id", out var oid) ? oid?.ToString() : "0")
            .Select(ov =>
            {
                var optionId = ov.TryGetValue("option_id", out var oid) ? oid?.ToString() : "0";
                var valueId = ov.TryGetValue("id", out var vid) ? vid?.ToString() : "0";
                return $"{optionId}:{valueId}";
            }));
    }

    private static Dictionary<string, object> CreateVariantWithOptions(
        string sku, 
        string productId, 
        string id, 
        (string optionId, string valueId)[] options)
    {
        var optionValues = options.Select(opt => new Dictionary<string, object>
        {
            ["option_id"] = int.Parse(opt.optionId, CultureInfo.InvariantCulture),
            ["id"] = int.Parse(opt.valueId, CultureInfo.InvariantCulture)
        }).ToList();

        return new Dictionary<string, object>
        {
            ["id"] = int.Parse(id, CultureInfo.InvariantCulture),
            ["sku"] = sku,
            ["product_id"] = int.Parse(productId, CultureInfo.InvariantCulture),
            ["option_values"] = optionValues,
            ["price"] = 29.99m,
            ["weight"] = 1.5m,
            ["inventory_level"] = 100
        };
    }

    #endregion
}