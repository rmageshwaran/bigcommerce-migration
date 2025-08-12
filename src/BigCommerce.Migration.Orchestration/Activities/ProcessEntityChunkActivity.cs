using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for processing manageable chunks of entities to avoid Azure Functions timeout
/// Uses timeout-safe chunked processing for better performance and reliability
/// 
/// Design Principles:
/// - Processes max 500 entities (well under 10-minute timeout limit)
/// - Uses existing services with comprehensive error handling
/// - Maintains progress reporting capabilities
/// - Provides detailed logging for troubleshooting
/// </summary>
public class ProcessEntityChunkActivity
{
    private readonly ILogger<ProcessEntityChunkActivity> _logger;
    private readonly IEntityFetchService _entityFetchService;
    private readonly IEntityTransformService _entityTransformService;
    private readonly IEntityCreateService _entityCreateService;
    private readonly IEntityMappingService _entityMappingService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ISignalREventFactory _signalREventFactory; // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
    private readonly IProductComponentsMigrationPipeline _productComponentsPipeline; // 🔗 PHASE 2: Special pipeline for product components

    private readonly IUniversalMigrationProgressAggregator _universalAggregator; // ✅ P2-T1.5: Universal aggregator for Tier 2 progress

    public ProcessEntityChunkActivity(
        ILogger<ProcessEntityChunkActivity> logger,
        IEntityFetchService entityFetchService,
        IEntityTransformService entityTransformService,
        IEntityCreateService entityCreateService,
        IEntityMappingService entityMappingService,
        IEntityErrorHandlingService errorHandlingService,
        IProgressEventPublisher progressEventPublisher,
        ISignalREventFactory signalREventFactory,
        IProductComponentsMigrationPipeline productComponentsPipeline,

        IUniversalMigrationProgressAggregator universalAggregator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityFetchService = entityFetchService ?? throw new ArgumentNullException(nameof(entityFetchService));
        _entityTransformService = entityTransformService ?? throw new ArgumentNullException(nameof(entityTransformService));
        _entityCreateService = entityCreateService ?? throw new ArgumentNullException(nameof(entityCreateService));
        _entityMappingService = entityMappingService ?? throw new ArgumentNullException(nameof(entityMappingService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory)); // 🎯 CENTRALIZED SIGNALR: Factory injection
        _productComponentsPipeline = productComponentsPipeline ?? throw new ArgumentNullException(nameof(productComponentsPipeline)); // 🔗 PHASE 2: Special pipeline injection

        _universalAggregator = universalAggregator ?? throw new ArgumentNullException(nameof(universalAggregator)); // ✅ P2-T1.5: Universal aggregator injection
    }

    /// <summary>
    /// Processes a manageable chunk of entities (max 500) to stay within Azure Functions timeout limits
    /// </summary>
    /// <param name="request">Chunk processing request with entity details</param>
    /// <returns>Batch processing result for this chunk</returns>
    [Function("ProcessEntityChunk")]
    public async Task<BatchProcessingResult> ProcessEntityChunkAsync(
        [ActivityTrigger] ProcessEntityChunkRequest request)
    {
        var startTime = DateTime.UtcNow;
        var chunkId = $"chunk-{request.ChunkNumber}";
        
                _logger.LogInformation("🎯 [CHUNK-{ChunkNumber}] 🚀 STARTING: Processing {ChunkSize} {EntityType} entities " +
                                     "for migration {MigrationId} (BatchNumber={BatchNumber}, TotalChunks={TotalChunks}, StartIndex={StartIndex})",
            request.ChunkNumber, request.ChunkSize, request.EntityType, request.MigrationId, 
            request.ChunkNumber, request.TotalChunks, request.StartIndex);
            
        _logger.LogInformation("🎯 [CHUNK-{ChunkNumber}] 📋 CHUNK DETAILS: UseDirectPagination={UseDirectPagination}, " +
                              "EntityIds.Count={EntityIdsCount}, ChunkId=CHUNK-{ChunkNumber}-{EntityType}",
            request.ChunkNumber, request.UseDirectPagination, request.EntityIds?.Count ?? 0, 
            request.ChunkNumber, request.EntityType);

        try
        {
            // Validate chunk size to ensure timeout safety
            if (request.ChunkSize > 500)
            {
                _logger.LogWarning("⚠️ [CHUNK-{ChunkNumber}] Chunk size {ChunkSize} exceeds recommended limit of 500. " +
                                 "This may risk timeout issues.",
                    request.ChunkNumber, request.ChunkSize);
            }

            var result = new BatchProcessingResult
            {
                TotalProcessed = 0,
                SuccessfulEntities = 0,
                FailedEntities = 0,
                ProcessingTime = TimeSpan.Zero,
                Errors = new List<string>()
            };

            // Create a batch processing request for this chunk
            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                BatchNumber = request.ChunkNumber,
                TotalBatches = request.TotalChunks,
                EntityIds = request.EntityIds ?? new List<string>(),
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext,
                PaginationMetadata = request.PaginationMetadata,
                UseDirectPagination = request.UseDirectPagination
            };

            // Step 1: Fetch entities
            var entities = await FetchEntitiesForChunk(batchRequest);

            if (!entities.Any())
            {
                _logger.LogWarning("⚠️ [CHUNK-{ChunkNumber}] No entities found for chunk",
                    request.ChunkNumber);
                
                return new BatchProcessingResult
                {
                    TotalProcessed = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = 0,
                    ProcessingTime = DateTime.UtcNow - startTime,
                    Errors = new List<string>()
                };
            }

            // Step 2: Process entities using appropriate pipeline based on entity type
            _logger.LogInformation("📋 [CHUNK-{ChunkNumber}] Processing {EntityType} entities", 
                request.ChunkNumber, batchRequest.EntityType);

            // 🔗 PHASE 2 SPECIAL ROUTING: product-components requires component extraction and parallel processing
            if (batchRequest.EntityType.Equals("product-components", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🔗 [CHUNK-{ChunkNumber}] Routing to ProductComponentsMigrationPipeline for product-components", 
                    request.ChunkNumber);
                    
                result = await _productComponentsPipeline.ProcessProductComponentsAsync(
                    entities,
                    batchRequest.MigrationId,
                    batchRequest.SourceStore,
                    batchRequest.DestinationStore,
                    CancellationToken.None);
            }
            else
            {
                // Standard processing for products, brands, categories, variants, etc.
                result = await ProcessStandardEntitiesForChunk(entities, batchRequest, request.ChunkNumber);
            }
            
            // ✅ Update Universal Aggregator (Tier 2) with entity progress
            await _universalAggregator.UpdatePrimaryEntityProgressAsync(
                batchRequest.MigrationId,
                batchRequest.EntityType,
                new PrimaryEntityProgress
                {
                    EntityType = batchRequest.EntityType,
                    TotalCount = result.TotalProcessed, // Will be aggregated across chunks
                    ProcessedCount = result.TotalProcessed,
                    SuccessCount = result.SuccessfulEntities,
                    FailureCount = result.FailedEntities,
                    Status = result.SuccessfulEntities == result.TotalProcessed ? "completed" : "processing",
                    ThroughputPerSecond = result.TotalProcessed / Math.Max(result.ProcessingTime.TotalSeconds, 1),
                    ProcessingTime = result.ProcessingTime
                });

            var processingTime = DateTime.UtcNow - startTime;
            result.ProcessingTime = processingTime;

            _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Chunk processing completed: " +
                                 "{SuccessfulEntities}/{TotalProcessed} entities successful in {ProcessingTimeMs}ms " +
                                 "(avg: {AvgTimePerEntity:F1}ms per entity)",
                request.ChunkNumber, result.SuccessfulEntities, result.TotalProcessed, 
                processingTime.TotalMilliseconds,
                result.TotalProcessed > 0 ? processingTime.TotalMilliseconds / result.TotalProcessed : 0);

            // Step 3: Publish progress update for this chunk
            await PublishChunkProgress(request, result);

            return result;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            
            _logger.LogError(ex, "💥 [CHUNK-{ChunkNumber}] Chunk processing failed after {ProcessingTimeMs}ms: {ErrorMessage}",
                request.ChunkNumber, processingTime.TotalMilliseconds, ex.Message);

            return new BatchProcessingResult
            {
                TotalProcessed = request.ChunkSize,
                SuccessfulEntities = 0,
                FailedEntities = request.ChunkSize,
                ProcessingTime = processingTime,
                Errors = new List<string> { $"Chunk {request.ChunkNumber} failed: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Fetches entities for this chunk using the appropriate strategy
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchEntitiesForChunk(BatchProcessingRequest batchRequest)
    {
        try
        {
            _logger.LogInformation("📄 [CHUNK-{ChunkNumber}] Fetching entities for {EntityType}",
                batchRequest.BatchNumber, batchRequest.EntityType);

            var entities = await _entityFetchService.FetchEntitiesAsync(batchRequest, CancellationToken.None);

            _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Fetched {EntityCount} {EntityType} entities",
                batchRequest.BatchNumber, entities.Count, batchRequest.EntityType);

            return entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHUNK-{ChunkNumber}] Failed to fetch entities for {EntityType}: {ErrorMessage}",
                batchRequest.BatchNumber, batchRequest.EntityType, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Processes entities for this chunk using standard pipeline (transform + create + mapping)
    /// Used for brands, products, categories, variants - entities that don't require comprehensive sub-entity processing
    /// ✅ P2-T1.4: Renamed to distinguish from comprehensive pipeline processing
    /// </summary>
    private async Task<BatchProcessingResult> ProcessStandardEntitiesForChunk(
        List<Dictionary<string, object>> entities,
        BatchProcessingRequest batchRequest,
        int chunkNumber)
    {
        var result = new BatchProcessingResult
        {
            TotalProcessed = entities.Count,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            ProcessingTime = TimeSpan.Zero,
            Errors = new List<string>()
        };

        try
        {
            _logger.LogInformation("⚙️ [CHUNK-{ChunkNumber}] Processing {EntityCount} {EntityType} entities",
                chunkNumber, entities.Count, batchRequest.EntityType);

            // Transform all entities
            var transformedEntities = new List<Dictionary<string, object>>();
            
            foreach (var entity in entities)
            {
                try
                {
                    var transformedEntity = await _entityTransformService.TransformEntityAsync(entity, batchRequest);

                    if (transformedEntity != null)
                    {
                        transformedEntities.Add(transformedEntity);
                    }
                    else
                    {
                        result.FailedEntities++;
                        result.Errors.Add($"Failed to transform entity: transformation returned null");
                    }
                }
                catch (Exception ex)
                {
                    result.FailedEntities++;
                    result.Errors.Add($"Failed to transform entity: {ex.Message}");
                    
                    _logger.LogError(ex, "❌ [CHUNK-{ChunkNumber}] Failed to transform individual entity: {ErrorMessage}",
                        chunkNumber, ex.Message);
                }
            }

            // Create all transformed entities
            if (transformedEntities.Any())
            {
                var createdEntities = await _entityCreateService.CreateEntitiesAsync(
                    transformedEntities, 
                    batchRequest, 
                    CancellationToken.None);

                if (createdEntities != null && createdEntities.Any())
                {
                    result.SuccessfulEntities = createdEntities.Count;
                    
                    _logger.LogInformation("🔍 [CHUNK-{ChunkNumber}] Created {CreatedCount} entities successfully",
                        chunkNumber, createdEntities.Count);
                    
                    // Store mappings for successfully created entities
                    await StoreMappingsForCreatedEntities(entities, createdEntities, batchRequest, chunkNumber);
                }
                else
                {
                    result.FailedEntities += transformedEntities.Count;
                    result.Errors.Add("Entity creation service returned no results");
                }
            }

            // Update failed entities count (successful + failed should equal total)
            result.FailedEntities = result.TotalProcessed - result.SuccessfulEntities;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [CHUNK-{ChunkNumber}] Entity processing failed: {ErrorMessage}",
                chunkNumber, ex.Message);
            
            result.FailedEntities = entities.Count;
            result.SuccessfulEntities = 0;
            result.Errors.Add($"Entity processing failed: {ex.Message}");
            
            return result;
        }
    }

    /// <summary>
    /// Stores mappings for successfully created entities
    /// </summary>
    private async Task StoreMappingsForCreatedEntities(
        List<Dictionary<string, object>> sourceEntities,
        List<Dictionary<string, object>> createdEntities,
        BatchProcessingRequest batchRequest,
        int chunkNumber)
    {
        try
        {
            // Store mappings for successfully created entities  
            for (int i = 0; i < Math.Min(sourceEntities.Count, createdEntities.Count); i++)
            {
                try
                {
                    var sourceEntity = sourceEntities[i];
                    var createdEntity = createdEntities[i];
                    
                    // Extract source and destination IDs
                    string? sourceId = null;
                    if (batchRequest.EntityType.ToLowerInvariant() == "categories")
                    {
                        // Try category_id first (real categories), then fallback to id (placeholder entities)
                        sourceId = sourceEntity.GetValueOrDefault("category_id")?.ToString() 
                            ?? sourceEntity.GetValueOrDefault("id")?.ToString();
                    }
                    else
                    {
                        sourceId = sourceEntity.GetValueOrDefault("id")?.ToString();
                    }
                    
                    var destinationId = batchRequest.EntityType.ToLowerInvariant() == "categories"
                        ? createdEntity.GetValueOrDefault("category_id")?.ToString()
                        : createdEntity.GetValueOrDefault("id")?.ToString();
                                
                    if (!string.IsNullOrEmpty(sourceId) && !string.IsNullOrEmpty(destinationId))
                    {
                        // 🔗 SKIP OPTIONS: Options use hierarchical mapping via HierarchicalOptionMappingService
                        // Only create individual EntityMapping records for non-option entities
                        if (batchRequest.EntityType.ToLowerInvariant() != "options")
                        {
                            // Extract level from source entity for mapping differentiation
                            var processingLevel = sourceEntity.TryGetValue("_processing_level", out var level) ? (int)(level ?? 0) : 0;
                            var processingMode = sourceEntity.TryGetValue("_processing_mode", out var mode) ? mode?.ToString() : "Unknown";
                            
                            // Store level metadata for filtering mappings by level
                            var metadata = System.Text.Json.JsonSerializer.Serialize(new { 
                                Level = processingLevel,
                                ProcessingMode = processingMode
                            });

                            // 🔗 PRODUCT METADATA: Extract channels and related_products data from source entity
                            string? channelsData = null;
                            string? relatedProductsData = null;
                            if (batchRequest.EntityType.ToLowerInvariant() == "products")
                            {
                                // Extract channels data directly from source entity
                                if (sourceEntity.TryGetValue("channels", out var channelsValue))
                                {
                                    var serializedChannels = System.Text.Json.JsonSerializer.Serialize(channelsValue);
                                    if (serializedChannels != "\"[]\"" && serializedChannels != "null" && serializedChannels != "\"[-1]\"")
                                    {
                                        channelsData = serializedChannels;
                                        _logger.LogInformation("✅ [ENTITY-MAPPING] Extracted channels data for product {ProductId}: {ChannelsData}", sourceId, channelsData);
                                    }
                                    else
                                    {
                                        _logger.LogInformation("ℹ️ [ENTITY-MAPPING] Skipped empty/null channels array for product {ProductId} (value: {SerializedChannels})", sourceId, serializedChannels);
                                    }
                                }

                                // Extract related_products data directly from source entity
                                if (sourceEntity.TryGetValue("related_products", out var relatedProductsValue))
                                {
                                    var serializedRelatedProducts = System.Text.Json.JsonSerializer.Serialize(relatedProductsValue);

                                    if (serializedRelatedProducts != "\"[]\"" && serializedRelatedProducts != "null" && serializedRelatedProducts != "\"[-1]\"")
                                    {
                                        relatedProductsData = serializedRelatedProducts;
                                        _logger.LogInformation("✅ [ENTITY-MAPPING] Extracted related_products data for product {ProductId}: {RelatedProductsData}", sourceId, relatedProductsData);
                                    }
                                    else
                                    {
                                        _logger.LogInformation("ℹ️ [ENTITY-MAPPING] Skipped empty/null related_products array for product {ProductId} (value: {SerializedRelatedProducts})", sourceId, serializedRelatedProducts);
                                    }
                                }
                            }

                            var mapping = new EntityMapping
                            {
                                MigrationId = batchRequest.MigrationId,
                                EntityType = batchRequest.EntityType,
                                SourceStoreId = batchRequest.SourceStore.StoreId!,
                                DestinationStoreId = batchRequest.DestinationStore.StoreId!,
                                SourceId = sourceId,
                                DestinationId = destinationId,
                                Metadata = metadata, // ✅ Store level info for mapping differentiation
                                ChannelsData = channelsData, // 🔗 PHASE 4: Store channels data for product-channel assignment
                                RelatedProductsData = relatedProductsData, // 🔗 PHASE 6: Store related products data for related product updates
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            };

                            await _entityMappingService.StoreEntityMappingAsync(mapping, CancellationToken.None);
                        }
                        else
                        {
                            _logger.LogDebug("🔗 [MAPPING-SKIP] Skipping individual EntityMapping for options - using hierarchical mapping instead");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ [CHUNK-{ChunkNumber}] Failed to store entity mapping: {ErrorMessage}",
                        chunkNumber, ex.Message);
                    // Don't fail the whole chunk for mapping issues
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CHUNK-{ChunkNumber}] Failed to store mappings: {ErrorMessage}",
                chunkNumber, ex.Message);
            // Don't fail the chunk for mapping issues
        }
    }

    /// <summary>
    /// Publishes progress update for this chunk completion using centralized SignalR factory
    /// SOLID: Single Responsibility - focused on publishing chunk progress only
    /// </summary>
    private async Task PublishChunkProgress(ProcessEntityChunkRequest request, BatchProcessingResult result)
    {
        try
        {
            // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation with auto-populated base properties
            var progressEvent = _signalREventFactory.CreateEntityProgress(request.MigrationId, new EntityProgressOptions
            {
                EntityType = request.EntityType,
                TotalCount = request.ChunkSize,
                ProcessedCount = result.TotalProcessed,
                Status = result.SuccessfulEntities == result.TotalProcessed ? "completed" : "processing",
                ProcessingTime = result.ProcessingTime
                // ✅ Base properties (Timestamp, IsCancelled, HubMethod) auto-populated by factory
                // ✅ SuccessCount/FailureCount calculated from ProcessedCount internally by factory
                // ✅ Validation built-in
                // ✅ Consistent naming enforced
            });

            await _progressEventPublisher.PublishEntityProgressAsync(progressEvent);
            
            _logger.LogDebug("📊 [CHUNK-{ChunkNumber}] Published centralized SignalR progress update: {Successful}/{Total} entities",
                request.ChunkNumber, result.SuccessfulEntities, result.TotalProcessed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CHUNK-{ChunkNumber}] Failed to publish progress update: {ErrorMessage}",
                request.ChunkNumber, ex.Message);
            // Don't fail the chunk processing due to progress publishing issues
        }
    }
}