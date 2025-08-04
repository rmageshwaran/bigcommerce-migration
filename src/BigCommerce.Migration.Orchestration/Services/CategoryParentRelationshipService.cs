using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// 🔗 PHASE 2: CHILD CATEGORY CREATION SERVICE
/// 
/// Handles the second phase of the Two-Phase category migration approach.
/// After root categories are created (Phase 1), this service creates all
/// child categories level by level using the stored entity mappings.
/// 
/// Key Features:
/// - Fetches source children using parent_id:in filters
/// - Creates children with proper parent references using entity mappings
/// - Processes level by level until all categories are created
/// - Provides detailed progress tracking
/// </summary>
public interface ICategoryParentRelationshipService
{
    Task<CategoryParentFixupResult> CreateChildCategoriesAsync(
        CategoryParentFixupRequest request, 
        CancellationToken cancellationToken = default);
}

public class CategoryParentRelationshipService : ICategoryParentRelationshipService
{
    private readonly ILogger<CategoryParentRelationshipService> _logger;
    private readonly IBigCommerceApiClient _apiClient;
    private readonly IEntityCreateService _entityCreateService;
    private readonly IEntityMappingService _entityMappingService;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ISignalREventFactory _signalREventFactory;

    public CategoryParentRelationshipService(
        ILogger<CategoryParentRelationshipService> logger,
        IBigCommerceApiClient apiClient,
        IEntityCreateService entityCreateService,
        IEntityMappingService entityMappingService,
        IProgressEventPublisher progressEventPublisher,
        ISignalREventFactory signalREventFactory)
    {
        _logger = logger;
        _apiClient = apiClient;
        _entityCreateService = entityCreateService;
        _entityMappingService = entityMappingService;
        _progressEventPublisher = progressEventPublisher;
        _signalREventFactory = signalREventFactory;
    }

    /// <summary>
    /// Phase 2: Create child categories level by level using stored root category mappings
    /// </summary>
    public async Task<CategoryParentFixupResult> CreateChildCategoriesAsync(
        CategoryParentFixupRequest request, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🌳 [CHILD-CREATION-P2] Starting Phase 2: Creating child categories level-by-level for migration {MigrationId}", 
            request.MigrationId);

        // 📡 SIGNALR: Broadcast Phase 2 start
        await PublishPhase2ProgressAsync(request.MigrationId, 0, request.TotalCategories, "starting");

        var result = new CategoryParentFixupResult
        {
            MigrationId = request.MigrationId,
            TotalCategories = request.TotalCategories,
            ProcessedCategories = 0,
            SuccessfulUpdates = 0,
            FailedUpdates = 0,
            Errors = new List<string>()
        };

        try
        {
            // 🎯 TIMING FIX: Use Phase 1 mappings passed directly from orchestrator (eliminates database timing issues)
            _logger.LogInformation("🔍 [CHILD-CREATION-P2] Using {Count} entity mappings passed directly from Phase 1", request.Phase1Mappings?.Count ?? 0);
            
            var rootMappings = request.Phase1Mappings ?? new List<EntityMapping>();
            
            if (rootMappings.Any())
            {
                foreach (var mapping in rootMappings.Take(5))
                {
                    _logger.LogInformation("🔍 [CHILD-CREATION-P2] Sample mapping: {SourceId} -> {DestinationId}", mapping.SourceId, mapping.DestinationId);
                }
                
                if (rootMappings.Count > 5)
                {
                    _logger.LogInformation("🔍 [CHILD-CREATION-P2] ... and {RemainingCount} more mappings", rootMappings.Count - 5);
                }
            }
            
            if (!rootMappings.Any())
            {
                _logger.LogError("❌ [CHILD-CREATION-P2] No root category mappings provided from Phase 1. Phase 1 must complete successfully first.");
                _logger.LogError("❌ [CHILD-CREATION-P2] Debug: MigrationId='{MigrationId}', Phase1Mappings count: {MappingsCount}", 
                    request.MigrationId, request.Phase1Mappings?.Count ?? 0);
                result.Errors.Add("No root category mappings provided from Phase 1");
                return result;
            }

            _logger.LogInformation("📊 [CHILD-CREATION-P2] Using {RootCount} root category mappings passed from Phase 1", rootMappings.Count);
            
            // Create child categories level by level
            await CreateChildCategoriesLevelByLevel(request, rootMappings, result, cancellationToken);
            
            _logger.LogInformation("✅ [CHILD-CREATION-P2] Phase 2 completed: {SuccessfulUpdates} child categories created", 
                result.SuccessfulUpdates);

            // 📡 SIGNALR: Broadcast Phase 2 completion
            await PublishPhase2ProgressAsync(request.MigrationId, result.ProcessedCategories, result.TotalCategories, "completed");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHILD-CREATION-P2] Phase 2 failed for migration {MigrationId}: {ErrorMessage}", 
                request.MigrationId, ex.Message);
            
            result.Errors.Add($"Child category creation failed: {ex.Message}");
            
            // 📡 SIGNALR: Broadcast Phase 2 failure
            await PublishPhase2ProgressAsync(request.MigrationId, result.ProcessedCategories, result.TotalCategories, "failed");
            
            return result;
        }
    }

    /// <summary>
    /// Create child categories level by level using parent_id:in API filtering
    /// </summary>
    private async Task CreateChildCategoriesLevelByLevel(
        CategoryParentFixupRequest request,
        List<EntityMapping> currentLevelMappings,
        CategoryParentFixupResult result,
        CancellationToken cancellationToken)
    {
        const int MAX_IDS_PER_BATCH = 150; // Conservative limit for URL length
        const int MAX_LEVELS = 20; // Safety limit
        
        var allMappings = currentLevelMappings.ToList(); // Start with root mappings
        
        for (int level = 1; level <= MAX_LEVELS; level++)
        {
            _logger.LogInformation("🌳 [CHILD-CREATION-P2] Processing Level {Level}: Using {ParentCount} parent categories", 
                level, currentLevelMappings.Count);
            
            // Get parent IDs for this level (destination IDs from previous level)
            var parentIds = currentLevelMappings.Select(m => m.DestinationId).ToList();
            
            if (!parentIds.Any())
            {
                _logger.LogInformation("🏁 [CHILD-CREATION-P2] No parent IDs available for level {Level}. Creation complete.", level);
                break;
            }
            
            // Fetch children from source store using parent_id:in filter
            var allChildren = new List<Dictionary<string, object>>();
            
            // Handle batching for URL limits (though likely not needed with 45 roots)
            for (int i = 0; i < parentIds.Count; i += MAX_IDS_PER_BATCH)
            {
                var batchParentIds = parentIds.Skip(i).Take(MAX_IDS_PER_BATCH).ToList();
                
                // Map destination parent IDs back to source IDs for fetching
                var sourceParentIds = new List<string>();
                foreach (var destId in batchParentIds)
                {
                    var mapping = allMappings.FirstOrDefault(m => m.DestinationId == destId);
                    if (mapping != null)
                    {
                        sourceParentIds.Add(mapping.SourceId);
                    }
                }
                
                if (sourceParentIds.Any())
                {
                    _logger.LogInformation("🔍 [CHILD-CREATION-P2] Fetching children for {Count} parents: {ParentIds}", 
                        sourceParentIds.Count, string.Join(",", sourceParentIds.Take(5)) + (sourceParentIds.Count > 5 ? "..." : ""));
                    
                    var batchChildren = await FetchChildrenFromSource(request, sourceParentIds, cancellationToken);
                    allChildren.AddRange(batchChildren);
                }
            }
            
            if (!allChildren.Any())
            {
                _logger.LogInformation("🏁 [CHILD-CREATION-P2] No children found at level {Level}. Creation complete.", level);
                break;
            }
            
            _logger.LogInformation("📊 [CHILD-CREATION-P2] Found {ChildCount} children at level {Level}", allChildren.Count, level);
            
            // Transform children to have correct destination parent IDs
            var transformedChildren = TransformChildCategories(allChildren, allMappings);
            
            // Create children in destination store
            var createdChildren = await CreateChildrenInDestination(request, transformedChildren, cancellationToken);
            
            if (createdChildren.Any())
            {
                // Store new entity mappings for next level
                var newMappings = await StoreChildMappings(request.MigrationId, allChildren, createdChildren, cancellationToken);
                
                // Update tracking
                result.SuccessfulUpdates += createdChildren.Count;
                result.ProcessedCategories += createdChildren.Count;
                
                // Add new mappings to our master list
                allMappings.AddRange(newMappings);
                
                // Set up for next level
                currentLevelMappings = newMappings;
                
                _logger.LogInformation("✅ [CHILD-CREATION-P2] Level {Level} complete: {CreatedCount} children created", 
                    level, createdChildren.Count);
            }
            else
            {
                _logger.LogWarning("⚠️ [CHILD-CREATION-P2] No children created at level {Level}. Stopping.", level);
                break;
            }
        }
        
        _logger.LogInformation("🎯 [CHILD-CREATION-P2] All levels processed. Total children created: {TotalCreated}", 
            result.SuccessfulUpdates);
    }

    /// <summary>
    /// Fetch children from source store using parent_id:in API filtering
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchChildrenFromSource(
        CategoryParentFixupRequest request,
        List<string> sourceParentIds,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build parent_id:in filter
            var parentFilter = $"parent_id:in={string.Join(",", sourceParentIds)}";
            
            _logger.LogDebug("🔍 [CHILD-CREATION-P2] Fetching children with filter: {Filter}", parentFilter);
            
            // Use BigCommerce API to fetch categories with parent_id filter
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250, // Get as many as possible per request
                AdditionalParams = new Dictionary<string, string> { { "parent_id:in", string.Join(",", sourceParentIds) } }
            };

            var response = await _apiClient.GetPaginatedEntitiesAsync(
                request.SourceStore, "categories", paginationRequest, cancellationToken);
            
            var categories = response?.Data ?? new List<Dictionary<string, object>>();
            
            _logger.LogInformation("📥 [CHILD-CREATION-P2] Fetched {Count} children from source for {ParentCount} parents", 
                categories.Count, sourceParentIds.Count);
            
            return categories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHILD-CREATION-P2] Failed to fetch children from source: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Transform child categories to have correct destination parent IDs
    /// </summary>
    private List<Dictionary<string, object>> TransformChildCategories(
        List<Dictionary<string, object>> sourceChildren,
        List<EntityMapping> allMappings)
    {
        var transformedChildren = new List<Dictionary<string, object>>();
        
        foreach (var child in sourceChildren)
        {
            var transformedChild = new Dictionary<string, object>(child);
            
            // Get source parent ID
            var sourceParentId = child.GetValueOrDefault("parent_id")?.ToString();
            
            if (!string.IsNullOrEmpty(sourceParentId) && sourceParentId != "0")
            {
                // Find destination parent ID using entity mappings
                var parentMapping = allMappings.FirstOrDefault(m => m.SourceId == sourceParentId);
                
                if (parentMapping != null)
                {
                    // Update parent_id to destination parent ID
                    transformedChild["parent_id"] = int.Parse(parentMapping.DestinationId);
                    
                    _logger.LogDebug("🔄 [CHILD-CREATION-P2] Transformed parent_id: {SourceId} → {DestinationId}", 
                        sourceParentId, parentMapping.DestinationId);
                }
                else
                {
                    _logger.LogWarning("⚠️ [CHILD-CREATION-P2] No mapping found for parent ID {ParentId}", sourceParentId);
                    transformedChild["parent_id"] = 0; // Fallback to root
                }
            }
            
            // Remove read-only fields that shouldn't be sent to creation API
            transformedChild.Remove("id");
            transformedChild.Remove("category_id");
            transformedChild.Remove("date_created");
            transformedChild.Remove("date_modified");
            
            // ✅ FIX: Handle meta_keywords field - BigCommerce expects array, not string
            HandleMetaKeywords(transformedChild);
            
            transformedChildren.Add(transformedChild);
        }
        
        _logger.LogInformation("🔄 [CHILD-CREATION-P2] Transformed {Count} children with correct parent IDs", transformedChildren.Count);
        
        return transformedChildren;
    }

    /// <summary>
    /// Create child categories in destination store
    /// </summary>
    private async Task<List<Dictionary<string, object>>> CreateChildrenInDestination(
        CategoryParentFixupRequest request,
        List<Dictionary<string, object>> transformedChildren,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!transformedChildren.Any())
            {
                return new List<Dictionary<string, object>>();
            }
            
            _logger.LogInformation("🚀 [CHILD-CREATION-P2] Creating {Count} child categories in destination", transformedChildren.Count);
            
            // Create batch processing request for entity creation
            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = request.MigrationId,
                EntityType = "categories",
                BatchNumber = 1,
                TotalBatches = 1,
                EntityIds = new List<string>(), // Not used for creation
                SourceStore = request.SourceStore,
                DestinationStore = request.DestinationStore,
                UseDirectPagination = false,
                CategoryTreeContext = request.CategoryTreeContext // ✅ FIXED: Pass tree context for child creation
            };
            
            // Use Entity Creation Service to create categories
            var createdCategories = await _entityCreateService.CreateEntitiesAsync(transformedChildren, batchRequest, cancellationToken);
            
            var result = createdCategories ?? new List<Dictionary<string, object>>();
            
            _logger.LogInformation("✅ [CHILD-CREATION-P2] Successfully created {Count} child categories", result.Count);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHILD-CREATION-P2] Failed to create children in destination: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Store entity mappings for newly created child categories
    /// </summary>
    private async Task<List<EntityMapping>> StoreChildMappings(
        string migrationId,
        List<Dictionary<string, object>> sourceChildren,
        List<Dictionary<string, object>> createdChildren,
        CancellationToken cancellationToken)
    {
        var newMappings = new List<EntityMapping>();
        
        try
        {
            for (int i = 0; i < Math.Min(sourceChildren.Count, createdChildren.Count); i++)
            {
                var sourceId = sourceChildren[i].GetValueOrDefault("category_id")?.ToString(); // 🔧 FIX: Use category_id for source
                var destinationId = createdChildren[i].GetValueOrDefault("category_id")?.ToString();
                
                if (!string.IsNullOrEmpty(sourceId) && !string.IsNullOrEmpty(destinationId))
                {
                    var mapping = new EntityMapping
                    {
                        MigrationId = migrationId,
                        EntityType = "categories",
                        SourceId = sourceId,
                        DestinationId = destinationId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    
                    await _entityMappingService.StoreEntityMappingAsync(mapping, cancellationToken);
                    newMappings.Add(mapping);
                    
                    _logger.LogDebug("💾 [CHILD-CREATION-P2] Stored mapping: {SourceId} → {DestinationId}", sourceId, destinationId);
                }
            }
            
            _logger.LogInformation("💾 [CHILD-CREATION-P2] Stored {Count} new entity mappings", newMappings.Count);
            
            return newMappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHILD-CREATION-P2] Failed to store child mappings: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Publishes Phase 2 progress updates via SignalR for real-time dashboard updates
    /// </summary>
    private async Task PublishPhase2ProgressAsync(string migrationId, int processedUpdates, int totalUpdates, string status)
    {
        try
        {
            // Create a simple entity progress event for Phase 2 updates
            var progressEvent = _signalREventFactory.CreateEntityProgress(migrationId, new EntityProgressOptions
            {
                EntityType = "categories",
                TotalCount = totalUpdates,
                ProcessedCount = processedUpdates,
                Status = $"phase2_{status}",
                ProcessingTime = null // Will be set when phase completes
            });

            await _progressEventPublisher.PublishEntityProgressAsync(progressEvent);

            _logger.LogDebug("📡 [PHASE-2-SIGNALR] Published progress: {ProcessedUpdates}/{TotalUpdates} relationships processed, status: {Status}",
                processedUpdates, totalUpdates, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PHASE-2-SIGNALR] Failed to publish Phase 2 progress for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't fail the operation due to progress publishing issues
        }
    }

    /// <summary>
    /// Handle meta_keywords field - BigCommerce expects array, not string
    /// Copied from CategoryTransformStrategy to fix Phase 2 API validation errors
    /// </summary>
    private void HandleMetaKeywords(Dictionary<string, object> transformed)
    {
        if (transformed.TryGetValue("meta_keywords", out var metaKeywords))
        {
            if (metaKeywords is string metaKeywordsString)
            {
                try
                {
                    // Handle common malformed cases
                    if (string.IsNullOrWhiteSpace(metaKeywordsString) || 
                        metaKeywordsString == "[]" || 
                        metaKeywordsString == "[\"\"]" ||
                        metaKeywordsString == "[\"[]\"]")
                    {
                        transformed["meta_keywords"] = new List<string>();
                        _logger.LogDebug("🔧 [PHASE-2] Converted empty/malformed meta_keywords to empty array");
                    }
                    else
                    {
                        // Try to parse as JSON array
                        var parsedKeywords = System.Text.Json.JsonSerializer.Deserialize<List<string>>(metaKeywordsString);
                        transformed["meta_keywords"] = parsedKeywords ?? new List<string>();
                        _logger.LogDebug("🔧 [PHASE-2] Successfully parsed meta_keywords JSON");
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // If parsing fails, try to split as comma-separated values
                    var keywords = metaKeywordsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim())
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .ToList();
                    
                    transformed["meta_keywords"] = keywords;
                    _logger.LogWarning(ex, "🔧 [PHASE-2] Failed to parse meta_keywords as JSON, split as CSV instead");
                }
            }
            else if (metaKeywords is not List<string> && metaKeywords is not string[])
            {
                transformed["meta_keywords"] = new List<string>();
                _logger.LogWarning("🔧 [PHASE-2] Invalid meta_keywords type, converted to empty array");
            }
        }
    }
}

/// <summary>
/// Request for Phase 2 child category creation
/// </summary>
public class CategoryParentFixupRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public StoreConfiguration SourceStore { get; set; } = new();
    public StoreConfiguration DestinationStore { get; set; } = new();
    public Dictionary<string, object> HierarchyMetadata { get; set; } = new();
    public int TotalCategories { get; set; }
    public List<EntityMapping> Phase1Mappings { get; set; } = new();
    public CategoryTreeContext CategoryTreeContext { get; set; } = new(); // ✅ ADDED: Required for child category creation
}

/// <summary>
/// Result of Phase 2 parent relationship fixing
/// </summary>
public class CategoryParentFixupResult
{
    public string MigrationId { get; set; } = string.Empty;
    public int TotalCategories { get; set; }
    public int ProcessedCategories { get; set; }
    public int SuccessfulUpdates { get; set; }
    public int FailedUpdates { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Individual category parent update operation
/// </summary>
public class CategoryParentUpdate
{
    public string CategoryId { get; set; } = string.Empty; // New (destination) category ID
    public string? NewParentId { get; set; } // New (destination) parent ID (null for root)
    public string OriginalCategoryId { get; set; } = string.Empty; // Original (source) category ID
    public string OriginalParentId { get; set; } = string.Empty; // Original (source) parent ID
}