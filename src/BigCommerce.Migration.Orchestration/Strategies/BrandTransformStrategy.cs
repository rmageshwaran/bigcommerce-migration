using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Transform strategy for brand entities
/// Handles required field validation and null value cleanup
/// </summary>
public class BrandTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<BrandTransformStrategy> _logger;

    public string EntityType => "brands";

    public BrandTransformStrategy(ILogger<BrandTransformStrategy> logger)
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
        _logger.LogDebug("Transforming brand for migration {MigrationId}", migrationId);
        
        var transformed = new Dictionary<string, object>(entity);

        // Ensure required fields
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Brand";
            _logger.LogWarning("Brand missing name field, using default for migration {MigrationId}", migrationId);
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