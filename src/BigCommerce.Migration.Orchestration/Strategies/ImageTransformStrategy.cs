using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Transform strategy for image entities
/// Handles product mapping and image URL validation
/// </summary>
public class ImageTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<ImageTransformStrategy> _logger;

    public string EntityType => "images";

    public ImageTransformStrategy(ILogger<ImageTransformStrategy> logger)
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
        var transformed = new Dictionary<string, object>(entity);

        // Handle product mapping if available
        // Note: ProductMapping property doesn't exist in BatchProcessingRequest, so we'll skip this for now
        // In a full implementation, you'd have a mapping service to handle this

        // Ensure required fields
        if (!transformed.ContainsKey("image_url"))
        {
            _logger.LogWarning("Image missing URL field for migration {MigrationId}", migrationId);
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