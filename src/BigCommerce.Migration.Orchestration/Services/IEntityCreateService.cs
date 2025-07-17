using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Service responsible for creating entities in destination stores
/// Implements Strategy Pattern: Uses entity-specific strategies for creation logic
/// Single Responsibility: Entity creation orchestration only
/// Open/Closed Principle: New entity types can be added without modifying this interface
/// </summary>
public interface IEntityCreateService
{
    /// <summary>
    /// Creates entities of the specified type in the destination store using Strategy Pattern
    /// Delegates to appropriate strategy based on entity type
    /// </summary>
    /// <param name="entities">Entities to create</param>
    /// <param name="request">Batch processing request containing entity type and context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created entities with destination IDs</returns>
    Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken);

    // 🎯 LEGACY METHODS REMOVED - Now handled by Strategy Pattern
    // All entity-specific creation methods have been replaced by strategies:
    // - CreateCategoriesAsync() → CategoryCreationStrategy
    // - CreateProductsAsync() → ProductCreationStrategy  
    // - CreateBrandsAsync() → BrandCreationStrategy
    // - CreateVariantsAsync() → VariantCreationStrategy
    // - CreateImagesAsync() → ImageCreationStrategy
    // - CreateModifiersAsync() → ModifierCreationStrategy
    // 
    // Benefits:
    // ✅ Open/Closed Principle: New entity types can be added as new strategies
    // ✅ Single Responsibility: Each strategy handles one entity type
    // ✅ Testability: Individual strategies can be tested in isolation
    // ✅ Maintainability: Entity-specific logic is encapsulated and organized

} 