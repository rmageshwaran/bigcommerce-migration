using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Transform strategy for brand entities
/// Handles required field validation and null value cleanup
/// </summary>
public class BrandTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<BrandTransformStrategy> _logger;

    public string EntityType => "brands";

    public BrandTransformStrategy(ILogger<BrandTransformStrategy> logger)
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
        _logger.LogDebug("Transforming brand for migration {MigrationId}", migrationId);
        
        var transformed = new Dictionary<string, object>();

        // Required field: name
        var brandName = GetStringValue(entity, "name") ?? GetStringValue(entity, "brand_name") ?? "Unnamed Brand";
        transformed["name"] = brandName;

        if (brandName == "Unnamed Brand")
        {
            _logger.LogWarning("Brand missing name field, using default for migration {MigrationId}", migrationId);
        }

        // Optional fields with proper mapping to BigCommerce API format
        
        // Page title (falls back to name if not provided)
        var pageTitle = GetStringValue(entity, "page_title") ?? GetStringValue(entity, "seo_title") ?? brandName;
        if (!string.IsNullOrWhiteSpace(pageTitle))
        {
            transformed["page_title"] = pageTitle;
        }

        // Meta keywords (can be array or comma-separated string)
        var metaKeywords = GetMetaKeywords(entity);
        if (metaKeywords != null && metaKeywords.Count > 0)
        {
            transformed["meta_keywords"] = metaKeywords;
        }

        // Meta description
        var metaDescription = GetStringValue(entity, "meta_description") ?? GetStringValue(entity, "description");
        if (!string.IsNullOrWhiteSpace(metaDescription))
        {
            transformed["meta_description"] = metaDescription;
        }

        // Search keywords (comma-separated string)
        var searchKeywords = GetStringValue(entity, "search_keywords") ?? GetStringValue(entity, "keywords");
        if (!string.IsNullOrWhiteSpace(searchKeywords))
        {
            transformed["search_keywords"] = searchKeywords;
        }

        // Brand image URL
        var imageUrl = GetStringValue(entity, "image_url") ?? GetStringValue(entity, "image") ?? GetStringValue(entity, "logo_url");
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            transformed["image_url"] = imageUrl;
        }

        // Custom URL structure
        var customUrl = GetCustomUrl(entity);
        if (customUrl != null)
        {
            transformed["custom_url"] = customUrl;
        }

        _logger.LogDebug("Transformed brand '{BrandName}' with {FieldCount} fields for migration {MigrationId}", 
            brandName, transformed.Count, migrationId);

        return await Task.FromResult(transformed);
    }

    /// <summary>
    /// Safely gets string value from entity, handling various field name variations
    /// </summary>
    private static string? GetStringValue(Dictionary<string, object> entity, string key)
    {
        if (entity.TryGetValue(key, out var value))
        {
            return value?.ToString()?.Trim();
        }
        return null;
    }

    /// <summary>
    /// Extracts meta keywords as array, handling both array and string formats
    /// </summary>
    private static List<string>? GetMetaKeywords(Dictionary<string, object> entity)
    {
        // Try meta_keywords first
        if (entity.TryGetValue("meta_keywords", out var metaKeywords))
        {
            if (metaKeywords is IEnumerable<object> keywordArray)
            {
                var keywords = keywordArray.Select(k => k?.ToString()?.Trim())
                                         .Where(k => !string.IsNullOrWhiteSpace(k))
                                         .Cast<string>()
                                         .ToList();
                return keywords.Count > 0 ? keywords : null;
            }
            
            if (metaKeywords is string keywordString && !string.IsNullOrWhiteSpace(keywordString))
            {
                var keywords = keywordString.Split(',', ';')
                                           .Select(k => k.Trim())
                                           .Where(k => !string.IsNullOrWhiteSpace(k))
                                           .ToList();
                return keywords.Count > 0 ? keywords : null;
            }
        }

        // Try alternative field names
        var alternateFields = new[] { "keywords", "tags", "meta_tags" };
        foreach (var field in alternateFields)
        {
            if (entity.TryGetValue(field, out var value) && value is string strValue && !string.IsNullOrWhiteSpace(strValue))
            {
                var keywords = strValue.Split(',', ';')
                                      .Select(k => k.Trim())
                                      .Where(k => !string.IsNullOrWhiteSpace(k))
                                      .ToList();
                return keywords.Count > 0 ? keywords : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts or generates custom URL structure
    /// </summary>
    private static Dictionary<string, object>? GetCustomUrl(Dictionary<string, object> entity)
    {
        // Check if custom_url already exists as an object
        if (entity.TryGetValue("custom_url", out var customUrlObj) && customUrlObj is Dictionary<string, object> existingCustomUrl)
        {
            return existingCustomUrl;
        }

        // Try to construct from url and slug fields
        var url = GetStringValue(entity, "url") ?? GetStringValue(entity, "slug") ?? GetStringValue(entity, "custom_url");
        if (!string.IsNullOrWhiteSpace(url))
        {
            // Ensure URL starts with /
            if (!url.StartsWith('/'))
            {
                url = '/' + url;
            }

            return new Dictionary<string, object>
            {
                ["url"] = url,
                ["is_customized"] = true
            };
        }

        return null;
    }
} 