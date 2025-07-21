using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.PerformanceTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Baselines;

/// <summary>
/// TDD RED PHASE: API Performance Baseline Tests
/// These tests establish current API performance baselines that we'll improve upon
/// Tests will initially fail because we need to implement test infrastructure
/// </summary>
public class ApiPerformanceBaselineTests
{
    private readonly ITestOutputHelper _output;
    private readonly PerformanceBenchmark _benchmark;

    public ApiPerformanceBaselineTests(ITestOutputHelper output)
    {
        _output = output;
        _benchmark = new PerformanceBenchmark();
    }

    [Fact]
    public async Task Current_API_Client_Single_Product_Fetch_Performance_Baseline()
    {
        // ARRANGE
        var apiClient = CreateTestApiClient();
        
        // ACT - Measure current API client performance
        var result = await _benchmark.MeasureAsync(async () =>
        {
            var storeConfig = CreateTestStoreConfiguration();
            var products = await apiClient.GetProductsAsync(storeConfig, 1, 1).ConfigureAwait(false);
            GC.KeepAlive(products);
        }).ConfigureAwait(false);
        
        // ASSERT - Document current performance (will be improved later)
        Assert.True(result.ElapsedMilliseconds > 0);
        Assert.True(result.ElapsedMilliseconds < 10000); // Should complete within 10 seconds
        Assert.True(result.MemoryBefore > 0);
        
        // Log baseline metrics for comparison
        _output.WriteLine("=== API CLIENT BASELINE PERFORMANCE ===");
        _output.WriteLine($"Single Product Fetch Time: {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Memory Before: {result.MemoryBefore:N0} bytes");
        _output.WriteLine($"Memory After: {result.MemoryAfter:N0} bytes");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
        _output.WriteLine($"Start Time: {result.StartTime:HH:mm:ss.fff}");
        _output.WriteLine($"End Time: {result.EndTime:HH:mm:ss.fff}");
        
        // Store baseline for future comparison
        await SavePerformanceBaseline("api_single_product_fetch", result).ConfigureAwait(false);
    }

    [Fact]
    public async Task Current_API_Client_Multiple_Products_Fetch_Performance()
    {
        // ARRANGE
        var apiClient = CreateTestApiClient();
        var productIds = new[] { "product-1", "product-2", "product-3", "product-4", "product-5" };
        
        // ACT - Measure sequential API calls (current approach)
        var result = await _benchmark.MeasureAsync(async () =>
        {
            var storeConfig = CreateTestStoreConfiguration();
            foreach (var productId in productIds)
            {
                var products = await apiClient.GetProductsAsync(storeConfig, 1, 1).ConfigureAwait(false);
                GC.KeepAlive(products);
            }
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(result.ElapsedMilliseconds > 0);
        var averageTimePerCall = result.ElapsedMilliseconds / productIds.Length;
        
        _output.WriteLine("=== MULTIPLE PRODUCTS BASELINE (Sequential) ===");
        _output.WriteLine($"Total Time for {productIds.Length} products: {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Average Time per Product: {averageTimePerCall:F1}ms");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
        _output.WriteLine($"Throughput: {productIds.Length / (result.ElapsedMilliseconds / 1000.0):F1} products/second");
        
        await SavePerformanceBaseline("api_sequential_products_fetch", result).ConfigureAwait(false);
    }

    [Fact]
    public async Task Current_API_Client_Categories_Fetch_Performance()
    {
        // ARRANGE
        var apiClient = CreateTestApiClient();
        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = 50,
            SortBy = "id",
            SortDirection = "asc"
        };
        
        // ACT - Measure categories API call
        var result = await _benchmark.MeasureAsync(async () =>
        {
            var storeConfig = CreateTestStoreConfiguration();
            await apiClient.GetCategoriesAsync(storeConfig, "tree-123").ConfigureAwait(false);
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(result.ElapsedMilliseconds > 0);
        
        _output.WriteLine("=== CATEGORIES FETCH BASELINE ===");
        _output.WriteLine($"Categories Fetch Time (50 items): {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
        
        await SavePerformanceBaseline("api_categories_fetch", result).ConfigureAwait(false);
    }

    [Fact]
    public async Task Current_API_Client_Large_Response_Memory_Usage()
    {
        // ARRANGE
        var apiClient = CreateTestApiClient();
        
        // ACT - Measure memory usage with larger API response
        var result = await _benchmark.MeasureAsync(async () =>
        {
            var storeConfig = CreateTestStoreConfiguration();
            // Simulate fetching a product with many variants and images
            var products = await apiClient.GetProductsAsync(storeConfig, 1, 1).ConfigureAwait(false);
            var variants = await apiClient.GetProductVariantsAsync(storeConfig, 123, CancellationToken.None).ConfigureAwait(false);
            var images = await apiClient.GetProductImagesAsync(storeConfig, 123, CancellationToken.None).ConfigureAwait(false);
            var modifiers = await apiClient.GetProductModifiersAsync(storeConfig, 123, CancellationToken.None).ConfigureAwait(false);
            
            // Keep references to measure actual memory usage
            GC.KeepAlive(products);
            GC.KeepAlive(variants);
            GC.KeepAlive(images);
            GC.KeepAlive(modifiers);
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(result.MemoryUsed >= 0); // May be negative due to GC, but should be tracked
        
        _output.WriteLine("=== LARGE RESPONSE MEMORY USAGE ===");
        _output.WriteLine($"Complex Product Fetch Time: {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
        _output.WriteLine($"Memory Efficiency: {result.MemoryUsed / result.ElapsedMilliseconds:F1} bytes/ms");
        
        await SavePerformanceBaseline("api_large_response_memory", result).ConfigureAwait(false);
    }

    [Fact]
    public async Task Current_API_Client_Concurrent_Requests_Performance()
    {
        // ARRANGE
        var apiClient = CreateTestApiClient();
        var productIds = new[] { "prod-1", "prod-2", "prod-3", "prod-4", "prod-5" };
        
        // ACT - Measure parallel API calls (if supported)
        var result = await _benchmark.MeasureAsync(async () =>
        {
            var storeConfig = CreateTestStoreConfiguration();
            var tasks = productIds.Select(async productId => 
            {
                var products = await apiClient.GetProductsAsync(storeConfig, 1, 1).ConfigureAwait(false);
                return products;
            });
            
            var allProducts = await Task.WhenAll(tasks).ConfigureAwait(false);
            GC.KeepAlive(allProducts);
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(result.ElapsedMilliseconds > 0);
        var averageTimePerCall = result.ElapsedMilliseconds / productIds.Length;
        
        _output.WriteLine("=== CONCURRENT REQUESTS BASELINE ===");
        _output.WriteLine($"Parallel Time for {productIds.Length} products: {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Average Time per Product: {averageTimePerCall:F1}ms");
        _output.WriteLine($"Concurrent Throughput: {productIds.Length / (result.ElapsedMilliseconds / 1000.0):F1} products/second");
        
        await SavePerformanceBaseline("api_concurrent_requests", result).ConfigureAwait(false);
    }

    [Fact]
    public async Task API_Performance_Comparison_Sequential_vs_Concurrent()
    {
        // ARRANGE
        var apiClient = CreateTestApiClient();
        var productIds = new[] { "prod-1", "prod-2", "prod-3" };
        
        // ACT - Measure both approaches
        var storeConfig = CreateTestStoreConfiguration();
        
        var sequentialResult = await _benchmark.MeasureAsync(async () =>
        {
            foreach (var productId in productIds)
            {
                var products = await apiClient.GetProductsAsync(storeConfig, 1, 1).ConfigureAwait(false);
                GC.KeepAlive(products);
            }
        }).ConfigureAwait(false);
        
        var concurrentResult = await _benchmark.MeasureAsync(async () =>
        {
            var tasks = productIds.Select(async productId => 
            {
                var products = await apiClient.GetProductsAsync(storeConfig, 1, 1).ConfigureAwait(false);
                return products;
            });
            var allProducts = await Task.WhenAll(tasks).ConfigureAwait(false);
            GC.KeepAlive(allProducts);
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(sequentialResult.ElapsedMilliseconds > 0);
        Assert.True(concurrentResult.ElapsedMilliseconds > 0);
        
        var improvement = concurrentResult.GetImprovementOver(sequentialResult);
        
        _output.WriteLine("=== SEQUENTIAL vs CONCURRENT COMPARISON ===");
        _output.WriteLine($"Sequential Time: {sequentialResult.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Concurrent Time: {concurrentResult.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Performance Improvement: {improvement}");
        _output.WriteLine($"Speed Multiplier: {sequentialResult.ElapsedMilliseconds / concurrentResult.ElapsedMilliseconds:F1}x");
        _output.WriteLine($"Sequential Memory: {sequentialResult.MemoryUsed:N0} bytes");
        _output.WriteLine($"Concurrent Memory: {concurrentResult.MemoryUsed:N0} bytes");
        
        // Document which approach is faster
        if (concurrentResult.IsFasterThan(sequentialResult))
        {
            _output.WriteLine("✅ Concurrent approach is faster");
        }
        else
        {
            _output.WriteLine("⚠️ Sequential approach is faster (possible bottleneck)");
        }
    }

    #region Helper Methods (GREEN phase implementation)

    /// <summary>
    /// Creates a test API client for performance measurement
    /// </summary>
    private IBigCommerceApiClient CreateTestApiClient()
    {
        return new TestBigCommerceApiClient();
    }

    /// <summary>
    /// Creates a test store configuration for API calls
    /// </summary>
    private StoreConfiguration CreateTestStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "test-store-123",
            AccessToken = "test-access-token",
            ChannelId = "1",
            BaseUrl = "https://api.bigcommerce.com"
        };
    }

    /// <summary>
    /// Saves performance baseline for future comparison
    /// For now, just logs the results - could be extended to save to file/database
    /// </summary>
    private async Task SavePerformanceBaseline(string testName, PerformanceResult result)
    {
        // For now, just log baseline data for future reference
        await Task.Delay(1).ConfigureAwait(false); // Simulate async operation
        
        _output.WriteLine($"=== BASELINE SAVED: {testName} ===");
        _output.WriteLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        _output.WriteLine($"Execution Time: {result.ElapsedMilliseconds:F1}ms");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
        _output.WriteLine($"Start: {result.StartTime:HH:mm:ss.fff}");
        _output.WriteLine($"End: {result.EndTime:HH:mm:ss.fff}");
        _output.WriteLine($"Baseline Data: {result}");
        _output.WriteLine("=====================================");
        
        // Future enhancement: Save to JSON file or database for historical comparison
        // var baselineData = new 
        // {
        //     TestName = testName,
        //     Timestamp = DateTime.UtcNow,
        //     Performance = result
        // };
        // await File.WriteAllTextAsync($"baselines/{testName}.json", JsonSerializer.Serialize(baselineData));
    }

    #endregion
} 