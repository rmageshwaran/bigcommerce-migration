using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

#pragma warning disable CA2007

namespace BigCommerce.Migration.PerformanceTests;

/// <summary>
/// Performance tests for bulk category processing pipeline
/// Task 3.2.4: Performance validation for 5-10x improvement requirements
/// Validates that bulk processing achieves documented performance targets
/// </summary>
public class BulkCategoryProcessingPerformanceTests
{
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly Mock<IEntityMappingService> _mockMappingService;
    private readonly Mock<IEntityTransformStrategy> _mockTransformStrategy;
    private readonly Mock<ILogger<BulkCategoryTransformService>> _mockTransformLogger;
    private readonly Mock<ILogger<BulkCategoryCreationService>> _mockCreationLogger;

    private readonly BulkCategoryTransformService _transformService;
    private readonly BulkCategoryCreationService _creationService;

    private readonly BatchProcessingRequest _testBatchRequest;

    public BulkCategoryProcessingPerformanceTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _mockMappingService = new Mock<IEntityMappingService>();
        _mockTransformStrategy = new Mock<IEntityTransformStrategy>();
        _mockTransformLogger = new Mock<ILogger<BulkCategoryTransformService>>();
        _mockCreationLogger = new Mock<ILogger<BulkCategoryCreationService>>();

        _transformService = new BulkCategoryTransformService(
            _mockTransformStrategy.Object,
            _mockMappingService.Object,
            _mockTransformLogger.Object);

        _creationService = new BulkCategoryCreationService(
            _mockApiClient.Object,
            _mockMappingService.Object,
            _mockCreationLogger.Object);

        _testBatchRequest = new BatchProcessingRequest
        {
            MigrationId = "perf-test-migration",
            SourceStore = new StoreConfiguration
            {
                StoreId = "perf-source-store",
                AccessToken = "perf-source-token",
                ChannelId = "1"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "perf-dest-store",
                AccessToken = "perf-dest-token",
                ChannelId = "1"
            },
            BatchSize = 25,
            CategoryTreeContext = new CategoryTreeContext
            {
                SourceCategoryTreeId = "1",
                DestinationCategoryTreeId = "1"
            }
        };
    }

    [Fact]
    public async Task BulkCategoryCreation_ShouldAchieve_96PercentApiCallReduction()
    {
        // Arrange - Test documented BigCommerce bulk creation API performance impact
        var categories = CreatePerformanceTestDataset(1000); // 1000 categories
        SetupOptimalBulkCreationPerformance(categories);

        // Act - Measure bulk creation performance
        var stopwatch = Stopwatch.StartNew();
        var result = await _creationService.CreateCategoriesInBulkAsync(
            categories, _testBatchRequest, CancellationToken.None);
        stopwatch.Stop();

        // Assert - Documented performance targets from BigCommerce-Bulk-Creation-API-Discovery.md
        result.Success.Should().BeTrue("Bulk creation should succeed");
        result.TotalCategories.Should().Be(1000, "Should process all 1000 categories");

        // CRITICAL: 96% API call reduction target
        // Before: 1,000 categories = 1,000 API calls
        // After: 1,000 categories ÷ 25 per batch = 40 API calls
        result.TotalApiCalls.Should().BeLessOrEqualTo(40, 
            "Should make ≤40 API calls for 1000 categories (25 per batch)");
        result.ApiCallReductionPercent.Should().BeGreaterOrEqualTo(96.0,
            "Should achieve ≥96% API call reduction as documented");

        // CRITICAL: 25x performance improvement target
        // At 50 req/sec rate limit: 20 seconds → 0.8 seconds = 25x improvement
        result.PerformanceImprovement.Should().BeGreaterOrEqualTo(20.0,
            "Should achieve ≥20x performance improvement");

        // Network overhead reduction validation
        result.NetworkOverheadReduction.Should().BeGreaterOrEqualTo(90.0,
            "Should achieve ≥90% network overhead reduction");

        // Rate limit compliance
        result.RateLimitCompliance.Should().BeTrue(
            "Should maintain rate limit compliance during bulk operations");

        // Processing time target
        result.ProcessingTimeMinutes.Should().BeLessThan(0.5,
            "Should complete 1000 categories in <30 seconds");
    }

    [Fact]
    public async Task BulkProcessingPipeline_ShouldMaintain_OptimalThroughputAtScale()
    {
        // Arrange - Scale test with various dataset sizes
        var testSizes = new[] { 100, 500, 1000, 2000 };
        var throughputResults = new List<(int Size, double ThroughputPerMinute, double ProcessingTimeMinutes)>();

        foreach (var size in testSizes)
        {
            var categories = CreatePerformanceTestDataset(size);
            SetupOptimalBulkCreationPerformance(categories);

            // Act - Measure throughput at different scales
            var stopwatch = Stopwatch.StartNew();
            var result = await _creationService.CreateCategoriesInBulkAsync(
                categories, _testBatchRequest, CancellationToken.None);
            stopwatch.Stop();

            throughputResults.Add((size, result.ThroughputCategoriesPerMinute, result.ProcessingTimeMinutes));

            // Assert - Throughput targets per scale
            result.Success.Should().BeTrue($"Should succeed for {size} categories");
            result.ThroughputCategoriesPerMinute.Should().BeGreaterThan(1000.0,
                $"Should maintain >1000 categories/minute throughput for {size} categories");
        }

        // Validate linear scalability (throughput should remain consistent)
        var throughputVariance = CalculateVariance(throughputResults.Select(r => r.ThroughputPerMinute));
        throughputVariance.Should().BeLessThan(500000.0, // Allow reasonable variance
            "Throughput should scale linearly without significant degradation");

        // Validate processing time scales sub-linearly (efficiency improves with size)
        var largestDataset = throughputResults.Last();
        var smallestDataset = throughputResults.First();
        
        var efficiencyRatio = (largestDataset.Size / (double)smallestDataset.Size) / 
                            (largestDataset.ProcessingTimeMinutes / smallestDataset.ProcessingTimeMinutes);
        
        efficiencyRatio.Should().BeGreaterThan(1.0,
            "Bulk processing should become more efficient with larger datasets");
    }

    [Fact]
    public async Task BulkCategoryTransformation_ShouldMaintain_MemoryEfficiencyAtScale()
    {
        // Arrange - Large dataset for memory efficiency testing
        var categories = CreateLargeHierarchicalDataset(5000); // 5000 categories
        SetupMemoryEfficientTransformation(categories);

        // Act - Monitor memory during large transformation
        var initialMemory = GC.GetTotalMemory(true);
        var result = await _transformService.TransformCategoriesInBulkAsync(
            categories, _testBatchRequest, CancellationToken.None);
        var finalMemory = GC.GetTotalMemory(true);

        // Assert - Memory efficiency targets
        result.Success.Should().BeTrue("Large transformation should succeed");
        result.ProcessedCategories.Should().Be(5000, "All categories should be transformed");

        // Memory usage validation
        result.MemoryUsageMB.Should().BeLessThan(100.0,
            "Memory usage should stay <100MB for 5000 categories");

        // Memory growth should be reasonable
        var memoryGrowthMB = (finalMemory - initialMemory) / (1024.0 * 1024.0);
        memoryGrowthMB.Should().BeLessThan(150.0,
            "Total memory growth should be <150MB for large dataset processing");

        // Throughput validation for large datasets
        result.ThroughputCategoriesPerMinute.Should().BeGreaterThan(2000.0,
            "Should maintain >2000 categories/minute for large transformations");

        // Batch processing efficiency
        result.BatchesProcessed.Should().BeGreaterThan(100, "Should process in many batches");
        var avgCategoriesPerBatch = result.ProcessedCategories / (double)result.BatchesProcessed;
        avgCategoriesPerBatch.Should().BeInRange(20.0, 35.0, "Should maintain optimal batch sizes");
    }

    [Fact]
    public async Task BulkProcessingPipeline_ShouldExceed_BaselineIndividualProcessingPerformance()
    {
        // Arrange - Medium dataset for realistic comparison
        var categories = CreatePerformanceTestDataset(500);
        SetupOptimalBulkCreationPerformance(categories);

        // Simulate baseline individual processing time (100ms per category)
        var baselineIndividualTimeMs = categories.Count * 100.0; // 500 * 100ms = 50 seconds
        var baselineIndividualTimeMinutes = baselineIndividualTimeMs / (60.0 * 1000.0);

        // Act - Measure actual bulk processing performance
        var stopwatch = Stopwatch.StartNew();
        var transformResult = await _transformService.TransformCategoriesInBulkAsync(
            categories, _testBatchRequest, CancellationToken.None);
        var creationResult = await _creationService.CreateCategoriesInBulkAsync(
            transformResult.TransformedCategories, _testBatchRequest, CancellationToken.None);
        stopwatch.Stop();

        var actualPipelineTimeMinutes = stopwatch.Elapsed.TotalMinutes;

        // Assert - Performance improvement validation
        var actualPerformanceImprovement = baselineIndividualTimeMinutes / actualPipelineTimeMinutes;
        
        actualPerformanceImprovement.Should().BeGreaterThan(5.0,
            "Actual measured performance should exceed 5x improvement");
        
        creationResult.PerformanceImprovement.Should().BeInRange(5.0, 15.0,
            "Service-reported performance should be 5-15x improvement");

        // Validate actual vs calculated performance metrics alignment
        var calculatedVsActualDifference = Math.Abs(creationResult.PerformanceImprovement - actualPerformanceImprovement);
        calculatedVsActualDifference.Should().BeLessThan(3.0,
            "Calculated performance metrics should align with actual measurements");

        // API efficiency validation
        creationResult.TotalApiCalls.Should().BeLessOrEqualTo(20,
            "Should make ≤20 API calls for 500 categories");

        // Time efficiency validation
        actualPipelineTimeMinutes.Should().BeLessThan(2.0,
            "Complete pipeline should finish in <2 minutes for 500 categories");
    }

    [Fact]
    public async Task BulkProcessingPipeline_ShouldHandle_ConcurrentBatchProcessingEfficiently()
    {
        // Arrange - Multiple concurrent processing scenarios
        var datasets = new[]
        {
            CreatePerformanceTestDataset(200), // Dataset 1
            CreatePerformanceTestDataset(300), // Dataset 2
            CreatePerformanceTestDataset(250)  // Dataset 3
        };

        SetupConcurrentBulkProcessing(datasets);

        // Act - Process multiple datasets concurrently
        var concurrentTasks = datasets.Select(async dataset =>
        {
            var stopwatch = Stopwatch.StartNew();
            
            var transformResult = await _transformService.TransformCategoriesInBulkAsync(
                dataset, _testBatchRequest, CancellationToken.None);
            
            var creationResult = await _creationService.CreateCategoriesInBulkAsync(
                transformResult.TransformedCategories, _testBatchRequest, CancellationToken.None);
            
            stopwatch.Stop();
            
            return new
            {
                Size = dataset.Count,
                TransformResult = transformResult,
                CreationResult = creationResult,
                TotalTimeMinutes = stopwatch.Elapsed.TotalMinutes
            };
        });

        var results = await Task.WhenAll(concurrentTasks);

        // Assert - Concurrent processing efficiency
        results.Should().AllSatisfy(result =>
        {
            result.TransformResult.Success.Should().BeTrue("Concurrent transformation should succeed");
            result.CreationResult.Success.Should().BeTrue("Concurrent creation should succeed");
            
            // Performance should not degrade significantly under concurrent load
            result.CreationResult.PerformanceImprovement.Should().BeGreaterThan(4.0,
                "Performance should remain >4x even under concurrent load");
            
            // Processing time should remain reasonable
            result.TotalTimeMinutes.Should().BeLessThan(3.0,
                "Each concurrent process should complete in <3 minutes");
        });

        // Validate total concurrent processing efficiency
        var totalCategories = results.Sum(r => r.Size);
        var maxConcurrentTime = results.Max(r => r.TotalTimeMinutes);
        var concurrentThroughput = totalCategories / maxConcurrentTime;

        concurrentThroughput.Should().BeGreaterThan(250.0,
            "Concurrent processing should maintain >250 categories/minute aggregate throughput");
    }

    #region Test Data Creation and Setup

    private List<Dictionary<string, object>> CreatePerformanceTestDataset(int count)
    {
        var categories = new List<Dictionary<string, object>>();
        for (int i = 1; i <= count; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Perf Category {i}",
                ["parent_id"] = i <= 20 ? 0 : (i - 1) / 20, // Create hierarchy
                ["description"] = $"Performance test category {i}",
                ["is_visible"] = true,
                ["sort_order"] = i,
                ["meta_keywords"] = new[] { "performance", "test", $"category{i}" }
            });
        }
        return categories;
    }

    private List<Dictionary<string, object>> CreateLargeHierarchicalDataset(int count)
    {
        var categories = new List<Dictionary<string, object>>();
        
        // Create 10% root categories
        var rootCount = Math.Max(10, count / 10);
        for (int i = 1; i <= rootCount; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Root {i}",
                ["parent_id"] = 0,
                ["description"] = $"Root category {i} for memory testing",
                ["is_visible"] = true,
                ["sort_order"] = i
            });
        }

        // Create remaining as child categories
        for (int i = rootCount + 1; i <= count; i++)
        {
            var parentId = ((i - rootCount - 1) % rootCount) + 1;
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Child {i}",
                ["parent_id"] = parentId,
                ["description"] = $"Child category {i} under parent {parentId}",
                ["is_visible"] = true,
                ["sort_order"] = i
            });
        }

        return categories;
    }

    private void SetupOptimalBulkCreationPerformance(List<Dictionary<string, object>> categories)
    {
        _mockApiClient.Setup(client => client.CreateCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            It.IsAny<string>(),
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreConfiguration store, string treeId, List<Dictionary<string, object>> batch, CancellationToken ct) =>
            {
                // Simulate optimal bulk creation response
                return batch.Select((cat, index) => new Dictionary<string, object>(cat)
                {
                    ["id"] = 10000 + (int)(cat["id"] ?? 0), // New destination IDs
                    ["tree_id"] = int.Parse(treeId)
                }).ToList();
            });

        _mockMappingService.Setup(service => service.StoreEntityMappingAsync(
            It.IsAny<EntityMapping>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupMemoryEfficientTransformation(List<Dictionary<string, object>> categories)
    {
        foreach (var category in categories)
        {
            var transformedCategory = new Dictionary<string, object>(category)
            {
                ["tree_id"] = 1,
                ["url"] = new Dictionary<string, object>
                {
                    ["path"] = $"/{category["name"]?.ToString()?.ToLowerInvariant()?.Replace(" ", "-")}/",
                    ["is_customized"] = false
                }
            };

            _mockTransformStrategy.Setup(strategy => strategy.TransformEntityAsync(
                It.Is<Dictionary<string, object>>(c => c["id"].Equals(category["id"])),
                It.IsAny<string>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<StoreConfiguration>(),
                It.IsAny<CategoryTreeContext>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(transformedCategory);
        }
    }

    private void SetupConcurrentBulkProcessing(List<Dictionary<string, object>>[] datasets)
    {
        var callCount = 0;
        _mockApiClient.Setup(client => client.CreateCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            It.IsAny<string>(),
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreConfiguration store, string treeId, List<Dictionary<string, object>> batch, CancellationToken ct) =>
            {
                var batchOffset = Interlocked.Increment(ref callCount) * 1000;
                
                return batch.Select((cat, index) => new Dictionary<string, object>(cat)
                {
                    ["id"] = batchOffset + (int)(cat["id"] ?? 0),
                    ["tree_id"] = int.Parse(treeId)
                }).ToList();
            });

        // Setup transformation for all datasets
        foreach (var dataset in datasets)
        {
            SetupMemoryEfficientTransformation(dataset);
        }

        _mockMappingService.Setup(service => service.StoreEntityMappingAsync(
            It.IsAny<EntityMapping>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private double CalculateVariance(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        var mean = valuesList.Average();
        return valuesList.Sum(value => Math.Pow(value - mean, 2)) / valuesList.Count;
    }

    #endregion
}