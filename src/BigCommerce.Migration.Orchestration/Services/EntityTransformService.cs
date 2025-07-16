using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Services;

/// <summary>
/// Implementation of IEntityTransformService for transforming entities between source and destination formats
/// </summary>
public class EntityTransformService : IEntityTransformService
{
    private readonly ILogger<EntityTransformService> _logger;

    public EntityTransformService(ILogger<EntityTransformService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> sourceEntity, 
        BatchProcessingRequest request)
    {
        if (sourceEntity == null)
            throw new ArgumentNullException(nameof(sourceEntity));
        
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        _logger.LogDebug("Starting transformation for {EntityType} in migration {MigrationId}", 
            request.EntityType, request.MigrationId);

        try
        {
            // Create a copy of the entity for transformation
            var transformedEntity = new Dictionary<string, object>(sourceEntity);

            // Remove source-specific fields that shouldn't be migrated
            transformedEntity.Remove("id");
            transformedEntity.Remove("date_created");
            transformedEntity.Remove("date_modified");
            transformedEntity.Remove("_links"); // Remove BigCommerce API links
            transformedEntity.Remove("_meta"); // Remove BigCommerce API metadata
            transformedEntity.Remove("_original_entity_id"); // Remove internal field used for error logging

            // Apply entity-specific transformations
            switch (request.EntityType.ToLowerInvariant())
            {
                case "categories":
                    return await TransformCategoryAsync(transformedEntity, request);
                case "products":
                    return await TransformProductAsync(transformedEntity, request);
                case "brands":
                    return await TransformBrandAsync(transformedEntity, request);
                case "variants":
                    return await TransformVariantAsync(transformedEntity, request);
                case "images":
                    return await TransformImageAsync(transformedEntity, request);
                case "modifiers":
                    return await TransformModifierAsync(transformedEntity, request);
                default:
                    _logger.LogWarning("Unknown entity type {EntityType} for migration {MigrationId}, returning untransformed entity", 
                        request.EntityType, request.MigrationId);
                    return transformedEntity;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transform {EntityType} entity for migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            throw new InvalidOperationException($"Entity transformation failed for {request.EntityType}", ex);
        }
    }

    public async Task<Dictionary<string, object>> TransformCategoryAsync(
        Dictionary<string, object> category, 
        BatchProcessingRequest request)
    {
        _logger.LogDebug("Transforming category for migration {MigrationId}", request.MigrationId);
        
        var transformed = new Dictionary<string, object>(category);

        // Handle category tree mapping if context is available
        if (request.CategoryTreeContext != null)
        {
            if (transformed.TryGetValue("category_tree_id", out var sourceTreeId))
            {
                // For now, we'll use the destination category tree ID directly
                // In a full implementation, you'd have a mapping service to handle this
                if (!string.IsNullOrWhiteSpace(request.CategoryTreeContext.DestinationCategoryTreeId))
                {
                    transformed["category_tree_id"] = request.CategoryTreeContext.DestinationCategoryTreeId;
                    _logger.LogDebug("Mapped category tree ID from {SourceTreeId} to {DestinationTreeId} for migration {MigrationId}", 
                        sourceTreeId, request.CategoryTreeContext.DestinationCategoryTreeId, request.MigrationId);
                }
                else
                {
                    _logger.LogWarning("No destination category tree ID found for migration {MigrationId}", 
                        request.MigrationId);
                }
            }
        }

        // Fix meta_keywords field - BigCommerce expects an array, not a malformed string
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
                        // Empty or malformed, set to empty array
                        transformed["meta_keywords"] = new List<string>();
                        _logger.LogDebug("Converted empty/malformed meta_keywords to empty array for migration {MigrationId}", request.MigrationId);
                    }
                    else
                    {
                        // Try to parse as JSON array
                        var parsedKeywords = System.Text.Json.JsonSerializer.Deserialize<List<string>>(metaKeywordsString);
                        transformed["meta_keywords"] = parsedKeywords ?? new List<string>();
                        _logger.LogDebug("Successfully parsed meta_keywords JSON for migration {MigrationId}", request.MigrationId);
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // If parsing fails, try to split as comma-separated values
                    var keywords = metaKeywordsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim().Trim('"', '[', ']', '\\'))
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .ToList();
                    
                    transformed["meta_keywords"] = keywords;
                    _logger.LogWarning(ex, "Failed to parse meta_keywords as JSON, split as CSV instead for migration {MigrationId}. Original: {MetaKeywords}", 
                        request.MigrationId, metaKeywordsString);
                }
            }
            else if (metaKeywords is not List<string> && metaKeywords is not string[])
            {
                // If it's not already a proper array, convert to empty array
                transformed["meta_keywords"] = new List<string>();
                _logger.LogWarning("Invalid meta_keywords type, converted to empty array for migration {MigrationId}", request.MigrationId);
            }
        }

        // Ensure required BigCommerce category fields are present
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Category";
            _logger.LogWarning("Category missing name field, using default for migration {MigrationId}", request.MigrationId);
        }

        // Handle parent_id - ensure it's properly set for root categories (must be int or null)
        if (!transformed.ContainsKey("parent_id") || transformed["parent_id"] == null || 
            (transformed["parent_id"] is int parentIdInt && parentIdInt == 0))
        {
            transformed["parent_id"] = null; // Root category
            _logger.LogDebug("Setting parent_id to null for root category in migration {MigrationId}", request.MigrationId);
        }
        else if (transformed["parent_id"] is string parentIdString)
        {
            // Convert string parent_id to int if it's a string
            if (int.TryParse(parentIdString, out var parentId))
            {
                transformed["parent_id"] = parentId;
                _logger.LogDebug("Converted parent_id from string '{StringParentId}' to int {ParentId} for category in migration {MigrationId}", 
                    parentIdString, parentId, request.MigrationId);
            }
            else
            {
                transformed["parent_id"] = null; // Invalid string, treat as root category
                _logger.LogWarning("Failed to parse parent_id string '{ParentId}', treating as root category for migration {MigrationId}", 
                    parentIdString, request.MigrationId);
            }
        }

        // Ensure tree_id is set (required by BigCommerce API) - must be a number
        if (!transformed.ContainsKey("tree_id"))
        {
            if (request.CategoryTreeContext?.DestinationCategoryTreeId != null)
            {
                // Try to parse as int, fallback to 1 if parsing fails
                if (int.TryParse(request.CategoryTreeContext.DestinationCategoryTreeId, out var treeId))
                {
                    transformed["tree_id"] = treeId;
                    _logger.LogDebug("Setting tree_id to {TreeId} for category in migration {MigrationId}", 
                        treeId, request.MigrationId);
                }
                else
                {
                    transformed["tree_id"] = 1; // Default tree ID as number
                    _logger.LogWarning("Failed to parse destination category tree ID '{TreeId}', using default tree_id=1 for migration {MigrationId}", 
                        request.CategoryTreeContext.DestinationCategoryTreeId, request.MigrationId);
                }
            }
            else
            {
                transformed["tree_id"] = 1; // Default tree ID as number
                _logger.LogWarning("No destination category tree ID available, using default tree_id=1 for migration {MigrationId}", request.MigrationId);
            }
        }
        else if (transformed["tree_id"] is string treeIdString)
        {
            // Convert string tree_id to int if it's a string
            if (int.TryParse(treeIdString, out var treeId))
            {
                transformed["tree_id"] = treeId;
                _logger.LogDebug("Converted tree_id from string '{StringTreeId}' to int {TreeId} for category in migration {MigrationId}", 
                    treeIdString, treeId, request.MigrationId);
            }
            else
            {
                transformed["tree_id"] = 1; // Default tree ID as number
                _logger.LogWarning("Failed to parse tree_id string '{TreeId}', using default tree_id=1 for migration {MigrationId}", 
                    treeIdString, request.MigrationId);
            }
        }

        // Ensure url.path is present (required by BigCommerce API)
        if (!transformed.ContainsKey("url") || transformed["url"] == null)
        {
            var categoryName = transformed.TryGetValue("name", out var name) ? name.ToString() : "category";
            var urlPath = GenerateUrlPath(categoryName);
            transformed["url"] = new Dictionary<string, object>
            {
                ["path"] = urlPath
            };
            _logger.LogDebug("Generated url.path '{UrlPath}' for category '{CategoryName}' in migration {MigrationId}", 
                urlPath, categoryName, request.MigrationId);
        }
        else if (transformed["url"] is Dictionary<string, object> urlDict)
        {
            if (!urlDict.ContainsKey("path"))
            {
                var categoryName = transformed.TryGetValue("name", out var name) ? name.ToString() : "category";
                var urlPath = GenerateUrlPath(categoryName);
                urlDict["path"] = urlPath;
                _logger.LogDebug("Added missing url.path '{UrlPath}' for category '{CategoryName}' in migration {MigrationId}", 
                    urlPath, categoryName, request.MigrationId);
            }
            else
            {
                _logger.LogDebug("URL path already exists for category in migration {MigrationId}", request.MigrationId);
            }
        }
        else
        {
            // URL exists but is not a dictionary, replace it with proper structure
            var categoryName = transformed.TryGetValue("name", out var name) ? name.ToString() : "category";
            var urlPath = GenerateUrlPath(categoryName);
            transformed["url"] = new Dictionary<string, object>
            {
                ["path"] = urlPath
            };
            _logger.LogWarning("Replaced invalid URL structure with proper url.path '{UrlPath}' for category '{CategoryName}' in migration {MigrationId}", 
                urlPath, categoryName, request.MigrationId);
        }

        // Final validation: ensure url.path exists
        if (transformed.ContainsKey("url") && transformed["url"] is Dictionary<string, object> finalUrlDict)
        {
            if (!finalUrlDict.ContainsKey("path"))
            {
                var categoryName = transformed.TryGetValue("name", out var name) ? name.ToString() : "category";
                var urlPath = GenerateUrlPath(categoryName);
                finalUrlDict["path"] = urlPath;
                _logger.LogWarning("Final validation: Added missing url.path '{UrlPath}' for category '{CategoryName}' in migration {MigrationId}", 
                    urlPath, categoryName, request.MigrationId);
            }
        }

        // Remove any null values that might cause API issues, but preserve required fields
        var keysToRemove = transformed.Where(kvp => kvp.Value == null && 
            !IsRequiredCategoryField(kvp.Key)).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        return transformed;
    }

    public async Task<Dictionary<string, object>> TransformProductAsync(
        Dictionary<string, object> product, 
        BatchProcessingRequest request)
    {
        _logger.LogDebug("Transforming product for migration {MigrationId}", request.MigrationId);
        
        var transformed = new Dictionary<string, object>(product);

        // Handle category mapping if context is available
        if (request.CategoryTreeContext != null)
        {
            if (transformed.TryGetValue("categories", out var categories) && categories is List<object> categoryList)
            {
                var mappedCategories = new List<object>();
                foreach (var category in categoryList)
                {
                    // For now, we'll keep the original category IDs
                    // In a full implementation, you'd have a mapping service to handle this
                    mappedCategories.Add(category);
                    _logger.LogDebug("Keeping original category {CategoryId} for product in migration {MigrationId}", 
                        category, request.MigrationId);
                }
                transformed["categories"] = mappedCategories;
            }
        }

        // Handle brand mapping if available
        // Note: BrandMapping property doesn't exist in BatchProcessingRequest, so we'll skip this for now
        // In a full implementation, you'd have a mapping service to handle this

        // Ensure required fields
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Product";
            _logger.LogWarning("Product missing name field, using default for migration {MigrationId}", request.MigrationId);
        }

        if (!transformed.ContainsKey("type"))
        {
            transformed["type"] = "physical";
        }

        // Remove null values
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        return transformed;
    }

    public async Task<Dictionary<string, object>> TransformBrandAsync(
        Dictionary<string, object> brand, 
        BatchProcessingRequest request)
    {
        _logger.LogDebug("Transforming brand for migration {MigrationId}", request.MigrationId);
        
        var transformed = new Dictionary<string, object>(brand);

        // Ensure required fields
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Brand";
            _logger.LogWarning("Brand missing name field, using default for migration {MigrationId}", request.MigrationId);
        }

        // Remove null values
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        return transformed;
    }

    public async Task<Dictionary<string, object>> TransformVariantAsync(
        Dictionary<string, object> variant, 
        BatchProcessingRequest request)
    {
        _logger.LogDebug("Transforming variant for migration {MigrationId}", request.MigrationId);
        
        var transformed = new Dictionary<string, object>(variant);

        // Handle product mapping if available
        // Note: ProductMapping property doesn't exist in BatchProcessingRequest, so we'll skip this for now
        // In a full implementation, you'd have a mapping service to handle this

        // Ensure required fields
        if (!transformed.ContainsKey("sku"))
        {
            transformed["sku"] = $"SKU-{Guid.NewGuid():N}";
            _logger.LogWarning("Variant missing SKU, generating default for migration {MigrationId}", request.MigrationId);
        }

        // Remove null values
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        return transformed;
    }

    public async Task<Dictionary<string, object>> TransformImageAsync(
        Dictionary<string, object> image, 
        BatchProcessingRequest request)
    {
        _logger.LogDebug("Transforming image for migration {MigrationId}", request.MigrationId);
        
        var transformed = new Dictionary<string, object>(image);

        // Handle product mapping if available
        // Note: ProductMapping property doesn't exist in BatchProcessingRequest, so we'll skip this for now
        // In a full implementation, you'd have a mapping service to handle this

        // Ensure required fields
        if (!transformed.ContainsKey("image_url"))
        {
            _logger.LogWarning("Image missing URL field for migration {MigrationId}", request.MigrationId);
        }

        // Remove null values
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        return transformed;
    }

    public async Task<Dictionary<string, object>> TransformModifierAsync(
        Dictionary<string, object> modifier, 
        BatchProcessingRequest request)
    {
        _logger.LogDebug("Transforming modifier for migration {MigrationId}", request.MigrationId);
        
        var transformed = new Dictionary<string, object>(modifier);

        // Handle product mapping if available
        // Note: ProductMapping property doesn't exist in BatchProcessingRequest, so we'll skip this for now
        // In a full implementation, you'd have a mapping service to handle this

        // Ensure required fields
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Modifier";
            _logger.LogWarning("Modifier missing name field, using default for migration {MigrationId}", request.MigrationId);
        }

        if (!transformed.ContainsKey("type"))
        {
            transformed["type"] = "radio_button";
        }

        // Remove null values
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
        }

        return transformed;
    }

    /// <summary>
    /// Determines if a field is required for BigCommerce categories and should not be removed even if null
    /// </summary>
    /// <param name="fieldName">Field name to check</param>
    /// <returns>True if the field is required and should be preserved</returns>
    private static bool IsRequiredCategoryField(string fieldName)
    {
        return fieldName switch
        {
            "parent_id" => true, // Required field, can be null for root categories
            "tree_id" => true,   // Required field
            "name" => true,      // Required field
            "url" => true,       // Required field
            _ => false
        };
    }

    /// <summary>
    /// Generates a URL-friendly path from a category name
    /// </summary>
    /// <param name="categoryName">Category name</param>
    /// <returns>URL-friendly path</returns>
    private static string GenerateUrlPath(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return "category";

        // Convert to lowercase and replace spaces/special characters with hyphens
        var urlPath = System.Text.RegularExpressions.Regex.Replace(
            categoryName.ToLowerInvariant().Trim(),
            @"[^a-z0-9\s-]", ""); // Remove special characters except spaces and hyphens
        
        urlPath = System.Text.RegularExpressions.Regex.Replace(urlPath, @"\s+", "-"); // Replace spaces with hyphens
        urlPath = System.Text.RegularExpressions.Regex.Replace(urlPath, @"-+", "-"); // Replace multiple hyphens with single
        
        // Remove leading/trailing hyphens
        urlPath = urlPath.Trim('-');
        
        // Ensure we have a valid path
        if (string.IsNullOrWhiteSpace(urlPath))
            urlPath = "category";
        
        return urlPath;
    }
} 