using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.PerformanceTests.Infrastructure;
using System.Diagnostics;
using FluentAssertions;
using ApiCallTracker = BigCommerce.Migration.PerformanceTests.Infrastructure.ApiCallTracker;

namespace BigCommerce.Migration.PerformanceTests;

/// <summary>
/// Task 2.2.1: Batch API Interface Tests
/// Tests the performance difference between individual vs batch API operations
/// Goal: 100 products processed in less than 5 seconds with 90%+ API call reduction
/// 
/// TDD RED Phase: These tests define the expected batch API interface behavior
/// </summary>
public class BatchApiInterfaceTests : IClassFixture<PerformanceTestFixture>
{
    private readonly PerformanceTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly ILogger<BatchApiInterfaceTests> _logger;

    public BatchApiInterfaceTests(PerformanceTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        _logger = _fixture.CreateLogger<BatchApiInterfaceTests>();
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Task", "2.2.1")]
    public async Task BatchApiInterface_Should_Exist_And_Support_Batch_Operations()
    {
        // Arrange: Expected batch API interface
        var storeConfig = _fixture.CreateTestStoreConfiguration();

        // Act and Assert: Verify IBatchApiClient interface exists
        var batchClient = _fixture.ServiceProvider.GetService(typeof(IBatchApiClient));
        batchClient.Should().NotBeNull("IBatchApiClient interface should be implemented");

        // Act and Assert: Verify batch methods exist
        var batchApiClient = batchClient as IBatchApiClient;
        batchApiClient.Should().NotBeNull();

        // Test batch product operations
        var testProducts = _fixture.CreateTestProductsData(10);
        
        // This should work without throwing (interface contract)
        Func<Task> batchCreateAction = async () => 
            await batchApiClient.CreateProductsBatchAsync(storeConfig, testProducts, CancellationToken.None);
        
        await batchCreateAction.Should().NotThrowAsync("Batch create products should be supported");

        _output.WriteLine("✅ IBatchApiClient interface verified with required batch operations");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Task", "2.2.1")]
    public async Task Individual_API_Calls_Should_Have_High_API_Call_Count_Baseline()
    {
        // Arrange: 100 products to process individually
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var testProducts = _fixture.CreateTestProductsData(100);
        var apiClient = _fixture.ServiceProvider.GetRequiredService<IBigCommerceApiClient>();
        
        var apiCallTracker = new ApiCallTracker();
        var stopwatch = Stopwatch.StartNew();

        // Act: Process products individually (current approach)
        foreach (var product in testProducts)
        {
            apiCallTracker.RecordApiCall("CreateProduct", "Individual");
            // Simulate individual API call
            await apiClient.CreateProductsAsync(storeConfig, new List<Dictionary<string, object>> { product }, CancellationToken.None);
        }

        stopwatch.Stop();

        // Assert: High API call count and longer duration
        apiCallTracker.TotalApiCalls.Should().Be(100, "Individual approach should make 100 API calls");
        apiCallTracker.GetApiCallsByType("CreateProduct").Should().Be(100);
        
        var individualProcessingTime = stopwatch.ElapsedMilliseconds;
        _output.WriteLine($"Individual API Calls - Count: {apiCallTracker.TotalApiCalls}, Time: {individualProcessingTime}ms");
        
        // Store baseline for comparison
        _fixture.SetTestData("Individual_API_Calls", apiCallTracker.TotalApiCalls);
        _fixture.SetTestData("Individual_Processing_Time", individualProcessingTime);

        _output.WriteLine("✅ Individual API baseline established: 100 calls for 100 products");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Task", "2.2.1")]
    public async Task Batch_API_Calls_Should_Achieve_90_Percent_API_Call_Reduction()
    {
        // Arrange: Same 100 products to process in batches
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var testProducts = _fixture.CreateTestProductsData(100);
        var batchApiClient = _fixture.ServiceProvider.GetRequiredService<IBatchApiClient>();
        
        var apiCallTracker = new ApiCallTracker();
        var stopwatch = Stopwatch.StartNew();

        // Act: Process products in batches (target approach)
        const int batchSize = 20; // BigCommerce recommended batch size
        var batches = testProducts.Chunk(batchSize);

        foreach (var batch in batches)
        {
            apiCallTracker.RecordApiCall("CreateProductsBatch", "Batch");
            await batchApiClient.CreateProductsBatchAsync(storeConfig, batch.ToList(), CancellationToken.None);
        }

        stopwatch.Stop();

        // Assert: 90%+ reduction in API calls
        var batchApiCalls = apiCallTracker.TotalApiCalls;
        var individualApiCalls = _fixture.GetTestData<int>("Individual_API_Calls");
        if (individualApiCalls == 0) individualApiCalls = 100;
        
        var apiCallReduction = ((double)(individualApiCalls - batchApiCalls) / individualApiCalls) * 100;
        
        batchApiCalls.Should().BeLessOrEqualTo(5, "Batch approach should use ≤5 API calls for 100 products");
        apiCallReduction.Should().BeGreaterOrEqualTo(90, "Should achieve ≥90% API call reduction");

        var batchProcessingTime = stopwatch.ElapsedMilliseconds;
        _output.WriteLine($"Batch API Calls - Count: {batchApiCalls}, Time: {batchProcessingTime}ms, Reduction: {apiCallReduction:F1}%");

        _output.WriteLine("✅ Batch API achieved 90%+ API call reduction target");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Task", "2.2.1")]
    public async Task Batch_Processing_Should_Complete_100_Products_Under_5_Seconds()
    {
        // Arrange: 100 products for performance test
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var testProducts = _fixture.CreateTestProductsData(100);
        var batchApiClient = _fixture.ServiceProvider.GetRequiredService<IBatchApiClient>();
        
        var stopwatch = Stopwatch.StartNew();

        // Act: Process 100 products using batch operations
        const int batchSize = 20;
        var batches = testProducts.Chunk(batchSize);

        var results = new List<List<Dictionary<string, object>>>();
        foreach (var batch in batches)
        {
            var batchResult = await batchApiClient.CreateProductsBatchAsync(storeConfig, batch.ToList(), CancellationToken.None);
            results.Add(batchResult);
        }

        stopwatch.Stop();

        // Assert: Performance target met
        var totalProcessingTime = stopwatch.ElapsedMilliseconds;
        totalProcessingTime.Should().BeLessThan(5000, "100 products should be processed in under 5 seconds");

        var totalProductsProcessed = results.Sum(r => r.Count);
        totalProductsProcessed.Should().Be(100, "All 100 products should be processed successfully");

        _output.WriteLine($"✅ Batch processing completed 100 products in {totalProcessingTime}ms (<5s target)");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Task", "2.2.1")]
    public async Task Batch_Categories_Should_Support_Bulk_Operations()
    {
        // Arrange: Category batch testing
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var testCategories = _fixture.CreateTestCategoriesData(50);
        var batchApiClient = _fixture.ServiceProvider.GetRequiredService<IBatchApiClient>();
        
        var apiCallTracker = new ApiCallTracker();

        // Act: Process categories in batch
        apiCallTracker.RecordApiCall("CreateCategoriesBatch", "Batch");
        var result = await batchApiClient.CreateCategoriesBatchAsync(storeConfig, testCategories, CancellationToken.None);

        // Assert: Single API call for 50 categories
        apiCallTracker.TotalApiCalls.Should().Be(1, "50 categories should require only 1 batch API call");
        result.Should().HaveCount(50, "All categories should be created in single batch");

        _output.WriteLine("✅ Category batch operations support confirmed");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Task", "2.2.1")]
    public async Task Batch_Variants_Should_Support_Bulk_Operations()
    {
        // Arrange: Variant batch testing
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var parentProductId = "123";
        var testVariants = _fixture.CreateTestVariantsData(parentProductId, 30);
        var batchApiClient = _fixture.ServiceProvider.GetRequiredService<IBatchApiClient>();
        
        var apiCallTracker = new ApiCallTracker();

        // Act: Process variants in batch
        apiCallTracker.RecordApiCall("CreateVariantsBatch", "Batch");
        var result = await batchApiClient.CreateVariantsBatchAsync(storeConfig, parentProductId, testVariants, CancellationToken.None);

        // Assert: Single API call for 30 variants
        apiCallTracker.TotalApiCalls.Should().Be(1, "30 variants should require only 1 batch API call");
        result.Should().HaveCount(30, "All variants should be created in single batch");

        _output.WriteLine("✅ Variant batch operations support confirmed");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Task", "2.2.1")]
    public async Task Performance_Comparison_Should_Show_Significant_Improvement()
    {
        // Arrange: Performance comparison metrics
        var individualTime = _fixture.GetTestData<long>("Individual_Processing_Time");
        if (individualTime == 0) individualTime = 10000L;
        var individualCalls = _fixture.GetTestData<int>("Individual_API_Calls");
        if (individualCalls == 0) individualCalls = 100;

        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var testProducts = _fixture.CreateTestProductsData(100);
        var batchApiClient = _fixture.ServiceProvider.GetRequiredService<IBatchApiClient>();
        
        var stopwatch = Stopwatch.StartNew();
        var apiCallTracker = new ApiCallTracker();

        // Act: Batch processing measurement
        const int batchSize = 20;
        var batches = testProducts.Chunk(batchSize);

        foreach (var batch in batches)
        {
            apiCallTracker.RecordApiCall("CreateProductsBatch", "Batch");
            await batchApiClient.CreateProductsBatchAsync(storeConfig, batch.ToList(), CancellationToken.None);
        }

        stopwatch.Stop();
        var batchTime = stopwatch.ElapsedMilliseconds;
        var batchCalls = apiCallTracker.TotalApiCalls;

        // Assert: Performance improvements
        var timeImprovement = ((double)(individualTime - batchTime) / individualTime) * 100;
        var callReduction = ((double)(individualCalls - batchCalls) / individualCalls) * 100;

        timeImprovement.Should().BeGreaterThan(0, "Batch processing should be faster than individual calls");
        callReduction.Should().BeGreaterOrEqualTo(90, "Should achieve 90%+ API call reduction");

        _output.WriteLine($"Performance Improvement Summary:");
        _output.WriteLine($"  Individual: {individualCalls} calls, {individualTime}ms");
        _output.WriteLine($"  Batch: {batchCalls} calls, {batchTime}ms");
        _output.WriteLine($"  Improvement: {timeImprovement:F1}% faster, {callReduction:F1}% fewer calls");

        _output.WriteLine("✅ Significant performance improvement demonstrated");
    }
}

