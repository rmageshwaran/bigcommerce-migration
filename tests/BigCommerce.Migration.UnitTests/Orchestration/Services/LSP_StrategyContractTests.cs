using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Strategies;
using System.Reflection;

namespace BigCommerce.Migration.UnitTests.Orchestration.Services;

/// <summary>
/// LSP (Liskov Substitution Principle) contract tests for IEntityFetchStrategy implementations
/// Ensures all strategy implementations are truly substitutable and behave consistently
/// RED PHASE: All tests should initially fail - we're testing desired LSP compliance behavior
/// </summary>
public class LSP_StrategyContractTests
{
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly Mock<ILogger<CategoryFetchStrategy>> _mockCategoryLogger;
    private readonly Mock<ILogger<ProductFetchStrategy>> _mockProductLogger;
    private readonly Mock<ILogger<BrandFetchStrategy>> _mockBrandLogger;
    private readonly Mock<ILogger<VariantFetchStrategy>> _mockVariantLogger;
    private readonly Mock<ILogger<ImageFetchStrategy>> _mockImageLogger;
    private readonly Mock<ILogger<ModifierFetchStrategy>> _mockModifierLogger;

    private readonly StoreConfiguration _testStoreConfig;
    private readonly List<string> _testEntityIds;
    private readonly string _testMigrationId;

    public LSP_StrategyContractTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _mockCategoryLogger = new Mock<ILogger<CategoryFetchStrategy>>();
        _mockProductLogger = new Mock<ILogger<ProductFetchStrategy>>();
        _mockBrandLogger = new Mock<ILogger<BrandFetchStrategy>>();
        _mockVariantLogger = new Mock<ILogger<VariantFetchStrategy>>();
        _mockImageLogger = new Mock<ILogger<ImageFetchStrategy>>();
        _mockModifierLogger = new Mock<ILogger<ModifierFetchStrategy>>();

        _testStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store-123",
            AccessToken = "test-token",
            ChannelId = "1", // Required for StoreConfiguration.IsValid()
            BaseUrl = "https://test-store.mybigcommerce.com"
        };

        _testEntityIds = new List<string> { "1", "2", "3" };
        _testMigrationId = "migration-123";
    }

    /// <summary>
    /// Factory method to create all strategy implementations for testing
    /// </summary>
    private List<IEntityFetchStrategy> GetAllStrategyImplementations()
    {
        return new List<IEntityFetchStrategy>
        {
            new CategoryFetchStrategy(_mockApiClient.Object, _mockCategoryLogger.Object),
            new ProductFetchStrategy(_mockApiClient.Object, _mockProductLogger.Object),
            new BrandFetchStrategy(_mockApiClient.Object, _mockBrandLogger.Object),
            new VariantFetchStrategy(_mockApiClient.Object, _mockVariantLogger.Object),
            new ImageFetchStrategy(_mockApiClient.Object, _mockImageLogger.Object),
            new ModifierFetchStrategy(_mockApiClient.Object, _mockModifierLogger.Object)
        };
    }

    #region LSP Contract Tests - Null Parameter Handling

    /// <summary>
    /// RED PHASE: LSP Contract - All strategies must handle null entityIds consistently
    /// </summary>
    [Fact]
    public async Task LSP_AllStrategies_ShouldHandleNullEntityIds_Consistently()
    {
        // Arrange
        var strategies = GetAllStrategyImplementations();
        var behaviorsConsistent = true;
        Exception? firstException = null;
        var firstStrategyName = "";

        // Act & Assert - All strategies should behave the same way with null input
        foreach (var strategy in strategies)
        {
            try
            {
                var result = await strategy.FetchEntitiesAsync(
                    null!, // NULL input to test LSP compliance
                    _testMigrationId,
                    _testStoreConfig,
                    cancellationToken: CancellationToken.None);

                if (firstException == null)
                {
                    // First strategy didn't throw - record this behavior
                    firstStrategyName = strategy.EntityType;
                }
                else
                {
                    // Previous strategy threw but this one didn't - inconsistent!
                    behaviorsConsistent = false;
                    break;
                }
            }
            catch (Exception ex)
            {
                if (firstException == null)
                {
                    // First exception - record it
                    firstException = ex;
                    firstStrategyName = strategy.EntityType;
                }
                else
                {
                    // Compare with first exception - should be same type
                    if (ex.GetType() != firstException.GetType())
                    {
                        behaviorsConsistent = false;
                        break;
                    }
                }
            }
        }

        // LSP VALIDATION: All strategies must behave identically with null input
        behaviorsConsistent.Should().BeTrue(
            $"LSP Violation: All IEntityFetchStrategy implementations must handle null entityIds consistently. " +
            $"First strategy ({firstStrategyName}) behavior differs from others.");
        
        // If they all throw, they should throw ArgumentNullException
        if (firstException != null)
        {
            firstException.Should().BeOfType<ArgumentNullException>(
                "All strategies should throw ArgumentNullException for null entityIds");
        }
    }

    /// <summary>
    /// RED PHASE: LSP Contract - All strategies must handle null migrationId consistently
    /// </summary>
    [Fact]
    public async Task LSP_AllStrategies_ShouldHandleNullMigrationId_Consistently()
    {
        // Arrange
        var strategies = GetAllStrategyImplementations();
        var exceptionTypes = new List<Type>();

        // Act & Assert
        foreach (var strategy in strategies)
        {
            try
            {
                await strategy.FetchEntitiesAsync(
                    _testEntityIds,
                    null!, // NULL migration ID
                    _testStoreConfig,
                    cancellationToken: CancellationToken.None);
                
                exceptionTypes.Add(typeof(void)); // No exception thrown
            }
            catch (Exception ex)
            {
                exceptionTypes.Add(ex.GetType());
            }
        }

        // LSP VALIDATION: All strategies must handle null migrationId the same way
        var uniqueExceptionTypes = exceptionTypes.Distinct().ToList();
        uniqueExceptionTypes.Should().HaveCount(1, 
            "LSP Violation: All strategies must handle null migrationId consistently");
    }

    /// <summary>
    /// RED PHASE: LSP Contract - All strategies must handle null storeConfig consistently
    /// </summary>
    [Fact]
    public async Task LSP_AllStrategies_ShouldHandleNullStoreConfig_Consistently()
    {
        // Arrange
        var strategies = GetAllStrategyImplementations();
        var exceptionTypes = new List<Type>();

        // Act & Assert
        foreach (var strategy in strategies)
        {
            try
            {
                await strategy.FetchEntitiesAsync(
                    _testEntityIds,
                    _testMigrationId,
                    null!, // NULL store config
                    cancellationToken: CancellationToken.None);
                
                exceptionTypes.Add(typeof(void)); // No exception thrown
            }
            catch (Exception ex)
            {
                exceptionTypes.Add(ex.GetType());
            }
        }

        // LSP VALIDATION: All strategies must handle null storeConfig the same way
        var uniqueExceptionTypes = exceptionTypes.Distinct().ToList();
        uniqueExceptionTypes.Should().HaveCount(1, 
            "LSP Violation: All strategies must handle null storeConfig consistently");
        
        // Should throw ArgumentNullException for null store config
        if (exceptionTypes.First() != typeof(void))
        {
            exceptionTypes.First().Should().Be(typeof(ArgumentNullException),
                "All strategies should throw ArgumentNullException for null storeConfig");
        }
    }

    #endregion

    #region LSP Contract Tests - Empty Input Handling

    /// <summary>
    /// RED PHASE: LSP Contract - All strategies must handle empty entityIds consistently
    /// </summary>
    [Fact]
    public async Task LSP_AllStrategies_ShouldHandleEmptyEntityIds_Consistently()
    {
        // Arrange
        var strategies = GetAllStrategyImplementations();
        var results = new List<List<Dictionary<string, object>>>();

        // Act
        foreach (var strategy in strategies)
        {
            var result = await strategy.FetchEntitiesAsync(
                new List<string>(), // EMPTY list
                _testMigrationId,
                _testStoreConfig,
                cancellationToken: CancellationToken.None);
            
            results.Add(result);
        }

        // Assert - LSP VALIDATION: All strategies should return empty lists for empty input
        foreach (var result in results)
        {
            result.Should().NotBeNull("All strategies should return non-null results");
            result.Should().BeEmpty("All strategies should return empty list for empty entityIds (LSP compliance)");
        }
    }

    #endregion

    #region LSP Contract Tests - Return Value Structure

    /// <summary>
    /// RED PHASE: LSP Contract - All strategies must return consistent data structure
    /// </summary>
    [Fact]
    public async Task LSP_AllStrategies_ShouldReturnConsistentDataStructure()
    {
        // Arrange
        var strategies = GetAllStrategyImplementations();
        
        // Setup mock API client to return test data for all entity types
        SetupMockApiClientForAllEntityTypes();

        var results = new List<List<Dictionary<string, object>>>();

        // Act
        foreach (var strategy in strategies)
        {
            var result = await strategy.FetchEntitiesAsync(
                _testEntityIds,
                _testMigrationId,
                _testStoreConfig,
                cancellationToken: CancellationToken.None);
            
            results.Add(result);
        }

        // Assert - LSP VALIDATION: All strategies should return the same type structure
        foreach (var result in results)
        {
            result.Should().NotBeNull("All strategies should return non-null results");
            result.Should().BeOfType<List<Dictionary<string, object>>>(
                "All strategies must return List<Dictionary<string, object>> for LSP compliance");
            
            // Each item should have consistent structure
            foreach (var item in result)
            {
                item.Should().ContainKey("id", "All entities should have 'id' field for consistency");
                item["id"].Should().NotBeNull("Entity ID should not be null");
            }
        }
    }

    /// <summary>
    /// RED PHASE: LSP Contract - All strategies must include tracking metadata consistently
    /// </summary>
    [Fact]
    public async Task LSP_AllStrategies_ShouldIncludeTrackingMetadata_Consistently()
    {
        // Arrange
        var strategies = GetAllStrategyImplementations();
        SetupMockApiClientForAllEntityTypes();

        var results = new List<List<Dictionary<string, object>>>();

        // Act
        foreach (var strategy in strategies)
        {
            var result = await strategy.FetchEntitiesAsync(
                _testEntityIds,
                _testMigrationId,
                _testStoreConfig,
                cancellationToken: CancellationToken.None);
            
            results.Add(result);
        }

        // Assert - LSP VALIDATION: All strategies should include same tracking metadata
        foreach (var result in results)
        {
            foreach (var item in result)
            {
                // All strategies should add original entity ID tracking
                item.Should().ContainKey("_original_entity_id", 
                    "All strategies must include _original_entity_id for error tracking (LSP compliance)");
            }
        }
    }

    #endregion

    #region LSP Contract Tests - Cancellation Handling

    /// <summary>
    /// RED PHASE: LSP Contract - All strategies must handle cancellation consistently
    /// </summary>
    [Fact]
    public async Task LSP_AllStrategies_ShouldHandleCancellation_Consistently()
    {
        // Arrange
        var strategies = GetAllStrategyImplementations();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        var exceptionTypes = new List<Type>();

        // Act & Assert
        foreach (var strategy in strategies)
        {
            try
            {
                await strategy.FetchEntitiesAsync(
                    _testEntityIds,
                    _testMigrationId,
                    _testStoreConfig,
                    cancellationToken: cts.Token);
                
                exceptionTypes.Add(typeof(void)); // No exception
            }
            catch (Exception ex)
            {
                exceptionTypes.Add(ex.GetType());
            }
        }

        // LSP VALIDATION: All strategies must handle cancellation the same way
        var uniqueExceptionTypes = exceptionTypes.Distinct().ToList();
        uniqueExceptionTypes.Should().HaveCount(1, 
            "LSP Violation: All strategies must handle cancellation consistently");
        
        if (exceptionTypes.First() != typeof(void))
        {
            exceptionTypes.First().Should().Be(typeof(OperationCanceledException),
                "All strategies should throw OperationCanceledException for cancelled tokens");
        }
    }

    #endregion

    #region LSP Contract Tests - Constructor Requirements

    /// <summary>
    /// RED PHASE: LSP Contract - All strategies must have consistent constructor dependencies
    /// </summary>
    [Fact]
    public void LSP_AllStrategies_ShouldHaveConsistentConstructorDependencies()
    {
        // Arrange
        var strategyTypes = new[]
        {
            typeof(CategoryFetchStrategy),
            typeof(ProductFetchStrategy),
            typeof(BrandFetchStrategy),
            typeof(VariantFetchStrategy),
            typeof(ImageFetchStrategy),
            typeof(ModifierFetchStrategy)
        };

        var constructorSignatures = new List<Type[]>();

        // Act
        foreach (var strategyType in strategyTypes)
        {
            var constructor = strategyType.GetConstructors().FirstOrDefault();
            constructor.Should().NotBeNull($"{strategyType.Name} should have a public constructor");
            
            var parameterTypes = constructor!.GetParameters().Select(p => p.ParameterType).ToArray();
            constructorSignatures.Add(parameterTypes);
        }

        // Assert - LSP VALIDATION: All strategies should have consistent constructor signatures
        var firstSignature = constructorSignatures.First();
        foreach (var signature in constructorSignatures)
        {
            signature.Should().HaveCount(firstSignature.Length, 
                "LSP Violation: All strategy implementations must have the same number of constructor parameters");
            
            // Check that parameter types match (except for logger which can be strategy-specific)
            for (int i = 0; i < firstSignature.Length; i++)
            {
                if (i == 1) // Logger parameter (second parameter)
                {
                    // Logger types can be different for each strategy (ILogger<StrategyType>)
                    // Skip logger type validation as they can be strategy-specific
                }
                else
                {
                    signature[i].Should().Be(firstSignature[i], 
                        $"Parameter {i} should have the same type across all strategies");
                }
            }
        }

        // Validate expected dependencies
        firstSignature.Should().Contain(typeof(IBigCommerceApiClient), 
            "All strategies should depend on IBigCommerceApiClient");
        firstSignature.Should().HaveCount(2, 
            "All strategies should have exactly 2 constructor parameters for consistency");
    }

    #endregion

    #region LSP Contract Tests - EntityType Property

    /// <summary>
    /// RED PHASE: LSP Contract - All strategies must have unique, non-null EntityType values
    /// </summary>
    [Fact]
    public void LSP_AllStrategies_ShouldHaveUniqueEntityTypes()
    {
        // Arrange
        var strategies = GetAllStrategyImplementations();
        var entityTypes = new List<string>();

        // Act
        foreach (var strategy in strategies)
        {
            entityTypes.Add(strategy.EntityType);
        }

        // Assert - LSP VALIDATION
        entityTypes.Should().OnlyContain(et => !string.IsNullOrWhiteSpace(et), 
            "All strategies must have non-null, non-empty EntityType");
        
        entityTypes.Should().OnlyHaveUniqueItems("All strategies must have unique EntityType values");
        
        // Validate expected entity types
        var expectedEntityTypes = new[] { "categories", "products", "brands", "variants", "images", "modifiers" };
        entityTypes.Should().BeEquivalentTo(expectedEntityTypes, 
            "All expected entity types should be implemented");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Sets up mock API client to return test data for all entity types
    /// </summary>
    private void SetupMockApiClientForAllEntityTypes()
    {
        // Setup for categories - correct signature: GetCategoriesAsync(StoreConfiguration, string, CancellationToken)
        _mockApiClient.Setup(x => x.GetCategoriesAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>
            {
                new() { ["id"] = "1", ["name"] = "Category 1" },
                new() { ["id"] = "2", ["name"] = "Category 2" }
            });

        // Setup for products - correct signature: GetProductsAsync(StoreConfiguration, int, int, CancellationToken)
        _mockApiClient.Setup(x => x.GetProductsAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>
            {
                new() { ["id"] = "1", ["name"] = "Product 1" },
                new() { ["id"] = "2", ["name"] = "Product 2" }
            });

        // Setup for paginated entities (brands, variants, images, modifiers)
        var paginatedResponse = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = new List<Dictionary<string, object>>
            {
                new() { ["id"] = "1", ["name"] = "Test Entity 1" },
                new() { ["id"] = "2", ["name"] = "Test Entity 2" }
            },
            HasNextPage = false
        };

        _mockApiClient.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<string>(),
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(paginatedResponse);
    }

    #endregion
} 