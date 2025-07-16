using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Service responsible for transforming entities between source and destination formats
/// Single Responsibility: Entity transformation only
/// </summary>
public interface IEntityTransformService
{
    /// <summary>
    /// Transforms a source entity to destination format
    /// </summary>
    /// <param name="sourceEntity">Source entity data</param>
    /// <param name="request">Batch processing request</param>
    /// <returns>Transformed entity data</returns>
    Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> sourceEntity, 
        BatchProcessingRequest request);

    /// <summary>
    /// Transforms category entities with hierarchical structure
    /// </summary>
    Task<Dictionary<string, object>> TransformCategoryAsync(
        Dictionary<string, object> category, 
        BatchProcessingRequest request);

    /// <summary>
    /// Transforms product entities with variant relationships
    /// </summary>
    Task<Dictionary<string, object>> TransformProductAsync(
        Dictionary<string, object> product, 
        BatchProcessingRequest request);

    /// <summary>
    /// Transforms brand entities
    /// </summary>
    Task<Dictionary<string, object>> TransformBrandAsync(
        Dictionary<string, object> brand, 
        BatchProcessingRequest request);

    /// <summary>
    /// Transforms variant entities
    /// </summary>
    Task<Dictionary<string, object>> TransformVariantAsync(
        Dictionary<string, object> variant, 
        BatchProcessingRequest request);

    /// <summary>
    /// Transforms image entities
    /// </summary>
    Task<Dictionary<string, object>> TransformImageAsync(
        Dictionary<string, object> image, 
        BatchProcessingRequest request);

    /// <summary>
    /// Transforms modifier entities
    /// </summary>
    Task<Dictionary<string, object>> TransformModifierAsync(
        Dictionary<string, object> modifier, 
        BatchProcessingRequest request);
} 