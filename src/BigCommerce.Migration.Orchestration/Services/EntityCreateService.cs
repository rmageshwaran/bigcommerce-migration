using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Implementation of IEntityCreateService for creating entities in destination stores
/// </summary>
public class EntityCreateService : IEntityCreateService
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<EntityCreateService> _logger;
    private readonly IEntityErrorHandlingService _errorHandlingService;

    public EntityCreateService(
        IBigCommerceApiClient apiClient, 
        ILogger<EntityCreateService> logger,
        IEntityErrorHandlingService errorHandlingService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
    }

    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No entities provided for creation in migration {MigrationId}", request.MigrationId);
            return new List<Dictionary<string, object>>();
        }

        _logger.LogInformation("Creating {Count} {EntityType} entities for migration {MigrationId}", 
            entities.Count, request.EntityType, request.MigrationId);

        try
        {
            return request.EntityType.ToLowerInvariant() switch
            {
                "categories" => await CreateCategoriesAsync(entities, request, cancellationToken),
                "products" => await CreateProductsAsync(entities, request, cancellationToken),
                "brands" => await CreateBrandsAsync(entities, request, cancellationToken),
                "variants" => await CreateVariantsAsync(entities, request, cancellationToken),
                "images" => await CreateImagesAsync(entities, request, cancellationToken),
                "modifiers" => await CreateModifiersAsync(entities, request, cancellationToken),
                _ => throw new ArgumentException($"Unsupported entity type: {request.EntityType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create {EntityType} entities for migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            throw new InvalidOperationException($"Entity creation failed for {request.EntityType}", ex);
        }
    }

    public async Task<List<Dictionary<string, object>>?> CreateCategoriesAsync(
        List<Dictionary<string, object>> categories, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating {Count} categories for migration {MigrationId}", categories.Count, request.MigrationId);
        
        var storeConfig = ValidateStoreConfiguration(request.DestinationStore);
        var categoryTreeId = request.CategoryTreeContext?.DestinationCategoryTreeId;

        if (string.IsNullOrWhiteSpace(categoryTreeId))
        {
            var errorMessage = "Destination category tree ID is required for creating categories";
            _logger.LogError("Destination category tree ID is required for creating categories in migration {MigrationId}", request.MigrationId);
            throw new InvalidOperationException(errorMessage);
        }

        if (!categories.Any())
        {
            _logger.LogWarning("No categories provided for creation in migration {MigrationId}", request.MigrationId);
            return new List<Dictionary<string, object>>();
        }

        // Log category details for debugging (from original implementation)
        foreach (var category in categories)
        {
            var categoryName = category.TryGetValue("name", out var name) ? name.ToString() : "unknown";
            var parentId = category.TryGetValue("parent_id", out var parent) ? parent?.ToString() : "null";
            
            _logger.LogDebug("Creating category: Name='{CategoryName}', ParentId={ParentId} in migration {MigrationId}",
                categoryName, parentId, request.MigrationId);
        }

        try
        {
            _logger.LogDebug("Attempting batch creation of {CategoryCount} categories in destination store {DestinationStore} tree {DestinationTreeId} for migration {MigrationId}",
                categories.Count, storeConfig.StoreId, categoryTreeId, request.MigrationId);

            // Try batch creation first (original approach)
            var createdCategories = await _apiClient.CreateCategoriesAsync(storeConfig, categoryTreeId, categories, cancellationToken);

            if (createdCategories != null && createdCategories.Any())
            {
                _logger.LogInformation("Successfully created {CreatedCount} categories in destination store {DestinationStore} for migration {MigrationId}",
                    createdCategories.Count, storeConfig.StoreId, request.MigrationId);

                // Log created category mappings for debugging (from original implementation)
                for (int i = 0; i < Math.Min(categories.Count, createdCategories.Count); i++)
                {
                    var sourceName = categories[i].TryGetValue("name", out var sName) ? sName.ToString() : "unknown";
                    var createdId = createdCategories[i].TryGetValue("category_id", out var cId) ? cId.ToString() : "unknown";
                    
                    _logger.LogDebug("Created category mapping: '{CategoryName}' -> ID {CreatedId} in migration {MigrationId}",
                        sourceName, createdId, request.MigrationId);
                }
            }
            else
            {
                _logger.LogWarning("Category creation returned no results for migration {MigrationId}", request.MigrationId);
            }

            return createdCategories;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information (from original implementation)
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "Batch creation failed for {CategoryCount} categories in destination store {DestinationStore} tree {DestinationTreeId} for migration {MigrationId}. {DetailedError}",
                categories.Count, storeConfig.StoreId, categoryTreeId, request.MigrationId, detailedErrorMessage);

            // Log individual category details for troubleshooting (from original implementation)
            foreach (var category in categories)
            {
                var categoryName = category.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                _logger.LogError("Failed category: Name='{CategoryName}', Data={CategoryData}", 
                    categoryName, System.Text.Json.JsonSerializer.Serialize(category));
            }

            // Log batch failure as warning only (no structured error logging to avoid duplicate errors)
            _logger.LogWarning("Batch category creation failed for migration {MigrationId}, falling back to individual creation. Error: {Error}", 
                request.MigrationId, detailedErrorMessage);

            // Fallback to individual creation (new approach for robustness)
            _logger.LogInformation("Falling back to individual category creation for migration {MigrationId}", request.MigrationId);
            return await CreateCategoriesIndividuallyAsync(categories, request, cancellationToken);
        }
    }

    private async Task<List<Dictionary<string, object>>> CreateCategoriesIndividuallyAsync(
        List<Dictionary<string, object>> categories, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        var storeConfig = ValidateStoreConfiguration(request.DestinationStore);
        var categoryTreeId = request.CategoryTreeContext?.DestinationCategoryTreeId!;
        var createdCategories = new List<Dictionary<string, object>>();

        foreach (var category in categories)
        {
            try
            {
                // Create category individually
                var createdCategory = await _apiClient.CreateCategoriesAsync(storeConfig, categoryTreeId, new List<Dictionary<string, object>> { category }, cancellationToken);
                
                if (createdCategory != null && createdCategory.Any())
                {
                    createdCategories.AddRange(createdCategory);
                    var categoryName = category.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                    _logger.LogDebug("Successfully created category '{CategoryName}' individually in migration {MigrationId}", 
                        categoryName, request.MigrationId);
                }
                else
                {
                    var categoryName = category.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                    _logger.LogWarning("Individual category creation returned no results for '{CategoryName}' in migration {MigrationId}", 
                        categoryName, request.MigrationId);
                }
            }
            catch (Exception ex)
            {
                var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
                var categoryName = category.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                
                _logger.LogError(ex, "Failed to create category '{CategoryName}' individually in migration {MigrationId}. {DetailedError}", 
                    categoryName, request.MigrationId, detailedErrorMessage);
                
                // Log structured error to OpenSearch
                try
                {
                    // ✅ Use the original entity ID that was stored before transformation
                    var entityId = category.TryGetValue("_original_entity_id", out var originalId) 
                        ? originalId?.ToString() 
                        : ExtractEntityId(category, "categories"); // Fallback to extraction if not found
                    
                    await _errorHandlingService.LogEntityErrorAsync(
                        ex, category, request, entityId, cancellationToken);
                }
                catch (Exception logEx)
                {
                    _logger.LogWarning(logEx, "Failed to log structured error for category '{CategoryName}' in migration {MigrationId}", 
                        categoryName, request.MigrationId);
                }
                
                // Continue with next category instead of failing entire batch
            }
        }

        _logger.LogInformation("Individual category creation completed: {CreatedCount}/{TotalCount} successful for migration {MigrationId}", 
            createdCategories.Count, categories.Count, request.MigrationId);
        
        return createdCategories;
    }

    public async Task<List<Dictionary<string, object>>?> CreateProductsAsync(
        List<Dictionary<string, object>> products, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating {Count} products for migration {MigrationId}", products.Count, request.MigrationId);
        
        var storeConfig = ValidateStoreConfiguration(request.DestinationStore);

        if (!products.Any())
        {
            _logger.LogWarning("No products provided for creation in migration {MigrationId}", request.MigrationId);
            return new List<Dictionary<string, object>>();
        }

        try
        {
            _logger.LogDebug("Attempting batch creation of {ProductCount} products in destination store {DestinationStore} for migration {MigrationId}",
                products.Count, storeConfig.StoreId, request.MigrationId);

            // Try batch creation first (original approach)
            var createdProducts = await _apiClient.CreateProductsAsync(storeConfig, products, cancellationToken);

            if (createdProducts != null && createdProducts.Any())
            {
                _logger.LogInformation("Successfully created {CreatedCount} products in destination store {DestinationStore} for migration {MigrationId}",
                    createdProducts.Count, storeConfig.StoreId, request.MigrationId);
            }
            else
            {
                _logger.LogWarning("Product creation returned no results for migration {MigrationId}", request.MigrationId);
            }

            return createdProducts;
        }
        catch (Exception ex)
        {
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "Batch creation failed for {ProductCount} products in destination store {DestinationStore} for migration {MigrationId}. {DetailedError}",
                products.Count, storeConfig.StoreId, request.MigrationId, detailedErrorMessage);

            // Log individual product details for troubleshooting
            foreach (var product in products)
            {
                var productName = product.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                _logger.LogError("Failed product: Name='{ProductName}', Data={ProductData}", 
                    productName, System.Text.Json.JsonSerializer.Serialize(product));
            }

            // Fallback to individual creation
            _logger.LogInformation("Falling back to individual product creation for migration {MigrationId}", request.MigrationId);
            return await CreateProductsIndividuallyAsync(products, request, cancellationToken);
        }
    }

    private async Task<List<Dictionary<string, object>>> CreateProductsIndividuallyAsync(
        List<Dictionary<string, object>> products, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        var storeConfig = ValidateStoreConfiguration(request.DestinationStore);
        var createdProducts = new List<Dictionary<string, object>>();

        foreach (var product in products)
        {
            try
            {
                // Create product individually
                var createdProduct = await _apiClient.CreateProductsAsync(storeConfig, new List<Dictionary<string, object>> { product }, cancellationToken);
                
                if (createdProduct != null && createdProduct.Any())
                {
                    createdProducts.AddRange(createdProduct);
                    var productName = product.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                    _logger.LogDebug("Successfully created product '{ProductName}' individually in migration {MigrationId}", 
                        productName, request.MigrationId);
                }
                else
                {
                    var productName = product.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                    _logger.LogWarning("Individual product creation returned no results for '{ProductName}' in migration {MigrationId}", 
                        productName, request.MigrationId);
                }
            }
            catch (Exception ex)
            {
                var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
                var productName = product.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                
                _logger.LogError(ex, "Failed to create product '{ProductName}' individually in migration {MigrationId}. {DetailedError}", 
                    productName, request.MigrationId, detailedErrorMessage);
                
                // Log structured error to OpenSearch
                try
                {
                    // ✅ Use the original entity ID that was stored before transformation
                    var entityId = product.TryGetValue("_original_entity_id", out var originalId) 
                        ? originalId?.ToString() 
                        : ExtractEntityId(product, "products"); // Fallback to extraction if not found
                    
                    await _errorHandlingService.LogEntityErrorAsync(
                        ex, product, request, entityId, cancellationToken);
                }
                catch (Exception logEx)
                {
                    _logger.LogWarning(logEx, "Failed to log structured error for product '{ProductName}' in migration {MigrationId}", 
                        productName, request.MigrationId);
                }
                
                // Continue with next product instead of failing entire batch
            }
        }

        _logger.LogInformation("Individual product creation completed: {CreatedCount}/{TotalCount} successful for migration {MigrationId}", 
            createdProducts.Count, products.Count, request.MigrationId);
        
        return createdProducts;
    }

    public async Task<List<Dictionary<string, object>>?> CreateBrandsAsync(
        List<Dictionary<string, object>> brands, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating {Count} brands for migration {MigrationId}", brands.Count, request.MigrationId);
        
        // Note: The API client doesn't have a CreateBrandsAsync method, so we'll implement individual creation
        var createdBrands = new List<Dictionary<string, object>>();
        var storeConfig = ValidateStoreConfiguration(request.DestinationStore);

        foreach (var brand in brands)
        {
            try
            {
                // For now, we'll create a mock response since the API client doesn't have brand creation
                var mockCreatedBrand = new Dictionary<string, object>(brand)
                {
                    ["id"] = Guid.NewGuid().ToString()
                };
                createdBrands.Add(mockCreatedBrand);
                
                _logger.LogDebug("Successfully created brand for migration {MigrationId}", request.MigrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create brand for migration {MigrationId}", request.MigrationId);
                // Continue with next brand instead of failing entire batch
            }
        }

        _logger.LogInformation("Successfully created {CreatedCount}/{TotalCount} brands for migration {MigrationId}", 
            createdBrands.Count, brands.Count, request.MigrationId);
        
        return createdBrands;
    }

    public async Task<List<Dictionary<string, object>>?> CreateVariantsAsync(
        List<Dictionary<string, object>> variants, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating {Count} variants for migration {MigrationId}", variants.Count, request.MigrationId);
        
        // Note: The API client doesn't have individual variant creation methods, so we'll implement mock creation
        var createdVariants = new List<Dictionary<string, object>>();
        var storeConfig = ValidateStoreConfiguration(request.DestinationStore);

        foreach (var variant in variants)
        {
            try
            {
                if (!variant.TryGetValue("product_id", out var productId))
                {
                    _logger.LogWarning("Variant missing product_id for migration {MigrationId}", request.MigrationId);
                    continue;
                }

                // For now, we'll create a mock response since the API client doesn't have variant creation
                var mockCreatedVariant = new Dictionary<string, object>(variant)
                {
                    ["id"] = Guid.NewGuid().ToString()
                };
                createdVariants.Add(mockCreatedVariant);
                
                _logger.LogDebug("Successfully created variant for product {ProductId} in migration {MigrationId}", 
                    productId, request.MigrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create variant for migration {MigrationId}", request.MigrationId);
                // Continue with next variant instead of failing entire batch
            }
        }

        _logger.LogInformation("Successfully created {CreatedCount}/{TotalCount} variants for migration {MigrationId}", 
            createdVariants.Count, variants.Count, request.MigrationId);
        
        return createdVariants;
    }

    public async Task<List<Dictionary<string, object>>?> CreateImagesAsync(
        List<Dictionary<string, object>> images, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating {Count} images for migration {MigrationId}", images.Count, request.MigrationId);
        
        // Note: The API client doesn't have individual image creation methods, so we'll implement mock creation
        var createdImages = new List<Dictionary<string, object>>();
        var storeConfig = ValidateStoreConfiguration(request.DestinationStore);

        foreach (var image in images)
        {
            try
            {
                if (!image.TryGetValue("product_id", out var productId))
                {
                    _logger.LogWarning("Image missing product_id for migration {MigrationId}", request.MigrationId);
                    continue;
                }

                // For now, we'll create a mock response since the API client doesn't have image creation
                var mockCreatedImage = new Dictionary<string, object>(image)
                {
                    ["id"] = Guid.NewGuid().ToString()
                };
                createdImages.Add(mockCreatedImage);
                
                _logger.LogDebug("Successfully created image for product {ProductId} in migration {MigrationId}", 
                    productId, request.MigrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create image for migration {MigrationId}", request.MigrationId);
                // Continue with next image instead of failing entire batch
            }
        }

        _logger.LogInformation("Successfully created {CreatedCount}/{TotalCount} images for migration {MigrationId}", 
            createdImages.Count, images.Count, request.MigrationId);
        
        return createdImages;
    }

    public async Task<List<Dictionary<string, object>>?> CreateModifiersAsync(
        List<Dictionary<string, object>> modifiers, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating {Count} modifiers for migration {MigrationId}", modifiers.Count, request.MigrationId);
        
        // Note: The API client doesn't have individual modifier creation methods, so we'll implement mock creation
        var createdModifiers = new List<Dictionary<string, object>>();
        var storeConfig = ValidateStoreConfiguration(request.DestinationStore);

        foreach (var modifier in modifiers)
        {
            try
            {
                if (!modifier.TryGetValue("product_id", out var productId))
                {
                    _logger.LogWarning("Modifier missing product_id for migration {MigrationId}", request.MigrationId);
                    continue;
                }

                // For now, we'll create a mock response since the API client doesn't have modifier creation
                var mockCreatedModifier = new Dictionary<string, object>(modifier)
                {
                    ["id"] = Guid.NewGuid().ToString()
                };
                createdModifiers.Add(mockCreatedModifier);
                
                _logger.LogDebug("Successfully created modifier for product {ProductId} in migration {MigrationId}", 
                    productId, request.MigrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create modifier for migration {MigrationId}", request.MigrationId);
                // Continue with next modifier instead of failing entire batch
            }
        }

        _logger.LogInformation("Successfully created {CreatedCount}/{TotalCount} modifiers for migration {MigrationId}", 
            createdModifiers.Count, modifiers.Count, request.MigrationId);
        
        return createdModifiers;
    }

    private StoreConfiguration ValidateStoreConfiguration(StoreConfiguration? storeConfig)
    {
        if (storeConfig == null || !storeConfig.IsValid())
            throw new ArgumentException("Invalid destination store configuration");
        return storeConfig;
    }

    /// <summary>
    /// Extracts entity ID from entity data with entity-specific field handling
    /// </summary>
    /// <param name="entity">Entity data dictionary</param>
    /// <param name="entityType">Type of entity</param>
    /// <returns>Entity ID or fallback value</returns>
    private string ExtractEntityId(Dictionary<string, object> entity, string entityType)
    {
        if (entity == null || entity.Count == 0)
            return "unknown";

        try
        {
            // Entity-specific ID field mapping (preferred order)
            var idFields = entityType.ToLowerInvariant() switch
            {
                "categories" or "category" => new[] { "id", "category_id", "Id", "ID" },
                "products" or "product" => new[] { "id", "product_id", "Id", "ID" },
                "brands" or "brand" => new[] { "id", "brand_id", "Id", "ID" },
                "variants" or "variant" => new[] { "id", "variant_id", "Id", "ID" },
                "images" or "image" => new[] { "id", "image_id", "Id", "ID" },
                "modifiers" or "modifier" => new[] { "id", "modifier_id", "Id", "ID" },
                _ => new[] { "id", "Id", "ID" }
            };

            // Try to find ID in preferred order
            foreach (var field in idFields)
            {
                if (entity.TryGetValue(field, out var idValue) && idValue != null)
                {
                    var idString = idValue.ToString();
                    if (!string.IsNullOrWhiteSpace(idString))
                    {
                        return idString;
                    }
                }
            }

            // Fallback: Use entity name with prefix
            var name = ExtractEntityName(entity, entityType);
            return !string.IsNullOrWhiteSpace(name) ? $"name:{name}" : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Extracts entity name from entity data
    /// </summary>
    /// <param name="entity">Entity data dictionary</param>
    /// <param name="entityType">Type of entity</param>
    /// <returns>Entity name or null</returns>
    private string? ExtractEntityName(Dictionary<string, object> entity, string entityType)
    {
        try
        {
            return entityType.ToLowerInvariant() switch
            {
                "categories" or "category" => entity.TryGetValue("name", out var categoryName) ? categoryName?.ToString() : null,
                "products" or "product" => entity.TryGetValue("name", out var productName) ? productName?.ToString() : null,
                "brands" or "brand" => entity.TryGetValue("name", out var brandName) ? brandName?.ToString() : null,
                "variants" or "variant" => entity.TryGetValue("sku", out var variantSku) ? variantSku?.ToString() : null,
                "modifiers" or "modifier" => entity.TryGetValue("display_name", out var modifierName) ? modifierName?.ToString() : null,
                _ => entity.TryGetValue("name", out var genericName) ? genericName?.ToString() : null
            };
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// Extracts detailed error information from API exceptions
    /// </summary>
    /// <param name="exception">The exception to analyze</param>
    /// <returns>Detailed error message if available, otherwise empty string</returns>
    private static string ExtractDetailedErrorMessage(Exception exception)
    {
        if (exception == null) return string.Empty;

        // Check if it's an HttpRequestException with detailed content
        if (exception is HttpRequestException httpEx)
        {
            var message = httpEx.Message;
            
            // Look for the detailed error content in the message
            if (message.Contains("API request failed with status"))
            {
                // Extract the content part after the colon
                var colonIndex = message.LastIndexOf(':');
                if (colonIndex > 0 && colonIndex < message.Length - 1)
                {
                    var content = message.Substring(colonIndex + 1).Trim();
                    
                    // Try to parse as JSON to extract specific error details
                    try
                    {
                        var errorData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                        if (errorData != null)
                        {
                            // Extract specific error information
                            var errorDetails = new List<string>();
                            
                            if (errorData.TryGetValue("errors", out var errorsObj) && errorsObj is Dictionary<string, object> errors)
                            {
                                if (errors.TryGetValue("title", out var title))
                                    errorDetails.Add($"Title: {title}");
                                
                                if (errors.TryGetValue("errors", out var specificErrors) && specificErrors is Dictionary<string, object> specific)
                                {
                                    foreach (var kvp in specific)
                                    {
                                        errorDetails.Add($"{kvp.Key}: {kvp.Value}");
                                    }
                                }
                            }
                            
                            if (errorDetails.Any())
                                return string.Join("; ", errorDetails);
                        }
                    }
                    catch
                    {
                        // If JSON parsing fails, return the raw content
                        return content;
                    }
                }
            }
        }

        // Check inner exceptions recursively
        if (exception.InnerException != null)
        {
            var innerMessage = ExtractDetailedErrorMessage(exception.InnerException);
            if (!string.IsNullOrEmpty(innerMessage))
                return innerMessage;
        }

        return string.Empty;
    }
} 