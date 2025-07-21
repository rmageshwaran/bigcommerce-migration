using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Service responsible for transforming entities between source and destination formats
/// Single Responsibility: Entity transformation orchestration using strategy pattern
/// Open/Closed Principle: Closed for modification, open for extension via new strategies
/// </summary>
public interface IEntityTransformService
{
    /// <summary>
    /// Transforms a source entity to destination format using the appropriate strategy
    /// Delegates to entity-specific strategies based on entity type
    /// </summary>
    /// <param name="sourceEntity">Source entity data</param>
    /// <param name="request">Batch processing request containing entity type and migration context</param>
    /// <returns>Transformed entity data</returns>
    Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> sourceEntity, 
        BatchProcessingRequest request);
} 