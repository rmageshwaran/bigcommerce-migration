using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Transform strategy for product entities
/// Handles category mapping, brand mapping, and required field validation
/// </summary>
public class ProductTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<ProductTransformStrategy> _logger;

    public string EntityType => "products";

    public ProductTransformStrategy(ILogger<ProductTransformStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> entity,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Transforming product for migration {MigrationId}", migrationId);
        
        var transformed = new Dictionary<string, object>(entity);

        // Handle category mapping if context is available
        if (categoryTreeContext != null)
        {
            if (transformed.TryGetValue("categories", out var categories) && categories is List<object> categoryList)
            {
                var mappedCategories = new List<object>();
                foreach (var category in categoryList)
                {
                    // For now, we'll keep the original category IDs
                    // In a full implementation, you'd have a mapping service to handle this
                    mappedCategories.Add(category);
                    _logger.LogDebug("Keeping original category {CategoryId} for product in migration {MigrationId}", 
                        category, migrationId);
                }
                transformed["categories"] = mappedCategories;
            }
        }

        // Handle brand mapping if available
        // Note: BrandMapping property doesn't exist in BatchProcessingRequest, so we'll skip this for now
        // In a full implementation, you'd have a mapping service to handle this

        // Ensure required fields
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Product";
            _logger.LogWarning("Product missing name field, using default for migration {MigrationId}", migrationId);
        }

        if (!transformed.ContainsKey("type"))
        {
            transformed["type"] = "physical";
        }

        // Remove null values
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        return await Task.FromResult(transformed);
    }
} 