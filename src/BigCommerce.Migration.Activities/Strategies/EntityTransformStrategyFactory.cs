using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies;

/// <summary>
/// Factory for creating entity transform strategies
/// Implements Open/Closed Principle: new strategies can be added without modifying the factory
/// Uses case-insensitive entity type matching for robust resolution
/// </summary>
public class EntityTransformStrategyFactory : IEntityTransformStrategyFactory
{
    private readonly Dictionary<string, IEntityTransformStrategy> _strategies;
    private readonly ILogger<EntityTransformStrategyFactory> _logger;

    public EntityTransformStrategyFactory(
        IEnumerable<IEntityTransformStrategy> strategies,
        ILogger<EntityTransformStrategyFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Create case-insensitive dictionary for strategy lookup
        _strategies = strategies?.ToDictionary(
            s => s.EntityType.ToLowerInvariant(),
            s => s,
            StringComparer.OrdinalIgnoreCase) ?? throw new ArgumentNullException(nameof(strategies));

        _logger.LogInformation("Initialized EntityTransformStrategyFactory with {StrategyCount} strategies: {EntityTypes}",
            _strategies.Count, string.Join(", ", _strategies.Keys));
    }

    public IEntityTransformStrategy GetStrategy(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        }

        var normalizedEntityType = entityType.ToLowerInvariant();

        if (_strategies.TryGetValue(normalizedEntityType, out var strategy))
        {
            _logger.LogDebug("Found transform strategy for entity type: {EntityType}", entityType);
            return strategy;
        }

        var availableTypes = string.Join(", ", _strategies.Keys);
        var message = $"No transform strategy found for entity type '{entityType}'. Available types: {availableTypes}";
        
        _logger.LogError(message);
        throw new ArgumentException(message, nameof(entityType));
    }
} 