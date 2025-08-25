using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Strategies.Creation;

/// <summary>
/// Creation strategy for product-related entities
/// Updates existing products with related_products data via BigCommerce catalog API
/// Unlike other creation strategies, this UPDATES existing products rather than creating new entities
/// Uses SubBatchProcessor for parallel processing like VariantCreationStrategy
/// </summary>
public class ProductRelatedCreationStrategy : IEntityCreationStrategy
{
    /// <summary>
    /// Entity type this strategy handles
    /// </summary>
    public string EntityType => "product-related";

    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly ISubBatchProcessor _subBatchProcessor;
    private readonly ISubBatchConfigurationService _configService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<ProductRelatedCreationStrategy> _logger;

    public ProductRelatedCreationStrategy(
        IApiRequestHandler apiRequestHandler,
        ISubBatchProcessor subBatchProcessor,
        ISubBatchConfigurationService configService,
        IEntityErrorHandlingService errorHandlingService,
        ICancellationStore cancellationStore,
        ILogger<ProductRelatedCreationStrategy> logger)
    {
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _subBatchProcessor = subBatchProcessor ?? throw new ArgumentNullException(nameof(subBatchProcessor));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
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
            _logger.LogWarning("🚀 No product-related entities provided for update in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        _logger.LogInformation("🚀 Creating {Count} product-related updates using optimized parallel processing for migration {MigrationId}", 
            entities.Count, migrationId);

        try
        {
            // 🚫 CANCELLATION: Check before starting processing
            await CheckCancellationAsync(migrationId);

            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Get configuration for product-related updates
            var config = _configService.GetConfiguration("product-related");
            
            // Batch processor function for product updates (similar to VariantCreationStrategy)
            async Task<List<Dictionary<string, object>>> BatchProductUpdateProcessor(
                List<Dictionary<string, object>> batchEntities, CancellationToken ct)
            {
                var batchId = Guid.NewGuid().ToString("N")[..8]; // Short batch ID for tracking
                var batchStartTime = DateTime.UtcNow;
                
                _logger.LogInformation("🎯 [BATCH-{BatchId}] Starting processing of {Count} product updates at {StartTime} for migration {MigrationId}", 
                    batchId, batchEntities.Count, batchStartTime.ToString("HH:mm:ss.fff"), migrationId);
                
                try
                {
                    // 🚫 CANCELLATION: Check before processing batch
                    await CheckCancellationAsync(migrationId);
                    
                    // ✅ ENHANCED: Track skipped entities during payload building
                    var skippedEntities = new List<Dictionary<string, object>>();
                    var batchUpdatePayload = BuildBatchUpdatePayloadWithSkipTracking(batchEntities, skippedEntities);
                    
                    if (!batchUpdatePayload.Any())
                    {
                        _logger.LogInformation("🚫 [ALL-SKIPPED] All {Count} products in batch were skipped - no API call needed", 
                            batchEntities.Count);
                        
                        // Return results representing all skipped entities
                        return skippedEntities;
                    }
                    
                    // ✅ CORRECT API: BigCommerce batch product update 
                    var requestPayload = JsonSerializer.Serialize(batchUpdatePayload);
                    var url = $"{destinationStore.GetApiBaseUrl()}/catalog/products";
                    
                    _logger.LogDebug("📤 [BATCH-{BatchId}] PUT {Url} with {Count} products payload: {Payload}", 
                        batchId, url, batchUpdatePayload.Count, requestPayload);
                    
                    var request = ApiRequest.CreatePut(url, requestPayload, destinationStore);
                    var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, ct);
                    
                    _logger.LogInformation("✅ [BATCH-{BatchId}] Successfully updated {Count} products with related_products using batch API", 
                        batchId, batchUpdatePayload.Count);
                    
                    // Return success results for all entities that were processed
                    return batchEntities.Select((entity, index) => new Dictionary<string, object>
                    {
                        ["status"] = "success",
                        ["DestinationId"] = entity.GetValueOrDefault("DestinationId")?.ToString() ?? "",
                        ["SourceId"] = entity.GetValueOrDefault("SourceId")?.ToString() ?? "",
                        ["updated_at"] = DateTime.UtcNow.ToString("O"),
                        ["source_index"] = index
                    }).ToList();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🚫 [BATCH-{BatchId}] Product-related batch processing cancelled", batchId);
                    throw; // Re-throw cancellation exceptions to propagate cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to update batch of {Count} products", batchEntities.Count);
                    
                    // Enhanced error handling: Categorize and log with structured data
                    await HandleBatchErrorAsync(ex, batchEntities, migrationId, destinationStore, requestPayload: null, responsePayload: null, ct);
                    
                    // Continue-on-error policy: return empty list to continue with other batches
                    return new List<Dictionary<string, object>>();
                }
            }

            // Use SubBatchProcessor: Similar to variants (e.g., 250 products → 5 concurrent batches of 50)
            var result = await _subBatchProcessor.ProcessInSubBatchesAsync(
                entities, config, BatchProductUpdateProcessor, "product-related", migrationId, cancellationToken);

            _logger.LogInformation("✅ Successfully processed {ProcessedCount}/{TotalCount} product-related updates for migration {MigrationId}", 
                result?.Count ?? 0, entities.Count, migrationId);

            // Add detailed creation summary
            var updatedCount = result?.Count ?? 0;
            var skippedCount = entities.Count - updatedCount;
            _logger.LogInformation("🎯 [PRODUCT-RELATED-PIPELINE] ===== PRODUCT-RELATED UPDATE COMPLETED ===== " +
                                  "MigrationId: {MigrationId}, InputProducts: {InputCount}, UpdatedProducts: {UpdatedCount}, " +
                                  "SkippedProducts: {SkippedCount}, SuccessRate: {SuccessRate:P1}",
                migrationId, entities.Count, updatedCount, skippedCount, 
                entities.Count > 0 ? (double)updatedCount / entities.Count : 0);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🚫 [PRODUCT-RELATED-CANCEL] Product-related creation cancelled for migration {MigrationId}", migrationId);
            throw; // Re-throw cancellation exceptions to propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update {EntityCount} products with related_products data in migration {MigrationId}", 
                entities.Count, migrationId);
            
            throw;
        }
    }

    /// <summary>
    /// Builds batch update payload for BigCommerce products API
    /// Converts transformed entities to BigCommerce batch update format
    /// </summary>
    private List<Dictionary<string, object>> BuildBatchUpdatePayload(List<Dictionary<string, object>> entities)
    {
        var batchPayload = new List<Dictionary<string, object>>();
        
        foreach (var entity in entities)
        {
            // ✅ EXPLICIT SKIP HANDLING: Check for _skip marker first
            if (entity.ContainsKey("_skip") && entity["_skip"] is bool skipValue && skipValue)
            {
                var skipReason = entity.GetValueOrDefault("_skipReason")?.ToString() ?? "unknown";
                var skipSourceId = entity.GetValueOrDefault("SourceId")?.ToString();
                var skipDestinationId = entity.GetValueOrDefault("DestinationId")?.ToString();
                
                _logger.LogDebug("🚫 [BATCH-PAYLOAD] Skipping entity marked with _skip: SourceId={SourceId}, DestinationId={DestinationId}, Reason={Reason}", 
                    skipSourceId, skipDestinationId, skipReason);
                continue;
            }

            var destinationId = entity.GetValueOrDefault("DestinationId")?.ToString();
            var sourceId = entity.GetValueOrDefault("SourceId")?.ToString();
            
            if (string.IsNullOrWhiteSpace(destinationId))
            {
                _logger.LogWarning("🔶 [BATCH-PAYLOAD] Skipping entity with missing DestinationId. SourceId: {SourceId}", sourceId);
                continue;
            }

            // Extract related_products from transformed entity
            var relatedProductsData = entity.GetValueOrDefault("related_products");
            
            _logger.LogDebug("🔍 [BATCH-PAYLOAD-DEBUG] Product {DestinationId}: RelatedProductsData type={Type}, value={Value}", 
                destinationId, 
                relatedProductsData?.GetType()?.Name ?? "null",
                relatedProductsData?.ToString() ?? "null");
            
            if (relatedProductsData == null)
            {
                _logger.LogDebug("🔶 [BATCH-PAYLOAD] Skipping product {DestinationId} - no related_products data", destinationId);
                continue;
            }

            // Build product update item for batch payload
            var productUpdate = new Dictionary<string, object>
            {
                ["id"] = int.Parse(destinationId), // BigCommerce requires numeric ID
                ["related_products"] = relatedProductsData
            };

            batchPayload.Add(productUpdate);
            
            _logger.LogDebug("📝 [BATCH-PAYLOAD] Added product {DestinationId} to batch: related_products={RelatedProducts}", 
                destinationId, JsonSerializer.Serialize(relatedProductsData));
        }
        
        _logger.LogInformation("📦 [BATCH-PAYLOAD] Built batch payload with {Count} product updates", batchPayload.Count);
        
        return batchPayload;
    }

    /// <summary>
    /// Builds batch update payload while tracking skipped entities for proper counting
    /// Enhanced version that returns both the payload and skipped entities
    /// </summary>
    private List<Dictionary<string, object>> BuildBatchUpdatePayloadWithSkipTracking(
        List<Dictionary<string, object>> entities, 
        List<Dictionary<string, object>> skippedEntities)
    {
        var batchPayload = new List<Dictionary<string, object>>();
        
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            
            // ✅ EXPLICIT SKIP HANDLING: Check for _skip marker first
            if (entity.ContainsKey("_skip") && entity["_skip"] is bool skipValue && skipValue)
            {
                var skipReason = entity.GetValueOrDefault("_skipReason")?.ToString() ?? "unknown";
                var sourceId = entity.GetValueOrDefault("SourceId")?.ToString();
                var destinationId = entity.GetValueOrDefault("DestinationId")?.ToString();
                
                _logger.LogDebug("🚫 [BATCH-PAYLOAD] Skipping entity marked with _skip: SourceId={SourceId}, DestinationId={DestinationId}, Reason={Reason}", 
                    sourceId, destinationId, skipReason);
                
                // Add to skipped entities collection
                skippedEntities.Add(new Dictionary<string, object>
                {
                    ["status"] = "skipped",
                    ["reason"] = skipReason,
                    ["SourceId"] = sourceId ?? "",
                    ["DestinationId"] = destinationId ?? "",
                    ["source_index"] = i
                });
                continue;
            }

            var entityDestinationId = entity.GetValueOrDefault("DestinationId")?.ToString();
            var entitySourceId = entity.GetValueOrDefault("SourceId")?.ToString();
            
            if (string.IsNullOrWhiteSpace(entityDestinationId))
            {
                _logger.LogWarning("🔶 [BATCH-PAYLOAD] Skipping entity with missing DestinationId. SourceId: {SourceId}", entitySourceId);
                
                // Add to skipped entities collection
                skippedEntities.Add(new Dictionary<string, object>
                {
                    ["status"] = "skipped",
                    ["reason"] = "missing_destination_id",
                    ["SourceId"] = entitySourceId ?? "",
                    ["DestinationId"] = "",
                    ["source_index"] = i
                });
                continue;
            }

            // Extract related_products from transformed entity
            var relatedProductsData = entity.GetValueOrDefault("related_products");
            
            if (relatedProductsData == null)
            {
                _logger.LogDebug("🔶 [BATCH-PAYLOAD] Skipping product {DestinationId} - no related_products data", entityDestinationId);
                
                // Add to skipped entities collection
                skippedEntities.Add(new Dictionary<string, object>
                {
                    ["status"] = "skipped",
                    ["reason"] = "no_related_products_data",
                    ["SourceId"] = entitySourceId ?? "",
                    ["DestinationId"] = entityDestinationId,
                    ["source_index"] = i
                });
                continue;
            }

            // Build product update item for batch payload
            var productUpdate = new Dictionary<string, object>
            {
                ["id"] = int.Parse(entityDestinationId), // BigCommerce requires numeric ID
                ["related_products"] = relatedProductsData
            };

            batchPayload.Add(productUpdate);
            
            _logger.LogDebug("📝 [BATCH-PAYLOAD] Added product {DestinationId} to batch: related_products={RelatedProducts}", 
                entityDestinationId, JsonSerializer.Serialize(relatedProductsData));
        }
        
        _logger.LogInformation("📦 [BATCH-PAYLOAD] Built batch payload: {PayloadCount} updates + {SkippedCount} skipped", 
            batchPayload.Count, skippedEntities.Count);
        
        return batchPayload;
    }

    /// <summary>
    /// Handles batch processing errors with detailed logging and error categorization
    /// </summary>
    private async Task HandleBatchErrorAsync(
        Exception exception,
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        string? requestPayload,
        string? responsePayload,
        CancellationToken cancellationToken)
    {
        // Log detailed error information
        _logger.LogError(exception, "❌ [ERROR-HANDLER] Batch error for {Count} product updates in migration {MigrationId}. " +
                                   "Error: {ErrorMessage}", 
            entities.Count, migrationId, exception.Message);

        // Log errors for individual entities using the error handling service
        foreach (var entity in entities)
        {
            try
            {
                var entityId = entity.GetValueOrDefault("DestinationId")?.ToString() ?? 
                              entity.GetValueOrDefault("SourceId")?.ToString() ?? "unknown";
                
                await _errorHandlingService.LogEntityErrorAsync(
                    exception,
                    entity,
                    new BatchProcessingRequest
                    {
                        MigrationId = migrationId,
                        EntityType = "product-related",
                        SourceStore = new StoreConfiguration(), // Not needed for error logging
                        DestinationStore = destinationStore
                    },
                    entityId,
                    responsePayload,
                    "Product-related update batch processing failed",
                    cancellationToken);
            }
            catch (Exception logError)
            {
                _logger.LogError(logError, "❌ [ERROR-HANDLER] Failed to log error for entity in migration {MigrationId}", migrationId);
            }
        }
    }

    /// <summary>
    /// Validates that the destination store configuration has required properties
    /// </summary>
    private static void ValidateStoreConfiguration(StoreConfiguration destinationStore)
    {
        if (destinationStore == null)
        {
            throw new ArgumentNullException(nameof(destinationStore), "Destination store configuration is required");
        }

        if (string.IsNullOrWhiteSpace(destinationStore.AccessToken))
        {
            throw new InvalidOperationException("Destination store access token is required for product updates");
        }

        if (string.IsNullOrWhiteSpace(destinationStore.StoreId))
        {
            throw new InvalidOperationException("Destination store ID is required for product updates");
        }
    }

    /// <summary>
    /// Checks cancellation status and throws OperationCanceledException if migration is cancelled
    /// Uses cooperative cancellation pattern suitable for Azure Functions activities
    /// </summary>
    private async Task CheckCancellationAsync(string migrationId)
    {
        try
        {
            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            if (isCancelled)
            {
                var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                _logger.LogInformation("🚫 [CANCELLATION] Product-related creation cancelled: {Reason}", reason);
                throw new OperationCanceledException($"Migration cancelled: {reason}");
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CANCELLATION-CHECK] Failed to check cancellation flag for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't throw - continue processing if cancellation check fails
        }
    }
}