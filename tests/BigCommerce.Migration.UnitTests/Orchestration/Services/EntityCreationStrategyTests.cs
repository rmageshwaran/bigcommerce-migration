using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.UnitTests.TestHelpers;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Orchestration.Services;

/// <summary>
/// TDD tests for Entity Creation Strategy Pattern interfaces
/// GREEN phase: Basic interface contract validation
/// </summary>
public class EntityCreationStrategyTests
{
    #region Interface Contract Tests (GREEN phase)

    [Fact]
    public void IEntityCreationStrategy_ShouldHave_RequiredProperties()
    {
        // Verify interface exists and has expected properties
        var strategyType = typeof(IEntityCreationStrategy);
        strategyType.Should().NotBeNull();
        
        // Verify EntityType property exists
        var entityTypeProperty = strategyType.GetProperty("EntityType");
        entityTypeProperty.Should().NotBeNull();
        entityTypeProperty!.PropertyType.Should().Be(typeof(string));
        
        // Verify CreateEntitiesAsync method exists
        var createMethod = strategyType.GetMethod("CreateEntitiesAsync");
        createMethod.Should().NotBeNull();
        createMethod!.ReturnType.Should().Be(typeof(Task<List<Dictionary<string, object>>?>));
    }

    [Fact]
    public void IEntityCreationStrategy_ShouldHave_CorrectMethodSignature()
    {
        // Verify CreateEntitiesAsync method signature
        var strategyType = typeof(IEntityCreationStrategy);
        var createMethod = strategyType.GetMethod("CreateEntitiesAsync");
        
        createMethod.Should().NotBeNull();
        
        var parameters = createMethod!.GetParameters();
        parameters.Should().HaveCount(5);
        parameters[0].ParameterType.Should().Be(typeof(List<Dictionary<string, object>>));
        parameters[1].ParameterType.Should().Be(typeof(string)); // migrationId
        parameters[2].ParameterType.Should().Be(typeof(StoreConfiguration)); // destinationStore
        parameters[3].ParameterType.Should().Be(typeof(CategoryTreeContext)); // categoryTreeContext
        parameters[4].ParameterType.Should().Be(typeof(CancellationToken));
    }

    #endregion

    #region Strategy Factory Interface Tests (GREEN phase)

    [Fact]
    public void IEntityCreationStrategyFactory_ShouldHave_GetStrategyMethod()
    {
        // Verify factory interface contract
        var factoryType = typeof(IEntityCreationStrategyFactory);
        factoryType.Should().NotBeNull();
        
        var getStrategyMethod = factoryType.GetMethod("GetStrategy");
        getStrategyMethod.Should().NotBeNull();
        getStrategyMethod!.ReturnType.Should().Be(typeof(IEntityCreationStrategy));
        
        var parameters = getStrategyMethod.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(string)); // entityType
    }

    #endregion

    #region Test Data Helpers

    private StoreConfiguration CreateTestStoreConfiguration()
    {
        return TestDataFactory.CreateDestinationStoreConfiguration();
    }

    private CategoryTreeContext CreateTestCategoryTreeContext()
    {
        return new CategoryTreeContext
        {
            DestinationCategoryTreeId = "dest-tree-123"
        };
    }

    private List<Dictionary<string, object>> CreateTestEntities()
    {
        return new List<Dictionary<string, object>>
        {
            new() { ["id"] = "1", ["name"] = "Test Entity 1" },
            new() { ["id"] = "2", ["name"] = "Test Entity 2" }
        };
    }

    #endregion
} 