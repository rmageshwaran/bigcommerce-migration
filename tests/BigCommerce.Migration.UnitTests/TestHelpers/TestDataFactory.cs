using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.TestHelpers;

/// <summary>
/// Factory class for creating consistent test data across all test files
/// Provides standard test configurations, requests, and mock data
/// </summary>
public static class TestDataFactory
{
    /// <summary>
    /// Creates a valid source store configuration for testing
    /// </summary>
    public static StoreConfiguration CreateSourceStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "v6q95r5n91",
            AccessToken = "source-test-token-123",
            ChannelId = "1",
            BaseUrl = "https://api.bigcommerce.com"
        };
    }

    /// <summary>
    /// Creates a valid destination store configuration for testing
    /// </summary>
    public static StoreConfiguration CreateDestinationStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "in2msaitrc",
            AccessToken = "dest-test-token-456",
            ChannelId = "2",
            BaseUrl = "https://api.bigcommerce.com"
        };
    }

    /// <summary>
    /// Creates a custom store configuration for testing
    /// </summary>
    public static StoreConfiguration CreateStoreConfiguration(
        string storeId, 
        string accessToken, 
        string channelId, 
        string? baseUrl = null)
    {
        return new StoreConfiguration
        {
            StoreId = storeId,
            AccessToken = accessToken,
            ChannelId = channelId,
            BaseUrl = baseUrl ?? "https://api.bigcommerce.com"
        };
    }

    /// <summary>
    /// Creates a valid migration request for testing
    /// </summary>
    public static MigrationRequest CreateMigrationRequest(
        StoreConfiguration? sourceStore = null,
        StoreConfiguration? destinationStore = null,
        List<string>? entities = null,
        MigrationSettings? settings = null)
    {
        return new MigrationRequest
        {
            SourceStore = sourceStore ?? CreateSourceStoreConfiguration(),
            DestinationStore = destinationStore ?? CreateDestinationStoreConfiguration(),
            Entities = entities ?? new List<string> { "categories", "products" },
            Settings = settings
        };
    }

    /// <summary>
    /// Creates migration settings for testing
    /// </summary>
    public static MigrationSettings CreateMigrationSettings(
        int maxApiCallsPerSecond = 12,
        bool enableAdaptiveBatching = true,
        string logLevel = "INFO",
        int requestTimeoutSeconds = 30,
        int maxRetries = 3)
    {
        return new MigrationSettings
        {
            MaxApiCallsPerSecond = maxApiCallsPerSecond,
            EnableAdaptiveBatching = enableAdaptiveBatching,
            LogLevel = logLevel,
            RequestTimeoutSeconds = requestTimeoutSeconds,
            MaxRetries = maxRetries
        };
    }

    /// <summary>
    /// Creates a BigCommerce global configuration for testing
    /// </summary>
    public static BigCommerceConfiguration CreateBigCommerceConfiguration(
        string baseUrl = "https://api.bigcommerce.com",
        int requestTimeoutSeconds = 30,
        int maxRetries = 3,
        int rateLimitRequestsPerSecond = 12,
        bool enableDebugLogging = false,
        string userAgent = "BigCommerce-Migration-System/1.0-Test")
    {
        return new BigCommerceConfiguration
        {
            BaseUrl = baseUrl,
            RequestTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds),
            MaxRetries = maxRetries,
            RateLimitRequestsPerSecond = rateLimitRequestsPerSecond,
            EnableDebugLogging = enableDebugLogging,
            UserAgent = userAgent
        };
    }

    /// <summary>
    /// Creates mock category tree data for API responses
    /// </summary>
    public static List<Dictionary<string, object>> CreateMockCategoryTrees(string channelId, string treeId = "1")
    {
        // Create the data as JSON and parse it back to simulate real API response
        var jsonData = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new
            {
                id = treeId,
                name = "Test Category Tree",
                channels = new[]
                {
                    new { channel_id = channelId }
                }
            }
        });
        
        var jsonDocument = System.Text.Json.JsonDocument.Parse(jsonData);
        var result = new List<Dictionary<string, object>>();
        
        foreach (var element in jsonDocument.RootElement.EnumerateArray())
        {
            var dict = new Dictionary<string, object>();
            foreach (var property in element.EnumerateObject())
            {
                dict[property.Name] = property.Value.Clone();
            }
            result.Add(dict);
        }
        
        return result;
    }

    /// <summary>
    /// Creates mock category data for API responses
    /// </summary>
    public static List<Dictionary<string, object>> CreateMockCategories(int count = 2)
    {
        var categories = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= count; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Test Category {i}",
                ["parent_id"] = i == 1 ? 0 : 1
            });
        }
        
        return categories;
    }

    /// <summary>
    /// Creates mock product data for API responses
    /// </summary>
    public static List<Dictionary<string, object>> CreateMockProducts(int count = 2)
    {
        var products = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= count; i++)
        {
            products.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Test Product {i}",
                ["price"] = 19.99 + i,
                ["weight"] = 1.0 + i * 0.5,
                ["sku"] = $"SKU-{i:D4}"
            });
        }
        
        return products;
    }

    /// <summary>
    /// Creates a CategoryTreeContext for testing
    /// </summary>
    public static CategoryTreeContext CreateCategoryTreeContext(
        string sourceChannelId = "1",
        string destinationChannelId = "2",
        string sourceCategoryTreeId = "tree-1",
        string destinationCategoryTreeId = "tree-2")
    {
        return new CategoryTreeContext
        {
            SourceChannelId = sourceChannelId,
            DestinationChannelId = destinationChannelId,
            SourceCategoryTreeId = sourceCategoryTreeId,
            DestinationCategoryTreeId = destinationCategoryTreeId
        };
    }
} 