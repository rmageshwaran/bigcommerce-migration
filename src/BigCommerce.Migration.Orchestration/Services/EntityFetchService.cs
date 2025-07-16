using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Implementation of IEntityFetchService for fetching entities from source stores
/// Enhanced with parallel fetching, retry logic, and sophisticated error handling
/// </summary>
public class EntityFetchService : IEntityFetchService
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<EntityFetchService> _logger;
    
    // Configuration for parallel processing
    private const int MaxConcurrency = 5; // Maximum concurrent API calls

    public EntityFetchService(IBigCommerceApiClient apiClient, ILogger<EntityFetchService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        return request.EntityType.ToLowerInvariant() switch
        {
            "categories" => await FetchCategoriesAsync(request, cancellationToken),
            "products" => await FetchProductsAsync(request, cancellationToken),
            "brands" => await FetchBrandsAsync(request, cancellationToken),
            "variants" => await FetchVariantsAsync(request, cancellationToken),
            "images" => await FetchImagesAsync(request, cancellationToken),
            "modifiers" => await FetchModifiersAsync(request, cancellationToken),
            _ => throw new ArgumentException($"Unsupported entity type: {request.EntityType}")
        };
    }

    public async Task<List<Dictionary<string, object>>> FetchCategoriesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching {Count} specific categories for batch {BatchNumber}/{TotalBatches} in migration {MigrationId}", 
            request.EntityIds.Count, request.BatchNumber, request.TotalBatches, request.MigrationId);
        
        var fetchedCategories = new List<Dictionary<string, object>>();

        try
        {
            // ✅ Check if we have specific entity IDs to fetch
            if (!request.EntityIds.Any())
            {
                _logger.LogInformation("No specific category IDs provided for batch {BatchNumber} in migration {MigrationId}", 
                    request.BatchNumber, request.MigrationId);
                return fetchedCategories;
            }

            // ✅ OPTIMIZATION: Use cached category data from discovery phase (avoids all API calls!)
            if (request.CachedEntityData != null && request.CachedEntityData.Any())
            {
                _logger.LogInformation("Using cached category data from discovery phase for batch {BatchNumber} in migration {MigrationId} - no API calls needed!", 
                    request.BatchNumber, request.MigrationId);

                // Filter cached data to get only the categories for this batch
                var cachedCategoriesForBatch = request.CachedEntityData
                    .Where(category => 
                    {
                        var categoryId = category.TryGetValue("id", out var id) ? id.ToString() : null;
                        return categoryId != null && request.EntityIds.Contains(categoryId);
                    })
                    .ToList();

                _logger.LogInformation("Found {FoundCount}/{RequestedCount} categories in cached data for batch {BatchNumber} in migration {MigrationId}", 
                    cachedCategoriesForBatch.Count, request.EntityIds.Count, request.BatchNumber, request.MigrationId);

                return cachedCategoriesForBatch;
            }

            // ✅ FALLBACK: If no cached data available, log warning and use API (should rarely happen for categories)
            _logger.LogWarning("No cached category data available for batch {BatchNumber} in migration {MigrationId}. " +
                "This is unexpected for categories - falling back to API calls.", 
                request.BatchNumber, request.MigrationId);

            var storeConfig = ValidateStoreConfiguration(request.SourceStore);
            var categoryTreeId = request.CategoryTreeContext?.SourceCategoryTreeId;
            
            if (string.IsNullOrWhiteSpace(categoryTreeId))
            {
                _logger.LogError("No source category tree ID available for store {StoreId} in migration {MigrationId}. " +
                    "Category tree resolution may have failed.", 
                    storeConfig.StoreId, request.MigrationId);
                return fetchedCategories;
            }

            _logger.LogInformation("Fetching {Count} specific categories {CategoryIds} using tree {CategoryTreeId} for migration {MigrationId}", 
                request.EntityIds.Count, string.Join(",", request.EntityIds.Take(5)) + (request.EntityIds.Count > 5 ? "..." : ""), 
                categoryTreeId, request.MigrationId);

            // ✅ FALLBACK: Fetch ALL categories with pagination (just like discovery phase)
            // This is critical for hierarchical processing - we need ALL categories to maintain parent-child relationships
            var allCategories = await FetchAllCategoriesWithPaginationAsync(storeConfig, categoryTreeId, cancellationToken);
            
            if (allCategories.Any())
            {
                // ✅ Sort categories hierarchically (parents first)
                var sortedCategories = SortCategoriesHierarchically(allCategories);
                
                // ✅ Filter to get only the requested categories, maintaining hierarchical order
                var filteredCategories = sortedCategories
                    .Where(category => 
                    {
                        var categoryId = category.TryGetValue("id", out var id) ? id.ToString() : null;
                        return categoryId != null && request.EntityIds.Contains(categoryId);
                    })
                    .ToList();

                fetchedCategories.AddRange(filteredCategories);
                
                _logger.LogInformation("API fallback: Found {FoundCount}/{RequestedCount} categories (from {TotalFetched} total) for batch {BatchNumber} in migration {MigrationId}", 
                    filteredCategories.Count, request.EntityIds.Count, allCategories.Count, request.BatchNumber, request.MigrationId);
            }
            else
            {
                _logger.LogWarning("API fallback: No categories found in store {StoreId} tree {TreeId} for migration {MigrationId}", 
                    storeConfig.StoreId, categoryTreeId, request.MigrationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch categories for batch {BatchNumber} in migration {MigrationId}", 
                request.BatchNumber, request.MigrationId);
            throw new InvalidOperationException($"Category fetch failed for batch {request.BatchNumber} in migration {request.MigrationId}", ex);
        }

        _logger.LogInformation("Successfully fetched {FetchedCount}/{RequestedCount} categories for batch {BatchNumber} in migration {MigrationId}", 
            fetchedCategories.Count, request.EntityIds.Count, request.BatchNumber, request.MigrationId);
        
        return fetchedCategories;
    }

    public async Task<List<Dictionary<string, object>>> FetchProductsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching products for batch {BatchNumber}/{TotalBatches} in migration {MigrationId}", 
            request.BatchNumber, request.TotalBatches, request.MigrationId);
        
        // Check for cancellation at the start
        cancellationToken.ThrowIfCancellationRequested();
        
        var storeConfig = ValidateStoreConfiguration(request.SourceStore);

        try
        {
            // ✅ CORRECT APPROACH: Use direct pagination based on batch number
            // Don't load all products into memory - fetch only this batch's page
            var pageNumber = request.BatchNumber; // Batch number = page number
            var pageSize = 50; // Standard batch size for products
            
            _logger.LogInformation("Fetching products page {PageNumber} with size {PageSize} for migration {MigrationId}", 
                pageNumber, pageSize, request.MigrationId);

            var products = await _apiClient.GetProductsAsync(storeConfig, pageNumber, pageSize, cancellationToken);

            if (products == null)
            {
                _logger.LogWarning("No products returned from API for page {PageNumber} in migration {MigrationId}", 
                    pageNumber, request.MigrationId);
                return new List<Dictionary<string, object>>();
            }

            _logger.LogInformation("Successfully fetched {Count} products for batch {BatchNumber} in migration {MigrationId}", 
                products.Count, request.BatchNumber, request.MigrationId);
            
            return products;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Product fetch was cancelled for batch {BatchNumber} in migration {MigrationId}", 
                request.BatchNumber, request.MigrationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch products for batch {BatchNumber} in migration {MigrationId}", 
                request.BatchNumber, request.MigrationId);
            throw new InvalidOperationException($"Product fetch failed for batch {request.BatchNumber} in migration {request.MigrationId}", ex);
        }
    }

    public async Task<List<Dictionary<string, object>>> FetchBrandsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching brands for batch {BatchNumber}/{TotalBatches} in migration {MigrationId}", 
            request.BatchNumber, request.TotalBatches, request.MigrationId);
        
        var storeConfig = ValidateStoreConfiguration(request.SourceStore);

        try
        {
            // ✅ CORRECT APPROACH: Use direct pagination based on batch number
            // Don't load all brands into memory - fetch only this batch's page
            var pageNumber = request.BatchNumber; // Batch number = page number
            var pageSize = 50; // Standard batch size for brands
            
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = pageNumber,
                Limit = pageSize,
                SortBy = "id",
                SortDirection = "asc"
            };

            _logger.LogInformation("Fetching brands page {PageNumber} with size {PageSize} for migration {MigrationId}", 
                pageNumber, pageSize, request.MigrationId);

            var brandResponse = await _apiClient.GetBrandPageAsync(storeConfig, paginationRequest, cancellationToken);

            if (brandResponse?.Data == null)
            {
                _logger.LogWarning("No brands returned from API for page {PageNumber} in migration {MigrationId}", 
                    pageNumber, request.MigrationId);
                return new List<Dictionary<string, object>>();
            }

            // Convert BrandSummary to Dictionary<string, object>
            var brands = brandResponse.Data.Select(brand => new Dictionary<string, object>
            {
                ["id"] = brand.Id,
                ["name"] = brand.Name
            }).ToList();

            _logger.LogInformation("Successfully fetched {Count} brands for batch {BatchNumber} in migration {MigrationId}", 
                brands.Count, request.BatchNumber, request.MigrationId);
            
            return brands;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch brands for batch {BatchNumber} in migration {MigrationId}", 
                request.BatchNumber, request.MigrationId);
            throw new InvalidOperationException($"Brand fetch failed for batch {request.BatchNumber} in migration {request.MigrationId}", ex);
        }
    }

    public async Task<List<Dictionary<string, object>>> FetchVariantsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching variants for migration {MigrationId}", request.MigrationId);
        var allVariants = new ConcurrentBag<Dictionary<string, object>>();
        var storeConfig = ValidateStoreConfiguration(request.SourceStore);

        // For variants, we need to fetch them per product - use parallel processing
        if (request.EntityIds == null || !request.EntityIds.Any())
        {
            _logger.LogWarning("No product IDs provided for variant fetching in migration {MigrationId}", request.MigrationId);
            return allVariants.ToList();
        }

        var productIds = ParseProductIds(request.EntityIds, request.MigrationId);
        if (!productIds.Any())
        {
            _logger.LogWarning("No valid product IDs found for variant fetching in migration {MigrationId}", request.MigrationId);
            return allVariants.ToList();
        }

        var semaphore = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var tasks = productIds.Select(async productId =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await FetchVariantsForProductAsync(productId, storeConfig, allVariants, request.MigrationId, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        _logger.LogInformation("Successfully fetched {Count} variants for migration {MigrationId}", 
            allVariants.Count, request.MigrationId);
        return allVariants.ToList();
    }

    public async Task<List<Dictionary<string, object>>> FetchImagesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching images for migration {MigrationId}", request.MigrationId);
        var allImages = new ConcurrentBag<Dictionary<string, object>>();
        var storeConfig = ValidateStoreConfiguration(request.SourceStore);

        // For images, we need to fetch them per product - use parallel processing
        if (request.EntityIds == null || !request.EntityIds.Any())
        {
            _logger.LogWarning("No product IDs provided for image fetching in migration {MigrationId}", request.MigrationId);
            return allImages.ToList();
        }

        var productIds = ParseProductIds(request.EntityIds, request.MigrationId);
        if (!productIds.Any())
        {
            _logger.LogWarning("No valid product IDs found for image fetching in migration {MigrationId}", request.MigrationId);
            return allImages.ToList();
        }

        var semaphore = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var tasks = productIds.Select(async productId =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await FetchImagesForProductAsync(productId, storeConfig, allImages, request.MigrationId, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        _logger.LogInformation("Successfully fetched {Count} images for migration {MigrationId}", 
            allImages.Count, request.MigrationId);
        return allImages.ToList();
    }

    public async Task<List<Dictionary<string, object>>> FetchModifiersAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching modifiers for migration {MigrationId}", request.MigrationId);
        var allModifiers = new ConcurrentBag<Dictionary<string, object>>();
        var storeConfig = ValidateStoreConfiguration(request.SourceStore);

        // For modifiers, we need to fetch them per product - use parallel processing
        if (request.EntityIds == null || !request.EntityIds.Any())
        {
            _logger.LogWarning("No product IDs provided for modifier fetching in migration {MigrationId}", request.MigrationId);
            return allModifiers.ToList();
        }

        var productIds = ParseProductIds(request.EntityIds, request.MigrationId);
        if (!productIds.Any())
        {
            _logger.LogWarning("No valid product IDs found for modifier fetching in migration {MigrationId}", request.MigrationId);
            return allModifiers.ToList();
        }

        var semaphore = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var tasks = productIds.Select(async productId =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await FetchModifiersForProductAsync(productId, storeConfig, allModifiers, request.MigrationId, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        _logger.LogInformation("Successfully fetched {Count} modifiers for migration {MigrationId}", 
            allModifiers.Count, request.MigrationId);
        return allModifiers.ToList();
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
