using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Utilities;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Activities.Models;
using BigCommerce.Migration.Activities.Services;

namespace BigCommerce.Migration.Activities.Activities;

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
    private readonly ICentralizedProgressBroadcastService _centralizedBroadcastService; // 🎯 PHASE 2: Single point for all progress broadcasting
    private readonly IProductComponentsMigrationPipeline _productComponentsPipeline; // 🔗 PHASE 2: Special pipeline for product components

    // Note: Universal aggregator removed - using simplified chunk-level progress tracking
    private readonly ICancellationStore _cancellationStore; // ✅ Phase 3.1.1: Native cancellation support
    private readonly ISubBatchProcessor _subBatchProcessor; // ✅ Phase 3.1.2: Sub-batch processing for 50-100 entities per sub-batch
    private readonly IProgressTracker _progressTracker; // 🆕 INCREMENTAL PROGRESS: Real-time progress tracking

    public ProcessEntityChunkActivity(
        ILogger<ProcessEntityChunkActivity> logger,
        IEntityFetchService entityFetchService,
        IEntityTransformService entityTransformService,
        IEntityCreateService entityCreateService,
        IEntityMappingService entityMappingService,
        IEntityErrorHandlingService errorHandlingService,
        IProgressEventPublisher progressEventPublisher,
        ISignalREventFactory signalREventFactory,
        ICentralizedProgressBroadcastService centralizedBroadcastService,
        IProductComponentsMigrationPipeline productComponentsPipeline,
        // universalAggregator parameter removed
        ICancellationStore cancellationStore,
        ISubBatchProcessor subBatchProcessor,
        IProgressTracker progressTracker) // 🆕 INCREMENTAL PROGRESS: Real-time progress tracking
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityFetchService = entityFetchService ?? throw new ArgumentNullException(nameof(entityFetchService));
        _entityTransformService = entityTransformService ?? throw new ArgumentNullException(nameof(entityTransformService));
        _entityCreateService = entityCreateService ?? throw new ArgumentNullException(nameof(entityCreateService));
        _entityMappingService = entityMappingService ?? throw new ArgumentNullException(nameof(entityMappingService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory)); // 🎯 CENTRALIZED SIGNALR: Factory injection
        _centralizedBroadcastService = centralizedBroadcastService ?? throw new ArgumentNullException(nameof(centralizedBroadcastService)); // 🎯 PHASE 2: Centralized broadcasting injection
        _productComponentsPipeline = productComponentsPipeline ?? throw new ArgumentNullException(nameof(productComponentsPipeline)); // 🔗 PHASE 2: Special pipeline injection

        // Universal aggregator injection removed - using simplified progress tracking
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore)); // ✅ Phase 3.1.1: Native cancellation injection
        _subBatchProcessor = subBatchProcessor ?? throw new ArgumentNullException(nameof(subBatchProcessor)); // ✅ Phase 3.1.2: Sub-batch processing injection
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker)); // 🆕 INCREMENTAL PROGRESS: Real-time progress tracking injection
    }

    /// <summary>
    /// Processes a manageable chunk of entities (max 500) to stay within Azure Functions timeout limits
    /// Phase 3.1.1: Enhanced with native cancellation support via blob-based cooperative cancellation
    /// Note: CancellationToken only used for runtime shutdown, not orchestrator cancellation
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
            // Phase 3.1.1: Check for cancellation at the start of chunk processing
            await CheckCancellationAsync(request.MigrationId);

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
                CachedEntityData = request.CachedEntityData,
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext,
                PaginationMetadata = request.PaginationMetadata,
                UseDirectPagination = request.UseDirectPagination,
                ChannelMapping = request.ChannelMapping // ✅ Pass ChannelMapping from ProcessEntityChunkRequest
            };
            
            // ✅ INJECT CHANNEL MAPPING: Add to AdditionalData for EntityFetchService access
            if (request.EntityType.Equals("product-channel-assign", StringComparison.OrdinalIgnoreCase) && 
                request.ChannelMapping?.Any() == true)
            {
                batchRequest.AdditionalData["ChannelMapping"] = System.Text.Json.JsonSerializer.Serialize(request.ChannelMapping);
                _logger.LogInformation("✅ [CHANNEL-MAPPING] Injected {MappingCount} channel mappings for {EntityType} batch {ChunkNumber} (migration: {MigrationId})", 
                    request.ChannelMapping.Count, request.EntityType, request.ChunkNumber, request.MigrationId);
            }

            // Step 1: Fetch entities
            var entities = await FetchEntitiesForChunk(batchRequest);
            
            // Phase 3.1.1: Check for cancellation after fetch step
            await CheckCancellationAsync(request.MigrationId);

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
            _logger.LogInformation("📋 [CHUNK-{ChunkNumber}] Processing {EntityCount} {EntityType} entities", 
                request.ChunkNumber, entities.Count, batchRequest.EntityType);

            // Phase 3.1.1: Check for cancellation before processing
            await CheckCancellationAsync(request.MigrationId);

            // Phase 3.1.3: Enhanced pipeline routing with comprehensive entity type support
            _logger.LogInformation("🔄 [CHUNK-{ChunkNumber}] Starting entity processing for {EntityCount} {EntityType} entities", 
                request.ChunkNumber, entities.Count, request.EntityType);
            
            result = await RouteToAppropriatePipeline(entities, batchRequest, request.ChunkNumber);
            
            // 🚨 CRITICAL FIX: Record progress IMMEDIATELY after processing, BEFORE cancellation check
            // This ensures that completed sub-batches are recorded even if cancellation occurs
            var processingTime = DateTime.UtcNow - startTime;
            result.ProcessingTime = processingTime;

            _logger.LogInformation("📊 [CHUNK-{ChunkNumber}] Entity processing completed - Starting progress recording phase. " +
                                 "Results: Success={SuccessCount}, Failed={FailedCount}, Skipped={SkippedCount}, Cancelled={CancelledCount}, Total={TotalProcessed}",
                request.ChunkNumber, result.SuccessfulEntities, result.FailedEntities, result.SkippedEntities, 
                result.CancelledEntities, result.TotalProcessed);

            // Note: Universal aggregator removed - using simplified chunk-level progress tracking
            _logger.LogInformation("📊 [CHUNK-{ChunkNumber}] Chunk progress: Success={Success}, Failed={Failed}, Skipped={Skipped}, Cancelled={Cancelled}, Total={Total}", 
                request.ChunkNumber, result.SuccessfulEntities, result.FailedEntities, result.SkippedEntities, result.CancelledEntities, result.TotalProcessed);

            _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Chunk processing completed: " +
                                 "{SuccessfulEntities}/{TotalProcessed} entities successful in {ProcessingTimeMs}ms " +
                                 "(avg: {AvgTimePerEntity:F1}ms per entity)",
                request.ChunkNumber, result.SuccessfulEntities, result.TotalProcessed, 
                processingTime.TotalMilliseconds,
                result.TotalProcessed > 0 ? processingTime.TotalMilliseconds / result.TotalProcessed : 0);

            // Step 3: Publish progress update for this chunk - MOVED BEFORE cancellation check
            // 🛡️ CRITICAL: Protect SignalR publishing from exceptions to prevent data loss
            _logger.LogInformation("🔄 [CHUNK-{ChunkNumber}] STEP 2/3: Publishing SignalR progress update for {MigrationId}:{EntityType}", 
                request.ChunkNumber, request.MigrationId, request.EntityType);
            
            try
            {
                await PublishChunkProgress(request, result);
                _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] STEP 2/3 COMPLETED: SignalR progress published successfully for {MigrationId}:{EntityType}", 
                    request.ChunkNumber, request.MigrationId, request.EntityType);
            }
            catch (Exception signalREx)
            {
                _logger.LogError(signalREx, "❌ [CHUNK-{ChunkNumber}] STEP 2/3 FAILED: SignalR progress publishing failed for {MigrationId}:{EntityType} - {ErrorMessage}",
                    request.ChunkNumber, request.MigrationId, request.EntityType, signalREx.Message);
                // Don't fail the chunk - continue with other progress recording
            }

            // 🆕 INCREMENTAL PROGRESS: Record incremental progress immediately after chunk completion - MOVED BEFORE cancellation check
            // This is the CRITICAL integration that enables real-time progress tracking and prevents data loss on cancellation
            // 🛡️ CRITICAL: Protect incremental progress recording from exceptions to prevent data loss
            _logger.LogInformation("🔄 [CHUNK-{ChunkNumber}] STEP 3/3: Recording incremental progress events for {MigrationId}:{EntityType}", 
                request.ChunkNumber, request.MigrationId, request.EntityType);
            
            try
            {
                await RecordIncrementalProgress(request, result, startTime, DateTime.UtcNow);
                _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] STEP 3/3 COMPLETED: Incremental progress recorded successfully for {MigrationId}:{EntityType}", 
                    request.ChunkNumber, request.MigrationId, request.EntityType);
            }
            catch (Exception incrementalEx)
            {
                _logger.LogError(incrementalEx, "❌ [CHUNK-{ChunkNumber}] STEP 3/3 FAILED: Incremental progress recording failed for {MigrationId}:{EntityType} - {ErrorMessage}",
                    request.ChunkNumber, request.MigrationId, request.EntityType, incrementalEx.Message);
                // Don't fail the chunk - this is non-critical for core functionality
            }
            
            _logger.LogInformation("🎯 [CHUNK-{ChunkNumber}] ALL PROGRESS RECORDING STEPS COMPLETED - Now checking for cancellation for {MigrationId}:{EntityType}", 
                request.ChunkNumber, request.MigrationId, request.EntityType);
            
            // Phase 3.1.1: Final cancellation check AFTER progress recording
            // This ensures progress is saved even if cancellation occurs
            try
            {
                await CheckCancellationAsync(request.MigrationId);
                _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Cancellation check passed - chunk completed successfully for {MigrationId}:{EntityType}", 
                    request.ChunkNumber, request.MigrationId, request.EntityType);
            }
            catch (Exception cancelEx)
            {
                _logger.LogInformation("🚫 [CHUNK-{ChunkNumber}] Cancellation detected AFTER progress recording for {MigrationId}:{EntityType} - Progress data preserved! Error: {ErrorMessage}", 
                    request.ChunkNumber, request.MigrationId, request.EntityType, cancelEx.Message);
                throw; // Re-throw to maintain cancellation behavior
            }

            return result;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            
            // 🚫 CHUNK-LEVEL CANCELLATION FIX: Distinguish between cancellation and actual failures
            bool isCancellationError = ex.Message.Contains("Migration cancelled", StringComparison.OrdinalIgnoreCase) ||
                                     ex.Message.Contains("User requested cancellation", StringComparison.OrdinalIgnoreCase) ||
                                     ex is OperationCanceledException;
            
            if (isCancellationError)
            {
                _logger.LogInformation("🚫 [CHUNK-{ChunkNumber}] Chunk processing was cancelled after {ProcessingTimeMs}ms: {ErrorMessage}",
                    request.ChunkNumber, processingTime.TotalMilliseconds, ex.Message);

                return new BatchProcessingResult
                {
                    TotalProcessed = request.ChunkSize,
                    SuccessfulEntities = 0,
                    FailedEntities = 0,  // 🚫 FIX: Don't mark cancelled entities as failed
                    SkippedEntities = 0,
                    CancelledEntities = request.ChunkSize,  // 🚫 FIX: Mark entire chunk as cancelled
                    ProcessingTime = processingTime,
                    Errors = new List<string> { $"Chunk {request.ChunkNumber} cancelled: {ex.Message}" }
                };
            }
            else
            {
                _logger.LogError(ex, "💥 [CHUNK-{ChunkNumber}] Chunk processing failed after {ProcessingTimeMs}ms: {ErrorMessage}",
                    request.ChunkNumber, processingTime.TotalMilliseconds, ex.Message);

                return new BatchProcessingResult
                {
                    TotalProcessed = request.ChunkSize,
                    SuccessfulEntities = 0,
                    FailedEntities = request.ChunkSize,  // Only actual failures marked as failed
                    ProcessingTime = processingTime,
                    Errors = new List<string> { $"Chunk {request.ChunkNumber} failed: {ex.Message}" }
                };
            }
        }
    }

    /// <summary>
    /// Fetches entities for this chunk using the appropriate strategy
    /// Phase 3.1.1: Uses blob-based cooperative cancellation (checked elsewhere)
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
    /// Phase 3.1.1: Uses blob-based cooperative cancellation (checked elsewhere)
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

            // Phase 3.1.2: Transform entities using sub-batch processing for better performance and cancellation responsiveness
            var transformedEntities = new List<Dictionary<string, object>>();
            var cancelledTransformations = 0; // Track cancelled transformations
            
            // Phase 3.1.1: Check for cancellation before transformation
            await CheckCancellationAsync(batchRequest.MigrationId);
            
            // Create sub-batch configuration for transformation (50-100 entities per sub-batch as per task requirement)
            var subBatchConfig = new SubBatchConfiguration
            {
                EntityType = batchRequest.EntityType,
                SubBatchSize = Math.Min(75, Math.Max(50, entities.Count / 4)), // Dynamic size: 50-100 entities, optimized based on total count
                MaxConcurrency = 3, // Conservative concurrency for transformation
                ProcessSubBatchesSequentially = true, // Sequential for deterministic processing
                SubBatchDelayMs = 0 // No delay for transformation
            };

            _logger.LogInformation("🔧 [CHUNK-{ChunkNumber}] Starting sub-batch transformation with {SubBatchSize} entities per sub-batch for {TotalEntities} {EntityType} entities",
                chunkNumber, subBatchConfig.SubBatchSize, entities.Count, batchRequest.EntityType);

            try
            {
                transformedEntities = await _subBatchProcessor.ProcessIndividuallyInSubBatchesAsync(
                    entities,
                    subBatchConfig,
                    async (entity, cancellationToken) =>
                    {
                        // Check for cancellation in each entity transformation
                        await CheckCancellationAsync(batchRequest.MigrationId);
                        
                        try
                        {
                            var transformedEntity = await _entityTransformService.TransformEntityAsync(entity, batchRequest);
                            return transformedEntity;
                        }
                        catch (Exception ex)
                        {
                            // 🚫 CANCELLATION FIX: Distinguish between actual failures and cancellation-induced failures
                            bool isCancellationError = ex.Message.Contains("Migration cancelled", StringComparison.OrdinalIgnoreCase) ||
                                                     ex.Message.Contains("User requested cancellation", StringComparison.OrdinalIgnoreCase) ||
                                                     ex is OperationCanceledException;
                            
                            if (isCancellationError)
                            {
                                _logger.LogInformation("🚫 [CHUNK-{ChunkNumber}] Entity transformation was cancelled: {ErrorMessage}",
                                    chunkNumber, ex.Message);
                                
                                // Return a special object to indicate cancellation during transformation
                                return new Dictionary<string, object>
                                {
                                    ["status"] = "cancelled",
                                    ["reason"] = "transformation_cancelled",
                                    ["phase"] = "transformation",
                                    ["original_entity"] = entity
                                };
                            }
                            else
                            {
                                result.FailedEntities++;
                                result.Errors.Add($"Failed to transform entity: {ex.Message}");
                                
                                _logger.LogError(ex, "❌ [CHUNK-{ChunkNumber}] Failed to transform individual entity: {ErrorMessage}",
                                    chunkNumber, ex.Message);
                                return null; // Return null for failed transformations (will be filtered out)
                            }
                        }
                    },
                    batchRequest.EntityType,
                    batchRequest.MigrationId);

                            // 🚫 TRANSFORMATION CANCELLATION FIX: Filter out cancelled transformations
            cancelledTransformations = transformedEntities.Count(e => 
                e.ContainsKey("status") && e["status"].ToString().Equals("cancelled"));
            var actuallyTransformed = transformedEntities.Where(e => 
                !e.ContainsKey("status") || !e["status"].ToString().Equals("cancelled")).ToList();
            
            // 🔍 DEBUG: Transformation phase results including cancellations
            var transformationSkippedCount = entities.Count - transformedEntities.Count - result.FailedEntities;
            
            _logger.LogInformation("🔍 [TRANSFORM-DEBUG] CHUNK-{ChunkNumber} {EntityType}: InputEntities={InputCount}, TransformedEntities={TransformedCount}, ActuallyTransformed={ActualTransformed}, TransformationCancelled={TransformCancelled}, TransformationFailures={TransformFailures}, TransformationSkips={TransformSkips}",
                chunkNumber, batchRequest.EntityType, entities.Count, transformedEntities.Count, actuallyTransformed.Count, cancelledTransformations, result.FailedEntities, transformationSkippedCount);
            
            _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Sub-batch transformation completed: {ActualTransformed} actually transformed + {CancelledTransformed} cancelled during transform + {SkippedCount} skipped = {TotalProcessed}/{InputCount} {EntityType} entities processed, {FailedCount} failed",
                chunkNumber, actuallyTransformed.Count, cancelledTransformations, transformationSkippedCount, actuallyTransformed.Count + cancelledTransformations + transformationSkippedCount, entities.Count, batchRequest.EntityType, result.FailedEntities);
                
            // Update transformedEntities to exclude cancelled ones
            transformedEntities = actuallyTransformed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [CHUNK-{ChunkNumber}] Sub-batch transformation failed: {ErrorMessage}", chunkNumber, ex.Message);
                result.FailedEntities = entities.Count;
                result.Errors.Add($"Sub-batch transformation failed: {ex.Message}");
                transformedEntities = new List<Dictionary<string, object>>(); // Empty list on failure
            }

            // Phase 3.1.1: Check for cancellation before creation
            await CheckCancellationAsync(batchRequest.MigrationId);

            // Phase 3.1.2: Create all transformed entities (bulk operation - optimal for BigCommerce API)
            if (transformedEntities.Any())
            {
                _logger.LogInformation("🚀 [CHUNK-{ChunkNumber}] Starting bulk entity creation for {TransformedCount} {EntityType} entities",
                    chunkNumber, transformedEntities.Count, batchRequest.EntityType);

                var createdEntities = await _entityCreateService.CreateEntitiesAsync(
                    transformedEntities, 
                    batchRequest, 
                    CancellationToken.None);

                if (createdEntities != null && createdEntities.Any())
                {
                    // 🚨 FIX: Count created, skipped, and cancelled entities separately
                    var actuallyCreated = createdEntities.Count(e => 
                        !e.ContainsKey("status") || (!e["status"].ToString().Equals("skipped") && !e["status"].ToString().Equals("cancelled")));
                    var skippedEntities = createdEntities.Count(e => 
                        e.ContainsKey("status") && e["status"].ToString().Equals("skipped"));
                    var cancelledEntities = createdEntities.Count(e => 
                        e.ContainsKey("status") && e["status"].ToString().Equals("cancelled"));
                    
                    // 🔍 DEBUG: Creation phase results
                    _logger.LogInformation("🔍 [CREATION-DEBUG] CHUNK-{ChunkNumber} {EntityType}: CreatedEntities={CreatedCount}, ActuallyCreated={ActualCreated}, CreationSkipped={CreationSkipped}, CreationCancelled={CreationCancelled}",
                        chunkNumber, batchRequest.EntityType, createdEntities.Count, actuallyCreated, skippedEntities, cancelledEntities);
                    
                    // ✅ SIMPLE CORRECT LOGIC: 
                    // Transformation skips = input entities that didn't get transformed (and weren't failures)
                    var transformationFailures = result.FailedEntities; // Failures accumulated from transformation phase
                    var transformationSkippedCount = entities.Count - transformedEntities.Count - transformationFailures;
                    
                    // 🔍 DEBUG: Final calculation values
                    _logger.LogInformation("🔍 [FINAL-CALC-DEBUG] CHUNK-{ChunkNumber} {EntityType}: InputCount={Input}, TransformedCount={Transformed}, CreatedCount={Created}, TransformFailures={TFailures}, TransformSkips={TSkips}",
                        chunkNumber, batchRequest.EntityType, entities.Count, transformedEntities.Count, createdEntities.Count, transformationFailures, transformationSkippedCount);
                    
                    // 🚨 STATUS FIX: Track skipped and cancelled entities separately from successful entities
                    result.SuccessfulEntities = actuallyCreated; // Only entities that were actually created (not skipped or cancelled)
                    result.FailedEntities = transformationFailures; // Only actual failures
                    result.SkippedEntities = skippedEntities + Math.Max(0, transformationSkippedCount); // Creation skips + transformation skips
                    result.CancelledEntities = cancelledEntities + cancelledTransformations; // Entities cancelled during creation + transformation
                    
                    // 🔍 DISCREPANCY LOGGING: Track the difference between sub-batch reports and chunk counting
                    var subBatchReportedTotal = createdEntities.Count; // What sub-batches reported as "created"
                    var chunkCountedTotal = result.SuccessfulEntities + result.CancelledEntities + result.SkippedEntities;
                    if (subBatchReportedTotal != chunkCountedTotal)
                    {
                        _logger.LogWarning("⚠️ [COUNT-DISCREPANCY] CHUNK-{ChunkNumber} {EntityType}: Sub-batches reported {SubBatchTotal} created, but chunk counted {ChunkTotal} (Success={Success}, Cancelled={Cancelled}, Skipped={Skipped}). " +
                            "This indicates products were created in destination store but marked as cancelled due to cancellation timing.",
                            chunkNumber, batchRequest.EntityType, subBatchReportedTotal, chunkCountedTotal, 
                            result.SuccessfulEntities, result.CancelledEntities, result.SkippedEntities);
                    }
                    
                    // 🔍 DEBUG: Final result values with skipped and cancelled entities tracked separately
                    _logger.LogInformation("🔍 [RESULT-DEBUG] CHUNK-{ChunkNumber} {EntityType}: FinalSuccessful={Successful}, FinalFailed={Failed}, FinalSkipped={Skipped}, FinalCancelled={Cancelled}, FinalTotal={Total}",
                        chunkNumber, batchRequest.EntityType, result.SuccessfulEntities, result.FailedEntities, result.SkippedEntities, result.CancelledEntities, result.TotalProcessed);
                    
                    _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Entity processing completed: {CreatedCount} API-created + {CreationSkippedCount} creation-skipped + {CreationCancelledCount} creation-cancelled + {TransformCancelledCount} transform-cancelled + {TransformSkippedCount} transform-skipped = {SuccessCount} successful, {FailedCount} failed, {SkippedCount} skipped, {CancelledCount} cancelled out of {InputCount} {EntityType} input entities",
                        chunkNumber, actuallyCreated, skippedEntities, cancelledEntities, cancelledTransformations, transformationSkippedCount, result.SuccessfulEntities, result.FailedEntities, result.SkippedEntities, result.CancelledEntities, entities.Count, batchRequest.EntityType);
                    
                    // Store mappings only for actually created entities (not skipped or cancelled)
                    var createdOnlyEntities = createdEntities.Where(e => 
                        !e.ContainsKey("status") || (!e["status"].ToString().Equals("skipped") && !e["status"].ToString().Equals("cancelled"))).ToList();
                    if (createdOnlyEntities.Any())
                    {
                        await StoreMappingsForCreatedEntities(entities, createdOnlyEntities, batchRequest, chunkNumber);
                    }
                }
                else
                {
                    result.FailedEntities += transformedEntities.Count;
                    result.Errors.Add("Entity creation service returned no results");
                    _logger.LogWarning("⚠️ [CHUNK-{ChunkNumber}] Bulk entity creation returned no results for {TransformedCount} {EntityType} entities",
                        chunkNumber, transformedEntities.Count, batchRequest.EntityType);
                }
            }
            else
            {
                _logger.LogWarning("⚠️ [CHUNK-{ChunkNumber}] No entities available for creation after transformation phase for {EntityType}",
                    chunkNumber, batchRequest.EntityType);
            }

            // ✅ SIMPLE FIX: Don't override failed count - it's already correct from entity creation
            // If skipped entities are counted as successful, then failed count should remain 0
            // result.FailedEntities is already correctly set by the entity creation logic above

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
    /// Phase 3.1.1: Uses blob-based cooperative cancellation (checked elsewhere)
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
                        // 🔗 SKIP OPTIONS & VARIANTS: 
                        // - Options use hierarchical mapping via HierarchicalOptionMappingService
                        // - Variants are leaf entities with no dependents - no mapping storage needed
                        // Only create individual EntityMapping records for entities that have dependents
                        if (batchRequest.EntityType.ToLowerInvariant() != "options" && 
                            batchRequest.EntityType.ToLowerInvariant() != "variants")
                        {
                            // Extract level from source entity for mapping differentiation
                            var processingLevel = sourceEntity.TryGetValue("_processing_level", out var level) ? JsonElementHelper.GetIntegerValue(level) : 0;
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
                                string sku = createdEntity.GetValueOrDefault("sku")?.ToString();
                                
                                metadata = System.Text.Json.JsonSerializer.Serialize(new { 
                                   Sku = sku
                                });
                                // Extract channels data directly from source entity
                                if (sourceEntity.TryGetValue("channels", out var channelsValue))
                                {
                                    var serializedChannels = System.Text.Json.JsonSerializer.Serialize(channelsValue);
                                    if (serializedChannels != "\"[]\"" && serializedChannels != "null")
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

                                    if (serializedRelatedProducts != "\"[]\"" && serializedRelatedProducts != "null")
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
                            var entityType = batchRequest.EntityType.ToLowerInvariant();
                            if (entityType == "options")
                            {
                                _logger.LogDebug("🔗 [MAPPING-SKIP] Skipping individual EntityMapping for options - using hierarchical mapping instead");
                            }
                            else if (entityType == "variants" || entityType == "product-metafields")
                            {
                                _logger.LogDebug("🔗 [MAPPING-SKIP] Skipping individual EntityMapping for {EntityType} - leaf entities with no dependents", entityType);
                            }
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
    /// Publishes progress update for this chunk completion using CentralizedProgressBroadcastService
    /// PHASE 2: Single point of broadcasting with rate limiting and clean event structure
    /// Uses ProgressTracker for accurate cumulative counts across all chunks
    /// </summary>
    private async Task PublishChunkProgress(ProcessEntityChunkRequest request, BatchProcessingResult result)
    {
        try
        {
            // 🎯 PHASE 2: Use CentralizedProgressBroadcastService for all progress broadcasting
            // This provides rate limiting, consistent event structure, and single source of truth
            
            // 🚨 FIX: Skip SignalR publishing for product-components - individual components handle their own progress
            if (request.EntityType.Equals("product-components", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🎯 [CHUNK-{ChunkNumber}] Skipping SignalR progress for product-components - individual components broadcast their own progress", request.ChunkNumber);
                return;
            }
            
            // Get accurate cumulative progress from the progress tracker
            var migrationProgress = await _progressTracker.GetLatestAggregatedProgressAsync(request.MigrationId);
            var entityProgress = migrationProgress.EntityProgress.GetValueOrDefault(request.EntityType);
            
            // 🔍 DEBUG: Log TotalCount assignment source
            _logger.LogInformation("🔍 [CHUNK-{ChunkNumber}] TotalCount DEBUG: EntityProgress={EntityProgressExists}, " +
                "TotalCount={TotalCount}, FallbackCalculation={FallbackCalc}, ChunkSize={ChunkSize}, TotalChunks={TotalChunks}",
                request.ChunkNumber, entityProgress != null, entityProgress?.TotalCount ?? -1, 
                request.TotalChunks * request.ChunkSize, request.ChunkSize, request.TotalChunks);
            
            // Calculate progress percentage based on entity-specific progress
            var progressPercentage = entityProgress?.ProgressPercentage ?? 0.0;
            
            // 🎯 USE PROGRESSTRACKER COMPREHENSIVELY: Get all data from single source of truth
            var chunkProgress = new EntityChunkProgress
            {
                EntityType = request.EntityType,
                ChunkNumber = request.ChunkNumber,
                ChunkSize = result.TotalProcessed, // Actual entities processed in this chunk
                ProcessedInChunk = result.SuccessfulEntities, // This chunk only
                FailedInChunk = result.FailedEntities, // This chunk only
                
                // ✅ COMPREHENSIVE PROGRESSTRACKER DATA: Use all cumulative counts
                CumulativeProcessed = entityProgress?.ProcessedCount ?? result.SuccessfulEntities, // Total processed (success + failed + skipped + cancelled)
                CumulativeFailed = entityProgress?.FailureCount ?? result.FailedEntities, // Total failed
                CumulativeSkipped = entityProgress?.SkippedCount ?? 0, // Total skipped
                CumulativeCancelled = entityProgress?.CancelledCount ?? 0, // Total cancelled
                TotalEntitiesForType = entityProgress?.TotalCount ?? (request.TotalChunks * request.ChunkSize), // Total from discovery
                
                ProgressPercentage = entityProgress?.ProgressPercentage ?? progressPercentage,
                Status = entityProgress?.Status ?? (result.SuccessfulEntities == result.TotalProcessed ? "completed" : "processing"),
                Message = $"Processed chunk {request.ChunkNumber}/{request.TotalChunks}: {result.SuccessfulEntities}/{result.TotalProcessed} entities (Total: {entityProgress?.ProcessedCount ?? 0}/{entityProgress?.TotalCount ?? 0})",
                ProcessingTimeMs = (long)(entityProgress?.ProcessingTime.TotalMilliseconds ?? result.ProcessingTime.TotalMilliseconds),
                ShowTotalCount = entityProgress?.ShowTotalCount ?? true // 🎯 UI FLAG: Pass display flag to SignalR event
            };

            // Use centralized service with built-in rate limiting and error handling
            await _centralizedBroadcastService.BroadcastEntityChunkProgressAsync(request.MigrationId, chunkProgress);
            
            _logger.LogDebug("📊 [CHUNK-{ChunkNumber}] Published centralized progress update via CentralizedProgressBroadcastService: {Successful}/{Total} entities (Cumulative: {CumulativeSuccess}/{CumulativeTotal})",
                request.ChunkNumber, result.SuccessfulEntities, result.TotalProcessed, 
                entityProgress?.SuccessCount ?? result.SuccessfulEntities, entityProgress?.TotalCount ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CHUNK-{ChunkNumber}] Failed to publish progress update via CentralizedProgressBroadcastService: {ErrorMessage}",
                request.ChunkNumber, ex.Message);
            // Don't fail the chunk processing due to progress publishing issues
        }
    }

    /// <summary>
    /// Phase 3.1.1: Checks for cancellation using blob store (cooperative cancellation)
    /// This is the primary cancellation mechanism since Activities cannot receive 
    /// cancellation tokens from orchestrators in Azure Functions
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    private async Task CheckCancellationAsync(string migrationId)
    {
        try
        {
            // Check blob store for cooperative cancellation flag
            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            if (isCancelled)
            {
                var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                _logger.LogInformation("🚫 [CANCELLATION] Chunk processing cancelled: {Reason}", reason);
                throw new OperationCanceledException($"Migration cancelled: {reason}");
            }
        }
        catch (OperationCanceledException)
        {
            // Re-throw cancellation exceptions
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CANCELLATION-CHECK] Failed to check cancellation status for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't fail processing due to cancellation check issues - fail safe approach
        }
    }

    /// <summary>
    /// Phase 3.1.3: Routes entities to the appropriate processing pipeline based on entity type
    /// Provides comprehensive routing with future extensibility for new special pipelines
    /// </summary>
    /// <param name="entities">Entities to process</param>
    /// <param name="batchRequest">Batch processing request</param>
    /// <param name="chunkNumber">Chunk number for logging</param>
    /// <returns>Batch processing result</returns>
    private async Task<BatchProcessingResult> RouteToAppropriatePipeline(
        List<Dictionary<string, object>> entities,
        BatchProcessingRequest batchRequest,
        int chunkNumber)
    {
        var entityType = batchRequest.EntityType.ToLowerInvariant();
        
        _logger.LogInformation("🔀 [CHUNK-{ChunkNumber}] Routing {EntityCount} {EntityType} entities to appropriate pipeline",
            chunkNumber, entities.Count, batchRequest.EntityType);

        try
        {
            switch (entityType)
            {
                case "product-components":
                    // 🎯 EFFICIENT COMPONENT PROCESSING: Single fetch, individual progress tracking
                    // ProductComponentsMigrationPipeline handles fetching products with include="options,modifiers,reviews"
                    // and publishes individual progress events for options, modifiers, and reviews
                    _logger.LogInformation("🔗 [CHUNK-{ChunkNumber}] ===== ROUTING TO PRODUCT-COMPONENTS PIPELINE ===== EntityType: {EntityType}, EntityCount: {EntityCount}, MigrationId: {MigrationId}", 
                        chunkNumber, batchRequest.EntityType, entities.Count, batchRequest.MigrationId);
                    
                    _logger.LogInformation("🔍 [ROUTING-DEBUG] About to call ProductComponentsMigrationPipeline.ProcessProductComponentsAsync");
                    var result = await _productComponentsPipeline.ProcessProductComponentsAsync(
                        entities,
                        batchRequest.MigrationId,
                        batchRequest.SourceStore,
                        batchRequest.DestinationStore,
                        batchRequest.EntityType,
                        CancellationToken.None);
                    _logger.LogInformation("🔍 [ROUTING-DEBUG] ProductComponentsMigrationPipeline.ProcessProductComponentsAsync completed - Processed: {Processed}, Duration: {Duration}ms", 
                        result.TotalProcessed, result.ProcessingTime.TotalMilliseconds);
                    
                    return result;

                case "options":
                case "modifiers":  
                case "reviews":
                    // 🚫 ARCHITECTURE ERROR: Individual component types should not be processed separately
                    // They are processed together in the product-components phase for efficiency
                    // This prevents duplicate API fetches and maintains data consistency
                    _logger.LogError("❌ [CHUNK-{ChunkNumber}] ARCHITECTURE ERROR: {EntityType} should not be processed individually. " +
                                   "Components are processed together in product-components phase for efficiency.", 
                        chunkNumber, batchRequest.EntityType);
                    
                    throw new InvalidOperationException($"Invalid entity routing: {batchRequest.EntityType} should not be processed separately. " +
                        "Individual components (options, modifiers, reviews) are processed together in the product-components phase to avoid duplicate API calls.");

                case "products":
                case "brands":
                case "categories":
                case "variants":
                case "customers":
                case "orders":
                case "coupons":
                case "product-related":
                case "product-images":
                case "product-metafields":
                case "product-channels":
                default:
                    // Standard processing for all other entity types
                    _logger.LogInformation("📋 [CHUNK-{ChunkNumber}] Routing to standard processing pipeline for {EntityType}", 
                        chunkNumber, batchRequest.EntityType);
                    
                    return await ProcessStandardEntitiesForChunk(entities, batchRequest, chunkNumber);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHUNK-{ChunkNumber}] Pipeline routing failed for {EntityType}: {ErrorMessage}",
                chunkNumber, batchRequest.EntityType, ex.Message);
            
            // Return error result instead of throwing to maintain consistent error handling
            return new BatchProcessingResult
            {
                TotalProcessed = entities.Count,
                SuccessfulEntities = 0,
                FailedEntities = entities.Count,
                ProcessingTime = TimeSpan.Zero,
                Errors = new List<string> { $"Pipeline routing failed: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// 🆕 INCREMENTAL PROGRESS: Records incremental progress immediately after chunk completion
    /// This is the CRITICAL method that enables real-time progress tracking and prevents data loss during cancellations
    /// 
    /// Design Principles:
    /// - Fire-and-forget: Doesn't block chunk processing if increment write fails
    /// - Graceful failure: Increment failures don't break the migration
    /// - Immediate persistence: Writes to ChunkIncrementEvents table immediately
    /// - Complete data: Records all chunk results for accurate progress tracking
    /// </summary>
    /// <param name="request">Original chunk processing request</param>
    /// <param name="result">Chunk processing results</param>
    /// <param name="processingStartTime">When chunk processing started</param>
    /// <param name="processingEndTime">When chunk processing completed</param>
    private async Task RecordIncrementalProgress(
        ProcessEntityChunkRequest request,
        BatchProcessingResult result,
        DateTime processingStartTime,
        DateTime processingEndTime)
    {
        try
        {
            // Extract store IDs from request
            var sourceStoreId = request.SourceStore?.StoreId ?? "unknown";
            var destinationStoreId = request.DestinationStore?.StoreId ?? "unknown";

            // Collect error messages from result for detailed tracking
            var errors = result.Errors?.Any() == true ? result.Errors : null;

            _logger.LogInformation("📝 [CHUNK-{ChunkNumber}] INCREMENTAL-PROGRESS: Preparing chunk increment event - " +
                           "Success={Success}, Failed={Failed}, Skipped={Skipped}, Cancelled={Cancelled}, " +
                           "StartIndex={StartIndex}, ChunkSize={ChunkSize}, Source={Source}, Dest={Dest}",
                request.ChunkNumber, result.SuccessfulEntities, result.FailedEntities, 
                result.SkippedEntities, result.CancelledEntities, request.StartIndex, request.ChunkSize,
                sourceStoreId, destinationStoreId);

            // 🚨 FIX: Skip parent-level chunkincrementevents for product-components
            // ProductComponentsMigrationPipeline handles individual component tracking (options, modifiers, reviews)
            // to avoid duplication and provide more granular progress visibility
            if (request.EntityType.Equals("product-components", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("⏭️ [CHUNK-{ChunkNumber}] INCREMENTAL-PROGRESS: Skipping parent-level chunkincrementevents for {EntityType} - individual component tracking used instead", 
                    request.ChunkNumber, request.EntityType);
                return;
            }

            // Call the ProgressTracker's IncrementProgressAsync method with all required data
            _logger.LogInformation("📤 [CHUNK-{ChunkNumber}] INCREMENTAL-PROGRESS: Calling ProgressTracker.IncrementProgressAsync for {MigrationId}:{EntityType}",
                request.ChunkNumber, request.MigrationId, request.EntityType);
                
            await _progressTracker.IncrementProgressAsync(
                migrationId: request.MigrationId,
                entityType: request.EntityType,
                chunkNumber: request.ChunkNumber,
                chunkStartIndex: request.StartIndex,
                chunkSize: request.ChunkSize,
                successfulEntities: result.SuccessfulEntities,
                failedEntities: result.FailedEntities,
                skippedEntities: result.SkippedEntities,
                cancelledEntities: result.CancelledEntities,
                processingStartTime: processingStartTime,
                processingEndTime: processingEndTime,
                sourceStore: sourceStoreId,
                destinationStore: destinationStoreId,
                errors: errors);

            _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] INCREMENTAL-PROGRESS: ProgressTracker.IncrementProgressAsync completed successfully for {MigrationId}:{EntityType}",
                request.ChunkNumber, request.MigrationId, request.EntityType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CHUNK-{ChunkNumber}] Failed to record incremental progress for {MigrationId}:{EntityType} - migration continues normally. " +
                             "This may result in less accurate real-time progress updates, but end-of-migration progress will still be recorded.",
                request.ChunkNumber, request.MigrationId, request.EntityType);
            
            // 🎯 CRITICAL: Don't throw - incremental progress failures should NEVER break the migration
            // The end-of-migration UpdateEntityProgressActivity will still provide fallback progress data
        }
    }


}
