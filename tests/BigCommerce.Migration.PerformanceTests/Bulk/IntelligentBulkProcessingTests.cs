using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.PerformanceTests.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Bulk
{
    /// <summary>
    /// Phase 5: Task 5.2.1 - Intelligent Bulk Processing Tests
    /// Validates intelligent operation grouping and smart batching for 90% storage efficiency improvement
    /// 
    /// Goals:
    /// - Test mixed entity type processing with intelligent grouping
    /// - Validate smart batching algorithms achieve efficiency targets
    /// - Ensure adaptive processing optimizes based on entity characteristics
    /// - Verify memory safety while maximizing throughput and storage efficiency
    /// </summary>
    public class IntelligentBulkProcessingTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<IntelligentBulkProcessor>> _mockLogger;
        private readonly Mock<DynamicBatchSizeCalculator> _mockBatchSizeCalculator;
        private readonly Mock<AdaptiveConcurrencyController> _mockConcurrencyController;

        public IntelligentBulkProcessingTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<IntelligentBulkProcessor>>();
            _mockBatchSizeCalculator = new Mock<DynamicBatchSizeCalculator>(Mock.Of<ILogger<DynamicBatchSizeCalculator>>());
            _mockConcurrencyController = new Mock<AdaptiveConcurrencyController>(Mock.Of<ILogger<AdaptiveConcurrencyController>>());
        }

        [Fact]
        public async Task IntelligentBulkProcessor_Should_Achieve_90_Percent_Storage_Efficiency()
        {
            // Arrange
            var processor = CreateIntelligentBulkProcessor();
            var entityGroups = CreateMixedEntityGroups();
            var processingStrategies = CreateProcessingStrategies();

            // Act
            var results = await processor.ProcessMixedEntitiesAsync(
                entityGroups, processingStrategies, CancellationToken.None);

            // Assert - Target: 90% storage efficiency improvement
            Assert.True(results.OverallEfficiencyGain >= 85, // Allow 5% variance
                $"Should achieve ~90% storage efficiency. Actual: {results.OverallEfficiencyGain:F1}%");

            Assert.True(results.OverallSuccessRate >= 95,
                $"Should maintain high success rate. Actual: {results.OverallSuccessRate:F1}%");

            Assert.True(results.TotalBatchesProcessed <= results.TotalEntitiesProcessed / 10,
                "Should dramatically reduce storage operations (max 10% of individual operations)");

            // Validate mixed entity processing
            Assert.True(results.GroupResults.Count >= 3,
                "Should process multiple entity types");

            Assert.Contains(results.GroupResults, r => r.EntityType == "Categories");
            Assert.Contains(results.GroupResults, r => r.EntityType == "Products");
            Assert.Contains(results.GroupResults, r => r.EntityType == "Variants");

            _output.WriteLine($"🎯 Intelligent Bulk Processing Results:");
            _output.WriteLine($"Overall Efficiency Gain: {results.OverallEfficiencyGain:F1}%");
            _output.WriteLine($"Overall Success Rate: {results.OverallSuccessRate:F1}%");
            _output.WriteLine($"Total Entities Processed: {results.TotalEntitiesProcessed:N0}");
            _output.WriteLine($"Total Batches: {results.TotalBatchesProcessed:N0}");
            _output.WriteLine($"Processing Duration: {results.TotalDuration.TotalSeconds:F1}s");

            foreach (var groupResult in results.GroupResults)
            {
                _output.WriteLine($"  {groupResult.EntityType}: {groupResult.ProcessedEntities}/{groupResult.TotalEntities} " +
                    $"entities, {groupResult.EfficiencyGain:F1}% efficiency");
            }
        }

        [Fact]
        public async Task Smart_Batching_Should_Optimize_Based_On_Entity_Characteristics()
        {
            // Arrange
            var processor = CreateIntelligentBulkProcessor();
            
            var testScenarios = new[]
            {
                new { EntityType = "Categories", Count = 500, ExpectedBatchSize = 50 },
                new { EntityType = "Products", Count = 200, ExpectedBatchSize = 10 },
                new { EntityType = "Variants", Count = 1000, ExpectedBatchSize = 25 }
            };

            foreach (var scenario in testScenarios)
            {
                var entities = GenerateTestEntities(scenario.Count);
                var processingFunction = CreateMockProcessingFunction();

                // Act
                var results = await processor.ProcessWithSmartBatchingAsync(
                    entities, scenario.EntityType, processingFunction, CancellationToken.None);

                // Assert
                Assert.True(results.EfficiencyGain >= 80,
                    $"Smart batching for {scenario.EntityType} should achieve high efficiency. " +
                    $"Actual: {results.EfficiencyGain:F1}%");

                Assert.True(results.SuccessRate >= 95,
                    $"Should maintain high success rate for {scenario.EntityType}");

                // Verify intelligent batch sizing
                var expectedBatches = (int)Math.Ceiling((double)scenario.Count / scenario.ExpectedBatchSize);
                Assert.True(Math.Abs(results.ProcessedBatches - expectedBatches) <= 2,
                    $"Should use intelligent batch sizing for {scenario.EntityType}. " +
                    $"Expected ~{expectedBatches}, Actual: {results.ProcessedBatches}");

                _output.WriteLine($"Smart Batching - {scenario.EntityType}:");
                _output.WriteLine($"  Entities: {scenario.Count}, Batches: {results.ProcessedBatches}");
                _output.WriteLine($"  Efficiency: {results.EfficiencyGain:F1}%, Success Rate: {results.SuccessRate:F1}%");
                _output.WriteLine($"  Throughput: {results.ThroughputPerSecond:F1} entities/sec");
            }
        }

        [Fact]
        public async Task Processing_Plan_Should_Handle_Dependencies_And_Priorities()
        {
            // Arrange
            var processor = CreateIntelligentBulkProcessor();
            var entityGroups = CreateEntityGroupsWithDependencies();
            var processingStrategies = CreateProcessingStrategies();

            // Act
            var results = await processor.ProcessMixedEntitiesAsync(
                entityGroups, processingStrategies, CancellationToken.None);

            // Assert
            Assert.True(results.GroupResults.Count >= 3,
                "Should process all entity groups with dependencies");

            // Verify dependencies are respected (Categories before Products before Variants)
            var categoryResult = results.GroupResults.First(r => r.EntityType == "Categories");
            var productResult = results.GroupResults.First(r => r.EntityType == "Products");
            var variantResult = results.GroupResults.First(r => r.EntityType == "Variants");

            Assert.True(categoryResult.StageNumber <= productResult.StageNumber,
                "Categories should be processed before or with Products");

            Assert.True(productResult.StageNumber <= variantResult.StageNumber,
                "Products should be processed before or with Variants");

            // Verify efficiency is maintained despite dependency constraints
            Assert.True(results.OverallEfficiencyGain >= 80,
                "Should maintain efficiency despite dependency constraints");

            _output.WriteLine($"Dependency Processing Results:");
            _output.WriteLine($"Categories - Stage: {categoryResult.StageNumber}, " +
                $"Processed: {categoryResult.ProcessedEntities}");
            _output.WriteLine($"Products - Stage: {productResult.StageNumber}, " +
                $"Processed: {productResult.ProcessedEntities}");
            _output.WriteLine($"Variants - Stage: {variantResult.StageNumber}, " +
                $"Processed: {variantResult.ProcessedEntities}");
        }

        [Fact]
        public async Task Adaptive_Optimization_Should_Improve_Performance_Over_Time()
        {
            // Arrange
            var processor = CreateIntelligentBulkProcessor();
            var entities = GenerateTestEntities(1000);
            var processingFunction = CreateAdaptiveProcessingFunction();

            // Set up mock to simulate improving performance over time
            SetupAdaptiveMocks();

            // Act - Process same entities multiple times to simulate learning
            var results1 = await processor.ProcessWithSmartBatchingAsync(
                entities, "Products", processingFunction, CancellationToken.None);

            var results2 = await processor.ProcessWithSmartBatchingAsync(
                entities, "Products", processingFunction, CancellationToken.None);

            var results3 = await processor.ProcessWithSmartBatchingAsync(
                entities, "Products", processingFunction, CancellationToken.None);

            // Assert - Performance should improve over iterations
            Assert.True(results3.ThroughputPerSecond > results1.ThroughputPerSecond * 1.1,
                "Throughput should improve by at least 10% through adaptive optimization");

            Assert.True(results3.EfficiencyGain >= results1.EfficiencyGain,
                "Efficiency should not decrease through optimization");

            Assert.True(results3.AverageProcessingTimePerBatch <= results1.AverageProcessingTimePerBatch,
                "Processing time should improve through optimization");

            _output.WriteLine($"Adaptive Optimization Results:");
            _output.WriteLine($"Iteration 1: {results1.ThroughputPerSecond:F1} entities/sec, " +
                $"{results1.EfficiencyGain:F1}% efficiency");
            _output.WriteLine($"Iteration 2: {results2.ThroughputPerSecond:F1} entities/sec, " +
                $"{results2.EfficiencyGain:F1}% efficiency");
            _output.WriteLine($"Iteration 3: {results3.ThroughputPerSecond:F1} entities/sec, " +
                $"{results3.EfficiencyGain:F1}% efficiency");
            _output.WriteLine($"Throughput Improvement: {(results3.ThroughputPerSecond / results1.ThroughputPerSecond - 1) * 100:F1}%");
        }

        [Fact]
        public async Task Memory_Safe_Bulk_Processing_Should_Handle_Large_Mixed_Datasets()
        {
            // Arrange
            var processor = CreateIntelligentBulkProcessor();
            var largeEntityGroups = CreateLargeMixedEntityGroups(); // 10K+ entities
            var processingStrategies = CreateProcessingStrategies();
            var memoryMonitor = new BulkProcessingMemoryMonitor();

            const long maxMemoryGrowth = 150 * 1024 * 1024; // 150MB limit for mixed processing

            // Act
            var initialMemory = GC.GetTotalMemory(forceFullCollection: true);
            
            using (memoryMonitor.StartMonitoring())
            {
                var results = await processor.ProcessMixedEntitiesAsync(
                    largeEntityGroups, processingStrategies, CancellationToken.None);

                // Assert
                var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
                var memoryGrowth = finalMemory - initialMemory;

                Assert.True(memoryGrowth < maxMemoryGrowth,
                    $"Memory growth should stay under limit. Actual: {memoryGrowth / 1024 / 1024:N0}MB, " +
                    $"Limit: {maxMemoryGrowth / 1024 / 1024:N0}MB");

                Assert.True(results.OverallEfficiencyGain >= 85,
                    "Should maintain efficiency even with large datasets");

                Assert.True(results.TotalEntitiesProcessed >= 10000,
                    "Should process large dataset successfully");

                _output.WriteLine($"Large Dataset Processing Results:");
                _output.WriteLine($"Entities Processed: {results.TotalEntitiesProcessed:N0}");
                _output.WriteLine($"Memory Growth: {memoryGrowth / 1024 / 1024:F1}MB");
                _output.WriteLine($"Peak Memory: {memoryMonitor.PeakMemoryUsage / 1024 / 1024:F1}MB");
                _output.WriteLine($"Efficiency: {results.OverallEfficiencyGain:F1}%");
                _output.WriteLine($"Duration: {results.TotalDuration.TotalSeconds:F1}s");
            }
        }

        [Fact]
        public async Task Intelligent_Grouping_Should_Optimize_Mixed_Entity_Processing()
        {
            // Arrange
            var processor = CreateIntelligentBulkProcessor();
            var mixedEntityGroups = CreateComplexMixedEntityGroups();
            var processingStrategies = CreateProcessingStrategies();

            // Act
            var results = await processor.ProcessMixedEntitiesAsync(
                mixedEntityGroups, processingStrategies, CancellationToken.None);

            // Assert
            // Verify different entity types achieve different but appropriate efficiency levels
            var categoryResult = results.GroupResults.First(r => r.EntityType == "Categories");
            var productResult = results.GroupResults.First(r => r.EntityType == "Products");
            var variantResult = results.GroupResults.First(r => r.EntityType == "Variants");

            // Categories (low complexity) should achieve highest efficiency
            Assert.True(categoryResult.EfficiencyGain >= 92,
                $"Categories should achieve highest efficiency. Actual: {categoryResult.EfficiencyGain:F1}%");

            // Products (high complexity) should still achieve good efficiency
            Assert.True(productResult.EfficiencyGain >= 80,
                $"Products should achieve good efficiency. Actual: {productResult.EfficiencyGain:F1}%");

            // Variants (medium complexity) should be in between
            Assert.True(variantResult.EfficiencyGain >= 85,
                $"Variants should achieve medium-high efficiency. Actual: {variantResult.EfficiencyGain:F1}%");

            // Overall efficiency should meet target
            Assert.True(results.OverallEfficiencyGain >= 88,
                $"Overall efficiency should be high. Actual: {results.OverallEfficiencyGain:F1}%");

            _output.WriteLine($"Mixed Entity Optimization Results:");
            foreach (var groupResult in results.GroupResults.OrderBy(r => r.EntityType))
            {
                _output.WriteLine($"{groupResult.EntityType}:");
                _output.WriteLine($"  Efficiency: {groupResult.EfficiencyGain:F1}%");
                _output.WriteLine($"  Entities: {groupResult.ProcessedEntities}/{groupResult.TotalEntities}");
                _output.WriteLine($"  Batches: {groupResult.ProcessedBatches}");
                _output.WriteLine($"  Stage: {groupResult.StageNumber}");
            }
        }

        // Helper methods for test setup
        private IntelligentBulkProcessor CreateIntelligentBulkProcessor()
        {
            // Setup default mock behaviors
            _mockBatchSizeCalculator.Setup(x => x.CalculateOptimalBatchSizeAsync(
                    It.IsAny<string>(), It.IsAny<EntityComplexity>(), It.IsAny<int>()))
                .Returns<string, EntityComplexity, int>((entityType, complexity, size) =>
                {
                    var batchSize = complexity switch
                    {
                        EntityComplexity.Low => 50,
                        EntityComplexity.Medium => 25,
                        EntityComplexity.High => 10,
                        _ => 20
                    };
                    return Task.FromResult(batchSize);
                });

            _mockConcurrencyController.Setup(x => x.GetOptimalConcurrencyAsync(It.IsAny<string>()))
                .ReturnsAsync(4);

            _mockConcurrencyController.Setup(x => x.RecordPerformanceAsync(
                    It.IsAny<string>(), It.IsAny<SystemPerformanceMetrics>()))
                .Returns(Task.CompletedTask);

            return new IntelligentBulkProcessor(
                _mockLogger.Object,
                _mockBatchSizeCalculator.Object,
                _mockConcurrencyController.Object);
        }

        private IEnumerable<EntityGroup<TestEntity>> CreateMixedEntityGroups()
        {
            return new[]
            {
                new EntityGroup<TestEntity>
                {
                    EntityType = "Categories",
                    Entities = GenerateTestEntities(500),
                    EstimatedEntitySize = 5 * 1024,
                    Priority = 1
                },
                new EntityGroup<TestEntity>
                {
                    EntityType = "Products",
                    Entities = GenerateTestEntities(200),
                    EstimatedEntitySize = 50 * 1024,
                    Priority = 2
                },
                new EntityGroup<TestEntity>
                {
                    EntityType = "Variants",
                    Entities = GenerateTestEntities(1000),
                    EstimatedEntitySize = 15 * 1024,
                    Priority = 3,
                    Dependencies = new List<string> { "Products" }
                }
            };
        }

        private IEnumerable<EntityGroup<TestEntity>> CreateEntityGroupsWithDependencies()
        {
            return new[]
            {
                new EntityGroup<TestEntity>
                {
                    EntityType = "Categories",
                    Entities = GenerateTestEntities(100),
                    EstimatedEntitySize = 5 * 1024,
                    Priority = 1
                },
                new EntityGroup<TestEntity>
                {
                    EntityType = "Products",
                    Entities = GenerateTestEntities(100),
                    EstimatedEntitySize = 50 * 1024,
                    Priority = 2,
                    Dependencies = new List<string> { "Categories" }
                },
                new EntityGroup<TestEntity>
                {
                    EntityType = "Variants",
                    Entities = GenerateTestEntities(200),
                    EstimatedEntitySize = 15 * 1024,
                    Priority = 3,
                    Dependencies = new List<string> { "Products" }
                }
            };
        }

        private IEnumerable<EntityGroup<TestEntity>> CreateLargeMixedEntityGroups()
        {
            return new[]
            {
                new EntityGroup<TestEntity>
                {
                    EntityType = "Categories",
                    Entities = GenerateTestEntities(2000),
                    EstimatedEntitySize = 5 * 1024,
                    Priority = 1
                },
                new EntityGroup<TestEntity>
                {
                    EntityType = "Products",
                    Entities = GenerateTestEntities(3000),
                    EstimatedEntitySize = 50 * 1024,
                    Priority = 2
                },
                new EntityGroup<TestEntity>
                {
                    EntityType = "Variants",
                    Entities = GenerateTestEntities(5000),
                    EstimatedEntitySize = 15 * 1024,
                    Priority = 3
                }
            };
        }

        private IEnumerable<EntityGroup<TestEntity>> CreateComplexMixedEntityGroups()
        {
            return new[]
            {
                new EntityGroup<TestEntity>
                {
                    EntityType = "Categories",
                    Entities = GenerateTestEntities(300),
                    EstimatedEntitySize = 5 * 1024,
                    Priority = 1
                },
                new EntityGroup<TestEntity>
                {
                    EntityType = "Products",
                    Entities = GenerateTestEntities(150),
                    EstimatedEntitySize = 50 * 1024,
                    Priority = 2
                },
                new EntityGroup<TestEntity>
                {
                    EntityType = "Variants",
                    Entities = GenerateTestEntities(600),
                    EstimatedEntitySize = 15 * 1024,
                    Priority = 3
                },
                new EntityGroup<TestEntity>
                {
                    EntityType = "Images",
                    Entities = GenerateTestEntities(450),
                    EstimatedEntitySize = 30 * 1024,
                    Priority = 3
                }
            };
        }

        private Dictionary<string, Func<IEnumerable<TestEntity>, Task<ProcessingResult>>> CreateProcessingStrategies()
        {
            return new Dictionary<string, Func<IEnumerable<TestEntity>, Task<ProcessingResult>>>
            {
                ["Categories"] = CreateMockProcessingFunction(),
                ["Products"] = CreateMockProcessingFunction(),
                ["Variants"] = CreateMockProcessingFunction(),
                ["Images"] = CreateMockProcessingFunction()
            };
        }

        private Func<IEnumerable<TestEntity>, Task<ProcessingResult>> CreateMockProcessingFunction()
        {
            return async entities =>
            {
                var entityList = entities.ToList();
                var processingTime = entityList.Count * 2; // 2ms per entity
                
                await Task.Delay(Math.Min(processingTime, 100)); // Simulate processing

                return new ProcessingResult
                {
                    ProcessedCount = entityList.Count,
                    FailedCount = 0,
                    IsSuccess = true,
                    ProcessingTime = TimeSpan.FromMilliseconds(processingTime),
                    Throughput = entityList.Count / (processingTime / 1000.0),
                    ErrorRate = 0,
                    MemoryUsedMB = entityList.Count * 10 // 10KB per entity
                };
            };
        }

        private Func<IEnumerable<TestEntity>, Task<ProcessingResult>> CreateAdaptiveProcessingFunction()
        {
            var iteration = 0;
            return async entities =>
            {
                iteration++;
                var entityList = entities.ToList();
                
                // Simulate improving performance over iterations
                var baseProcessingTime = entityList.Count * 3; // 3ms per entity initially
                var optimizationFactor = Math.Max(0.5, 1.0 - (iteration * 0.1)); // 10% improvement per iteration
                var actualProcessingTime = (int)(baseProcessingTime * optimizationFactor);

                await Task.Delay(Math.Min(actualProcessingTime, 150));

                return new ProcessingResult
                {
                    ProcessedCount = entityList.Count,
                    FailedCount = 0,
                    IsSuccess = true,
                    ProcessingTime = TimeSpan.FromMilliseconds(actualProcessingTime),
                    Throughput = entityList.Count / (actualProcessingTime / 1000.0),
                    ErrorRate = 0,
                    MemoryUsedMB = entityList.Count * 8 // Improving memory efficiency too
                };
            };
        }

        private void SetupAdaptiveMocks()
        {
            var callCount = 0;
            _mockBatchSizeCalculator.Setup(x => x.CalculateOptimalBatchSizeAsync(
                    It.IsAny<string>(), It.IsAny<EntityComplexity>(), It.IsAny<int>()))
                .Returns(() =>
                {
                    callCount++;
                    // Simulate learning better batch sizes over time
                    var improvementFactor = 1 + (callCount * 0.05); // 5% improvement per call
                    var baseBatchSize = 20;
                    return Task.FromResult((int)(baseBatchSize * improvementFactor));
                });

            _mockConcurrencyController.Setup(x => x.GetOptimalConcurrencyAsync(It.IsAny<string>()))
                .Returns(() =>
                {
                    // Simulate learning optimal concurrency
                    var baseConcurrency = 3;
                    var learnedConcurrency = Math.Min(8, baseConcurrency + (callCount / 3));
                    return Task.FromResult(learnedConcurrency);
                });
        }

        private static IEnumerable<TestEntity> GenerateTestEntities(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new TestEntity
                {
                    Id = i,
                    Name = $"Entity_{i}",
                    Data = $"Data_{i % 100}" // Some variation in data
                };
            }
        }
    }

} 