using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.Orchestration.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating products in destination stores
/// Implements Open/Closed Principle: specific to products without modifying base service
/// </summary>
public class ProductCreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<ProductCreationStrategy> _logger;
    private readonly ISubBatchConfigurationService _configService;
    private readonly ISubBatchProcessor _subBatchProcessor;

    public ProductCreationStrategy(
        IBigCommerceApiClient apiClient, 
        ILogger<ProductCreationStrategy> logger,
        ISubBatchConfigurationService configService,
        ISubBatchProcessor subBatchProcessor)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _subBatchProcessor = subBatchProcessor ?? throw new ArgumentNullException(nameof(subBatchProcessor));
    }

    /// <summary>
    /// The entity type this strategy handles
    /// </summary>
    public string EntityType => "products";

    /// <summary>
    /// Creates products in the destination store
    /// </summary>
    /// <param name="entities">Products to create</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (optional for products)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created products with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No products provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        var executionId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("📦 [PROD-{ExecutionId}] Creating {Count} products for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        try
        {
            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // 🚀 SUB-BATCH PROCESSING: Use configured sub-batching for optimal rate limiting
            var config = _configService.GetConfiguration("products");
            
            _logger.LogInformation("🚀 [PROD-{ExecutionId}] Starting SUB-BATCH creation of {ProductCount} products " +
                                 "using sub-batches of {SubBatchSize} with max {MaxConcurrency} concurrent for migration {MigrationId}", 
                executionId, entities.Count, config.SubBatchSize, config.MaxConcurrency, migrationId);

            // Sub-batch processor function that calls API client for each sub-batch
            async Task<List<Dictionary<string, object>>> SubBatchProductProcessor(List<Dictionary<string, object>> subBatch, CancellationToken ct)
            {
                try
                {
                    _logger.LogDebug("📦 [PROD-{ExecutionId}] Processing sub-batch of {SubBatchSize} products (SUB-BATCH) in migration {MigrationId}",
                        executionId, subBatch.Count, migrationId);

                    // Log product details for debugging
                    foreach (var product in subBatch)
                    {
                        var productName = product.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                        var sku = product.TryGetValue("sku", out var skuValue) ? skuValue?.ToString() : "no-sku";
                        
                        _logger.LogDebug("📦 [PROD-{ExecutionId}] Sub-batch product: Name='{ProductName}', SKU='{Sku}' in migration {MigrationId}",
                            executionId, productName, sku, migrationId);
                    }

                    // Create sub-batch using the API client
                    var subBatchResult = await _apiClient.CreateProductsAsync(destinationStore, subBatch, ct);

                    _logger.LogDebug("✅ [PROD-{ExecutionId}] Sub-batch completed: {CreatedCount}/{SubBatchSize} products created for migration {MigrationId}", 
                        executionId, subBatchResult?.Count ?? 0, subBatch.Count, migrationId);

                    return subBatchResult ?? new List<Dictionary<string, object>>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [PROD-{ExecutionId}] Sub-batch failed for {SubBatchSize} products: {ErrorMessage}",
                        executionId, subBatch.Count, ex.Message);
                    
                    // Return empty list for failed sub-batch to continue with other sub-batches
                    return new List<Dictionary<string, object>>();
                }
            }

            // Process products using sub-batch processor
            var result = await _subBatchProcessor.ProcessInSubBatchesAsync(
                entities, config, SubBatchProductProcessor, "products", migrationId, cancellationToken);

            _logger.LogInformation("✅ [PROD-{ExecutionId}] SUB-BATCH processing completed: {CreatedCount} products created successfully for migration {MigrationId}", 
                executionId, result?.Count ?? 0, migrationId);

            return result;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "📦 [PROD-{ExecutionId}] ❌ Failed to create {ProductCount} products in migration {MigrationId}. {DetailedError}",
                executionId, entities.Count, migrationId, detailedErrorMessage);

            // Return empty list to avoid causing Durable Functions replay issues
            _logger.LogWarning("📦 [PROD-{ExecutionId}] Returning empty result to prevent replay in migration {MigrationId}", 
                executionId, migrationId);
            
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Validates store configuration
    /// </summary>
    /// <param name="storeConfig">Store configuration to validate</param>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    private static void ValidateStoreConfiguration(StoreConfiguration storeConfig)
    {
        if (storeConfig == null || !storeConfig.IsValid())
            throw new ArgumentException("Invalid destination store configuration");
    }

    /// <summary>
    /// Extracts detailed error information from API exceptions
    /// </summary>
    /// <param name="exception">The exception to analyze</param>
    /// <returns>Detailed error message if available, otherwise empty string</returns>
    private static string ExtractDetailedErrorMessage(Exception exception)
    {
        if (exception == null) return string.Empty;

        // Check for inner exceptions with detailed error messages
        var innerException = exception.InnerException;
        while (innerException != null)
        {
            if (!string.IsNullOrEmpty(innerException.Message) && 
                innerException.Message != exception.Message)
            {
                return innerException.Message;
            }
            innerException = innerException.InnerException;
        }

        return exception.Message;
    }
} 