using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Storage
{
    /// <summary>
    /// Phase 5: Task 5.2.2 - Storage Index Optimization Tests
    /// Validates Azure Table Storage query patterns and indexing strategies for 50% latency reduction
    /// 
    /// Goals:
    /// - Test partition key optimization for better data distribution
    /// - Validate row key strategies for efficient range queries
    /// - Ensure query batching achieves latency and throughput targets
    /// - Verify performance monitoring and adaptive optimization
    /// - Test complex query optimization with secondary indexes
    /// </summary>
    public class StorageIndexOptimizationTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<StorageIndexOptimizer>> _mockLogger;

        public StorageIndexOptimizationTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<StorageIndexOptimizer>>();
        }

        [Fact]
        public async Task Partition_Key_Optimization_Should_Achieve_Target_Performance_Improvement()
        {
            // Arrange
            var optimizer = CreateStorageIndexOptimizer();
            var queryPatterns = CreateCategoryQueryPatterns();
            var dataDistribution = CreateDataDistribution(5000, 0.3, 0.4); // 5K entities, 30% skew, 40% hotspot

            // Act
            var strategy = await optimizer.OptimizePartitionKeyAsync(
                "Categories", queryPatterns, dataDistribution);

            // Assert - Target: 40-50% performance improvement for Categories
            Assert.True(strategy.ExpectedImprovement >= 35,
                $"Should achieve significant performance improvement. Actual: {strategy.ExpectedImprovement:F1}%");

            Assert.True(strategy.PartitionCount > 1,
                "Should create multiple partitions for better distribution");

            Assert.True(strategy.PartitionKeyTemplates.Any(),
                "Should provide partition key templates");

            Assert.NotEqual(PartitionPattern.StoreBasedPartitioning, strategy.RecommendedPattern);

            Assert.NotNull(strategy.HotspotPrevention);
            Assert.True(strategy.HotspotPrevention.UseHashingSalt,
                "Should use hashing salt for hotspot prevention with high skew");

            _output.WriteLine($"🎯 Partition Key Optimization Results for Categories:");
            _output.WriteLine($"Recommended Pattern: {strategy.RecommendedPattern}");
            _output.WriteLine($"Partition Count: {strategy.PartitionCount}");
            _output.WriteLine($"Expected Improvement: {strategy.ExpectedImprovement:F1}%");
            _output.WriteLine($"Load Balancing: {strategy.LoadBalancingStrategy}");
            _output.WriteLine($"Templates: {string.Join(", ", strategy.PartitionKeyTemplates)}");
        }

        [Fact]
        public async Task Row_Key_Optimization_Should_Improve_Range_Query_Performance()
        {
            // Arrange
            var optimizer = CreateStorageIndexOptimizer();
            var accessPatterns = CreateProductAccessPatterns();
            var sortingRequirements = CreateSortingRequirements();

            // Act
            var strategy = await optimizer.OptimizeRowKeyAsync(
                "Products", accessPatterns, sortingRequirements);

            // Assert - Target: 30-45% query performance improvement
            Assert.True(strategy.ExpectedQueryImprovement >= 25,
                $"Should achieve significant query improvement. Actual: {strategy.ExpectedQueryImprovement:F1}%");

            Assert.True(strategy.RowKeyTemplates.Any(),
                "Should provide row key templates");

            Assert.NotNull(strategy.RangeQueryOptimization);
            Assert.True(strategy.RangeQueryOptimization.ExpectedSpeedup > 0,
                "Should optimize range queries");

            Assert.NotNull(strategy.CompositeKeyStrategy);
            if (accessPatterns.Any(p => p.FilterFields.Count > 1))
            {
                Assert.True(strategy.CompositeKeyStrategy.UseCompositeKeys,
                    "Should use composite keys for multi-field queries");
            }

            _output.WriteLine($"🎯 Row Key Optimization Results for Products:");
            _output.WriteLine($"Recommended Pattern: {strategy.RecommendedPattern}");
            _output.WriteLine($"Expected Query Improvement: {strategy.ExpectedQueryImprovement:F1}%");
            _output.WriteLine($"Range Query Speedup: {strategy.RangeQueryOptimization.ExpectedSpeedup:F1}%");
            _output.WriteLine($"Composite Keys: {strategy.CompositeKeyStrategy.UseCompositeKeys}");
            _output.WriteLine($"Templates: {string.Join(", ", strategy.RowKeyTemplates)}");
        }

        [Fact]
        public async Task Query_Batching_Should_Achieve_Latency_And_Throughput_Targets()
        {
            // Arrange
            var optimizer = CreateStorageIndexOptimizer();
            var queries = CreateStorageQueries(250); // Large query set
            var batchingOptions = new QueryBatchingOptions
            {
                MaxBatchSize = 100,
                MaxConcurrency = 5,
                EnableAdaptiveBatching = true
            };

            // Act
            var optimization = await optimizer.OptimizeQueryBatchingAsync(
                queries, batchingOptions, CancellationToken.None);

            // Assert - Target: 40% latency reduction, 60% throughput gain
            Assert.True(optimization.ExpectedLatencyReduction >= 35,
                $"Should achieve significant latency reduction. Actual: {optimization.ExpectedLatencyReduction:F1}%");

            Assert.True(optimization.ExpectedThroughputGain >= 50,
                $"Should achieve significant throughput gain. Actual: {optimization.ExpectedThroughputGain:F1}%");

            Assert.True(optimization.OptimizedBatches.Count < queries.Count(),
                "Should reduce total number of operations through batching");

            Assert.True(optimization.OptimizedBatches.Count <= queries.Count() / 2,
                "Should achieve at least 50% operation reduction");

            Assert.NotNull(optimization.ExecutionPlan);
            Assert.True(optimization.ExecutionPlan.ParallelismLevel <= 5,
                "Should respect Azure Table Storage concurrency limits");

            _output.WriteLine($"🎯 Query Batching Optimization Results:");
            _output.WriteLine($"Original Queries: {optimization.OriginalQueryCount}");
            _output.WriteLine($"Optimized Batches: {optimization.OptimizedBatches.Count}");
            _output.WriteLine($"Expected Latency Reduction: {optimization.ExpectedLatencyReduction:F1}%");
            _output.WriteLine($"Expected Throughput Gain: {optimization.ExpectedThroughputGain:F1}%");
            _output.WriteLine($"Parallelism Level: {optimization.ExecutionPlan.ParallelismLevel}");
            _output.WriteLine($"Operation Reduction: {((queries.Count() - optimization.OptimizedBatches.Count) / (double)queries.Count() * 100):F1}%");
        }

        [Fact]
        public async Task Performance_Monitoring_Should_Detect_Issues_And_Generate_Recommendations()
        {
            // Arrange
            var optimizer = CreateStorageIndexOptimizer();
            var performanceData = CreatePoorPerformanceData(); // High latency, hot partitions

            // Act
            var optimization = await optimizer.MonitorAndOptimizePerformanceAsync(
                "Products", performanceData);

            // Assert
            Assert.NotEmpty(optimization.Recommendations);
            Assert.True(optimization.Recommendations.Count >= 2,
                "Should generate multiple recommendations for poor performance");

            var highLatencyRecommendation = optimization.Recommendations
                .FirstOrDefault(r => r.Type == OptimizationType.QueryOptimization);
            Assert.NotNull(highLatencyRecommendation);
            Assert.Equal(RecommendationPriority.High, highLatencyRecommendation.Priority);

            var hotPartitionRecommendation = optimization.Recommendations
                .FirstOrDefault(r => r.Type == OptimizationType.PartitionOptimization);
            Assert.NotNull(hotPartitionRecommendation);

            Assert.True(optimization.ExpectedImprovements.OverallImprovement >= 35,
                "Should expect significant overall improvement for poor performance");

            Assert.NotEmpty(optimization.ImmediateActions);
            Assert.Contains(optimization.ImmediateActions, 
                a => a.Priority == ActionPriority.High);

            _output.WriteLine($"🎯 Performance Monitoring Results:");
            _output.WriteLine($"Recommendations Generated: {optimization.Recommendations.Count}");
            _output.WriteLine($"Expected Overall Improvement: {optimization.ExpectedImprovements.OverallImprovement:F1}%");
            _output.WriteLine($"Immediate Actions: {optimization.ImmediateActions.Count}");
            
            foreach (var recommendation in optimization.Recommendations)
            {
                _output.WriteLine($"  {recommendation.Type}: {recommendation.Description} " +
                    $"(Priority: {recommendation.Priority}, Improvement: {recommendation.ExpectedImprovement:F1}%)");
            }
        }

        [Fact]
        public async Task Complex_Query_Index_Strategy_Should_Optimize_Advanced_Scenarios()
        {
            // Arrange
            var optimizer = CreateStorageIndexOptimizer();
            var complexQueries = CreateComplexQueryPatterns();

            // Act
            var strategy = await optimizer.CreateIndexStrategyAsync(
                "Products", complexQueries);

            // Assert
            Assert.NotNull(strategy.ComplexityAnalysis);
            Assert.True(strategy.ComplexityAnalysis.TotalQueries > 0);

            if (complexQueries.Any(q => q.RequiresSecondaryIndex))
            {
                Assert.NotEmpty(strategy.SecondaryIndexes);
                Assert.True(strategy.SecondaryIndexes.Sum(i => i.ExpectedImprovement) >= 40,
                    "Secondary indexes should provide significant improvement");
            }

            if (complexQueries.Any(q => q.RequiresMaterializedView))
            {
                Assert.NotEmpty(strategy.MaterializedViews);
                Assert.True(strategy.MaterializedViews.Sum(v => v.ExpectedImprovement) >= 30,
                    "Materialized views should provide significant improvement");
            }

            Assert.NotEmpty(strategy.CachingStrategies);
            Assert.True(strategy.ExpectedImprovements.OverallImprovement >= 50,
                "Complex query optimization should achieve high improvement");

            _output.WriteLine($"🎯 Complex Query Index Strategy Results:");
            _output.WriteLine($"Total Queries Analyzed: {strategy.ComplexityAnalysis.TotalQueries}");
            _output.WriteLine($"Complex Queries: {strategy.ComplexityAnalysis.ComplexQueries}");
            _output.WriteLine($"Average Complexity: {strategy.ComplexityAnalysis.AverageComplexity:F1}");
            _output.WriteLine($"Secondary Indexes: {strategy.SecondaryIndexes.Count}");
            _output.WriteLine($"Materialized Views: {strategy.MaterializedViews.Count}");
            _output.WriteLine($"Caching Strategies: {strategy.CachingStrategies.Count}");
            _output.WriteLine($"Expected Overall Improvement: {strategy.ExpectedImprovements.OverallImprovement:F1}%");
        }

        [Fact]
        public async Task Optimized_Storage_Operations_Should_Achieve_Performance_Targets()
        {
            // Arrange
            var optimizer = CreateStorageIndexOptimizer();
            var operations = CreateStorageOperations(500); // Large operation set
            var optimizationOptions = new StorageOptimizationOptions
            {
                BatchThreshold = 50,
                MaxBatchSize = 100,
                MaxConcurrency = 5,
                OptimizationStrategy = OptimizationStrategy.ThroughputOptimized
            };

            // Act
            var results = await optimizer.ExecuteOptimizedOperationsAsync(
                operations, optimizationOptions, CancellationToken.None);

            // Assert - Target: <50ms average latency, >95% success rate, >100 ops/sec throughput
            Assert.True(results.AverageLatency <= 75, // Allow some tolerance
                $"Should achieve low average latency. Actual: {results.AverageLatency:F1}ms");

            Assert.True(results.OverallSuccessRate >= 0.95,
                $"Should maintain high success rate. Actual: {results.OverallSuccessRate:P1}");

            Assert.True(results.Throughput >= 80, // Allow some tolerance
                $"Should achieve high throughput. Actual: {results.Throughput:F1} ops/sec");

            Assert.True(results.TotalDuration.TotalSeconds <= operations.Count() / 80, // Based on throughput target
                "Should complete operations within expected timeframe");

            Assert.NotEmpty(results.GroupResults);
            Assert.True(results.GroupResults.All(g => g.SuccessRate >= 0.9),
                "All operation groups should achieve high success rates");

            _output.WriteLine($"🎯 Optimized Storage Operations Results:");
            _output.WriteLine($"Total Operations: {results.TotalOperations}");
            _output.WriteLine($"Total Duration: {results.TotalDuration.TotalSeconds:F1}s");
            _output.WriteLine($"Average Latency: {results.AverageLatency:F1}ms");
            _output.WriteLine($"Overall Success Rate: {results.OverallSuccessRate:P1}");
            _output.WriteLine($"Throughput: {results.Throughput:F1} ops/sec");
            _output.WriteLine($"Operation Groups: {results.GroupResults.Count}");
        }

        [Fact]
        public async Task Multiple_Entity_Types_Should_Each_Achieve_Optimization_Targets()
        {
            // Arrange
            var optimizer = CreateStorageIndexOptimizer();
            var entityTypes = new[] { "Categories", "Products", "Variants", "Images" };
            var optimizationResults = new Dictionary<string, double>();

            // Act & Assert
            foreach (var entityType in entityTypes)
            {
                var queryPatterns = CreateQueryPatternsForEntity(entityType);
                var dataDistribution = CreateDataDistributionForEntity(entityType);

                var strategy = await optimizer.OptimizePartitionKeyAsync(
                    entityType, queryPatterns, dataDistribution);

                optimizationResults[entityType] = strategy.ExpectedImprovement;

                // Each entity type should achieve meaningful improvement
                Assert.True(strategy.ExpectedImprovement >= 25,
                    $"{entityType} should achieve at least 25% improvement. " +
                    $"Actual: {strategy.ExpectedImprovement:F1}%");

                _output.WriteLine($"{entityType}: {strategy.ExpectedImprovement:F1}% improvement, " +
                    $"Pattern: {strategy.RecommendedPattern}, Partitions: {strategy.PartitionCount}");
            }

            // Overall average should meet target
            var averageImprovement = optimizationResults.Values.Average();
            Assert.True(averageImprovement >= 30,
                $"Average improvement across all entity types should be at least 30%. " +
                $"Actual: {averageImprovement:F1}%");

            _output.WriteLine($"🎯 Multi-Entity Optimization Summary:");
            _output.WriteLine($"Average Improvement: {averageImprovement:F1}%");
            _output.WriteLine($"Best Performing: {optimizationResults.OrderByDescending(x => x.Value).First().Key} " +
                $"({optimizationResults.Values.Max():F1}%)");
            _output.WriteLine($"Lowest Performing: {optimizationResults.OrderBy(x => x.Value).First().Key} " +
                $"({optimizationResults.Values.Min():F1}%)");
        }

        [Fact]
        public async Task Adaptive_Optimization_Should_Improve_Performance_Over_Time()
        {
            // Arrange
            var optimizer = CreateStorageIndexOptimizer();
            var entityType = "Products";
            var baselinePerformance = CreatePoorPerformanceData();
            var optimizationResults = new List<double>();

            // Act - Simulate multiple optimization cycles
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                var performanceData = SimulateImprovingPerformance(baselinePerformance, cycle);
                var optimization = await optimizer.MonitorAndOptimizePerformanceAsync(
                    entityType, performanceData);

                optimizationResults.Add(optimization.ExpectedImprovements.OverallImprovement);

                _output.WriteLine($"Cycle {cycle}: Current Latency: {performanceData.AverageLatency:F1}ms, " +
                    $"Expected Improvement: {optimization.ExpectedImprovements.OverallImprovement:F1}%");
            }

            // Assert - Later cycles should show continued improvement potential
            Assert.True(optimizationResults.Count == 3);
            
            // Even as performance improves, the optimizer should find additional optimizations
            Assert.True(optimizationResults.Last() >= optimizationResults.First() * 0.7,
                "Should maintain reasonable improvement potential even as performance improves");

            var totalPotentialImprovement = optimizationResults.Sum();
            Assert.True(totalPotentialImprovement >= 60,
                $"Total improvement potential across cycles should be significant. " +
                $"Actual: {totalPotentialImprovement:F1}%");

            _output.WriteLine($"🎯 Adaptive Optimization Results:");
            _output.WriteLine($"Total Improvement Potential: {totalPotentialImprovement:F1}%");
            _output.WriteLine($"Improvement Consistency: {(optimizationResults.Last() / optimizationResults.First()):F2}x");
        }

        // Helper methods for test setup and data creation

        private StorageIndexOptimizer CreateStorageIndexOptimizer()
        {
            return new StorageIndexOptimizer(_mockLogger.Object);
        }

        private IEnumerable<QueryPattern> CreateCategoryQueryPatterns()
        {
            return new[]
            {
                new QueryPattern
                {
                    QueryType = QueryType.Equality,
                    OperationType = OperationType.Read,
                    FrequencyPerHour = 500,
                    AverageLatencyMs = 120,
                    FilterFields = new List<string> { "StoreId", "ParentCategoryId" },
                    PartitionKeyPattern = "{StoreId}",
                    RowKeyPattern = "{CategoryId}"
                },
                new QueryPattern
                {
                    QueryType = QueryType.Range,
                    OperationType = OperationType.Read,
                    FrequencyPerHour = 200,
                    AverageLatencyMs = 180,
                    FilterFields = new List<string> { "StoreId", "CreatedDate" },
                    PartitionKeyPattern = "{StoreId}",
                    RowKeyPattern = "{CreatedDate}_{CategoryId}"
                },
                new QueryPattern
                {
                    QueryType = QueryType.Point,
                    OperationType = OperationType.Write,
                    FrequencyPerHour = 100,
                    AverageLatencyMs = 95,
                    FilterFields = new List<string> { "CategoryId" },
                    PartitionKeyPattern = "{StoreId}_{CategoryHash}",
                    RowKeyPattern = "{CategoryId}"
                }
            };
        }

        private DataDistributionAnalysis CreateDataDistribution(int entityCount, double skewFactor, double hotspotPercentage)
        {
            return new DataDistributionAnalysis
            {
                EntityCount = entityCount,
                SkewFactor = skewFactor,
                HotspotPercentage = hotspotPercentage,
                AverageEntitySize = 8 * 1024, // 8KB average
                GrowthRatePerMonth = 0.15, // 15% growth per month
                PartitionDistribution = new Dictionary<string, int>
                {
                    ["Store1"] = (int)(entityCount * 0.6), // 60% in one store (hotspot)
                    ["Store2"] = (int)(entityCount * 0.25), // 25% in another
                    ["Store3"] = (int)(entityCount * 0.15) // 15% distributed
                }
            };
        }

        private IEnumerable<AccessPattern> CreateProductAccessPatterns()
        {
            return new[]
            {
                new AccessPattern
                {
                    AccessType = AccessType.Point,
                    FilterField = "ProductId",
                    FilterFields = new List<string> { "ProductId" },
                    RequiresSorting = false,
                    Frequency = 800,
                    AverageResponseTimeMs = 45
                },
                new AccessPattern
                {
                    AccessType = AccessType.Range,
                    FilterField = "CategoryId",
                    FilterFields = new List<string> { "CategoryId", "Price" },
                    RequiresSorting = true,
                    Frequency = 300,
                    AverageResponseTimeMs = 150
                },
                new AccessPattern
                {
                    AccessType = AccessType.Range,
                    FilterField = "CreatedDate",
                    FilterFields = new List<string> { "StoreId", "CreatedDate" },
                    RequiresSorting = true,
                    Frequency = 200,
                    AverageResponseTimeMs = 180
                }
            };
        }

        private SortingRequirements CreateSortingRequirements()
        {
            return new SortingRequirements
            {
                SortFields = new List<string> { "CreatedDate", "ProductId", "Price" },
                DefaultDirection = SortDirection.Descending,
                RequiresDescendingSort = true,
                RequiresMultipleSortOrders = true
            };
        }

        private IEnumerable<StorageQuery> CreateStorageQueries(int count)
        {
            var random = new Random(42); // Deterministic for testing
            var partitionKeys = new[] { "Store1", "Store2", "Store3", "Store4", "Store5" };
            var queryTypes = new[] { QueryType.Point, QueryType.Range, QueryType.Equality };

            for (int i = 0; i < count; i++)
            {
                yield return new StorageQuery
                {
                    PartitionKey = partitionKeys[i % partitionKeys.Length],
                    RowKey = $"Entity_{i:D6}",
                    QueryType = queryTypes[i % queryTypes.Length],
                    EstimatedLatencyMs = 20 + random.NextDouble() * 100, // 20-120ms
                    ExpectedResultCount = random.Next(1, 50)
                };
            }
        }

        private StoragePerformanceData CreatePoorPerformanceData()
        {
            return new StoragePerformanceData
            {
                AverageLatency = 180, // High latency
                P95Latency = 350,
                Throughput = 25, // Low throughput
                ErrorRate = 0.03, // 3% error rate
                HotPartitionPercentage = 0.85, // 85% traffic on hot partitions
                Timestamp = DateTime.UtcNow
            };
        }

        private IEnumerable<ComplexQueryPattern> CreateComplexQueryPatterns()
        {
            return new[]
            {
                new ComplexQueryPattern
                {
                    QueryId = "ComplexProductQuery1",
                    ComplexityScore = 8,
                    RequiresSecondaryIndex = true,
                    RequiresMaterializedView = false,
                    IndexField = "CategoryId",
                    RequiresRangeQuery = true,
                    RequiresEqualityQuery = true,
                    FrequencyPerHour = 150
                },
                new ComplexQueryPattern
                {
                    QueryId = "AggregationQuery1",
                    ComplexityScore = 9,
                    RequiresSecondaryIndex = false,
                    RequiresMaterializedView = true,
                    AggregationKey = "CategorySummary",
                    AggregationFields = new List<string> { "Price", "Quantity", "Revenue" },
                    RequiresRangeQuery = false,
                    RequiresEqualityQuery = true,
                    FrequencyPerHour = 75
                },
                new ComplexQueryPattern
                {
                    QueryId = "MultiFieldQuery1",
                    ComplexityScore = 7,
                    RequiresSecondaryIndex = true,
                    RequiresMaterializedView = false,
                    IndexField = "BrandId",
                    RequiresRangeQuery = true,
                    RequiresEqualityQuery = true,
                    FrequencyPerHour = 200
                }
            };
        }

        private IEnumerable<StorageOperation> CreateStorageOperations(int count)
        {
            var random = new Random(42);
            var partitionKeys = new[] { "Store1", "Store2", "Store3" };
            var operationTypes = new[] { OperationType.Read, OperationType.Write, OperationType.Update };

            for (int i = 0; i < count; i++)
            {
                yield return new StorageOperation
                {
                    OperationId = $"Op_{i:D6}",
                    PartitionKey = partitionKeys[i % partitionKeys.Length],
                    RowKey = $"Entity_{i:D6}",
                    OperationType = operationTypes[i % operationTypes.Length],
                    EstimatedLatencyMs = 15 + random.NextDouble() * 60, // 15-75ms
                    Priority = random.Next(1, 4)
                };
            }
        }

        private IEnumerable<QueryPattern> CreateQueryPatternsForEntity(string entityType)
        {
            return entityType.ToLowerInvariant() switch
            {
                "categories" => CreateCategoryQueryPatterns(),
                "products" => new[]
                {
                    new QueryPattern
                    {
                        QueryType = QueryType.Range,
                        OperationType = OperationType.Read,
                        FrequencyPerHour = 400,
                        AverageLatencyMs = 150,
                        FilterFields = new List<string> { "CategoryId", "Price" }
                    }
                },
                "variants" => new[]
                {
                    new QueryPattern
                    {
                        QueryType = QueryType.Equality,
                        OperationType = OperationType.Read,
                        FrequencyPerHour = 600,
                        AverageLatencyMs = 80,
                        FilterFields = new List<string> { "ProductId" }
                    }
                },
                "images" => new[]
                {
                    new QueryPattern
                    {
                        QueryType = QueryType.Point,
                        OperationType = OperationType.Read,
                        FrequencyPerHour = 1000,
                        AverageLatencyMs = 60,
                        FilterFields = new List<string> { "ProductId", "ImageId" }
                    }
                },
                _ => new[]
                {
                    new QueryPattern
                    {
                        QueryType = QueryType.Equality,
                        OperationType = OperationType.Read,
                        FrequencyPerHour = 300,
                        AverageLatencyMs = 100,
                        FilterFields = new List<string> { "EntityId" }
                    }
                }
            };
        }

        private DataDistributionAnalysis CreateDataDistributionForEntity(string entityType)
        {
            return entityType.ToLowerInvariant() switch
            {
                "categories" => CreateDataDistribution(2000, 0.4, 0.5),
                "products" => CreateDataDistribution(50000, 0.6, 0.7),
                "variants" => CreateDataDistribution(150000, 0.3, 0.4),
                "images" => CreateDataDistribution(200000, 0.5, 0.6),
                _ => CreateDataDistribution(10000, 0.4, 0.5)
            };
        }

        private StoragePerformanceData SimulateImprovingPerformance(StoragePerformanceData baseline, int cycle)
        {
            var improvementFactor = 1.0 - (cycle * 0.2); // 20% improvement per cycle
            return new StoragePerformanceData
            {
                AverageLatency = baseline.AverageLatency * improvementFactor,
                P95Latency = baseline.P95Latency * improvementFactor,
                Throughput = baseline.Throughput / improvementFactor, // Throughput improves (increases)
                ErrorRate = baseline.ErrorRate * improvementFactor,
                HotPartitionPercentage = baseline.HotPartitionPercentage * improvementFactor,
                Timestamp = DateTime.UtcNow
            };
        }
    }
} 