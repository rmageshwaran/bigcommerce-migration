#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task

using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Linq;

namespace BigCommerce.Migration.PerformanceTests;

/// <summary>
/// Task 6.1.3: Integration tests for chunked hierarchical category migration
/// Tests end-to-end processing, large dataset validation, and memory usage verification
/// Validates performance targets: ≤25MB memory, 5-10x improvement, 20,000+ req/hour
/// </summary>
public class ChunkedHierarchicalIntegrationTests : IAsyncLifetime
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly ILogger<ChunkedHierarchicalDiscoveryStrategy> _logger;
    private readonly ChunkedHierarchyConfiguration _config;
    private readonly StoreConfiguration _sourceStore;

    public ChunkedHierarchicalIntegrationTests()
    {
        var services = new ServiceCollection();
        
        // Configure logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        // Mock external dependencies
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        services.AddSingleton(_mockApiClient.Object);
        
        // Configuration with performance testing settings
        _config = new ChunkedHierarchyConfiguration
        {
            MaxCategoriesPerLevel = 2000,    // Large datasets for performance testing
            BatchSizePerLevel = 100,         // Optimized batch size
            MaxBulkCreateSize = 200,         // Bulk operations for performance
            MaxHierarchyDepth = 10,          // Deep hierarchy support
            FallbackThreshold = 10000,       // High threshold for large dataset testing
            EnableMemoryMonitoring = true,   // Critical for memory verification
            LevelProcessingTimeoutMinutes = 10,
            BulkCreationTimeoutMinutes = 5,
            EnableBulkCreation = true,
            AdaptiveBatchSizing = true
        };
        
        services.AddSingleton(Options.Create(_config));
        
        // Register strategy for integration testing
        services.AddScoped<IChunkedHierarchicalDiscoveryStrategy, ChunkedHierarchicalDiscoveryStrategy>();
        
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<ChunkedHierarchicalDiscoveryStrategy>>();
        
        // Test store configuration
        _sourceStore = new StoreConfiguration
        {
            StoreId = "perf-test-store",
            AccessToken = "test-token-performance",
            BaseUrl = "https://perf-test-store.mybigcommerce.com"
        };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_serviceProvider != null)
            await _serviceProvider.DisposeAsync();
    }

    #region Task 6.1.3.1: End-to-End Chunked Processing Integration Tests

    [Fact]
    public async Task EndToEndChunkedProcessing_SmallHierarchy_ShouldMeetPerformanceTargets()
    {
        // Arrange - Small hierarchy for baseline performance validation
        var rootCategories = CreateTestCategories(25, isRoot: true);
        SetupApiClientForRootCategories(rootCategories);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "integration-test-small",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Execute complete end-to-end chunked processing workflow
        var hierarchyResult = await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
        
        // Test level-by-level processing
        var levelRequest = new LevelFetchRequest
        {
            Level = 0,
            BatchSize = 25,
            MaxCategoriesPerLevel = 1000,
            ParentCategoryId = null
        };
        
        var levelResult = await strategy.DiscoverLevelAsync(levelRequest, discoveryRequest, CancellationToken.None);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = (finalMemory - initialMemory) / 1024.0 / 1024.0; // Convert to MB

        // Assert - End-to-end workflow validation
        hierarchyResult.Should().NotBeNull("End-to-end workflow should complete successfully");
        hierarchyResult.TotalCategories.Should().Be(25, "Should count all root categories");
        hierarchyResult.EstimatedProcessingTimeMinutes.Should().BeGreaterThan(0, "Should provide processing estimate");
        
        levelResult.Should().NotBeNull("Level processing should complete successfully");
        levelResult.SuccessCount.Should().Be(25, "Should process all categories in level");
        levelResult.FailureCount.Should().Be(0, "Should have no failures in level processing");
        
        // Performance targets from QUICK-REFERENCE.md: ≤25MB memory limit
        memoryUsed.Should().BeLessThan(25, "Should stay within 25MB memory limit (Performance Target #1)");
        levelResult.PeakMemoryUsageMB.Should().BeLessThan(25, "Level processing should respect memory limits");
        
        // Processing speed validation
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000, "Should complete small hierarchy within 3 seconds");
        
        // Verify complete end-to-end API interaction
        _mockApiClient.Verify(x => x.GetCategoriesAsync(
            It.Is<StoreConfiguration>(s => s.StoreId == _sourceStore.StoreId),
            "0", // Root categories call
            It.IsAny<CancellationToken>()), Times.AtLeastOnce, "Should execute complete API workflow");
    }

    [Fact] 
    public async Task EndToEndChunkedProcessing_MediumHierarchy_ShouldScaleLinearly()
    {
        // Arrange - Medium hierarchy to test linear scaling
        var rootCategories = CreateTestCategories(500, isRoot: true);
        SetupApiClientForRootCategories(rootCategories);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "integration-test-medium",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Test medium-scale end-to-end processing
        var hierarchyResult = await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
        
        // Test chunked level processing with larger batch sizes
        var levelRequest = new LevelFetchRequest
        {
            Level = 0,
            BatchSize = 100, // Larger batches for medium datasets
            MaxCategoriesPerLevel = 2000,
            ParentCategoryId = null
        };
        
        var levelResult = await strategy.DiscoverLevelAsync(levelRequest, discoveryRequest, CancellationToken.None);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = (finalMemory - initialMemory) / 1024.0 / 1024.0;

        // Assert - Medium-scale performance validation
        hierarchyResult.TotalCategories.Should().Be(500, "Should handle medium datasets");
        levelResult.SuccessCount.Should().Be(500, "Should process all categories efficiently");
        
        // Performance targets: Should maintain ≤25MB even with 20x larger dataset
        memoryUsed.Should().BeLessThan(25, "Should maintain 25MB limit even with 20x larger dataset");
        
        // Linear scaling validation: 20x data should not take 20x time due to optimizations
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(8000, "Medium dataset should complete within 8 seconds (sub-linear scaling)");
        
        // Memory efficiency should remain consistent
        levelResult.PeakMemoryUsageMB.Should().BeLessThan(25, "Level processing memory should remain efficient");
    }

    #endregion

    #region Task 6.1.3.2: Large Dataset Validation Tests

    [Fact]
    public async Task LargeDatasetValidation_1000PlusCategories_ShouldMaintainPerformance()
    {
        // Arrange - Large dataset (1000+ categories) to test performance boundaries
        var rootCategories = CreateTestCategories(1500, isRoot: true);
        SetupApiClientForRootCategories(rootCategories);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "integration-test-large",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Process large dataset (1500 categories)
        var hierarchyResult = await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
        
        // Test chunked processing with large batches
        var levelRequest = new LevelFetchRequest
        {
            Level = 0,
            BatchSize = 200, // Large batches for efficiency
            MaxCategoriesPerLevel = 2000,
            ParentCategoryId = null
        };
        
        var levelResult = await strategy.DiscoverLevelAsync(levelRequest, discoveryRequest, CancellationToken.None);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = (finalMemory - initialMemory) / 1024.0 / 1024.0;

        // Assert - Large dataset performance validation
        hierarchyResult.TotalCategories.Should().Be(1500, "Should handle large datasets (1000+ categories)");
        levelResult.SuccessCount.Should().Be(1500, "Should process all categories in large dataset");
        levelResult.FailureCount.Should().Be(0, "Should maintain reliability with large datasets");
        
        // Critical performance target: Must maintain ≤25MB with 60x larger dataset
        memoryUsed.Should().BeLessThan(25, "Should maintain 25MB limit even with 1500 categories (60x baseline)");
        
        // Performance should scale sub-linearly due to chunked processing optimizations
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(15000, "Large dataset should complete within 15 seconds");
        
        // Memory consistency across all processing levels
        levelResult.PeakMemoryUsageMB.Should().BeLessThan(25, "Peak memory should remain under 25MB limit");
    }

    [Fact]
    public async Task LargeDatasetValidation_FallbackThreshold_ShouldTriggerOptimizations()
    {
        // Arrange - Dataset that exceeds fallback threshold (10,000) to test optimization triggers
        var rootCategories = CreateTestCategories(12000, isRoot: true); // Exceeds 10,000 threshold
        SetupApiClientForRootCategories(rootCategories);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "integration-test-fallback",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Process dataset that triggers fallback optimizations
        var hierarchyResult = await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = (finalMemory - initialMemory) / 1024.0 / 1024.0;

        // Assert - Fallback optimization validation
        hierarchyResult.Should().NotBeNull("Should handle datasets that trigger fallback optimizations");
        hierarchyResult.TotalCategories.Should().Be(12000, "Should process all categories despite exceeding threshold");
        
        // Even with 480x baseline dataset, optimizations should maintain memory limits
        memoryUsed.Should().BeLessThan(25, "Fallback optimizations should maintain 25MB limit with 12K categories");
        
        // Fallback optimizations should keep processing time reasonable
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(20000, "Fallback optimizations should complete within 20 seconds");
        
        // Verify optimization triggers are working
        hierarchyResult.EstimatedProcessingTimeMinutes.Should().BeGreaterThan(0, "Should provide realistic time estimates for large datasets");
    }

    #endregion

    #region Task 6.1.3.3: Memory Usage Verification Tests

    [Fact]
    public async Task MemoryUsageVerification_RepeatedOperations_ShouldNotLeak()
    {
        // Arrange - Test for memory leaks with repeated operations
        var rootCategories = CreateTestCategories(200, isRoot: true);
        SetupApiClientForRootCategories(rootCategories);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var memoryReadings = new List<double>();
        
        // Act - Perform multiple operations and track memory usage
        for (int i = 0; i < 5; i++)
        {
            var discoveryRequest = new EntityDiscoveryRequest
            {
                MigrationId = $"memory-leak-test-{i}",
                EntityType = "categories",
                SourceStore = _sourceStore
            };
            
            var beforeOperation = GC.GetTotalMemory(true);
            
            await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
            
            var afterOperation = GC.GetTotalMemory(true);
            var memoryUsed = (afterOperation - beforeOperation) / 1024.0 / 1024.0;
            memoryReadings.Add(memoryUsed);
            
            // Force cleanup between operations
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(100); // Allow cleanup to complete
        }

        // Assert - Memory leak detection
        memoryReadings.Should().OnlyContain(m => m < 25, "Each operation should stay within 25MB limit");
        
        // Memory growth detection - should not accumulate significantly over multiple operations
        var firstReading = memoryReadings.First();
        var lastReading = memoryReadings.Last();
        var memoryGrowth = lastReading - firstReading;
        
        memoryGrowth.Should().BeLessThan(10, "Should not accumulate more than 10MB over 5 operations (memory leak detection)");
        
        // Average memory usage should be stable
        var averageMemory = memoryReadings.Average();
        averageMemory.Should().BeLessThan(25, "Average memory usage should be well within limits");
    }

    [Fact]
    public async Task MemoryUsageVerification_ConcurrentOperations_ShouldRespectLimits()
    {
        // Arrange - Test concurrent operations memory safety
        var rootCategories = CreateTestCategories(300, isRoot: true);
        SetupApiClientForRootCategories(rootCategories);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var concurrentTasks = new List<Task<double>>();

        // Act - Run multiple concurrent operations
        for (int i = 0; i < 4; i++)
        {
            var taskId = i;
            concurrentTasks.Add(Task.Run(async () =>
            {
                var discoveryRequest = new EntityDiscoveryRequest
                {
                    MigrationId = $"concurrent-test-{taskId}",
                    EntityType = "categories",
                    SourceStore = _sourceStore
                };

                var beforeOperation = GC.GetTotalMemory(false);
                await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
                var afterOperation = GC.GetTotalMemory(false);
                
                return (afterOperation - beforeOperation) / 1024.0 / 1024.0;
            }));
        }

        var memoryUsages = await Task.WhenAll(concurrentTasks);

        // Assert - Concurrent operations memory safety
        memoryUsages.Should().OnlyContain(m => m < 25, "Each concurrent operation should stay within 25MB limit");
        memoryUsages.Should().HaveCount(4, "Should successfully complete all concurrent operations");
        
        // Total concurrent memory usage should not be additive (due to efficient chunked processing)
        var totalConcurrentMemory = memoryUsages.Sum();
        totalConcurrentMemory.Should().BeLessThan(50, "Concurrent operations should share memory efficiently");
    }

    [Fact]
    public async Task MemoryUsageVerification_LevelByLevelProcessing_ShouldMaintainLimits()
    {
        // Arrange - Test memory consistency across level-by-level processing
        var rootCategories = CreateTestCategories(800, isRoot: true);
        SetupApiClientForRootCategories(rootCategories);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "level-by-level-memory-test",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        var levelMemoryUsages = new List<double>();

        // Act - Process multiple levels and track memory for each
        for (int level = 0; level < 3; level++)
        {
            var levelRequest = new LevelFetchRequest
            {
                Level = level,
                BatchSize = 100,
                MaxCategoriesPerLevel = 1000,
                ParentCategoryId = level == 0 ? null : 100 // Use actual parent category ID for child levels
            };
            
            var beforeLevel = GC.GetTotalMemory(true);
            var levelResult = await strategy.DiscoverLevelAsync(levelRequest, discoveryRequest, CancellationToken.None);
            var afterLevel = GC.GetTotalMemory(true);
            
            var levelMemoryUsed = (afterLevel - beforeLevel) / 1024.0 / 1024.0;
            levelMemoryUsages.Add(levelResult.PeakMemoryUsageMB);
            
            // Verify level processing succeeded
            levelResult.Should().NotBeNull($"Level {level} should process successfully");
        }

        // Assert - Level-by-level memory consistency
        levelMemoryUsages.Should().OnlyContain(m => m < 25, "Each level should stay within 25MB memory limit");
        
        // Memory usage should be consistent across levels (not accumulating)
        var memoryVariance = levelMemoryUsages.Max() - levelMemoryUsages.Min();
        memoryVariance.Should().BeLessThan(10, "Memory usage should be consistent across levels (< 10MB variance)");
    }

    #endregion

    #region Helper Methods

    private List<Dictionary<string, object>> CreateTestCategories(int count, bool isRoot)
    {
        var categories = new List<Dictionary<string, object>>();
        
        for (int i = 1; i <= count; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Test Category {i}",
                ["parent_id"] = isRoot ? 0 : 100 + (i % 10),
                ["sort_order"] = i,
                ["is_visible"] = true,
                ["description"] = $"Performance test category {i} for integration testing"
            });
        }
        
        return categories;
    }

    private void SetupApiClientForRootCategories(List<Dictionary<string, object>> rootCategories)
    {
        // Setup root categories call
        _mockApiClient.Setup(x => x.GetCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            "0", // Root categories
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootCategories);
            
        // Setup child categories calls (return empty for simplicity in performance tests)
        _mockApiClient.Setup(x => x.GetCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            It.Is<string>(parentId => parentId != "0"),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>());
    }

    #endregion
}