using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Services;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Transform strategy for product entities
/// Handles category mapping, brand mapping, required field validation, and BigCommerce API compliance
/// Based on BigCommerce Products API: https://developer.bigcommerce.com/docs/rest-catalog/products#create-a-product
/// </summary>
public class ProductTransformStrategy : IEntityTransformStrategy
{
    private readonly ILogger<ProductTransformStrategy> _logger;
    private readonly IEntityMappingService _entityMappingService;

    public string EntityType => "products";

    public ProductTransformStrategy(
        ILogger<ProductTransformStrategy> logger,
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
        _logger.LogDebug("Transforming product for migration {MigrationId}", migrationId);
        
        var transformed = new Dictionary<string, object>();

        // Required field: name
        var productName = GetStringValue(entity, "name") ?? GetStringValue(entity, "title") ?? "Unnamed Product";
        transformed["name"] = productName;

        if (productName == "Unnamed Product")
        {
            _logger.LogWarning("Product missing name field, using default for migration {MigrationId}", migrationId);
        }

        // Required field: type (physical, digital)
        var productType = GetStringValue(entity, "type") ?? "physical";
        if (!IsValidProductType(productType))
        {
            _logger.LogWarning("Invalid product type '{ProductType}', defaulting to 'physical' for migration {MigrationId}", 
                productType, migrationId);
            productType = "physical";
        }
        transformed["type"] = productType;

        // Handle price and pricing fields
        TransformPricingFields(entity, transformed);

        // Handle SKU (Stock Keeping Unit)
        var sku = GetStringValue(entity, "sku");
        if (!string.IsNullOrWhiteSpace(sku))
        {
            transformed["sku"] = sku;
        }

        // Handle weight (important for shipping)
        TransformWeightField(entity, transformed);

        // Handle dimensions
        TransformDimensionFields(entity, transformed);

        // Handle descriptions
        var description = GetStringValue(entity, "description");
        if (!string.IsNullOrWhiteSpace(description))
        {
            transformed["description"] = description;
        }

        // 🚀 HARD-CODED CATEGORY: Using fixed category ID instead of dynamic mapping
        // This allows product migration to work without category migration dependency
        TransformCategoryMappingHardCoded(entity, transformed, migrationId);

        // Handle brand mapping
        await TransformBrandMappingAsync(entity, transformed, migrationId, cancellationToken);

        // Handle inventory and stock tracking
        TransformInventoryFields(entity, transformed);

        // Handle SEO fields
        TransformSeoFields(entity, transformed, productName);

        // Handle visibility and status
        TransformVisibilityFields(entity, transformed);

        // Handle custom fields and metadata
        TransformCustomFields(entity, transformed);

        // Handle product options (basic transformation)
        // Note: variants and images are handled as separate dependent entities
        TransformProductOptions(entity, transformed);

        // Handle videos (can be created with product)
        TransformVideoFields(entity, transformed);

        // Handle bulk pricing rules
        TransformBulkPricingRules(entity, transformed);

        // Handle product identifiers (UPC, GTIN, MPN)
        TransformProductIdentifiers(entity, transformed);

        // Handle shipping and tax fields
        TransformShippingAndTaxFields(entity, transformed);

        // Handle additional product attributes
        TransformAdditionalAttributes(entity, transformed);

        // Remove system fields that shouldn't be migrated
        RemoveSystemFields(transformed);

        // Final validation and cleanup
        ValidateRequiredFields(transformed, migrationId);

        _logger.LogDebug("Transformed product '{ProductName}' with {FieldCount} fields for migration {MigrationId}", 
            productName, transformed.Count, migrationId);

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
    /// Gets decimal value from entity with fallback handling
    /// </summary>
    private static decimal? GetDecimalValue(Dictionary<string, object> entity, string key)
    {
        if (entity.TryGetValue(key, out var value))
        {
            if (decimal.TryParse(value?.ToString(), out var result))
            {
                return result;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets integer value from entity with fallback handling
    /// </summary>
    private static int? GetIntValue(Dictionary<string, object> entity, string key)
    {
        if (entity.TryGetValue(key, out var value))
        {
            if (int.TryParse(value?.ToString(), out var result))
            {
                return result;
            }
        }
        return null;
    }

    /// <summary>
    /// Validates product type against BigCommerce accepted values
    /// </summary>
    private static bool IsValidProductType(string productType)
    {
        var validTypes = new[] { "physical", "digital" };
        return validTypes.Contains(productType?.ToLowerInvariant());
    }

    /// <summary>
    /// Transforms pricing fields (price, sale_price, cost_price, etc.)
    /// </summary>
    private void TransformPricingFields(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        // Main price field
        var price = GetDecimalValue(entity, "price") ?? GetDecimalValue(entity, "retail_price");
        if (price.HasValue && price > 0)
        {
            transformed["price"] = price.Value;
        }

        // Sale price
        var salePrice = GetDecimalValue(entity, "sale_price") ?? GetDecimalValue(entity, "special_price");
        if (salePrice.HasValue && salePrice > 0)
        {
            transformed["sale_price"] = salePrice.Value;
        }

        // Cost price (for profit calculations)
        var costPrice = GetDecimalValue(entity, "cost_price") ?? GetDecimalValue(entity, "wholesale_price");
        if (costPrice.HasValue && costPrice > 0)
        {
            transformed["cost_price"] = costPrice.Value;
        }

        // MSRP (Manufacturer's Suggested Retail Price)
        var msrp = GetDecimalValue(entity, "msrp") ?? GetDecimalValue(entity, "retail_price");
        if (msrp.HasValue && msrp > 0)
        {
            transformed["retail_price"] = msrp.Value;
        }
    }

    /// <summary>
    /// Transforms weight field with unit handling
    /// </summary>
    private void TransformWeightField(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        var weight = GetDecimalValue(entity, "weight");
        if (weight.HasValue && weight > 0)
        {
            transformed["weight"] = weight.Value;
        }
    }

    /// <summary>
    /// Transforms dimension fields (width, height, depth)
    /// </summary>
    private void TransformDimensionFields(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        var width = GetDecimalValue(entity, "width");
        if (width.HasValue && width > 0)
        {
            transformed["width"] = width.Value;
        }

        var height = GetDecimalValue(entity, "height");
        if (height.HasValue && height > 0)
        {
            transformed["height"] = height.Value;
        }

        var depth = GetDecimalValue(entity, "depth") ?? GetDecimalValue(entity, "length");
        if (depth.HasValue && depth > 0)
        {
            transformed["depth"] = depth.Value;
        }
    }

    /// <summary>
    /// Transforms category mapping using hard-coded category ID (no category migration dependency)
    /// </summary>
    private void TransformCategoryMappingHardCoded(Dictionary<string, object> entity, Dictionary<string, object> transformed, string migrationId)
    {
        // 🚀 HARD-CODED CATEGORY: Using fixed category ID 14988 for all products
        // This removes the dependency on category migration while focusing on brand/product migration
        const int HARD_CODED_CATEGORY_ID = 14988;
        
        // Check if the source product has any categories (for logging purposes)
        if (entity.TryGetValue("categories", out var categoriesValue))
        {
            var sourceCategoryIds = ExtractCategoryIds(categoriesValue);
            if (sourceCategoryIds.Any())
            {
                _logger.LogDebug("Product had {SourceCategoryCount} source categories, assigning hard-coded category ID {CategoryId} in migration {MigrationId}", 
                    sourceCategoryIds.Count, HARD_CODED_CATEGORY_ID, migrationId);
            }
            else
            {
                _logger.LogDebug("Product had no source categories, assigning hard-coded category ID {CategoryId} in migration {MigrationId}", 
                    HARD_CODED_CATEGORY_ID, migrationId);
            }
        }
        else
        {
            _logger.LogDebug("Product had no categories field, assigning hard-coded category ID {CategoryId} in migration {MigrationId}", 
                HARD_CODED_CATEGORY_ID, migrationId);
        }

        // Always assign the hard-coded category ID
        transformed["categories"] = new List<int> { HARD_CODED_CATEGORY_ID };
        
        _logger.LogDebug("✅ Assigned hard-coded category ID {CategoryId} to product in migration {MigrationId}", 
            HARD_CODED_CATEGORY_ID, migrationId);
    }

    /// <summary>
    /// Extracts category IDs from various category field formats
    /// </summary>
    private static List<int> ExtractCategoryIds(object categoriesValue)
    {
        var categoryIds = new List<int>();

        if (categoriesValue is IEnumerable<object> categoryArray)
        {
            foreach (var category in categoryArray)
            {
                if (category is Dictionary<string, object> categoryDict)
                {
                    // Category object with ID field
                    if (categoryDict.TryGetValue("id", out var idValue) && int.TryParse(idValue?.ToString(), out var id))
                    {
                        categoryIds.Add(id);
                    }
                }
                else if (int.TryParse(category?.ToString(), out var directId))
                {
                    // Direct category ID
                    categoryIds.Add(directId);
                }
            }
        }

        return categoryIds;
    }

    /// <summary>
    /// Transforms brand mapping using entity mapping service
    /// </summary>
    private async Task TransformBrandMappingAsync(Dictionary<string, object> entity, Dictionary<string, object> transformed, 
        string migrationId, CancellationToken cancellationToken)
    {
        var sourceBrandId = GetIntValue(entity, "brand_id");
        if (sourceBrandId.HasValue && sourceBrandId > 0)
        {
            try
            {
                // Use entity mapping service to get destination brand ID
                var destinationBrandId = await _entityMappingService.GetDestinationIdAsync(
                    migrationId, 
                    "brands", 
                    sourceBrandId.ToString(), 
                    cancellationToken);
                
                if (!string.IsNullOrEmpty(destinationBrandId) && int.TryParse(destinationBrandId, out var mappedBrandId))
                {
                    transformed["brand_id"] = mappedBrandId;
                    _logger.LogDebug("Mapped source brand ID {SourceId} to destination ID {DestinationId} in migration {MigrationId}", 
                        sourceBrandId, mappedBrandId, migrationId);
                }
                else
                {
                    _logger.LogWarning("No mapping found for source brand ID {SourceId} in migration {MigrationId}. Product will be created without brand.", 
                        sourceBrandId, migrationId);
                    // Don't include brand_id if no mapping exists
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to map brand ID {SourceId} in migration {MigrationId}. Product will be created without brand.", 
                    sourceBrandId, migrationId);
                // Don't include brand_id if mapping fails
            }
        }
    }

    /// <summary>
    /// Transforms inventory and stock tracking fields
    /// </summary>
    private void TransformInventoryFields(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        // Inventory tracking level
        var inventoryTracking = GetStringValue(entity, "inventory_tracking");
        if (!string.IsNullOrWhiteSpace(inventoryTracking))
        {
            // Valid values: "none", "product", "variant"
            var validTrackingLevels = new[] { "none", "product", "variant" };
            if (validTrackingLevels.Contains(inventoryTracking.ToLowerInvariant()))
            {
                transformed["inventory_tracking"] = inventoryTracking.ToLowerInvariant();
            }
        }

        // Current inventory level
        var inventoryLevel = GetIntValue(entity, "inventory_level") ?? GetIntValue(entity, "quantity");
        if (inventoryLevel.HasValue)
        {
            transformed["inventory_level"] = inventoryLevel.Value;
        }

        // Low stock warning level
        var inventoryWarningLevel = GetIntValue(entity, "inventory_warning_level") ?? GetIntValue(entity, "low_stock_level");
        if (inventoryWarningLevel.HasValue && inventoryWarningLevel > 0)
        {
            transformed["inventory_warning_level"] = inventoryWarningLevel.Value;
        }
    }

    /// <summary>
    /// Transforms SEO-related fields
    /// </summary>
    private void TransformSeoFields(Dictionary<string, object> entity, Dictionary<string, object> transformed, string productName)
    {
        // Page title (falls back to product name)
        var pageTitle = GetStringValue(entity, "page_title") ?? GetStringValue(entity, "seo_title") ?? productName;
        if (!string.IsNullOrWhiteSpace(pageTitle))
        {
            transformed["page_title"] = pageTitle;
        }

        // Meta description
        var metaDescription = GetStringValue(entity, "meta_description") ?? GetStringValue(entity, "seo_description");
        if (!string.IsNullOrWhiteSpace(metaDescription))
        {
            transformed["meta_description"] = metaDescription;
        }

        // Search keywords
        var searchKeywords = GetStringValue(entity, "search_keywords") ?? GetStringValue(entity, "keywords");
        if (!string.IsNullOrWhiteSpace(searchKeywords))
        {
            transformed["search_keywords"] = searchKeywords;
        }

        // Custom URL/slug
        var customUrl = GetStringValue(entity, "custom_url") ?? GetStringValue(entity, "url_path") ?? GetStringValue(entity, "slug");
        if (!string.IsNullOrWhiteSpace(customUrl))
        {
            // Ensure URL starts with /
            if (!customUrl.StartsWith('/'))
            {
                customUrl = '/' + customUrl;
            }
            transformed["custom_url"] = new Dictionary<string, object>
            {
                ["url"] = customUrl,
                ["is_customized"] = true
            };
        }
    }

    /// <summary>
    /// Transforms visibility and status fields
    /// </summary>
    private void TransformVisibilityFields(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        // Product visibility
        var isVisible = GetStringValue(entity, "is_visible") ?? GetStringValue(entity, "status");
        if (!string.IsNullOrWhiteSpace(isVisible))
        {
            // Convert various status formats to boolean
            bool visible = isVisible.ToLowerInvariant() switch
            {
                "true" => true,
                "false" => false,
                "1" => true,
                "0" => false,
                "active" => true,
                "inactive" => false,
                "enabled" => true,
                "disabled" => false,
                "published" => true,
                "draft" => false,
                _ => true // Default to visible
            };
            transformed["is_visible"] = visible;
        }

        // Featured product flag
        var isFeatured = GetStringValue(entity, "is_featured");
        if (!string.IsNullOrWhiteSpace(isFeatured))
        {
            if (bool.TryParse(isFeatured, out var featured))
            {
                transformed["is_featured"] = featured;
            }
        }
    }

    /// <summary>
    /// Transforms custom fields and metadata
    /// </summary>
    private void TransformCustomFields(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        // Handle custom fields if they exist
        if (entity.TryGetValue("custom_fields", out var customFieldsValue) && customFieldsValue is IEnumerable<object> customFields)
        {
            var transformedCustomFields = new List<object>();
            foreach (var field in customFields)
            {
                if (field is Dictionary<string, object> customField && 
                    customField.ContainsKey("name") && customField.ContainsKey("value"))
                {
                    transformedCustomFields.Add(customField);
                }
            }
            
            if (transformedCustomFields.Any())
            {
                transformed["custom_fields"] = transformedCustomFields;
            }
        }
    }

    /// <summary>
    /// Transforms basic product options (for variants)
    /// </summary>
    private void TransformProductOptions(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        // Handle product options if they exist (basic transformation)
        if (entity.TryGetValue("options", out var optionsValue) && optionsValue is IEnumerable<object> options)
        {
            var transformedOptions = new List<object>();
            foreach (var option in options)
            {
                if (option is Dictionary<string, object> optionDict)
                {
                    // Basic option transformation - in full implementation, you'd handle option types, values, etc.
                    transformedOptions.Add(optionDict);
                }
            }
            
            if (transformedOptions.Any())
            {
                transformed["options"] = transformedOptions;
            }
        }
    }

    /// <summary>
    /// Removes BigCommerce system fields that shouldn't be migrated (only truly read-only fields)
    /// </summary>
    private static void RemoveSystemFields(Dictionary<string, object> transformed)
    {
        // Remove read-only system fields and dependent entities that are handled separately
        var systemFields = new[]
        {
            // Read-only system fields that BigCommerce sets automatically
            "id", "product_id", "date_created", "date_modified", "calculated_price", "calculated_weight",
            "reviews_rating_sum", "reviews_count", "total_sold", "view_count",
            "option_set_id", "option_set_display", "primary_image", "base_variant_id",
            "storefronts", // Store-specific field managed by BigCommerce
            
            // Dependent entities handled separately after product creation
            "images", "variants", "related_products"
        };

        foreach (var field in systemFields)
        {
            transformed.Remove(field);
        }
    }

    /// <summary>
    /// Validates that all required fields are present
    /// </summary>
    private void ValidateRequiredFields(Dictionary<string, object> transformed, string migrationId)
    {
        var requiredFields = new[] { "name", "type" };
        
        foreach (var field in requiredFields)
        {
            if (!transformed.ContainsKey(field) || string.IsNullOrWhiteSpace(transformed[field]?.ToString()))
            {
                _logger.LogWarning("Product missing required field '{Field}' for migration {MigrationId}", field, migrationId);
            }
        }
    }

    /// <summary>
    /// Transforms video fields (can be created with product)
    /// </summary>
    private void TransformVideoFields(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        // Handle videos
        if (entity.TryGetValue("videos", out var videosValue) && videosValue is IEnumerable<object> videos)
        {
            var transformedVideos = new List<object>();
            foreach (var video in videos)
            {
                if (video is Dictionary<string, object> videoDict)
                {
                    transformedVideos.Add(videoDict);
                }
            }
            
            if (transformedVideos.Any())
            {
                transformed["videos"] = transformedVideos;
            }
        }
    }

    /// <summary>
    /// Transforms bulk pricing rules
    /// </summary>
    private void TransformBulkPricingRules(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        if (entity.TryGetValue("bulk_pricing_rules", out var bulkPricingValue) && bulkPricingValue is IEnumerable<object> bulkPricingRules)
        {
            var transformedRules = new List<object>();
            foreach (var rule in bulkPricingRules)
            {
                if (rule is Dictionary<string, object> ruleDict)
                {
                    transformedRules.Add(ruleDict);
                }
            }
            
            if (transformedRules.Any())
            {
                transformed["bulk_pricing_rules"] = transformedRules;
            }
        }
    }

    /// <summary>
    /// Transforms product identifiers (UPC, GTIN, MPN, etc.)
    /// </summary>
    private void TransformProductIdentifiers(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        // UPC (Universal Product Code)
        var upc = GetStringValue(entity, "upc");
        if (!string.IsNullOrWhiteSpace(upc))
        {
            transformed["upc"] = upc;
        }

        // GTIN (Global Trade Item Number)
        var gtin = GetStringValue(entity, "gtin") ?? GetStringValue(entity, "global_trade_item_number");
        if (!string.IsNullOrWhiteSpace(gtin))
        {
            transformed["gtin"] = gtin;
        }

        // MPN (Manufacturer Part Number)
        var mpn = GetStringValue(entity, "mpn") ?? GetStringValue(entity, "manufacturer_part_number");
        if (!string.IsNullOrWhiteSpace(mpn))
        {
            transformed["mpn"] = mpn;
        }

        // EAN (European Article Number)
        var ean = GetStringValue(entity, "ean");
        if (!string.IsNullOrWhiteSpace(ean))
        {
            transformed["ean"] = ean;
        }

        // ISBN (for books)
        var isbn = GetStringValue(entity, "isbn");
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            transformed["isbn"] = isbn;
        }
    }

    /// <summary>
    /// Transforms shipping and tax related fields
    /// </summary>
    private void TransformShippingAndTaxFields(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        // Tax class ID
        var taxClassId = GetIntValue(entity, "tax_class_id");
        if (taxClassId.HasValue)
        {
            transformed["tax_class_id"] = taxClassId.Value;
        }

        // Product tax code
        var productTaxCode = GetStringValue(entity, "product_tax_code");
        if (!string.IsNullOrWhiteSpace(productTaxCode))
        {
            transformed["product_tax_code"] = productTaxCode;
        }

        // Free shipping
        var isFreeShipping = GetBooleanValue(entity, "is_free_shipping");
        if (isFreeShipping.HasValue)
        {
            transformed["is_free_shipping"] = isFreeShipping.Value;
        }

        // Fixed cost shipping price
        var fixedCostShippingPrice = GetDecimalValue(entity, "fixed_cost_shipping_price");
        if (fixedCostShippingPrice.HasValue && fixedCostShippingPrice > 0)
        {
            transformed["fixed_cost_shipping_price"] = fixedCostShippingPrice.Value;
        }
    }

    /// <summary>
    /// Transforms additional product attributes
    /// </summary>
    private void TransformAdditionalAttributes(Dictionary<string, object> entity, Dictionary<string, object> transformed)
    {
        // Condition
        var condition = GetStringValue(entity, "condition");
        if (!string.IsNullOrWhiteSpace(condition))
        {
            transformed["condition"] = condition;
        }

        // Warranty
        var warranty = GetStringValue(entity, "warranty");
        if (!string.IsNullOrWhiteSpace(warranty))
        {
            transformed["warranty"] = warranty;
        }

        // Bin picking number
        var binPickingNumber = GetStringValue(entity, "bin_picking_number");
        if (!string.IsNullOrWhiteSpace(binPickingNumber))
        {
            transformed["bin_picking_number"] = binPickingNumber;
        }

        // Sort order
        var sortOrder = GetIntValue(entity, "sort_order");
        if (sortOrder.HasValue)
        {
            transformed["sort_order"] = sortOrder.Value;
        }

        // Availability description
        var availabilityDescription = GetStringValue(entity, "availability_description");
        if (!string.IsNullOrWhiteSpace(availabilityDescription))
        {
            transformed["availability_description"] = availabilityDescription;
        }

        // Availability
        var availability = GetStringValue(entity, "availability");
        if (!string.IsNullOrWhiteSpace(availability))
        {
            transformed["availability"] = availability;
        }

        // Note: Related products are handled separately after all products are created (requires product ID mapping)

        // Gift wrapping options
        var giftWrappingOptionsType = GetStringValue(entity, "gift_wrapping_options_type");
        if (!string.IsNullOrWhiteSpace(giftWrappingOptionsType))
        {
            transformed["gift_wrapping_options_type"] = giftWrappingOptionsType;
        }

        if (entity.TryGetValue("gift_wrapping_options_list", out var giftWrappingListValue) && giftWrappingListValue is IEnumerable<object> giftWrappingList)
        {
            var transformedGiftWrapping = new List<object>();
            foreach (var item in giftWrappingList)
            {
                transformedGiftWrapping.Add(item);
            }
            
            if (transformedGiftWrapping.Any())
            {
                transformed["gift_wrapping_options_list"] = transformedGiftWrapping;
            }
        }

        // Price hiding options
        var isPriceHidden = GetBooleanValue(entity, "is_price_hidden");
        if (isPriceHidden.HasValue)
        {
            transformed["is_price_hidden"] = isPriceHidden.Value;
        }

        var priceHiddenLabel = GetStringValue(entity, "price_hidden_label");
        if (!string.IsNullOrWhiteSpace(priceHiddenLabel))
        {
            transformed["price_hidden_label"] = priceHiddenLabel;
        }

        // Open Graph fields for social media
        var openGraphType = GetStringValue(entity, "open_graph_type");
        if (!string.IsNullOrWhiteSpace(openGraphType))
        {
            transformed["open_graph_type"] = openGraphType;
        }

        var openGraphTitle = GetStringValue(entity, "open_graph_title");
        if (!string.IsNullOrWhiteSpace(openGraphTitle))
        {
            transformed["open_graph_title"] = openGraphTitle;
        }

        var openGraphDescription = GetStringValue(entity, "open_graph_description");
        if (!string.IsNullOrWhiteSpace(openGraphDescription))
        {
            transformed["open_graph_description"] = openGraphDescription;
        }

        // Open Graph boolean flags
        var openGraphUseMetaDescription = GetBooleanValue(entity, "open_graph_use_meta_description");
        if (openGraphUseMetaDescription.HasValue)
        {
            transformed["open_graph_use_meta_description"] = openGraphUseMetaDescription.Value;
        }

        var openGraphUseProductName = GetBooleanValue(entity, "open_graph_use_product_name");
        if (openGraphUseProductName.HasValue)
        {
            transformed["open_graph_use_product_name"] = openGraphUseProductName.Value;
        }

        var openGraphUseImage = GetBooleanValue(entity, "open_graph_use_image");
        if (openGraphUseImage.HasValue)
        {
            transformed["open_graph_use_image"] = openGraphUseImage.Value;
        }

        // Note: Variants are handled as separate dependent entities after product creation
    }

    /// <summary>
    /// Gets boolean value from entity with various format handling
    /// </summary>
    private static bool? GetBooleanValue(Dictionary<string, object> entity, string key)
    {
        if (entity.TryGetValue(key, out var value))
        {
            return value?.ToString()?.ToLowerInvariant() switch
            {
                "true" => true,
                "false" => false,
                "1" => true,
                "0" => false,
                "yes" => true,
                "no" => false,
                _ => null
            };
        }
        return null;
    }
} 