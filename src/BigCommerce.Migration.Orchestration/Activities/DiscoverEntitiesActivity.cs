using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for discovering entities in BigCommerce stores
/// Implements Azure Durable Functions activity pattern
/// </summary>
public class DiscoverEntitiesActivity
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<DiscoverEntitiesActivity> _logger;

    public DiscoverEntitiesActivity(IBigCommerceApiClient apiClient, ILogger<DiscoverEntitiesActivity> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers entities of a specific type from source store
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity discovery result</returns>
    [Function("DiscoverEntitiesActivity")]
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync([ActivityTrigger] EntityDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Discovering entities for migration {MigrationId}, Entity Type: {EntityType}", 
                request.MigrationId, request.EntityType);

            // Validate request
            var validationErrors = ValidateRequest(request);
            if (validationErrors.Any())
            {
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    Errors = validationErrors
                };
            }

            // OPTIMIZATION: Detect API version to determine discovery strategy
            var apiVersion = await _apiClient.DetectApiVersionAsync(request.SourceStore, cancellationToken);
            
            _logger.LogInformation("Detected API version {ApiVersion} for {EntityType} in migration {MigrationId}", 
                apiVersion, request.EntityType, request.MigrationId);

            // For V2 APIs: Skip discovery phase and use direct pagination in processing
            if (apiVersion == BigCommerceApiVersion.V2)
            {
                _logger.LogInformation("Skipping discovery for V2 API - will use direct pagination in processing phase for {EntityType}", 
                    request.EntityType);
                
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0, // Will be determined during processing
                    EntityIds = new List<string>(), // Not needed for V2
                    ApiVersion = apiVersion,
                    SkipDiscovery = true,
                    PaginationMetadata = new Dictionary<string, object>
                    {
                        { "ApiVersion", "V2" },
                        { "Strategy", "DirectPagination" },
                        { "SkipDiscovery", true }
                    }
                };
            }

            // For V3 APIs: Use enhanced discovery with entity data storage
            var result = await DiscoverEntitiesWithOptimizedV3PaginationAsync(request, cancellationToken);
            result.ApiVersion = apiVersion;

            _logger.LogInformation("Discovery completed for {EntityType}: {Count} entities found with {DataCount} entity data cached", 
                request.EntityType, result.TotalCount, result.EntityData.Count);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Entity discovery was cancelled for migration {MigrationId}, Entity Type: {EntityType}",
                request.MigrationId, request.EntityType);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { "Discovery was cancelled" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering entities for migration {MigrationId}, Entity Type: {EntityType}", 
                request.MigrationId, request.EntityType);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Discovers entities using pagination approach - gets first page and metadata for coordination
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task<EntityDiscoveryResult> DiscoverEntitiesWithPaginationAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation(
                "Starting paginated discovery for {EntityType} in migration {MigrationId}",
                request.EntityType,
                request.MigrationId);

            // For categories, we need to fetch ALL entities and sort them hierarchically
            // to ensure parents are processed before children across all batches
            if (request.EntityType.ToLowerInvariant() == "categories")
            {
                return await DiscoverCategoriesWithHierarchicalSortingAsync(request, cancellationToken);
            }

            // For all other entity types, use standard pagination without hierarchical sorting
            return await DiscoverEntitiesWithStandardPaginationAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover {EntityType} entities for migration {MigrationId}",
                request.EntityType, request.MigrationId);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Discovers entities with standard pagination (for non-hierarchical entities)
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with all entity IDs</returns>
    private async Task<EntityDiscoveryResult> DiscoverEntitiesWithStandardPaginationAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Fetching all {EntityType} entities with standard pagination in migration {MigrationId}",
                request.EntityType, request.MigrationId);

            // Create pagination request
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250, // Use large limit to minimize pagination
                IncludeDeleted = request.EntityConfig.IncludeDeleted,
                IncludeDrafts = request.EntityConfig.IncludeDrafts,
                SortBy = "id",
                SortDirection = "asc"
            };

            var allEntities = new List<Dictionary<string, object>>();
            var currentPage = 1;
            int totalPages = 1; // Initialize with default
            int totalFromApi = 0; // Track total from API response

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                paginationRequest.Page = currentPage;
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    request.SourceStore,
                    request.EntityType,
                    paginationRequest,
                    cancellationToken);

                if (response.Data != null && response.Data.Any())
                {
                    allEntities.AddRange(response.Data);
                    _logger.LogDebug("Added {PageEntities} {EntityType} entities from page {Page}", response.Data.Count, request.EntityType, currentPage);
                }

                // Track pagination metadata
                totalPages = response.TotalPages ?? 1;
                totalFromApi = response.TotalItems ?? 0;
                currentPage++;

                _logger.LogDebug("Fetched page {Page}/{TotalPages} for {EntityType} discovery in migration {MigrationId}. Page entities: {PageEntities}, Total so far: {TotalSoFar}",
                    currentPage - 1, totalPages, request.EntityType, request.MigrationId, response.Data?.Count ?? 0, allEntities.Count);

                // Safety check: prevent infinite loops
                if (currentPage > 100) // Max 100 pages (25,000 entities at 250 per page)
                {
                    _logger.LogWarning("Breaking pagination loop after 100 pages to prevent infinite loop. Current total: {Total}", allEntities.Count);
                    break;
                }

            } while (currentPage <= totalPages);

            _logger.LogInformation("Pagination completed for {EntityType}: Fetched {ActualCount} entities (API reported {ApiTotal}) across {PagesProcessed} pages in migration {MigrationId}",
                request.EntityType, allEntities.Count, totalFromApi, currentPage - 1, request.MigrationId);

            // Validate we got all expected entities
            if (totalFromApi > 0 && allEntities.Count < totalFromApi)
            {
                _logger.LogWarning("{EntityType} count mismatch: Expected {Expected} from API, but fetched {Actual}. This may indicate pagination issues.",
                    request.EntityType, totalFromApi, allEntities.Count);
            }

            // Extract entity IDs from all fetched entities
            var entityIds = allEntities
                .Where(entity => entity.TryGetValue("id", out var id) && id != null)
                .Select(entity => entity["id"].ToString()!)
                .ToList();

            _logger.LogInformation("Standard pagination completed for {EntityType}: {EntityCount} entities discovered",
                request.EntityType, entityIds.Count);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                EntityIds = entityIds,
                TotalCount = allEntities.Count, // Use actual fetched count, not API reported
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "TotalPages", totalPages },
                    { "PageSize", paginationRequest.Limit },
                    { "HierarchicallySorted", false },
                    { "ApiReportedTotal", totalFromApi },
                    { "ActualFetched", allEntities.Count }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover {EntityType} entities with standard pagination for migration {MigrationId}",
                request.EntityType, request.MigrationId);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Optimized discovery for V3 APIs with entity-type-aware strategy
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with appropriate strategy</returns>
    private async Task<EntityDiscoveryResult> DiscoverEntitiesWithOptimizedV3PaginationAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting optimized V3 discovery for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);

            // STRATEGY SELECTION: Choose based on entity characteristics
            if (IsHierarchicalEntity(request.EntityType))
            {
                // Hierarchical entities: Fetch all and cache (categories)
                _logger.LogInformation("Using hierarchical caching strategy for {EntityType} - requires global sorting", request.EntityType);
                return await DiscoverCategoriesWithHierarchicalSortingAndDataStorageAsync(request, cancellationToken);
            }
            else
            {
                // Non-hierarchical entities: Use efficient pagination metadata only (products, brands, variants)
                _logger.LogInformation("Using efficient pagination strategy for {EntityType} - no caching required", request.EntityType);
                return await DiscoverEntitiesWithEfficientPaginationMetadataAsync(request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed optimized V3 discovery for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Determines if an entity type requires hierarchical processing
    /// </summary>
    /// <param name="entityType">The entity type to check</param>
    /// <returns>True if hierarchical, false otherwise</returns>
    private static bool IsHierarchicalEntity(string entityType)
    {
        return entityType.Equals("categories", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Discovers entities with efficient V3 pagination metadata only (no caching)
    /// Optimized for large-scale entities like products, brands, variants
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with pagination metadata only</returns>
    private async Task<EntityDiscoveryResult> DiscoverEntitiesWithEfficientPaginationMetadataAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting efficient pagination metadata discovery for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);

            // OPTIMIZATION: Only fetch first page to get total count and pagination metadata
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250, // Use large limit for efficiency, but only fetch first page
                IncludeDeleted = request.EntityConfig.IncludeDeleted,
                IncludeDrafts = request.EntityConfig.IncludeDrafts,
                SortBy = "id",
                SortDirection = "asc"
            };

            _logger.LogDebug("Fetching first page of {EntityType} to determine total count and pagination metadata", request.EntityType);

            var response = await _apiClient.GetPaginatedEntitiesAsync(
                request.SourceStore,
                request.EntityType,
                paginationRequest,
                cancellationToken);

            // Extract only the entity IDs from first page (no full entity caching)
            var firstPageEntityIds = ExtractEntityIds(response.Data ?? new List<Dictionary<string, object>>(), request.EntityType);
            
            // Calculate total entity IDs based on pagination metadata
            var totalCount = response.TotalItems ?? 0;
            var totalPages = response.TotalPages ?? 1;

            _logger.LogInformation("Efficient pagination metadata discovery completed for {EntityType}: {TotalCount} entities across {TotalPages} pages (no caching)", 
                request.EntityType, totalCount, totalPages);

            // Generate entity IDs sequence without fetching all entities
            var allEntityIds = GenerateEntityIdSequence(firstPageEntityIds, totalCount, response.PerPage);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                EntityIds = allEntityIds,
                EntityData = new List<Dictionary<string, object>>(), // NO caching for scalability
                TotalCount = totalCount,
                V3PaginationMetadata = response.Meta?.Pagination,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "Strategy", "EfficientPaginationMetadata" },
                    { "TotalPages", totalPages },
                    { "PageSize", paginationRequest.Limit },
                    { "HierarchicallySorted", false },
                    { "CachingDisabled", true },
                    { "MemoryOptimized", true }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed efficient pagination metadata discovery for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Generates entity ID sequence based on pagination metadata without fetching all entities
    /// </summary>
    /// <param name="firstPageIds">Entity IDs from first page</param>
    /// <param name="totalCount">Total entity count</param>
    /// <param name="pageSize">Page size</param>
    /// <returns>Estimated entity ID sequence</returns>
    private static List<string> GenerateEntityIdSequence(List<string> firstPageIds, int totalCount, int pageSize)
    {
        var entityIds = new List<string>();
        
        if (!firstPageIds.Any() || totalCount <= 0)
        {
            return entityIds;
        }

        // For first page, use actual IDs
        entityIds.AddRange(firstPageIds);
        
        // For remaining pages, generate estimated ID sequence
        // This works because BigCommerce typically uses sequential IDs
        if (totalCount > firstPageIds.Count && firstPageIds.Count > 0)
        {
            var startId = int.Parse(firstPageIds[0]);
            var endId = int.Parse(firstPageIds[^1]);
            var increment = firstPageIds.Count > 1 ? (endId - startId) / (firstPageIds.Count - 1) : 1;
            
            // Generate remaining IDs based on sequence pattern
            var remainingCount = totalCount - firstPageIds.Count;
            var nextId = endId + increment;
            
            for (int i = 0; i < remainingCount; i++)
            {
                entityIds.Add((nextId + (i * increment)).ToString());
            }
        }
        
        return entityIds;
    }

    /// <summary>
    /// Discovers standard entities with V3 pagination and stores entity data
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with cached entity data</returns>
    private async Task<EntityDiscoveryResult> DiscoverEntitiesWithStandardPaginationAndDataStorageAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting standard V3 pagination with data storage for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);

            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250, // Use large limit for efficiency
                IncludeDeleted = request.EntityConfig.IncludeDeleted,
                IncludeDrafts = request.EntityConfig.IncludeDrafts,
                SortBy = "id",
                SortDirection = "asc"
            };

            var allEntities = new List<Dictionary<string, object>>();
            var currentPage = 1;
            BigCommerceV3Pagination? v3Metadata = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                paginationRequest.Page = currentPage;
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    request.SourceStore,
                    request.EntityType,
                    paginationRequest,
                    cancellationToken);

                if (response.Data != null && response.Data.Any())
                {
                    allEntities.AddRange(response.Data);
                    _logger.LogDebug("Cached {PageEntities} {EntityType} entities from page {Page}", 
                        response.Data.Count, request.EntityType, currentPage);
                }

                // Store V3 metadata from first page
                if (currentPage == 1 && response.Meta != null)
                {
                    v3Metadata = response.Meta.Pagination;
                }

                // Use V3 metadata for efficient pagination
                if (response.TotalPages.HasValue && currentPage >= response.TotalPages.Value)
                {
                    _logger.LogDebug("Reached last page {TotalPages} for {EntityType}", response.TotalPages.Value, request.EntityType);
                    break;
                }

                currentPage++;

                // Safety check
                if (currentPage > 100)
                {
                    _logger.LogWarning("Breaking pagination loop after 100 pages for {EntityType} to prevent infinite loop", request.EntityType);
                    break;
                }

            } while (true);

            // Extract entity IDs
            var entityIds = ExtractEntityIds(allEntities, request.EntityType);

            _logger.LogInformation("V3 standard pagination completed for {EntityType}: Cached {EntityCount} entities across {PagesProcessed} pages", 
                request.EntityType, allEntities.Count, currentPage);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                EntityIds = entityIds,
                EntityData = allEntities, // Store actual entity data
                TotalCount = allEntities.Count,
                V3PaginationMetadata = v3Metadata,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "Strategy", "OptimizedWithDataStorage" },
                    { "TotalPages", currentPage },
                    { "PageSize", paginationRequest.Limit },
                    { "HierarchicallySorted", false }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed standard V3 pagination for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Discovers categories with full hierarchical sorting and stores entity data
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with hierarchically sorted entity IDs and cached data</returns>
    private async Task<EntityDiscoveryResult> DiscoverCategoriesWithHierarchicalSortingAndDataStorageAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting categories hierarchical sorting with data storage for migration {MigrationId}", 
                request.MigrationId);

            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250, // Use large limit to minimize pagination
                IncludeDeleted = request.EntityConfig.IncludeDeleted,
                IncludeDrafts = request.EntityConfig.IncludeDrafts,
                CategoryTreeId = request.CategoryTreeContext?.SourceCategoryTreeId,
                SortBy = "id",
                SortDirection = "asc"
            };

            var allCategories = new List<Dictionary<string, object>>();
            var currentPage = 1;
            BigCommerceV3Pagination? v3Metadata = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                paginationRequest.Page = currentPage;
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    request.SourceStore,
                    request.EntityType,
                    paginationRequest,
                    cancellationToken);

                if (response.Data != null && response.Data.Any())
                {
                    allCategories.AddRange(response.Data);
                    _logger.LogDebug("Cached {PageEntities} categories from page {Page}", response.Data.Count, currentPage);
                }

                // Store V3 metadata from first page
                if (currentPage == 1 && response.Meta != null)
                {
                    v3Metadata = response.Meta.Pagination;
                }

                // Use V3 metadata for efficient pagination
                if (response.TotalPages.HasValue && currentPage >= response.TotalPages.Value)
                {
                    _logger.LogDebug("Reached last page {TotalPages} for categories", response.TotalPages.Value);
                    break;
                }

                currentPage++;

                // Safety check
                if (currentPage > 50)
                {
                    _logger.LogWarning("Breaking pagination loop after 50 pages for categories to prevent infinite loop");
                    break;
                }

            } while (true);

            _logger.LogInformation("Pagination completed for categories: Cached {ActualCount} categories across {PagesProcessed} pages", 
                allCategories.Count, currentPage);

            // Sort categories hierarchically
            var sortedCategories = SortCategoriesHierarchically(allCategories);
            
            // Extract entity IDs in hierarchical order
            var hierarchicalEntityIds = ExtractEntityIds(sortedCategories, request.EntityType);

            _logger.LogInformation("Hierarchical sorting completed for categories: {EntityCount} entities sorted and cached", 
                sortedCategories.Count);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                EntityIds = hierarchicalEntityIds,
                EntityData = sortedCategories, // Store hierarchically sorted entity data
                TotalCount = sortedCategories.Count,
                V3PaginationMetadata = v3Metadata,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "Strategy", "OptimizedWithDataStorage" },
                    { "TotalPages", currentPage },
                    { "PageSize", paginationRequest.Limit },
                    { "HierarchicallySorted", true }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed hierarchical categories discovery for migration {MigrationId}", 
                request.MigrationId);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Discovers categories with full hierarchical sorting to ensure correct processing order
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with hierarchically sorted entity IDs</returns>
    private async Task<EntityDiscoveryResult> DiscoverCategoriesWithHierarchicalSortingAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Fetching all categories for hierarchical sorting in migration {MigrationId}",
                request.MigrationId);

            // Fetch ALL categories to perform global hierarchical sorting
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250, // Use large limit to minimize pagination
                IncludeDeleted = request.EntityConfig.IncludeDeleted,
                IncludeDrafts = request.EntityConfig.IncludeDrafts,
                CategoryTreeId = request.CategoryTreeContext?.SourceCategoryTreeId,
                SortBy = "id",
                SortDirection = "asc"
            };

            var allCategories = new List<Dictionary<string, object>>();
            var currentPage = 1;
            int totalPages = 1; // Initialize with default
            int totalFromApi = 0; // Track total from API response

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                paginationRequest.Page = currentPage;
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    request.SourceStore,
                    request.EntityType,
                    paginationRequest,
                    cancellationToken);

                if (response.Data != null && response.Data.Any())
                {
                    allCategories.AddRange(response.Data);
                    _logger.LogDebug("Added {PageEntities} categories from page {Page}", response.Data.Count, currentPage);
                }

                // Track pagination metadata
                totalPages = response.TotalPages ?? 1;
                totalFromApi = response.TotalItems ?? 0;
                currentPage++;

                _logger.LogDebug("Fetched page {Page}/{TotalPages} for category discovery in migration {MigrationId}. Page entities: {PageEntities}, Total so far: {TotalSoFar}",
                    currentPage - 1, totalPages, request.MigrationId, response.Data?.Count ?? 0, allCategories.Count);

                // Safety check: prevent infinite loops
                if (currentPage > 50) // Max 50 pages (12,500 categories at 250 per page)
                {
                    _logger.LogWarning("Breaking pagination loop after 50 pages to prevent infinite loop. Current total: {Total}", allCategories.Count);
                    break;
                }

            } while (currentPage <= totalPages);

            _logger.LogInformation("Pagination completed for categories: Fetched {ActualCount} categories (API reported {ApiTotal}) across {PagesProcessed} pages in migration {MigrationId}",
                allCategories.Count, totalFromApi, currentPage - 1, request.MigrationId);

            // Validate we got all expected categories
            if (totalFromApi > 0 && allCategories.Count < totalFromApi)
            {
                _logger.LogWarning("Category count mismatch: Expected {Expected} from API, but fetched {Actual}. This may indicate pagination issues.",
                    totalFromApi, allCategories.Count);
            }

            // Sort categories hierarchically to ensure parents come before children
            var sortedCategories = SortCategoriesHierarchically(allCategories);
            
            // Extract entity IDs in hierarchical order
            var hierarchicalEntityIds = sortedCategories
                .Where(c => c.TryGetValue("id", out var id) && id != null)
                .Select(c => c["id"].ToString()!)
                .ToList();

            _logger.LogInformation("Hierarchical sorting completed for {EntityType}: {EntityCount} entities sorted from {OriginalCount} fetched",
                request.EntityType, hierarchicalEntityIds.Count, allCategories.Count);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                EntityIds = hierarchicalEntityIds,
                TotalCount = allCategories.Count, // Use actual fetched count, not API reported
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "TotalPages", totalPages },
                    { "PageSize", paginationRequest.Limit },
                    { "HierarchicallySorted", true },
                    { "ApiReportedTotal", totalFromApi },
                    { "ActualFetched", allCategories.Count }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover categories with hierarchical sorting for migration {MigrationId}",
                request.MigrationId);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Sorts categories hierarchically to ensure parents are processed before children
    /// This prevents parent_id mapping failures during transformation
    /// </summary>
    /// <param name="categories">Unsorted categories</param>
    /// <returns>Categories sorted by hierarchy depth (parents first)</returns>
    private List<Dictionary<string, object>> SortCategoriesHierarchically(List<Dictionary<string, object>> categories)
    {
        if (!categories.Any())
        {
            return categories;
        }

        try
        {
            _logger.LogDebug("Starting hierarchical sort of {CategoryCount} categories", categories.Count);

            // Create a lookup dictionary for faster parent lookups
            var categoryLookup = categories.ToDictionary(
                c => c.TryGetValue("id", out var id) ? id.ToString()! : string.Empty,
                c => c
            );

            // Calculate depth for each category
            var categoryDepths = new Dictionary<string, int>();
            
            foreach (var category in categories)
            {
                var categoryId = category.TryGetValue("id", out var id) ? id.ToString()! : string.Empty;
                if (!string.IsNullOrEmpty(categoryId))
                {
                    categoryDepths[categoryId] = CalculateCategoryDepth(category, categoryLookup, new HashSet<string>());
                }
            }

            // Sort by depth (parents first), then by name for consistency
            var sortedCategories = categories
                .OrderBy(c => 
                {
                    var categoryId = c.TryGetValue("id", out var id) ? id.ToString()! : string.Empty;
                    return categoryDepths.TryGetValue(categoryId, out var depth) ? depth : int.MaxValue;
                })
                .ThenBy(c => c.TryGetValue("name", out var name) ? name.ToString() : string.Empty)
                .ToList();

            // Log the sorting results for debugging
            _logger.LogDebug("Hierarchical sorting results (showing first 10):");
            foreach (var category in sortedCategories.Take(10)) 
            {
                var categoryId = category.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                var categoryName = category.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                var parentId = category.TryGetValue("parent_id", out var parent) ? parent?.ToString() : "null";
                var depth = categoryDepths.TryGetValue(categoryId!, out var d) ? d : -1;
                
                _logger.LogDebug("  Category: {CategoryName} (ID: {CategoryId}, Parent: {ParentId}, Depth: {Depth})",
                    categoryName, categoryId, parentId, depth);
            }

            _logger.LogInformation("Hierarchical sorting completed. Categories arranged by depth for proper parent-child processing order.");

            return sortedCategories;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sort categories hierarchically, falling back to original order");
            return categories;
        }
    }

    /// <summary>
    /// Calculates the depth of a category in the hierarchy (root = 0, first level = 1, etc.)
    /// </summary>
    /// <param name="category">Category to calculate depth for</param>
    /// <param name="categoryLookup">Lookup dictionary for fast parent access</param>
    /// <param name="visited">Set to track visited categories and prevent infinite loops</param>
    /// <returns>Depth of the category (0 for root categories)</returns>
    private int CalculateCategoryDepth(Dictionary<string, object> category, Dictionary<string, Dictionary<string, object>> categoryLookup, HashSet<string> visited)
    {
        var categoryId = category.TryGetValue("id", out var id) ? id.ToString()! : string.Empty;
        
        // Prevent infinite loops from circular references
        if (visited.Contains(categoryId))
        {
            _logger.LogWarning("Circular reference detected for category {CategoryId}", categoryId);
            return 0; // Treat as root category
        }
        
        visited.Add(categoryId);

        // Check if this is a root category
        if (!category.TryGetValue("parent_id", out var parentId) || 
            parentId == null || 
            parentId.ToString() == "0" || 
            string.IsNullOrEmpty(parentId.ToString()))
        {
            return 0; // Root category
        }

        var parentIdString = parentId.ToString()!;
        
        // Check if parent exists in our category set
        if (!categoryLookup.TryGetValue(parentIdString, out var parentCategory))
        {
            _logger.LogDebug("Parent category {ParentId} not found for category {CategoryId}, treating as root", 
                parentIdString, categoryId);
            return 0; // Treat as root if parent not found
        }

        // Recursively calculate parent depth and add 1
        var parentDepth = CalculateCategoryDepth(parentCategory, categoryLookup, new HashSet<string>(visited));
        return parentDepth + 1;
    }

    /// <summary>
    /// Estimates total count for V2 API or when metadata is unavailable
    /// </summary>
    private static int EstimateTotalCount(BigCommercePaginatedResponse<Dictionary<string, object>> response)
    {
        if (response.TotalItems.HasValue)
            return response.TotalItems.Value;

        // For V2 API, estimate based on first page data
        if (response.Data.Count < response.PerPage)
        {
            // If first page is not full, it's likely the only page
            return response.Data.Count;
        }

        // Conservative estimate - assume there are more pages
        return response.Data.Count * 10; // Rough estimate for planning
    }

    /// <summary>
    /// Estimates total pages for V2 API or when metadata is unavailable
    /// </summary>
    private static int EstimateTotalPages(BigCommercePaginatedResponse<Dictionary<string, object>> response)
    {
        if (response.TotalPages.HasValue)
            return response.TotalPages.Value;

        var estimatedTotal = EstimateTotalCount(response);
        return (int)Math.Ceiling((double)estimatedTotal / response.PerPage);
    }

    /// <summary>
    /// Extracts entity IDs from response data
    /// </summary>
    private static List<string> ExtractEntityIds(List<Dictionary<string, object>> entities, string entityType)
    {
        var entityIds = new List<string>();
        
        foreach (var entity in entities)
        {
            if (entity.TryGetValue("id", out var idValue))
            {
                entityIds.Add(idValue.ToString() ?? string.Empty);
            }
            else if (entity.TryGetValue("Id", out var idValueCapital))
            {
                entityIds.Add(idValueCapital.ToString() ?? string.Empty);
            }
        }
        
        return entityIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
    }

    // Legacy DiscoverProductsAsync method removed - now handled by DiscoverEntitiesWithPaginationAsync

    // Legacy DiscoverCategoriesAsync method removed - now handled by DiscoverEntitiesWithPaginationAsync

    // Legacy DiscoverBrandsAsync method removed - now handled by DiscoverEntitiesWithPaginationAsync

    // Legacy DiscoverProductVariantsAsync, DiscoverProductImagesAsync, and DiscoverProductModifiersAsync methods removed - now handled by DiscoverEntitiesWithPaginationAsync

    /// <summary>
    /// Validates the entity discovery request
    /// </summary>
    private List<string> ValidateRequest(EntityDiscoveryRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(request.MigrationId))
        {
            errors.Add("MigrationId is required");
        }

        if (string.IsNullOrEmpty(request.EntityType))
        {
            errors.Add("EntityType is required");
        }

        if (request.SourceStore == null)
        {
            errors.Add("SourceStore is required");
        }
        else
        {
            if (string.IsNullOrEmpty(request.SourceStore.StoreId))
            {
                errors.Add("SourceStore.StoreId is required");
            }

            if (string.IsNullOrEmpty(request.SourceStore.AccessToken))
            {
                errors.Add("SourceStore.AccessToken is required");
            }
        }

        return errors;
    }
} 