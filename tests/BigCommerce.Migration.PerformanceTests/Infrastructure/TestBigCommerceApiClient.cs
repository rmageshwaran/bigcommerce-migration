using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// Test implementation of IBigCommerceApiClient for performance benchmarking
/// Simulates realistic API call timing and memory usage patterns
/// </summary>
public class TestBigCommerceApiClient : IBigCommerceApiClient
{
    private readonly Random _random = new();

    /// <summary>
    /// Simulates getting a single product with realistic timing
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetProductsAsync(
        StoreConfiguration storeConfig, 
        int page = 1, 
        int limit = 50, 
        CancellationToken cancellationToken = default)
    {
        // Simulate network latency and processing time (200-500ms typical)
        var delay = _random.Next(200, 500);
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        
        // Create realistic product data to simulate memory usage
        var products = new List<Dictionary<string, object>>();
        for (int i = 0; i < limit; i++)
        {
            var product = new Dictionary<string, object>
            {
                ["id"] = page * limit + i,
                ["name"] = $"Test Product {page * limit + i}",
                ["sku"] = $"TEST-SKU-{page * limit + i}",
                ["price"] = Math.Round(_random.NextDouble() * 100, 2),
                ["description"] = GenerateProductDescription(),
                ["weight"] = Math.Round(_random.NextDouble() * 10, 2),
                ["categories"] = new[] { _random.Next(1, 10), _random.Next(1, 10) },
                ["images"] = GenerateProductImages(3),
                ["variants"] = GenerateProductVariants(5),
                ["custom_fields"] = GenerateCustomFields(2)
            };
            products.Add(product);
        }
        
        return products;
    }



    /// <summary>
    /// Simulates getting categories with pagination support
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetCategoriesAsync(
        BigCommercePaginationRequest paginationRequest)
    {
        // Simulate category fetch (100-250ms typical)
        var delay = _random.Next(100, 250);
        await Task.Delay(delay).ConfigureAwait(false);
        
        var categories = new List<Dictionary<string, object>>();
        var limit = paginationRequest.Limit;
        
        for (int i = 0; i < limit; i++)
        {
            var category = new Dictionary<string, object>
            {
                ["id"] = paginationRequest.Page * limit + i,
                ["name"] = $"Category {paginationRequest.Page * limit + i}",
                ["parent_id"] = _random.Next(0, 5), // Some root categories (0), some sub-categories
                ["description"] = $"Description for category {paginationRequest.Page * limit + i}",
                ["sort_order"] = i,
                ["is_visible"] = true,
                ["meta_keywords"] = GenerateMetaKeywords(),
                ["meta_description"] = $"Meta description for category {paginationRequest.Page * limit + i}"
            };
            categories.Add(category);
        }
        
        return categories;
    }

    #region Helper Methods for Realistic Data Generation

    private string GenerateProductDescription()
    {
        var descriptions = new[]
        {
            "High-quality product with excellent features and durable construction.",
            "Premium item designed for performance and reliability in demanding environments.",
            "Innovative solution that combines functionality with elegant design principles.",
            "Professional-grade equipment suitable for both beginners and experts alike.",
            "Versatile product offering exceptional value and long-lasting performance."
        };
        return descriptions[_random.Next(descriptions.Length)];
    }

    private List<Dictionary<string, object>> GenerateProductImages(int count)
    {
        var images = new List<Dictionary<string, object>>();
        for (int i = 0; i < count; i++)
        {
            images.Add(new Dictionary<string, object>
            {
                ["id"] = _random.Next(10000, 99999),
                ["url"] = $"https://example.com/images/product_image_{i}.jpg",
                ["alt_text"] = $"Product image {i + 1}",
                ["sort_order"] = i,
                ["is_thumbnail"] = i == 0
            });
        }
        return images;
    }

    private List<Dictionary<string, object>> GenerateProductVariants(int count)
    {
        var variants = new List<Dictionary<string, object>>();
        for (int i = 0; i < count; i++)
        {
            variants.Add(new Dictionary<string, object>
            {
                ["id"] = _random.Next(10000, 99999),
                ["sku"] = $"VAR-{i:D3}",
                ["price"] = Math.Round(_random.NextDouble() * 50 + 10, 2),
                ["weight"] = Math.Round(_random.NextDouble() * 5, 2),
                ["inventory_level"] = _random.Next(0, 100),
                ["option_values"] = new[]
                {
                    new Dictionary<string, object> { ["label"] = "Color", ["value"] = "Red" },
                    new Dictionary<string, object> { ["label"] = "Size", ["value"] = "Large" }
                }
            });
        }
        return variants;
    }

    private List<Dictionary<string, object>> GenerateCustomFields(int count)
    {
        var fields = new List<Dictionary<string, object>>();
        for (int i = 0; i < count; i++)
        {
            fields.Add(new Dictionary<string, object>
            {
                ["id"] = _random.Next(1000, 9999),
                ["name"] = $"custom_field_{i}",
                ["value"] = $"Custom value {i}",
                ["type"] = "text"
            });
        }
        return fields;
    }

    private List<Dictionary<string, object>> GenerateProductModifiers(int count)
    {
        var modifiers = new List<Dictionary<string, object>>();
        for (int i = 0; i < count; i++)
        {
            modifiers.Add(new Dictionary<string, object>
            {
                ["id"] = _random.Next(1000, 9999),
                ["name"] = $"Modifier {i}",
                ["type"] = "dropdown",
                ["required"] = _random.Next(0, 2) == 1,
                ["config"] = new Dictionary<string, object>
                {
                    ["default_value"] = $"Default {i}",
                    ["checked_by_default"] = false
                }
            });
        }
        return modifiers;
    }

    private List<Dictionary<string, object>> GenerateProductReviews(int count)
    {
        var reviews = new List<Dictionary<string, object>>();
        for (int i = 0; i < count; i++)
        {
            reviews.Add(new Dictionary<string, object>
            {
                ["id"] = _random.Next(10000, 99999),
                ["rating"] = _random.Next(1, 6),
                ["title"] = $"Review {i + 1}",
                ["text"] = "Great product, would recommend to others!",
                ["date_created"] = DateTime.UtcNow.AddDays(-_random.Next(1, 365)),
                ["name"] = $"Customer {i + 1}"
            });
        }
        return reviews;
    }

    private List<Dictionary<string, object>> GenerateRelatedProducts(int count)
    {
        var related = new List<Dictionary<string, object>>();
        for (int i = 0; i < count; i++)
        {
            related.Add(new Dictionary<string, object>
            {
                ["id"] = _random.Next(1000, 9999),
                ["name"] = $"Related Product {i + 1}"
            });
        }
        return related;
    }

    private List<Dictionary<string, object>> GenerateMetaFields(int count)
    {
        var metaFields = new List<Dictionary<string, object>>();
        for (int i = 0; i < count; i++)
        {
            metaFields.Add(new Dictionary<string, object>
            {
                ["key"] = $"meta_key_{i}",
                ["value"] = $"Meta value {i}",
                ["namespace"] = "global"
            });
        }
        return metaFields;
    }

    private string GenerateMetaKeywords()
    {
        var keywords = new[] { "quality", "premium", "durable", "reliable", "innovative", "professional" };
        return string.Join(", ", keywords.OrderBy(x => _random.Next()).Take(3));
    }

    #endregion

    #region IBigCommerceApiClient Interface Implementation

    public async Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(100, 200), cancellationToken).ConfigureAwait(false);
        return new List<Dictionary<string, object>>
        {
            new() { ["id"] = "1", ["name"] = "Default Category Tree", ["channels"] = new[] { 1 } }
        };
    }

    public async Task<List<Dictionary<string, object>>> GetCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, CancellationToken cancellationToken = default)
    {
        var paginationRequest = new BigCommercePaginationRequest { Page = 1, Limit = 50 };
        return await GetCategoriesAsync(paginationRequest).ConfigureAwait(false);
    }

    public async Task<List<Dictionary<string, object>>> CreateProductsAsync(StoreConfiguration storeConfig, List<Dictionary<string, object>> products, CancellationToken cancellationToken = default)
    {
        // Simulate creation time proportional to number of products
        var delay = products.Count * _random.Next(50, 150);
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        return products; // Return same products with simulated IDs
    }

    public async Task<List<Dictionary<string, object>>> CreateCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, List<Dictionary<string, object>> categories, CancellationToken cancellationToken = default)
    {
        var delay = categories.Count * _random.Next(30, 100);
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        return categories;
    }

    public async Task<bool> IsHealthyAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(50, 150), cancellationToken).ConfigureAwait(false);
        return true; // Always healthy for test purposes
    }

    public async Task<BigCommerceApiVersion> DetectApiVersionAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(25, 75), cancellationToken).ConfigureAwait(false);
        return BigCommerceApiVersion.V3; // Always return V3 for test purposes
    }

    public async Task<List<ProductVariantSummary>> GetProductVariantsAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken)
    {
        await Task.Delay(_random.Next(100, 250), cancellationToken).ConfigureAwait(false);
        return new List<ProductVariantSummary>
        {
            new() { Id = 1, ProductId = productId },
            new() { Id = 2, ProductId = productId }
        };
    }

    public async Task<List<ProductImageSummary>> GetProductImagesAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken)
    {
        await Task.Delay(_random.Next(100, 200), cancellationToken).ConfigureAwait(false);
        return new List<ProductImageSummary>
        {
            new() { Id = 1, ProductId = productId },
            new() { Id = 2, ProductId = productId }
        };
    }

    public async Task<List<ProductModifierSummary>> GetProductModifiersAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken)
    {
        await Task.Delay(_random.Next(75, 175), cancellationToken).ConfigureAwait(false);
        return new List<ProductModifierSummary>
        {
            new() { Id = 1, ProductId = productId },
            new() { Id = 2, ProductId = productId }
        };
    }

    public async Task<BigCommercePaginatedResponse<Dictionary<string, object>>> GetPaginatedEntitiesAsync(StoreConfiguration storeConfig, string entityType, BigCommercePaginationRequest paginationRequest, CancellationToken cancellationToken)
    {
        await Task.Delay(_random.Next(150, 300), cancellationToken).ConfigureAwait(false);
        
        return new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = new List<Dictionary<string, object>>(),
            CurrentPage = paginationRequest.Page,
            PerPage = paginationRequest.Limit,
            TotalItems = 1000,
            TotalPages = 20
        };
    }

    public async IAsyncEnumerable<BigCommercePaginatedResponse<Dictionary<string, object>>> GetAllEntitiesPaginatedAsync(StoreConfiguration storeConfig, string entityType, BigCommercePaginationRequest paginationRequest, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int page = 1; page <= 5; page++)
        {
            var pageRequest = new BigCommercePaginationRequest 
            { 
                Page = page, 
                Limit = paginationRequest.Limit 
            };
            yield return await GetPaginatedEntitiesAsync(storeConfig, entityType, pageRequest, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BigCommercePaginatedResponse<ProductSummary>> GetProductPageAsync(StoreConfiguration storeConfig, BigCommercePaginationRequest paginationRequest, CancellationToken cancellationToken)
    {
        await Task.Delay(_random.Next(200, 400), cancellationToken).ConfigureAwait(false);
        return new BigCommercePaginatedResponse<ProductSummary>
        {
            Data = new List<ProductSummary> { new() { Id = 1, Name = "Test Product" } },
            CurrentPage = paginationRequest.Page,
            TotalItems = 1000
        };
    }

    public async Task<BigCommercePaginatedResponse<CategorySummary>> GetCategoryPageAsync(StoreConfiguration storeConfig, BigCommercePaginationRequest paginationRequest, CancellationToken cancellationToken)
    {
        await Task.Delay(_random.Next(100, 250), cancellationToken).ConfigureAwait(false);
        return new BigCommercePaginatedResponse<CategorySummary>
        {
            Data = new List<CategorySummary> { new() { Id = 1, Name = "Test Category" } },
            CurrentPage = paginationRequest.Page,
            TotalItems = 500
        };
    }

    public async Task<BigCommercePaginatedResponse<BrandSummary>> GetBrandPageAsync(StoreConfiguration storeConfig, BigCommercePaginationRequest paginationRequest, CancellationToken cancellationToken)
    {
        await Task.Delay(_random.Next(100, 200), cancellationToken).ConfigureAwait(false);
        return new BigCommercePaginatedResponse<BrandSummary>
        {
            Data = new List<BrandSummary> { new() { Id = 1, Name = "Test Brand" } },
            CurrentPage = paginationRequest.Page,
            TotalItems = 100
        };
    }

    public async Task<int> EstimateEntityCountAsync(StoreConfiguration storeConfig, string entityType, BigCommercePaginationRequest paginationRequest, CancellationToken cancellationToken)
    {
        await Task.Delay(_random.Next(50, 150), cancellationToken).ConfigureAwait(false);
        return entityType switch
        {
            "products" => 10000,
            "categories" => 500,
            "brands" => 100,
            _ => 1000
        };
    }

    #endregion
} 