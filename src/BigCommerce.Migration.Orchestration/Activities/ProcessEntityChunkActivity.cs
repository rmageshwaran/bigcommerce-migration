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

    public ProcessEntityChunkActivity(
        ILogger<ProcessEntityChunkActivity> logger,
        IEntityFetchService entityFetchService,
        IEntityTransformService entityTransformService,
        IEntityCreateService entityCreateService,
        IEntityMappingService entityMappingService,
        IEntityErrorHandlingService errorHandlingService,
        IProgressEventPublisher progressEventPublisher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // Step 2: Transform and create entities
            result = await ProcessEntitiesForChunk(entities, batchRequest, request.ChunkNumber);

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
    /// Processes entities for this chunk (transform + create + mapping)
    /// </summary>
    private async Task<BatchProcessingResult> ProcessEntitiesForChunk(
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
                        
                        await _entityMappingService.StoreEntityMappingAsync(mapping, CancellationToken.None);
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