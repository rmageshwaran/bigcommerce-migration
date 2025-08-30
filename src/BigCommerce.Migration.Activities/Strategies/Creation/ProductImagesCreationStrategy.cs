using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Creation;

/// <summary>
/// Creation strategy for product-images entities - Phase 4 of product migration
/// 
/// Updates existing products with image data via individual product API calls.
/// Unlike other creation strategies, this UPDATES existing products rather than creating new entities.
/// Processes products individually with chunked image updates for memory efficiency.
/// 
/// Key Features:
/// - Individual product processing (subBatchSize: 1, maxConcurrency: 5)
/// - Chunked image updates (max 50 images per API call)
/// - Streaming image fetch to prevent memory exhaustion
/// - Continue-on-error policy for individual image failures
/// - Proper cancellation support throughout processing
/// - Virtual path URL construction for image reliability
/// 
/// Architecture:
/// - Uses SubBatchProcessor for parallel product processing
/// - Integrates ProductImagesFetchService for streaming image retrieval
/// - Uses ProductImagesTransformStrategy for image transformation
/// - Makes individual PUT /v3/catalog/products/{product_id} calls
/// - Implements proper error tracking and logging to OpenSearch
/// </summary>
public class ProductImagesCreationStrategy : IEntityCreationStrategy
{
    /// <summary>
    /// Entity type this strategy handles
    /// </summary>
    public string EntityType => "product-images";

    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly ISubBatchProcessor _subBatchProcessor;
    private readonly ISubBatchConfigurationService _configService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly ICancellationStore _cancellationStore;
    private readonly IProductImagesFetchService _imagesFetchService;
    private readonly IEntityTransformStrategyFactory _transformStrategyFactory;
    private readonly ILogger<ProductImagesCreationStrategy> _logger;

    public ProductImagesCreationStrategy(
        IApiRequestHandler apiRequestHandler,
        ISubBatchProcessor subBatchProcessor,
        ISubBatchConfigurationService configService,
        IEntityErrorHandlingService errorHandlingService,
        ICancellationStore cancellationStore,
        IProductImagesFetchService imagesFetchService,
        IEntityTransformStrategyFactory transformStrategyFactory,
        ILogger<ProductImagesCreationStrategy> logger)
    {
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _subBatchProcessor = subBatchProcessor ?? throw new ArgumentNullException(nameof(subBatchProcessor));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _imagesFetchService = imagesFetchService ?? throw new ArgumentNullException(nameof(imagesFetchService));
        _transformStrategyFactory = transformStrategyFactory ?? throw new ArgumentNullException(nameof(transformStrategyFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("🖼️ No product-images entities provided for update in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        _logger.LogInformation("🖼️ [PRODUCT-IMAGES-CREATE] ===== STARTING PRODUCT-IMAGES CREATION ===== Migration: {MigrationId}, Products: {ProductCount}", 
            migrationId, entities.Count);
        
        _logger.LogInformation("🔍 [PRODUCT-IMAGES-DEBUG] First entity sample keys: [{Keys}]", 
            entities.FirstOrDefault()?.Keys != null ? string.Join(", ", entities.First().Keys) : "NO_ENTITIES");
        
        _logger.LogInformation("🖼️ [PRODUCT-IMAGES-CREATE] Starting image updates for {Count} products using individual processing for migration {MigrationId}", 
            entities.Count, migrationId);

        try
        {
            // 🚫 CANCELLATION: Check before starting processing
            await CheckCancellationAsync(migrationId);

            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Get configuration for product-images updates
            var config = _configService.GetConfiguration("product-images");
            
            // Individual product processor function for image updates
            async Task<List<Dictionary<string, object>>> BatchProductImageProcessor(
                List<Dictionary<string, object>> batchProducts, CancellationToken ct)
            {
                var batchId = Guid.NewGuid().ToString("N")[..8]; // Short batch ID for tracking
                var batchStartTime = DateTime.UtcNow;
                
                _logger.LogInformation("🎯 [BATCH-{BatchId}] Starting image processing for {Count} products at {StartTime} for migration {MigrationId}", 
                    batchId, batchProducts.Count, batchStartTime, migrationId);
                
                        _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: First product entity keys in batch: [{Keys}]",
            batchId, batchProducts.FirstOrDefault()?.Keys != null ? string.Join(", ", batchProducts.First().Keys) : "NO_PRODUCTS");

        // 🔍 DEBUG: Log actual field values for first product
        var firstProduct = batchProducts.FirstOrDefault();
        if (firstProduct != null)
        {
            _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: First product field values - SourceId: '{SourceId}', DestinationId: '{DestinationId}', EntityType: '{EntityType}'",
                batchId,
                firstProduct.GetValueOrDefault("SourceId") ?? firstProduct.GetValueOrDefault("source_id") ?? "MISSING",
                firstProduct.GetValueOrDefault("DestinationId") ?? firstProduct.GetValueOrDefault("destination_id") ?? "MISSING",
                firstProduct.GetValueOrDefault("EntityType") ?? "MISSING");
        }

                var batchResults = new List<Dictionary<string, object>>();
                var batchErrors = new List<string>();
                var productCount = 0;
                var totalImageCount = 0;
                var successfulImageCount = 0;
                var skippedImageCount = 0;
                var skippedProductCount = 0;

                foreach (var productEntity in batchProducts)
                {
                    try
                    {
                        productCount++;
                        
                        // 🚫 CANCELLATION: Check periodically during processing
                        await CheckCancellationAsync(migrationId);

                        // ✅ EXPLICIT SKIP HANDLING: Check for _skip marker first (similar to ProductRelatedCreationStrategy)
                        if (productEntity.ContainsKey("_skip") && productEntity["_skip"] is bool skipValue && skipValue)
                        {
                            var skipReason = productEntity.GetValueOrDefault("_skipReason")?.ToString() ?? "unknown";
                            var sourceId = productEntity.GetValueOrDefault("source_id")?.ToString();
                            var destinationId = productEntity.GetValueOrDefault("destination_id")?.ToString();
                            
                            _logger.LogDebug("🚫 [BATCH-{BatchId}] Skipping product marked with _skip: SourceId={SourceId}, DestinationId={DestinationId}, Reason={Reason}", 
                                batchId, sourceId, destinationId, skipReason);
                            
                            skippedProductCount++;
                            
                            // Add to results with skip status
                            batchResults.Add(new Dictionary<string, object>
                            {
                                ["id"] = destinationId ?? "",
                                ["source_id"] = sourceId ?? "",
                                ["destination_id"] = destinationId ?? "",
                                ["status"] = "skipped",
                                ["reason"] = skipReason,
                                ["images_processed"] = 0,
                                ["images_successful"] = 0,
                                ["images_skipped"] = 0
                            });
                            
                            continue;
                        }

                        // Process individual product images
                        var productImageResults = await ProcessSingleProductImagesAsync(
                            productEntity, migrationId, destinationStore, batchId, ct);

                        if (productImageResults != null && productImageResults.Any())
                        {
                            // 🎯 PROGRESS TRACKING FIX: ProcessSingleProductImagesAsync now returns List<Dictionary> for individual images
                            batchResults.AddRange(productImageResults);
                            
                            // Track image counts from individual image results
                            foreach (var imageResult in productImageResults)
                            {
                                var status = imageResult.GetValueOrDefault("status")?.ToString();
                                var type = imageResult.GetValueOrDefault("type")?.ToString();
                                
                                if (type == "image")
                                {
                                    totalImageCount++;
                                    if (status == "success")
                                    {
                                        successfulImageCount++;
                                    }
                                    else if (status == "skipped")
                                    {
                                        skippedImageCount++;
                                    }
                                }
                                else if (status == "skipped_no_images")
                                {
                                    skippedProductCount++;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorMessage = $"Product {productEntity.GetValueOrDefault("id", "unknown")} failed: {ex.Message}";
                        batchErrors.Add(errorMessage);
                        
                        _logger.LogError(ex, "❌ [BATCH-{BatchId}] Product image processing failed for product {ProductId} in migration {MigrationId}: {Error}", 
                            batchId, productEntity.GetValueOrDefault("id", "unknown"), migrationId, ex.Message);

                        // Continue processing other products (continue-on-error policy)
                        continue;
                    }
                }

                var batchDuration = DateTime.UtcNow - batchStartTime;
                _logger.LogInformation("✅ [BATCH-{BatchId}] Completed image processing for {ProcessedCount}/{TotalCount} products in {Duration}ms. " +
                    "Products: {SkippedProducts} skipped, Images: {SuccessfulImages}/{TotalImages} successful, {SkippedImages} skipped (migration: {MigrationId})", 
                    batchId, batchResults.Count, batchProducts.Count, batchDuration.TotalMilliseconds,
                    skippedProductCount, successfulImageCount, totalImageCount, skippedImageCount, migrationId);

                if (batchErrors.Any())
                {
                    _logger.LogWarning("⚠️ [BATCH-{BatchId}] {ErrorCount} product failures in batch: {Errors} (migration: {MigrationId})", 
                        batchId, batchErrors.Count, string.Join("; ", batchErrors.Take(3)), migrationId);
                }

                return batchResults;
            }

            // Use SubBatchProcessor for individual product processing (subBatchSize: 1, maxConcurrency: 5)
            var results = await _subBatchProcessor.ProcessInSubBatchesAsync(
                entities, 
                config,
                BatchProductImageProcessor,
                "product-images",
                migrationId,
                cancellationToken);

            // 🎯 PROGRESS TRACKING FIX: Count individual images from the new result format
            var totalImages = results?.Count(r => r.GetValueOrDefault("type")?.ToString() == "image") ?? 0;
            var successfulImages = results?.Count(r => r.GetValueOrDefault("type")?.ToString() == "image" && 
                                                       r.GetValueOrDefault("status")?.ToString() == "success") ?? 0;
            var skippedImages = results?.Count(r => r.GetValueOrDefault("type")?.ToString() == "image" && 
                                                    r.GetValueOrDefault("status")?.ToString() == "skipped") ?? 0;
            var skippedProducts = results?.Count(r => r.GetValueOrDefault("status")?.ToString() == "skipped" || 
                                                      r.GetValueOrDefault("status")?.ToString() == "skipped_no_images") ?? 0;
            var processedProducts = entities.Count - skippedProducts;

            _logger.LogInformation("🎉 [PRODUCT-IMAGES-CREATE] ===== PRODUCT-IMAGES UPDATE COMPLETED ===== " +
                                  "MigrationId: {MigrationId}, InputProducts: {InputCount}, ProcessedProducts: {ProcessedCount}, " +
                                  "SkippedProducts: {SkippedCount}, Images: {SuccessfulImages}/{TotalImages} successful, {SkippedImages} skipped, " +
                                  "SuccessRate: {SuccessRate:P1} for migration {MigrationId}", 
                migrationId, entities.Count, processedProducts, skippedProducts, successfulImages, totalImages, skippedImages,
                entities.Count > 0 ? (double)processedProducts / entities.Count : 0, migrationId);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [PRODUCT-IMAGES-CREATE] Critical failure during product image creation for migration {MigrationId}: {Error}", migrationId, ex.Message);
            
            // Log error to OpenSearch for monitoring
            var errorRequest = new BatchProcessingRequest { MigrationId = migrationId, EntityType = "product-images" };
            await _errorHandlingService.LogStructuredMigrationErrorAsync(ex, entities, errorRequest, "creation_strategy_failure", cancellationToken);
            
            throw;
        }
    }

    /// <summary>
    /// Processes images for a single product with chunked updates
    /// </summary>
    private async Task<List<Dictionary<string, object>>?> ProcessSingleProductImagesAsync(
        Dictionary<string, object> productEntity,
        string migrationId,
        StoreConfiguration destinationStore,
        string batchId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: Entity keys received: [{Keys}] (migration: {MigrationId})",
            batchId, string.Join(", ", productEntity.Keys), migrationId);

        // 🔍 DEBUG: Log all entity values
        foreach (var kvp in productEntity)
        {
            _logger.LogDebug("🔍 [BATCH-{BatchId}] DEBUG: Entity field '{Key}' = '{Value}' (migration: {MigrationId})",
                batchId, kvp.Key, kvp.Value ?? "NULL", migrationId);
        }
        
        // Extract product information from EntityMappings format
        var sourceProductId = productEntity.GetValueOrDefault("SourceId")?.ToString() ?? 
                             productEntity.GetValueOrDefault("source_id")?.ToString();
        var destinationProductId = productEntity.GetValueOrDefault("DestinationId")?.ToString() ?? 
                                   productEntity.GetValueOrDefault("destination_id")?.ToString();
        
        _logger.LogInformation("🔍 [BATCH-{BatchId}] FIELD EXTRACTION: SourceId='{SourceId}', DestinationId='{DestinationId}' (migration: {MigrationId})", 
            batchId, sourceProductId ?? "NULL", destinationProductId ?? "NULL", migrationId);

        if (string.IsNullOrWhiteSpace(sourceProductId) || string.IsNullOrWhiteSpace(destinationProductId))
        {
            _logger.LogWarning("🚨 [BATCH-{BatchId}] SKIP REASON 1: Missing product IDs - SourceId='{SourceId}', DestinationId='{DestinationId}' (migration: {MigrationId})", 
                batchId, sourceProductId ?? "NULL", destinationProductId ?? "NULL", migrationId);
            return new List<Dictionary<string, object>>();
        }

        _logger.LogDebug("🔄 [BATCH-{BatchId}] Processing images for product {SourceId} → {DestinationId} (migration: {MigrationId})", 
            batchId, sourceProductId, destinationProductId, migrationId);

        var config = _configService.GetConfiguration("product-images");
        var imageChunkSize = config.CustomSettings.TryGetValue("imageChunkSize", out var chunkSizeObj) ? Convert.ToInt32(chunkSizeObj) : 50;
        var imageChunkDelayMs = config.CustomSettings.TryGetValue("imageChunkDelayMs", out var delayObj) ? Convert.ToInt32(delayObj) : 300;
        
        var totalImagesProcessed = 0;
        var successfulImages = 0;
        var skippedImages = 0;
        var errors = new List<string>();

        try
        {
            // Use streaming fetch to get product images without loading all into memory
            var imageChunks = new List<List<Dictionary<string, object>>>();
            var currentChunk = new List<Dictionary<string, object>>();

            // Get source store configuration from entity data (added by EntityCreateService)
            var sourceStoreId = productEntity.GetValueOrDefault("_source_store_id")?.ToString();
            var sourceStoreToken = productEntity.GetValueOrDefault("_source_store_token")?.ToString();
            var sourceStoreChannelId = productEntity.GetValueOrDefault("_source_store_channel_id")?.ToString();
            
            if (string.IsNullOrWhiteSpace(sourceStoreId) || string.IsNullOrWhiteSpace(sourceStoreToken))
            {
                _logger.LogError("🚨 [BATCH-{BatchId}] SKIP REASON 2: Missing source store config - StoreId='{StoreId}', Token='{Token}', ChannelId='{ChannelId}' (migration: {MigrationId})", 
                    batchId, sourceStoreId ?? "NULL", string.IsNullOrWhiteSpace(sourceStoreToken) ? "NULL" : "***", sourceStoreChannelId ?? "NULL", migrationId);
                return null;
            }

            var sourceStoreConfig = new StoreConfiguration 
            { 
                StoreId = sourceStoreId,
                AccessToken = sourceStoreToken,
                ChannelId = sourceStoreChannelId ?? "1" // Default to "1" if not specified
            };

            _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: About to call FetchProductImagesStreamingAsync for product {SourceProductId} (migration: {MigrationId})",
                batchId, sourceProductId, migrationId);

            await _imagesFetchService.FetchProductImagesStreamingAsync(
                sourceProductId,
                sourceStoreConfig,
                migrationId,
                (imagesBatch) =>
                {
                    _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: Received imagesBatch with {Count} images for product {SourceProductId} (migration: {MigrationId})",
                        batchId, imagesBatch.Count, sourceProductId, migrationId);

                    foreach (var image in imagesBatch)
                    {
                        _logger.LogDebug("🔍 [BATCH-{BatchId}] DEBUG: Adding image to chunk - Image fields: [{Fields}] (migration: {MigrationId})",
                            batchId, string.Join(", ", image.Keys), migrationId);

                        currentChunk.Add(image);
                        
                        // Split into chunks of imageChunkSize (default: 50)
                        if (currentChunk.Count >= imageChunkSize)
                        {
                            imageChunks.Add(new List<Dictionary<string, object>>(currentChunk));
                            currentChunk.Clear();
                        }
                    }
                    return Task.CompletedTask;
                });

            // Add remaining images as final chunk
            if (currentChunk.Any())
            {
                _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: Adding final chunk with {Count} images (migration: {MigrationId})",
                    batchId, currentChunk.Count, migrationId);
                imageChunks.Add(currentChunk);
            }

            _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: After image fetching - imageChunks.Count={ChunkCount}, totalImages={TotalImages} for product {DestinationId} (migration: {MigrationId})",
                batchId, imageChunks.Count, imageChunks.Sum(c => c.Count), destinationProductId, migrationId);

            if (!imageChunks.Any())
            {
                _logger.LogInformation("📋 [BATCH-{BatchId}] Product {DestinationId} has no images to migrate, skipping (migration: {MigrationId})", 
                    batchId, destinationProductId, migrationId);
                
                return new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = $"{destinationProductId}_no_images",
                        ["product_id"] = destinationProductId,
                        ["source_product_id"] = sourceProductId,
                        ["status"] = "skipped_no_images",
                        ["type"] = "product",
                        ["reason"] = "No images found for this product"
                    }
                };
            }

            _logger.LogInformation("📸 [BATCH-{BatchId}] Processing {TotalImages} images in {ChunkCount} chunks for product {DestinationId} (migration: {MigrationId})", 
                batchId, imageChunks.Sum(c => c.Count), imageChunks.Count, destinationProductId, migrationId);

            _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: About to enter chunk processing loop - imageChunks.Count={ChunkCount} (migration: {MigrationId})",
                batchId, imageChunks.Count, migrationId);

            // Process each chunk sequentially to avoid overwhelming the API
            for (int chunkIndex = 0; chunkIndex < imageChunks.Count; chunkIndex++)
            {
                _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: Entered chunk processing loop - chunkIndex={ChunkIndex}/{TotalChunks} (migration: {MigrationId})",
                    batchId, chunkIndex, imageChunks.Count, migrationId);

                var chunk = imageChunks[chunkIndex];
                totalImagesProcessed += chunk.Count;
                
                _logger.LogDebug("🔍 [BATCH-{BatchId}] Processing chunk {ChunkIndex}/{TotalChunks} with {ChunkSize} images for product {DestinationId} (migration: {MigrationId})", 
                    batchId, chunkIndex + 1, imageChunks.Count, chunk.Count, destinationProductId, migrationId);

                Dictionary<string, object>? transformedPayload = null;
                try
                {
                    // 🚫 CANCELLATION: Check before each chunk
                    await CheckCancellationAsync(migrationId);

                    // Transform images for this chunk
                    var transformData = new Dictionary<string, object>
                    {
                        ["productId"] = destinationProductId,
                        ["images"] = chunk
                    };

                    _logger.LogInformation("🔄 [BATCH-{BatchId}] DEBUG: Calling transform with {ImageCount} images for product {DestinationId} (migration: {MigrationId})", 
                        batchId, chunk.Count, destinationProductId, migrationId);

                    var sourceStore = new StoreConfiguration { 
                        StoreId = productEntity.GetValueOrDefault("_source_store_id")?.ToString() ?? "",
                        AccessToken = productEntity.GetValueOrDefault("_source_store_token")?.ToString() ?? "",
                        ChannelId = productEntity.GetValueOrDefault("_source_store_channel_id")?.ToString() ?? "1"
                    };
                    var transformStrategy = _transformStrategyFactory.GetStrategy("product-images");
                    _logger.LogInformation("🔍 [BATCH-{BatchId}] DEBUG: About to call transform strategy - Type: {StrategyType} (migration: {MigrationId})",
                        batchId, transformStrategy.GetType().Name, migrationId);

                    transformedPayload = await transformStrategy.TransformEntityAsync(transformData, migrationId, sourceStore, destinationStore, null, cancellationToken);
                    
                    _logger.LogInformation("🔄 [BATCH-{BatchId}] DEBUG: Transform returned: {IsNull} for product {DestinationId} (migration: {MigrationId})", 
                        batchId, transformedPayload == null ? "NULL" : "PAYLOAD", destinationProductId, migrationId);

                    if (transformedPayload != null && transformedPayload.ContainsKey("images"))
                    {
                        // Extract only the images array for the BigCommerce API payload
                        var apiPayload = new Dictionary<string, object>
                        {
                            ["images"] = transformedPayload["images"]
                        };
                        
                        // Make individual product update API call
                        var url = $"{destinationStore.GetApiBaseUrl()}/catalog/products/{destinationProductId}";
                        var requestPayload = JsonSerializer.Serialize(apiPayload);
                        var request = ApiRequest.CreatePut(url, requestPayload, destinationStore);
                        
                        _logger.LogInformation("🚀 [BATCH-{BatchId}] API Request: PUT {Url} with {ImageCount} images for product {DestinationId} (migration: {MigrationId})", 
                            batchId, url, chunk.Count, destinationProductId, migrationId);
                            
                        var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);

                        // Successful API call (no exception thrown)
                        successfulImages += chunk.Count;
                        _logger.LogInformation("✅ [BATCH-{BatchId}] Chunk {ChunkIndex}/{TotalChunks} ({ImageCount} images) updated successfully for product {DestinationId} (migration: {MigrationId})", 
                            batchId, chunkIndex + 1, imageChunks.Count, chunk.Count, destinationProductId, migrationId);
                    }
                    else
                    {
                        var errorMsg = $"Chunk {chunkIndex + 1} transformation failed";
                        errors.Add(errorMsg);
                        skippedImages += chunk.Count;
                        
                        _logger.LogWarning("⚠️ [BATCH-{BatchId}] {ErrorMessage} for product {DestinationId} (migration: {MigrationId})", 
                            batchId, errorMsg, destinationProductId, migrationId);
                    }

                    // Implement chunk delay (default: 300ms) for rate limiting
                    if (chunkIndex < imageChunks.Count - 1 && imageChunkDelayMs > 0)
                    {
                        await Task.Delay(imageChunkDelayMs, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Chunk {chunkIndex + 1} processing failed: {ex.Message}";
                    errors.Add(errorMsg);
                    skippedImages += chunk.Count;
                    
                    _logger.LogError(ex, "❌ [BATCH-{BatchId}] DEBUG: EXCEPTION in chunk processing - {ErrorMessage} for product {DestinationId} (migration: {MigrationId})", 
                        batchId, errorMsg, destinationProductId, migrationId);

                    // Store the actual API payload for error analysis (not the EntityMappings data)
                    try 
                    {
                        var errorPayload = new Dictionary<string, object>
                        {
                            ["destination_product_id"] = destinationProductId,
                            ["chunk_index"] = chunkIndex + 1,
                            ["chunk_size"] = chunk.Count,
                            ["error_message"] = ex.Message,
                            ["api_url"] = $"{destinationStore.GetApiBaseUrl()}/catalog/products/{destinationProductId}",
                            ["raw_images"] = chunk, // The raw images that were being processed
                            ["api_payload"] = transformedPayload != null && transformedPayload.ContainsKey("images") 
                                ? new Dictionary<string, object> { ["images"] = transformedPayload["images"] }
                                : "TRANSFORM_FAILED" // The actual API payload that was sent (or failed to create)
                        };
                        
                        var errorRequest = new BatchProcessingRequest { MigrationId = migrationId, EntityType = "product-images" };
                        var apiException = new Exception($"API call failed for product {destinationProductId}, chunk {chunkIndex + 1}: {ex.Message}");
                        await _errorHandlingService.LogStructuredMigrationErrorAsync(apiException, new List<Dictionary<string, object>> { errorPayload }, errorRequest, "image_update_failures", cancellationToken);
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogWarning(logEx, "Failed to log API error payload for product {DestinationId} (migration: {MigrationId})", destinationProductId, migrationId);
                    }

                    // Continue with next chunk (continue-on-error policy)
                    continue;
                }
            }

            // Errors are now logged individually in the catch blocks above

            // 🎯 PROGRESS TRACKING FIX: Return individual image results instead of product-level results
            // This ensures the dashboard counts actual images migrated, not products processed
            var imageResults = new List<Dictionary<string, object>>();
            
            // Create individual results for successful images
            for (int i = 0; i < successfulImages; i++)
            {
                imageResults.Add(new Dictionary<string, object>
                {
                    ["id"] = $"{destinationProductId}_image_{i + 1}",
                    ["product_id"] = destinationProductId,
                    ["source_product_id"] = sourceProductId,
                    ["status"] = "success",
                    ["type"] = "image"
                });
            }
            
            // Create individual results for skipped images  
            for (int i = 0; i < skippedImages; i++)
            {
                imageResults.Add(new Dictionary<string, object>
                {
                    ["id"] = $"{destinationProductId}_image_skipped_{i + 1}",
                    ["product_id"] = destinationProductId,
                    ["source_product_id"] = sourceProductId,
                    ["status"] = "skipped",
                    ["type"] = "image"
                });
            }
            
            // If no images were processed, return a single product-level result for tracking
            if (imageResults.Count == 0)
            {
                imageResults.Add(new Dictionary<string, object>
                {
                    ["id"] = $"{destinationProductId}_no_images",
                    ["product_id"] = destinationProductId,
                    ["source_product_id"] = sourceProductId,
                    ["status"] = "skipped_no_images",
                    ["type"] = "product",
                    ["reason"] = "No images found for this product"
                });
            }
            
            _logger.LogDebug("📊 [BATCH-{BatchId}] Returning {ResultCount} individual image results for product {DestinationId} (migration: {MigrationId})", 
                batchId, imageResults.Count, destinationProductId, migrationId);
            
            return imageResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [BATCH-{BatchId}] Critical failure processing images for product {DestinationId} in migration {MigrationId}: {Error}", 
                batchId, destinationProductId, migrationId, ex.Message);

            // Log error to OpenSearch
            var errorRequest = new BatchProcessingRequest { MigrationId = migrationId, EntityType = "product-images" };
            await _errorHandlingService.LogStructuredMigrationErrorAsync(ex, new List<Dictionary<string, object>> { productEntity }, errorRequest, "product_processing_failure", cancellationToken);

            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["id"] = $"{destinationProductId}_error",
                    ["product_id"] = destinationProductId,
                    ["source_product_id"] = sourceProductId,
                    ["status"] = "failed",
                    ["type"] = "product",
                    ["error"] = ex.Message
                }
            };
        }
    }

    /// <summary>
    /// Validates store configuration for API requests
    /// </summary>
    private void ValidateStoreConfiguration(StoreConfiguration store)
    {
        if (store?.IsValid() != true)
        {
            throw new ArgumentException("Invalid destination store configuration", nameof(store));
        }
    }

    /// <summary>
    /// Checks if migration has been cancelled and throws if so
    /// </summary>
    private async Task CheckCancellationAsync(string migrationId)
    {
        if (await _cancellationStore.CheckCancellationFlagAsync(migrationId))
        {
            _logger.LogWarning("🚫 [PRODUCT-IMAGES-CREATE] Migration {MigrationId} has been cancelled, stopping processing", migrationId);
            throw new OperationCanceledException($"Migration {migrationId} was cancelled");
        }
    }
}
