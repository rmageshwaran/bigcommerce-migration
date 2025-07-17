using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services.EntityCreation;

/// <summary>
/// Factory implementation for providing entity creation strategies
/// Implements Open/Closed Principle: new entity types can be added without modifying existing code
/// </summary>
public class EntityCreationStrategyFactory : IEntityCreationStrategyFactory
{
    private readonly IEnumerable<IEntityCreationStrategy> _strategies;
    private readonly ILogger<EntityCreationStrategyFactory> _logger;

    public EntityCreationStrategyFactory(
        IEnumerable<IEntityCreationStrategy> strategies,
        ILogger<EntityCreationStrategyFactory> logger)
    {
        _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the appropriate entity creation strategy for the specified entity type
    /// </summary>
    /// <param name="entityType">The entity type (e.g., "categories", "products", "brands")</param>
    /// <returns>Strategy instance for the entity type</returns>
    /// <exception cref="ArgumentException">Thrown when entity type is not supported</exception>
    public IEntityCreationStrategy GetStrategy(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        }

        _logger.LogDebug("Selecting creation strategy for entity type: {EntityType}", entityType);

        // Find strategy that matches entity type (case-insensitive)
        var strategy = _strategies.FirstOrDefault(s => 
            s.EntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase));

        if (strategy == null)
        {
            var supportedTypes = string.Join(", ", _strategies.Select(s => s.EntityType));
            var errorMessage = $"Unsupported entity type: {entityType}. Supported types: {supportedTypes}";
            
            _logger.LogError("No creation strategy found for entity type {EntityType}. Supported types: {SupportedTypes}", 
                entityType, supportedTypes);
            
            throw new ArgumentException(errorMessage, nameof(entityType));
        }

        _logger.LogInformation("Selected {StrategyType} for entity type {EntityType}", 
            strategy.GetType().Name, entityType);

        return strategy;
    }
} 