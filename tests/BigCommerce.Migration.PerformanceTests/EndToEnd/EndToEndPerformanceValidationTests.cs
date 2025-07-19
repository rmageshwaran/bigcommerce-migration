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

namespace BigCommerce.Migration.PerformanceTests.EndToEnd
{
    /// <summary>
    /// Phase 5: Task 5.4 - End-to-End Performance Validation Tests
    /// Validates the complete performance optimization pipeline with all components working together
    /// 
    /// Goals:
    /// - Validate 60%+ overall performance improvement from combined optimizations
    /// - Test realistic BigCommerce migration scenarios (10K, 100K, 1M+ products)
    /// - Ensure seamless integration of all optimization components
    /// - Verify system reliability and scalability under load
    /// - Provide comprehensive performance benchmarking and analytics
    /// </summary>
    public class EndToEndPerformanceValidationTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<PerformanceIntegrationOrchestrator>> _mockLogger;

        public EndToEndPerformanceValidationTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<PerformanceIntegrationOrchestrator>>();
        }

        [Fact]
        public async Task End_To_End_Optimization_Should_Achieve_60_Percent_Overall_Performance_Improvement()
        {
            // Arrange
            var orchestrator = CreatePerformanceOrchestrator();
            var migrationScenario = CreateLargeMigrationScenario(); // 100K products scenario
            var baselineConfig = CreateBaselineConfiguration();
            var optimizedConfig = CreateOptimizedConfiguration();

            // Act - Test baseline performance (without optimizations)
            var baselineResults = await orchestrator.ExecuteMigrationPerformanceTestAsync(
                "BaselineTest", migrationScenario, baselineConfig, CancellationToken.None);

            // Act - Test optimized performance (with all optimizations)
            var optimizedResults = await orchestrator.ExecuteMigrationPerformanceTestAsync(
                "OptimizedTest", migrationScenario, optimizedConfig, CancellationToken.None);

            // Calculate overall performance improvement
            var performanceImprovement = CalculateOverallPerformanceImprovement(baselineResults, optimizedResults);

            // Assert - Target: 60%+ overall performance improvement
            Assert.True(performanceImprovement.OverallImprovement >= 60,
                $"Should achieve 60%+ overall performance improvement. Actual: {performanceImprovement.OverallImprovement:F1}%");

            Assert.True(performanceImprovement.ThroughputImprovement >= 50,
                $"Should achieve 50%+ throughput improvement. Actual: {performanceImprovement.ThroughputImprovement:F1}%");

            Assert.True(performanceImprovement.LatencyReduction >= 40,
                $"Should achieve 40%+ latency reduction. Actual: {performanceImprovement.LatencyReduction:F1}%");

            Assert.True(performanceImprovement.ResourceEfficiencyGain >= 30,
                $"Should achieve 30%+ resource efficiency gain. Actual: {performanceImprovement.ResourceEfficiencyGain:F1}%");

            Assert.True(optimizedResults.ReliabilityScore >= 0.95,
                $"Should maintain high reliability. Actual: {optimizedResults.ReliabilityScore:F1}");

            _output.WriteLine($"🎯 End-to-End Performance Validation Results:");
            _output.WriteLine($"Overall Performance Improvement: {performanceImprovement.OverallImprovement:F1}%");
            _output.WriteLine($"Throughput Improvement: {performanceImprovement.ThroughputImprovement:F1}%");
            _output.WriteLine($"Latency Reduction: {performanceImprovement.LatencyReduction:F1}%");
            _output.WriteLine($"Resource Efficiency Gain: {performanceImprovement.ResourceEfficiencyGain:F1}%");
            _output.WriteLine($"Baseline Duration: {baselineResults.TotalExecutionTime.TotalMinutes:F1} minutes");
            _output.WriteLine($"Optimized Duration: {optimizedResults.TotalExecutionTime.TotalMinutes:F1} minutes");
            _output.WriteLine($"Reliability Score: {optimizedResults.ReliabilityScore:F1}");
        }

        [Fact]
        public async Task Small_Enterprise_Migration_Should_Complete_Within_Performance_Targets()
        {
            // Arrange
            var orchestrator = CreatePerformanceOrchestrator();
            var smallMigrationScenario = CreateSmallMigrationScenario(); // 10K products
            var config = CreateOptimizedConfiguration();

            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await orchestrator.ExecuteMigrationPerformanceTestAsync(
                "SmallEnterprise", smallMigrationScenario, config, CancellationToken.None);
            stopwatch.Stop();

            // Assert - Small enterprise targets
            Assert.True(results.TotalExecutionTime.TotalMinutes <= 30,
                $"Small migration should complete within 30 minutes. Actual: {results.TotalExecutionTime.TotalMinutes:F1} minutes");

            Assert.True(results.ThroughputProductsPerSecond >= 10,
                $"Should achieve 10+ products/second. Actual: {results.ThroughputProductsPerSecond:F1}");

            Assert.True(results.SuccessRate >= 0.98,
                $"Should achieve 98%+ success rate. Actual: {results.SuccessRate:P1}");

            Assert.True(results.AverageResponseTime <= 100,
                $"Should maintain low response time. Actual: {results.AverageResponseTime:F1}ms");

            Assert.True(results.ResourceUtilization.MemoryEfficiency >= 0.8,
                $"Should achieve memory efficiency. Actual: {results.ResourceUtilization.MemoryEfficiency:F1}");

            _output.WriteLine($"🎯 Small Enterprise Migration Results:");
            _output.WriteLine($"Products Migrated: {smallMigrationScenario.TotalProducts:N0}");
            _output.WriteLine($"Execution Time: {results.TotalExecutionTime.TotalMinutes:F1} minutes");
            _output.WriteLine($"Throughput: {results.ThroughputProductsPerSecond:F1} products/sec");
            _output.WriteLine($"Success Rate: {results.SuccessRate:P1}");
            _output.WriteLine($"Average Response Time: {results.AverageResponseTime:F1}ms");
            _output.WriteLine($"Memory Efficiency: {results.ResourceUtilization.MemoryEfficiency:P1}");
        }

        [Fact]
        public async Task Medium_Enterprise_Migration_Should_Scale_Efficiently()
        {
            // Arrange
            var orchestrator = CreatePerformanceOrchestrator();
            var mediumMigrationScenario = CreateMediumMigrationScenario(); // 100K products
            var config = CreateOptimizedConfiguration();

            // Act
            var results = await orchestrator.ExecuteMigrationPerformanceTestAsync(
                "MediumEnterprise", mediumMigrationScenario, config, CancellationToken.None);

            // Assert - Medium enterprise targets
            Assert.True(results.TotalExecutionTime.TotalHours <= 8,
                $"Medium migration should complete within 8 hours. Actual: {results.TotalExecutionTime.TotalHours:F1} hours");

            Assert.True(results.ThroughputProductsPerSecond >= 8,
                $"Should achieve 8+ products/second. Actual: {results.ThroughputProductsPerSecond:F1}");

            Assert.True(results.SuccessRate >= 0.97,
                $"Should achieve 97%+ success rate. Actual: {results.SuccessRate:P1}");

            Assert.True(results.ScalabilityScore >= 0.85,
                $"Should demonstrate good scalability. Actual: {results.ScalabilityScore:F1}");

            Assert.True(results.AdaptiveOptimizationEffectiveness >= 0.7,
                $"Should show adaptive optimization effectiveness. Actual: {results.AdaptiveOptimizationEffectiveness:F1}");

            _output.WriteLine($"🎯 Medium Enterprise Migration Results:");
            _output.WriteLine($"Products Migrated: {mediumMigrationScenario.TotalProducts:N0}");
            _output.WriteLine($"Execution Time: {results.TotalExecutionTime.TotalHours:F1} hours");
            _output.WriteLine($"Throughput: {results.ThroughputProductsPerSecond:F1} products/sec");
            _output.WriteLine($"Success Rate: {results.SuccessRate:P1}");
            _output.WriteLine($"Scalability Score: {results.ScalabilityScore:F1}");
            _output.WriteLine($"Adaptive Optimization: {results.AdaptiveOptimizationEffectiveness:P1}");
        }

        [Fact]
        public async Task Large_Enterprise_Migration_Should_Handle_Massive_Scale()
        {
            // Arrange
            var orchestrator = CreatePerformanceOrchestrator();
            var largeMigrationScenario = CreateLargeMigrationScenario(); // 1M+ products
            var config = CreateOptimizedConfiguration();

            // Act
            var results = await orchestrator.ExecuteMigrationPerformanceTestAsync(
                "LargeEnterprise", largeMigrationScenario, config, CancellationToken.None);

            // Assert - Large enterprise targets
            Assert.True(results.TotalExecutionTime.TotalDays <= 5,
                $"Large migration should complete within 5 days. Actual: {results.TotalExecutionTime.TotalDays:F1} days");

            Assert.True(results.ThroughputProductsPerSecond >= 5,
                $"Should achieve 5+ products/second. Actual: {results.ThroughputProductsPerSecond:F1}");

            Assert.True(results.SuccessRate >= 0.95,
                $"Should achieve 95%+ success rate. Actual: {results.SuccessRate:P1}");

            Assert.True(results.ScalabilityScore >= 0.8,
                $"Should handle massive scale. Actual: {results.ScalabilityScore:F1}");

            Assert.True(results.ResourceUtilization.OverallEfficiency >= 0.75,
                $"Should maintain resource efficiency. Actual: {results.ResourceUtilization.OverallEfficiency:F1}");

            Assert.True(results.ErrorRecoveryRate >= 0.9,
                $"Should recover from errors efficiently. Actual: {results.ErrorRecoveryRate:F1}");

            _output.WriteLine($"🎯 Large Enterprise Migration Results:");
            _output.WriteLine($"Products Migrated: {largeMigrationScenario.TotalProducts:N0}");
            _output.WriteLine($"Execution Time: {results.TotalExecutionTime.TotalDays:F1} days");
            _output.WriteLine($"Throughput: {results.ThroughputProductsPerSecond:F1} products/sec");
            _output.WriteLine($"Success Rate: {results.SuccessRate:P1}");
            _output.WriteLine($"Scalability Score: {results.ScalabilityScore:F1}");
            _output.WriteLine($"Resource Efficiency: {results.ResourceUtilization.OverallEfficiency:P1}");
            _output.WriteLine($"Error Recovery Rate: {results.ErrorRecoveryRate:P1}");
        }

        [Fact]
        public async Task Optimization_Components_Should_Work_Together_Seamlessly()
        {
            // Arrange
            var orchestrator = CreatePerformanceOrchestrator();
            var integrationScenario = CreateIntegrationTestScenario();
            var config = CreateOptimizedConfiguration();

            // Act
            var results = await orchestrator.ValidateOptimizationIntegrationAsync(
                integrationScenario, config, CancellationToken.None);

            // Assert - Integration validation
            Assert.True(results.AdaptiveConcurrencyIntegration >= 0.9,
                $"Adaptive concurrency should integrate well. Actual: {results.AdaptiveConcurrencyIntegration:F1}");

            Assert.True(results.BulkProcessingIntegration >= 0.9,
                $"Bulk processing should integrate well. Actual: {results.BulkProcessingIntegration:F1}");

            Assert.True(results.StorageOptimizationIntegration >= 0.9,
                $"Storage optimization should integrate well. Actual: {results.StorageOptimizationIntegration:F1}");

            Assert.True(results.ConnectionPoolingIntegration >= 0.9,
                $"Connection pooling should integrate well. Actual: {results.ConnectionPoolingIntegration:F1}");

            Assert.True(results.OverallIntegrationScore >= 0.95,
                $"Overall integration should be excellent. Actual: {results.OverallIntegrationScore:F1}");

            Assert.True(results.ComponentSynergy >= 0.8,
                $"Components should show synergy. Actual: {results.ComponentSynergy:F1}");

            _output.WriteLine($"🎯 Optimization Integration Results:");
            _output.WriteLine($"Adaptive Concurrency Integration: {results.AdaptiveConcurrencyIntegration:P1}");
            _output.WriteLine($"Bulk Processing Integration: {results.BulkProcessingIntegration:P1}");
            _output.WriteLine($"Storage Optimization Integration: {results.StorageOptimizationIntegration:P1}");
            _output.WriteLine($"Connection Pooling Integration: {results.ConnectionPoolingIntegration:P1}");
            _output.WriteLine($"Overall Integration Score: {results.OverallIntegrationScore:P1}");
            _output.WriteLine($"Component Synergy: {results.ComponentSynergy:P1}");
        }

        [Fact]
        public async Task System_Should_Maintain_Performance_Under_Stress()
        {
            // Arrange
            var orchestrator = CreatePerformanceOrchestrator();
            var stressScenario = CreateStressTestScenario();
            var config = CreateOptimizedConfiguration();

            // Act
            var results = await orchestrator.ExecuteStressTestAsync(
                stressScenario, config, CancellationToken.None);

            // Assert - Stress test targets
            Assert.True(results.PeakThroughputSustained >= 0.8,
                $"Should sustain 80%+ of peak throughput. Actual: {results.PeakThroughputSustained:P1}");

            Assert.True(results.MemoryStabilityScore >= 0.85,
                $"Should maintain memory stability. Actual: {results.MemoryStabilityScore:F1}");

            Assert.True(results.CpuUtilizationEfficiency >= 0.7,
                $"Should maintain CPU efficiency under stress. Actual: {results.CpuUtilizationEfficiency:F1}");

            Assert.True(results.ErrorRateUnderStress <= 0.05,
                $"Should keep error rate low under stress. Actual: {results.ErrorRateUnderStress:P1}");

            Assert.True(results.RecoveryTimeAfterStress <= 30,
                $"Should recover quickly after stress. Actual: {results.RecoveryTimeAfterStress:F1} seconds");

            _output.WriteLine($"🎯 Stress Test Results:");
            _output.WriteLine($"Peak Throughput Sustained: {results.PeakThroughputSustained:P1}");
            _output.WriteLine($"Memory Stability Score: {results.MemoryStabilityScore:F1}");
            _output.WriteLine($"CPU Utilization Efficiency: {results.CpuUtilizationEfficiency:P1}");
            _output.WriteLine($"Error Rate Under Stress: {results.ErrorRateUnderStress:P1}");
            _output.WriteLine($"Recovery Time: {results.RecoveryTimeAfterStress:F1} seconds");
        }

        [Fact]
        public async Task Adaptive_Optimization_Should_Improve_Performance_Over_Time()
        {
            // Arrange
            var orchestrator = CreatePerformanceOrchestrator();
            var adaptiveScenario = CreateAdaptiveOptimizationScenario();
            var config = CreateOptimizedConfiguration();

            // Act
            var results = await orchestrator.TestAdaptiveOptimizationAsync(
                adaptiveScenario, config, CancellationToken.None);

            // Assert - Adaptive optimization
            Assert.True(results.InitialPerformanceScore >= 0.7,
                $"Should start with good performance. Actual: {results.InitialPerformanceScore:F1}");

            Assert.True(results.FinalPerformanceScore >= 0.9,
                $"Should achieve excellent performance. Actual: {results.FinalPerformanceScore:F1}");

            Assert.True(results.PerformanceImprovement >= 20,
                $"Should improve 20%+ over time. Actual: {results.PerformanceImprovement:F1}%");

            Assert.True(results.AdaptationSpeed >= 0.8,
                $"Should adapt quickly. Actual: {results.AdaptationSpeed:F1}");

            Assert.True(results.OptimizationStability >= 0.85,
                $"Should maintain stable optimizations. Actual: {results.OptimizationStability:F1}");

            _output.WriteLine($"🎯 Adaptive Optimization Results:");
            _output.WriteLine($"Initial Performance Score: {results.InitialPerformanceScore:F1}");
            _output.WriteLine($"Final Performance Score: {results.FinalPerformanceScore:F1}");
            _output.WriteLine($"Performance Improvement: {results.PerformanceImprovement:F1}%");
            _output.WriteLine($"Adaptation Speed: {results.AdaptationSpeed:P1}");
            _output.WriteLine($"Optimization Stability: {results.OptimizationStability:P1}");
        }

        [Fact]
        public async Task Complex_Product_Scenarios_Should_Be_Handled_Efficiently()
        {
            // Arrange
            var orchestrator = CreatePerformanceOrchestrator();
            var complexScenario = CreateComplexProductScenario();
            var config = CreateOptimizedConfiguration();

            // Act
            var results = await orchestrator.TestComplexProductHandlingAsync(
                complexScenario, config, CancellationToken.None);

            // Assert - Complex product handling
            Assert.True(results.VariantProcessingEfficiency >= 0.8,
                $"Should handle variants efficiently. Actual: {results.VariantProcessingEfficiency:F1}");

            Assert.True(results.ImageProcessingThroughput >= 20,
                $"Should process 20+ images/second. Actual: {results.ImageProcessingThroughput:F1}");

            Assert.True(results.ModifierHandlingAccuracy >= 0.95,
                $"Should handle modifiers accurately. Actual: {results.ModifierHandlingAccuracy:F1}");

            Assert.True(results.ComplexProductCompletionRate >= 0.9,
                $"Should complete complex products. Actual: {results.ComplexProductCompletionRate:F1}");

            Assert.True(results.TimeoutPreventionEffectiveness >= 0.85,
                $"Should prevent timeouts. Actual: {results.TimeoutPreventionEffectiveness:F1}");

            _output.WriteLine($"🎯 Complex Product Handling Results:");
            _output.WriteLine($"Variant Processing Efficiency: {results.VariantProcessingEfficiency:P1}");
            _output.WriteLine($"Image Processing Throughput: {results.ImageProcessingThroughput:F1} images/sec");
            _output.WriteLine($"Modifier Handling Accuracy: {results.ModifierHandlingAccuracy:P1}");
            _output.WriteLine($"Complex Product Completion Rate: {results.ComplexProductCompletionRate:P1}");
            _output.WriteLine($"Timeout Prevention Effectiveness: {results.TimeoutPreventionEffectiveness:P1}");
        }

        [Fact]
        public async Task Multi_Channel_Migration_Should_Scale_Linearly()
        {
            // Arrange
            var orchestrator = CreatePerformanceOrchestrator();
            var multiChannelScenario = CreateMultiChannelScenario();
            var config = CreateOptimizedConfiguration();

            // Act
            var results = await orchestrator.TestMultiChannelPerformanceAsync(
                multiChannelScenario, config, CancellationToken.None);

            // Assert - Multi-channel performance
            Assert.True(results.ChannelIsolationEffectiveness >= 0.9,
                $"Should isolate channels effectively. Actual: {results.ChannelIsolationEffectiveness:F1}");

            Assert.True(results.LoadBalancingAcrossChannels >= 0.85,
                $"Should balance load across channels. Actual: {results.LoadBalancingAcrossChannels:F1}");

            Assert.True(results.LinearScalingFactor >= 0.8,
                $"Should scale linearly. Actual: {results.LinearScalingFactor:F1}");

            Assert.True(results.ResourceSharingEfficiency >= 0.75,
                $"Should share resources efficiently. Actual: {results.ResourceSharingEfficiency:F1}");

            Assert.True(results.CrossChannelOptimization >= 0.7,
                $"Should optimize across channels. Actual: {results.CrossChannelOptimization:F1}");

            _output.WriteLine($"🎯 Multi-Channel Migration Results:");
            _output.WriteLine($"Channel Isolation: {results.ChannelIsolationEffectiveness:P1}");
            _output.WriteLine($"Load Balancing: {results.LoadBalancingAcrossChannels:P1}");
            _output.WriteLine($"Linear Scaling Factor: {results.LinearScalingFactor:P1}");
            _output.WriteLine($"Resource Sharing Efficiency: {results.ResourceSharingEfficiency:P1}");
            _output.WriteLine($"Cross-Channel Optimization: {results.CrossChannelOptimization:P1}");
        }

        // Helper methods for test setup and scenario creation

        private PerformanceIntegrationOrchestrator CreatePerformanceOrchestrator()
        {
            return new PerformanceIntegrationOrchestrator(_mockLogger.Object);
        }

        private MigrationScenario CreateSmallMigrationScenario()
        {
            return new MigrationScenario
            {
                Name = "SmallEnterprise",
                TotalProducts = 10_000,
                AverageVariantsPerProduct = 3,
                AverageImagesPerProduct = 5,
                ComplexProductPercentage = 0.1, // 10% complex products
                CategoriesCount = 500,
                BrandsCount = 100,
                ExpectedDurationMinutes = 30,
                ConcurrencyLevel = ConcurrencyLevel.Medium
            };
        }

        private MigrationScenario CreateMediumMigrationScenario()
        {
            return new MigrationScenario
            {
                Name = "MediumEnterprise",
                TotalProducts = 100_000,
                AverageVariantsPerProduct = 5,
                AverageImagesPerProduct = 8,
                ComplexProductPercentage = 0.2, // 20% complex products
                CategoriesCount = 2_000,
                BrandsCount = 500,
                ExpectedDurationMinutes = 480, // 8 hours
                ConcurrencyLevel = ConcurrencyLevel.High
            };
        }

        private MigrationScenario CreateLargeMigrationScenario()
        {
            return new MigrationScenario
            {
                Name = "LargeEnterprise",
                TotalProducts = 1_000_000,
                AverageVariantsPerProduct = 8,
                AverageImagesPerProduct = 12,
                ComplexProductPercentage = 0.3, // 30% complex products
                CategoriesCount = 10_000,
                BrandsCount = 2_000,
                ExpectedDurationMinutes = 7_200, // 5 days
                ConcurrencyLevel = ConcurrencyLevel.Maximum
            };
        }

        private PerformanceConfiguration CreateBaselineConfiguration()
        {
            return new PerformanceConfiguration
            {
                EnableAdaptiveConcurrency = false,
                EnableIntelligentBulkProcessing = false,
                EnableStorageOptimization = false,
                EnableConnectionPooling = false,
                MaxConcurrency = 5,
                BatchSize = 10,
                ConnectionPoolSize = 20
            };
        }

        private PerformanceConfiguration CreateOptimizedConfiguration()
        {
            return new PerformanceConfiguration
            {
                EnableAdaptiveConcurrency = true,
                EnableIntelligentBulkProcessing = true,
                EnableStorageOptimization = true,
                EnableConnectionPooling = true,
                MaxConcurrency = 20,
                BatchSize = 50,
                ConnectionPoolSize = 40,
                AdaptiveOptimizationLevel = OptimizationLevel.Maximum
            };
        }

        private IntegrationTestScenario CreateIntegrationTestScenario()
        {
            return new IntegrationTestScenario
            {
                TestComponents = new[]
                {
                    OptimizationComponent.AdaptiveConcurrency,
                    OptimizationComponent.IntelligentBulkProcessing,
                    OptimizationComponent.StorageOptimization,
                    OptimizationComponent.ConnectionPooling
                },
                TestDurationMinutes = 60,
                LoadPatterns = new[] { LoadPattern.Steady, LoadPattern.Burst, LoadPattern.Gradual },
                ValidationDepth = ValidationDepth.Comprehensive
            };
        }

        private StressTestScenario CreateStressTestScenario()
        {
            return new StressTestScenario
            {
                MaxConcurrentRequests = 1000,
                SustainedLoadDurationMinutes = 30,
                PeakLoadDurationMinutes = 10,
                MemoryPressureLevel = MemoryPressureLevel.High,
                CpuUtilizationTarget = 0.9,
                ExpectedDegradationThreshold = 0.2
            };
        }

        private AdaptiveOptimizationScenario CreateAdaptiveOptimizationScenario()
        {
            return new AdaptiveOptimizationScenario
            {
                InitialLoadLevel = 0.3,
                FinalLoadLevel = 0.9,
                LoadIncrementSteps = 10,
                AdaptationTimeWindowMinutes = 5,
                OptimizationMetrics = new[]
                {
                    OptimizationMetric.Throughput,
                    OptimizationMetric.Latency,
                    OptimizationMetric.ResourceUtilization,
                    OptimizationMetric.ErrorRate
                }
            };
        }

        private ComplexProductScenario CreateComplexProductScenario()
        {
            return new ComplexProductScenario
            {
                ProductsWithManyVariants = 1000, // Products with 50+ variants
                ProductsWithManyImages = 800, // Products with 20+ images
                ProductsWithManyModifiers = 600, // Products with 15+ modifiers
                MaxVariantsPerProduct = 100,
                MaxImagesPerProduct = 50,
                MaxModifiersPerProduct = 30,
                TimeoutPreventionRequired = true
            };
        }

        private MultiChannelScenario CreateMultiChannelScenario()
        {
            return new MultiChannelScenario
            {
                NumberOfChannels = 5,
                ProductsPerChannel = 20_000,
                SharedProductPercentage = 0.3, // 30% products shared across channels
                ChannelSpecificCategoriesPercentage = 0.4, // 40% categories are channel-specific
                ConcurrentChannelMigrations = 3,
                ResourceSharingEnabled = true
            };
        }

        private PerformanceImprovement CalculateOverallPerformanceImprovement(
            MigrationPerformanceResults baseline, MigrationPerformanceResults optimized)
        {
            var throughputImprovement = ((optimized.ThroughputProductsPerSecond - baseline.ThroughputProductsPerSecond) 
                / baseline.ThroughputProductsPerSecond) * 100;

            var latencyReduction = ((baseline.AverageResponseTime - optimized.AverageResponseTime) 
                / baseline.AverageResponseTime) * 100;

            var timeReduction = ((baseline.TotalExecutionTime - optimized.TotalExecutionTime) 
                / baseline.TotalExecutionTime) * 100;

            var resourceEfficiencyGain = 
                (optimized.ResourceUtilization.OverallEfficiency - baseline.ResourceUtilization.OverallEfficiency) * 100;

            var overallImprovement = (throughputImprovement + latencyReduction + timeReduction + resourceEfficiencyGain) / 4;

            return new PerformanceImprovement
            {
                OverallImprovement = overallImprovement,
                ThroughputImprovement = throughputImprovement,
                LatencyReduction = latencyReduction,
                TimeReduction = timeReduction,
                ResourceEfficiencyGain = resourceEfficiencyGain
            };
        }
    }
} 