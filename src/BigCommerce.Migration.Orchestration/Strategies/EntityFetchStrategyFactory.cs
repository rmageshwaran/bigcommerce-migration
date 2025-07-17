using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Factory for creating entity fetch strategies based on entity type
/// Implements Open/Closed Principle by enabling new strategies without modifying existing code
/// </summary>
public class EntityFetchStrategyFactory : IEntityFetchStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _strategyMap;

    public EntityFetchStrategyFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // Map entity types to their corresponding strategy implementations
        _strategyMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["categories"] = typeof(CategoryFetchStrategy),
            ["products"] = typeof(ProductFetchStrategy),
            ["brands"] = typeof(BrandFetchStrategy),
            ["variants"] = typeof(VariantFetchStrategy),
            ["images"] = typeof(ImageFetchStrategy),
            ["modifiers"] = typeof(ModifierFetchStrategy)
        };
    }

    public IEntityFetchStrategy GetStrategy(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        }

        // Normalize entity type to handle case variations and pluralization
        var normalizedType = NormalizeEntityType(entityType);

        if (!_strategyMap.TryGetValue(normalizedType, out var strategyType))
        {
            throw new ArgumentException($"No fetch strategy found for entity type: {entityType}. " +
                $"Supported types: {string.Join(", ", _strategyMap.Keys)}", nameof(entityType));
        }

        // Resolve strategy from DI container
        var strategy = _serviceProvider.GetService(strategyType) as IEntityFetchStrategy;
        
        if (strategy == null)
        {
            throw new InvalidOperationException($"Failed to resolve fetch strategy for entity type: {entityType}. " +
                $"Ensure {strategyType.Name} is registered in the DI container.");
        }

        return strategy;
    }

    /// <summary>
    /// Normalizes entity type to handle various input formats
    /// Supports: categories, category, Categories, CATEGORIES, etc.
    /// </summary>
    private static string NormalizeEntityType(string entityType)
    {
        var normalized = entityType.Trim().ToLowerInvariant();
        
        // Handle singular/plural variations
        return normalized switch
        {
            "category" => "categories",
            "product" => "products", 
            "brand" => "brands",
            "variant" => "variants",
            "image" => "images",
            "modifier" => "modifiers",
            _ => normalized
        };
    }
} 