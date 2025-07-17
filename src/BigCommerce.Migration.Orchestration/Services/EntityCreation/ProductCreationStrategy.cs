using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating products in destination stores
/// Implements Open/Closed Principle: specific to products without modifying base service
/// </summary>
public class ProductCreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<ProductCreationStrategy> _logger;

    public ProductCreationStrategy(IBigCommerceApiClient apiClient, ILogger<ProductCreationStrategy> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // Log product details for debugging
            foreach (var product in entities)
            {
                var productName = product.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                var sku = product.TryGetValue("sku", out var skuValue) ? skuValue?.ToString() : "no-sku";
                
                _logger.LogDebug("📦 [PROD-{ExecutionId}] Creating product: Name='{ProductName}', SKU='{Sku}' in migration {MigrationId}",
                    executionId, productName, sku, migrationId);
            }

            // Create products using the API client
            var result = await _apiClient.CreateProductsAsync(destinationStore, entities, cancellationToken);

            _logger.LogInformation("✅ [PROD-{ExecutionId}] Successfully created {CreatedCount} products for migration {MigrationId}", 
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