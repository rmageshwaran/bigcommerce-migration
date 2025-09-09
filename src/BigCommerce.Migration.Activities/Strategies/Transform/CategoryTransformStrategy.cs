using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Strategies.Transform;

/// <summary>
/// Transform strategy for category entities
/// Handles category tree mapping, meta_keywords parsing, URL generation, and required field validation
/// </summary>
public class CategoryTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<CategoryTransformStrategy> _logger;
    private readonly IEntityMappingService _entityMappingService;

    public string EntityType => "categories";

    public CategoryTransformStrategy(
        ILogger<CategoryTransformStrategy> logger,
        IEntityMappingService entityMappingService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityMappingService = entityMappingService ?? throw new ArgumentNullException(nameof(entityMappingService));
    }

    public async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> entity,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        var entityId = entity.GetValueOrDefault("category_id")?.ToString() ?? "unknown";
        var entityName = entity.GetValueOrDefault("name")?.ToString() ?? "unknown";

        _logger.LogDebug("🔧 [TRANSFORM] ⭐ STARTING: Transforming category {EntityId} ('{EntityName}') for migration {MigrationId}",
            entityId, entityName, migrationId);

        if (categoryTreeContext == null)
        {
            _logger.LogError("🚨 [TRANSFORM] ❌ CRITICAL: CategoryTreeContext is NULL for category {EntityId} in migration {MigrationId}",
                entityId, migrationId);
            throw new InvalidOperationException("CategoryTreeContext is required for category transformation.");
        }

        var transformed = new Dictionary<string, object>();

        CopyValidBigCommerceFields(entity, transformed, migrationId);
        HandleCategoryTreeMapping(transformed, categoryTreeContext, entity);
        HandleMetaKeywords(transformed, migrationId);
        HandleCommonFields(transformed, migrationId);
        EnsureRequiredFields(transformed, migrationId);
        EnsureCustomUrlStructure(transformed, categoryTreeContext, migrationId, entity);
        await HandleParentIdConversionAsync(transformed, migrationId, cancellationToken);
        RemoveInvalidFields(transformed, migrationId);

        _logger.LogDebug("Transformed category '{CategoryName}' for migration {MigrationId}",
            transformed.GetValueOrDefault("name"), migrationId);

        return transformed;
    }

    /// <summary>
    /// Copies only valid BigCommerce category fields from source to destination
    /// </summary>
    private void CopyValidBigCommerceFields(Dictionary<string, object> source, Dictionary<string, object> destination, string migrationId)
    {
        // Valid BigCommerce V3 Category fields based on API documentation
        var validFields = new HashSet<string>
        {
            // Core fields
            "name", "parent_id", "is_visible", "sort_order", "tree_id",
            
            // Content fields  
            "description", "page_title", "meta_keywords", "meta_description",
            "search_keywords", "layout_file",
            
            // Display fields
            "image_url", "views", "default_product_sort",
            
            // URL field will be handled separately in EnsureCustomUrlStructure
            "url"
        };

        foreach (var field in validFields)
        {
            if (source.ContainsKey(field) && source[field] != null)
            {
                destination[field] = source[field];
            }
        }

        _logger.LogDebug("Copied {Count} valid BigCommerce fields for migration {MigrationId}",
            destination.Count, migrationId);
    }

    /// <summary>
    /// Removes invalid fields that shouldn't be sent to BigCommerce API
    /// </summary>
    private void RemoveInvalidFields(Dictionary<string, object> transformed, string migrationId)
    {
        // Fields that should not be sent to BigCommerce Create API
        var invalidFields = new HashSet<string>
        {
            // Source system hierarchical fields
            "depth", "path", "children",
            
            // Internal tracking fields
            "_original_entity_id", "original_id", "source_id", "_sourceTreeId",
            
            // BigCommerce read-only fields that shouldn't be in create requests
            "id", "category_id", "category_uuid", "date_created", "date_modified"
        };

        var removedFields = new List<string>();
        foreach (var invalidField in invalidFields)
        {
            if (transformed.Remove(invalidField))
            {
                removedFields.Add(invalidField);
            }
        }

        if (removedFields.Any())
        {
            _logger.LogDebug("Removed invalid fields: {InvalidFields} for migration {MigrationId}",
                string.Join(", ", removedFields), migrationId);
        }
    }

    private void HandleCategoryTreeMapping(Dictionary<string, object> transformed, CategoryTreeContext categoryTreeContext, Dictionary<string, object> sourceEntity)
    {
        if (sourceEntity.TryGetValue("_sourceTreeId", out var sourceTreeIdObj))
        {
            string sourceTreeId = sourceTreeIdObj?.ToString() ?? string.Empty;

            if (categoryTreeContext.CategoryTreeIdMapping.TryGetValue(sourceTreeId, out var destinationTreeId))
            {
                if (int.TryParse(destinationTreeId, out var treeIdInt))
                {
                    transformed["tree_id"] = treeIdInt;
                    _logger.LogDebug("Mapped source tree {SourceTreeId} to destination tree {DestinationTreeId}", sourceTreeId, destinationTreeId);
                }
                else
                {
                    _logger.LogWarning("Could not parse destination tree ID '{DestinationTreeId}' for source tree {SourceTreeId}", destinationTreeId, sourceTreeId);
                }
            }
            else
            {
                _logger.LogWarning("No destination tree mapping found for source tree {SourceTreeId}", sourceTreeId);
            }
        }
        else
        {
            _logger.LogWarning("Category is missing the '_sourceTreeId' tag. Cannot determine destination tree.");
        }
    }

    private void HandleMetaKeywords(Dictionary<string, object> transformed, string migrationId)
    {
        if (transformed.TryGetValue("meta_keywords", out var metaKeywords))
        {
            if (metaKeywords is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    string metaKeywordsString = metaKeywords?.ToString() ?? string.Empty;

                    try
                    {
                        // Handle common malformed cases
                        if (string.IsNullOrWhiteSpace(metaKeywordsString) ||
                            metaKeywordsString == "[]" ||
                            metaKeywordsString == "[\"\"]" ||
                            metaKeywordsString == "[\"[]\"]")
                        {
                            transformed["meta_keywords"] = new List<string>();
                            _logger.LogDebug("Converted empty/malformed meta_keywords to empty array for migration {MigrationId}", migrationId);
                        }
                        else
                        {
                            // Try to parse as JSON array
                            var parsedKeywords = JsonSerializer.Deserialize<List<string>>(metaKeywordsString);
                            transformed["meta_keywords"] = parsedKeywords ?? new List<string>();
                            _logger.LogDebug("Successfully parsed meta_keywords JSON for migration {MigrationId}", migrationId);
                        }
                    }
                    catch (JsonException ex)
                    {
                        // If parsing fails, try to split as comma-separated values
                        var keywords = metaKeywordsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(k => k.Trim())
                            .Where(k => !string.IsNullOrWhiteSpace(k))
                            .ToList();

                        transformed["meta_keywords"] = keywords;
                        _logger.LogWarning(ex, "Failed to parse meta_keywords as JSON, split as CSV instead for migration {MigrationId}", migrationId);
                    }
                }
            }
            else if (metaKeywords is not List<string> && metaKeywords is not string[])
            {
                transformed["meta_keywords"] = new List<string>();
                _logger.LogWarning("Invalid meta_keywords type, converted to empty array for migration {MigrationId}", migrationId);
            }
        }
    }

    private void HandleCommonFields(Dictionary<string, object> transformed, string migrationId)
    {
        // Handle page_title - use name if not present
        if (!transformed.ContainsKey("page_title") && transformed.ContainsKey("name"))
        {
            transformed["page_title"] = transformed["name"];
            _logger.LogDebug("Set page_title from category name for migration {MigrationId}", migrationId);
        }

        // Ensure views field exists (default to 0 if not present)
        if (!transformed.ContainsKey("views"))
        {
            transformed["views"] = 0;
            _logger.LogDebug("Set default views 0 for migration {MigrationId}", migrationId);
        }

        // Handle layout_file - set default if not present
        if (!transformed.ContainsKey("layout_file"))
        {
            transformed["layout_file"] = "category.html";
            _logger.LogDebug("Set default layout_file to category.html for migration {MigrationId}", migrationId);
        }

        // Ensure meta_description is string (not null)
        if (!transformed.ContainsKey("meta_description"))
        {
            transformed["meta_description"] = "";
            _logger.LogDebug("Set empty meta_description for migration {MigrationId}", migrationId);
        }

        // Ensure search_keywords is string (not null)
        if (!transformed.ContainsKey("search_keywords"))
        {
            transformed["search_keywords"] = "";
            _logger.LogDebug("Set empty search_keywords for migration {MigrationId}", migrationId);
        }
    }

    private void EnsureRequiredFields(Dictionary<string, object> transformed, string migrationId)
    {
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Category";
            _logger.LogWarning("Category missing name field, using default for migration {MigrationId}", migrationId);
        }

        // Ensure parent_id is set (required field)
        if (!transformed.ContainsKey("parent_id"))
        {
            transformed["parent_id"] = 0;
            _logger.LogDebug("Set default parent_id 0 for root category in migration {MigrationId}", migrationId);
        }

        // Ensure is_visible is set (common field)
        if (!transformed.ContainsKey("is_visible"))
        {
            transformed["is_visible"] = true;
            _logger.LogDebug("Set default is_visible true for migration {MigrationId}", migrationId);
        }

        // Ensure sort_order is set
        if (!transformed.ContainsKey("sort_order"))
        {
            transformed["sort_order"] = 0;
            _logger.LogDebug("Set default sort_order 0 for migration {MigrationId}", migrationId);
        }

        // Set default_product_sort if not present
        if (!transformed.ContainsKey("default_product_sort"))
        {
            transformed["default_product_sort"] = "use_store_settings";
            _logger.LogDebug("Set default_product_sort to use_store_settings for migration {MigrationId}", migrationId);
        }
    }

    private void EnsureCustomUrlStructure(Dictionary<string, object> transformed, CategoryTreeContext categoryTreeContext, string migrationId, Dictionary<string, object> sourceEntity)
    {
        // BigCommerce V3 API expects 'url' structure with 'path' field as an object
        string? existingUrlPath = null;

        // Check if there's already a URL field and extract the path
        if (transformed.ContainsKey("url"))
        {
            var existingUrl = transformed["url"];
            if (existingUrl is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    var urlString = jsonElement.GetString();
                    if (!string.IsNullOrWhiteSpace(urlString))
                    {
                        if (urlString.TrimStart().StartsWith("{")) // JSON inside a string
                        {
                            using var doc = JsonDocument.Parse(urlString);
                            if (doc.RootElement.TryGetProperty("path", out var pathElement))
                            {
                                existingUrlPath = pathElement.GetString();
                                _logger.LogDebug("Found existing string URL '{ExistingUrl}' - converting to object structure for migration {MigrationId}",
                                urlString, migrationId);
                            }
                        }
                        else
                        {
                            // plain string URL
                            existingUrlPath = urlString;
                        }
                    }
                }
            }
            else if (existingUrl is Dictionary<string, object> existingUrlDict &&
                     existingUrlDict.TryGetValue("path", out var pathValue) &&
                     !string.IsNullOrWhiteSpace(pathValue?.ToString()))
            {
                existingUrlPath = pathValue.ToString();
                _logger.LogDebug("Found existing URL object with path '{ExistingPath}' for migration {MigrationId}",
                    existingUrlPath, migrationId);
            }
        }

        // Always create a new URL object structure (required by BigCommerce V3)
        var urlStructure = new Dictionary<string, object>();

        // Set the path field
        if (!string.IsNullOrWhiteSpace(existingUrlPath))
        {
            // Use existing path, ensure it has proper format
            var cleanPath = existingUrlPath.Trim();
            if (!cleanPath.StartsWith("/")) cleanPath = "/" + cleanPath;
            if (!cleanPath.EndsWith("/")) cleanPath = cleanPath + "/";
            urlStructure["path"] = cleanPath;
            _logger.LogDebug("Using existing URL path '{UrlPath}' for migration {MigrationId}", cleanPath, migrationId);
        }
        else
        {
            // Generate URL path from category name
            var categoryName = transformed["name"]?.ToString() ?? "category";
            var urlPath = GenerateUrlPath(categoryName);
            urlStructure["path"] = $"/{urlPath}/";
            _logger.LogDebug("Generated URL path '{UrlPath}' from category name '{CategoryName}' for migration {MigrationId}",
                urlPath, categoryName, migrationId);
        }

        // Always set is_customized to false (matches BigCommerce API documentation)
        urlStructure["is_customized"] = false;

        // Replace the url field with the proper object structure
        transformed["url"] = urlStructure;

        _logger.LogDebug("Created URL object structure with path '{UrlPath}' for migration {MigrationId}",
            urlStructure["path"], migrationId);
    }

    private async Task HandleParentIdConversionAsync(Dictionary<string, object> transformed, string migrationId, CancellationToken cancellationToken)
    {
        if (transformed.TryGetValue("parent_id", out var parentIdObj))
        {
            _logger.LogDebug("🔍 DEBUG: HandleParentIdConversionAsync - processing parent_id: {ParentId} (type: {ParentIdType}) for migration {MigrationId}",
                parentIdObj, parentIdObj?.GetType().Name, migrationId);

            if (parentIdObj is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Number)
                {
                    string parentIdString = parentIdObj?.ToString() ?? null!;

                    _logger.LogDebug("🔍 DEBUG: Looking up mapping for parent_id string '{ParentIdString}' in migration {MigrationId}",
                    parentIdObj, migrationId);

                    var mappedParentId = await _entityMappingService.GetDestinationIdAsync(
                        migrationId,
                        "categories",
                        parentIdString,
                        cancellationToken);

                    _logger.LogDebug("🔍 DEBUG: Mapping lookup result for parent_id '{ParentIdString}': '{MappedParentId}' in migration {MigrationId}",
                        parentIdObj, mappedParentId ?? "NULL", migrationId);

                    if (!string.IsNullOrEmpty(mappedParentId) && long.TryParse(mappedParentId, out long parsedMappedId))
                    {
                        transformed["parent_id"] = parsedMappedId;
                        _logger.LogDebug("🔍 DEBUG: ✅ Successfully mapped parent_id {ParentIdString} to {MappedParentId} for migration {MigrationId}",
                            parentIdObj, parsedMappedId, migrationId);
                    }
                    else if (long.TryParse(parentIdString, out var parsedOriginal))
                    {
                        transformed["parent_id"] = parsedOriginal;
                        _logger.LogDebug("No mapping found for parent_id {ParentIdString}, using original value {ParsedOriginal} for migration {MigrationId}",
                            parentIdObj, parsedOriginal, migrationId);
                    }
                    else
                    {
                        transformed["parent_id"] = 0;
                        _logger.LogDebug("Parent_id {ParentIdString} not found in mapping and cannot be parsed, treating as root category for migration {MigrationId}",
                            parentIdObj, migrationId);
                    }
                }
            }
            else if (parentIdObj is int parentIdInt)
            {
                _logger.LogDebug("🔍 DEBUG: Looking up mapping for parent_id int '{ParentIdInt}' in migration {MigrationId}",
                    parentIdInt, migrationId);

                // Try to map the integer parent_id to destination store
                var mappedParentId = await _entityMappingService.GetDestinationIdAsync(
                    migrationId,
                    "categories",
                    parentIdInt.ToString(),
                    cancellationToken);

                _logger.LogDebug("🔍 DEBUG: Mapping lookup result for parent_id '{ParentIdInt}': '{MappedParentId}' in migration {MigrationId}",
                    parentIdInt, mappedParentId ?? "NULL", migrationId);

                if (!string.IsNullOrEmpty(mappedParentId) && int.TryParse(mappedParentId, out var parsedMappedId))
                {
                    transformed["parent_id"] = parsedMappedId;
                    _logger.LogDebug("🔍 DEBUG: ✅ Successfully mapped parent_id {ParentIdInt} to {MappedParentId} for migration {MigrationId}",
                        parentIdInt, parsedMappedId, migrationId);
                }
                else
                {
                    // Keep original value if no mapping found
                    _logger.LogDebug("No mapping found for parent_id {ParentIdInt}, using original value for migration {MigrationId}",
                        parentIdInt, migrationId);
                }
            }
        }
        else
        {
            transformed["parent_id"] = 0;
            _logger.LogDebug("🔍 DEBUG: No parent_id found in entity, setting to 0 (root category) for migration {MigrationId}", migrationId);
        }
    }

    private static string GenerateUrlPath(string categoryName)
    {
        // Simple URL-friendly path generation using string operations only
        var result = categoryName.ToLowerInvariant();

        // Replace spaces with dashes
        result = result.Replace(" ", "-");

        // Replace common characters
        result = result.Replace("&", "and");
        result = result.Replace("+", "plus");
        result = result.Replace("@", "at");

        // Remove problematic characters by using string operations
        var charsToRemove = new string[] { "'", "\"", "(", ")", ",", ".", ":", ";", "?", "!", "#", "$", "%", "^", "*", "=", "[", "]", "{", "}", "|", "\\", "/", "<", ">", "~", "`" };
        foreach (var charStr in charsToRemove)
        {
            result = result.Replace(charStr, "");
        }

        // Clean up multiple dashes
        while (result.Contains("--"))
        {
            result = result.Replace("--", "-");
        }

        // Trim dashes from start and end
        result = result.Trim('-');

        return result;
    }
}
