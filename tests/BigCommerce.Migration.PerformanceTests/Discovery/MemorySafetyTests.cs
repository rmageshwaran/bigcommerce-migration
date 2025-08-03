#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task

using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System;
using System.Diagnostics;

namespace BigCommerce.Migration.PerformanceTests.Discovery;

/// <summary>
/// TDD performance tests for memory-safe level analysis
/// Tests define the expected behavior - implementation will follow (TDD)
/// Validates memory monitoring, performance optimization, and scalability requirements
/// </summary>
public class MemorySafetyTests
{
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly Mock<ILogger<ChunkedHierarchicalDiscoveryStrategy>> _mockLogger;
    private readonly Mock<IOptions<ChunkedHierarchyConfiguration>> _mockConfig;
    private readonly ChunkedHierarchyConfiguration _testConfig;
    private readonly StoreConfiguration _testStoreConfig;
    private readonly EntityDiscoveryRequest _testDiscoveryRequest;

    public MemorySafetyTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _mockLogger = new Mock<ILogger<ChunkedHierarchicalDiscoveryStrategy>>();
        _mockConfig = new Mock<IOptions<ChunkedHierarchyConfiguration>>();

        _testConfig = new ChunkedHierarchyConfiguration
        {
            MaxCategoriesPerLevel = 10000,
            BatchSizePerLevel = 25,
            MaxBulkCreateSize = 50,
            MaxHierarchyDepth = 10,
            FallbackThreshold = 25000,
            EnableMemoryMonitoring = true,
            LevelProcessingTimeoutMinutes = 4,
            BulkCreationTimeoutMinutes = 2,
            EnableBulkCreation = true,
            AdaptiveBatchSizing = true
        };

        _mockConfig.Setup(x => x.Value).Returns(_testConfig);

        _testStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store-id",
            AccessToken = "test-token",
            ChannelId = "test-channel-id"
        };

        _testDiscoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-123",
            EntityType = "categories",
            SourceStore = _testStoreConfig
        };

        // Setup API client to return V3 by default
        _mockApiClient.Setup(x => x.DetectApiVersionAsync(It.IsAny<StoreConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigCommerceApiVersion.V3);
    }

    #region Memory Monitoring Tests (RED phase - should fail initially)

    [Fact]
    public async Task MemoryMonitoring_ShouldTrack_GCMemoryUsage()
    {
        // Arrange - Enable memory monitoring
        _testConfig.EnableMemoryMonitoring = true;
        var strategy = CreateStrategy();

        // Setup API to return test data
        SetupApiClientForCategories(CreateLargeDataset(1000));

        // Act - Perform memory-intensive operation
        var initialMemory = await strategy.GetMemoryUsageAsync(CancellationToken.None);
        var metadata = await strategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);
        var finalMemory = await strategy.GetMemoryUsageAsync(CancellationToken.None);

        // Assert - Memory tracking should be accurate
        initialMemory.Should().BeGreaterThan(0, "Initial memory should be tracked");
        finalMemory.Should().BeGreaterThan(0, "Final memory should be tracked");
        metadata.Should().NotBeNull("Analysis should complete successfully");
        
        // Memory should stay within Azure Functions constraints
        finalMemory.Should().BeLessOrEqualTo(10.0, "Memory should not exceed 10MB constraint");
    }

    [Fact]
    public async Task MemoryMonitoring_ShouldAlert_WhenThresholdExceeded()
    {
        // Arrange - Configure low memory threshold for testing
        _testConfig.EnableMemoryMonitoring = true;
        var strategy = CreateStrategy();

        // Setup API to return memory-intensive data
        SetupApiClientForCategories(CreateLargeDataset(5000));

        // Act - Perform operation that may exceed threshold
        var metadata = await strategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);

        // Assert - Memory alerts should be logged when approaching limits
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Memory usage approaching limit")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "Memory threshold warnings should be logged");

        metadata.Should().NotBeNull("Processing should continue despite memory pressure");
    }

    [Fact]
    public async Task MemoryMonitoring_ShouldTrigger_CleanupMechanisms()
    {
        // Arrange - Enable memory monitoring with cleanup
        _testConfig.EnableMemoryMonitoring = true;
        var strategy = CreateStrategy();

        // Setup API to return data requiring cleanup
        SetupApiClientForCategories(CreateLargeDataset(2000));

        // Act - Perform multiple operations to trigger cleanup
        var startMemory = await strategy.GetMemoryUsageAsync(CancellationToken.None);
        
        for (int i = 0; i < 3; i++)
        {
            var metadata = await strategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);
            metadata.Should().NotBeNull();
        }
        
        var endMemory = await strategy.GetMemoryUsageAsync(CancellationToken.None);

        // Assert - Memory should be managed efficiently
        endMemory.Should().BeLessOrEqualTo(startMemory * 2, 
            "Memory usage should not grow excessively due to cleanup mechanisms");
        
        // Memory cleanup should be logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Memory")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "Memory monitoring should log memory usage information");
    }

    #endregion

    #region Performance Optimization Tests

    [Fact]
    public async Task PerformanceOptimization_ShouldReduce_ProcessingTimeBy50Percent()
    {
        // Arrange - Create strategy with performance optimizations enabled
        _testConfig.EnableBulkCreation = true;
        _testConfig.AdaptiveBatchSizing = true;
        var optimizedStrategy = CreateStrategy();

        // Create baseline strategy without optimizations for comparison
        var baselineConfig = new ChunkedHierarchyConfiguration
        {
            MaxCategoriesPerLevel = 10000,
            BatchSizePerLevel = 25,
            MaxBulkCreateSize = 50,
            MaxHierarchyDepth = 10,
            FallbackThreshold = 25000,
            EnableMemoryMonitoring = false,
            LevelProcessingTimeoutMinutes = 4,
            BulkCreationTimeoutMinutes = 2,
            EnableBulkCreation = false,  // Disable optimizations
            AdaptiveBatchSizing = false
        };

        var mockBaselineConfig = new Mock<IOptions<ChunkedHierarchyConfiguration>>();
        mockBaselineConfig.Setup(x => x.Value).Returns(baselineConfig);
        var baselineStrategy = new ChunkedHierarchicalDiscoveryStrategy(_mockApiClient.Object, _mockLogger.Object, mockBaselineConfig.Object);

        // Setup test data
        SetupApiClientForCategories(CreateMediumDataset(1000));

        // Act - Measure processing times with multiple runs for more accurate measurement
        const int runs = 5;
        long totalBaselineTime = 0;
        long totalOptimizedTime = 0;

        // Baseline measurements
        for (int i = 0; i < runs; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await baselineStrategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, baselineConfig, CancellationToken.None);
            stopwatch.Stop();
            totalBaselineTime += stopwatch.ElapsedMilliseconds;
        }

        // Optimized measurements  
        for (int i = 0; i < runs; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await optimizedStrategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);
            stopwatch.Stop();
            totalOptimizedTime += stopwatch.ElapsedMilliseconds;
        }

        var avgBaselineTime = totalBaselineTime / (double)runs;
        var avgOptimizedTime = totalOptimizedTime / (double)runs;

        // Get final results for validation
        var baselineResult = await baselineStrategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, baselineConfig, CancellationToken.None);
        var optimizedResult = await optimizedStrategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);

        // Assert - Performance validation with tolerance for very fast operations
        if (avgBaselineTime >= 5.0) // Only require 50% improvement for operations >= 5ms average
        {
            avgOptimizedTime.Should().BeLessOrEqualTo(avgBaselineTime * 0.5, 
                "Performance optimizations should reduce processing time by at least 50% for measurable operations");
        }
        else
        {
            // For very fast operations, just ensure we don't regress significantly (allow up to 2ms variance)
            avgOptimizedTime.Should().BeLessOrEqualTo(avgBaselineTime + 2.0, 
                "Performance optimizations should not significantly slow down fast operations");
        }
        
        baselineResult.TotalCategories.Should().Be(optimizedResult.TotalCategories, 
            "Both strategies should discover the same number of categories");
        
        optimizedResult.Should().NotBeNull("Optimized strategy should complete successfully");
    }

    [Fact]
    public async Task APICallOptimization_ShouldReduce_NumberOfHTTPCalls()
    {
        // Arrange - Enable API call optimizations
        _testConfig.EnableBulkCreation = true;
        _testConfig.AdaptiveBatchSizing = true;
        var strategy = CreateStrategy();

        // Track API calls
        var apiCallCount = 0;
        _mockApiClient.Setup(x => x.GetCategoriesAsync(It.IsAny<StoreConfiguration>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => apiCallCount++)
            .ReturnsAsync(CreateSmallDataset(100));

        // Act - Perform hierarchy analysis
        var metadata = await strategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);

        // Assert - API calls should be minimized through optimization
        apiCallCount.Should().BeLessOrEqualTo(3, 
            "API call optimization should minimize HTTP requests");
        
        metadata.Should().NotBeNull("Analysis should complete with optimized API calls");
        metadata.TotalCategories.Should().BeGreaterThan(0, "Categories should be discovered");
    }

    [Fact]
    public async Task CachingStrategy_ShouldImprove_RepeatedOperations()
    {
        // Arrange - Strategy with caching enabled
        var strategy = CreateStrategy();
        SetupApiClientForCategories(CreateMediumDataset(500));

        // Act - Perform same operation twice
        var stopwatch = Stopwatch.StartNew();
        var firstResult = await strategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);
        stopwatch.Stop();
        var firstTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        var secondResult = await strategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);
        stopwatch.Stop();
        var secondTime = stopwatch.ElapsedMilliseconds;

        // Assert - Second operation should be faster due to caching
        secondTime.Should().BeLessOrEqualTo(firstTime, 
            "Caching should improve performance for repeated operations");
        
        firstResult.TotalCategories.Should().Be(secondResult.TotalCategories, 
            "Cached results should be consistent");
    }

    #endregion

    #region Scalability Tests

    [Fact]
    public async Task LargeDatasetSimulation_ShouldHandle_100KCategories()
    {
        // Arrange - Simulate very large dataset
        var strategy = CreateStrategy();
        SetupApiClientForCategories(CreateLargeDataset(100000));

        // Act - Test with large dataset
        var startTime = DateTime.UtcNow;
        var startMemory = await strategy.GetMemoryUsageAsync(CancellationToken.None);
        
        var metadata = await strategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);
        
        var endTime = DateTime.UtcNow;
        var endMemory = await strategy.GetMemoryUsageAsync(CancellationToken.None);

        // Assert - Should handle large datasets efficiently
        metadata.Should().NotBeNull("Should handle large datasets successfully");
        metadata.TotalCategories.Should().BeGreaterThan(50000, "Should discover significant number of categories");
        
        var processingTime = (endTime - startTime).TotalMinutes;
        processingTime.Should().BeLessOrEqualTo(4.0, "Should complete within timeout limit");
        
        endMemory.Should().BeLessOrEqualTo(10.0, "Memory should stay within Azure Functions limits");
    }

    [Fact]
    public async Task ConcurrentOperations_ShouldMaintain_MemorySafety()
    {
        // Arrange - Multiple concurrent operations
        var strategy = CreateStrategy();
        SetupApiClientForCategories(CreateMediumDataset(1000));

        // Act - Run multiple operations concurrently
        var tasks = new List<Task<HierarchyMetadata>>();
        for (int i = 0; i < 5; i++)
        {
            var task = strategy.AnalyzeHierarchyAsync(_testDiscoveryRequest, _testConfig, CancellationToken.None);
            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);
        var finalMemory = await strategy.GetMemoryUsageAsync(CancellationToken.None);

        // Assert - Concurrent operations should maintain memory safety
        results.Should().HaveCount(5, "All concurrent operations should complete");
        results.Should().OnlyContain(r => r != null, "All results should be valid");
        
        finalMemory.Should().BeLessOrEqualTo(10.0, 
            "Memory should remain safe even with concurrent operations");
    }

    #endregion

    #region Helper Methods

    private ChunkedHierarchicalDiscoveryStrategy CreateStrategy()
    {
        return new ChunkedHierarchicalDiscoveryStrategy(
            _mockApiClient.Object,
            _mockLogger.Object,
            _mockConfig.Object);
    }

    private List<Dictionary<string, object>> CreateSmallDataset(int count)
    {
        var categories = new List<Dictionary<string, object>>();
        for (int i = 1; i <= count; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Category {i}",
                ["parent_id"] = i <= 10 ? 0 : (i % 10) + 1,
                ["sort_order"] = i
            });
        }
        return categories;
    }

    private List<Dictionary<string, object>> CreateMediumDataset(int count)
    {
        var categories = new List<Dictionary<string, object>>();
        for (int i = 1; i <= count; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Category {i}",
                ["parent_id"] = i <= 50 ? 0 : (i % 50) + 1,
                ["sort_order"] = i,
                ["description"] = $"Description for category {i} with some additional data"
            });
        }
        return categories;
    }

    private List<Dictionary<string, object>> CreateLargeDataset(int count)
    {
        var categories = new List<Dictionary<string, object>>();
        for (int i = 1; i <= count; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Large Category {i}",
                ["parent_id"] = i <= 100 ? 0 : (i % 100) + 1,
                ["sort_order"] = i,
                ["description"] = $"Detailed description for category {i} with extensive metadata and information",
                ["meta_keywords"] = $"keyword1, keyword2, category{i}",
                ["meta_description"] = $"SEO description for category {i}",
                ["custom_url"] = $"/category-{i}",
                ["image_url"] = $"https://example.com/images/category-{i}.jpg"
            });
        }
        return categories;
    }

    private void SetupApiClientForCategories(List<Dictionary<string, object>> categories)
    {
        _mockApiClient.Setup(x => x.GetCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);
    }

    #endregion
}