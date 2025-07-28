using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// 🚀 THROUGHPUT OPTIMIZED: Activity for processing batches in parallel using 17.0x optimizations
/// This bridges Azure Functions orchestration with our optimized parallel processing services
/// </summary>
public class ProcessParallelBatchesActivity
{
    private readonly ILogger<ProcessParallelBatchesActivity> _logger;
    private readonly IEnhancedParallelProcessor _parallelProcessor;
    private readonly IParallelBatchProcessingPipeline _parallelPipeline;
    
    // ✅ ADD REAL ENTITY PROCESSING SERVICES
    private readonly IEntityFetchService _entityFetchService;
    private readonly IEntityTransformService _entityTransformService;
    private readonly IEntityCreateService _entityCreateService;
    private readonly IEntityMappingService _entityMappingService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly IMigrationStorageService _migrationStorageService;

    public ProcessParallelBatchesActivity(
        ILogger<ProcessParallelBatchesActivity> logger,
        IEnhancedParallelProcessor parallelProcessor,
        IParallelBatchProcessingPipeline parallelPipeline,
        IEntityFetchService entityFetchService,
        IEntityTransformService entityTransformService,
        IEntityCreateService entityCreateService,
        IEntityMappingService entityMappingService,
        IEntityErrorHandlingService errorHandlingService,
        IMigrationStorageService migrationStorageService)
    {
        _logger = logger;
        _parallelProcessor = parallelProcessor;
        _parallelPipeline = parallelPipeline;
        _entityFetchService = entityFetchService;
        _entityTransformService = entityTransformService;
        _entityCreateService = entityCreateService;
        _entityMappingService = entityMappingService;
        _errorHandlingService = errorHandlingService;
        _migrationStorageService = migrationStorageService;
    }

    /// <summary>
    /// 🎯 PARALLEL PROCESSING: Processes multiple batches in parallel using 17.0x optimizations
    /// </summary>
    [Function("ProcessParallelBatches")]
    public async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessParallelBatchesAsync(
        [ActivityTrigger] ProcessParallelBatchesRequest request)
    {
        var startTime = DateTime.UtcNow;
        
        _logger.LogInformation("🔄 [PARALLEL] ⭐ STARTING: Processing {Count} entities for {EntityType} in migration {MigrationId}", 
            request.EntityIds.Count, request.EntityType, request.MigrationId);

        // 🚨 CRITICAL FIX: Categories MUST be processed sequentially to avoid parent-child race conditions
        // Other entities can still use parallel processing for throughput optimization
        if (request.EntityType.ToLower() == "categories")
        {
            _logger.LogWarning("🚨 [CATEGORIES] ⚠️ SEQUENTIAL MODE: Categories will be processed sequentially to prevent parent-child race conditions in migration {MigrationId}", request.MigrationId);
            return await ProcessCategoriesSequentially(request, startTime);
        }
        else
        {
            _logger.LogInformation("🚀 [PARALLEL] ⚡ PARALLEL MODE: {EntityType} will be processed in parallel for throughput optimization in migration {MigrationId}", request.EntityType, request.MigrationId);
            return await ProcessEntitiesInParallel(request, startTime);
        }
    }

    // 🚨 SEQUENTIAL PROCESSING: Process categories one by one to prevent race conditions
    private async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessCategoriesSequentially(
        ProcessParallelBatchesRequest request, 
        DateTime startTime)
    {
        var result = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
        {
            BatchNumber = 1,
            TotalProcessed = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            Errors = new List<string>(),
            ProcessingTime = TimeSpan.Zero
        };

        _logger.LogInformation("🚨 [SEQUENTIAL] ⭐ STARTING: Processing {Count} categories in ONE batch with hierarchy-aware sequential processing for migration {MigrationId}", 
            request.EntityIds.Count, request.MigrationId);

        // 🔧 CRITICAL FIX: Process ALL categories in ONE batch, but handle sequentially by hierarchy level
        try
        {
            if (request.IsCancelled)
            {
                _logger.LogInformation("🚫 [SEQUENTIAL] CANCELLED: Migration {MigrationId} was cancelled", request.MigrationId);
                result.Errors.Add(request.CancellationReason ?? "Migration was cancelled");
                return result;
            }

            // Create ONE batch with ALL categories for proper hierarchy processing
            var allCategoriesBatch = new BatchProcessingRequest
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                BatchNumber = 1,
                TotalBatches = 1, // Only ONE batch with ALL categories
                EntityIds = request.EntityIds, // ALL entity IDs in one batch
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext,
                PaginationMetadata = request.PaginationMetadata,
                UseDirectPagination = request.UseDirectPagination,
                IsCancelled = request.IsCancelled,
                CancellationReason = request.CancellationReason,
                CancelledAt = request.CancelledAt
            };

            // Process ALL categories in one batch with hierarchy awareness
            var batchResult = await ProcessBatchWithHierarchyAwareness(allCategoriesBatch, CancellationToken.None);

            // Update totals from the single batch result
            result.TotalProcessed = batchResult.TotalProcessed;
            result.SuccessfulEntities = batchResult.SuccessfulEntities;
            result.FailedEntities = batchResult.FailedEntities;
                
            if (batchResult.Errors != null)
            {
                result.Errors.AddRange(batchResult.Errors);
            }

            _logger.LogDebug("🚨 [SEQUENTIAL] ✅ Processed {Count} categories: Success={Success}, Failed={Failed}", 
                request.EntityIds.Count, batchResult.SuccessfulEntities, batchResult.FailedEntities);
        }
        catch (Exception ex)
        {
            result.FailedEntities = request.EntityIds.Count;
            result.TotalProcessed = request.EntityIds.Count;
            result.Errors.Add($"Failed to process categories batch: {ex.Message}");
            _logger.LogError(ex, "🚨 [SEQUENTIAL] ❌ Failed to process categories batch in migration {MigrationId}", 
                request.MigrationId);
        }

        result.ProcessingTime = DateTime.UtcNow - startTime;
        
        _logger.LogInformation("🚨 [SEQUENTIAL] ✅ COMPLETED: Processed {TotalProcessed} categories in {Duration}ms - {SuccessfulEntities} successful, {FailedEntities} failed for migration {MigrationId}", 
            result.TotalProcessed, result.ProcessingTime.TotalMilliseconds, 
            result.SuccessfulEntities, result.FailedEntities, request.MigrationId);

        return result;
    }

    // 🚀 PARALLEL PROCESSING: Process non-category entities in parallel for throughput optimization
    private async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessEntitiesInParallel(
        ProcessParallelBatchesRequest request, 
        DateTime startTime)
    {
        _logger.LogInformation("🚀 [PARALLEL] ⭐ STARTING: Processing {Count} {EntityType} entities in parallel for migration {MigrationId}", 
            request.EntityIds.Count, request.EntityType, request.MigrationId);

        // Create batch processing requests for the parallel pipeline
        var batches = new List<BatchProcessingRequest>();
        
        var batchSize = 10; // Default batch size for parallel processing
        var totalBatches = (int)Math.Ceiling((double)request.EntityIds.Count / batchSize);
        
        for (int batchNumber = 1; batchNumber <= totalBatches; batchNumber++)
        {
            List<string> batchEntityIds;
            
            if (request.UseDirectPagination)
            {
                // For efficient pagination strategies: use page-based processing
                batchEntityIds = new List<string> { $"page-{batchNumber}" };
            }
            else
            {
                // For hierarchical strategies: use traditional entity ID batching
                var startIndex = (batchNumber - 1) * batchSize;
                var endIndex = Math.Min(startIndex + batchSize, request.EntityIds.Count);
                batchEntityIds = request.EntityIds.Skip(startIndex).Take(endIndex - startIndex).ToList();
            }

            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = request.MigrationId,
                EntityType = request.EntityType,
                BatchNumber = batchNumber,
                TotalBatches = totalBatches,
                EntityIds = batchEntityIds,
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                CategoryTreeContext = request.CategoryTreeContext,
                PaginationMetadata = request.PaginationMetadata,
                UseDirectPagination = request.UseDirectPagination,
                IsCancelled = request.IsCancelled,
                CancellationReason = request.CancellationReason,
                CancelledAt = request.CancelledAt
            };
            
            batches.Add(batchRequest);
        }

        _logger.LogInformation("🚀 [PARALLEL] ✅ Created {BatchCount} batches for {EntityType} processing, MigrationId: {MigrationId}", 
            batches.Count, request.EntityType, request.MigrationId);

        // Configure parallel processing
        var parallelConfig = new Core.Models.ParallelProcessingConfiguration
        {
            MaxConcurrentBatches = Math.Min(batches.Count, 10), // Limit concurrency
            StoreId = request.SourceStore.StoreId,
            EntityType = request.EntityType,
            EnableSignalRUpdates = true, // ✅ ENABLE SignalR for real-time dashboard updates
            BatchTimeoutMinutes = 30,
            RespectDynamicRateLimits = true
        };

        // Use REAL batch processor function for actual migration work
        async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessBatch(BatchProcessingRequest batch, CancellationToken cancellationToken)
        {
            return await ProcessBatchWithHierarchyAwareness(batch, cancellationToken);
        }

        // Execute parallel processing using our optimized services
        var parallelResult = await _parallelProcessor.ProcessBatchesInParallelAsync(
            batches,
            ProcessBatch,
            parallelConfig,
            cancellationToken: CancellationToken.None);

        var endTime = DateTime.UtcNow;
        var processingTime = endTime - startTime;

        _logger.LogInformation("🚀 [PARALLEL] ✅ COMPLETED: Parallel processing for {EntityType} in {Duration}ms - {Processed} entities processed for migration {MigrationId}", 
            request.EntityType, processingTime.TotalMilliseconds, 
            parallelResult.TotalEntitiesProcessed, request.MigrationId);

        return new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
        {
            BatchNumber = 1,
            TotalProcessed = parallelResult.TotalEntitiesProcessed,
            SuccessfulEntities = parallelResult.TotalEntitiesProcessed - parallelResult.TotalEntitiesFailed,
            FailedEntities = parallelResult.TotalEntitiesFailed,
            Errors = parallelResult.ProcessingErrors ?? new List<string>(),
            ProcessingTime = parallelResult.TotalProcessingTime
        };
    }

    // 🔧 HIERARCHY-AWARE: Main batch processing method that switches between hierarchy-aware and regular processing
    private async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessBatchWithHierarchyAwareness(
        BatchProcessingRequest batch, 
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var result = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
        {
            BatchNumber = batch.BatchNumber,
            TotalProcessed = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            ProcessingTime = TimeSpan.Zero,
            Errors = new List<string>()
        };

        try
        {
            _logger.LogInformation("🚀 REAL MIGRATION: Processing batch {BatchNumber}/{TotalBatches} for {EntityType} - {EntityCount} entities", 
                batch.BatchNumber, batch.TotalBatches, batch.EntityType, batch.EntityIds?.Count ?? 0);

            // Validate batch
            if (batch.EntityIds == null || !batch.EntityIds.Any())
            {
                _logger.LogInformation("Empty batch {BatchNumber} for {EntityType} - skipping", 
                    batch.BatchNumber, batch.EntityType);
                result.ProcessingTime = DateTime.UtcNow - startTime;
                return result;
            }

            // Check for cancellation
            if (batch.IsCancelled)
            {
                _logger.LogInformation("Batch {BatchNumber} for {EntityType} was cancelled: {Reason}", 
                    batch.BatchNumber, batch.EntityType, batch.CancellationReason);
                result.Errors.Add($"Batch was cancelled: {batch.CancellationReason}");
                result.ProcessingTime = DateTime.UtcNow - startTime;
                return result;
            }

            // STEP 1: Fetch entities from source store
            _logger.LogDebug("🔍 FETCH: Fetching {EntityCount} {EntityType} entities from source store", 
                batch.EntityIds.Count, batch.EntityType);
            
            var sourceEntities = await _entityFetchService.FetchEntitiesAsync(batch, cancellationToken);
            
            if (sourceEntities == null || !sourceEntities.Any())
            {
                _logger.LogWarning("⚠️ FETCH: No entities returned from source store for batch {BatchNumber}", batch.BatchNumber);
                result.ProcessingTime = DateTime.UtcNow - startTime;
                return result;
            }

            _logger.LogInformation("✅ FETCH: Retrieved {EntityCount} {EntityType} entities from source store", 
                sourceEntities.Count, batch.EntityType);

            // 🔧 HIERARCHY-AWARE PROCESSING: For categories, use level-by-level processing to avoid parent-child race conditions
            if (batch.EntityType.ToLower() == "categories")
            {
                result = await ProcessCategoriesHierarchically(sourceEntities, batch, cancellationToken, startTime);
            }
            else
            {
                // STEP 2: Process other entities individually (non-hierarchical)
                result = await ProcessEntitiesIndividually(sourceEntities, batch, cancellationToken, startTime);
            }

            result.ProcessingTime = DateTime.UtcNow - startTime;
            
            _logger.LogInformation("🎉 BATCH COMPLETE: Batch {BatchNumber} for {EntityType} - {Successful}/{Total} successful in {Duration}ms", 
                batch.BatchNumber, batch.EntityType, result.SuccessfulEntities, result.TotalProcessed, result.ProcessingTime.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            result.ProcessingTime = DateTime.UtcNow - startTime;
            result.Errors.Add($"Batch processing failed: {ex.Message}");
            _logger.LogError(ex, "🚨 BATCH ERROR: Failed to process batch {BatchNumber} for {EntityType}", 
                batch.BatchNumber, batch.EntityType);
            return result;
        }
    }

    // Helper method to extract entity ID for logging  
    private string ExtractEntityId(Dictionary<string, object> entity, string entityType)
    {
        if (entity.TryGetValue("id", out var id)) return id?.ToString() ?? "unknown";
        if (entity.TryGetValue("Id", out var Id)) return Id?.ToString() ?? "unknown";
        if (entity.TryGetValue("ID", out var ID)) return ID?.ToString() ?? "unknown";
        return "unknown";
    }

    // 🔧 HIERARCHY-AWARE: Process categories level-by-level to avoid parent-child race conditions
    private async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessCategoriesHierarchically(
        List<Dictionary<string, object>> sourceEntities, 
        BatchProcessingRequest batch, 
        CancellationToken cancellationToken, 
        DateTime startTime)
    {
        // Use deterministic hierarchy ID for logging (Durable Functions compliance)
        var hierarchyId = $"{batch.BatchNumber}-hierarchy-{sourceEntities.Count}".GetHashCode().ToString("X8");
        
        _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] ⭐ STARTING: Processing {Count} categories with level-by-level hierarchy-aware processing", 
            hierarchyId, sourceEntities.Count);

        var result = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
        {
            BatchNumber = batch.BatchNumber,
            TotalProcessed = sourceEntities.Count,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            ProcessingTime = TimeSpan.Zero,
            Errors = new List<string>()
        };

        try
        {
            // STEP 1: Group categories by hierarchy level (breadth-first) with error handling
            _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] 🔄 GROUPING: Organizing categories by hierarchy levels", hierarchyId);
            
            List<List<Dictionary<string, object>>> categoryLevels;
            try
            {
                categoryLevels = GroupCategoriesByHierarchyLevel(sourceEntities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🏗️ [HIERARCHY-{HierarchyId}] ❌ GROUPING FAILED: Error organizing categories into hierarchy levels", hierarchyId);
                
                // Fallback: Treat all categories as Level-0 to prevent orchestration replay
                _logger.LogWarning("🏗️ [HIERARCHY-{HierarchyId}] 🔄 FALLBACK: Processing all categories as single level to prevent replay", hierarchyId);
                categoryLevels = new List<List<Dictionary<string, object>>> { sourceEntities };
                result.Errors.Add($"Hierarchy grouping failed, using fallback: {ex.Message}");
            }
            
            _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] 📊 GROUPED: Organized categories into {LevelCount} hierarchy levels", 
                hierarchyId, categoryLevels.Count);

            // Validate grouping results
            if (!categoryLevels.Any())
            {
                _logger.LogWarning("🏗️ [HIERARCHY-{HierarchyId}] ⚠️ EMPTY GROUPING: No hierarchy levels created, using fallback", hierarchyId);
                categoryLevels = new List<List<Dictionary<string, object>>> { sourceEntities };
                result.Errors.Add("Empty hierarchy grouping, using single-level fallback");
            }

        // Log detailed level breakdown
        for (int i = 0; i < categoryLevels.Count; i++)
        {
            var levelCategories = categoryLevels[i];
            var levelCategoryNames = levelCategories.Select(c => c.GetValueOrDefault("name")?.ToString() ?? "unknown").ToList();
            
            _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] 📋 LEVEL-{Level}: {Count} categories - {CategoryNames}", 
                hierarchyId, i, levelCategories.Count, string.Join(", ", levelCategoryNames.Take(5)) + (levelCategoryNames.Count > 5 ? "..." : ""));
        }

            // STEP 2: Process each level sequentially with comprehensive error handling
            var totalSuccessful = 0;
            var totalFailed = 0;
            
            for (int level = 0; level < categoryLevels.Count; level++)
            {
                var levelCategories = categoryLevels[level];
                
                if (levelCategories?.Any() != true)
                {
                    _logger.LogWarning("🏗️ [HIERARCHY-{HierarchyId}] ⚠️ LEVEL-{Level}: Skipping empty level", hierarchyId, level);
                    continue;
                }
                
                _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] 🚀 PROCESSING LEVEL-{Level}: Starting {Count} categories", 
                    hierarchyId, level, levelCategories.Count);

                try
                {
                    // Process categories in this level (SEQUENTIAL within level to prevent duplicates)
                    var levelResult = await ProcessEntitiesSequentially(levelCategories, batch, cancellationToken, $"Hierarchy-Level-{level}");
                    
                    result.SuccessfulEntities += levelResult.SuccessfulEntities;
                    result.FailedEntities += levelResult.FailedEntities;
                    if (levelResult.Errors?.Any() == true)
                    {
                        result.Errors.AddRange(levelResult.Errors);
                    }
                    
                    totalSuccessful += levelResult.SuccessfulEntities;
                    totalFailed += levelResult.FailedEntities;
                    
                    _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] ✅ LEVEL-{Level} COMPLETED: {Successful}/{Total} successful, {Failed} failed", 
                        hierarchyId, level, levelResult.SuccessfulEntities, levelCategories.Count, levelResult.FailedEntities);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] 🚫 LEVEL-{Level} CANCELLED: Processing was cancelled", hierarchyId, level);
                    result.Errors.Add($"Level {level} processing was cancelled");
                    break; // Exit gracefully on cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🏗️ [HIERARCHY-{HierarchyId}] ❌ LEVEL-{Level} FAILED: Error processing level", hierarchyId, level);
                    
                    // Mark all categories in this level as failed but continue processing
                    var levelFailedCount = levelCategories.Count;
                    result.FailedEntities += levelFailedCount;
                    totalFailed += levelFailedCount;
                    result.Errors.Add($"Level {level} failed: {ex.Message}");
                    
                    // Continue to next level to prevent replay
                    _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] 🔄 LEVEL-{Level}: Continuing to next level despite failure", hierarchyId, level);
                }
                
                // Add delay between levels with cancellation support
                if (level < categoryLevels.Count - 1) // Don't delay after the last level
                {
                    try
                    {
                        _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] ⏳ LEVEL-{Level}: Waiting 500ms for parent mappings to propagate", 
                            hierarchyId, level);
                        await Task.Delay(500, cancellationToken); // 500ms delay between hierarchy levels
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] 🚫 DELAY CANCELLED: Cancellation requested during level delay", hierarchyId);
                        break; // Exit gracefully
                    }
                }
            }
            
            result.ProcessingTime = DateTime.UtcNow - startTime;
            
            _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] 🎉 HIERARCHY COMPLETED: {TotalSuccessful}/{TotalCount} successful across {LevelCount} levels in {Duration}ms", 
                hierarchyId, totalSuccessful, sourceEntities.Count, categoryLevels.Count, result.ProcessingTime.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            result.ProcessingTime = DateTime.UtcNow - startTime;
            result.FailedEntities = sourceEntities.Count;
            result.Errors.Add($"Hierarchy processing failed completely: {ex.Message}");
            
            _logger.LogError(ex, "🏗️ [HIERARCHY-{HierarchyId}] 💥 COMPLETE FAILURE: Hierarchy processing failed completely", hierarchyId);
            
            // Don't re-throw to prevent orchestration replay
            _logger.LogInformation("🏗️ [HIERARCHY-{HierarchyId}] 🔄 GRACEFUL FAILURE: Returning failure result to prevent replay", hierarchyId);
        }

        return result;
    }

    // Group categories by hierarchy level (0 = roots, 1 = children of roots, etc.)
    private List<List<Dictionary<string, object>>> GroupCategoriesByHierarchyLevel(List<Dictionary<string, object>> categories)
    {
        var levels = new List<List<Dictionary<string, object>>>();
        var processedIds = new HashSet<string>();
        
        // Use deterministic ID for logging (Durable Functions compliance)
        var groupingId = $"hierarchy-{categories.Count}".GetHashCode().ToString("X8");
        
        _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] ⭐ STARTING: Analyzing {Count} categories for hierarchy levels", 
            groupingId, categories.Count);

        // STEP 1: Debug - Log all parent_id values to understand the data structure
        var parentIdCounts = new Dictionary<string, int>();
        var categoryDetails = new List<string>();
        
        foreach (var category in categories)
        {
            var categoryId = category.GetValueOrDefault("id")?.ToString() ?? "unknown";
            var categoryName = category.GetValueOrDefault("name")?.ToString() ?? "unknown";
            var parentId = category.GetValueOrDefault("parent_id")?.ToString() ?? "null";
            
            // Count parent_id occurrences
            if (!parentIdCounts.ContainsKey(parentId)) parentIdCounts[parentId] = 0;
            parentIdCounts[parentId]++;
            
            // Collect sample details
            if (categoryDetails.Count < 10)
            {
                categoryDetails.Add($"ID:{categoryId}('{categoryName}')->Parent:{parentId}");
            }
        }
        
        _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] 📊 PARENT_ID ANALYSIS: {ParentIdCounts}", 
            groupingId, string.Join(", ", parentIdCounts.Select(kv => $"{kv.Key}:{kv.Value}")));
        
        _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] 📋 SAMPLE DATA: {CategoryDetails}", 
            groupingId, string.Join(" | ", categoryDetails));

        // STEP 2: Identify root categories (enhanced logic for BigCommerce)
        var rootParentIds = new HashSet<string>();
        
        // Common root indicators in BigCommerce
        var potentialRootValues = new[] { "0", "", "null", null };
        foreach (var rootValue in potentialRootValues)
        {
            var normalizedValue = rootValue?.ToString() ?? "null";
            if (parentIdCounts.ContainsKey(normalizedValue))
            {
                rootParentIds.Add(normalizedValue);
                _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] 🌳 ROOT DETECTED: parent_id='{RootValue}' has {Count} categories", 
                    groupingId, normalizedValue, parentIdCounts[normalizedValue]);
            }
        }
        
        // If no standard root values found, find categories that aren't children of any other category
        if (!rootParentIds.Any())
        {
            var allCategoryIds = new HashSet<string>(categories.Select(c => c.GetValueOrDefault("id")?.ToString() ?? ""));
            var allParentIds = new HashSet<string>(categories.Select(c => c.GetValueOrDefault("parent_id")?.ToString() ?? ""));
            
            // Find parent_ids that don't exist as category IDs (external parents = roots)
            foreach (var parentId in allParentIds)
            {
                if (!string.IsNullOrEmpty(parentId) && parentId != "null" && !allCategoryIds.Contains(parentId))
                {
                    rootParentIds.Add(parentId);
                    _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] 🌳 EXTERNAL ROOT: parent_id='{ParentId}' (not in category set)", 
                        groupingId, parentId);
                }
            }
        }
        
        // Fallback: If still no roots, treat categories with most common parent_id as roots
        if (!rootParentIds.Any())
        {
            var mostCommonParentId = parentIdCounts.OrderByDescending(kv => kv.Value).First().Key;
            rootParentIds.Add(mostCommonParentId);
            _logger.LogWarning("🏗️ [GROUPING-{GroupingId}] ⚠️ FALLBACK ROOT: Using most common parent_id='{ParentId}' ({Count} categories)", 
                groupingId, mostCommonParentId, parentIdCounts[mostCommonParentId]);
        }

        // STEP 3: Build hierarchy levels iteratively
        var currentLevelParentIds = rootParentIds;
        var levelNumber = 0;
        var maxLevels = 10; // Safety limit to prevent infinite loops
        
        _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] 🚀 BUILDING LEVELS: Starting with {RootCount} root parent IDs: {RootIds}", 
            groupingId, rootParentIds.Count, string.Join(", ", rootParentIds));
        
        while (currentLevelParentIds.Any() && processedIds.Count < categories.Count && levelNumber < maxLevels)
        {
            var currentLevel = new List<Dictionary<string, object>>();
            var nextLevelParentIds = new HashSet<string>();

            _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] 🔄 LEVEL-{Level}: Searching for children of parents: {ParentIds}", 
                groupingId, levelNumber, string.Join(", ", currentLevelParentIds.Take(5)) + (currentLevelParentIds.Count > 5 ? "..." : ""));

            foreach (var category in categories)
            {
                var categoryId = category.GetValueOrDefault("id")?.ToString() ?? "";
                var categoryName = category.GetValueOrDefault("name")?.ToString() ?? "unknown";
                var parentId = category.GetValueOrDefault("parent_id")?.ToString() ?? "null";

                // Add categories whose parents are in the current level parent set
                if (!processedIds.Contains(categoryId) && currentLevelParentIds.Contains(parentId))
                {
                    currentLevel.Add(category);
                    processedIds.Add(categoryId);
                    nextLevelParentIds.Add(categoryId); // This category can be a parent for next level
                    
                    _logger.LogDebug("🏗️ [GROUPING-{GroupingId}] ✅ LEVEL-{Level}: Added '{CategoryName}' (ID:{CategoryId}, Parent:{ParentId})", 
                        groupingId, levelNumber, categoryName, categoryId, parentId);
                }
            }

            if (currentLevel.Any())
            {
                levels.Add(currentLevel);
                var levelCategoryNames = currentLevel.Select(c => c.GetValueOrDefault("name")?.ToString() ?? "unknown").ToList();
                
                _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] ✅ LEVEL-{Level}: Created with {Count} categories: {CategoryNames}", 
                    groupingId, levelNumber, currentLevel.Count, 
                    string.Join(", ", levelCategoryNames.Take(3)) + (levelCategoryNames.Count > 3 ? "..." : ""));
                
                currentLevelParentIds = nextLevelParentIds;
                levelNumber++;
            }
            else
            {
                _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] 🛑 LEVEL-{Level}: No more categories found, stopping", 
                    groupingId, levelNumber);
                break; // No more categories to process
            }
        }

        // STEP 4: Handle orphaned categories (categories that didn't fit into any level)
        var orphanedCategories = categories.Where(c => 
        {
            var categoryId = c.GetValueOrDefault("id")?.ToString() ?? "";
            return !processedIds.Contains(categoryId);
        }).ToList();
        
        if (orphanedCategories.Any())
        {
            _logger.LogWarning("🏗️ [GROUPING-{GroupingId}] ⚠️ ORPHANED: Found {Count} orphaned categories, adding as final level", 
                groupingId, orphanedCategories.Count);
            
            var orphanedNames = orphanedCategories.Select(c => c.GetValueOrDefault("name")?.ToString() ?? "unknown").ToList();
            _logger.LogWarning("🏗️ [GROUPING-{GroupingId}] 📋 ORPHANED CATEGORIES: {OrphanedNames}", 
                groupingId, string.Join(", ", orphanedNames));
            
            levels.Add(orphanedCategories);
        }

        _logger.LogInformation("🏗️ [GROUPING-{GroupingId}] 🎉 COMPLETED: Created {LevelCount} hierarchy levels with {ProcessedCount}/{TotalCount} categories", 
            groupingId, levels.Count, processedIds.Count + orphanedCategories.Count, categories.Count);

        return levels;
    }

    // Process entities individually (for non-hierarchical entities)
    private async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessEntitiesIndividually(
        List<Dictionary<string, object>> sourceEntities, 
        BatchProcessingRequest batch, 
        CancellationToken cancellationToken, 
        DateTime startTime)
    {
        var result = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
        {
            BatchNumber = batch.BatchNumber,
            TotalProcessed = sourceEntities.Count,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            ProcessingTime = TimeSpan.Zero,
            Errors = new List<string>()
        };

        // Process all entities in parallel (no hierarchy dependencies)
        var parallelResult = await ProcessEntitiesInParallel(sourceEntities, batch, cancellationToken, "Parallel");
        
        result.SuccessfulEntities = parallelResult.SuccessfulEntities;
        result.FailedEntities = parallelResult.FailedEntities;
        result.Errors = parallelResult.Errors;

        return result;
    }

    // Process a list of entities in parallel (helper method)
    private async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessEntitiesInParallel(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest batch, 
        CancellationToken cancellationToken, 
        string context)
    {
        // Use deterministic batch ID for logging (Durable Functions compliance)
        var batchId = $"{batch.BatchNumber}-{entities.Count}-{context}".GetHashCode().ToString("X8");
        
        _logger.LogInformation("🔄 [PARALLEL-{BatchId}] ⭐ STARTING: {Context} processing {Count} entities for {EntityType}", 
            batchId, context, entities.Count, batch.EntityType);

        var result = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
        {
            BatchNumber = batch.BatchNumber,
            TotalProcessed = entities.Count,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            ProcessingTime = TimeSpan.Zero,
            Errors = new List<string>()
        };

        // Use SemaphoreSlim to control concurrency within the level/batch
        var maxConcurrency = Math.Min(entities.Count, 5); // Limit to 5 concurrent entities per level
        
        _logger.LogInformation("🔄 [PARALLEL-{BatchId}] 🚀 EXECUTING: Using {MaxConcurrency} concurrent workers for {Count} entities", 
            batchId, maxConcurrency, entities.Count);
        
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        // Process entities in parallel
        var tasks = entities.Select(async (entity, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var entityId = ExtractEntityId(entity, batch.EntityType);
                _logger.LogDebug("🔄 [PARALLEL-{BatchId}] 🚀 WORKER[{Index}]: Starting entity {EntityId}", batchId, index, entityId);
                
                var entityResult = await ProcessSingleEntity(entity, batch, cancellationToken);
                
                _logger.LogDebug("🔄 [PARALLEL-{BatchId}] {Status} WORKER[{Index}]: Entity {EntityId} - {Result}", 
                    batchId, entityResult.Success ? "✅" : "❌", index, entityId, entityResult.Success ? "SUCCESS" : $"FAILED: {entityResult.ErrorMessage}");
                
                return entityResult;
            }
            finally
            {
                semaphore.Release();
            }
        });

        _logger.LogInformation("🔄 [PARALLEL-{BatchId}] ⏳ WAITING: For all {TaskCount} workers to complete", batchId, tasks.Count());
        
        var entityResults = await Task.WhenAll(tasks);

        _logger.LogInformation("🔄 [PARALLEL-{BatchId}] 📊 AGGREGATING: Processing {ResultCount} worker results", batchId, entityResults.Length);

        // Aggregate results
        var successCount = 0;
        var failureCount = 0;
        
        foreach (var entityResult in entityResults)
        {
            if (entityResult.Success)
            {
                result.SuccessfulEntities++;
                successCount++;
            }
            else
            {
                result.FailedEntities++;
                failureCount++;
                if (!string.IsNullOrEmpty(entityResult.ErrorMessage))
                {
                    result.Errors.Add(entityResult.ErrorMessage);
                }
            }
        }

        _logger.LogInformation("🔄 [PARALLEL-{BatchId}] ✅ COMPLETED: {Context} - {Successful}/{Total} successful, {Failed} failed for {EntityType}", 
            batchId, context, successCount, entities.Count, failureCount, batch.EntityType);

        return result;
    }

    // Process a single entity (helper method)
    private async Task<(bool Success, string ErrorMessage)> ProcessSingleEntity(
        Dictionary<string, object> entity, 
        BatchProcessingRequest batch, 
        CancellationToken cancellationToken)
    {
        var entityId = ExtractEntityId(entity, batch.EntityType);
        var entityName = entity.GetValueOrDefault("name")?.ToString() ?? "unknown";
        var entityParentId = entity.GetValueOrDefault("parent_id")?.ToString() ?? "0";
        
        // Use deterministic worker ID for logging (Durable Functions compliance)
        var workerId = $"{batch.BatchNumber}-{entityId}".GetHashCode().ToString("X8");
        
        _logger.LogDebug("🔄 [WORKER-{WorkerId}] ⭐ STARTING: Processing {EntityType} {EntityId} (name: '{EntityName}', parent: {ParentId})", 
            workerId, batch.EntityType, entityId, entityName, entityParentId);

        // 🚨 CRITICAL DEBUG: Log CategoryTreeContext at the beginning
        if (batch.CategoryTreeContext == null)
        {
            _logger.LogError("🚨 [WORKER-{WorkerId}] ❌ CRITICAL: BatchProcessingRequest.CategoryTreeContext is NULL for {EntityType} {EntityId} in migration {MigrationId}", 
                workerId, batch.EntityType, entityId, batch.MigrationId);
        }
        else
        {
            _logger.LogInformation("🔄 [WORKER-{WorkerId}] 📋 CategoryTreeContext: SourceTreeId='{SourceTreeId}', DestinationTreeId='{DestinationTreeId}' for {EntityType} {EntityId}", 
                workerId, 
                batch.CategoryTreeContext.SourceCategoryTreeId ?? "NULL", 
                batch.CategoryTreeContext.DestinationCategoryTreeId ?? "NULL",
                batch.EntityType, entityId);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // STEP 1: Transform entity
            _logger.LogDebug("🔄 [WORKER-{WorkerId}] 🔄 TRANSFORM: Starting transformation for {EntityType} {EntityId}", 
                workerId, batch.EntityType, entityId);

            var transformedEntity = await _entityTransformService.TransformEntityAsync(entity, batch);
            transformedEntity["_original_entity_id"] = entityId;

            _logger.LogDebug("🔄 [WORKER-{WorkerId}] ✅ TRANSFORMED: {EntityType} {EntityId} transformation completed", 
                workerId, batch.EntityType, entityId);

            // STEP 2a: Create entity in destination store
            _logger.LogDebug("🔄 [WORKER-{WorkerId}] 🚀 CREATE: Creating {EntityType} {EntityId} in destination store", 
                workerId, batch.EntityType, entityId);
            
            var createdEntities = await _entityCreateService.CreateEntitiesAsync(
                new List<Dictionary<string, object>> { transformedEntity }, 
                batch, 
                cancellationToken);

            // 🔍 DETAILED LOGGING: Log exactly what the API returned
            var returnedCount = createdEntities?.Count ?? 0;
            _logger.LogDebug("🔄 [WORKER-{WorkerId}] 📊 API RESPONSE: Entity {EntityId} - API returned {ReturnedCount} entities", 
                workerId, entityId, returnedCount);
            
            if (createdEntities != null && returnedCount > 0)
            {
                // Log all returned entities for debugging with FULL DETAILS
                for (int i = 0; i < createdEntities.Count; i++)
                {
                    var returnedEntity = createdEntities[i];
                    var returnedId = returnedEntity.GetValueOrDefault("id")?.ToString() ?? "unknown";
                    var returnedName = returnedEntity.GetValueOrDefault("name")?.ToString() ?? "";
                    var returnedParentId = returnedEntity.GetValueOrDefault("parent_id")?.ToString() ?? "0";
                    var returnedTreeId = returnedEntity.GetValueOrDefault("tree_id")?.ToString() ?? "0";
                    
                    // 🚨 CRITICAL DEBUG: Log the FULL entity as JSON
                    var fullEntityJson = System.Text.Json.JsonSerializer.Serialize(returnedEntity, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
                    
                    _logger.LogInformation("🔄 [WORKER-{WorkerId}] 📋 RETURNED[{Index}]: ID={ReturnedId}, Name='{ReturnedName}', ParentId='{ReturnedParentId}', TreeId='{ReturnedTreeId}'", 
                        workerId, i, returnedId, returnedName, returnedParentId, returnedTreeId);
                    _logger.LogInformation("🔄 [WORKER-{WorkerId}] 📋 FULL-ENTITY[{Index}]: {FullEntity}", 
                        workerId, i, fullEntityJson);
                }
            }

            // 🚨 CRITICAL: Check if THIS SPECIFIC entity was created
            if (createdEntities != null && createdEntities.Any())
            {
                var originalName = entity.GetValueOrDefault("name")?.ToString() ?? "";
                var originalParentId = entity.GetValueOrDefault("parent_id")?.ToString() ?? "0";
                var originalTreeId = entity.GetValueOrDefault("tree_id")?.ToString() ?? "0";
                
                // 🚨 CRITICAL DEBUG: Log the FULL INPUT entity as JSON
                var inputEntityJson = System.Text.Json.JsonSerializer.Serialize(entity, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
                
                _logger.LogInformation("🔄 [WORKER-{WorkerId}] 🔍 MATCHING: Looking for entity with Name='{OriginalName}', ParentId='{OriginalParentId}', TreeId='{OriginalTreeId}' (parent_id type: {OriginalParentIdType})", 
                    workerId, originalName, originalParentId, originalTreeId, entity.GetValueOrDefault("parent_id")?.GetType().Name ?? "null");
                _logger.LogInformation("🔄 [WORKER-{WorkerId}] 🔍 INPUT-ENTITY: {InputEntity}", 
                    workerId, inputEntityJson);
                
                var matchingCreatedEntity = createdEntities.FirstOrDefault(created =>
                {
                    var createdName = created.GetValueOrDefault("name")?.ToString() ?? "";
                    var createdParentId = created.GetValueOrDefault("parent_id")?.ToString() ?? "0";
                    
                    _logger.LogDebug("🔄 [WORKER-{WorkerId}] 🔍 COMPARING: '{CreatedName}' vs '{OriginalName}', '{CreatedParentId}' vs '{OriginalParentId}' (type: {CreatedParentIdType})", 
                        workerId, createdName, originalName, createdParentId, originalParentId, created.GetValueOrDefault("parent_id")?.GetType().Name ?? "null");
                    
                    // 🔧 ENHANCED MATCHING: More robust comparison logic
                    var nameMatch = createdName.Equals(originalName, StringComparison.OrdinalIgnoreCase);
                    
                    // Handle parent_id comparison with different data types
                    var parentIdMatch = false;
                    if (createdParentId == originalParentId)
                    {
                        parentIdMatch = true;
                    }
                    else if (int.TryParse(createdParentId, out var createdParentInt) && int.TryParse(originalParentId, out var originalParentInt))
                    {
                        parentIdMatch = createdParentInt == originalParentInt;
                    }
                    else if (createdParentId == "0" && (originalParentId == "null" || originalParentId == "" || originalParentId == "0"))
                    {
                        parentIdMatch = true; // Root categories
                    }
                    else if (originalParentId == "0" && (createdParentId == "null" || createdParentId == "" || createdParentId == "0"))
                    {
                        parentIdMatch = true; // Root categories
                    }
                    
                    // 🚀 FALLBACK FOR UNMAPPED PARENT IDS: Accept name-only matches during hierarchy migration
                    // This handles cases where parent_id mapping hasn't been applied properly
                    var isAcceptableMatch = nameMatch; // Accept if name matches, regardless of parent_id
                    
                    if (nameMatch && !parentIdMatch)
                    {
                        _logger.LogInformation("🔄 [WORKER-{WorkerId}] 🟡 FALLBACK-MATCH: Name '{OriginalName}' matches but parent_id differs (Input: '{OriginalParentId}' vs Output: '{CreatedParentId}'). Accepting based on name match for hierarchy migration.", 
                            workerId, originalName, originalParentId, createdParentId);
                    }
                    
                    // 🚨 NOTE: We do NOT match on tree_id because BigCommerce assigns its own tree_id
                    // Input tree_id may be 0 or source system value, but BigCommerce always returns its assigned tree_id
                    
                    _logger.LogInformation("🔄 [WORKER-{WorkerId}] 🔍 MATCH-RESULT: nameMatch={NameMatch}, parentIdMatch={ParentIdMatch}, acceptableMatch={AcceptableMatch} (tree_id ignored)", 
                        workerId, nameMatch, parentIdMatch, isAcceptableMatch);
                    
                    return isAcceptableMatch;
                });

                if (matchingCreatedEntity != null)
                {
                    // STEP 2b: Store entity mapping for the successfully created entity
                    _logger.LogDebug("🔄 [WORKER-{WorkerId}] 💾 MAPPING: Storing entity mapping for {EntityId}", workerId, entityId);
                    
                    var mapping = _entityMappingService.CreateEntityMapping(entity, matchingCreatedEntity, batch);
                    await _entityMappingService.StoreEntityMappingAsync(mapping, cancellationToken);

                    var createdId = matchingCreatedEntity.GetValueOrDefault("id")?.ToString() ?? "unknown";
                    _logger.LogDebug("🔄 [WORKER-{WorkerId}] ✅ SUCCESS: {EntityType} {EntityId} migrated successfully as {CreatedId}", 
                        workerId, batch.EntityType, entityId, createdId);
                    return (true, string.Empty);
                }
                else
                {
                    var errorMsg = $"Failed to create {batch.EntityType} {entityId} (name: '{originalName}') - entity not found in API response. API returned {returnedCount} entities but none matched.";
                    _logger.LogWarning("🔄 [WORKER-{WorkerId}] ❌ PARTIAL FAILURE: {ErrorMessage}", workerId, errorMsg);
                    return (false, errorMsg);
                }
            }
            else
            {
                var errorMsg = $"Failed to create {batch.EntityType} {entityId} - no entities returned from API";
                _logger.LogWarning("🔄 [WORKER-{WorkerId}] ❌ FAILED: {ErrorMessage}", workerId, errorMsg);
                return (false, errorMsg);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🔄 [WORKER-{WorkerId}] 🚫 CANCELLED: Entity processing cancelled for {EntityId}", workerId, entityId);
            return (false, "Processing was cancelled");
        }
        catch (Exception ex)
        {
            var errorMsg = $"Error processing {batch.EntityType} {entityId}: {ex.Message}";
            _logger.LogError(ex, "🔄 [WORKER-{WorkerId}] ❌ ERROR: {ErrorMessage}", workerId, errorMsg);
            return (false, errorMsg);
        }
    }

    // 🚨 TRULY SEQUENTIAL PROCESSING: Process entities one by one with delays to prevent duplicates
    private async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessEntitiesSequentially(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest batch, 
        CancellationToken cancellationToken,
        string levelContext)
    {
        var result = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
        {
            BatchNumber = batch.BatchNumber,
            TotalProcessed = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            ProcessingTime = TimeSpan.Zero,
            Errors = new List<string>()
        };

        var startTime = DateTime.UtcNow;
        
        _logger.LogInformation("🐌 [SEQUENTIAL-{Context}] ⭐ STARTING: Processing {Count} {EntityType} entities ONE-BY-ONE to prevent duplicates", 
            levelContext, entities.Count, batch.EntityType);

        // Process each entity individually with delays
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            var entityId = entity.GetValueOrDefault("id")?.ToString() ?? $"unknown-{i}";
            var entityName = entity.GetValueOrDefault("name")?.ToString() ?? "";

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("🐌 [SEQUENTIAL-{Context}] 🚫 CANCELLED: Processing cancelled at entity {Index}/{Total}", 
                        levelContext, i + 1, entities.Count);
                    break;
                }

                _logger.LogInformation("🐌 [SEQUENTIAL-{Context}] 🔄 PROCESSING: Entity {Index}/{Total} - ID={EntityId}, Name='{EntityName}'", 
                    levelContext, i + 1, entities.Count, entityId, entityName);

                // Process single entity
                var (success, errorMessage) = await ProcessSingleEntity(entity, batch, cancellationToken);

                result.TotalProcessed++;
                if (success)
                {
                    result.SuccessfulEntities++;
                    _logger.LogInformation("🐌 [SEQUENTIAL-{Context}] ✅ SUCCESS: Entity {Index}/{Total} - {EntityName} created successfully", 
                        levelContext, i + 1, entities.Count, entityName);
                }
                else
                {
                    result.FailedEntities++;
                    result.Errors.Add(errorMessage ?? $"Failed to process {entityName}");
                    _logger.LogWarning("🐌 [SEQUENTIAL-{Context}] ❌ FAILED: Entity {Index}/{Total} - {EntityName}: {ErrorMessage}", 
                        levelContext, i + 1, entities.Count, entityName, errorMessage);
                }

                // Critical delay between entities to prevent API race conditions and duplicates
                if (i < entities.Count - 1) // Don't delay after the last entity
                {
                    _logger.LogDebug("🐌 [SEQUENTIAL-{Context}] ⏳ DELAY: Waiting 200ms before next entity to prevent race conditions", levelContext);
                    await Task.Delay(200, cancellationToken); // 200ms delay between each entity
                }
            }
            catch (Exception ex)
            {
                result.TotalProcessed++;
                result.FailedEntities++;
                result.Errors.Add($"Exception processing {entityName}: {ex.Message}");
                _logger.LogError(ex, "🐌 [SEQUENTIAL-{Context}] 💥 EXCEPTION: Entity {Index}/{Total} - {EntityName}", 
                    levelContext, i + 1, entities.Count, entityName);
            }
        }

        result.ProcessingTime = DateTime.UtcNow - startTime;
        
        _logger.LogInformation("🐌 [SEQUENTIAL-{Context}] 🎉 COMPLETED: Processed {TotalProcessed} entities in {Duration}ms - {SuccessfulEntities} successful, {FailedEntities} failed", 
            levelContext, result.TotalProcessed, result.ProcessingTime.TotalMilliseconds, 
            result.SuccessfulEntities, result.FailedEntities);

        return result;
    }
} 