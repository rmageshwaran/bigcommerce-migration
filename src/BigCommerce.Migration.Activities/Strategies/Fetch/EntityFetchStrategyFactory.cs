using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Fetch;

/// <summary>
/// Factory for creating entity fetch strategies based on entity type
/// Implements Open/Closed Principle by enabling new strategies without modifying existing code
/// Uses interface collection pattern for automatic strategy discovery
/// </summary>
public class EntityFetchStrategyFactory : IEntityFetchStrategyFactory
{
    private readonly Dictionary<string, IEntityFetchStrategy> _strategies;
    private readonly ILogger<EntityFetchStrategyFactory> _logger;

    public EntityFetchStrategyFactory(
        IEnumerable<IEntityFetchStrategy> strategies,
        ILogger<EntityFetchStrategyFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create case-insensitive dictionary for strategy lookup
        _strategies = strategies?.ToDictionary(
            s => s.EntityType.ToLowerInvariant(),
            s => s,
            StringComparer.OrdinalIgnoreCase) ?? throw new ArgumentNullException(nameof(strategies));

        _logger.LogInformation("Initialized EntityFetchStrategyFactory with {StrategyCount} strategies: {EntityTypes}",
            _strategies.Count, string.Join(", ", _strategies.Keys));
    }

    public IEntityFetchStrategy GetStrategy(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        }

        // Normalize entity type to handle case variations and pluralization
        var normalizedType = NormalizeEntityType(entityType);

        if (_strategies.TryGetValue(normalizedType, out var strategy))
        {
            _logger.LogDebug("Found fetch strategy for entity type: {EntityType}", entityType);
            return strategy;
        }

        var availableTypes = string.Join(", ", _strategies.Keys);
        var message = $"No fetch strategy found for entity type '{entityType}'. Available types: {availableTypes}";

        _logger.LogError(message);
        throw new ArgumentException(message, nameof(entityType));
    }

    /// <summary>
    /// Normalizes entity type to handle various input formats
    /// Supports: categories, category, Categories, CATEGORIES, etc.
    /// Note: Products, brands, and variants use direct pagination, not fetch strategies
    /// Note: Variants are routed to direct pagination in EntityFetchService to align with product workflow
    /// </summary>
    private static string NormalizeEntityType(string entityType)
    {
        var normalized = entityType.Trim().ToLowerInvariant();

        // Handle singular/plural variations
        return normalized switch
        {
            "category" => "categories",
            // Note: "product", "brand", "variant" removed - these use direct pagination
            // Note: "image", "modifier", "option" removed - these are handled as sub-entities within product phases
            _ => normalized
        };
    }
}