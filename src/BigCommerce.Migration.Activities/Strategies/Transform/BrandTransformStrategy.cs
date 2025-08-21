using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace BigCommerce.Migration.Activities.Strategies.Transform;

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
        // 🚨 ENHANCED DEBUG: Track individual brand transformations
        var brandId = entity.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
        var originalId = entity.TryGetValue("_original_entity_id", out var origId) ? origId?.ToString() : "unknown";
        var threadId = Thread.CurrentThread.ManagedThreadId;
        var transformId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation("🔄 [TRANSFORM-{TransformId}] 🚀 STARTING: Brand transformation for SourceId={SourceId}, " +
                              "OriginalId={OriginalId}, ThreadId={ThreadId}, MigrationId={MigrationId}",
            transformId, brandId, originalId, threadId, migrationId);

        var transformed = new Dictionary<string, object>();

        // Required field: name
        var brandName = GetStringValue(entity, "name") ?? GetStringValue(entity, "brand_name") ?? "Unnamed Brand";
        transformed["name"] = brandName;

        _logger.LogInformation("🔄 [TRANSFORM-{TransformId}] BRAND NAME: '{BrandName}' (SourceId={SourceId}, ThreadId={ThreadId})",
            transformId, brandName, brandId, threadId);

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

        // 🚨 CRITICAL FIX: Preserve chunk tracking metadata from fetch phase
        // This metadata is essential for debugging race conditions and chunk overlap
        if (entity.TryGetValue("_chunk_number", out var chunkNumber))
        {
            transformed["_chunk_number"] = chunkNumber;
        }
        if (entity.TryGetValue("_api_page", out var apiPage))
        {
            transformed["_api_page"] = apiPage;
        }
        if (entity.TryGetValue("_migration_id", out var migId))
        {
            transformed["_migration_id"] = migId;
        }
        if (entity.TryGetValue("_original_entity_id", out var origEntityId))
        {
            transformed["_original_entity_id"] = origEntityId;
        }

        _logger.LogInformation("🔄 [TRANSFORM-{TransformId}] ✅ COMPLETED: Brand '{BrandName}' transformed with {FieldCount} fields " +
                              "CHUNK={ChunkNumber}, API_PAGE={ApiPage} (SourceId={SourceId}, ThreadId={ThreadId}, MigrationId={MigrationId})",
            transformId, brandName, transformed.Count, chunkNumber?.ToString() ?? "unknown",
            apiPage?.ToString() ?? "unknown", brandId, threadId, migrationId);

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
    /// Properly handles empty arrays and JSON serialization artifacts
    /// </summary>
    private static List<string>? GetMetaKeywords(Dictionary<string, object> entity)
    {
        // Try meta_keywords first
        if (entity.TryGetValue("meta_keywords", out var metaKeywords))
        {
            // Handle JsonElement (from JSON deserialization)
            if (metaKeywords is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var keywords = new List<string>();
                    foreach (var item in jsonElement.EnumerateArray())
                    {
                        var keyword = item.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(keyword))
                        {
                            keywords.Add(keyword);
                        }
                    }
                    return keywords.Count > 0 ? keywords : null;
                }
                else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var keywordString = jsonElement.GetString();
                    if (!string.IsNullOrWhiteSpace(keywordString) && keywordString != "[]")
                    {
                        var keywords = keywordString.Split(',', ';')
                                                   .Select(k => k.Trim())
                                                   .Where(k => !string.IsNullOrWhiteSpace(k))
                                                   .ToList();
                        return keywords.Count > 0 ? keywords : null;
                    }
                }
            }
            // Handle native array types
            else if (metaKeywords is IEnumerable<object> keywordArray)
            {
                var keywords = keywordArray.Select(k => k?.ToString()?.Trim())
                                         .Where(k => !string.IsNullOrWhiteSpace(k))
                                         .Cast<string>()
                                         .ToList();
                return keywords.Count > 0 ? keywords : null;
            }
            // Handle string format (but avoid "[]" artifact)
            else if (metaKeywords is string keywordString && !string.IsNullOrWhiteSpace(keywordString) && keywordString != "[]")
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
            if (entity.TryGetValue(field, out var value) && value is string strValue && !string.IsNullOrWhiteSpace(strValue) && strValue != "[]")
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
    /// Handles JSON deserialization and various object types
    /// </summary>
    private static Dictionary<string, object>? GetCustomUrl(Dictionary<string, object> entity)
    {
        // Check if custom_url already exists
        if (entity.TryGetValue("custom_url", out var customUrlObj))
        {
            // Handle JsonElement (from JSON deserialization)
            if (customUrlObj is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var customUrl = new Dictionary<string, object>();

                    if (jsonElement.TryGetProperty("url", out var urlElement))
                    {
                        var urlValue = urlElement.GetString();
                        if (!string.IsNullOrWhiteSpace(urlValue))
                        {
                            customUrl["url"] = urlValue;
                        }
                    }

                    if (jsonElement.TryGetProperty("is_customized", out var isCustomizedElement))
                    {
                        customUrl["is_customized"] = isCustomizedElement.GetBoolean();
                    }
                    else
                    {
                        customUrl["is_customized"] = true; // Default for migrated URLs
                    }

                    return customUrl.ContainsKey("url") ? customUrl : null;
                }
                else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var urlString = jsonElement.GetString();
                    if (!string.IsNullOrWhiteSpace(urlString))
                    {
                        return new Dictionary<string, object>
                        {
                            ["url"] = urlString.StartsWith('/') ? urlString : '/' + urlString,
                            ["is_customized"] = true
                        };
                    }
                }
            }
            // Handle native Dictionary<string, object>
            else if (customUrlObj is Dictionary<string, object> existingCustomUrl)
            {
                return existingCustomUrl;
            }
            // Handle other dictionary types
            else if (customUrlObj is IDictionary<string, object> dictCustomUrl)
            {
                return new Dictionary<string, object>(dictCustomUrl);
            }
        }

        // Try to construct from url and slug fields
        var finalUrl = GetStringValue(entity, "url") ?? GetStringValue(entity, "slug");
        if (!string.IsNullOrWhiteSpace(finalUrl))
        {
            // Ensure URL starts with /
            if (!finalUrl.StartsWith('/'))
            {
                finalUrl = '/' + finalUrl;
            }

            return new Dictionary<string, object>
            {
                ["url"] = finalUrl,
                ["is_customized"] = true
            };
        }

        return null;
    }
}