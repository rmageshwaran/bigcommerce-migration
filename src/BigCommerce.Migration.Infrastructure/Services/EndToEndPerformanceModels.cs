using System;
using System.Collections.Generic;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Supporting data models for End-to-End Performance Validation
    /// Phase 5: Task 5.4 - Comprehensive performance testing and validation
    /// </summary>

    /// <summary>
    /// Migration scenario for performance testing
    /// </summary>
    public class MigrationScenario
    {
        /// <summary>
        /// Gets or sets the scenario name
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the total number of products to migrate
        /// </summary>
        public int TotalProducts { get; set; }

        /// <summary>
        /// Gets or sets the average number of variants per product
        /// </summary>
        public int AverageVariantsPerProduct { get; set; }

        /// <summary>
        /// Gets or sets the average number of images per product
        /// </summary>
        public int AverageImagesPerProduct { get; set; }

        /// <summary>
        /// Gets or sets the percentage of complex products (0.0 to 1.0)
        /// </summary>
        public double ComplexProductPercentage { get; set; }

        /// <summary>
        /// Gets or sets the number of categories
        /// </summary>
        public int CategoriesCount { get; set; }

        /// <summary>
        /// Gets or sets the number of brands
        /// </summary>
        public int BrandsCount { get; set; }

        /// <summary>
        /// Gets or sets the expected duration in minutes
        /// </summary>
        public int ExpectedDurationMinutes { get; set; }

        /// <summary>
        /// Gets or sets the concurrency level for this scenario
        /// </summary>
        public ConcurrencyLevel ConcurrencyLevel { get; set; }
    }

    /// <summary>
    /// Concurrency levels for migration scenarios
    /// </summary>
    public enum ConcurrencyLevel
    {
        /// <summary>
        /// Low concurrency for small scenarios
        /// </summary>
        Low,

        /// <summary>
        /// Medium concurrency for balanced scenarios
        /// </summary>
        Medium,

        /// <summary>
        /// High concurrency for large scenarios
        /// </summary>
        High,

        /// <summary>
        /// Maximum concurrency for stress testing
        /// </summary>
        Maximum
    }

    /// <summary>
    /// Performance configuration for testing
    /// </summary>
    public class PerformanceConfiguration
    {
        /// <summary>
        /// Gets or sets whether adaptive concurrency is enabled
        /// </summary>
        public bool EnableAdaptiveConcurrency { get; set; } = true;

        /// <summary>
        /// Gets or sets whether intelligent bulk processing is enabled
        /// </summary>
        public bool EnableIntelligentBulkProcessing { get; set; } = true;

        /// <summary>
        /// Gets or sets whether storage optimization is enabled
        /// </summary>
        public bool EnableStorageOptimization { get; set; } = true;

        /// <summary>
        /// Gets or sets whether connection pooling is enabled
        /// </summary>
        public bool EnableConnectionPooling { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum concurrency level
        /// </summary>
        public int MaxConcurrency { get; set; } = 20;

        /// <summary>
        /// Gets or sets the batch size for processing
        /// </summary>
        public int BatchSize { get; set; } = 50;

        /// <summary>
        /// Gets or sets the connection pool size
        /// </summary>
        public int ConnectionPoolSize { get; set; } = 40;

        /// <summary>
        /// Gets or sets the adaptive optimization level
        /// </summary>
        public OptimizationLevel AdaptiveOptimizationLevel { get; set; } = OptimizationLevel.High;
    }

    /// <summary>
    /// Integration test scenario configuration
    /// </summary>
    public class IntegrationTestScenario
    {
        /// <summary>
        /// Gets or sets the components to test
        /// </summary>
        public OptimizationComponent[] TestComponents { get; set; } = Array.Empty<OptimizationComponent>();

        /// <summary>
        /// Gets or sets the test duration in minutes
        /// </summary>
        public int TestDurationMinutes { get; set; } = 30;

        /// <summary>
        /// Gets or sets the load patterns to test
        /// </summary>
        public LoadPattern[] LoadPatterns { get; set; } = Array.Empty<LoadPattern>();

        /// <summary>
        /// Gets or sets the validation depth
        /// </summary>
        public ValidationDepth ValidationDepth { get; set; } = ValidationDepth.Standard;
    }

    /// <summary>
    /// Optimization components for testing
    /// </summary>
    public enum OptimizationComponent
    {
        /// <summary>
        /// Adaptive concurrency control component
        /// </summary>
        AdaptiveConcurrency,

        /// <summary>
        /// Intelligent bulk processing component
        /// </summary>
        IntelligentBulkProcessing,

        /// <summary>
        /// Storage optimization component
        /// </summary>
        StorageOptimization,

        /// <summary>
        /// Connection pooling component
        /// </summary>
        ConnectionPooling
    }

    /// <summary>
    /// Load patterns for testing
    /// </summary>
    public enum LoadPattern
    {
        /// <summary>
        /// Steady constant load
        /// </summary>
        Steady,

        /// <summary>
        /// Burst load pattern
        /// </summary>
        Burst,

        /// <summary>
        /// Gradual load increase
        /// </summary>
        Gradual,

        /// <summary>
        /// Random load variation
        /// </summary>
        Random
    }

    /// <summary>
    /// Validation depth levels
    /// </summary>
    public enum ValidationDepth
    {
        /// <summary>
        /// Basic validation
        /// </summary>
        Basic,

        /// <summary>
        /// Standard validation
        /// </summary>
        Standard,

        /// <summary>
        /// Comprehensive validation
        /// </summary>
        Comprehensive,

        /// <summary>
        /// Deep validation with detailed analysis
        /// </summary>
        Deep
    }

    /// <summary>
    /// Stress test scenario configuration
    /// </summary>
    public class StressTestScenario
    {
        /// <summary>
        /// Gets or sets the maximum concurrent requests
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the sustained load duration in minutes
        /// </summary>
        public int SustainedLoadDurationMinutes { get; set; } = 30;

        /// <summary>
        /// Gets or sets the peak load duration in minutes
        /// </summary>
        public int PeakLoadDurationMinutes { get; set; } = 10;

        /// <summary>
        /// Gets or sets the memory pressure level
        /// </summary>
        public MemoryPressureLevel MemoryPressureLevel { get; set; } = MemoryPressureLevel.Medium;

        /// <summary>
        /// Gets or sets the CPU utilization target (0.0 to 1.0)
        /// </summary>
        public double CpuUtilizationTarget { get; set; } = 0.8;

        /// <summary>
        /// Gets or sets the expected degradation threshold (0.0 to 1.0)
        /// </summary>
        public double ExpectedDegradationThreshold { get; set; } = 0.2;
    }

    /// <summary>
    /// Memory pressure levels for stress testing
    /// </summary>
    public enum MemoryPressureLevel
    {
        /// <summary>
        /// Low memory pressure
        /// </summary>
        Low,

        /// <summary>
        /// Medium memory pressure
        /// </summary>
        Medium,

        /// <summary>
        /// High memory pressure
        /// </summary>
        High,

        /// <summary>
        /// Critical memory pressure
        /// </summary>
        Critical
    }

    /// <summary>
    /// Adaptive optimization scenario configuration
    /// </summary>
    public class AdaptiveOptimizationScenario
    {
        /// <summary>
        /// Gets or sets the initial load level (0.0 to 1.0)
        /// </summary>
        public double InitialLoadLevel { get; set; } = 0.1;

        /// <summary>
        /// Gets or sets the final load level (0.0 to 1.0)
        /// </summary>
        public double FinalLoadLevel { get; set; } = 1.0;

        /// <summary>
        /// Gets or sets the number of load increment steps
        /// </summary>
        public int LoadIncrementSteps { get; set; } = 10;

        /// <summary>
        /// Gets or sets the adaptation time window in minutes
        /// </summary>
        public int AdaptationTimeWindowMinutes { get; set; } = 5;

        /// <summary>
        /// Gets or sets the optimization metrics to track
        /// </summary>
        public OptimizationMetric[] OptimizationMetrics { get; set; } = Array.Empty<OptimizationMetric>();
    }

    /// <summary>
    /// Optimization levels for adaptive optimization
    /// </summary>
    public enum OptimizationLevel
    {
        /// <summary>
        /// Basic optimization level
        /// </summary>
        Basic,

        /// <summary>
        /// Standard optimization level
        /// </summary>
        Standard,

        /// <summary>
        /// High optimization level
        /// </summary>
        High,

        /// <summary>
        /// Maximum optimization level
        /// </summary>
        Maximum
    }

    /// <summary>
    /// Optimization metrics for tracking
    /// </summary>
    public enum OptimizationMetric
    {
        /// <summary>
        /// Throughput metric
        /// </summary>
        Throughput,

        /// <summary>
        /// Latency metric
        /// </summary>
        Latency,

        /// <summary>
        /// Resource utilization metric
        /// </summary>
        ResourceUtilization,

        /// <summary>
        /// Error rate metric
        /// </summary>
        ErrorRate,

        /// <summary>
        /// Success rate metric
        /// </summary>
        SuccessRate
    }

    /// <summary>
    /// Complex product scenario configuration
    /// </summary>
    public class ComplexProductScenario
    {
        /// <summary>
        /// Gets or sets the number of products with many variants
        /// </summary>
        public int ProductsWithManyVariants { get; set; }

        /// <summary>
        /// Gets or sets the number of products with many images
        /// </summary>
        public int ProductsWithManyImages { get; set; }

        /// <summary>
        /// Gets or sets the number of products with many modifiers
        /// </summary>
        public int ProductsWithManyModifiers { get; set; }

        /// <summary>
        /// Gets or sets the maximum variants per product
        /// </summary>
        public int MaxVariantsPerProduct { get; set; } = 100;

        /// <summary>
        /// Gets or sets the maximum images per product
        /// </summary>
        public int MaxImagesPerProduct { get; set; } = 50;

        /// <summary>
        /// Gets or sets the maximum modifiers per product
        /// </summary>
        public int MaxModifiersPerProduct { get; set; } = 30;

        /// <summary>
        /// Gets or sets whether timeout prevention is required
        /// </summary>
        public bool TimeoutPreventionRequired { get; set; } = true;
    }

    /// <summary>
    /// Multi-channel scenario configuration
    /// </summary>
    public class MultiChannelScenario
    {
        /// <summary>
        /// Gets or sets the number of channels
        /// </summary>
        public int NumberOfChannels { get; set; } = 3;

        /// <summary>
        /// Gets or sets the products per channel
        /// </summary>
        public int ProductsPerChannel { get; set; } = 10000;

        /// <summary>
        /// Gets or sets the shared product percentage (0.0 to 1.0)
        /// </summary>
        public double SharedProductPercentage { get; set; } = 0.2;

        /// <summary>
        /// Gets or sets the channel-specific categories percentage (0.0 to 1.0)
        /// </summary>
        public double ChannelSpecificCategoriesPercentage { get; set; } = 0.3;

        /// <summary>
        /// Gets or sets the number of concurrent channel migrations
        /// </summary>
        public int ConcurrentChannelMigrations { get; set; } = 2;

        /// <summary>
        /// Gets or sets whether resource sharing is enabled
        /// </summary>
        public bool ResourceSharingEnabled { get; set; } = true;
    }

    // Results Models

    /// <summary>
    /// Migration performance test results
    /// </summary>
    public class MigrationPerformanceResults
    {
        /// <summary>
        /// Gets or sets the session identifier
        /// </summary>
        public string SessionId { get; set; } = "";

        /// <summary>
        /// Gets or sets the test name
        /// </summary>
        public string TestName { get; set; } = "";

        /// <summary>
        /// Gets or sets the total execution time
        /// </summary>
        public TimeSpan TotalExecutionTime { get; set; }

        /// <summary>
        /// Gets or sets the total products processed
        /// </summary>
        public int TotalProductsProcessed { get; set; }

        /// <summary>
        /// Gets or sets the number of successful products
        /// </summary>
        public int SuccessfulProducts { get; set; }

        /// <summary>
        /// Gets or sets the throughput in products per second
        /// </summary>
        public double ThroughputProductsPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the average response time in milliseconds
        /// </summary>
        public double AverageResponseTime { get; set; }

        /// <summary>
        /// Gets or sets the overall success rate (0.0 to 1.0)
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the scalability score (0.0 to 1.0)
        /// </summary>
        public double ScalabilityScore { get; set; }

        /// <summary>
        /// Gets or sets the reliability score (0.0 to 1.0)
        /// </summary>
        public double ReliabilityScore { get; set; }

        /// <summary>
        /// Gets or sets the adaptive optimization effectiveness (0.0 to 1.0)
        /// </summary>
        public double AdaptiveOptimizationEffectiveness { get; set; }

        /// <summary>
        /// Gets or sets the error recovery rate (0.0 to 1.0)
        /// </summary>
        public double ErrorRecoveryRate { get; set; }

        /// <summary>
        /// Gets or sets the resource utilization results
        /// </summary>
        public ResourceUtilizationResults ResourceUtilization { get; set; } = new();
    }

    /// <summary>
    /// Resource utilization results
    /// </summary>
    public class ResourceUtilizationResults
    {
        /// <summary>
        /// Gets or sets the memory efficiency (0.0 to 1.0)
        /// </summary>
        public double MemoryEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the CPU efficiency (0.0 to 1.0)
        /// </summary>
        public double CpuEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the overall efficiency (0.0 to 1.0)
        /// </summary>
        public double OverallEfficiency { get; set; }
    }

    /// <summary>
    /// Performance improvement analysis
    /// </summary>
    public class PerformanceImprovement
    {
        /// <summary>
        /// Gets or sets the overall improvement percentage
        /// </summary>
        public double OverallImprovement { get; set; }

        /// <summary>
        /// Gets or sets the throughput improvement percentage
        /// </summary>
        public double ThroughputImprovement { get; set; }

        /// <summary>
        /// Gets or sets the latency reduction percentage
        /// </summary>
        public double LatencyReduction { get; set; }

        /// <summary>
        /// Gets or sets the time reduction percentage
        /// </summary>
        public double TimeReduction { get; set; }

        /// <summary>
        /// Gets or sets the resource efficiency gain percentage
        /// </summary>
        public double ResourceEfficiencyGain { get; set; }
    }

    /// <summary>
    /// Optimization integration results
    /// </summary>
    public class OptimizationIntegrationResults
    {
        /// <summary>
        /// Gets or sets the test scenario
        /// </summary>
        public IntegrationTestScenario TestScenario { get; set; } = new();

        /// <summary>
        /// Gets or sets the test start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the test end time
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the adaptive concurrency integration score (0.0 to 1.0)
        /// </summary>
        public double AdaptiveConcurrencyIntegration { get; set; }

        /// <summary>
        /// Gets or sets the bulk processing integration score (0.0 to 1.0)
        /// </summary>
        public double BulkProcessingIntegration { get; set; }

        /// <summary>
        /// Gets or sets the storage optimization integration score (0.0 to 1.0)
        /// </summary>
        public double StorageOptimizationIntegration { get; set; }

        /// <summary>
        /// Gets or sets the connection pooling integration score (0.0 to 1.0)
        /// </summary>
        public double ConnectionPoolingIntegration { get; set; }

        /// <summary>
        /// Gets or sets the overall integration score (0.0 to 1.0)
        /// </summary>
        public double OverallIntegrationScore { get; set; }

        /// <summary>
        /// Gets or sets the component synergy score (0.0 to 1.0)
        /// </summary>
        public double ComponentSynergy { get; set; }
    }

    /// <summary>
    /// Stress test results
    /// </summary>
    public class StressTestResults
    {
        /// <summary>
        /// Gets or sets the test scenario
        /// </summary>
        public StressTestScenario TestScenario { get; set; } = new();

        /// <summary>
        /// Gets or sets the test start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the test end time
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the peak throughput sustained ratio (0.0 to 1.0)
        /// </summary>
        public double PeakThroughputSustained { get; set; }

        /// <summary>
        /// Gets or sets the memory stability score (0.0 to 1.0)
        /// </summary>
        public double MemoryStabilityScore { get; set; }

        /// <summary>
        /// Gets or sets the CPU utilization efficiency (0.0 to 1.0)
        /// </summary>
        public double CpuUtilizationEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the error rate under stress (0.0 to 1.0)
        /// </summary>
        public double ErrorRateUnderStress { get; set; }

        /// <summary>
        /// Gets or sets the recovery time after stress in seconds
        /// </summary>
        public double RecoveryTimeAfterStress { get; set; }
    }

    /// <summary>
    /// Adaptive optimization test results
    /// </summary>
    public class AdaptiveOptimizationResults
    {
        /// <summary>
        /// Gets or sets the test scenario
        /// </summary>
        public AdaptiveOptimizationScenario TestScenario { get; set; } = new();

        /// <summary>
        /// Gets or sets the test start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the test end time
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the initial performance score (0.0 to 1.0)
        /// </summary>
        public double InitialPerformanceScore { get; set; }

        /// <summary>
        /// Gets or sets the final performance score (0.0 to 1.0)
        /// </summary>
        public double FinalPerformanceScore { get; set; }

        /// <summary>
        /// Gets or sets the performance improvement percentage
        /// </summary>
        public double PerformanceImprovement { get; set; }

        /// <summary>
        /// Gets or sets the adaptation speed (0.0 to 1.0)
        /// </summary>
        public double AdaptationSpeed { get; set; }

        /// <summary>
        /// Gets or sets the optimization stability (0.0 to 1.0)
        /// </summary>
        public double OptimizationStability { get; set; }
    }

    /// <summary>
    /// Complex product handling results
    /// </summary>
    public class ComplexProductResults
    {
        /// <summary>
        /// Gets or sets the test scenario
        /// </summary>
        public ComplexProductScenario TestScenario { get; set; } = new();

        /// <summary>
        /// Gets or sets the test start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the test end time
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the variant processing efficiency (0.0 to 1.0)
        /// </summary>
        public double VariantProcessingEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the image processing throughput (images per second)
        /// </summary>
        public double ImageProcessingThroughput { get; set; }

        /// <summary>
        /// Gets or sets the modifier handling accuracy (0.0 to 1.0)
        /// </summary>
        public double ModifierHandlingAccuracy { get; set; }

        /// <summary>
        /// Gets or sets the complex product completion rate (0.0 to 1.0)
        /// </summary>
        public double ComplexProductCompletionRate { get; set; }

        /// <summary>
        /// Gets or sets the timeout prevention effectiveness (0.0 to 1.0)
        /// </summary>
        public double TimeoutPreventionEffectiveness { get; set; }
    }

    /// <summary>
    /// Multi-channel performance results
    /// </summary>
    public class MultiChannelResults
    {
        /// <summary>
        /// Gets or sets the test scenario
        /// </summary>
        public MultiChannelScenario TestScenario { get; set; } = new();

        /// <summary>
        /// Gets or sets the test start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the test end time
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the channel isolation effectiveness (0.0 to 1.0)
        /// </summary>
        public double ChannelIsolationEffectiveness { get; set; }

        /// <summary>
        /// Gets or sets the load balancing across channels (0.0 to 1.0)
        /// </summary>
        public double LoadBalancingAcrossChannels { get; set; }

        /// <summary>
        /// Gets or sets the linear scaling factor (0.0 to 1.0)
        /// </summary>
        public double LinearScalingFactor { get; set; }

        /// <summary>
        /// Gets or sets the resource sharing efficiency (0.0 to 1.0)
        /// </summary>
        public double ResourceSharingEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the cross-channel optimization score (0.0 to 1.0)
        /// </summary>
        public double CrossChannelOptimization { get; set; }
    }

    // Internal support models for orchestrator

    /// <summary>
    /// Performance session tracking
    /// </summary>
    public class PerformanceSession
    {
        /// <summary>
        /// Gets or sets the test name
        /// </summary>
        public string TestName { get; set; } = "";

        /// <summary>
        /// Gets or sets the session identifier
        /// </summary>
        public string SessionId { get; set; } = "";

        /// <summary>
        /// Gets or sets the migration scenario
        /// </summary>
        public MigrationScenario Scenario { get; set; } = new();

        /// <summary>
        /// Gets or sets the performance configuration
        /// </summary>
        public PerformanceConfiguration Configuration { get; set; } = new();

        /// <summary>
        /// Gets or sets the session start time
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the session metrics
        /// </summary>
        public Dictionary<string, object> Metrics { get; set; } = new();
    }

    /// <summary>
    /// Migration phase results
    /// </summary>
    public class MigrationPhaseResults
    {
        /// <summary>
        /// Gets or sets the dependency phase results
        /// </summary>
        public PhaseResults DependencyPhaseResults { get; set; } = new();

        /// <summary>
        /// Gets or sets the product phase results
        /// </summary>
        public PhaseResults ProductPhaseResults { get; set; } = new();

        /// <summary>
        /// Gets or sets the component phase results
        /// </summary>
        public PhaseResults ComponentPhaseResults { get; set; } = new();
    }

    /// <summary>
    /// Individual phase results
    /// </summary>
    public class PhaseResults
    {
        /// <summary>
        /// Gets or sets the phase name
        /// </summary>
        public string PhaseName { get; set; } = "";

        /// <summary>
        /// Gets or sets the number of entities processed
        /// </summary>
        public int EntitiesProcessed { get; set; }

        /// <summary>
        /// Gets or sets the processing time
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>
        /// Gets or sets the success rate (0.0 to 1.0)
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the throughput per second
        /// </summary>
        public double ThroughputPerSecond { get; set; }
    }

    /// <summary>
    /// Performance metrics collection
    /// </summary>
    public class PerformanceMetrics
    {
        /// <summary>
        /// Gets or sets the session identifier
        /// </summary>
        public string SessionId { get; set; } = "";

        /// <summary>
        /// Gets or sets the collection time
        /// </summary>
        public DateTime CollectionTime { get; set; }

        /// <summary>
        /// Gets or sets the total products processed
        /// </summary>
        public int TotalProductsProcessed { get; set; }

        /// <summary>
        /// Gets or sets the processing duration
        /// </summary>
        public TimeSpan ProcessingDuration { get; set; }

        /// <summary>
        /// Gets or sets the concurrency metrics
        /// </summary>
        public ConcurrencyMetrics? ConcurrencyMetrics { get; set; }

        /// <summary>
        /// Gets or sets the bulk processing metrics
        /// </summary>
        public BulkProcessingMetrics? BulkProcessingMetrics { get; set; }

        /// <summary>
        /// Gets or sets the storage metrics
        /// </summary>
        public StorageMetrics? StorageMetrics { get; set; }

        /// <summary>
        /// Gets or sets the connection pooling metrics
        /// </summary>
        public ConnectionPoolingMetrics? ConnectionPoolingMetrics { get; set; }
    }

    /// <summary>
    /// Concurrency performance metrics
    /// </summary>
    public class ConcurrencyMetrics
    {
        /// <summary>
        /// Gets or sets the average concurrency level
        /// </summary>
        public double AverageConcurrencyLevel { get; set; }

        /// <summary>
        /// Gets or sets the peak concurrency level
        /// </summary>
        public int PeakConcurrencyLevel { get; set; }

        /// <summary>
        /// Gets or sets the concurrency efficiency (0.0 to 1.0)
        /// </summary>
        public double ConcurrencyEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the number of adaptations
        /// </summary>
        public int AdaptationCount { get; set; }

        /// <summary>
        /// Gets or sets whether optimal concurrency was achieved
        /// </summary>
        public bool OptimalConcurrencyAchieved { get; set; }
    }

    /// <summary>
    /// Bulk processing performance metrics
    /// </summary>
    public class BulkProcessingMetrics
    {
        /// <summary>
        /// Gets or sets the average batch size
        /// </summary>
        public double AverageBatchSize { get; set; }

        /// <summary>
        /// Gets or sets the batching efficiency (0.0 to 1.0)
        /// </summary>
        public double BatchingEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the storage efficiency (0.0 to 1.0)
        /// </summary>
        public double StorageEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the number of batch optimizations
        /// </summary>
        public int BatchOptimizationCount { get; set; }
    }

    /// <summary>
    /// Storage performance metrics
    /// </summary>
    public class StorageMetrics
    {
        /// <summary>
        /// Gets or sets the query latency reduction (0.0 to 1.0)
        /// </summary>
        public double QueryLatencyReduction { get; set; }

        /// <summary>
        /// Gets or sets the throughput improvement (0.0 to 1.0)
        /// </summary>
        public double ThroughputImprovement { get; set; }

        /// <summary>
        /// Gets or sets the index optimization effectiveness (0.0 to 1.0)
        /// </summary>
        public double IndexOptimizationEffectiveness { get; set; }

        /// <summary>
        /// Gets or sets the partition balancing score (0.0 to 1.0)
        /// </summary>
        public double PartitionBalancing { get; set; }
    }

    /// <summary>
    /// Connection pooling performance metrics
    /// </summary>
    public class ConnectionPoolingMetrics
    {
        /// <summary>
        /// Gets or sets the pool utilization efficiency (0.0 to 1.0)
        /// </summary>
        public double PoolUtilizationEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the connection reuse rate (0.0 to 1.0)
        /// </summary>
        public double ConnectionReuseRate { get; set; }

        /// <summary>
        /// Gets or sets the resource efficiency gain (0.0 to 1.0)
        /// </summary>
        public double ResourceEfficiencyGain { get; set; }

        /// <summary>
        /// Gets or sets the latency reduction (0.0 to 1.0)
        /// </summary>
        public double LatencyReduction { get; set; }
    }

    /// <summary>
    /// Optimization analysis results
    /// </summary>
    public class OptimizationAnalysis
    {
        /// <summary>
        /// Gets or sets the session identifier
        /// </summary>
        public string SessionId { get; set; } = "";

        /// <summary>
        /// Gets or sets the analysis time
        /// </summary>
        public DateTime AnalysisTime { get; set; }

        /// <summary>
        /// Gets or sets the concurrency contribution (0.0 to 1.0)
        /// </summary>
        public double ConcurrencyContribution { get; set; }

        /// <summary>
        /// Gets or sets the bulk processing contribution (0.0 to 1.0)
        /// </summary>
        public double BulkProcessingContribution { get; set; }

        /// <summary>
        /// Gets or sets the storage optimization contribution (0.0 to 1.0)
        /// </summary>
        public double StorageOptimizationContribution { get; set; }

        /// <summary>
        /// Gets or sets the connection pooling contribution (0.0 to 1.0)
        /// </summary>
        public double ConnectionPoolingContribution { get; set; }

        /// <summary>
        /// Gets or sets the overall effectiveness (0.0 to 1.0)
        /// </summary>
        public double OverallEffectiveness { get; set; }

        /// <summary>
        /// Gets or sets the component synergy score (0.0 to 1.0)
        /// </summary>
        public double ComponentSynergy { get; set; }
    }

    /// <summary>
    /// Component integration test result
    /// </summary>
    public class ComponentIntegrationResult
    {
        /// <summary>
        /// Gets or sets the optimization component
        /// </summary>
        public OptimizationComponent Component { get; set; }

        /// <summary>
        /// Gets or sets the integration score (0.0 to 1.0)
        /// </summary>
        public double IntegrationScore { get; set; }

        /// <summary>
        /// Gets or sets the performance impact (0.0 to 1.0)
        /// </summary>
        public double PerformanceImpact { get; set; }

        /// <summary>
        /// Gets or sets the resource impact (0.0 to 1.0)
        /// </summary>
        public double ResourceImpact { get; set; }

        /// <summary>
        /// Gets or sets the interaction quality (0.0 to 1.0)
        /// </summary>
        public double InteractionQuality { get; set; }
    }

    // Additional support models for detailed testing

    /// <summary>
    /// Performance baseline tracking
    /// </summary>
    public class PerformanceBaseline
    {
        /// <summary>
        /// Gets or sets the baseline name
        /// </summary>
        public string BaselineName { get; set; } = "";

        /// <summary>
        /// Gets or sets the baseline throughput
        /// </summary>
        public double BaselineThroughput { get; set; }

        /// <summary>
        /// Gets or sets the baseline latency
        /// </summary>
        public double BaselineLatency { get; set; }

        /// <summary>
        /// Gets or sets the baseline resource usage
        /// </summary>
        public double BaselineResourceUsage { get; set; }

        /// <summary>
        /// Gets or sets the baseline timestamp
        /// </summary>
        public DateTime BaselineTimestamp { get; set; }
    }

    /// <summary>
    /// Baseline metrics for stress testing
    /// </summary>
    public class BaselineMetrics
    {
        /// <summary>
        /// Gets or sets the throughput per second
        /// </summary>
        public double ThroughputPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the average latency in milliseconds
        /// </summary>
        public double AverageLatency { get; set; }

        /// <summary>
        /// Gets or sets the memory usage in bytes
        /// </summary>
        public long MemoryUsage { get; set; }

        /// <summary>
        /// Gets or sets the CPU utilization (0.0 to 1.0)
        /// </summary>
        public double CpuUtilization { get; set; }
    }

    /// <summary>
    /// Load ramp-up results
    /// </summary>
    public class LoadRampResults
    {
        /// <summary>
        /// Gets or sets the peak throughput achieved
        /// </summary>
        public double PeakThroughputAchieved { get; set; }

        /// <summary>
        /// Gets or sets the ramp-up duration
        /// </summary>
        public TimeSpan RampUpDuration { get; set; }

        /// <summary>
        /// Gets or sets the stability during ramp (0.0 to 1.0)
        /// </summary>
        public double StabilityDuringRamp { get; set; }
    }

    /// <summary>
    /// Sustained load test results
    /// </summary>
    public class SustainedLoadResults
    {
        /// <summary>
        /// Gets or sets the throughput sustainability ratio (0.0 to 1.0)
        /// </summary>
        public double ThroughputSustainabilityRatio { get; set; }

        /// <summary>
        /// Gets or sets the error rate (0.0 to 1.0)
        /// </summary>
        public double ErrorRate { get; set; }

        /// <summary>
        /// Gets or sets the memory metrics over time
        /// </summary>
        public List<double> MemoryMetrics { get; set; } = new();

        /// <summary>
        /// Gets or sets the CPU metrics over time
        /// </summary>
        public List<double> CpuMetrics { get; set; } = new();
    }

    /// <summary>
    /// Recovery test results
    /// </summary>
    public class RecoveryResults
    {
        /// <summary>
        /// Gets or sets the recovery time in seconds
        /// </summary>
        public double RecoveryTimeSeconds { get; set; }

        /// <summary>
        /// Gets or sets the recovery completeness (0.0 to 1.0)
        /// </summary>
        public double RecoveryCompleteness { get; set; }
    }

    /// <summary>
    /// Adaptation measurement for adaptive optimization
    /// </summary>
    public class AdaptationMeasurement
    {
        /// <summary>
        /// Gets or sets the load level (0.0 to 1.0)
        /// </summary>
        public double LoadLevel { get; set; }

        /// <summary>
        /// Gets or sets the performance score (0.0 to 1.0)
        /// </summary>
        public double PerformanceScore { get; set; }

        /// <summary>
        /// Gets or sets the adaptation time
        /// </summary>
        public TimeSpan AdaptationTime { get; set; }

        /// <summary>
        /// Gets or sets the number of optimization changes
        /// </summary>
        public int OptimizationChanges { get; set; }
    }

    // Complex product testing models

    /// <summary>
    /// Variant processing test results
    /// </summary>
    public class VariantProcessingResults
    {
        /// <summary>
        /// Gets or sets the processing efficiency (0.0 to 1.0)
        /// </summary>
        public double ProcessingEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the variants processed per second
        /// </summary>
        public double VariantsPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the complex product handling rate (0.0 to 1.0)
        /// </summary>
        public double ComplexProductHandlingRate { get; set; }
    }

    /// <summary>
    /// Image processing test results
    /// </summary>
    public class ImageProcessingResults
    {
        /// <summary>
        /// Gets or sets the throughput in images per second
        /// </summary>
        public double ThroughputImagesPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the processing accuracy (0.0 to 1.0)
        /// </summary>
        public double ProcessingAccuracy { get; set; }

        /// <summary>
        /// Gets or sets the large image handling score (0.0 to 1.0)
        /// </summary>
        public double LargeImageHandling { get; set; }
    }

    /// <summary>
    /// Modifier handling test results
    /// </summary>
    public class ModifierHandlingResults
    {
        /// <summary>
        /// Gets or sets the handling accuracy (0.0 to 1.0)
        /// </summary>
        public double HandlingAccuracy { get; set; }

        /// <summary>
        /// Gets or sets the complex modifier support (0.0 to 1.0)
        /// </summary>
        public double ComplexModifierSupport { get; set; }

        /// <summary>
        /// Gets or sets the processing speed (modifiers per second)
        /// </summary>
        public double ProcessingSpeed { get; set; }
    }

    /// <summary>
    /// Timeout prevention test results
    /// </summary>
    public class TimeoutPreventionResults
    {
        /// <summary>
        /// Gets or sets the prevention effectiveness (0.0 to 1.0)
        /// </summary>
        public double PreventionEffectiveness { get; set; }

        /// <summary>
        /// Gets or sets the large product completion rate (0.0 to 1.0)
        /// </summary>
        public double LargeProductCompletionRate { get; set; }

        /// <summary>
        /// Gets or sets the sub-orchestration utilization (0.0 to 1.0)
        /// </summary>
        public double SubOrchestrationUtilization { get; set; }
    }

    // Multi-channel testing models

    /// <summary>
    /// Channel isolation test results
    /// </summary>
    public class ChannelIsolationResults
    {
        /// <summary>
        /// Gets or sets the isolation effectiveness (0.0 to 1.0)
        /// </summary>
        public double IsolationEffectiveness { get; set; }

        /// <summary>
        /// Gets or sets the cross-channel interference (0.0 to 1.0)
        /// </summary>
        public double CrossChannelInterference { get; set; }

        /// <summary>
        /// Gets or sets the independent performance (0.0 to 1.0)
        /// </summary>
        public double IndependentPerformance { get; set; }
    }

    /// <summary>
    /// Channel load balancing test results
    /// </summary>
    public class ChannelLoadBalancingResults
    {
        /// <summary>
        /// Gets or sets the load balancing score (0.0 to 1.0)
        /// </summary>
        public double LoadBalancingScore { get; set; }

        /// <summary>
        /// Gets or sets the load distribution variance (0.0 to 1.0)
        /// </summary>
        public double LoadDistributionVariance { get; set; }

        /// <summary>
        /// Gets or sets the channel utilization balance (0.0 to 1.0)
        /// </summary>
        public double ChannelUtilizationBalance { get; set; }
    }

    /// <summary>
    /// Linear scaling test results
    /// </summary>
    public class LinearScalingResults
    {
        /// <summary>
        /// Gets or sets the scaling factor (0.0 to 1.0)
        /// </summary>
        public double ScalingFactor { get; set; }

        /// <summary>
        /// Gets or sets the performance linearity (0.0 to 1.0)
        /// </summary>
        public double PerformanceLinearity { get; set; }

        /// <summary>
        /// Gets or sets the resource linear growth (0.0 to 1.0)
        /// </summary>
        public double ResourceLinearGrowth { get; set; }
    }

    /// <summary>
    /// Resource sharing test results
    /// </summary>
    public class ResourceSharingResults
    {
        /// <summary>
        /// Gets or sets the sharing efficiency (0.0 to 1.0)
        /// </summary>
        public double SharingEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the resource optimization gain (0.0 to 1.0)
        /// </summary>
        public double ResourceOptimizationGain { get; set; }

        /// <summary>
        /// Gets or sets the conflict resolution rate (0.0 to 1.0)
        /// </summary>
        public double ConflictResolutionRate { get; set; }
    }
} 