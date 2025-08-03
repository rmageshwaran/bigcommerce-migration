using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.IntegrationTests.Infrastructure;
using System.Diagnostics;
using FluentAssertions;
using BatchProcessingResult = BigCommerce.Migration.Core.Interfaces.BatchProcessingResult;

namespace BigCommerce.Migration.IntegrationTests;

/// <summary>
/// 🎯 **COMPREHENSIVE STORE INTEGRATION TESTS**
/// Tests real API connectivity, throughput optimizations, and E2E migration flow
/// Using actual BigCommerce store credentials provided by user
/// </summary>
public class ComprehensiveStoreIntegrationTests : IClassFixture<IntegrationTestBase>
{
    private readonly IntegrationTestBase _testBase;
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ComprehensiveStoreIntegrationTests> _logger;

    // 🔑 **REAL STORE CREDENTIALS** (provided by user)
    private static readonly StoreConfiguration SourceStore = new()
    {
        StoreId = "v6q95r5n91",
        AccessToken = "2jxzl0n457l7dbz9jgeo8tzbj6xw6ba", 
        BaseUrl = "https://api.bigcommerce.com"
    };

    private static readonly StoreConfiguration DestinationStore = new()
    {
        StoreId = "in2msaitrc",
        AccessToken = "bntqbbjvnnap8agkdbo5bekqehb473z",
        BaseUrl = "https://api.bigcommerce.com"
    };

    public ComprehensiveStoreIntegrationTests(IntegrationTestBase testBase, ITestOutputHelper output)
    {
        _testBase = testBase;
        _output = output;
        _serviceProvider = _testBase.ServiceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger<ComprehensiveStoreIntegrationTests>>();
    }

    /// <summary>
    /// 🔍 **TEST 1: API Connectivity Validation**
    /// Ensures both source and destination stores are accessible
    /// </summary>
    [Fact]
    public async Task ValidateApiConnectivity_BothStores_ShouldBeAccessible()
    {
        // Arrange
        var apiClient = _serviceProvider.GetRequiredService<IBigCommerceApiClient>();
        _output.WriteLine("🔍 Testing API connectivity to both stores...");

        // Act & Assert - Source Store
        _output.WriteLine($"📡 Testing source store: {SourceStore.StoreId}");
        var sourceResponse = await apiClient.GetAsync(
            "/v3/catalog/categories?limit=1",
            SourceStore.StoreId,
            SourceStore.AccessToken,
            CancellationToken.None);

        sourceResponse.Should().NotBeNull();
        sourceResponse.IsSuccessStatusCode.Should().BeTrue($"Source store API should be accessible. Status: {sourceResponse.StatusCode}");
        _output.WriteLine($"✅ Source store connectivity: SUCCESS ({sourceResponse.StatusCode})");

        // Act & Assert - Destination Store
        _output.WriteLine($"📡 Testing destination store: {DestinationStore.StoreId}");
        var destinationResponse = await apiClient.GetAsync(
            "/v3/catalog/categories?limit=1",
            DestinationStore.StoreId,
            DestinationStore.AccessToken,
            CancellationToken.None);

        destinationResponse.Should().NotBeNull();
        destinationResponse.IsSuccessStatusCode.Should().BeTrue($"Destination store API should be accessible. Status: {destinationResponse.StatusCode}");
        _output.WriteLine($"✅ Destination store connectivity: SUCCESS ({destinationResponse.StatusCode})");

        _output.WriteLine("🎉 Both stores are accessible!");
    }

    /// <summary>
    /// ⚡ **TEST 2: Dynamic Rate Limiting Validation**
    /// Validates Phase 1 optimizations are working correctly
    /// </summary>
    [Fact]
    public async Task ValidateDynamicRateLimiting_WithRealStoreCredentials_ShouldOptimizeRates()
    {
        // Arrange
        var dynamicRateLimiter = _serviceProvider.GetRequiredService<IDynamicRateLimiter>();
        var stopwatch = new Stopwatch();
        _output.WriteLine("⚡ Testing dynamic rate limiting optimization...");

        // Act - Test rate limit detection and optimization
        stopwatch.Start();
        var initialRate = await dynamicRateLimiter.CalculateOptimalRateAsync(SourceStore.StoreId, "categories");
        
        // Simulate API health update (good performance)
        await dynamicRateLimiter.UpdateApiHealthAsync(SourceStore.StoreId, new BigCommerceRateLimitInfo
        {
            RequestsRemaining = 450,
            RequestsQuotaTotal = 500,
            WindowTimeRemaining = TimeSpan.FromSeconds(30),
            WindowSizeInSeconds = 30
        }, responseTimeMs: 150, isSuccessful: true);

        var optimizedRate = await dynamicRateLimiter.CalculateOptimalRateAsync(SourceStore.StoreId, "categories");
        stopwatch.Stop();

        // Assert
        optimizedRate.Should().BeGreaterThan(initialRate, "Rate should improve with good API health");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500, "Rate calculation should be fast");
        
        _output.WriteLine($"✅ Initial rate: {initialRate} req/sec");
        _output.WriteLine($"✅ Optimized rate: {optimizedRate} req/sec"); 
        _output.WriteLine($"✅ Improvement: {(optimizedRate / Math.Max(initialRate, 1)):F2}x");
        _output.WriteLine($"✅ Calculation time: {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 🚀 **TEST 3: Parallel Processing Validation**
    /// Validates Phase 2 parallel optimizations are working
    /// </summary>
    [Fact]
    public async Task ValidateParallelProcessing_WithMockBatches_ShouldShowPerformanceGains()
    {
        // Arrange
        var parallelProcessor = _serviceProvider.GetRequiredService<IEnhancedParallelProcessor>();
        var mockBatches = CreateMockBatches(10); // Create 10 test batches
        var stopwatch = new Stopwatch();
        _output.WriteLine("🚀 Testing parallel processing optimization...");

        var config = new ParallelProcessingConfiguration
        {
            MaxConcurrentBatches = 5,
            StoreId = SourceStore.StoreId,
            EntityType = "categories",
            EnableSignalRUpdates = false // Disable for test
        };

        // Act - Test parallel processing
        stopwatch.Start();
        var result = await parallelProcessor.ProcessBatchesInParallelAsync(
            mockBatches,
            MockBatchProcessor,
            config,
            cancellationToken: CancellationToken.None);
        stopwatch.Stop();

        // Assert
        result.Should().NotBeNull();
        result.TotalBatchesProcessed.Should().Be(10, "All batches should be processed");
        result.SuccessfulBatches.Should().Be(10, "All batches should succeed");
        result.FailedBatches.Should().Be(0, "No batches should fail");
        result.PeakConcurrency.Should().BeGreaterThan(1, "Parallel processing should use multiple threads");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000, "Parallel processing should be fast");

        _output.WriteLine($"✅ Batches processed: {result.TotalBatchesProcessed}");
        _output.WriteLine($"✅ Peak concurrency: {result.PeakConcurrency}");
        _output.WriteLine($"✅ Processing time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"✅ Throughput: {result.OverallThroughput:F2} batches/sec");
    }

    /// <summary>
    /// 🏗️ **TEST 4: Category Discovery Integration**
    /// Tests actual category discovery from source store
    /// </summary>
    [Fact]
    public async Task ValidateCategoryDiscovery_FromSourceStore_ShouldReturnCategories()
    {
        // Arrange
        var entityFetchService = _serviceProvider.GetRequiredService<IEntityFetchService>();
        _output.WriteLine("🏗️ Testing category discovery from source store...");

        // Act - Discover categories from source store
        var categories = await entityFetchService.FetchEntitiesAsync(
            new List<string>(), // Empty list means discover all
            "test-migration-001",
            "categories",
            SourceStore,
            null,
            CancellationToken.None);

        // Assert
        categories.Should().NotBeNull();
        categories.Should().NotBeEmpty("Source store should have categories");
        categories.Count.Should().BeLessThan(50, "Limit discovery for testing");

        _output.WriteLine($"✅ Categories discovered: {categories.Count}");
        
        // Log first few categories for validation
        for (int i = 0; i < Math.Min(3, categories.Count); i++)
        {
            var category = categories[i];
            if (category.ContainsKey("id") && category.ContainsKey("name"))
            {
                _output.WriteLine($"✅ Category {i + 1}: ID={category["id"]}, Name={category["name"]}");
            }
        }
    }

    /// <summary>
    /// 📊 **TEST 5: Performance Comparison**
    /// Compares sequential vs parallel processing performance
    /// </summary>
    [Fact]
    public async Task ValidatePerformanceImprovement_ParallelVsSequential_ShouldShowGains()
    {
        // Arrange
        var parallelProcessor = _serviceProvider.GetRequiredService<IEnhancedParallelProcessor>();
        var mockBatches = CreateMockBatches(20);
        _output.WriteLine("📊 Comparing sequential vs parallel performance...");

        var config = new ParallelProcessingConfiguration
        {
            MaxConcurrentBatches = 1, // Sequential
            StoreId = SourceStore.StoreId,
            EntityType = "categories",
            EnableSignalRUpdates = false
        };

        // Act - Sequential processing
        var sequentialStopwatch = Stopwatch.StartNew();
        var sequentialResult = await parallelProcessor.ProcessBatchesInParallelAsync(
            mockBatches,
            MockBatchProcessor,
            config,
            cancellationToken: CancellationToken.None);
        sequentialStopwatch.Stop();

        // Act - Parallel processing
        config.MaxConcurrentBatches = 8; // Parallel
        var parallelStopwatch = Stopwatch.StartNew();
        var parallelResult = await parallelProcessor.ProcessBatchesInParallelAsync(
            mockBatches,
            MockBatchProcessor,
            config,
            cancellationToken: CancellationToken.None);
        parallelStopwatch.Stop();

        // Assert
        var improvementFactor = (double)sequentialStopwatch.ElapsedMilliseconds / parallelStopwatch.ElapsedMilliseconds;
        improvementFactor.Should().BeGreaterThan(1.5, "Parallel should be at least 1.5x faster");

        _output.WriteLine($"✅ Sequential time: {sequentialStopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"✅ Parallel time: {parallelStopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"✅ Performance improvement: {improvementFactor:F2}x");
        _output.WriteLine($"✅ Sequential throughput: {sequentialResult.OverallThroughput:F2} batches/sec");
        _output.WriteLine($"✅ Parallel throughput: {parallelResult.OverallThroughput:F2} batches/sec");
    }

    /// <summary>
    /// 🎯 **TEST 6: End-to-End Integration Smoke Test**
    /// Tests the complete flow without actual data migration
    /// </summary>
    [Fact]
    public async Task ValidateEndToEndIntegration_WithoutDataMigration_ShouldCompleteSuccessfully()
    {
        // Arrange
        var apiClient = _serviceProvider.GetRequiredService<IBigCommerceApiClient>();
        var dynamicRateLimiter = _serviceProvider.GetRequiredService<IDynamicRateLimiter>();
        var parallelProcessor = _serviceProvider.GetRequiredService<IEnhancedParallelProcessor>();
        _output.WriteLine("🎯 Running end-to-end integration smoke test...");

        // Act & Assert - Step 1: API Health Check
        _output.WriteLine("Step 1: API health check...");
        var sourceHealth = await apiClient.GetAsync("/v3/time", SourceStore.StoreId, SourceStore.AccessToken, CancellationToken.None);
        sourceHealth.IsSuccessStatusCode.Should().BeTrue("Source store should be healthy");

        var destHealth = await apiClient.GetAsync("/v3/time", DestinationStore.StoreId, DestinationStore.AccessToken, CancellationToken.None);
        destHealth.IsSuccessStatusCode.Should().BeTrue("Destination store should be healthy");

        // Act & Assert - Step 2: Rate Limit Optimization
        _output.WriteLine("Step 2: Rate limit optimization...");
        var optimalRate = await dynamicRateLimiter.CalculateOptimalRateAsync(SourceStore.StoreId, "categories");
        optimalRate.Should().BeGreaterThan(0, "Should calculate optimal rate");

        // Act & Assert - Step 3: Parallel Processing Readiness
        _output.WriteLine("Step 3: Parallel processing validation...");
        var testBatches = CreateMockBatches(5);
        var config = new ParallelProcessingConfiguration 
        { 
            MaxConcurrentBatches = 3,
            StoreId = SourceStore.StoreId,
            EntityType = "categories",
            EnableSignalRUpdates = false
        };

        var parallelResult = await parallelProcessor.ProcessBatchesInParallelAsync(
            testBatches,
            MockBatchProcessor,
            config,
            cancellationToken: CancellationToken.None);

        parallelResult.IsSuccess.Should().BeTrue("Parallel processing should succeed");

        _output.WriteLine($"✅ E2E Smoke Test: PASSED");
        _output.WriteLine($"✅ Source store: Healthy");
        _output.WriteLine($"✅ Destination store: Healthy");
        _output.WriteLine($"✅ Rate optimization: {optimalRate} req/sec");
        _output.WriteLine($"✅ Parallel processing: {parallelResult.PeakConcurrency}x concurrency");
    }

    #region Helper Methods

    /// <summary>
    /// Creates mock batches for testing parallel processing
    /// </summary>
    private static List<BatchProcessingRequest> CreateMockBatches(int count)
    {
        var batches = new List<BatchProcessingRequest>();
        for (int i = 1; i <= count; i++)
        {
            batches.Add(new BatchProcessingRequest
            {
                MigrationId = "test-migration-001",
                EntityType = "categories",
                BatchNumber = i,
                TotalBatches = count,
                EntityIds = new List<string> { $"test-entity-{i}" },
                SourceStore = SourceStore,
                DestinationStore = DestinationStore
            });
        }
        return batches;
    }

    /// <summary>
    /// Mock batch processor for testing (simulates processing delay)
    /// </summary>
    private static async Task<BatchProcessingResult> MockBatchProcessor(
        BatchProcessingRequest batch, 
        CancellationToken cancellationToken)
    {
        // Simulate processing time (faster than real processing)
        await Task.Delay(Random.Shared.Next(50, 150), cancellationToken);

        return new BatchProcessingResult
        {
            BatchNumber = batch.BatchNumber,
            TotalProcessed = batch.EntityIds?.Count ?? 0,
            SuccessfulEntities = batch.EntityIds?.Count ?? 0,
            FailedEntities = 0,
            ProcessingTime = TimeSpan.FromMilliseconds(100),
            Errors = new List<string>()
        };
    }

    #endregion
} 