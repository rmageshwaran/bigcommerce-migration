using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Orchestration.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private readonly IApiRequestHandler _apiRequestHandler;

    public ProductCreationStrategy(
        IBigCommerceApiClient apiClient, 
        ILogger<ProductCreationStrategy> logger,
        ISubBatchConfigurationService configService,
        ISubBatchProcessor subBatchProcessor,
        IApiRequestHandler apiRequestHandler)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _subBatchProcessor = subBatchProcessor ?? throw new ArgumentNullException(nameof(subBatchProcessor));
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
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

            // Individual product processor function (mirrors BrandCreationStrategy approach)
            async Task<Dictionary<string, object>?> IndividualProductProcessor(Dictionary<string, object> product, CancellationToken ct)
            {
                try
                {
                    var productName = product.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                    var productId = product.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
                    var sku = product.TryGetValue("sku", out var skuValue) ? skuValue?.ToString() : "no-sku";
                    var originalId = product.TryGetValue("_original_entity_id", out var origId) ? origId?.ToString() : "unknown";
                    
                    _logger.LogInformation("🔍 [PROD-{ExecutionId}] PROCESSING: Product='{ProductName}', SKU='{Sku}', SourceId={SourceId}, OriginalId={OriginalId} (INDIVIDUAL) in migration {MigrationId}",
                        executionId, productName, sku, productId, originalId, migrationId);

                    var createdProduct = await CreateSingleProductAsync(destinationStore, product, ct);
                    
                    if (createdProduct != null)
                    {
                        _logger.LogDebug("✅ [PROD-{ExecutionId}] Successfully created product '{ProductName}' (INDIVIDUAL) in migration {MigrationId}",
                            executionId, productName, migrationId);
                    }
                    
                    return createdProduct;
                }
                catch (Exception ex)
                {
                    var productName = product.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
                    var productId = product.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
                    var sku = product.TryGetValue("sku", out var skuValue) ? skuValue?.ToString() : "no-sku";
                    
                    _logger.LogError(ex, "🛒 [PROD-{ExecutionId}] ❌ Failed to create product '{ProductName}' (ID: {ProductId}, SKU: {Sku}) (INDIVIDUAL) in migration {MigrationId}: {ErrorMessage}",
                        executionId, productName, productId, sku, migrationId, ex.Message);

                    // Return null for failed products - sub-batch processor will filter them out
                    return null;
                }
            }

            // Process products using individual processor (like brands)
            var result = await _subBatchProcessor.ProcessIndividuallyInSubBatchesAsync(
                entities, config, IndividualProductProcessor, "products", migrationId, cancellationToken);

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

    /// <summary>
    /// Creates a single product in the destination store using direct API calls
    /// Mirrors the approach used in BrandCreationStrategy.CreateSingleBrandAsync
    /// </summary>
    /// <param name="storeConfig">Destination store configuration</param>
    /// <param name="product">Product data to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created product data or null if creation failed</returns>
    private async Task<Dictionary<string, object>?> CreateSingleProductAsync(
        StoreConfiguration storeConfig, 
        Dictionary<string, object> product, 
        CancellationToken cancellationToken)
    {
        var debugId = Guid.NewGuid().ToString("N")[..8];
        var threadId = Thread.CurrentThread.ManagedThreadId;
        var productName = product.TryGetValue("name", out var name) ? name?.ToString() : "unknown";
        var sku = product.TryGetValue("sku", out var skuValue) ? skuValue?.ToString() : "no-sku";
        var chunkNumber = product.TryGetValue("_chunk_number", out var chunk) ? chunk?.ToString() : "unknown";
        var apiPage = product.TryGetValue("_api_page", out var page) ? page?.ToString() : "unknown";
        
        _logger.LogInformation("🔍 [PROD-CREATE-{DebugId}] API PREPARATION: Will call BigCommerce API to create product '{ProductName}' (SKU: {Sku}) on thread {ThreadId}", 
            debugId, productName, sku, threadId);

        // Remove fields that shouldn't be sent to the API (like source IDs)
        var cleanProduct = new Dictionary<string, object>(product);
        cleanProduct.Remove("id"); // Remove source ID
        cleanProduct.Remove("_original_entity_id"); // Remove tracking fields
        cleanProduct.Remove("_chunk_number"); // Remove metadata
        cleanProduct.Remove("_api_page"); // Remove metadata
        cleanProduct.Remove("_migration_id"); // Remove metadata

        // Ensure required fields are present and valid according to BigCommerce API
        if (!cleanProduct.ContainsKey("name") || string.IsNullOrWhiteSpace(cleanProduct["name"]?.ToString()))
        {
            throw new ArgumentException($"Product name is required for creation. SKU: {sku}");
        }

        // Build the API request URL
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products";

        // Serialize the product data
        var jsonContent = JsonSerializer.Serialize(cleanProduct, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        _logger.LogInformation("🚨 [PROD-CREATE-{DebugId}] 🌐 API CALL: URL={Url}, ProductName='{ProductName}', SKU='{Sku}', ThreadId={ThreadId}", 
            debugId, url, productName, sku, threadId);
        
        _logger.LogDebug("🔍 [PROD-CREATE-{DebugId}] REQUEST DETAILS: Method=POST, ContentType=application/json, PayloadSize={PayloadSize} bytes", 
            debugId, jsonContent.Length);

        // Create and execute the API request
        var request = ApiRequest.CreatePost(url, jsonContent, storeConfig);
        
        try
        {
            var apiStartTime = DateTime.UtcNow;
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            var apiDuration = DateTime.UtcNow - apiStartTime;
            
            _logger.LogInformation("🚨 [PROD-CREATE-{DebugId}] ✅ API SUCCESS: Product='{ProductName}', SKU='{Sku}', CHUNK={ChunkNumber}, " +
                                  "API_PAGE={ApiPage}, Duration={Duration}ms, ThreadId={ThreadId}", 
                debugId, productName, sku, chunkNumber, apiPage, apiDuration.TotalMilliseconds, threadId);
            
            _logger.LogDebug("🔍 [PROD-CREATE-{DebugId}] TIMING: API call completed in {Duration}ms for product '{ProductName}' on thread {ThreadId}", 
                debugId, apiDuration.TotalMilliseconds, productName, threadId);

            // Handle the response data 
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var createdProduct = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText());
                if (createdProduct != null)
                {
                    var createdId = createdProduct.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                    _logger.LogDebug("✅ [PROD-CREATE-{DebugId}] Successfully created product '{ProductName}' (SKU: {Sku}) with ID {ProductId}",
                        debugId, productName, sku, createdId);
                    return createdProduct;
                }
            }
            else if (response != null && response.ContainsKey("id"))
            {
                // Handle direct response format
                var createdId = response.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                _logger.LogDebug("✅ [PROD-CREATE-{DebugId}] Successfully created product '{ProductName}' (SKU: {Sku}) with ID {ProductId}",
                    debugId, productName, sku, createdId);
                return response;
            }

            _logger.LogWarning("⚠️ [PROD-CREATE-{DebugId}] Unexpected API response format for product '{ProductName}' (SKU: {Sku})",
                debugId, productName, sku);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PROD-CREATE-{DebugId}] Failed to create product '{ProductName}' (SKU: {Sku}): {ErrorMessage}",
                debugId, productName, sku, ex.Message);
            throw;
        }
    }
} 