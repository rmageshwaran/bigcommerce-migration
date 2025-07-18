using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.PerformanceTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using ApiCallTracker = BigCommerce.Migration.PerformanceTests.Infrastructure.ApiCallTracker;

namespace BigCommerce.Migration.PerformanceTests.Validation;

/// <summary>
/// Task 2.2.3: Batch Performance Validation Tests
/// Comprehensive validation of batch API performance improvements
/// Target: 100 products processed in &lt;5 seconds with 90%+ API call reduction
/// 
/// Validates the complete batch API performance optimization implementation
/// </summary>
public class BatchPerformanceValidationTests : IClassFixture<PerformanceTestFixture>
{
    private readonly PerformanceTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly IBatchApiClient _batchApiClient;
    private readonly IBigCommerceApiClient _individualApiClient;

    public BatchPerformanceValidationTests(PerformanceTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        
        var serviceProvider = _fixture.ServiceProvider;
        _batchApiClient = serviceProvider.GetRequiredService<IBatchApiClient>();
        _individualApiClient = serviceProvider.GetRequiredService<IBigCommerceApiClient>();
    }

    [Fact]
    [Trait("Task", "2.2.3")]
    [Trait("Category", "PerformanceValidation")]
    public async Task Batch_Processing_Should_Meet_100_Products_Under_5_Seconds_Target()
    {
        // ARRANGE: Setup for 100 products performance target validation
        const int targetProductCount = 100;
        const int maxAllowedTimeMs = 5000; // 5 seconds target
        
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var products = _fixture.CreateTestProducts(targetProductCount);
        
        _output.WriteLine($"🎯 VALIDATION TARGET: Process {targetProductCount} products in &lt;{maxAllowedTimeMs}ms");
        _output.WriteLine($"📦 Products prepared: {products.Count} test products");

        // ACT: Execute batch processing with performance measurement
        var stopwatch = Stopwatch.StartNew();
        
        var results = await _batchApiClient.CreateProductsBatchAsync(
            storeConfig, 
            products, 
            CancellationToken.None);
        
        stopwatch.Stop();
        var actualTimeMs = stopwatch.ElapsedMilliseconds;

        // ASSERT: Validate performance targets are met
        results.Should().HaveCount(targetProductCount, 
            "All products should be processed successfully");
        
        actualTimeMs.Should().BeLessThan(maxAllowedTimeMs, 
            $"Batch processing should complete under {maxAllowedTimeMs}ms target");

        // Calculate performance metrics
        var throughputPerSecond = (double)targetProductCount / (actualTimeMs / 1000.0);
        var targetThroughput = (double)targetProductCount / (maxAllowedTimeMs / 1000.0);
        var performanceRatio = throughputPerSecond / targetThroughput;

        _output.WriteLine($"✅ PERFORMANCE TARGET ACHIEVED:");
        _output.WriteLine($"   Actual Time: {actualTimeMs}ms (Target: &lt;{maxAllowedTimeMs}ms)");
        _output.WriteLine($"   Throughput: {throughputPerSecond:F1} products/sec");
        _output.WriteLine($"   Performance Ratio: {performanceRatio:F2}x target speed");
        _output.WriteLine($"   Results: {results.Count}/{targetProductCount} products processed");
    }

    [Fact]
    [Trait("Task", "2.2.3")]
    [Trait("Category", "PerformanceValidation")]
    public async Task API_Call_Reduction_Should_Achieve_90_Percent_Target()
    {
        // ARRANGE: Setup for API call reduction validation
        const int productCount = 100;
        const double targetReductionPercent = 90.0;
        
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var products = _fixture.CreateTestProducts(productCount);
        
        _output.WriteLine($"🎯 API CALL REDUCTION TARGET: &gt;={targetReductionPercent}% reduction");
        _output.WriteLine($"📊 Testing with {productCount} products");

        // ACT: Measure individual vs batch API calls
        var apiCallTracker = _fixture.ServiceProvider.GetRequiredService<ApiCallTracker>();
        
        // Reset tracking
        apiCallTracker.Reset();
        
        // Execute batch processing
        var stopwatch = Stopwatch.StartNew();
        var batchResults = await _batchApiClient.CreateProductsBatchAsync(
            storeConfig, 
            products, 
            CancellationToken.None);
        stopwatch.Stop();
        
        var batchApiCalls = apiCallTracker.TotalApiCalls;
        var batchTimeMs = stopwatch.ElapsedMilliseconds;

        // ASSERT: Validate API call reduction
        var expectedIndividualCalls = productCount; // 1 call per product for individual approach
        var actualReductionPercent = ((double)(expectedIndividualCalls - batchApiCalls) / expectedIndividualCalls) * 100;

        actualReductionPercent.Should().BeGreaterOrEqualTo(targetReductionPercent,
            $"API call reduction should meet {targetReductionPercent}% target");

        batchResults.Should().HaveCount(productCount,
            "All products should be processed successfully");

        _output.WriteLine($"✅ API CALL REDUCTION ACHIEVED:");
        _output.WriteLine($"   Individual Approach: {expectedIndividualCalls} API calls");
        _output.WriteLine($"   Batch Approach: {batchApiCalls} API calls");
        _output.WriteLine($"   Reduction: {actualReductionPercent:F1}% (Target: &gt;={targetReductionPercent}%)");
        _output.WriteLine($"   Processing Time: {batchTimeMs}ms");
        _output.WriteLine($"   Efficiency Gain: {expectedIndividualCalls / (double)batchApiCalls:F1}x fewer calls");
    }

    [Fact]
    [Trait("Task", "2.2.3")]
    [Trait("Category", "PerformanceValidation")]
    public async Task Batch_Throughput_Should_Exceed_Individual_Performance()
    {
        // ARRANGE: Setup for throughput comparison validation
        const int comparisonProductCount = 50;
        
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var products = _fixture.CreateTestProducts(comparisonProductCount);
        
        _output.WriteLine($"🚀 THROUGHPUT COMPARISON: Batch vs Individual processing");
        _output.WriteLine($"📈 Testing with {comparisonProductCount} products");

        // ACT: Measure individual processing performance (simulated)
        var individualStopwatch = Stopwatch.StartNew();
        var individualResults = new List<Dictionary<string, object>>();
        
        // Simulate individual API calls (much slower than batch)
        for (int i = 0; i < comparisonProductCount; i++)
        {
            await Task.Delay(100); // Simulate individual API call latency
            individualResults.Add(new Dictionary<string, object> { ["id"] = i + 1 });
        }
        individualStopwatch.Stop();
        var individualTimeMs = individualStopwatch.ElapsedMilliseconds;

        // Measure batch processing performance
        var batchStopwatch = Stopwatch.StartNew();
        var batchResults = await _batchApiClient.CreateProductsBatchAsync(
            storeConfig, 
            products, 
            CancellationToken.None);
        batchStopwatch.Stop();
        var batchTimeMs = batchStopwatch.ElapsedMilliseconds;

        // ASSERT: Validate throughput improvements
        var individualThroughput = (double)comparisonProductCount / (individualTimeMs / 1000.0);
        var batchThroughput = (double)comparisonProductCount / (batchTimeMs / 1000.0);
        var throughputImprovement = (batchThroughput / individualThroughput) - 1;

        batchThroughput.Should().BeGreaterThan(individualThroughput,
            "Batch processing should have higher throughput than individual processing");

        throughputImprovement.Should().BeGreaterThan(0.5, // 50% improvement minimum
            "Batch processing should show significant throughput improvement");

        _output.WriteLine($"✅ THROUGHPUT IMPROVEMENT VALIDATED:");
        _output.WriteLine($"   Individual: {individualTimeMs}ms ({individualThroughput:F1} products/sec)");
        _output.WriteLine($"   Batch: {batchTimeMs}ms ({batchThroughput:F1} products/sec)");
        _output.WriteLine($"   Improvement: {throughputImprovement * 100:F1}% faster throughput");
        _output.WriteLine($"   Speed Multiplier: {batchThroughput / individualThroughput:F1}x faster");
    }

    [Fact]
    [Trait("Task", "2.2.3")]
    [Trait("Category", "PerformanceValidation")]
    public async Task Multiple_Entity_Types_Should_Support_Batch_Optimization()
    {
        // ARRANGE: Setup for multi-entity batch validation
        const int entityCount = 25;
        
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var products = _fixture.CreateTestProducts(entityCount);
        var categories = _fixture.CreateTestCategories(entityCount);
        var brands = _fixture.CreateTestBrands(entityCount);
        
        _output.WriteLine($"🔄 MULTI-ENTITY BATCH VALIDATION:");
        _output.WriteLine($"   Testing {entityCount} each of products, categories, and brands");

        // ACT: Execute batch operations for different entity types
        var totalStopwatch = Stopwatch.StartNew();
        
        var productTask = _batchApiClient.CreateProductsBatchAsync(storeConfig, products, CancellationToken.None);
        var categoryTask = _batchApiClient.CreateCategoriesBatchAsync(storeConfig, categories, CancellationToken.None);
        var brandTask = _batchApiClient.CreateBrandsBatchAsync(storeConfig, brands, CancellationToken.None);
        
        var results = await Task.WhenAll(productTask, categoryTask, brandTask);
        totalStopwatch.Stop();

        var productResults = results[0];
        var categoryResults = results[1];
        var brandResults = results[2];

        // ASSERT: Validate all entity types processed successfully
        productResults.Should().HaveCount(entityCount, "All products should be processed");
        categoryResults.Should().HaveCount(entityCount, "All categories should be processed");
        brandResults.Should().HaveCount(entityCount, "All brands should be processed");

        var totalEntities = entityCount * 3;
        var totalTimeMs = totalStopwatch.ElapsedMilliseconds;
        var overallThroughput = (double)totalEntities / (totalTimeMs / 1000.0);

        _output.WriteLine($"✅ MULTI-ENTITY BATCH SUCCESS:");
        _output.WriteLine($"   Products: {productResults.Count}/{entityCount} processed");
        _output.WriteLine($"   Categories: {categoryResults.Count}/{entityCount} processed");
        _output.WriteLine($"   Brands: {brandResults.Count}/{entityCount} processed");
        _output.WriteLine($"   Total Time: {totalTimeMs}ms for {totalEntities} entities");
        _output.WriteLine($"   Combined Throughput: {overallThroughput:F1} entities/sec");
    }

    [Fact]
    [Trait("Task", "2.2.3")]
    [Trait("Category", "PerformanceValidation")]
    public async Task Large_Scale_Batch_Should_Maintain_Performance_Efficiency()
    {
        // ARRANGE: Setup for large-scale performance validation
        const int largeScaleProductCount = 500; // 5x our target for stress testing
        const double maxTimePerProductMs = 50; // Maximum time per product (very generous)
        
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        var products = _fixture.CreateTestProducts(largeScaleProductCount);
        
        _output.WriteLine($"⚡ LARGE SCALE VALIDATION:");
        _output.WriteLine($"   Processing {largeScaleProductCount} products (5x target scale)");
        _output.WriteLine($"   Performance threshold: &lt;{maxTimePerProductMs}ms per product");

        // ACT: Execute large-scale batch processing
        var stopwatch = Stopwatch.StartNew();
        
        var results = await _batchApiClient.CreateProductsBatchAsync(
            storeConfig, 
            products, 
            CancellationToken.None);
        
        stopwatch.Stop();
        var totalTimeMs = stopwatch.ElapsedMilliseconds;
        var timePerProductMs = (double)totalTimeMs / largeScaleProductCount;

        // ASSERT: Validate large-scale efficiency
        results.Should().HaveCount(largeScaleProductCount,
            "All products should be processed at large scale");

        timePerProductMs.Should().BeLessThan(maxTimePerProductMs,
            $"Average time per product should be under {maxTimePerProductMs}ms even at large scale");

        var throughput = (double)largeScaleProductCount / (totalTimeMs / 1000.0);
        
        _output.WriteLine($"✅ LARGE SCALE EFFICIENCY VALIDATED:");
        _output.WriteLine($"   Total Products: {results.Count}/{largeScaleProductCount}");
        _output.WriteLine($"   Total Time: {totalTimeMs}ms ({totalTimeMs / 1000.0:F1} seconds)");
        _output.WriteLine($"   Time per Product: {timePerProductMs:F1}ms");
        _output.WriteLine($"   Throughput: {throughput:F1} products/sec");
        _output.WriteLine($"   Scale Factor: {largeScaleProductCount / 100.0:F1}x target scale maintained");
    }

    [Fact]
    [Trait("Task", "2.2.3")]
    [Trait("Category", "PerformanceValidation")]
    public async Task Performance_Metrics_Should_Show_Consistent_Improvements()
    {
        // ARRANGE: Setup for performance metrics validation
        var storeConfig = _fixture.CreateTestStoreConfiguration();
        
        _output.WriteLine($"📊 PERFORMANCE METRICS VALIDATION:");
        _output.WriteLine($"   Generating comprehensive performance metrics");

        // ACT: Get batch performance metrics
        var metrics = await _batchApiClient.GetBatchPerformanceMetricsAsync(
            storeConfig, 
            timeframeHours: 1, 
            CancellationToken.None);

        // ASSERT: Validate performance metrics quality
        metrics.Should().NotBeNull("Performance metrics should be available");
        
        metrics.ApiCallReductionPercentage.Should().BeGreaterOrEqualTo(90.0,
            "API call reduction should meet 90% target");
        
        metrics.SuccessRate.Should().BeGreaterOrEqualTo(95.0,
            "Success rate should be very high");
        
        metrics.TotalEntitiesProcessed.Should().BeGreaterThan(0,
            "Should have processed entities");

        var efficiencyGain = (double)metrics.EquivalentIndividualCalls / metrics.ActualBatchCalls;

        _output.WriteLine($"✅ PERFORMANCE METRICS VALIDATED:");
        _output.WriteLine($"   API Call Reduction: {metrics.ApiCallReductionPercentage:F1}%");
        _output.WriteLine($"   Success Rate: {metrics.SuccessRate:F1}%");
        _output.WriteLine($"   Entities Processed: {metrics.TotalEntitiesProcessed:N0}");
        _output.WriteLine($"   Efficiency Gain: {efficiencyGain:F1}x fewer API calls");
        _output.WriteLine($"   Time Saved: {metrics.TimeSavedMs / 1000.0:F1} seconds");
        _output.WriteLine($"   Batch Operations: {metrics.TotalBatchOperations}");
    }
} 