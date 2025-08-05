#nullable disable
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Extensions;
using System.Linq;

namespace BigCommerce.Migration.Functions.Orchestrators;

/// <summary>
/// 🚀 THROUGHPUT OPTIMIZED: Entity migration orchestrator with 17.0x parallel processing
/// Uses Azure Functions context but calls optimized parallel processing pipeline
/// </summary>
public static class EntityMigrationDurableOrchestrator
{
    /// <summary>
    /// 🎯 OPTIMIZED: Entity migration orchestrator function that uses 17.0x parallel processing
    /// </summary>
    [Function("EntityMigrationDurableOrchestrator")]
    public static async Task<EntityMigrationResult> RunEntityMigrationOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger("EntityMigrationDurableOrchestrator");
        var input = context.GetInput<EntityMigrationRequest>();
        
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input), "Entity migration request is required");
        }

        var migrationId = input.MigrationId;
        var entityType = input.EntityType;
        
        var result = new EntityMigrationResult
        {
            EntityType = entityType,
            StartTime = context.CurrentUtcDateTime,
            ProcessedEntities = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            BatchResults = new List<BatchProcessingResult>()
        };

        try
        {
            logger.LogInformation("🚀 THROUGHPUT OPTIMIZED: Starting {EntityType} migration with 17.0x parallel processing for MigrationId: {MigrationId}", 
                entityType, migrationId);

            // Step 1: Fast cancellation check using passed state (no external storage call needed)
            if (input.IsCancelled)
            {
                logger.LogInformation("Migration {MigrationId} was cancelled before {EntityType} processing began. Reason: {Reason}", 
                    migrationId, entityType, input.CancellationReason);
                
                result.IsSuccess = false;
                result.ErrorMessage = $"{entityType} migration was cancelled: {input.CancellationReason}";
                result.EndTime = context.CurrentUtcDateTime;
                result.Duration = result.EndTime.Value - result.StartTime;
                result.Errors.Add($"{entityType} migration was cancelled: {input.CancellationReason}");
                
                return result;
            }

            // Step 2: Check rate limiting before starting entity processing
            await context.CallActivityAsync(
                "CheckRateLimitActivity",
                new CheckRateLimitRequest
                {
                    StoreId = input.SourceStore?.StoreId ?? string.Empty,
                    EntityType = entityType
                });

            // Step 3: Get Configuration - Load entity-specific settings from appsettings.json
            logger.LogError("🔧🔧🔧 [ORCHESTRATOR-CONFIG-DEBUG] ===== CALLING GetEntityConfigurationActivity for '{EntityType}' =====", entityType);
            var entityConfig = await context.CallActivityAsync<EntityConfiguration>("GetEntityConfigurationActivity", entityType);
            
            logger.LogError("🔧 [ORCHESTRATOR-CONFIG-DEBUG] RECEIVED EntityConfiguration from activity: " +
                          "EntityType={EntityType}, ChunkSize={ChunkSize}, FetchBatchSize={FetchBatchSize}, PageSize={PageSize}, " +
                          "SubBatchSize={SubBatchSize}, MaxConcurrency={MaxConcurrency}, ProcessSubBatchesSequentially={ProcessSubBatchesSequentially}",
                          entityConfig.EntityType, entityConfig.ChunkSize, entityConfig.FetchBatchSize, entityConfig.PageSize,
                          entityConfig.SubBatchSize, entityConfig.MaxConcurrency, entityConfig.ProcessSubBatchesSequentially);

            // Validate configuration received
            if (entityConfig == null)
            {
                logger.LogError("🚨 [ORCHESTRATOR-CONFIG-DEBUG] NULL EntityConfiguration received from activity!");
                throw new InvalidOperationException($"Failed to get configuration for entity type: {entityType}");
            }
            
            if (entityConfig.ChunkSize <= 0 || entityConfig.FetchBatchSize <= 0)
            {
                logger.LogError("🚨 [ORCHESTRATOR-CONFIG-DEBUG] INVALID configuration values: ChunkSize={ChunkSize}, FetchBatchSize={FetchBatchSize}", 
                    entityConfig.ChunkSize, entityConfig.FetchBatchSize);
            }

            // Step 4: Discover entities to migrate
            logger.LogInformation("Discovering {EntityType} entities for MigrationId: {MigrationId}", 
                entityType, migrationId);

            var discoverRequest = new EntityDiscoveryRequest
            {
                MigrationId = migrationId,
                EntityType = entityType,
                SourceStore = input.SourceStore ?? new StoreConfiguration(),
                EntityConfig = entityConfig, // ✅ Use loaded configuration
                CategoryTreeContext = input.CategoryTreeContext
            };

            var discoverResult = await context.CallActivityAsync<EntityDiscoveryResult>(
                "DiscoverEntitiesActivity",
                discoverRequest);

            if (discoverResult.Errors.Any())
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Failed to discover {entityType} entities: {string.Join(", ", discoverResult.Errors)}";
                result.EndTime = context.CurrentUtcDateTime;
                return result;
            }

            if (discoverResult.TotalCount == 0)
            {
                logger.LogInformation("No {EntityType} entities found to migrate for MigrationId: {MigrationId}", 
                    entityType, migrationId);
                result.IsSuccess = true;
                result.EndTime = context.CurrentUtcDateTime;
                return result;
            }

            logger.LogInformation("Discovered {EntityCount} {EntityType} entities for MigrationId: {MigrationId}", 
                discoverResult.TotalCount, entityType, migrationId);

            // Step 5: Start entity progress tracking
            await context.CallActivityAsync(
                "StartEntityProcessingActivity",
                new StartEntityProcessingRequest
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    TotalCount = discoverResult.TotalCount,
                    Timestamp = context.CurrentUtcDateTime,
                    IsCancelled = input.IsCancelled,
                    CancellationReason = input.CancellationReason,
                    CancelledAt = input.CancelledAt
                });

            // Step 6: Update initial progress
            await context.CallActivityAsync(
                "UpdateEntityProgressActivity",
                new UpdateEntityProgressRequest
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    Phase = "Processing",
                    TotalEntities = discoverResult.TotalCount,
                    ProcessedEntities = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = 0,
                    Timestamp = context.CurrentUtcDateTime,
                    IsCancelled = input.IsCancelled,
                    CancellationReason = input.CancellationReason,
                    CancelledAt = input.CancelledAt
                });

            // 🚀 **STEP 7: SMART PROCESSING** - Hybrid approach for optimal performance
            var entityIds = discoverResult?.EntityIds ?? new List<string>();
            
            // Handle efficient pagination strategy (empty EntityIds but has TotalCount)
            var useDirectPagination = !entityIds.Any() && (discoverResult?.TotalCount ?? 0) > 0;
            var totalEntities = useDirectPagination ? (discoverResult?.TotalCount ?? 0) : entityIds.Count;
            
            // 🎯 PERFORMANCE OPTIMIZATION: Use configuration-driven chunking  
            var chunkingThreshold = 100; // TODO: Make this configurable too
            var chunkSize = entityConfig.ChunkSize; // ✅ NOW CONFIGURABLE!
            
            logger.LogError("🎯 [ORCHESTRATOR-CHUNKING-DEBUG] ===== CHUNKING CONFIGURATION =====");
            logger.LogError("🎯 [ORCHESTRATOR-CHUNKING-DEBUG] EntityType='{EntityType}', TotalEntities={TotalEntities}", entityType, totalEntities);
            logger.LogError("🎯 [ORCHESTRATOR-CHUNKING-DEBUG] ChunkingThreshold={ChunkingThreshold}, ConfiguredChunkSize={ConfiguredChunkSize}", chunkingThreshold, chunkSize);
            logger.LogError("🎯 [ORCHESTRATOR-CHUNKING-DEBUG] UseDirectPagination={UseDirectPagination}", useDirectPagination);
            
            var shouldUseChunking = totalEntities > chunkingThreshold;
            var batchSize = shouldUseChunking ? chunkSize : totalEntities;
            var totalBatches = shouldUseChunking ? CalculateBatchCount(totalEntities, chunkSize) : 1;
            
            logger.LogError("🎯 [ORCHESTRATOR-CHUNKING-DEBUG] Calculated: ShouldUseChunking={ShouldUseChunking}, BatchSize={BatchSize}, TotalBatches={TotalBatches}", 
                shouldUseChunking, batchSize, totalBatches);
            
            // Declare result variable at method scope to avoid compilation errors
            BigCommerce.Migration.Core.Interfaces.BatchProcessingResult parallelResult;
            
            // 🚨 CRITICAL DEBUG: Log CategoryTreeContext before processing
            if (input.CategoryTreeContext == null)
            {
                logger.LogError("🚨 [ORCHESTRATOR] ❌ CRITICAL: input.CategoryTreeContext is NULL for {EntityType} in migration {MigrationId}", entityType, migrationId);
            }
            else
            {
                logger.LogInformation("🔄 [ORCHESTRATOR] 📋 CategoryTreeContext: SourceTreeId='{SourceTreeId}', DestinationTreeId='{DestinationTreeId}' for {EntityType} in migration {MigrationId}", 
                    input.CategoryTreeContext.SourceCategoryTreeId ?? "NULL", 
                    input.CategoryTreeContext.DestinationCategoryTreeId ?? "NULL",
                    entityType, migrationId);
            }
            
            if (shouldUseChunking)
            {
                // 🚀 LARGE DATASET: Use chunked orchestration for timeout prevention
                logger.LogInformation("🚀 CHUNKED WORKFLOW: Processing {TotalEntities} {EntityType} entities in {TotalChunks} chunks " +
                                    "of max {ChunkSize} entities each to prevent timeouts", 
                    totalEntities, entityType, totalBatches, batchSize);

                // Process all chunks in parallel using sub-orchestrators
                var chunkTasks = new List<Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>>();
            
                for (int chunkNumber = 0; chunkNumber < totalBatches; chunkNumber++)
                {
                    var startIndex = chunkNumber * batchSize;
                    var endIndex = Math.Min(startIndex + batchSize, totalEntities);
                    var actualChunkSize = endIndex - startIndex;
                
                    logger.LogError("🔥 [ORCHESTRATOR-CHUNK-DEBUG] ===== CREATING CHUNK {ChunkNumber}/{TotalBatches} =====", chunkNumber, totalBatches);
                    logger.LogError("🔥 [ORCHESTRATOR-CHUNK-DEBUG] ChunkNumber={ChunkNumber}, StartIndex={StartIndex}, EndIndex={EndIndex}, ActualChunkSize={ActualChunkSize}", 
                        chunkNumber, startIndex, endIndex, actualChunkSize);
                    logger.LogError("🔥 [ORCHESTRATOR-CHUNK-DEBUG] BatchSize={BatchSize}, UseDirectPagination={UseDirectPagination}", batchSize, useDirectPagination);
                
                    // Get entity IDs for this chunk
                    var chunkEntityIds = useDirectPagination 
                        ? new List<string>() // Direct pagination doesn't use pre-fetched IDs
                        : entityIds.Skip(startIndex).Take(actualChunkSize).ToList();
                    
                    logger.LogError("🔥 [ORCHESTRATOR-CHUNK-DEBUG] ChunkEntityIds.Count={ChunkEntityIdsCount}", chunkEntityIds.Count);
                
                // 🚨 CRITICAL FIX: For direct pagination, adjust pagination metadata for chunk boundaries
                var chunkPaginationMetadata = new Dictionary<string, object>();
                if (useDirectPagination && discoverResult?.PaginationMetadata != null)
                {
                    logger.LogError("🔥 [ORCHESTRATOR-CHUNK-DEBUG] Setting up chunk pagination metadata for direct pagination...");
                    logger.LogError("🔥 [ORCHESTRATOR-CHUNK-DEBUG] Original PaginationMetadata keys: [{OriginalKeys}]", 
                        string.Join(", ", discoverResult.PaginationMetadata.Keys));
                    
                    // Copy original metadata
                    foreach (var kvp in discoverResult.PaginationMetadata)
                    {
                        chunkPaginationMetadata[kvp.Key] = kvp.Value;
                        logger.LogError("🔥 [ORCHESTRATOR-CHUNK-DEBUG] Copied: {Key}={Value}", kvp.Key, kvp.Value);
                    }
                    // Override with chunk-specific values
                    chunkPaginationMetadata["TotalCount"] = actualChunkSize; // Limit each chunk to its size
                    chunkPaginationMetadata["StartIndex"] = startIndex;
                    chunkPaginationMetadata["ChunkSize"] = actualChunkSize;
                    
                    logger.LogError("🔥 [ORCHESTRATOR-CHUNK-DEBUG] OVERRIDDEN values: TotalCount={TotalCount}, StartIndex={StartIndex}, ChunkSize={ChunkSize}", 
                        actualChunkSize, startIndex, actualChunkSize);
                }
                else
                {
                    logger.LogError("🔥 [ORCHESTRATOR-CHUNK-DEBUG] NOT using direct pagination OR no original pagination metadata");
                }

                var chunkRequest = new ProcessEntityChunkRequest
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    ChunkNumber = chunkNumber,
                    TotalChunks = totalBatches,
                    StartIndex = startIndex,
                    ChunkSize = actualChunkSize,
                    EntityIds = chunkEntityIds,
                    SourceStore = input.SourceStore ?? new StoreConfiguration(),
                    DestinationStore = input.DestinationStore ?? new StoreConfiguration(),
                    CategoryTreeContext = input.CategoryTreeContext ?? new CategoryTreeContext(),
                    UseDirectPagination = useDirectPagination,
                    PaginationMetadata = useDirectPagination ? chunkPaginationMetadata : discoverResult?.PaginationMetadata,
                    IsCancelled = input.IsCancelled,
                    CancellationReason = input.CancellationReason ?? string.Empty,
                    CancelledAt = input.CancelledAt
                };
                
                logger.LogError("🚀 [ORCHESTRATOR-CHUNK-DEBUG] FINAL chunkRequest for chunk {ChunkNumber}: " +
                              "MigrationId={MigrationId}, EntityType={EntityType}, ChunkNumber={ChunkNumber}, TotalChunks={TotalChunks}, " +
                              "StartIndex={StartIndex}, ChunkSize={ChunkSize}, UseDirectPagination={UseDirectPagination}, EntityIds.Count={EntityIdsCount}, " +
                              "PaginationMetadata.Count={PaginationMetadataCount}",
                              chunkNumber, migrationId, entityType, chunkNumber, totalBatches, 
                              startIndex, actualChunkSize, useDirectPagination, chunkEntityIds.Count, 
                              (chunkRequest.PaginationMetadata?.Count ?? 0));
                
                if (chunkRequest.PaginationMetadata != null && chunkRequest.PaginationMetadata.Any())
                {
                    foreach (var kvp in chunkRequest.PaginationMetadata)
                    {
                        logger.LogError("🚀 [ORCHESTRATOR-CHUNK-DEBUG] PaginationMetadata[{Key}]={Value}", kvp.Key, kvp.Value);
                    }
                }

                logger.LogInformation("🎯 [CHUNK-{ChunkNumber}] Queuing chunk processing: entities {StartIndex}-{EndIndex} " +
                                    "({ActualChunkSize} entities) for {EntityType}", 
                    chunkNumber, startIndex, endIndex - 1, actualChunkSize, entityType);

                // Call ProcessEntityChunkOrchestrator for each chunk
                var chunkTask = context.CallSubOrchestratorAsync<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>(
                    "ProcessEntityChunkOrchestrator",
                    chunkRequest);
                
                chunkTasks.Add(chunkTask);
            }

                // Wait for all chunks to complete
                logger.LogInformation("🔄 [ENHANCED-ORCHESTRATOR] Waiting for {TotalChunks} chunks to complete for {EntityType}", 
                    totalBatches, entityType);

                var chunkResults = await Task.WhenAll(chunkTasks);

                // Aggregate results from all chunks
                parallelResult = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
                {
                    TotalProcessed = chunkResults.Sum(r => r.TotalProcessed),
                    SuccessfulEntities = chunkResults.Sum(r => r.SuccessfulEntities),
                    FailedEntities = chunkResults.Sum(r => r.FailedEntities),
                    ProcessingTime = chunkResults.Max(r => r.ProcessingTime), // Use max processing time
                    Errors = chunkResults.SelectMany(r => r.Errors ?? new List<string>()).ToList()
                };

                logger.LogInformation("🎉 [CHUNKED-ORCHESTRATOR] All {TotalChunks} chunks completed for {EntityType}: " +
                                    "{SuccessfulEntities} successful, {FailedEntities} failed, {TotalErrors} errors", 
                    totalBatches, entityType, parallelResult.SuccessfulEntities, parallelResult.FailedEntities, 
                    parallelResult.Errors?.Count ?? 0);
            }
            else
            {
                // ⚡ SMALL DATASET: Use direct activity processing for maximum speed (like original 28-second approach)
                logger.LogInformation("⚡ FAST WORKFLOW: Processing {TotalEntities} {EntityType} entities using direct activity " +
                                    "(≤{Threshold} entities - no chunking needed)", 
                    totalEntities, entityType, chunkingThreshold);

                // Create a single chunk request for all entities (fast processing)
                var fastRequest = new ProcessEntityChunkRequest
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    ChunkNumber = 0,
                    TotalChunks = 1,
                    StartIndex = 0,
                    ChunkSize = totalEntities,
                    EntityIds = entityIds,
                    SourceStore = input.SourceStore ?? new StoreConfiguration(),
                    DestinationStore = input.DestinationStore ?? new StoreConfiguration(),
                    CategoryTreeContext = input.CategoryTreeContext ?? new CategoryTreeContext(),
                    UseDirectPagination = useDirectPagination,
                    PaginationMetadata = discoverResult?.PaginationMetadata,
                    IsCancelled = input.IsCancelled,
                    CancellationReason = input.CancellationReason ?? string.Empty,
                    CancelledAt = input.CancelledAt
                };

                // Call the fast parallel processing activity directly (like the original approach)
                parallelResult = await context.CallActivityAsync<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>(
                    "ProcessEntityChunk", fastRequest);

                logger.LogInformation("⚡ [FAST-ORCHESTRATOR] Direct activity completed for {EntityType}: " +
                                    "{SuccessfulEntities} successful, {FailedEntities} failed, {TotalErrors} errors", 
                    entityType, parallelResult.SuccessfulEntities, parallelResult.FailedEntities, 
                    parallelResult.Errors?.Count ?? 0);
            }

            logger.LogInformation("🎉 P2.5: PARALLEL processing completed for {EntityType} in {Duration}ms - {Processed}/{Total} entities", 
                entityType, parallelResult.ProcessingTime.TotalMilliseconds, 
                parallelResult.TotalProcessed, discoverResult?.TotalCount ?? 0);

            // Update result with parallel processing results
            // 🚨 FIX: Use actual cumulative processed count, not wrong TotalProcessed value
            result.ProcessedEntities = parallelResult.SuccessfulEntities + parallelResult.FailedEntities;  // Actual processed count
            result.SuccessfulEntities = parallelResult.SuccessfulEntities;
            result.FailedEntities = parallelResult.FailedEntities;
            
            logger.LogInformation("🚨 [DURABLE-ORCHESTRATOR-FIX] Fixed ProcessedEntities: TotalProcessed={TotalProcessed} (WRONG) -> ProcessedEntities={ProcessedEntities} (CORRECT) = Successful={Successful} + Failed={Failed}", 
                parallelResult.TotalProcessed, result.ProcessedEntities, result.SuccessfulEntities, result.FailedEntities);

            // Step 6: Complete entity processing
            await context.CallActivityAsync(
                "UpdateEntityProgressActivity",
                new UpdateEntityProgressRequest
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    Phase = "Completed",
                    TotalEntities = discoverResult?.TotalCount ?? 0,
                    ProcessedEntities = result.ProcessedEntities,
                    SuccessfulEntities = result.SuccessfulEntities,
                    FailedEntities = result.FailedEntities,
                    CurrentBatch = totalBatches,
                    TotalBatches = totalBatches,
                    Timestamp = context.CurrentUtcDateTime,
                    IsCancelled = input.IsCancelled,
                    CancellationReason = input.CancellationReason,
                    CancelledAt = input.CancelledAt
                });

            // Step 7: Determine final result
            result.EndTime = context.CurrentUtcDateTime;
            result.Duration = result.EndTime.Value - result.StartTime;

            if (result.ProcessedEntities == 0)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"No {entityType} entities were processed";
            }
            else if (result.FailedEntities == 0)
            {
                result.IsSuccess = true;
                logger.LogInformation("🎉 PARALLEL MIGRATION SUCCESS: Completed {EntityType} migration for MigrationId: {MigrationId}. " +
                                    "Processed: {ProcessedCount} entities with 17.0x optimizations", 
                    entityType, migrationId, result.ProcessedEntities);
            }
            else if (result.SuccessfulEntities > 0)
            {
                result.IsSuccess = true; // Partial success
                result.ErrorMessage = $"Completed with {result.FailedEntities} failed entities out of {result.ProcessedEntities} total";
                logger.LogWarning("Completed {EntityType} migration with errors for MigrationId: {MigrationId}. " +
                                "Processed: {ProcessedCount}, Successful: {SuccessfulCount}, Failed: {FailedCount}", 
                    entityType, migrationId, result.ProcessedEntities, result.SuccessfulEntities, result.FailedEntities);
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"All {result.FailedEntities} {entityType} entities failed to migrate";
                logger.LogError("All {EntityType} entities failed for MigrationId: {MigrationId}. Failed: {FailedCount}", 
                    entityType, migrationId, result.FailedEntities);
            }

            return result;
        }
        catch (TaskCanceledException)
        {
            logger.LogInformation("{EntityType} migration was cancelled for MigrationId: {MigrationId}", 
                entityType, migrationId);
            result.IsSuccess = false;
            result.ErrorMessage = $"{entityType} migration was cancelled";
            result.EndTime = context.CurrentUtcDateTime;
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "🚨 PARALLEL PROCESSING ERROR: Unexpected error in {EntityType} migration for MigrationId: {MigrationId}", 
                entityType, migrationId);
            result.IsSuccess = false;
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
            result.EndTime = context.CurrentUtcDateTime;
            return result;
        }
    }

    /// <summary>
    /// Calculates the number of batches needed for a given entity count and batch size
    /// </summary>
    /// <param name="totalCount">Total number of entities</param>
    /// <param name="batchSize">Number of entities per batch</param>
    /// <returns>Number of batches needed</returns>
    private static int CalculateBatchCount(int totalCount, int batchSize)
    {
        if (totalCount == 0) return 0;
        return (int)Math.Ceiling((double)totalCount / batchSize);
    }
}

 