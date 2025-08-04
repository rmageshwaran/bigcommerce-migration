using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.Orchestration.Orchestrators;

/// <summary>
/// Entity-specific migration sub-orchestrator that handles batch processing,
/// rate limiting, and progress tracking for individual entity types
/// </summary>
public class EntityMigrationOrchestrator
{
    private readonly ILogger<EntityMigrationOrchestrator> _logger;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ISignalREventFactory _signalREventFactory; // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
    private readonly IParallelBatchProcessingPipeline _parallelPipeline;
    private readonly IEnhancedParallelProcessor _parallelProcessor;

    // Default batch sizes by entity type [[memory:2322834]]
    private static readonly Dictionary<string, int> DefaultBatchSizes = new()
    {
        ["categories"] = 25,
        ["products"] = 10,
        ["brands"] = 50,
        ["variants"] = 20,
        ["images"] = 15,
        ["modifiers"] = 30
    };

    public EntityMigrationOrchestrator(
        ILogger<EntityMigrationOrchestrator> logger, 
        IProgressEventPublisher progressEventPublisher,
        ISignalREventFactory signalREventFactory, // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
        IParallelBatchProcessingPipeline parallelPipeline,
        IEnhancedParallelProcessor parallelProcessor)
    {
        _logger = logger;
        _progressEventPublisher = progressEventPublisher;
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory)); // 🎯 CENTRALIZED SIGNALR: Store factory reference
        _parallelPipeline = parallelPipeline;
        _parallelProcessor = parallelProcessor;
    }

    /// <summary>
    /// Main entity migration orchestrator function
    /// </summary>
    /// <param name="context">Durable orchestration context</param>
    /// <returns>Entity migration result</returns>
    [Function("EntityMigrationOrchestrator")]
    public async Task<EntityMigrationResult> RunEntityMigrationOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var startTime = context.CurrentUtcDateTime; // ✅ Use deterministic datetime
        var request = context.GetInput<EntityMigrationRequest>();

        // Initialize result with deterministic values
        var result = new EntityMigrationResult
        {
            EntityType = request.EntityType,
            TotalEntities = 0,
            ProcessedEntities = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            Mappings = new List<EntityMapping>(),
            Errors = new List<string>(),
            ProcessingTime = TimeSpan.Zero
        };

        try
        {
            // Step 0: Check for cancellation before starting
            var isCancelled = await context.CallActivityAsync<bool>("CheckMigrationCancellation", request.MigrationId);
            if (isCancelled)
            {
                result.Errors.Add($"{request.EntityType} migration was cancelled before processing started");
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                return result;
            }

            // Step 1: Validate request
            context.SetCustomStatus($"Starting {request.EntityType} migration");
            
            if (!request.IsValid())
            {
                var validationErrors = request.GetValidationErrors();
                result.Errors.AddRange(validationErrors);
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                return result;
            }

            // Step 2: Discover entities to migrate
            context.SetCustomStatus($"Discovering {request.EntityType} entities");
            
            var discoveryResult = await DiscoverEntitiesWithRetry(context, request);
            
            // 🎯 CRITICAL FIX: Handle both ID-based and page-based discovery strategies
            bool hasEntities = false;
            
            if (discoveryResult == null)
            {
                context.SetCustomStatus($"Discovery failed for {request.EntityType} entities");
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                return result;
            }
            
            // Check for entities based on strategy type
            if (discoveryResult.PaginationMetadata?.ContainsKey("UseDirectPagination") == true && 
                discoveryResult.PaginationMetadata["UseDirectPagination"].ToString() == "True")
            {
                // Page-based strategy: Check TotalCount instead of EntityIds
                hasEntities = discoveryResult.TotalCount > 0;
                _logger.LogInformation($"📄 Page-based discovery for {request.EntityType}: TotalCount={discoveryResult.TotalCount}, HasEntities={hasEntities}");
            }
            else
            {
                // ID-based strategy: Check EntityIds count
                hasEntities = discoveryResult.EntityIds.Count > 0;
                _logger.LogInformation($"📋 ID-based discovery for {request.EntityType}: EntityIds={discoveryResult.EntityIds.Count}, HasEntities={hasEntities}");
            }
            
            if (!hasEntities)
            {
                context.SetCustomStatus($"No {request.EntityType} entities found to migrate");
                result.ProcessingTime = context.CurrentUtcDateTime - startTime;
                return result;
            }

            result.TotalEntities = discoveryResult.TotalCount;
            
            // Step 3: Create batches for processing

            
            // 🚀 ALL ENTITIES USE PAGE-BASED PROCESSING: Simplified timeout-safe approach
            _logger.LogInformation($"📄 Using page-based chunked processing for {request.EntityType} with {discoveryResult.TotalCount} entities", 
                request.EntityType, discoveryResult.TotalCount);
            
            // Use chunked sub-orchestration for timeout-safe processing
            context.SetCustomStatus($"Processing {request.EntityType} using page-based chunked approach");
            await ProcessWithParallelPipeline(context, request, discoveryResult, result);

            // Step 5: Finalize result
            result.ProcessingTime = context.CurrentUtcDateTime - startTime;
            context.SetCustomStatus($"Completed {request.EntityType} migration: {result.SuccessfulEntities}/{result.TotalEntities} successful");

            return result;
        }
        catch (Exception ex)
        {
            // Handle any unhandled exceptions
            context.SetCustomStatus($"{request.EntityType} migration failed");
            
            result.Errors.Add($"{request.EntityType} migration failed with error: {ex.Message}");
            
            // Also add the original error message for test compatibility
            if (!result.Errors.Contains(ex.Message))
            {
                result.Errors.Add(ex.Message);
            }
            
            result.ProcessingTime = context.CurrentUtcDateTime - startTime;
            return result;
        }
    }

    /// <summary>
    /// Discovers entities with retry logic for resilience
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <returns>Discovery result or null if failed</returns>
    private async Task<EntityDiscoveryResult?> DiscoverEntitiesWithRetry(
        TaskOrchestrationContext context, 
        EntityMigrationRequest request)
    {
        try
        {
            var discoveryRequest = new
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                SourceStore = request.SourceStore,
                EntityConfig = request.EntityConfig
            };

            return await context.CallActivityAsync<EntityDiscoveryResult>("DiscoverEntitiesActivity", discoveryRequest);
        }
        catch (Exception ex)
        {
            throw new Exception($"Entity discovery failed: {ex.Message}", ex);
        }
    }





    /// <summary>
    /// Processes entities using chunked sub-orchestration to avoid Azure Functions timeout
    /// Uses dynamic rate limiting for optimal BigCommerce API performance
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <param name="discoveryResult">Discovery result with entities and metadata</param>
    /// <param name="result">Result to update</param>
    private async Task ProcessWithParallelPipeline(
        TaskOrchestrationContext context,
        EntityMigrationRequest request,
        EntityDiscoveryResult discoveryResult,
        EntityMigrationResult result)
    {
        try
        {
            _logger.LogInformation("🚀 Starting chunked sub-orchestration processing for {EntityType} in migration {MigrationId} " +
                                 "(timeout-safe approach)", 
                request.EntityType, request.MigrationId);

            // 🚀 LEVEL-BY-LEVEL: Check if this is Level-by-Level category processing BEFORE chunking
            // Only trigger for "categories", not "categories-level" (sub-orchestrators)
            var isCategories = request.EntityType.ToLowerInvariant() == "categories";
            var strategy = discoveryResult.PaginationMetadata?.GetValueOrDefault("Strategy")?.ToString();
            var isLevelByLevel = strategy == "LevelByLevel";
            
            _logger.LogInformation("🔍 [LEVEL-CHECK] EntityType: {EntityType}, IsCategories: {IsCategories}, Strategy: {Strategy}, IsLevelByLevel: {IsLevelByLevel}", 
                request.EntityType, isCategories, strategy, isLevelByLevel);
            
            if (isCategories && isLevelByLevel)
            {
                _logger.LogInformation("🚀 [LEVEL-BY-LEVEL] Detected Level-by-Level strategy - skipping normal chunking workflow");
                
                await ExecuteLevelByLevelCategoryMigrationAsync(context, request, discoveryResult, result);
                _logger.LogInformation("✅ [LEVEL-BY-LEVEL] Level-by-Level category migration completed successfully");
                
                // Early return - skip normal chunking workflow for Level-by-Level categories
                return;
            }

            // Calculate dynamic chunking strategy based on API health and rate limits
            var totalEntities = GetTotalEntitiesCount(discoveryResult.PaginationMetadata, discoveryResult.EntityIds.Count);
            
            // 🚀 CONCURRENCY: Use deterministic default concurrency (Durable Functions orchestrators cannot perform I/O)
            // TODO: Move dynamic rate limiting to an Activity for proper I/O handling
            var optimalConcurrentChunks = Math.Min(8, Math.Max(1, totalEntities / 50)); // 8 max concurrent chunks, ~50 entities per chunk
            var chunkSize = Math.Max(50, totalEntities / optimalConcurrentChunks); // Ensure minimum chunk size
            var totalChunks = (int)Math.Ceiling((double)totalEntities / chunkSize);

            _logger.LogInformation("🚀 [CHUNKING] Store {StoreId}: Using {ConcurrentChunks} concurrent chunks " +
                                 "(deterministic concurrency, no I/O in orchestrator)", 
                request.SourceStore.StoreId, optimalConcurrentChunks);

            _logger.LogInformation("📊 [CHUNKING] Splitting {TotalEntities} {EntityType} entities into {TotalChunks} chunks " +
                                 "of {ChunkSize} entities each (dynamic rate-limit-aware processing)",
                totalEntities, request.EntityType, totalChunks, chunkSize);

            // Create chunk processing tasks
            var chunkTasks = new List<Task<BatchProcessingResult>>();

            for (int chunkNumber = 0; chunkNumber < totalChunks; chunkNumber++)
            {
                var startIndex = chunkNumber * chunkSize;
                var actualChunkSize = Math.Min(chunkSize, totalEntities - startIndex);

                var chunkRequest = new ProcessEntityChunkRequest
                {
                    MigrationId = request.MigrationId,
                    EntityType = request.EntityType,
                    StartIndex = startIndex,
                    ChunkSize = actualChunkSize,
                    ChunkNumber = chunkNumber + 1, // 1-based for user-friendly logging
                    TotalChunks = totalChunks,
                    SourceStore = request.SourceStore,
                    DestinationStore = request.DestinationStore,
                    CategoryTreeContext = request.CategoryTreeContext,
                    PaginationMetadata = discoveryResult.PaginationMetadata,
                    UseDirectPagination = true, // All strategies now use page-based processing
                    EntityIds = (discoveryResult.PaginationMetadata?.ContainsKey("UseDirectPagination") == true &&
                               discoveryResult.PaginationMetadata["UseDirectPagination"].ToString() == "True") ||
                              (discoveryResult.PaginationMetadata?.ContainsKey("UseApiFiltering") == true &&
                               discoveryResult.PaginationMetadata["UseApiFiltering"].ToString() == "True")
                        ? discoveryResult.EntityIds.Skip(startIndex).Take(actualChunkSize).ToList() // Use placeholder EntityIds for chunking
                        : discoveryResult.EntityIds.Skip(startIndex).Take(actualChunkSize).ToList(), // For hierarchical
                    IsCancelled = request.IsCancelled,
                    CancellationReason = request.CancellationReason ?? "",
                    CancelledAt = request.CancelledAt
                };

                // Launch sub-orchestrator for this chunk
                var chunkTask = context.CallSubOrchestratorAsync<BatchProcessingResult>(
                    "ProcessEntityChunkOrchestrator", 
                    chunkRequest);

                chunkTasks.Add(chunkTask);

                _logger.LogInformation("🎯 [CHUNK-{ChunkNumber}] Launched sub-orchestrator for entities {StartIndex}-{EndIndex} " +
                                     "({ActualChunkSize} entities)",
                    chunkNumber + 1, startIndex, startIndex + actualChunkSize - 1, actualChunkSize);
            }

            // Wait for all chunks to complete in parallel
            _logger.LogInformation("⏳ Waiting for {TotalChunks} chunk sub-orchestrators to complete in parallel...", totalChunks);
            var chunkResults = await Task.WhenAll(chunkTasks);
            _logger.LogInformation("✅ [ORCHESTRATOR] All {TotalChunks} chunk sub-orchestrators completed successfully", totalChunks);

            // Aggregate results from all chunks
            var aggregatedResult = AggregateChunkResults(chunkResults, request.EntityType, totalChunks);
            _logger.LogInformation("📊 [ORCHESTRATOR] Aggregated results: {SuccessfulEntities} successful, {FailedEntities} failed", 
                aggregatedResult.SuccessfulEntities, aggregatedResult.FailedEntities);
            
            // Update main result with aggregated data
            result.ProcessedEntities = aggregatedResult.TotalProcessed;
            result.SuccessfulEntities = aggregatedResult.SuccessfulEntities;
            result.FailedEntities = aggregatedResult.FailedEntities;
            result.Errors.AddRange(aggregatedResult.Errors ?? new List<string>());



            _logger.LogInformation("✅ Chunked sub-orchestration processing completed for {EntityType}: " +
                                 "{SuccessfulEntities}/{TotalProcessed} successful across {TotalChunks} chunks " +
                                 "(avg: {AvgPerChunk:F1} entities per chunk)",
                request.EntityType, result.SuccessfulEntities, result.ProcessedEntities, totalChunks,
                totalChunks > 0 ? (double)result.ProcessedEntities / totalChunks : 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during chunked sub-orchestration processing for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            
            result.Errors.Add($"Chunked processing failed: {ex.Message}");
            
            // Set failed count to total if we don't have better information
            if (result.ProcessedEntities == 0 && result.TotalEntities > 0)
            {
                result.ProcessedEntities = result.TotalEntities;
                result.FailedEntities = result.TotalEntities;
            }
        }
    }



    /// <summary>
    /// Calculates optimal chunk size based on entity type and dataset size to avoid timeouts
    /// </summary>
    /// <param name="entityType">Type of entity being processed</param>
    /// <param name="totalEntities">Total number of entities to process</param>
    /// <returns>Optimal chunk size for timeout-safe processing</returns>
    private int CalculateOptimalChunkSize(string entityType, int totalEntities)
    {
        // Base chunk sizes designed to complete well under 10-minute timeout limit
        var baseChunkSize = entityType.ToLowerInvariant() switch
        {
            "products" => 200,      // Products are more complex, smaller chunks
            "categories" => 500,    // Categories are simpler, larger chunks
            "brands" => 500,        // Brands are simple, larger chunks
            "variants" => 100,      // Variants can be complex, smaller chunks
            "images" => 50,         // Images require file processing, smallest chunks
            _ => 300                // Default safe chunk size
        };

        // Adjust based on dataset size
        if (totalEntities < 100)
        {
            // Small dataset - process in single chunk
            return totalEntities;
        }
        else if (totalEntities > 10000)
        {
            // Very large dataset - use smaller chunks for better parallelization
            return Math.Max(baseChunkSize / 2, 100);
        }
        else
        {
            // Medium dataset - use base chunk size
            return baseChunkSize;
        }
    }

    /// <summary>
    /// Aggregates results from multiple chunk processing operations
    /// </summary>
    /// <param name="chunkResults">Results from individual chunks</param>
    /// <param name="entityType">Type of entity processed</param>
    /// <param name="totalChunks">Total number of chunks processed</param>
    /// <returns>Aggregated batch processing result</returns>
    private BatchProcessingResult AggregateChunkResults(
        BatchProcessingResult[] chunkResults, 
        string entityType, 
        int totalChunks)
    {
        var aggregated = new BatchProcessingResult
        {
            TotalProcessed = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            ProcessingTime = TimeSpan.Zero,
            Errors = new List<string>()
        };

        var successfulChunks = 0;
        var maxProcessingTime = TimeSpan.Zero;

        foreach (var chunkResult in chunkResults)
        {
            if (chunkResult != null)
            {
                aggregated.TotalProcessed += chunkResult.TotalProcessed;
                aggregated.SuccessfulEntities += chunkResult.SuccessfulEntities;
                aggregated.FailedEntities += chunkResult.FailedEntities;
                
                if (chunkResult.SuccessfulEntities > 0)
                {
                    successfulChunks++;
                }

                // Track the longest processing time (chunks run in parallel)
                if (chunkResult.ProcessingTime > maxProcessingTime)
                {
                    maxProcessingTime = chunkResult.ProcessingTime;
                }

                // Collect all errors
                if (chunkResult.Errors?.Any() == true)
                {
                    aggregated.Errors.AddRange(chunkResult.Errors);
                }
            }
            else
            {
                aggregated.Errors.Add("One or more chunks returned null results");
            }
        }

        aggregated.ProcessingTime = maxProcessingTime; // Parallel execution time

        _logger.LogInformation("📊 [AGGREGATION] Combined results from {TotalChunks} chunks: " +
                             "{SuccessfulEntities}/{TotalProcessed} entities successful, " +
                             "{SuccessfulChunks}/{TotalChunks} chunks successful, " +
                             "processing time: {ProcessingTimeMs}ms",
            totalChunks, aggregated.SuccessfulEntities, aggregated.TotalProcessed,
            successfulChunks, totalChunks, aggregated.ProcessingTime.TotalMilliseconds);

        return aggregated;
    }

    /// <summary>
    /// Safely extracts total entities count from pagination metadata or falls back to entity IDs count
    /// </summary>
    /// <param name="paginationMetadata">Pagination metadata dictionary</param>
    /// <param name="entityIdsCount">Fallback count from entity IDs</param>
    /// <returns>Total entities count</returns>
    private int GetTotalEntitiesCount(Dictionary<string, object>? paginationMetadata, int entityIdsCount)
    {
        if (paginationMetadata?.ContainsKey("TotalCount") == true)
        {
            if (paginationMetadata["TotalCount"] is int totalCount)
            {
                return totalCount;
            }
            else if (paginationMetadata["TotalCount"]?.ToString() is string totalCountStr && 
                     int.TryParse(totalCountStr, out var parsedCount))
            {
                return parsedCount;
            }
        }

        return entityIdsCount;
    }

    /// <summary>
    /// 🚀 LEVEL-BY-LEVEL: Execute level-by-level category migration
    /// Each level uses the same EntityMigrationOrchestrator workflow with parent_id filtering
    /// Level 1: parent_id:in=<level_0_new_ids>, Level 2: parent_id:in=<level_1_new_ids>, etc.
    /// </summary>
    private async Task ExecuteLevelByLevelCategoryMigrationAsync(
        TaskOrchestrationContext context,
        EntityMigrationRequest request,
        EntityDiscoveryResult discoveryResult,
        EntityMigrationResult result)
    {
        _logger.LogInformation("🚀 [LEVEL-BY-LEVEL] Starting level-by-level category migration for migration {MigrationId}", 
            request.MigrationId);

        // 🚀 PROCESS CURRENT DISCOVERY RESULT FIRST
        // The discoveryResult contains the categories for the current level - process them first!
        var currentLevel = discoveryResult.PaginationMetadata?.GetValueOrDefault("Level") as int? ?? 0;
        _logger.LogInformation("🔄 [CURRENT-LEVEL] Processing current discovery result for Level {Level} with {Count} categories", 
            currentLevel, discoveryResult.EntityIds.Count);

        // Process the current level (from discovery result)
        if (discoveryResult.EntityIds.Any())
        {
            await ProcessCurrentLevelCategories(context, request, discoveryResult, result, currentLevel);
        }

        // Continue with subsequent levels
        var level = currentLevel + 1;
        var totalLevelsProcessed = 1;
        var totalCategoriesCreated = result.SuccessfulEntities;

        while (level <= 8) // BigCommerce supports max 8 levels
        {
            _logger.LogInformation("🔄 [LEVEL-{Level}] Starting level {Level} processing", level, level);

            // Get parent IDs from previous level
            var parentIds = await GetParentIdsFromPreviousLevel(context, request.MigrationId, level - 1);

            if (parentIds == null || !parentIds.Any())
            {
                _logger.LogInformation("✅ [LEVEL-{Level}] No parent IDs found for level {Level} - migration complete", level, level);
                break;
            }

            _logger.LogInformation("📊 [LEVEL-{Level}] Found {ParentCount} parent IDs: {ParentIds}", 
                level, parentIds.Count, string.Join(",", parentIds.Take(5)) + (parentIds.Count > 5 ? "..." : ""));

            // Create EntityMigrationRequest for this level using proper typed properties
            // 🚀 CRITICAL FIX: Use "categories-level" entity type to prevent infinite Level-by-Level loops
            // Sub-orchestrators should use normal chunking, not Level-by-Level processing
            var levelRequest = new EntityMigrationRequest
            {
                MigrationId = request.MigrationId,
                EntityType = "categories-level", // Prevents Level-by-Level detection in sub-orchestrators
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext,
                EntityConfig = new EntityConfiguration
                {
                    EntityType = "categories", // Keep original type for strategy resolution
                    // ✅ USE TYPED PROPERTIES: No more Dictionary<string, object> serialization issues!
                    Level = level,
                    ParentIds = parentIds,
                    IncludeDeleted = false,
                    IncludeDrafts = false,
                    IncludeImages = true,
                    IncludeMetadata = true,
                    CreateMappings = true,
                    FieldMappings = new Dictionary<string, string>()
                },
                BatchSize = request.BatchSize,
                MaxRetries = request.MaxRetries,
                Settings = request.Settings
            };

            // Execute this level using the same orchestrator workflow
            var levelResult = await context.CallSubOrchestratorAsync<EntityMigrationResult>(
                "EntityMigrationOrchestrator",
                levelRequest);

            _logger.LogInformation("📊 [LEVEL-{Level}] Completed: {SuccessfulEntities}/{TotalEntities} categories created", 
                level, levelResult.SuccessfulEntities, levelResult.TotalEntities);

            // Update total results
            totalCategoriesCreated += levelResult.SuccessfulEntities;
            result.ProcessedEntities += levelResult.ProcessedEntities;
            result.SuccessfulEntities += levelResult.SuccessfulEntities;
            result.FailedEntities += levelResult.FailedEntities;
            result.Errors.AddRange(levelResult.Errors ?? new List<string>());

            if (levelResult.SuccessfulEntities == 0)
            {
                _logger.LogInformation("✅ [LEVEL-{Level}] No categories created at level {Level} - migration complete", level, level);
                break;
            }

            totalLevelsProcessed++;
            level++;
        }

        _logger.LogInformation("🎉 [LEVEL-BY-LEVEL] Level-by-level migration completed! " +
                             "Levels processed: {TotalLevels}, Total categories: {TotalCategories}",
            totalLevelsProcessed, totalCategoriesCreated);
    }

    /// <summary>
    /// Process the current level categories directly from the discovery result
    /// </summary>
    private async Task ProcessCurrentLevelCategories(
        TaskOrchestrationContext context,
        EntityMigrationRequest request,
        EntityDiscoveryResult discoveryResult,
        EntityMigrationResult result,
        int currentLevel)
    {
        _logger.LogInformation("🚀 [PROCESS-CURRENT] Processing {Count} categories for level {Level}", 
            discoveryResult.EntityIds.Count, currentLevel);

        // Create a standard chunked processing request for the current level categories
        var totalEntities = discoveryResult.EntityIds.Count;
        var chunkSize = Math.Max(50, totalEntities / 8); // Use reasonable chunk size
        var totalChunks = (int)Math.Ceiling((double)totalEntities / chunkSize);

        _logger.LogInformation("🔢 [PROCESS-CURRENT] Processing {TotalEntities} entities in {TotalChunks} chunks of size {ChunkSize}", 
            totalEntities, totalChunks, chunkSize);

        var chunkTasks = new List<Task<BatchProcessingResult>>();

        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            var startIndex = chunkIndex * chunkSize;
            var chunkEntityIds = discoveryResult.EntityIds.Skip(startIndex).Take(chunkSize).ToList();
            
            var chunkRequest = new
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                EntityIds = chunkEntityIds,
                ChunkIndex = chunkIndex,
                TotalChunks = totalChunks,
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext,
                Level = currentLevel
            };

            _logger.LogInformation("📦 [CHUNK-{ChunkIndex}] Processing chunk {ChunkIndex} with {Count} entities", 
                chunkIndex, chunkIndex, chunkEntityIds.Count);

            var chunkTask = context.CallSubOrchestratorAsync<BatchProcessingResult>(
                "ProcessEntityChunkOrchestrator",
                chunkRequest);

            chunkTasks.Add(chunkTask);
        }

        // Wait for all chunks to complete
        var chunkResults = await Task.WhenAll(chunkTasks);

        // Update the result with chunk processing outcomes
        foreach (var chunkResult in chunkResults)
        {
            result.SuccessfulEntities += chunkResult.SuccessfulEntities;
            result.FailedEntities += chunkResult.FailedEntities;
            result.ProcessedEntities += chunkResult.TotalProcessed;
            result.BatchResults.Add(chunkResult);
            result.Mappings.AddRange(chunkResult.EntityMappings);
            result.Errors.AddRange(chunkResult.Errors);
        }

        _logger.LogInformation("✅ [PROCESS-CURRENT] Level {Level} completed: {Successful}/{Total} successful", 
            currentLevel, result.SuccessfulEntities, totalEntities);
    }

    /// <summary>
    /// Get parent IDs from the previous level using entity mappings
    /// </summary>
    private async Task<List<string>?> GetParentIdsFromPreviousLevel(
        TaskOrchestrationContext context,
        string migrationId,
        int previousLevel)
    {
        try
        {
            _logger.LogInformation("🔍 [GET-PARENT-IDS] Getting parent IDs from level {PreviousLevel} for migration {MigrationId}", 
                previousLevel, migrationId);

            // Get all entity mappings for categories
            var mappingsRequest = new RetrieveEntityMappingsRequest
            {
                MigrationId = migrationId,
                EntityType = "categories"
            };

            var allMappings = await context.CallActivityAsync<List<EntityMapping>>(
                "RetrieveEntityMappingsActivity",
                mappingsRequest);

            if (allMappings == null || !allMappings.Any())
            {
                _logger.LogWarning("⚠️ [GET-PARENT-IDS] No entity mappings found for migration {MigrationId}", migrationId);
                return null;
            }

            // 🚀 LEVEL-BY-LEVEL: Filter mappings by previous level to get correct parent IDs
            // Only get source IDs from the previous level, not all levels
            _logger.LogDebug("🔍 [LEVEL-FILTER] Starting to filter {TotalMappings} mappings for level {PreviousLevel}", 
                allMappings.Count, previousLevel);
            
            var previousLevelMappings = allMappings.Where(m => 
            {
                if (string.IsNullOrEmpty(m.Metadata)) 
                {
                    _logger.LogTrace("🔍 [LEVEL-FILTER] Skipping mapping {SourceId} -> {DestinationId} - no metadata", 
                        m.SourceId, m.DestinationId);
                    return false;
                }
                
                try
                {
                    var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(m.Metadata);
                    if (metadata?.TryGetValue("Level", out var levelValue) == true)
                    {
                        var levelNumber = levelValue.ToString();
                        var matches = levelNumber == previousLevel.ToString();
                        
                        _logger.LogTrace("🔍 [LEVEL-FILTER] Mapping {SourceId} -> {DestinationId}: Level={MappingLevel}, Target={PreviousLevel}, Match={Matches}", 
                            m.SourceId, m.DestinationId, levelNumber, previousLevel, matches);
                        
                        return matches;
                    }
                    else
                    {
                        _logger.LogTrace("🔍 [LEVEL-FILTER] Skipping mapping {SourceId} -> {DestinationId} - no Level in metadata", 
                            m.SourceId, m.DestinationId);
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse metadata for mapping {SourceId} -> {DestinationId}", m.SourceId, m.DestinationId);
                }
                
                return false;
            }).ToList();

            // Use SOURCE IDs from the previous level to fetch children from source store
            var parentIds = previousLevelMappings.Select(m => m.SourceId).ToList();

            _logger.LogInformation("🔍 [LEVEL-FILTER] Total mappings: {TotalCount}, Level {PreviousLevel} mappings: {LevelCount}", 
                allMappings.Count, previousLevel, previousLevelMappings.Count);
            _logger.LogInformation("✅ [GET-PARENT-IDS] Found {Count} SOURCE parent IDs from level {PreviousLevel}: {ParentIds}", 
                parentIds.Count, previousLevel, string.Join(",", parentIds.Take(5)) + (parentIds.Count > 5 ? "..." : ""));
            _logger.LogDebug("🔍 [GET-PARENT-IDS] Using SOURCE IDs to fetch children from source store (not destination IDs)");

            return parentIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [GET-PARENT-IDS] Failed to get parent IDs from level {PreviousLevel}: {ErrorMessage}", 
                previousLevel, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 🔗 PHASE 2: Execute category parent relationship fixup for Hybrid Two-Phase approach
    /// Called after all category chunks are created to fix parent-child relationships
    /// Uses enhanced two-phase statistics tracking for accurate reporting
    /// </summary>
    private async Task ExecuteCategoryPhase2FixupAsync(
        TaskOrchestrationContext context,
        EntityMigrationRequest request,
        EntityDiscoveryResult discoveryResult,
        EntityMigrationResult result)
    {
        _logger.LogInformation("🔗 [PHASE-2] Starting category parent relationship fixup for migration {MigrationId}", 
            request.MigrationId);

        _logger.LogInformation("🔍 [PHASE-2-DEBUG] About to create Phase 1 statistics");
        
        // 📊 Create two-phase statistics from Phase 1 results
        var twoPhaseStats = result.CreatePhase1Statistics();
        
        _logger.LogInformation("✅ [PHASE-2-DEBUG] Phase 1 statistics created successfully");
        
        try
        {
            _logger.LogInformation("🔍 [PHASE-2-DEBUG] About to set custom status");
            context.SetCustomStatus("Phase 2: Fixing category parent relationships");
            _logger.LogInformation("✅ [PHASE-2-DEBUG] Custom status set successfully");

            _logger.LogInformation("🔍 [PHASE-2-DEBUG] About to extract hierarchy metadata");
            // Extract hierarchy metadata for Phase 2
            var hierarchyMetadata = discoveryResult.PaginationMetadata?.GetValueOrDefault("HierarchyMetadata") as Dictionary<string, object>
                ?? new Dictionary<string, object>();
            _logger.LogInformation("✅ [PHASE-2-DEBUG] Hierarchy metadata extracted successfully");

            // 🔍 TIMING FIX: Retrieve all entity mappings from Phase 1 before starting Phase 2
            _logger.LogInformation("🔍 [PHASE-2-MAPPINGS] Retrieving all entity mappings from Phase 1 for migration {MigrationId}", request.MigrationId);
            var mappingsRequest = new RetrieveEntityMappingsRequest
            {
                MigrationId = request.MigrationId,
                EntityType = "categories"
            };
            var phase1Mappings = await context.CallActivityAsync<List<EntityMapping>>(
                "RetrieveEntityMappingsActivity", 
                mappingsRequest);
            
            _logger.LogInformation("✅ [PHASE-2-MAPPINGS] Retrieved {MappingCount} entity mappings from Phase 1", phase1Mappings?.Count ?? 0);

            // Create Phase 2 request with mappings from Phase 1
            var phase2Request = new CategoryParentFixupRequest
            {
                MigrationId = request.MigrationId,
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                HierarchyMetadata = hierarchyMetadata,
                TotalCategories = result.ProcessedEntities,
                Phase1Mappings = phase1Mappings ?? new List<EntityMapping>(),
                CategoryTreeContext = request.CategoryTreeContext // ✅ FIXED: Pass tree context to Phase 2
            };

            var phase2StartTime = context.CurrentUtcDateTime;

            _logger.LogInformation("🎯 [PHASE-2] About to call FixCategoryParentRelationships activity for {TotalCategories} categories", 
                phase2Request.TotalCategories);

            CategoryParentFixupResult phase2Result;
            try
            {
                            // Execute Phase 2: Create child categories level by level
            phase2Result = await context.CallActivityAsync<CategoryParentFixupResult>(
                "CreateChildCategories", 
                phase2Request);

                _logger.LogInformation("✅ [PHASE-2] FixCategoryParentRelationships activity completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [PHASE-2] FixCategoryParentRelationships activity failed: {ErrorMessage}", ex.Message);
                
                // Create a failed result for Phase 2
                phase2Result = new CategoryParentFixupResult
                {
                    MigrationId = phase2Request.MigrationId,
                    TotalCategories = phase2Request.TotalCategories,
                    ProcessedCategories = 0,
                    SuccessfulUpdates = 0,
                    FailedUpdates = phase2Request.TotalCategories,
                    Errors = new List<string> { $"Phase 2 activity failed: {ex.Message}" }
                };
            }

            var phase2Duration = context.CurrentUtcDateTime - phase2StartTime;

            // 📊 Add Phase 2 statistics
            twoPhaseStats.Phase2 = new PhaseStatistics
            {
                PhaseName = "Parent Relationship Fixup",
                TotalEntities = phase2Result.TotalCategories,
                ProcessedEntities = phase2Result.ProcessedCategories,
                SuccessfulEntities = phase2Result.SuccessfulUpdates,
                FailedEntities = phase2Result.FailedUpdates,
                Duration = phase2Duration,
                Errors = new List<string>(phase2Result.Errors)
            };

            // 📊 Calculate overall statistics
            twoPhaseStats.Overall.FullySuccessfulEntities = Math.Min(
                twoPhaseStats.Phase1.SuccessfulEntities,  // Can't have more successful than Phase 1
                phase2Result.SuccessfulUpdates);          // Only count categories with fixed relationships

            twoPhaseStats.Overall.PartiallySuccessfulEntities = 
                twoPhaseStats.Phase1.SuccessfulEntities - twoPhaseStats.Overall.FullySuccessfulEntities;

            twoPhaseStats.Overall.TotalDuration = twoPhaseStats.Phase1.Duration + phase2Duration;
            twoPhaseStats.Overall.AllErrors.AddRange(phase2Result.Errors);

            // 📊 Update EntityMigrationResult with comprehensive statistics
            result.UpdateWithTwoPhaseStatistics(twoPhaseStats);

            // 📡 SIGNALR INTEGRATION: Broadcast two-phase progress completion
            // SKIP: Temporarily skip SignalR to prevent hanging - we can add this back once SignalR issues are resolved
            _logger.LogWarning("⚠️ [PHASE-2-COMPLETION-SKIP] Temporarily skipping BroadcastTwoPhaseProgressAsync for Phase 2 completion to prevent hanging.");
            // await BroadcastTwoPhaseProgressAsync(context, request, twoPhaseStats, 2, "Parent relationships fixed");

            // 📝 Enhanced logging with detailed breakdown
            _logger.LogInformation("✅ [PHASE-2] Two-phase category migration completed! {Summary}", 
                twoPhaseStats.GetSummary());
            
            _logger.LogInformation("📊 [PHASE-2] Detailed breakdown: " +
                                 "Phase 1 (Creation): {Phase1Success}/{Phase1Total} | " +
                                 "Phase 2 (Relationships): {Phase2Success}/{Phase2Total} | " +
                                 "Overall Classification: {Classification}",
                twoPhaseStats.Phase1.SuccessfulEntities, twoPhaseStats.Phase1.TotalEntities,
                phase2Result.SuccessfulUpdates, phase2Result.TotalCategories,
                twoPhaseStats.Overall.GetClassification());

            // Update status with meaningful two-phase information
            var statusMessage = twoPhaseStats.Overall.GetClassification() switch
            {
                ResultClassification.FullSuccess => 
                    $"Categories fully migrated: {twoPhaseStats.Overall.FullySuccessfulEntities}/{twoPhaseStats.Overall.TotalEntities} (100% success)",
                ResultClassification.MostlySuccessful => 
                    $"Categories mostly successful: {twoPhaseStats.Overall.FullySuccessfulEntities}/{twoPhaseStats.Overall.TotalEntities} fully migrated, {twoPhaseStats.Overall.PartiallySuccessfulEntities} partial",
                _ => 
                    $"Categories completed with issues: {twoPhaseStats.Overall.FullySuccessfulEntities} fully successful, {twoPhaseStats.Overall.PartiallySuccessfulEntities} partial, {twoPhaseStats.Overall.FailedEntities} failed"
            };
            
            context.SetCustomStatus(statusMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE-2] Category parent relationship fixup failed for migration {MigrationId}: {ErrorMessage}", 
                request.MigrationId, ex.Message);
            
            // 📊 Handle Phase 2 failure in statistics
            twoPhaseStats.Phase2 = new PhaseStatistics
            {
                PhaseName = "Parent Relationship Fixup",
                TotalEntities = result.SuccessfulEntities,
                ProcessedEntities = 0,
                SuccessfulEntities = 0,
                FailedEntities = result.SuccessfulEntities,
                Duration = TimeSpan.Zero,
                Errors = new List<string> { $"Phase 2 failed: {ex.Message}" }
            };

            // All Phase 1 successes become partial successes due to Phase 2 failure
            twoPhaseStats.Overall.FullySuccessfulEntities = 0;
            twoPhaseStats.Overall.PartiallySuccessfulEntities = twoPhaseStats.Phase1.SuccessfulEntities;
            twoPhaseStats.Overall.AllErrors.Add($"Phase 2 (parent relationship fixup) failed: {ex.Message}");

            result.UpdateWithTwoPhaseStatistics(twoPhaseStats);
            
            context.SetCustomStatus($"Categories created but parent relationship fixup failed: {twoPhaseStats.Overall.PartiallySuccessfulEntities} categories need manual hierarchy fixing");
        }
    }

    /// <summary>
    /// Applies rate limiting with delays if necessary
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    private async Task ApplyRateLimiting(TaskOrchestrationContext context, EntityMigrationRequest request)
    {
        try
        {
            var rateLimitRequest = new
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                StoreId = request.SourceStore.StoreId
            };

            var rateLimitResult = await context.CallActivityAsync<RateLimitResult>("CheckRateLimitActivity", rateLimitRequest);

            if (!rateLimitResult.CanProceed && rateLimitResult.DelayMs > 0)
            {
                // ✅ Use deterministic timer instead of Task.Delay
                var delayUntil = context.CurrentUtcDateTime.AddMilliseconds(rateLimitResult.DelayMs);
                await context.CreateTimer(delayUntil, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Log rate limiting failure but don't stop processing
            _logger.LogWarning(ex, "Rate limiting check failed for {EntityType}", request.EntityType);
        }
    }

    /// <summary>
    /// Updates enhanced progress tracking with detailed information
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <param name="currentBatch">Current batch number</param>
    /// <param name="totalBatches">Total number of batches</param>
    /// <param name="result">Current result state</param>
    private async Task UpdateEnhancedProgress(
        TaskOrchestrationContext context,
        EntityMigrationRequest request,
        int currentBatch,
        int totalBatches,
        EntityMigrationResult result)
    {
        try
        {
            var enhancedProgressUpdate = new
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                CurrentBatch = currentBatch,
                TotalBatches = totalBatches,
                TotalEntities = result.TotalEntities,
                ProcessedEntities = result.ProcessedEntities,
                SuccessfulEntities = result.SuccessfulEntities,
                FailedEntities = result.FailedEntities,
                ProgressPercentage = totalBatches > 0 ? Math.Round((double)currentBatch / totalBatches * 100, 1) : 0,
                Phase = currentBatch >= totalBatches ? "Completed" : "Processing",
                CurrentActivity = $"Batch {currentBatch}/{totalBatches}",
                BatchSize = result.TotalEntities / totalBatches, // Average batch size
                RemainingBatches = Math.Max(0, totalBatches - currentBatch),
                RemainingEntities = Math.Max(0, result.TotalEntities - result.ProcessedEntities)
            };

            // Publish enhanced entity progress via queue event
            try
            {
                // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation with auto-populated base properties
                var entityProgressEvent = _signalREventFactory.CreateEntityProgress(request.MigrationId, new EntityProgressOptions
                {
                    EntityType = request.EntityType,
                    TotalCount = result.TotalEntities,
                    ProcessedCount = result.ProcessedEntities,
                    Status = currentBatch >= totalBatches ? "completed" : "processing"
                    // ✅ Base properties (Timestamp, IsCancelled, HubMethod) auto-populated by factory
                    // ✅ SuccessCount/FailureCount calculated from ProcessedCount/TotalCount
                    // ✅ Validation built-in
                    // ✅ Consistent naming enforced
                });

                await _progressEventPublisher.PublishEntityProgressAsync(entityProgressEvent);

                // Also publish batch progress if we have batch information
                if (currentBatch > 0)
                {
                    // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation with auto-populated base properties
                    var batchProgressEvent = _signalREventFactory.CreateBatchProgress(request.MigrationId, new BatchProgressOptions
                    {
                        EntityType = request.EntityType,
                        BatchNumber = currentBatch,
                        TotalBatches = totalBatches,
                        BatchSize = result.TotalEntities / totalBatches, // Average batch size
                        ProcessedCount = result.ProcessedEntities,
                        FailedCount = result.FailedEntities,
                        Status = currentBatch >= totalBatches ? "completed" : "processing",
                        ProcessingTime = TimeSpan.FromSeconds(1) // Approximate
                        // ✅ Base properties (Timestamp, IsCancelled, HubMethod) auto-populated by factory
                        // ✅ Validation built-in
                        // ✅ Consistent naming enforced
                    });

                    await _progressEventPublisher.PublishBatchProgressAsync(batchProgressEvent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish enhanced progress event for {EntityType}", request.EntityType);
                // Don't throw - progress events should not break the migration
            }
        }
        catch (Exception ex)
        {
            // Log progress update failure but don't stop processing
            _logger.LogWarning(ex, "Enhanced progress update failed for {EntityType}", request.EntityType);
        }
    }

    /// <summary>
    /// Updates progress tracking (legacy method for backward compatibility)
    /// </summary>
    /// <param name="context">Orchestration context</param>
    /// <param name="request">Entity migration request</param>
    /// <param name="currentBatch">Current batch number</param>
    /// <param name="totalBatches">Total number of batches</param>
    /// <param name="result">Current result state</param>
    private async Task UpdateProgress(
        TaskOrchestrationContext context,
        EntityMigrationRequest request,
        int currentBatch,
        int totalBatches,
        EntityMigrationResult result)
    {
        try
        {
            var progressUpdate = new
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                CurrentBatch = currentBatch,
                TotalBatches = totalBatches,
                TotalEntities = result.TotalEntities,
                ProcessedEntities = result.ProcessedEntities,
                SuccessfulEntities = result.SuccessfulEntities,
                FailedEntities = result.FailedEntities,
                ProgressPercentage = totalBatches > 0 ? Math.Round((double)currentBatch / totalBatches * 100, 1) : 0
            };

            // Publish entity progress via queue event
            try
            {
                // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation with auto-populated base properties
                var entityProgressEvent = _signalREventFactory.CreateEntityProgress(request.MigrationId, new EntityProgressOptions
                {
                    EntityType = request.EntityType,
                    TotalCount = result.TotalEntities,
                    ProcessedCount = result.ProcessedEntities,
                    Status = currentBatch >= totalBatches ? "completed" : "processing"
                    // ✅ Base properties (Timestamp, IsCancelled, HubMethod) auto-populated by factory
                    // ✅ SuccessCount/FailureCount calculated from ProcessedCount/TotalCount
                    // ✅ Validation built-in
                    // ✅ Consistent naming enforced
                });

                await _progressEventPublisher.PublishEntityProgressAsync(entityProgressEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish entity progress event for {EntityType}", request.EntityType);
                // Don't throw - progress events should not break the migration
            }
        }
        catch (Exception ex)
        {
            // Log progress update failure but don't stop processing
            _logger.LogWarning(ex, "Progress update failed for {EntityType}", request.EntityType);
        }
    }

    /// <summary>
    /// Aggregates results from individual batch processing
    /// </summary>
    /// <param name="result">Overall result to update</param>
    /// <param name="batchResult">Individual batch result</param>
    private static void AggregateBatchResults(EntityMigrationResult result, BatchProcessingResult batchResult)
    {
        result.ProcessedEntities += batchResult.TotalProcessed;
        result.SuccessfulEntities += batchResult.SuccessfulEntities;
        result.FailedEntities += batchResult.FailedEntities;

        // Add entity mappings
        if (batchResult.EntityMappings != null && batchResult.EntityMappings.Any())
        {
            result.Mappings.AddRange(batchResult.EntityMappings);
        }

        // Add batch-specific errors
        if (batchResult.Errors != null && batchResult.Errors.Any())
        {
            result.Errors.AddRange(batchResult.Errors);
        }
    }

    /// <summary>
    /// 📡 SIGNALR INTEGRATION: Broadcasts two-phase progress updates for real-time dashboard updates
    /// Provides detailed phase-by-phase progress information for enhanced user visibility
    /// </summary>
    private async Task BroadcastTwoPhaseProgressAsync(
        TaskOrchestrationContext context,
        EntityMigrationRequest request,
        TwoPhaseStatistics twoPhaseStats, 
        int currentPhase,
        string phaseDescription)
    {
        try
        {
            // Map TwoPhaseStatistics to TwoPhaseEntityProgressOptions
            var progressOptions = new TwoPhaseEntityProgressOptions
            {
                EntityType = request.EntityType,
                CurrentPhase = currentPhase,
                PhaseDescription = phaseDescription,
                Phase1 = new PhaseProgress
                {
                    PhaseName = twoPhaseStats.Phase1.PhaseName,
                    TotalEntities = twoPhaseStats.Phase1.TotalEntities,
                    ProcessedEntities = twoPhaseStats.Phase1.ProcessedEntities,
                    SuccessfulEntities = twoPhaseStats.Phase1.SuccessfulEntities,
                    FailedEntities = twoPhaseStats.Phase1.FailedEntities,
                    Status = DeterminePhaseStatus(twoPhaseStats.Phase1),
                    Duration = twoPhaseStats.Phase1.Duration
                },
                Phase2 = twoPhaseStats.Phase2 != null ? new PhaseProgress
                {
                    PhaseName = twoPhaseStats.Phase2.PhaseName,
                    TotalEntities = twoPhaseStats.Phase2.TotalEntities,
                    ProcessedEntities = twoPhaseStats.Phase2.ProcessedEntities,
                    SuccessfulEntities = twoPhaseStats.Phase2.SuccessfulEntities,
                    FailedEntities = twoPhaseStats.Phase2.FailedEntities,
                    Status = DeterminePhaseStatus(twoPhaseStats.Phase2),
                    Duration = twoPhaseStats.Phase2.Duration
                } : null,
                Overall = new TwoPhaseOverallProgress
                {
                    TotalEntities = twoPhaseStats.Overall.TotalEntities,
                    FullySuccessfulEntities = twoPhaseStats.Overall.FullySuccessfulEntities,
                    PartiallySuccessfulEntities = twoPhaseStats.Overall.PartiallySuccessfulEntities,
                    FailedEntities = twoPhaseStats.Overall.FailedEntities,
                    Classification = twoPhaseStats.Overall.GetClassification().ToString(),
                    TotalDuration = twoPhaseStats.Overall.TotalDuration
                },
                IsCancelled = false, // TODO: Add cancellation support
                CancellationReason = null,
                CancelledAt = null
            };

            // Create and publish the two-phase progress event
            _logger.LogInformation("🔍 [SIGNALR-DEBUG] About to create TwoPhaseEntityProgress event");
            var twoPhaseProgressEvent = _signalREventFactory.CreateTwoPhaseEntityProgress(request.MigrationId, progressOptions);
            _logger.LogInformation("✅ [SIGNALR-DEBUG] TwoPhaseEntityProgress event created successfully");

            // Also create a backward-compatible standard progress event for existing UI components
            _logger.LogInformation("🔍 [SIGNALR-DEBUG] About to convert to standard progress event");
            var standardProgressEvent = twoPhaseProgressEvent.ToStandardEntityProgress();
            _logger.LogInformation("✅ [SIGNALR-DEBUG] Standard progress event created successfully");

            // Publish both events for maximum compatibility with timeout to prevent hanging
            _logger.LogInformation("🔍 [SIGNALR-DEBUG] About to publish standard progress event via _progressEventPublisher");
            try 
            {
                // Add timeout to prevent indefinite hang
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _progressEventPublisher.PublishEntityProgressAsync(standardProgressEvent).WaitAsync(cts.Token);
                _logger.LogInformation("✅ [SIGNALR-DEBUG] Standard progress event published successfully");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("⚠️ [SIGNALR-TIMEOUT] SignalR publish timed out after 10 seconds. Two-Phase migration will continue.");
                // Don't fail the migration due to progress broadcasting timeout - let Phase 2 continue
            }
            catch (Exception ex)
            {
                _logger.LogWarning("⚠️ [SIGNALR-SKIP] Skipping SignalR publish due to error: {Error}. Two-Phase migration will continue.", ex.Message);
                // Don't fail the migration due to progress broadcasting issues - let Phase 2 continue
            }
            
            // TODO: Add PublishTwoPhaseEntityProgressAsync to IProgressEventPublisher for the enhanced event
            // For now, log the detailed progress information
            _logger.LogInformation("📡 [SIGNALR-TWOPHASE] Published progress: Phase {CurrentPhase}/2 - {PhaseDescription}. " +
                                 "Overall: {OverallProgress:F1}% ({FullySuccessful}/{Total} fully migrated)",
                currentPhase, phaseDescription, progressOptions.Overall.OverallProgress,
                progressOptions.Overall.FullySuccessfulEntities, progressOptions.Overall.TotalEntities);

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [SIGNALR-TWOPHASE] Failed to broadcast two-phase progress for migration {MigrationId}: {ErrorMessage}",
                request.MigrationId, ex.Message);
            // Don't fail the migration due to progress broadcasting issues
        }
    }

    /// <summary>
    /// Determines the status of a phase based on its statistics
    /// </summary>
    private static string DeterminePhaseStatus(PhaseStatistics phase)
    {
        if (phase.ProcessedEntities == 0) return "pending";
        if (phase.ProcessedEntities < phase.TotalEntities) return "processing";
        if (phase.FailedEntities == 0) return "completed";
        return "completed_with_errors";
    }
}

 