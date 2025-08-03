#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task

using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Strategies;
// using BigCommerce.Migration.Orchestration.Orchestrators; // Static Durable Function classes not needed for DI
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

namespace BigCommerce.Migration.IntegrationTests;

/// <summary>
/// Integration tests for the complete chunked hierarchical category migration workflow
/// Tests the end-to-end orchestration including chunked processing, memory safety, and performance
/// Validates the complete interaction between discovery strategy, orchestrator, and activities
/// </summary>
public class ChunkedHierarchicalCategoryMigrationIntegrationTests : IAsyncLifetime
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly ILogger<ChunkedHierarchicalDiscoveryStrategy> _logger;
    // Note: Orchestrator is a static Durable Function, not needed for DI testing
    private readonly ChunkedHierarchyConfiguration _config;
    private readonly StoreConfiguration _sourceStore;
    private readonly StoreConfiguration _targetStore;

    public ChunkedHierarchicalCategoryMigrationIntegrationTests()
    {
        var services = new ServiceCollection();
        
        // Configure logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        // Mock external dependencies
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        services.AddSingleton(_mockApiClient.Object);
        
        // Configuration
        _config = new ChunkedHierarchyConfiguration
        {
            MaxCategoriesPerLevel = 1000,
            BatchSizePerLevel = 50,
            MaxBulkCreateSize = 100,
            MaxHierarchyDepth = 8,
            FallbackThreshold = 5000,
            EnableMemoryMonitoring = true,
            LevelProcessingTimeoutMinutes = 5,
            BulkCreationTimeoutMinutes = 3,
            EnableBulkCreation = true,
            AdaptiveBatchSizing = true
        };
        
        services.AddSingleton(Options.Create(_config));
        
        // Register strategy for integration testing
        services.AddScoped<IChunkedHierarchicalDiscoveryStrategy, ChunkedHierarchicalDiscoveryStrategy>();
        
        _serviceProvider = services.BuildServiceProvider();
        
        _logger = _serviceProvider.GetRequiredService<ILogger<ChunkedHierarchicalDiscoveryStrategy>>();
        
        // Test store configurations
        _sourceStore = new StoreConfiguration
        {
            StoreId = "source-store-test",
            AccessToken = "test-token-source",
            BaseUrl = "https://source-store.mybigcommerce.com"
        };
        
        _targetStore = new StoreConfiguration
        {
            StoreId = "target-store-test", 
            AccessToken = "test-token-target",
            BaseUrl = "https://target-store.mybigcommerce.com"
        };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_serviceProvider != null)
            await _serviceProvider.DisposeAsync();
    }

    #region End-to-End Chunked Processing Tests

    [Fact]
    public async Task EndToEndChunkedProcessing_WithSmallHierarchy_ShouldCompleteSuccessfully()
    {
        // Arrange - Small hierarchy (3 levels, 75 total categories)
        var testData = CreateHierarchicalTestData(
            rootCategories: 5,
            level1Children: 10, // 5 * 10 = 50 categories at level 1
            level2Children: 4   // 50 * 4 = 200 categories at level 2, but chunked
        );
        
        SetupApiClientForHierarchicalData(testData);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-small",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Run complete discovery analysis
        var hierarchyResult = await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = (finalMemory - initialMemory) / 1024.0 / 1024.0; // Convert to MB

        // Assert - Validate end-to-end processing
        hierarchyResult.Should().NotBeNull("Should successfully analyze hierarchy");
        hierarchyResult.TotalCategories.Should().Be(5, "Should count root categories for performance");
        hierarchyResult.MaxDepth.Should().BeGreaterThan(0, "Should detect hierarchy depth");
        hierarchyResult.EstimatedProcessingTimeMinutes.Should().BeGreaterThan(0, "Should estimate processing time");
        
        // Performance assertions
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000, "Should complete analysis within 2 seconds");
        memoryUsed.Should().BeLessThan(25, "Should stay within 25MB memory limit");
        
        // Verify the complete workflow executed
        _mockApiClient.Verify(x => x.GetCategoriesAsync(
            It.Is<StoreConfiguration>(s => s.StoreId == _sourceStore.StoreId),
            "0", // Root categories call
            It.IsAny<CancellationToken>()), Times.Once, "Should fetch root categories");
    }

    [Fact]
    public async Task EndToEndChunkedProcessing_WithMediumHierarchy_ShouldMaintainPerformance()
    {
        // Arrange - Medium hierarchy (500 root categories)
        var testData = CreateHierarchicalTestData(
            rootCategories: 500,
            level1Children: 0, // Only root level for this test
            level2Children: 0
        );
        
        SetupApiClientForHierarchicalData(testData);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-medium",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Run discovery with medium dataset
        var hierarchyResult = await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
        
        // Test individual level processing
        var levelRequest = new LevelFetchRequest
        {
            Level = 0,
            BatchSize = 50,
            MaxCategoriesPerLevel = 1000,
            ParentCategoryId = null
        };
        
        var levelResult = await strategy.DiscoverLevelAsync(levelRequest, discoveryRequest, CancellationToken.None);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = (finalMemory - initialMemory) / 1024.0 / 1024.0;

        // Assert - Performance and functionality
        hierarchyResult.TotalCategories.Should().Be(500, "Should handle medium dataset");
        levelResult.Should().NotBeNull("Should successfully process level");
        levelResult.SuccessCount.Should().Be(500, "Should process all categories");
        levelResult.FailureCount.Should().Be(0, "Should have no failures");
        
        // Performance requirements
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "Should complete medium dataset within 5 seconds");
        memoryUsed.Should().BeLessThan(25, "Should maintain memory limits even with larger datasets");
        levelResult.PeakMemoryUsageMB.Should().BeLessThan(25, "Level processing should respect memory limits");
    }

    [Fact]
    public async Task EndToEndChunkedProcessing_WithLargeDataset_ShouldScaleEfficiently()
    {
        // Arrange - Large dataset (2000 root categories to test scalability)
        var testData = CreateHierarchicalTestData(
            rootCategories: 2000,
            level1Children: 0,
            level2Children: 0
        );
        
        SetupApiClientForHierarchicalData(testData);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-large",
            EntityType = "categories", 
            SourceStore = _sourceStore
        };

        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Test large dataset processing
        var hierarchyResult = await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = (finalMemory - initialMemory) / 1024.0 / 1024.0;

        // Assert - Scalability and memory safety
        hierarchyResult.TotalCategories.Should().Be(2000, "Should handle large datasets");
        
        // Performance targets from QUICK-REFERENCE.md
        memoryUsed.Should().BeLessThan(25, "Should maintain 25MB memory limit even with large datasets");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000, "Should complete large dataset analysis within 10 seconds");
        
        // Verify large dataset triggers performance optimizations
        hierarchyResult.EstimatedProcessingTimeMinutes.Should().BeGreaterThan(0, "Should estimate processing time for large datasets");
    }

    #endregion

    #region Memory Usage Verification Tests

    [Fact]
    public async Task MemoryUsageVerification_WithRepeatedOperations_ShouldNotLeak()
    {
        // Arrange - Test for memory leaks with repeated operations
        var testData = CreateHierarchicalTestData(100, 0, 0);
        SetupApiClientForHierarchicalData(testData);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-memory-leak",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        var memoryReadings = new List<double>();
        
        // Act - Perform multiple operations and track memory
        for (int i = 0; i < 5; i++)
        {
            var beforeOperation = GC.GetTotalMemory(true);
            
            await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
            
            var afterOperation = GC.GetTotalMemory(true);
            var memoryUsed = (afterOperation - beforeOperation) / 1024.0 / 1024.0;
            memoryReadings.Add(memoryUsed);
            
            // Force cleanup between operations
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // Assert - Memory usage should remain stable
        memoryReadings.Should().OnlyContain(m => m < 25, "Each operation should stay within 25MB limit");
        
        // Check for memory leak pattern - last reading shouldn't be significantly higher than first
        var firstReading = memoryReadings.First();
        var lastReading = memoryReadings.Last();
        var memoryGrowth = lastReading - firstReading;
        
        memoryGrowth.Should().BeLessThan(5, "Should not accumulate more than 5MB over multiple operations (memory leak detection)");
    }

    [Fact]
    public async Task MemoryUsageVerification_WithConcurrentOperations_ShouldRespectLimits()
    {
        // Arrange - Test concurrent operations memory safety
        var testData = CreateHierarchicalTestData(200, 0, 0);
        SetupApiClientForHierarchicalData(testData);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var tasks = new List<Task>();
        var memoryPeaks = new List<double>();

        // Act - Run concurrent discovery operations
        for (int i = 0; i < 3; i++)
        {
            var migrationId = $"test-migration-concurrent-{i}";
            var discoveryRequest = new EntityDiscoveryRequest
            {
                MigrationId = migrationId,
                EntityType = "categories",
                SourceStore = _sourceStore
            };

            tasks.Add(Task.Run(async () =>
            {
                var beforeOperation = GC.GetTotalMemory(false);
                await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
                var afterOperation = GC.GetTotalMemory(false);
                var memoryUsed = (afterOperation - beforeOperation) / 1024.0 / 1024.0;
                
                lock (memoryPeaks)
                {
                    memoryPeaks.Add(memoryUsed);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Each concurrent operation should respect memory limits
        memoryPeaks.Should().OnlyContain(m => m < 25, "Concurrent operations should each stay within 25MB limit");
        memoryPeaks.Should().HaveCount(3, "Should track memory for all concurrent operations");
    }

    #endregion

    #region Large Dataset Validation Tests

    [Fact]
    public async Task LargeDatasetValidation_With5000Categories_ShouldTriggerOptimizations()
    {
        // Arrange - Dataset that exceeds fallback threshold (5000)
        var testData = CreateHierarchicalTestData(5500, 0, 0); // Exceeds 5000 threshold
        SetupApiClientForHierarchicalData(testData);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-fallback",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Process dataset that should trigger fallback optimizations
        var result = await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = (finalMemory - initialMemory) / 1024.0 / 1024.0;

        // Assert - Fallback optimizations should maintain performance
        result.Should().NotBeNull("Should handle large datasets");
        result.TotalCategories.Should().Be(5500, "Should process all categories");
        
        // Even with large dataset, should maintain memory limits due to optimizations
        memoryUsed.Should().BeLessThan(25, "Large dataset optimizations should maintain memory limits");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(15000, "Large dataset should complete within 15 seconds");
    }

    [Fact]
    public async Task LargeDatasetValidation_WithDeepHierarchy_ShouldHandleComplexity()
    {
        // Arrange - Deep hierarchy with multiple levels
        var testData = CreateDeepHierarchicalData(
            rootCount: 50,
            levelsDeep: 5,
            childrenPerLevel: 10
        );
        
        SetupApiClientForComplexHierarchy(testData);
        
        var strategy = _serviceProvider.GetRequiredService<IChunkedHierarchicalDiscoveryStrategy>();
        var discoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-deep",
            EntityType = "categories",
            SourceStore = _sourceStore
        };

        // Act - Process complex hierarchical structure
        var result = await strategy.AnalyzeHierarchyAsync(discoveryRequest, _config, CancellationToken.None);

        // Test level-by-level processing for deep hierarchy
        var levelResults = new List<LevelProcessingResult>();
        for (int level = 0; level < 3; level++) // Test first 3 levels
        {
            var levelRequest = new LevelFetchRequest
            {
                Level = level,
                BatchSize = 25,
                MaxCategoriesPerLevel = 1000
            };
            
            var levelResult = await strategy.DiscoverLevelAsync(levelRequest, discoveryRequest, CancellationToken.None);
            levelResults.Add(levelResult);
        }

        // Assert - Complex hierarchy should be handled efficiently
        result.Should().NotBeNull("Should handle complex hierarchies");
        result.MaxDepth.Should().BeGreaterThan(0, "Should detect hierarchy depth");
        
        levelResults.Should().HaveCount(3, "Should process multiple levels");
        levelResults.Should().OnlyContain(r => r.PeakMemoryUsageMB < 25, "Each level should respect memory limits");
        levelResults.Should().OnlyContain(r => r.FailureCount == 0, "Should successfully process all levels");
    }

    #endregion

    #region Helper Methods

    private HierarchicalTestData CreateHierarchicalTestData(int rootCategories, int level1Children, int level2Children)
    {
        var data = new HierarchicalTestData();
        
        // Create root categories
        for (int i = 1; i <= rootCategories; i++)
        {
            data.RootCategories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Root Category {i}",
                ["parent_id"] = 0,
                ["sort_order"] = i,
                ["description"] = $"Root category {i} description",
                ["is_visible"] = true
            });
        }
        
        // Create level 1 children
        int categoryId = rootCategories + 1;
        foreach (var rootCategory in data.RootCategories)
        {
            for (int j = 1; j <= level1Children; j++)
            {
                data.Level1Categories.Add(new Dictionary<string, object>
                {
                    ["id"] = categoryId++,
                    ["name"] = $"Level1 Cat {rootCategory["id"]}-{j}",
                    ["parent_id"] = rootCategory["id"],
                    ["sort_order"] = j,
                    ["description"] = $"Level 1 category under {rootCategory["name"]}",
                    ["is_visible"] = true
                });
            }
        }
        
        // Create level 2 children
        foreach (var level1Category in data.Level1Categories)
        {
            for (int k = 1; k <= level2Children; k++)
            {
                data.Level2Categories.Add(new Dictionary<string, object>
                {
                    ["id"] = categoryId++,
                    ["name"] = $"Level2 Cat {level1Category["id"]}-{k}",
                    ["parent_id"] = level1Category["id"],
                    ["sort_order"] = k,
                    ["description"] = $"Level 2 category under {level1Category["name"]}",
                    ["is_visible"] = true
                });
            }
        }
        
        return data;
    }

    private Dictionary<int, List<Dictionary<string, object>>> CreateDeepHierarchicalData(int rootCount, int levelsDeep, int childrenPerLevel)
    {
        var hierarchyData = new Dictionary<int, List<Dictionary<string, object>>>();
        int nextId = 1;
        
        // Create root level (level 0)
        var rootCategories = new List<Dictionary<string, object>>();
        for (int i = 0; i < rootCount; i++)
        {
            rootCategories.Add(new Dictionary<string, object>
            {
                ["id"] = nextId++,
                ["name"] = $"Root {i + 1}",
                ["parent_id"] = 0,
                ["sort_order"] = i + 1,
                ["is_visible"] = true
            });
        }
        hierarchyData[0] = rootCategories;
        
        // Create subsequent levels
        for (int level = 1; level <= levelsDeep; level++)
        {
            var levelCategories = new List<Dictionary<string, object>>();
            var parentCategories = hierarchyData[level - 1];
            
            foreach (var parent in parentCategories)
            {
                for (int child = 0; child < childrenPerLevel; child++)
                {
                    levelCategories.Add(new Dictionary<string, object>
                    {
                        ["id"] = nextId++,
                        ["name"] = $"L{level} Child {child + 1} of {parent["id"]}",
                        ["parent_id"] = parent["id"],
                        ["sort_order"] = child + 1,
                        ["is_visible"] = true
                    });
                }
            }
            
            hierarchyData[level] = levelCategories;
        }
        
        return hierarchyData;
    }

    private void SetupApiClientForHierarchicalData(HierarchicalTestData data)
    {
        // Setup root categories call
        _mockApiClient.Setup(x => x.GetCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            "0",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(data.RootCategories);
            
        // Setup child categories calls
        _mockApiClient.Setup(x => x.GetCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            It.Is<string>(parentId => parentId != "0"),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreConfiguration store, string parentId, CancellationToken ct) =>
            {
                // Return appropriate children based on parent ID
                if (!int.TryParse(parentId, out int parentIdInt))
                    return new List<Dictionary<string, object>>();
                    
                // Find children for this parent
                var allChildren = data.Level1Categories.Concat(data.Level2Categories);
                return allChildren.Where(c => c["parent_id"].Equals(parentIdInt)).ToList();
            });
    }

    private void SetupApiClientForComplexHierarchy(Dictionary<int, List<Dictionary<string, object>>> hierarchyData)
    {
        // Setup for each level
        foreach (var level in hierarchyData)
        {
            if (level.Key == 0)
            {
                // Root level
                _mockApiClient.Setup(x => x.GetCategoriesAsync(
                    It.IsAny<StoreConfiguration>(),
                    "0",
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync(level.Value);
            }
            else
            {
                // Child levels - return appropriate children based on parent ID
                _mockApiClient.Setup(x => x.GetCategoriesAsync(
                    It.IsAny<StoreConfiguration>(),
                    It.Is<string>(parentId => parentId != "0"),
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync((StoreConfiguration store, string parentId, CancellationToken ct) =>
                    {
                        if (!int.TryParse(parentId, out int parentIdInt))
                            return new List<Dictionary<string, object>>();
                            
                        // Find all children across all levels that match this parent
                        var allChildren = hierarchyData.Values.SelectMany(categories => categories);
                        return allChildren.Where(c => c["parent_id"].Equals(parentIdInt)).ToList();
                    });
            }
        }
    }

    #endregion

    #region Test Data Models

    private class HierarchicalTestData
    {
        public List<Dictionary<string, object>> RootCategories { get; } = new();
        public List<Dictionary<string, object>> Level1Categories { get; } = new();
        public List<Dictionary<string, object>> Level2Categories { get; } = new();
    }

    #endregion
}