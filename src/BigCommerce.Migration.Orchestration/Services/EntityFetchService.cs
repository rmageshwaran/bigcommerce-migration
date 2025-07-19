using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Implementation of IEntityFetchService for fetching entities from source stores
/// Refactored to use Strategy Pattern for Open/Closed Principle compliance
/// No longer violates OCP - new entity types can be added without modifying this class
/// </summary>
public class EntityFetchService : IEntityFetchService
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly IEntityFetchStrategyFactory _strategyFactory;
    private readonly ILogger<EntityFetchService> _logger;
    
    // Configuration for parallel processing (kept for backward compatibility)
    private const int MaxConcurrency = 5; // Maximum concurrent API calls

    public EntityFetchService(
        IBigCommerceApiClient apiClient, 
        IEntityFetchStrategyFactory strategyFactory,
        ILogger<EntityFetchService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching entities of type {EntityType} for batch {BatchNumber} in migration {MigrationId}", 
            request.EntityType, request.BatchNumber, request.MigrationId);

        try
        {
            // 🎯 STRATEGY PATTERN: Delegate to appropriate strategy instead of switch statement
            // This eliminates OCP violation - new entity types can be added without modifying this code
            var strategy = _strategyFactory.GetStrategy(request.EntityType);
            
            var result = await strategy.FetchEntitiesAsync(
                request.EntityIds,
                request.MigrationId,
                request.SourceStore,
                request.CategoryTreeContext,
                cancellationToken);

            _logger.LogInformation("Successfully fetched {Count} entities of type {EntityType} for batch {BatchNumber} in migration {MigrationId}", 
                result.Count, request.EntityType, request.BatchNumber, request.MigrationId);

            return result;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Unsupported entity type {EntityType} for batch {BatchNumber} in migration {MigrationId}", 
                request.EntityType, request.BatchNumber, request.MigrationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch entities of type {EntityType} for batch {BatchNumber} in migration {MigrationId}", 
                request.EntityType, request.BatchNumber, request.MigrationId);
            throw;
        }
    }

    public async Task<List<Dictionary<string, object>>> FetchCategoriesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 CACHED DATA HANDLING: Check for cached data first (common for category tree scenarios)
        if (request.CachedEntityData != null && request.CachedEntityData.Count > 0)
        {
            _logger.LogDebug("Using cached category data for migration {MigrationId}, count: {Count}", 
                request.MigrationId, request.CachedEntityData.Count);
            return request.CachedEntityData;
        }

        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchCategoriesAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    public async Task<List<Dictionary<string, object>>> FetchProductsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchProductsAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        try
        {
            return await FetchEntitiesAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            var message = "Product fetch failed";
            _logger.LogError(ex, "{Message} for migration {MigrationId}", message, request.MigrationId);
            throw new InvalidOperationException(message, ex);
        }
    }

    public async Task<List<Dictionary<string, object>>> FetchBrandsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchBrandsAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    public async Task<List<Dictionary<string, object>>> FetchVariantsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchVariantsAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    public async Task<List<Dictionary<string, object>>> FetchImagesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchImagesAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    public async Task<List<Dictionary<string, object>>> FetchModifiersAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchModifiersAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    #region Private Helper Methods

    private StoreConfiguration ValidateStoreConfiguration(StoreConfiguration? storeConfig)
    {
        if (storeConfig == null || !storeConfig.IsValid())
            throw new ArgumentException("Invalid source store configuration");
        return storeConfig;
    }

    private List<int> ParseProductIds(List<string> entityIds, string migrationId)
    {
        var productIds = new List<int>();
        foreach (var productIdStr in entityIds)
        {
            if (!int.TryParse(productIdStr, out var productId))
            {
                _logger.LogWarning("Invalid product ID: {ProductId} in migration {MigrationId}", productIdStr, migrationId);
                continue;
            }
            productIds.Add(productId);
        }
        return productIds;
    }

    private async Task<List<Dictionary<string, object>>> FetchAllCategoriesWithPaginationAsync(
        StoreConfiguration storeConfig, string categoryTreeId, CancellationToken cancellationToken)
    {
        var allCategories = new List<Dictionary<string, object>>();
        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = 250, // Fetch a large number of categories
            CategoryTreeId = categoryTreeId,
            IncludeDeleted = false,
            IncludeDrafts = false,
            SortBy = "id",
            SortDirection = "asc"
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await _apiClient.GetPaginatedEntitiesAsync(
                storeConfig, 
                "categories", // Always fetch categories
                paginationRequest, 
                cancellationToken);

            if (response.Data == null || response.Data.Count == 0)
                break;

            allCategories.AddRange(response.Data);
            _logger.LogDebug("Fetched {Count} categories from page {Page}", response.Data.Count, paginationRequest.Page);

            if (!response.HasNextPage)
                break;

            paginationRequest.Page++;
        }
        return allCategories;
    }

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

            _logger.LogDebug("Hierarchical sorting completed for {CategoryCount} categories", sortedCategories.Count);
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

        // Recursive depth calculation
        return 1 + CalculateCategoryDepth(parentCategory, categoryLookup, visited);
    }


    private async Task FetchVariantsForProductAsync(int productId, StoreConfiguration storeConfig, 
        ConcurrentBag<Dictionary<string, object>> allVariants, string migrationId, CancellationToken cancellationToken)
    {
        try
        {
            var variants = await _apiClient.GetProductVariantsAsync(storeConfig, productId, cancellationToken);

            if (variants != null)
            {
                var variantDicts = variants.Select(variant => new Dictionary<string, object>
                {
                    ["id"] = variant.Id,
                    ["product_id"] = variant.ProductId
                }).ToList();

                foreach (var variant in variantDicts)
                {
                    allVariants.Add(variant);
                }
                _logger.LogDebug("Fetched {Count} variants for product {ProductId}", variants.Count, productId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch variants for product {ProductId} in migration {MigrationId}", 
                productId, migrationId);
        }
    }

    private async Task FetchImagesForProductAsync(int productId, StoreConfiguration storeConfig, 
        ConcurrentBag<Dictionary<string, object>> allImages, string migrationId, CancellationToken cancellationToken)
    {
        try
        {
            var images = await _apiClient.GetProductImagesAsync(storeConfig, productId, cancellationToken);

            if (images != null)
            {
                var imageDicts = images.Select(image => new Dictionary<string, object>
                {
                    ["id"] = image.Id,
                    ["product_id"] = image.ProductId
                }).ToList();

                foreach (var image in imageDicts)
                {
                    allImages.Add(image);
                }
                _logger.LogDebug("Fetched {Count} images for product {ProductId}", images.Count, productId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch images for product {ProductId} in migration {MigrationId}", 
                productId, migrationId);
        }
    }

    private async Task FetchModifiersForProductAsync(int productId, StoreConfiguration storeConfig, 
        ConcurrentBag<Dictionary<string, object>> allModifiers, string migrationId, CancellationToken cancellationToken)
    {
        try
        {
            var modifiers = await _apiClient.GetProductModifiersAsync(storeConfig, productId, cancellationToken);

            if (modifiers != null)
            {
                var modifierDicts = modifiers.Select(modifier => new Dictionary<string, object>
                {
                    ["id"] = modifier.Id,
                    ["product_id"] = modifier.ProductId
                }).ToList();

                foreach (var modifier in modifierDicts)
                {
                    allModifiers.Add(modifier);
                }
                _logger.LogDebug("Fetched {Count} modifiers for product {ProductId}", modifiers.Count, productId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch modifiers for product {ProductId} in migration {MigrationId}", 
                productId, migrationId);
        }
    }

    #endregion
} 
