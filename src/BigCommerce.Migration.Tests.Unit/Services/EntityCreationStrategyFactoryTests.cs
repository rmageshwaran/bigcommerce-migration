using BigCommerce.Migration.Activities.Services.EntityCreation;
using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Services;

/// <summary>
/// Unit tests for EntityCreationStrategyFactory
/// Tests EntityCreationStrategyFactory with consistent variants entity type
/// </summary>
public class EntityCreationStrategyFactoryTests
{
    private readonly Mock<ILogger<EntityCreationStrategyFactory>> _mockLogger;
    private readonly Mock<IEntityCreationStrategy> _mockVariantStrategy;
    private readonly EntityCreationStrategyFactory _factory;

    public EntityCreationStrategyFactoryTests()
    {
        _mockLogger = new Mock<ILogger<EntityCreationStrategyFactory>>();
        _mockVariantStrategy = new Mock<IEntityCreationStrategy>();
        
        // Setup variant strategy to return "variants" as EntityType
        _mockVariantStrategy.Setup(x => x.EntityType).Returns("variants");
        
        var strategies = new List<IEntityCreationStrategy> { _mockVariantStrategy.Object };
        _factory = new EntityCreationStrategyFactory(strategies, _mockLogger.Object);
    }

    [Fact]
    public void GetStrategy_WithVariants_ShouldReturnVariantStrategy()
    {
        // Act
        var strategy = _factory.GetStrategy("variants");

        // Assert
        Assert.NotNull(strategy);
        Assert.Same(_mockVariantStrategy.Object, strategy);
    }

    [Theory]
    [InlineData("variants")]
    [InlineData("VARIANTS")]
    [InlineData("Variants")]
    public void GetStrategy_WithVariantsCaseInsensitive_ShouldReturnVariantStrategy(string entityType)
    {
        // Act
        var strategy = _factory.GetStrategy(entityType);

        // Assert
        Assert.NotNull(strategy);
        Assert.Same(_mockVariantStrategy.Object, strategy);
    }

    [Fact]
    public void GetStrategy_WithUnsupportedType_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _factory.GetStrategy("unsupported-type"));
        Assert.Contains("Unsupported entity type", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported-type", exception.Message, StringComparison.Ordinal);
    }
}