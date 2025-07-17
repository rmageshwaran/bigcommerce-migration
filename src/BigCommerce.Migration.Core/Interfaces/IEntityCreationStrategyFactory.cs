namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Factory interface for providing entity creation strategies
/// Implements Open/Closed Principle by enabling strategy selection without modifying existing code
/// </summary>
public interface IEntityCreationStrategyFactory
{
    /// <summary>
    /// Gets the appropriate entity creation strategy for the specified entity type
    /// </summary>
    /// <param name="entityType">The entity type (e.g., "categories", "products", "brands")</param>
    /// <returns>Strategy instance for the entity type</returns>
    /// <exception cref="ArgumentException">Thrown when entity type is not supported</exception>
    IEntityCreationStrategy GetStrategy(string entityType);
} 