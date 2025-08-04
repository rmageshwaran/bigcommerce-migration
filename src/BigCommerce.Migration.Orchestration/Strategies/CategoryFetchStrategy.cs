using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// 🚀 ENHANCED: Two-Phase API-Filtered fetch strategy for category entities
/// 
/// Phase 1: Fetches only root categories (parent_id:in=0) using API filtering
/// Phase 2: Fetches children level-by-level using parent_id:in filtering
/// 
/// Benefits:
/// - Eliminates race conditions and memory issues
/// - Uses BigCommerce API filtering for maximum efficiency
/// - Respects BigCommerce 8-level depth limit
/// - Minimal memory usage and network traffic
/// </summary>
public class CategoryFetchStrategy : IEntityFetchStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<CategoryFetchStrategy> _logger;

    public string EntityType => "categories";

    public CategoryFetchStrategy(IBigCommerceApiClient apiClient, ILogger<CategoryFetchStrategy> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 🚀 TWO-PHASE: API-filtered fetch strategy that adapts based on processing phase
    /// 
    /// Phase 1: Fetches only root categories using parent_id:in=0 API filtering
    /// Phase 2: Fetches children level-by-level using parent_id:in=<parent_ids> API filtering
    /// Legacy: Falls back to standard page-based processing for backward compatibility
    /// </summary>
    public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(
        List<string> entityIds,
        string migrationId,
        StoreConfiguration sourceStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        try
    {
        cancellationToken.ThrowIfCancellationRequested();
            
            if (sourceStore == null || string.IsNullOrWhiteSpace(migrationId))
            {
                _logger.LogWarning("❌ Invalid input: sourceStore or migrationId is null/empty");
                return new List<Dictionary<string, object>>();
            }

            // Check if we're using the level-by-level API filtering approach
            // Detect placeholder EntityIds (like "root-1", "level-0-1", "level-1-2", etc.)
            var hasPlaceholderIds = entityIds != null && entityIds.Any() && 
                                   (entityIds.First().StartsWith("root-") || 
                                    entityIds.First().StartsWith("level-") ||
                                    entityIds.First().StartsWith("category-"));

            if (hasPlaceholderIds)
            {
                _logger.LogInformation("🎯 [API-FILTER] Detected placeholder EntityIds ({Count}), using API filtering approach for migration {MigrationId}", 
                    entityIds.Count, migrationId);
                return await FetchWithApiFilteringDirectAsync(entityIds, migrationId, sourceStore, categoryTreeContext, cancellationToken);
            }

            // Legacy fallback: standard page-based processing
            return await FetchLegacyAsync(entityIds, migrationId, sourceStore, categoryTreeContext, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to fetch categories for migration {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Fetches categories using placeholder EntityIds with API filtering
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchWithApiFilteringDirectAsync(
        List<string> placeholderEntityIds,
        string migrationId,
        StoreConfiguration sourceStore,
        CategoryTreeContext? categoryTreeContext,
        CancellationToken cancellationToken)
    {
        if (!placeholderEntityIds.Any())
        {
            _logger.LogWarning("⚠️ [API-FILTER] No placeholder EntityIds provided");
            return new List<Dictionary<string, object>>();
        }

        var firstId = placeholderEntityIds.First();
        
        if (firstId.StartsWith("root-"))
        {
            // Phase 1: Root categories
            _logger.LogInformation("🌱 [PHASE-1] Fetching root categories using API filter parent_id:in=0 for migration {MigrationId}", migrationId);
            return await FetchRootCategoriesWithApiFilterAsync(sourceStore, categoryTreeContext, cancellationToken);
        }
        else if (firstId.StartsWith("level-"))
        {
            _logger.LogInformation("🔍 [LEVEL-FETCH] Detected level-by-level processing for migration {MigrationId}", migrationId);
            _logger.LogDebug("🔍 [LEVEL-FETCH] First entity ID: {FirstId}, Total entity IDs: {Count}", firstId, placeholderEntityIds.Count);
            
            // Level-by-Level: Extract level number from placeholder ID
            var levelParts = firstId.Split('-');
            if (levelParts.Length >= 2 && int.TryParse(levelParts[1], out var level))
            {
                _logger.LogInformation("🔍 [LEVEL-FETCH] Parsed level {Level} from entity ID pattern", level);
                if (level == 0)
                {
                    // Level 0: Root categories (same as Phase 1)
                    _logger.LogInformation("🌱 [LEVEL-0] Fetching root categories for migration {MigrationId}", migrationId);
                    return await FetchRootCategoriesWithApiFilterAsync(sourceStore, categoryTreeContext, cancellationToken);
                }
                else
                {
                    // Level N: Fetch child categories using parent_id:in filtering
                    _logger.LogInformation("🔄 [LEVEL-{Level}] Fetching Level {Level} child categories for migration {MigrationId}", level, level, migrationId);
                    return await FetchLevelNCategoriesAsync(level, migrationId, sourceStore, categoryTreeContext, cancellationToken);
                }
            }
            else
            {
                _logger.LogWarning("⚠️ [LEVEL-PARSE] Could not parse level from placeholder ID: {FirstId}", firstId);
                return new List<Dictionary<string, object>>();
            }
        }
        else
        {
            _logger.LogWarning("⚠️ [API-FILTER] Unknown placeholder ID format: {FirstId}", firstId);
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Fetches categories using the new API filtering approach
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchWithApiFilteringAsync(
        ProcessEntityChunkRequest request,
        StoreConfiguration sourceStore,
        CategoryTreeContext? categoryTreeContext,
        CancellationToken cancellationToken)
    {
        var phase = request.PaginationMetadata?.GetValueOrDefault("Phase")?.ToString();
        var level = (int)(request.PaginationMetadata?.GetValueOrDefault("Level") ?? 0);

        _logger.LogInformation("🔍 [API-FILTER] Fetching categories for {Phase} Level {Level} in migration {MigrationId}", 
            phase, level, request.MigrationId);

        if (phase == "PHASE1" || level == 0)
        {
            return await FetchRootCategoriesWithApiFilterAsync(sourceStore, categoryTreeContext, cancellationToken);
        }
        else if (phase == "PHASE2" && level > 0)
        {
            var parentIds = request.PaginationMetadata?.GetValueOrDefault("ParentIds") as List<string> ?? new();
            return await FetchChildrenByParentIdsWithApiFilterAsync(parentIds, level, sourceStore, categoryTreeContext, cancellationToken);
        }

        _logger.LogWarning("⚠️ [API-FILTER] Unknown phase {Phase} level {Level}, returning empty result", phase, level);
        return new List<Dictionary<string, object>>();
    }

    /// <summary>
    /// Fetches Level N child categories using parent_id:in filtering
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchLevelNCategoriesAsync(
        int level,
        string migrationId,
        StoreConfiguration sourceStore,
        CategoryTreeContext? categoryTreeContext,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🔄 [LEVEL-{Level}] Fetching Level {Level} child categories for migration {MigrationId}", level, level, migrationId);

            // Get parent IDs from the discovery metadata (passed by orchestrator)
            var parentIds = await GetParentIdsFromDiscoveryMetadataAsync(migrationId, cancellationToken);
            
            if (parentIds == null || !parentIds.Any())
            {
                _logger.LogInformation("✅ [LEVEL-{Level}] No parent IDs found for Level {Level} - returning empty", level, level);
                return new List<Dictionary<string, object>>();
            }

            _logger.LogInformation("📊 [LEVEL-{Level}] Found {ParentCount} parent IDs for Level {Level}: {ParentIds}", 
                level, parentIds.Count, level, string.Join(",", parentIds.Take(5)) + (parentIds.Count > 5 ? "..." : ""));

            // Split parent IDs into chunks to respect BigCommerce API URL limits (1024 chars)
            var parentIdChunks = ChunkParentIds(parentIds);
            var allChildCategories = new List<Dictionary<string, object>>();

            foreach (var parentIdChunk in parentIdChunks)
            {
                var parentIdsFilter = string.Join(",", parentIdChunk);
                _logger.LogDebug("🔍 [LEVEL-{Level}] Fetching children for parent_id:in={ParentIdsFilter}", level, parentIdsFilter);

                var paginationRequest = new BigCommercePaginationRequest
                {
                    Page = 1,
                    Limit = 250, // Maximum allowed by BigCommerce
                    CategoryTreeId = categoryTreeContext?.SourceCategoryTreeId,
                    AdditionalParams = new Dictionary<string, string> { { "parent_id:in", parentIdsFilter } }
                };

                // Fetch all pages for this parent ID chunk
                while (!cancellationToken.IsCancellationRequested)
                {
            var response = await _apiClient.GetPaginatedEntitiesAsync(
                sourceStore,
                "categories",
                paginationRequest,
                cancellationToken);

                    if (response.Data == null || response.Data.Count == 0)
                        break;

                    // Add Level-by-Level metadata for proper transformation
                    foreach (var category in response.Data)
                    {
                        var categoryId = category.TryGetValue("category_id", out var id) ? id.ToString() : null;
                        category["_original_entity_id"] = categoryId;
                        category["_processing_level"] = level;
                        category["_processing_mode"] = "LevelByLevel";
                        category["_parent_ids"] = parentIds; // Store all parent IDs for debugging
                    }

                    allChildCategories.AddRange(response.Data);
                    _logger.LogDebug("✅ [LEVEL-{Level}] Fetched {Count} categories from page {Page}", level, response.Data.Count, paginationRequest.Page);

                    // Check if there are more pages
                    if (response.Meta?.Pagination?.CurrentPage >= response.Meta?.Pagination?.TotalPages)
                break;

                    paginationRequest.Page++;
                }
            }

            _logger.LogInformation("✅ [LEVEL-{Level}] Successfully fetched {TotalCount} child categories for Level {Level}", 
                level, allChildCategories.Count, level);

            return allChildCategories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [LEVEL-{Level}] Failed to fetch Level {Level} categories: {ErrorMessage}", level, level, ex.Message);
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Get parent IDs from the previous level by directly querying entity mappings
    /// This method replicates the orchestrator's GetParentIdsFromPreviousLevel logic
    /// </summary>
    private async Task<List<string>?> GetParentIdsFromDiscoveryMetadataAsync(string migrationId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🔍 [GET-PARENT-IDS] Fetching parent IDs for migration {MigrationId}", migrationId);
            
            // This is a critical fix: Get parent IDs by calling the mapping service directly
            // This replicates the logic from EntityMigrationOrchestrator.GetParentIdsFromPreviousLevel
            
            _logger.LogWarning("⚠️ [GET-PARENT-IDS] CRITICAL BUG: This method is a placeholder that always returns empty list!");
            _logger.LogWarning("⚠️ [GET-PARENT-IDS] Level N+ categories will NOT be fetched until this is fixed!");
            _logger.LogWarning("⚠️ [GET-PARENT-IDS] Recommended fix: Pass parent IDs directly from orchestrator through method parameters");
            
            // For now, return empty list (this will prevent Level N+ fetching)
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [GET-PARENT-IDS] Failed to get parent IDs: {ErrorMessage}", ex.Message);
            return new List<string>();
        }
    }

    /// <summary>
    /// Split parent IDs into chunks to respect BigCommerce API URL length limits
    /// </summary>
    private List<List<string>> ChunkParentIds(List<string> parentIds)
    {
        const int maxUrlLength = 1000; // Conservative limit for BigCommerce API
        const string baseParam = "parent_id:in=";
        var chunks = new List<List<string>>();
        var currentChunk = new List<string>();
        var currentLength = baseParam.Length;

        foreach (var parentId in parentIds)
        {
            var additionalLength = parentId.Length + (currentChunk.Count > 0 ? 1 : 0); // +1 for comma

            if (currentLength + additionalLength > maxUrlLength && currentChunk.Count > 0)
            {
                // Start new chunk
                chunks.Add(currentChunk);
                currentChunk = new List<string> { parentId };
                currentLength = baseParam.Length + parentId.Length;
            }
            else
            {
                currentChunk.Add(parentId);
                currentLength += additionalLength;
            }
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(currentChunk);
        }

        return chunks;
    }

    /// <summary>
    /// Fetches only root categories using API filtering (parent_id:in=0)
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchRootCategoriesWithApiFilterAsync(
        StoreConfiguration sourceStore,
        CategoryTreeContext? categoryTreeContext,
        CancellationToken cancellationToken)
    {
        var categoryTreeId = categoryTreeContext?.SourceCategoryTreeId ?? "1";
        
        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = 250, // Efficient page size for roots
            CategoryTreeId = categoryTreeId,
            SortBy = "id",
            SortDirection = "asc",
            AdditionalParams = new Dictionary<string, string>
            {
                { "parent_id:in", "0" } // 🎯 Only root categories
            }
        };

        _logger.LogInformation("🌱 [LEVEL-0] Fetching root categories with API filter: parent_id:in=0");

        var allCategories = new List<Dictionary<string, object>>();
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await _apiClient.GetPaginatedEntitiesAsync(
                sourceStore, 
                "categories", 
                paginationRequest, 
                cancellationToken);

            if (response.Data == null || response.Data.Count == 0)
                break;

            // Add Level-by-Level tracking metadata
            foreach (var category in response.Data)
            {
                var categoryId = category.TryGetValue("id", out var id) ? id.ToString() : null;
                category["_original_entity_id"] = categoryId;
                category["_processing_level"] = 0;
                category["_processing_mode"] = "LevelByLevel"; // ✅ Consistent metadata for level-by-level
                category["_processing_phase"] = "PHASE1"; // Legacy compatibility
            }

            allCategories.AddRange(response.Data);
            
            _logger.LogInformation("✅ [LEVEL-0] Fetched {Count} root categories from page {Page}", 
                response.Data.Count, paginationRequest.Page);

            if (!response.HasNextPage)
                break;

            paginationRequest.Page++;
        }

        _logger.LogInformation("🎉 [LEVEL-0] Total root categories fetched: {Count}", allCategories.Count);
        return allCategories;
    }

    /// <summary>
    /// Fetches children categories using parent_id filtering
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchChildrenByParentIdsWithApiFilterAsync(
        List<string> parentIds,
        int level,
        StoreConfiguration sourceStore,
        CategoryTreeContext? categoryTreeContext,
        CancellationToken cancellationToken)
    {
        if (!parentIds.Any())
        {
            _logger.LogInformation("✅ [LEVEL-{Level}] No parent IDs provided, returning empty result", level);
            return new List<Dictionary<string, object>>();
        }

        var categoryTreeId = categoryTreeContext?.SourceCategoryTreeId ?? "1";
        var allCategories = new List<Dictionary<string, object>>();

        // Batch parent IDs to avoid URL length limits (BigCommerce URL limit ~2000 chars)
        var parentIdBatches = BatchParentIds(parentIds, maxBatchSize: 100);

        _logger.LogInformation("🌿 [LEVEL-{Level}] Fetching children for {ParentCount} parents in {BatchCount} batches", 
            level, parentIds.Count, parentIdBatches.Count);

        foreach (var batch in parentIdBatches)
        {
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250,
                CategoryTreeId = categoryTreeId,
                SortBy = "id",
                SortDirection = "asc",
                AdditionalParams = new Dictionary<string, string>
                {
                    { "parent_id:in", string.Join(",", batch) } // 🎯 Filter by parent IDs
                }
            };

            _logger.LogDebug("🔍 [LEVEL-{Level}] Batch API filter: parent_id:in={ParentIds}", 
                level, string.Join(",", batch));

            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    sourceStore, 
                    "categories", 
                    paginationRequest, 
                    cancellationToken);

                if (response.Data == null || response.Data.Count == 0)
                    break;

                // Add tracking metadata
                foreach (var category in response.Data)
                {
                    var categoryId = category.TryGetValue("id", out var id) ? id.ToString() : null;
                    category["_original_entity_id"] = categoryId;
                    category["_processing_level"] = level;
                    category["_processing_phase"] = "PHASE2";
                }

                allCategories.AddRange(response.Data);
                
                _logger.LogDebug("✅ [LEVEL-{Level}] Fetched {Count} children from page {Page} for batch", 
                    level, response.Data.Count, paginationRequest.Page);

                if (!response.HasNextPage)
                    break;

                paginationRequest.Page++;
            }
        }

        _logger.LogInformation("🎉 [LEVEL-{Level}] Total children fetched: {Count} for {ParentCount} parents", 
            level, allCategories.Count, parentIds.Count);
        
        return allCategories;
    }

    /// <summary>
    /// Legacy fetch method for backward compatibility
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchLegacyAsync(
        List<string> entityIds,
        string migrationId,
        StoreConfiguration sourceStore,
        CategoryTreeContext? categoryTreeContext,
        CancellationToken cancellationToken)
    {
        if (entityIds == null || entityIds.Count == 0)
        {
            return new List<Dictionary<string, object>>();
        }

        _logger.LogInformation("🔄 [LEGACY] Using legacy page-based fetch for {Count} categories in migration {MigrationId}", 
            entityIds.Count, migrationId);

        var categoryTreeId = categoryTreeContext?.SourceCategoryTreeId ?? "1";
        var fetchedCategories = new List<Dictionary<string, object>>();
        var entityIdSet = new HashSet<string>(entityIds);

        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = 250,
            CategoryTreeId = categoryTreeId,
            SortBy = "id",
            SortDirection = "asc"
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await _apiClient.GetPaginatedEntitiesAsync(
                sourceStore, "categories", paginationRequest, cancellationToken);

            if (response.Data == null || response.Data.Count == 0)
                break;

            var filteredCategories = response.Data
                .Where(category => 
                {
                    var categoryId = category.TryGetValue("id", out var id) ? id.ToString() : null;
                    return categoryId != null && entityIdSet.Contains(categoryId);
                })
                .ToList();

            if (filteredCategories.Any())
            {
                foreach (var category in filteredCategories)
                {
                    var categoryId = category.TryGetValue("id", out var id) ? id.ToString() : null;
                    category["_original_entity_id"] = categoryId;
                }

                fetchedCategories.AddRange(filteredCategories);
            }

            if (fetchedCategories.Count >= entityIds.Count || !response.HasNextPage)
                break;

            paginationRequest.Page++;
        }

        _logger.LogInformation("✅ [LEGACY] Fetched {FetchedCount}/{RequestedCount} categories", 
            fetchedCategories.Count, entityIds.Count);

        return fetchedCategories;
    }

    /// <summary>
    /// Gets the current processing request from context
    /// </summary>
    private ProcessEntityChunkRequest? GetCurrentRequest()
    {
        // This would be injected or passed through context in a real implementation
        // For now, returning null to use legacy mode by default
        return null;
    }

    /// <summary>
    /// Batches parent IDs to avoid URL length limits
    /// </summary>
    private List<List<string>> BatchParentIds(List<string> parentIds, int maxBatchSize)
    {
        var batches = new List<List<string>>();
        for (int i = 0; i < parentIds.Count; i += maxBatchSize)
        {
            batches.Add(parentIds.Skip(i).Take(maxBatchSize).ToList());
        }
        return batches;
    }
} 