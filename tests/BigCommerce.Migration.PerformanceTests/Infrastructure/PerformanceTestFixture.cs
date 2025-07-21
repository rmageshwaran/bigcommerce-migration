using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.PerformanceTests.Infrastructure;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// Performance test fixture providing test infrastructure and services
/// Used across batch API interface tests for TDD development
/// </summary>
public class PerformanceTestFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, object> _testData;

    public PerformanceTestFixture()
    {
        _testData = new ConcurrentDictionary<string, object>();
        
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Service provider for dependency injection in tests
    /// </summary>
    public IServiceProvider ServiceProvider => _serviceProvider;

    /// <summary>
    /// Configures services for performance testing
    /// </summary>
    private void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Core interfaces - use test implementations
        services.AddSingleton<IBigCommerceApiClient, TestBigCommerceApiClient>();
        
        // API request handler for batch client (using test implementation)
        services.AddSingleton<IApiRequestHandler, TestApiRequestHandler>();
        
        // Real batch API client implementation
        services.AddSingleton<IBatchApiClient, BatchApiClient>();

        // API call tracker for performance monitoring
        services.AddSingleton<ApiCallTracker>();

        // Other test services
        services.AddSingleton<IEntityMappingService, TestEntityMappingService>();
        services.AddSingleton<ITestEntityProcessor, TestEntityProcessor>();
    }

    /// <summary>
    /// Creates a logger for the specified type
    /// </summary>
    public ILogger<T> CreateLogger<T>()
    {
        return _serviceProvider.GetRequiredService<ILogger<T>>();
    }

    /// <summary>
    /// Creates test store configuration for performance tests
    /// </summary>
    public StoreConfiguration CreateTestStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "test-store-123",
            AccessToken = "test-token-456",
            BaseUrl = "https://api.bigcommerce.com",
            ChannelId = "1"
        };
    }

    /// <summary>
    /// Creates test product data for performance testing (convenience method)
    /// </summary>
    public List<Dictionary<string, object>> CreateTestProducts(int count) => CreateTestProductsData(count);

    /// <summary>
    /// Creates test product data for performance testing
    /// </summary>
    public List<Dictionary<string, object>> CreateTestProductsData(int count)
    {
        var products = new List<Dictionary<string, object>>();
        var random = new Random(42); // Fixed seed for reproducible tests

        for (int i = 1; i <= count; i++)
        {
            var product = new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Performance Test Product {i}",
                ["sku"] = $"PERF-{i:D6}",
                ["type"] = "physical",
                ["weight"] = Math.Round(random.NextDouble() * 10, 2),
                ["price"] = Math.Round(random.NextDouble() * 100 + 10, 2),
                ["description"] = $"Test product {i} for batch API performance testing. " +
                                $"This product contains realistic data to simulate actual API payloads. " +
                                $"Generated for performance benchmarking purposes.",
                ["is_visible"] = true,
                ["availability"] = "available",
                ["categories"] = new[] { random.Next(1, 10), random.Next(10, 20) },
                ["brand_id"] = random.Next(1, 50),
                ["inventory_level"] = random.Next(0, 1000),
                ["custom_url"] = new Dictionary<string, object>
                {
                    ["url"] = $"/performance-test-product-{i}/",
                    ["is_customized"] = false
                },
                ["meta_description"] = $"Meta description for performance test product {i}",
                ["meta_keywords"] = new[] { $"test{i}", "performance", "product" },
                ["images"] = CreateProductImages(i, 3),
                ["variants"] = CreateProductVariants(i, random.Next(2, 6)),
                ["custom_fields"] = CreateCustomFields(i, 2)
            };
            
            products.Add(product);
        }

        return products;
    }

    /// <summary>
    /// Creates test category data for performance testing (convenience method)
    /// </summary>
    public List<Dictionary<string, object>> CreateTestCategories(int count) => CreateTestCategoriesData(count);

    /// <summary>
    /// Creates test category data for performance testing
    /// </summary>
    public List<Dictionary<string, object>> CreateTestCategoriesData(int count)
    {
        var categories = new List<Dictionary<string, object>>();
        var random = new Random(42);

        for (int i = 1; i <= count; i++)
        {
            var category = new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Performance Test Category {i}",
                ["description"] = $"Test category {i} for batch API performance testing.",
                ["parent_id"] = i > 10 ? random.Next(1, 10) : 0, // Some root, some child categories
                ["sort_order"] = i,
                ["is_visible"] = true,
                ["page_title"] = $"Performance Test Category {i}",
                ["meta_keywords"] = new[] { $"category{i}", "test", "performance" },
                ["meta_description"] = $"Meta description for test category {i}",
                ["custom_url"] = new Dictionary<string, object>
                {
                    ["url"] = $"/performance-test-category-{i}/",
                    ["is_customized"] = false
                },
                ["image"] = new Dictionary<string, object>
                {
                    ["image_url"] = $"https://test.example.com/category-{i}.jpg",
                    ["description"] = $"Image for category {i}"
                }
            };
            
            categories.Add(category);
        }

        return categories;
    }

    /// <summary>
    /// Creates test brand data for performance testing (convenience method)
    /// </summary>
    public List<Dictionary<string, object>> CreateTestBrands(int count) => CreateTestBrandsData(count);

    /// <summary>
    /// Creates test brand data for performance testing
    /// </summary>
    public List<Dictionary<string, object>> CreateTestBrandsData(int count)
    {
        var brands = new List<Dictionary<string, object>>();
        var random = new Random(42);

        for (int i = 1; i <= count; i++)
        {
            var brand = new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Performance Test Brand {i}",
                ["page_title"] = $"Brand {i} - Performance Testing",
                ["meta_keywords"] = new[] { $"brand{i}", "test", "performance" },
                ["meta_description"] = $"Meta description for test brand {i}",
                ["image_url"] = $"https://test.example.com/brand-{i}.jpg",
                ["search_keywords"] = $"brand{i} test performance keyword{i}",
                ["custom_url"] = new Dictionary<string, object>
                {
                    ["url"] = $"/performance-test-brand-{i}/",
                    ["is_customized"] = false
                }
            };
            
            brands.Add(brand);
        }

        return brands;
    }

    /// <summary>
    /// Creates test variant data for performance testing
    /// </summary>
    public List<Dictionary<string, object>> CreateTestVariantsData(string productId, int count)
    {
        var variants = new List<Dictionary<string, object>>();
        var random = new Random(42);

        for (int i = 1; i <= count; i++)
        {
            var variant = new Dictionary<string, object>
            {
                ["id"] = $"{productId}-variant-{i}",
                ["product_id"] = productId,
                ["sku"] = $"VAR-{productId}-{i:D3}",
                ["price"] = Math.Round(random.NextDouble() * 50 + 10, 2),
                ["weight"] = Math.Round(random.NextDouble() * 5, 2),
                ["inventory_level"] = random.Next(0, 100),
                ["option_values"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["option_display_name"] = "Size",
                        ["label"] = $"Size {i}",
                        ["option_id"] = 1,
                        ["id"] = i
                    },
                    new Dictionary<string, object>
                    {
                        ["option_display_name"] = "Color",
                        ["label"] = $"Color {i}",
                        ["option_id"] = 2,
                        ["id"] = i + 100
                    }
                },
                ["image_url"] = $"https://test.example.com/variant-{productId}-{i}.jpg"
            };
            
            variants.Add(variant);
        }

        return variants;
    }

    /// <summary>
    /// Creates product images for test data
    /// </summary>
    private List<Dictionary<string, object>> CreateProductImages(int productId, int count)
    {
        var images = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= count; i++)
        {
            images.Add(new Dictionary<string, object>
            {
                ["image_url"] = $"https://test.example.com/product-{productId}-image-{i}.jpg",
                ["description"] = $"Image {i} for product {productId}",
                ["sort_order"] = i,
                ["is_thumbnail"] = i == 1
            });
        }
        
        return images;
    }

    /// <summary>
    /// Creates product variants for test data
    /// </summary>
    private List<Dictionary<string, object>> CreateProductVariants(int productId, int count)
    {
        var variants = new List<Dictionary<string, object>>();
        var random = new Random(productId); // Product-specific seed for consistency
        
        for (int i = 1; i <= count; i++)
        {
            variants.Add(new Dictionary<string, object>
            {
                ["sku"] = $"VAR-{productId}-{i:D3}",
                ["price"] = Math.Round(random.NextDouble() * 20 + 5, 2),
                ["inventory_level"] = random.Next(0, 50),
                ["option_values"] = new[] { i, i + 100 } // Simple option value IDs
            });
        }
        
        return variants;
    }

    /// <summary>
    /// Creates custom fields for test data
    /// </summary>
    private List<Dictionary<string, object>> CreateCustomFields(int entityId, int count)
    {
        var customFields = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= count; i++)
        {
            customFields.Add(new Dictionary<string, object>
            {
                ["name"] = $"custom_field_{i}",
                ["value"] = $"Custom value {i} for entity {entityId}"
            });
        }
        
        return customFields;
    }

    /// <summary>
    /// Stores test data for sharing between test methods
    /// </summary>
    public void SetTestData<T>(string key, T value)
    {
        _testData.AddOrUpdate(key, value!, (k, v) => value!);
    }

    /// <summary>
    /// Retrieves test data shared between test methods
    /// </summary>
    public T? GetTestData<T>(string key)
    {
        return _testData.TryGetValue(key, out var value) && value is T typedValue 
            ? typedValue 
            : default;
    }

    /// <summary>
    /// Disposes the service provider
    /// </summary>
    public void Dispose()
    {
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}



/// <summary>
/// Test implementation of IApiRequestHandler for performance testing
/// Simulates realistic API responses without making actual HTTP calls
/// </summary>
internal class TestApiRequestHandler : IApiRequestHandler
{
    private readonly Random _random = new(42); // Fixed seed for consistent tests

    public async Task<T> ExecuteRequestAsync<T>(ApiRequest request, CancellationToken cancellationToken = default)
    {
        // Simulate network latency
        await Task.Delay(_random.Next(50, 200), cancellationToken).ConfigureAwait(false);

        // Simulate realistic API responses based on the request type
        if (typeof(T) == typeof(Dictionary<string, object>))
        {
            var response = CreateMockBatchResponse(request);
            return (T)(object)response;
        }

        throw new NotSupportedException($"Response type {typeof(T)} not supported in test implementation");
    }

    public async Task<string> ExecuteRequestAsync(ApiRequest request, CancellationToken cancellationToken = default)
    {
        // Simulate network latency
        await Task.Delay(_random.Next(50, 200), cancellationToken).ConfigureAwait(false);

        var response = CreateMockBatchResponse(request);
        return JsonSerializer.Serialize(response);
    }

    public async Task<T> ExecuteGetRequestAsync<T>(string url, StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        var request = ApiRequest.CreateGet(url, storeConfig);
        return await ExecuteRequestAsync<T>(request, cancellationToken);
    }

    public async Task<T> ExecutePostRequestAsync<T>(string url, string content, StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        var request = ApiRequest.CreatePost(url, content, storeConfig);
        return await ExecuteRequestAsync<T>(request, cancellationToken);
    }

    /// <summary>
    /// Creates a mock BigCommerce API response based on the request
    /// </summary>
    private Dictionary<string, object> CreateMockBatchResponse(ApiRequest request)
    {
        // Parse the request content to determine how many entities to return
        var entityCount = 1;
        if (!string.IsNullOrEmpty(request.Content))
        {
            try
            {
                var entities = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(request.Content);
                entityCount = entities?.Count ?? 1;
            }
            catch
            {
                // Fallback to single entity if parsing fails
                entityCount = 1;
            }
        }

        // Create mock response data
        var responseData = new List<Dictionary<string, object>>();
        for (int i = 1; i <= entityCount; i++)
        {
            responseData.Add(CreateMockEntity(i, request.Url));
        }

        return new Dictionary<string, object>
        {
            ["data"] = responseData,
            ["meta"] = new Dictionary<string, object>
            {
                ["pagination"] = new Dictionary<string, object>
                {
                    ["total"] = entityCount,
                    ["count"] = entityCount,
                    ["per_page"] = entityCount,
                    ["current_page"] = 1,
                    ["total_pages"] = 1
                }
            }
        };
    }

    /// <summary>
    /// Creates a mock entity based on the entity type inferred from the URL
    /// </summary>
    private Dictionary<string, object> CreateMockEntity(int id, string url)
    {
        if (url.Contains("/products", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["name"] = $"Batch Test Product {id}",
                ["sku"] = $"BATCH-{id:D6}",
                ["price"] = Math.Round(_random.NextDouble() * 100 + 10, 2),
                ["weight"] = Math.Round(_random.NextDouble() * 5, 2),
                ["is_visible"] = true,
                ["date_created"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["date_modified"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        if (url.Contains("/categories", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["name"] = $"Batch Test Category {id}",
                ["description"] = $"Test category {id} created via batch API",
                ["sort_order"] = id,
                ["is_visible"] = true
            };
        }

        if (url.Contains("/variants", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["sku"] = $"VAR-BATCH-{id:D3}",
                ["price"] = Math.Round(_random.NextDouble() * 50 + 5, 2),
                ["inventory_level"] = _random.Next(0, 100)
            };
        }

        if (url.Contains("/brands", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["name"] = $"Batch Test Brand {id}",
                ["page_title"] = $"Brand {id}",
                ["meta_keywords"] = new[] { $"brand{id}", "test", "batch" }
            };
        }

        // Generic entity fallback
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["name"] = $"Test Entity {id}",
            ["created_at"] = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Helper class for tracking API call patterns in performance tests
/// Used to measure API call reduction and efficiency improvements
/// </summary>
public class ApiCallTracker
{
    private readonly List<(string Operation, string Type, DateTime Timestamp)> _apiCalls = new();

    /// <summary>
    /// Records an API call for tracking purposes
    /// </summary>
    public void RecordApiCall(string operation, string type)
    {
        _apiCalls.Add((operation, type, DateTime.UtcNow));
    }

    /// <summary>
    /// Gets the total number of API calls recorded
    /// </summary>
    public int TotalApiCalls => _apiCalls.Count;

    /// <summary>
    /// Gets the number of API calls for a specific operation
    /// </summary>
    public int GetApiCallsByType(string operation)
    {
        return _apiCalls.Count(call => call.Operation == operation);
    }

    /// <summary>
    /// Gets the number of API calls for a specific category
    /// </summary>
    public int GetApiCallsByCategory(string type)
    {
        return _apiCalls.Count(call => call.Type == type);
    }

    /// <summary>
    /// Resets the API call tracking for new test scenarios
    /// </summary>
    public void Reset()
    {
        _apiCalls.Clear();
    }

    /// <summary>
    /// Gets all recorded API calls for detailed analysis
    /// </summary>
    public IReadOnlyList<(string Operation, string Type, DateTime Timestamp)> GetAllCalls()
    {
        return _apiCalls.AsReadOnly();
    }
} 