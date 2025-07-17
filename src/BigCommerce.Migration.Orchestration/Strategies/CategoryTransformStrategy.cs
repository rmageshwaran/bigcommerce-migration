using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Transform strategy for category entities
/// Handles category tree mapping, meta_keywords parsing, URL generation, and required field validation
/// </summary>
public class CategoryTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<CategoryTransformStrategy> _logger;

    public string EntityType => "categories";

    public CategoryTransformStrategy(ILogger<CategoryTransformStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> entity,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Transforming category for migration {MigrationId}", migrationId);
        
        var transformed = new Dictionary<string, object>(entity);

        // Handle category tree mapping if context is available
        HandleCategoryTreeMapping(transformed, categoryTreeContext, migrationId);

        // Fix meta_keywords field - BigCommerce expects an array, not a malformed string
        HandleMetaKeywords(transformed, migrationId);

        // Ensure required BigCommerce category fields are present
        EnsureRequiredFields(transformed, migrationId);

        // Ensure custom_url structure exists and is valid
        EnsureCustomUrlStructure(transformed, migrationId);

        // Handle parent_id conversion - BigCommerce expects integer, but source might be string
        HandleParentIdConversion(transformed, migrationId);

        // Handle category_tree_id conversion - ensure it's an integer
        HandleCategoryTreeIdConversion(transformed, categoryTreeContext, migrationId);

        // Remove null values
        RemoveNullValues(transformed);

        return await Task.FromResult(transformed);
    }

    private void HandleCategoryTreeMapping(Dictionary<string, object> transformed, CategoryTreeContext? categoryTreeContext, string migrationId)
    {
        if (categoryTreeContext != null)
        {
            if (transformed.TryGetValue("category_tree_id", out var sourceTreeId))
            {
                if (!string.IsNullOrWhiteSpace(categoryTreeContext.DestinationCategoryTreeId))
                {
                    transformed["category_tree_id"] = categoryTreeContext.DestinationCategoryTreeId;
                    _logger.LogDebug("Mapped category tree ID from {SourceTreeId} to {DestinationTreeId} for migration {MigrationId}", 
                        sourceTreeId, categoryTreeContext.DestinationCategoryTreeId, migrationId);
                }
                else
                {
                    _logger.LogWarning("No destination category tree ID found for migration {MigrationId}", migrationId);
                }
            }
        }
    }

    private void HandleMetaKeywords(Dictionary<string, object> transformed, string migrationId)
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
                        _logger.LogDebug("Converted empty/malformed meta_keywords to empty array for migration {MigrationId}", migrationId);
                    }
                    else
                    {
                        // Try to parse as JSON array
                        var parsedKeywords = System.Text.Json.JsonSerializer.Deserialize<List<string>>(metaKeywordsString);
                        transformed["meta_keywords"] = parsedKeywords ?? new List<string>();
                        _logger.LogDebug("Successfully parsed meta_keywords JSON for migration {MigrationId}", migrationId);
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
                    _logger.LogWarning(ex, "Failed to parse meta_keywords as JSON, split as CSV instead for migration {MigrationId}", migrationId);
                }
            }
            else if (metaKeywords is not List<string> && metaKeywords is not string[])
            {
                transformed["meta_keywords"] = new List<string>();
                _logger.LogWarning("Invalid meta_keywords type, converted to empty array for migration {MigrationId}", migrationId);
            }
        }
    }

    private void EnsureRequiredFields(Dictionary<string, object> transformed, string migrationId)
    {
        if (!transformed.ContainsKey("name"))
        {
            transformed["name"] = "Unnamed Category";
            _logger.LogWarning("Category missing name field, using default for migration {MigrationId}", migrationId);
        }
    }

    private void EnsureCustomUrlStructure(Dictionary<string, object> transformed, string migrationId)
    {
        if (!transformed.ContainsKey("custom_url"))
        {
            transformed["custom_url"] = new Dictionary<string, object>();
        }

        if (transformed["custom_url"] is Dictionary<string, object> customUrl)
        {
            // Generate URL path if missing
            if (!customUrl.ContainsKey("url") || string.IsNullOrWhiteSpace(customUrl["url"]?.ToString()))
            {
                var categoryName = transformed["name"]?.ToString() ?? "category";
                var urlPath = GenerateUrlPath(categoryName);
                customUrl["url"] = $"/categories/{urlPath}/";
                _logger.LogDebug("Generated URL path {UrlPath} for category {CategoryName} in migration {MigrationId}", 
                    urlPath, categoryName, migrationId);
            }

            // Ensure is_customized is set
            if (!customUrl.ContainsKey("is_customized"))
            {
                customUrl["is_customized"] = true;
            }
        }
    }

    private void HandleParentIdConversion(Dictionary<string, object> transformed, string migrationId)
    {
        if (transformed.TryGetValue("parent_id", out var parentId))
        {
            if (parentId is string parentIdString)
            {
                if (int.TryParse(parentIdString, out var parsedParentId))
                {
                    transformed["parent_id"] = parsedParentId;
                    _logger.LogDebug("Converted string parent_id {ParentIdString} to integer {ParsedParentId} for migration {MigrationId}", 
                        parentIdString, parsedParentId, migrationId);
                }
                else
                {
                    transformed["parent_id"] = 0;
                    _logger.LogWarning("Invalid parent_id {ParentIdString}, treating as root category for migration {MigrationId}", 
                        parentIdString, migrationId);
                }
            }
        }
        else
        {
            transformed["parent_id"] = 0;
        }
    }

    private void HandleCategoryTreeIdConversion(Dictionary<string, object> transformed, CategoryTreeContext? categoryTreeContext, string migrationId)
    {
        if (transformed.TryGetValue("category_tree_id", out var treeId))
        {
            if (treeId is string treeIdString)
            {
                if (int.TryParse(treeIdString, out var parsedTreeId))
                {
                    transformed["category_tree_id"] = parsedTreeId;
                    _logger.LogDebug("Converted string category_tree_id {TreeIdString} to integer {ParsedTreeId} for migration {MigrationId}", 
                        treeIdString, parsedTreeId, migrationId);
                }
                else
                {
                    transformed["category_tree_id"] = 1;
                    _logger.LogWarning("Invalid category_tree_id {TreeIdString}, using default for migration {MigrationId}", 
                        treeIdString, migrationId);
                }
            }
        }
        else if (categoryTreeContext != null && !string.IsNullOrWhiteSpace(categoryTreeContext.DestinationCategoryTreeId))
        {
            if (int.TryParse(categoryTreeContext.DestinationCategoryTreeId, out var contextTreeId))
            {
                transformed["category_tree_id"] = contextTreeId;
            }
            else
            {
                transformed["category_tree_id"] = 1;
            }
        }
        else
        {
            transformed["category_tree_id"] = 1;
        }
    }

    private static void RemoveNullValues(Dictionary<string, object> transformed)
    {
        var keysToRemove = transformed.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            transformed.Remove(key);
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