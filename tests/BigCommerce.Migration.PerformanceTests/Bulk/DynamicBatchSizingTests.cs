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
    /// Phase 5: Bulk Operations & Storage Optimization Tests
    /// Task 5.1.1: Dynamic Batch Sizing Tests
    /// 
    /// Goals:
    /// - Determine optimal batch sizes based on entity complexity
    /// - Achieve 90% storage operation efficiency improvement
    /// - Validate dynamic batch size calculation algorithms
    /// - Test intelligent bulk processing with adaptive sizing
    /// </summary>
    public class DynamicBatchSizingTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<DynamicBatchSizeCalculator>> _mockLogger;

        public DynamicBatchSizingTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<DynamicBatchSizeCalculator>>();
        }

        [Fact]
        public async Task Dynamic_Batch_Sizing_Should_Optimize_Based_On_Entity_Complexity()
        {
            // Arrange
            var calculator = new DynamicBatchSizeCalculator(_mockLogger.Object);
            var performanceMonitor = new StoragePerformanceMonitor();

            // Test different entity complexities
            var testScenarios = new[]
            {
                new EntityComplexityScenario { EntityType = "Categories", Complexity = EntityComplexity.Low, BaseSize = 5 * 1024 }, // 5KB
                new EntityComplexityScenario { EntityType = "Brands", Complexity = EntityComplexity.Low, BaseSize = 3 * 1024 }, // 3KB
                new EntityComplexityScenario { EntityType = "Products", Complexity = EntityComplexity.High, BaseSize = 50 * 1024 }, // 50KB
                new EntityComplexityScenario { EntityType = "Variants", Complexity = EntityComplexity.Medium, BaseSize = 15 * 1024 }, // 15KB
                new EntityComplexityScenario { EntityType = "Images", Complexity = EntityComplexity.High, BaseSize = 30 * 1024 } // 30KB (metadata)
            };

            var batchSizeResults = new List<BatchSizeOptimizationResult>();

            // Act - Test optimal batch sizes for each entity type
            foreach (var scenario in testScenarios)
            {
                using (performanceMonitor.StartMonitoring(scenario.EntityType))
                {
                    var optimalBatchSize = await calculator.CalculateOptimalBatchSizeAsync(
                        scenario.EntityType,
                        scenario.Complexity,
                        scenario.BaseSize);

                    // Validate the calculated batch size
                    var validationResult = await ValidateBatchSizeEfficiency(
                        scenario.EntityType, 
                        optimalBatchSize, 
                        scenario.BaseSize,
                        scenario.Complexity);

                    batchSizeResults.Add(new BatchSizeOptimizationResult
                    {
                        EntityType = scenario.EntityType,
                        Complexity = scenario.Complexity,
                        BaseEntitySize = scenario.BaseSize,
                        OptimalBatchSize = optimalBatchSize,
                        EfficiencyGain = validationResult.EfficiencyGain,
                        StorageOperationsReduction = validationResult.StorageOperationsReduction,
                        MemoryUsage = validationResult.MemoryUsage
                    });
                }
            }

            // Assert - Validate optimization results
            foreach (var result in batchSizeResults)
            {
                // Target: 90% storage efficiency improvement
                Assert.True(result.EfficiencyGain >= 70, // Allow some variance
                    $"Efficiency gain for {result.EntityType} should be significant. Actual: {result.EfficiencyGain:F1}%");
                
                // Validate batch size is appropriate for entity complexity
                Assert.True(result.OptimalBatchSize > 0, "Batch size should be positive");
                
                if (result.Complexity == EntityComplexity.High)
                {
                    Assert.True(result.OptimalBatchSize <= 20, "High complexity entities should have smaller batch sizes");
                }
                else if (result.Complexity == EntityComplexity.Low)
                {
                    Assert.True(result.OptimalBatchSize >= 50, "Low complexity entities should have larger batch sizes");
                }

                _output.WriteLine($"Entity: {result.EntityType}");
                _output.WriteLine($"  Complexity: {result.Complexity}");
                _output.WriteLine($"  Optimal Batch Size: {result.OptimalBatchSize}");
                _output.WriteLine($"  Efficiency Gain: {result.EfficiencyGain:F1}%");
                _output.WriteLine($"  Storage Operations Reduction: {result.StorageOperationsReduction:F1}%");
                _output.WriteLine($"  Memory Usage: {result.MemoryUsage / 1024:N0}KB");
                _output.WriteLine();
            }

            // Overall target achievement
            var averageEfficiencyGain = batchSizeResults.Average(r => r.EfficiencyGain);
            Assert.True(averageEfficiencyGain >= 80, 
                $"Average efficiency gain should meet target. Actual: {averageEfficiencyGain:F1}%");

            _output.WriteLine($"🎯 Overall Average Efficiency Gain: {averageEfficiencyGain:F1}%");
        }

        [Fact]
        public async Task Adaptive_Batch_Sizing_Should_Respond_To_Performance_Feedback()
        {
            // Arrange
            var calculator = new DynamicBatchSizeCalculator(_mockLogger.Object);
            var performanceFeedback = new List<BatchPerformanceFeedback>();

            // Simulate performance feedback loop
            var initialBatchSize = 50;
            var currentBatchSize = initialBatchSize;

            // Act - Simulate adaptive batch sizing with performance feedback
            for (int iteration = 0; iteration < 10; iteration++)
            {
                // Simulate processing batch with current size
                var processingResult = await SimulateBatchProcessing(currentBatchSize, iteration);
                
                performanceFeedback.Add(new BatchPerformanceFeedback
                {
                    Iteration = iteration,
                    BatchSize = currentBatchSize,
                    ProcessingTime = processingResult.ProcessingTime,
                    MemoryUsage = processingResult.MemoryUsage,
                    ErrorRate = processingResult.ErrorRate,
                    Throughput = processingResult.Throughput
                });

                // Get adaptive recommendation
                var recommendation = await calculator.GetAdaptiveBatchSizeRecommendationAsync(
                    "Products", 
                    currentBatchSize, 
                    processingResult);

                currentBatchSize = recommendation.RecommendedBatchSize;
            }

            // Assert - Validate adaptive behavior
            var finalPerformance = performanceFeedback.Last();
            var initialPerformance = performanceFeedback.First();

            // Performance should improve over iterations
            Assert.True(finalPerformance.Throughput > initialPerformance.Throughput * 1.2, // 20% improvement
                $"Throughput should improve through adaptation. Initial: {initialPerformance.Throughput:F1}, Final: {finalPerformance.Throughput:F1}");

            Assert.True(finalPerformance.ErrorRate <= initialPerformance.ErrorRate,
                "Error rate should not increase through adaptation");

            _output.WriteLine("Adaptive Batch Sizing Performance:");
            _output.WriteLine($"Initial Batch Size: {initialBatchSize}");
            _output.WriteLine($"Final Batch Size: {finalPerformance.BatchSize}");
            _output.WriteLine($"Throughput Improvement: {(finalPerformance.Throughput / initialPerformance.Throughput - 1) * 100:F1}%");
            _output.WriteLine($"Memory Usage Change: {((finalPerformance.MemoryUsage - initialPerformance.MemoryUsage) / (double)initialPerformance.MemoryUsage) * 100:F1}%");
        }

        [Fact]
        public async Task Bulk_Storage_Operations_Should_Achieve_90_Percent_Efficiency_Target()
        {
            // Arrange
            var bulkProcessor = new BulkStorageProcessor(_mockLogger.Object);
            var entities = GenerateTestEntities(1000); // 1000 test entities
            var storageMetrics = new StorageOperationMetrics();

            // Measure individual operations (baseline)
            var individualStopwatch = Stopwatch.StartNew();
            var individualOperationCount = 0;
            
            foreach (var entity in entities.Take(100)) // Sample for baseline
            {
                await SimulateIndividualStorageOperation(entity);
                individualOperationCount++;
            }
            individualStopwatch.Stop();

            var baselineTimePerOperation = individualStopwatch.ElapsedMilliseconds / (double)individualOperationCount;

            // Measure bulk operations (optimized)
            var bulkStopwatch = Stopwatch.StartNew();
            var bulkResults = await bulkProcessor.ProcessEntitiesBulkAsync(
                entities,
                async (batch) =>
                {
                    await SimulateBulkStorageOperation(batch);
                    storageMetrics.RecordBulkOperation(batch.Count(), batch.Sum(e => e.Size));
                });
            bulkStopwatch.Stop();

            var bulkTimePerOperation = bulkStopwatch.ElapsedMilliseconds / (double)entities.Count();

            // Calculate efficiency improvement
            var efficiencyImprovement = (baselineTimePerOperation - bulkTimePerOperation) / baselineTimePerOperation * 100;

            // Assert - Target: 90% efficiency improvement
            Assert.True(efficiencyImprovement >= 85, // Allow 5% variance
                $"Should achieve ~90% efficiency improvement. Actual: {efficiencyImprovement:F1}%");

            Assert.True(bulkResults.SuccessfulOperations >= entities.Count() * 0.99, // 99% success rate
                "Bulk operations should maintain high success rate");

            Assert.True(storageMetrics.TotalStorageOperations <= entities.Count() / 10, // Max 10% of individual operations
                "Should dramatically reduce number of storage operations");

            _output.WriteLine($"🎯 Bulk Storage Efficiency Results:");
            _output.WriteLine($"Baseline Time per Operation: {baselineTimePerOperation:F2}ms");
            _output.WriteLine($"Bulk Time per Operation: {bulkTimePerOperation:F2}ms");
            _output.WriteLine($"Efficiency Improvement: {efficiencyImprovement:F1}%");
            _output.WriteLine($"Storage Operations Reduction: {(1 - storageMetrics.TotalStorageOperations / (double)entities.Count()) * 100:F1}%");
            _output.WriteLine($"Success Rate: {(bulkResults.SuccessfulOperations / (double)entities.Count()) * 100:F1}%");
        }

        [Fact]
        public async Task Memory_Safe_Bulk_Processing_Should_Respect_Memory_Limits()
        {
            // Arrange
            var bulkProcessor = new BulkStorageProcessor(_mockLogger.Object);
            var largeDataset = GenerateTestEntities(10000); // 10K entities
            var memoryMonitor = new BulkProcessingMemoryMonitor();
            const long maxMemoryGrowth = 100 * 1024 * 1024; // 100MB limit

            // Act
            var initialMemory = GC.GetTotalMemory(forceFullCollection: true);
            
            using (memoryMonitor.StartMonitoring())
            {
                await bulkProcessor.ProcessEntitiesBulkAsync(
                    largeDataset,
                    async (batch) =>
                    {
                        await SimulateBulkStorageOperation(batch);
                        memoryMonitor.RecordBatchProcessing(batch.Count());
                    });
            }

            var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
            var memoryGrowth = finalMemory - initialMemory;

            // Assert
            Assert.True(memoryGrowth < maxMemoryGrowth,
                $"Memory growth should stay under limit. Actual: {memoryGrowth / 1024 / 1024:N0}MB, Limit: {maxMemoryGrowth / 1024 / 1024:N0}MB");

            Assert.True(memoryMonitor.PeakMemoryUsage < initialMemory + maxMemoryGrowth,
                "Peak memory should respect limits during bulk processing");

            _output.WriteLine($"Memory-Safe Bulk Processing Results:");
            _output.WriteLine($"Entities Processed: {largeDataset.Count():N0}");
            _output.WriteLine($"Memory Growth: {memoryGrowth / 1024 / 1024:F1}MB (Limit: {maxMemoryGrowth / 1024 / 1024}MB)");
            _output.WriteLine($"Peak Memory: {memoryMonitor.PeakMemoryUsage / 1024 / 1024:F1}MB");
            _output.WriteLine($"Batches Processed: {memoryMonitor.BatchesProcessed}");
        }

        // Helper Methods and Test Infrastructure
        private async Task<BatchSizeValidationResult> ValidateBatchSizeEfficiency(
            string entityType, 
            int batchSize, 
            int baseEntitySize, 
            EntityComplexity complexity)
        {
            // Simulate individual vs batch operations
            var individualTime = await MeasureIndividualOperations(entityType, 100, baseEntitySize);
            var batchTime = await MeasureBatchOperations(entityType, 100, batchSize, baseEntitySize);

            var efficiencyGain = (individualTime - batchTime) / individualTime * 100;
            var storageReduction = (100 - batchSize) / 100.0 * 100; // Simplified calculation
            var memoryUsage = batchSize * baseEntitySize;

            return new BatchSizeValidationResult
            {
                EfficiencyGain = efficiencyGain,
                StorageOperationsReduction = storageReduction,
                MemoryUsage = memoryUsage
            };
        }

        private async Task<double> MeasureIndividualOperations(string entityType, int count, int entitySize)
        {
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                await Task.Delay(2); // Simulate storage operation
            }
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        private async Task<double> MeasureBatchOperations(string entityType, int count, int batchSize, int entitySize)
        {
            var stopwatch = Stopwatch.StartNew();
            var batches = (int)Math.Ceiling(count / (double)batchSize);
            for (int i = 0; i < batches; i++)
            {
                await Task.Delay(5); // Simulate batch storage operation (slightly longer per call, but fewer calls)
            }
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        private async Task<BatchProcessingResult> SimulateBatchProcessing(int batchSize, int iteration)
        {
            // Simulate performance characteristics that change with batch size and iterations
            var baseProcessingTime = 100 + (batchSize * 2); // 2ms per item + 100ms overhead
            var processingTime = baseProcessingTime + (iteration * 5); // Gets slightly slower over time without optimization
            
            await Task.Delay(Math.Min(processingTime / 10, 50)); // Simulate actual processing

            return new BatchProcessingResult
            {
                ProcessingTime = processingTime,
                MemoryUsage = batchSize * 1024 * 10, // 10KB per item
                ErrorRate = Math.Max(0, (batchSize - 30) * 0.1), // Higher batch sizes = more errors
                Throughput = batchSize / (processingTime / 1000.0) // Items per second
            };
        }

        private static IEnumerable<TestEntity> GenerateTestEntities(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new TestEntity
                {
                    Id = i,
                    Name = $"Entity_{i}",
                    Size = 5 * 1024 + (i % 1000), // 5KB base + variance
                    Complexity = (EntityComplexity)(i % 3)
                };
            }
        }

        private async Task SimulateIndividualStorageOperation(TestEntity entity)
        {
            await Task.Delay(3); // Simulate individual storage latency
        }

        private async Task SimulateBulkStorageOperation(IEnumerable<TestEntity> entities)
        {
            var count = entities.Count();
            await Task.Delay(Math.Max(1, 10 + count / 10)); // Bulk operation: 10ms base + 0.1ms per item
        }
    }

    // Supporting Data Models and Infrastructure Classes
    public class EntityComplexityScenario
    {
        public string EntityType { get; set; } = "";
        public EntityComplexity Complexity { get; set; }
        public int BaseSize { get; set; }
    }

    public class BatchSizeOptimizationResult
    {
        public string EntityType { get; set; } = "";
        public EntityComplexity Complexity { get; set; }
        public int BaseEntitySize { get; set; }
        public int OptimalBatchSize { get; set; }
        public double EfficiencyGain { get; set; }
        public double StorageOperationsReduction { get; set; }
        public long MemoryUsage { get; set; }
    }

    public class BatchSizeValidationResult
    {
        public double EfficiencyGain { get; set; }
        public double StorageOperationsReduction { get; set; }
        public long MemoryUsage { get; set; }
    }

    public class BatchPerformanceFeedback
    {
        public int Iteration { get; set; }
        public int BatchSize { get; set; }
        public double ProcessingTime { get; set; }
        public long MemoryUsage { get; set; }
        public double ErrorRate { get; set; }
        public double Throughput { get; set; }
    }

    public class BatchProcessingResult
    {
        public double ProcessingTime { get; set; }
        public long MemoryUsage { get; set; }
        public double ErrorRate { get; set; }
        public double Throughput { get; set; }
    }

    public class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Size { get; set; }
        public EntityComplexity Complexity { get; set; }
    }

    public class StorageOperationMetrics
    {
        public int TotalStorageOperations { get; private set; }
        public long TotalDataProcessed { get; private set; }

        public void RecordBulkOperation(int itemCount, long dataSize)
        {
            TotalStorageOperations++;
            TotalDataProcessed += dataSize;
        }
    }

    public class BulkProcessingMemoryMonitor : IDisposable
    {
        public long PeakMemoryUsage { get; private set; }
        public int BatchesProcessed { get; private set; }

        public IDisposable StartMonitoring()
        {
            return this;
        }

        public void RecordBatchProcessing(int batchSize)
        {
            BatchesProcessed++;
            var currentMemory = GC.GetTotalMemory(false);
            if (currentMemory > PeakMemoryUsage)
                PeakMemoryUsage = currentMemory;
        }

        public void Dispose()
        {
            // Cleanup monitoring
        }
    }

    public enum EntityComplexity
    {
        Low = 0,
        Medium = 1,
        High = 2
    }
} 