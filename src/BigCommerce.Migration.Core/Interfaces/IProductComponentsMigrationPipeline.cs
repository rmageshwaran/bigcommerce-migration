using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for product components migration pipeline that handles Phase 2 product component migration
/// (options, modifiers, reviews) for existing products with parallel processing and progress aggregation.
/// </summary>
public interface IProductComponentsMigrationPipeline
{
    /// <summary>
    /// Processes product components (options, modifiers, reviews) for existing products
    /// Fetches products 10/page with comprehensive includes and creates only the component entities
    /// Implements dual-tier progress aggregation for real-time dashboard updates
    /// </summary>
    /// <param name="productsWithComponents">List of products with comprehensive include data (options, modifiers, reviews)</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="sourceStore">Source store configuration</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="requestedEntityType">The specific component type being processed (images, options, modifiers, reviews)</param>
    /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
    /// <returns>Comprehensive batch processing result with component statistics</returns>
    Task<BatchProcessingResult> ProcessProductComponentsAsync(
        List<Dictionary<string, object>> productsWithComponents,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        string requestedEntityType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Estimates the processing time for a batch of product components
    /// Used for timeout management and progress estimation
    /// </summary>
    /// <param name="productCount">Number of products to process components for</param>
    /// <param name="hasComponents">Whether products have components to process</param>
    /// <returns>Estimated processing time in minutes</returns>
    TimeSpan EstimateProcessingTime(int productCount, bool hasComponents);
}