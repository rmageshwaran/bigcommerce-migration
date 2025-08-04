using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for processing manageable chunks of entities to avoid Azure Functions timeout
/// Uses timeout-safe chunked processing for better performance and reliability
/// 
/// Design Principles:
/// - Processes max 500 entities (well under 10-minute timeout limit)
/// - Uses existing parallel processing infrastructure but with safe limits
/// - Maintains all error handling and progress reporting capabilities
/// - Provides detailed logging for troubleshooting
/// </summary>
public class ProcessEntityChunkActivity
{
    private readonly ILogger<ProcessEntityChunkActivity> _logger;
    private readonly IEnhancedParallelProcessor _parallelProcessor;
    private readonly IEntityFetchService _entityFetchService;
    private readonly IEntityTransformService _entityTransformService;
    private readonly IEntityCreateService _entityCreateService;
    private readonly IEntityMappingService _entityMappingService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly IProgressEventPublisher _progressEventPublisher;

    public ProcessEntityChunkActivity(
        ILogger<ProcessEntityChunkActivity> logger,
        IEnhancedParallelProcessor parallelProcessor,
        IEntityFetchService entityFetchService,
        IEntityTransformService entityTransformService,
        IEntityCreateService entityCreateService,
        IEntityMappingService entityMappingService,
        IEntityErrorHandlingService errorHandlingService,
        IProgressEventPublisher progressEventPublisher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parallelProcessor = parallelProcessor ?? throw new ArgumentNullException(nameof(parallelProcessor));
        _entityFetchService = entityFetchService ?? throw new ArgumentNullException(nameof(entityFetchService));
        _entityTransformService = entityTransformService ?? throw new ArgumentNullException(nameof(entityTransformService));
        _entityCreateService = entityCreateService ?? throw new ArgumentNullException(nameof(entityCreateService));
        _entityMappingService = entityMappingService ?? throw new ArgumentNullException(nameof(entityMappingService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
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
        
        _logger.LogInformation("🎯 [CHUNK-{ChunkNumber}] Starting chunk processing: {ChunkSize} {EntityType} entities " +
                             "for migration {MigrationId} (timeout-safe design)",
            request.ChunkNumber, request.ChunkSize, request.EntityType, request.MigrationId);

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

            // Determine processing approach based on pagination settings
            if (request.UseDirectPagination)
            {
                result = await ProcessChunkWithDirectPagination(request);
            }
            else
            {
                result = await ProcessChunkWithEntityIds(request);
            }

            var processingTime = DateTime.UtcNow - startTime;
            result.ProcessingTime = processingTime;

            _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Chunk processing completed: " +
                                 "{SuccessfulEntities}/{TotalProcessed} entities successful in {ProcessingTimeMs}ms " +
                                 "(avg: {AvgTimePerEntity:F1}ms per entity)",
                request.ChunkNumber, result.SuccessfulEntities, result.TotalProcessed, 
                processingTime.TotalMilliseconds,
                result.TotalProcessed > 0 ? processingTime.TotalMilliseconds / result.TotalProcessed : 0);

            // Publish progress update for this chunk
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
    /// Processes chunk using direct pagination (for new approach)
    /// </summary>
    private async Task<BatchProcessingResult> ProcessChunkWithDirectPagination(ProcessEntityChunkRequest request)
    {
        _logger.LogInformation("📄 [CHUNK-{ChunkNumber}] Using direct pagination approach for {EntityType}",
            request.ChunkNumber, request.EntityType);

        // Create a batch processing request for this chunk
        var batchRequest = new BatchProcessingRequest
        {
            MigrationId = request.MigrationId,
            EntityType = request.EntityType,
            BatchNumber = request.ChunkNumber,
            TotalBatches = request.TotalChunks,
            EntityIds = request.EntityIds ?? new List<string>(), // Use actual EntityIds from request (including placeholders)
            SourceStore = request.SourceStore,
            DestinationStore = request.DestinationStore,
            CategoryTreeContext = request.CategoryTreeContext,
            PaginationMetadata = request.PaginationMetadata,
            UseDirectPagination = request.UseDirectPagination
        };

        try
        {
            // Use the existing fetch service with the batch request
            var entities = await _entityFetchService.FetchEntitiesAsync(batchRequest, CancellationToken.None);

            if (!entities.Any())
            {
                _logger.LogWarning("⚠️ [CHUNK-{ChunkNumber}] No entities found for chunk",
                    request.ChunkNumber);
                
                return new BatchProcessingResult
                {
                    TotalProcessed = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = 0,
                    ProcessingTime = TimeSpan.Zero,
                    Errors = new List<string>()
                };
            }

            // Process the fetched entities
            return await ProcessEntitiesBatch(request, entities, batchRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHUNK-{ChunkNumber}] Direct pagination processing failed: {ErrorMessage}",
                request.ChunkNumber, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Processes chunk using specific entity IDs (for traditional approach)
    /// </summary>
    private async Task<BatchProcessingResult> ProcessChunkWithEntityIds(ProcessEntityChunkRequest request)
    {
        _logger.LogInformation("🔢 [CHUNK-{ChunkNumber}] Using entity IDs approach for {EntityType} " +
                             "({EntityIdCount} entity IDs)",
            request.ChunkNumber, request.EntityType, request.EntityIds.Count);

        if (!request.EntityIds.Any())
        {
            _logger.LogWarning("⚠️ [CHUNK-{ChunkNumber}] No entity IDs provided for processing",
                request.ChunkNumber);
            
            return new BatchProcessingResult
            {
                TotalProcessed = 0,
                SuccessfulEntities = 0,
                FailedEntities = 0,
                ProcessingTime = TimeSpan.Zero,
                Errors = new List<string> { "No entity IDs provided for chunk processing" }
            };
        }

        try
        {
            // Create a batch processing request for this chunk with specific entity IDs
            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                BatchNumber = request.ChunkNumber,
                TotalBatches = request.TotalChunks,
                EntityIds = request.EntityIds,
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext,
                PaginationMetadata = request.PaginationMetadata,
                UseDirectPagination = false // Using entity IDs, not pagination
            };

            // Use the existing fetch service with the batch request
            var entities = await _entityFetchService.FetchEntitiesAsync(batchRequest, CancellationToken.None);

            if (!entities.Any())
            {
                _logger.LogWarning("⚠️ [CHUNK-{ChunkNumber}] No entities could be fetched from {EntityIdCount} IDs",
                    request.ChunkNumber, request.EntityIds.Count);
                
                return new BatchProcessingResult
                {
                    TotalProcessed = request.EntityIds.Count,
                    SuccessfulEntities = 0,
                    FailedEntities = request.EntityIds.Count,
                    ProcessingTime = TimeSpan.Zero,
                    Errors = new List<string> { "No entities could be fetched from provided IDs" }
                };
            }

            // Process the fetched entities
            return await ProcessEntitiesBatch(request, entities, batchRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHUNK-{ChunkNumber}] Entity IDs processing failed: {ErrorMessage}",
                request.ChunkNumber, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 🚀 PERFORMANCE FIX: Processes entities using parallel processor for proper rate limiting
    /// </summary>
    private async Task<BatchProcessingResult> ProcessEntitiesBatch(
        ProcessEntityChunkRequest request, 
        List<Dictionary<string, object>> entities,
        BatchProcessingRequest batchRequest)
    {
        _logger.LogInformation("⚙️ [CHUNK-{ChunkNumber}] Processing {EntityCount} {EntityType} entities through PARALLEL pipeline (FIXED)",
            request.ChunkNumber, entities.Count, request.EntityType);

        try
        {
            // 🚀 PERFORMANCE FIX: Use EnhancedParallelProcessor instead of sequential processing
            // This will register parallel context for proper rate limiting (no more 2000ms delays!)
            var parallelConfig = new ParallelProcessingConfiguration
            {
                EntityType = request.EntityType,
                StoreId = request.DestinationStore.StoreId,
                MaxConcurrentBatches = Math.Min(8, Math.Max(1, entities.Count / 10)), // Adaptive concurrency
                RespectDynamicRateLimits = true,
                EnableProgressReporting = true,
                EnableRealTimeSignalR = true
            };

            // Create batches from entities (smaller batches for better parallelism within the chunk)
            var entityBatches = CreateEntityBatches(entities, Math.Min(25, Math.Max(5, entities.Count / 4)));
            
            _logger.LogInformation("🚀 [CHUNK-{ChunkNumber}] Created {BatchCount} sub-batches for parallel processing " +
                                 "(avg: {AvgBatchSize:F1} entities per sub-batch)",
                request.ChunkNumber, entityBatches.Count, 
                entityBatches.Count > 0 ? (double)entities.Count / entityBatches.Count : 0);

            // Process using the parallel processor (this registers parallel context!)
            var parallelResult = await _parallelProcessor.ProcessInParallelAsync(
                entityBatches,
                parallelConfig,
                async (batch, batchNumber, cancellationToken) =>
                {
                    return await ProcessEntityBatch(batch, batchRequest, batchNumber, request.ChunkNumber, cancellationToken);
                },
                CancellationToken.None);

            // Convert ParallelProcessingResult to BatchProcessingResult
            var result = new BatchProcessingResult
            {
                TotalProcessed = entities.Count,
                SuccessfulEntities = parallelResult.SuccessfulEntities,
                FailedEntities = parallelResult.FailedEntities,
                ProcessingTime = parallelResult.TotalDuration,
                Errors = parallelResult.Errors ?? new List<string>()
            };

            _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Parallel processing completed: {SuccessfulEntities}/{TotalProcessed} successful",
                request.ChunkNumber, result.SuccessfulEntities, result.TotalProcessed);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [CHUNK-{ChunkNumber}] Parallel processing failed: {ErrorMessage}",
                request.ChunkNumber, ex.Message);
            
            return new BatchProcessingResult
            {
                TotalProcessed = entities.Count,
                SuccessfulEntities = 0,
                FailedEntities = entities.Count,
                ProcessingTime = TimeSpan.Zero,
                Errors = new List<string> { $"Parallel processing failed: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Creates entity batches for parallel processing within a chunk
    /// </summary>
    private List<List<Dictionary<string, object>>> CreateEntityBatches(
        List<Dictionary<string, object>> entities, 
        int batchSize)
    {
        var batches = new List<List<Dictionary<string, object>>>();
        
        for (int i = 0; i < entities.Count; i += batchSize)
        {
            var batch = entities.Skip(i).Take(batchSize).ToList();
            batches.Add(batch);
        }
        
        return batches;
    }

    /// <summary>
    /// Processes a single batch of entities (transform + create)
    /// </summary>
    private async Task<EntityProcessingResult> ProcessEntityBatch(
        List<Dictionary<string, object>> batch, 
        BatchProcessingRequest batchRequest,
        int batchNumber,
        int chunkNumber,
        CancellationToken cancellationToken)
    {
        var result = new EntityProcessingResult
        {
            TotalEntities = batch.Count,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            Errors = new List<string>()
        };

        try
        {
            // Transform all entities in this batch
            var transformedEntities = new List<Dictionary<string, object>>();
            
            foreach (var entity in batch)
            {
                try
                {
                    // Transform entity using the correct service signature
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
                    
                    _logger.LogError(ex, "❌ [BATCH-{BatchNumber}] Failed to transform individual entity: {ErrorMessage}",
                        batchNumber, ex.Message);
                }
            }

            // Create all transformed entities
            if (transformedEntities.Any())
            {
                var createdEntities = await _entityCreateService.CreateEntitiesAsync(
                    transformedEntities, 
                    batchRequest, 
                    cancellationToken);

                if (createdEntities != null && createdEntities.Any())
                {
                    result.SuccessfulEntities += createdEntities.Count;
                    
                    _logger.LogInformation("🔍 [BATCH-{BatchNumber}] Created {CreatedCount} entities successfully",
                        batchNumber, createdEntities.Count);
                    
                    // Store mappings for successfully created entities  
                    for (int i = 0; i < Math.Min(batch.Count, createdEntities.Count); i++)
                    {
                        try
                        {
                            var sourceEntity = batch[i];
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
                                // Extract level from source entity for mapping differentiation
                                var processingLevel = sourceEntity.TryGetValue("_processing_level", out var level) ? (int)(level ?? 0) : 0;
                                var processingMode = sourceEntity.TryGetValue("_processing_mode", out var mode) ? mode?.ToString() : "Unknown";
                                
                                // Store level metadata for filtering mappings by level
                                var metadata = System.Text.Json.JsonSerializer.Serialize(new { 
                                    Level = processingLevel,
                                    ProcessingMode = processingMode
                                });

                                var mapping = new EntityMapping
                                {
                                    MigrationId = batchRequest.MigrationId,
                                    EntityType = batchRequest.EntityType,
                                    SourceId = sourceId,
                                    DestinationId = destinationId,
                                    Metadata = metadata, // ✅ Store level info for mapping differentiation
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow
                                };
                                
                                await _entityMappingService.StoreEntityMappingAsync(mapping, cancellationToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ [BATCH-{BatchNumber}] Failed to store entity mapping: {ErrorMessage}",
                                batchNumber, ex.Message);
                            // Don't fail the whole batch for mapping issues
                        }
                    }
                }
                else
                {
                    result.FailedEntities += transformedEntities.Count;
                    result.Errors.Add("Entity creation service returned no results");
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            result.FailedEntities = batch.Count;
            result.Errors.Add($"Batch processing failed: {ex.Message}");
            
            _logger.LogError(ex, "❌ [BATCH-{BatchNumber}] Batch processing failed: {ErrorMessage}",
                batchNumber, ex.Message);
            
            return result;
        }
    }

    /// <summary>
    /// Publishes progress update for this chunk completion
    /// </summary>
    private async Task PublishChunkProgress(ProcessEntityChunkRequest request, BatchProcessingResult result)
    {
        try
        {
            var progressEvent = new EntityProgressEvent
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                TotalCount = request.ChunkSize,
                ProcessedCount = result.TotalProcessed,
                SuccessCount = result.SuccessfulEntities,
                FailureCount = result.FailedEntities,
                Status = result.SuccessfulEntities == result.TotalProcessed ? "completed" : "processing",
                ProcessingTime = result.ProcessingTime,
                Timestamp = DateTime.UtcNow
            };

            await _progressEventPublisher.PublishEntityProgressAsync(progressEvent);
            
            _logger.LogDebug("📊 [CHUNK-{ChunkNumber}] Published progress update: {Successful}/{Total} entities",
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