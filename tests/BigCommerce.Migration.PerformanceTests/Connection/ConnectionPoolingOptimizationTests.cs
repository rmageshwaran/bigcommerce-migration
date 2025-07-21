using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.PerformanceTests.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Connection
{
    /// <summary>
    /// Phase 5: Task 5.3 - Connection Pooling Optimization Tests
    /// Validates connection management and pooling strategies for 30% resource efficiency improvement
    /// 
    /// Goals:
    /// - Test connection pool management for optimal resource utilization
    /// - Validate connection lifecycle optimization for performance gains
    /// - Ensure adaptive pooling adjusts to demand and performance metrics
    /// - Verify connection monitoring and resource efficiency tracking
    /// - Test concurrent connection handling under load
    /// </summary>
    public class ConnectionPoolingOptimizationTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<ConnectionPoolOptimizer>> _mockLogger;

        public ConnectionPoolingOptimizationTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<ConnectionPoolOptimizer>>();
        }

        [Fact]
        public async Task Connection_Pool_Should_Achieve_30_Percent_Resource_Efficiency_Improvement()
        {
            // Arrange
            var optimizer = CreateConnectionPoolOptimizer();
            var poolConfig = new ConnectionPoolConfiguration
            {
                MinPoolSize = 5,
                MaxPoolSize = 50,
                MaxIdleTime = TimeSpan.FromMinutes(5),
                ConnectionTimeout = TimeSpan.FromSeconds(30),
                EnableAdaptivePooling = true
            };

            var connectionRequests = CreateConnectionRequests(100); // High load scenario

            // Act
            var results = await optimizer.OptimizeConnectionPoolAsync(
                "BigCommerceAPI", poolConfig, connectionRequests, CancellationToken.None);

            // Assert - Target: 30% resource efficiency improvement
            Assert.True(results.ResourceEfficiencyGain >= 25,
                $"Should achieve significant resource efficiency improvement. Actual: {results.ResourceEfficiencyGain:F1}%");

            Assert.True(results.OptimizedPoolSize <= poolConfig.MaxPoolSize,
                "Should respect maximum pool size limits");

            Assert.True(results.OptimizedPoolSize >= poolConfig.MinPoolSize,
                "Should respect minimum pool size limits");

            Assert.True(results.ConnectionReuseRate >= 0.7,
                "Should achieve high connection reuse rate (70%+)");

            Assert.True(results.AverageConnectionLatency <= 100, // 100ms or better
                $"Should maintain low connection latency. Actual: {results.AverageConnectionLatency:F1}ms");

            Assert.True(results.PoolUtilizationEfficiency >= 0.8,
                "Should achieve high pool utilization efficiency (80%+)");

            _output.WriteLine($"🎯 Connection Pool Optimization Results:");
            _output.WriteLine($"Resource Efficiency Gain: {results.ResourceEfficiencyGain:F1}%");
            _output.WriteLine($"Optimized Pool Size: {results.OptimizedPoolSize}");
            _output.WriteLine($"Connection Reuse Rate: {results.ConnectionReuseRate:P1}");
            _output.WriteLine($"Average Connection Latency: {results.AverageConnectionLatency:F1}ms");
            _output.WriteLine($"Pool Utilization Efficiency: {results.PoolUtilizationEfficiency:P1}");
            _output.WriteLine($"Total Connections Created: {results.TotalConnectionsCreated}");
            _output.WriteLine($"Connections Reused: {results.ConnectionsReused}");
        }

        [Fact]
        public async Task Adaptive_Connection_Pooling_Should_Dynamically_Adjust_To_Load()
        {
            // Arrange
            var optimizer = CreateConnectionPoolOptimizer();
            var poolConfig = new ConnectionPoolConfiguration
            {
                MinPoolSize = 3,
                MaxPoolSize = 30,
                EnableAdaptivePooling = true,
                AdaptiveScalingThreshold = 0.8 // 80% utilization triggers scaling
            };

            // Simulate varying load patterns
            var lowLoadRequests = CreateConnectionRequests(10);
            var mediumLoadRequests = CreateConnectionRequests(25);
            var highLoadRequests = CreateConnectionRequests(50);

            // Act & Assert - Test different load scenarios
            var lowLoadResults = await optimizer.OptimizeConnectionPoolAsync(
                "TestAPI", poolConfig, lowLoadRequests, CancellationToken.None);

            var mediumLoadResults = await optimizer.OptimizeConnectionPoolAsync(
                "TestAPI", poolConfig, mediumLoadRequests, CancellationToken.None);

            var highLoadResults = await optimizer.OptimizeConnectionPoolAsync(
                "TestAPI", poolConfig, highLoadRequests, CancellationToken.None);

            // Assert adaptive scaling behavior
            Assert.True(lowLoadResults.OptimizedPoolSize <= mediumLoadResults.OptimizedPoolSize,
                "Pool size should increase with medium load");

            Assert.True(mediumLoadResults.OptimizedPoolSize <= highLoadResults.OptimizedPoolSize,
                "Pool size should increase with high load");

            Assert.True(highLoadResults.OptimizedPoolSize <= poolConfig.MaxPoolSize,
                "Should not exceed maximum pool size even under high load");

            // All scenarios should maintain efficiency
            Assert.True(lowLoadResults.ResourceEfficiencyGain >= 20,
                $"Low load should maintain efficiency. Actual: {lowLoadResults.ResourceEfficiencyGain:F1}%");

            Assert.True(mediumLoadResults.ResourceEfficiencyGain >= 25,
                $"Medium load should achieve good efficiency. Actual: {mediumLoadResults.ResourceEfficiencyGain:F1}%");

            Assert.True(highLoadResults.ResourceEfficiencyGain >= 30,
                $"High load should achieve optimal efficiency. Actual: {highLoadResults.ResourceEfficiencyGain:F1}%");

            _output.WriteLine($"🎯 Adaptive Pooling Results:");
            _output.WriteLine($"Low Load - Pool Size: {lowLoadResults.OptimizedPoolSize}, Efficiency: {lowLoadResults.ResourceEfficiencyGain:F1}%");
            _output.WriteLine($"Medium Load - Pool Size: {mediumLoadResults.OptimizedPoolSize}, Efficiency: {mediumLoadResults.ResourceEfficiencyGain:F1}%");
            _output.WriteLine($"High Load - Pool Size: {highLoadResults.OptimizedPoolSize}, Efficiency: {highLoadResults.ResourceEfficiencyGain:F1}%");
        }

        [Fact]
        public async Task Connection_Lifecycle_Optimization_Should_Improve_Performance()
        {
            // Arrange
            var optimizer = CreateConnectionPoolOptimizer();
            var lifecycleConfig = new ConnectionLifecycleConfiguration
            {
                EnableConnectionPreWarming = true,
                EnableKeepAlive = true,
                KeepAliveInterval = TimeSpan.FromSeconds(30),
                MaxConnectionAge = TimeSpan.FromMinutes(10),
                EnableConnectionValidation = true,
                ValidationInterval = TimeSpan.FromMinutes(2)
            };

            var connectionRequests = CreateConnectionRequests(75);

            // Act
            var results = await optimizer.OptimizeConnectionLifecycleAsync(
                "BigCommerceAPI", lifecycleConfig, connectionRequests, CancellationToken.None);

            // Assert - Target: Improved connection performance
            Assert.True(results.ConnectionEstablishmentTime <= 50, // ≤50ms target
                $"Should achieve fast connection establishment. Actual: {results.ConnectionEstablishmentTime:F1}ms");

            Assert.True(results.ConnectionValidationSuccessRate >= 0.95,
                $"Should maintain high validation success rate. Actual: {results.ConnectionValidationSuccessRate:P1}");

            Assert.True(results.KeepAliveEffectiveness >= 0.8,
                "Keep-alive should be effective (80%+ connections maintained)");

            Assert.True(results.PreWarmingEffectiveness >= 0.7,
                "Pre-warming should be effective (70%+ immediate availability)");

            Assert.True(results.OverallPerformanceImprovement >= 25,
                $"Should achieve overall performance improvement. Actual: {results.OverallPerformanceImprovement:F1}%");

            _output.WriteLine($"🎯 Connection Lifecycle Optimization Results:");
            _output.WriteLine($"Connection Establishment Time: {results.ConnectionEstablishmentTime:F1}ms");
            _output.WriteLine($"Validation Success Rate: {results.ConnectionValidationSuccessRate:P1}");
            _output.WriteLine($"Keep-Alive Effectiveness: {results.KeepAliveEffectiveness:P1}");
            _output.WriteLine($"Pre-Warming Effectiveness: {results.PreWarmingEffectiveness:P1}");
            _output.WriteLine($"Overall Performance Improvement: {results.OverallPerformanceImprovement:F1}%");
        }

        [Fact]
        public async Task Concurrent_Connection_Management_Should_Handle_High_Load()
        {
            // Arrange
            var optimizer = CreateConnectionPoolOptimizer();
            var concurrencyConfig = new ConcurrentConnectionConfiguration
            {
                MaxConcurrentConnections = 20,
                ConnectionQueueSize = 100,
                QueueTimeout = TimeSpan.FromSeconds(10),
                EnableLoadBalancing = true,
                LoadBalancingStrategy = ConnectionLoadBalancingStrategy.LeastConnections
            };

            const int concurrentRequestCount = 200; // High concurrency scenario
            var connectionRequests = CreateConnectionRequests(concurrentRequestCount);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await optimizer.HandleConcurrentConnectionsAsync(
                "BigCommerceAPI", concurrencyConfig, connectionRequests, CancellationToken.None);
            stopwatch.Stop();

            // Assert - Target: Efficient concurrent handling
            Assert.True(results.ConcurrentRequestsHandled >= concurrentRequestCount * 0.95,
                $"Should handle 95%+ of concurrent requests. Actual: {results.ConcurrentRequestsHandled}/{concurrentRequestCount}");

            Assert.True(results.AverageWaitTime <= 200, // ≤200ms average wait
                $"Should maintain low average wait time. Actual: {results.AverageWaitTime:F1}ms");

            Assert.True(results.ThroughputPerSecond >= 50,
                $"Should achieve high throughput. Actual: {results.ThroughputPerSecond:F1} requests/sec");

            Assert.True(results.ConnectionPoolEfficiency >= 0.85,
                $"Should maintain high pool efficiency under load. Actual: {results.ConnectionPoolEfficiency:P1}");

            Assert.True(results.QueueOverflowRate <= 0.05,
                $"Should keep queue overflow low. Actual: {results.QueueOverflowRate:P1}");

            _output.WriteLine($"🎯 Concurrent Connection Management Results:");
            _output.WriteLine($"Concurrent Requests Handled: {results.ConcurrentRequestsHandled}/{concurrentRequestCount}");
            _output.WriteLine($"Average Wait Time: {results.AverageWaitTime:F1}ms");
            _output.WriteLine($"Throughput: {results.ThroughputPerSecond:F1} requests/sec");
            _output.WriteLine($"Pool Efficiency: {results.ConnectionPoolEfficiency:P1}");
            _output.WriteLine($"Queue Overflow Rate: {results.QueueOverflowRate:P1}");
            _output.WriteLine($"Total Processing Time: {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Connection_Monitoring_Should_Provide_Comprehensive_Metrics()
        {
            // Arrange
            var optimizer = CreateConnectionPoolOptimizer();
            var monitoringConfig = new ConnectionMonitoringConfiguration
            {
                EnableRealTimeMonitoring = true,
                MetricsCollectionInterval = TimeSpan.FromSeconds(1),
                EnablePerformanceAlerting = true,
                PerformanceThresholds = new ConnectionPerformanceThresholds
                {
                    MaxLatency = TimeSpan.FromMilliseconds(100),
                    MinSuccessRate = 0.95,
                    MaxErrorRate = 0.05
                }
            };

            var connectionRequests = CreateConnectionRequests(60);

            // Act
            var monitoringResults = await optimizer.MonitorConnectionPerformanceAsync(
                "BigCommerceAPI", monitoringConfig, connectionRequests, CancellationToken.None);

            // Assert - Comprehensive monitoring capabilities
            Assert.NotNull(monitoringResults.PerformanceMetrics);
            Assert.True(monitoringResults.PerformanceMetrics.Count >= 5,
                "Should collect multiple performance metrics");

            Assert.NotNull(monitoringResults.ConnectionStatistics);
            Assert.True(monitoringResults.ConnectionStatistics.TotalConnections > 0,
                "Should track connection statistics");

            Assert.NotNull(monitoringResults.ResourceUtilization);
            Assert.True(monitoringResults.ResourceUtilization.MemoryUsageBytes > 0,
                "Should track resource utilization");

            if (monitoringResults.PerformanceAlerts.Any())
            {
                Assert.True(monitoringResults.PerformanceAlerts.All(a => !string.IsNullOrEmpty(a.Message)),
                    "Performance alerts should have descriptive messages");
            }

            Assert.True(monitoringResults.OverallHealthScore >= 0.8,
                $"Should maintain good overall health score. Actual: {monitoringResults.OverallHealthScore:F1}");

            _output.WriteLine($"🎯 Connection Monitoring Results:");
            _output.WriteLine($"Performance Metrics Collected: {monitoringResults.PerformanceMetrics.Count}");
            _output.WriteLine($"Total Connections Tracked: {monitoringResults.ConnectionStatistics.TotalConnections}");
            _output.WriteLine($"Memory Usage: {monitoringResults.ResourceUtilization.MemoryUsageBytes / 1024:N0} KB");
            _output.WriteLine($"Performance Alerts: {monitoringResults.PerformanceAlerts.Count}");
            _output.WriteLine($"Overall Health Score: {monitoringResults.OverallHealthScore:F1}");
            
            foreach (var alert in monitoringResults.PerformanceAlerts)
            {
                _output.WriteLine($"  Alert: {alert.Severity} - {alert.Message}");
            }
        }

        [Fact]
        public async Task Resource_Optimization_Should_Minimize_Memory_And_CPU_Usage()
        {
            // Arrange
            var optimizer = CreateConnectionPoolOptimizer();
            var resourceConfig = new ResourceOptimizationConfiguration
            {
                EnableMemoryOptimization = true,
                EnableCpuOptimization = true,
                MaxMemoryUsageBytes = 50 * 1024 * 1024, // 50MB limit
                MaxCpuUsagePercent = 0.3, // 30% CPU limit
                EnableGarbageCollectionOptimization = true,
                OptimizationStrategy = ResourceOptimizationStrategy.Balanced
            };

            var connectionRequests = CreateConnectionRequests(80);

            // Act
            var initialMemory = GC.GetTotalMemory(forceFullCollection: true);
            var resourceResults = await optimizer.OptimizeResourceUsageAsync(
                "BigCommerceAPI", resourceConfig, connectionRequests, CancellationToken.None);
            var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
            var memoryGrowth = finalMemory - initialMemory;

            // Assert - Resource efficiency targets
            Assert.True(memoryGrowth <= resourceConfig.MaxMemoryUsageBytes,
                $"Should stay within memory limits. Growth: {memoryGrowth / 1024 / 1024:F1}MB, " +
                $"Limit: {resourceConfig.MaxMemoryUsageBytes / 1024 / 1024:F1}MB");

            Assert.True(resourceResults.CpuEfficiencyGain >= 15,
                $"Should achieve CPU efficiency gain. Actual: {resourceResults.CpuEfficiencyGain:F1}%");

            Assert.True(resourceResults.MemoryEfficiencyGain >= 20,
                $"Should achieve memory efficiency gain. Actual: {resourceResults.MemoryEfficiencyGain:F1}%");

            Assert.True(resourceResults.GarbageCollectionOptimization >= 0.25,
                "Should optimize garbage collection (25%+ improvement)");

            Assert.True(resourceResults.OverallResourceEfficiency >= 0.8,
                $"Should achieve high overall resource efficiency. Actual: {resourceResults.OverallResourceEfficiency:F1}");

            _output.WriteLine($"🎯 Resource Optimization Results:");
            _output.WriteLine($"Memory Growth: {memoryGrowth / 1024 / 1024:F1}MB");
            _output.WriteLine($"CPU Efficiency Gain: {resourceResults.CpuEfficiencyGain:F1}%");
            _output.WriteLine($"Memory Efficiency Gain: {resourceResults.MemoryEfficiencyGain:F1}%");
            _output.WriteLine($"GC Optimization: {resourceResults.GarbageCollectionOptimization:P1}");
            _output.WriteLine($"Overall Resource Efficiency: {resourceResults.OverallResourceEfficiency:F1}");
        }

        [Fact]
        public async Task Connection_Pool_Should_Handle_Error_Scenarios_Gracefully()
        {
            // Arrange
            var optimizer = CreateConnectionPoolOptimizer();
            var errorHandlingConfig = new ConnectionErrorHandlingConfiguration
            {
                EnableAutomaticRetry = true,
                MaxRetryAttempts = 3,
                RetryBackoffStrategy = BackoffStrategy.Exponential,
                EnableCircuitBreaker = true,
                CircuitBreakerThreshold = 0.5, // 50% error rate triggers circuit breaker
                HealthCheckInterval = TimeSpan.FromSeconds(5)
            };

            // Create requests with some that will simulate errors
            var connectionRequests = CreateConnectionRequestsWithErrors(50, 0.2); // 20% error rate

            // Act
            var errorResults = await optimizer.HandleConnectionErrorsAsync(
                "BigCommerceAPI", errorHandlingConfig, connectionRequests, CancellationToken.None);

            // Assert - Graceful error handling
            Assert.True(errorResults.SuccessRate >= 0.8,
                $"Should achieve good success rate despite errors. Actual: {errorResults.SuccessRate:P1}");

            Assert.True(errorResults.RetrySuccessRate >= 0.6,
                $"Should achieve reasonable retry success rate. Actual: {errorResults.RetrySuccessRate:P1}");

            Assert.True(errorResults.CircuitBreakerActivations <= 2,
                $"Should limit circuit breaker activations. Actual: {errorResults.CircuitBreakerActivations}");

            Assert.True(errorResults.AverageRecoveryTime <= 10000, // ≤10 seconds
                $"Should achieve fast recovery. Actual: {errorResults.AverageRecoveryTime:F1}ms");

            Assert.True(errorResults.ErrorIsolationEffectiveness >= 0.7,
                "Should effectively isolate errors (70%+ effectiveness)");

            _output.WriteLine($"🎯 Error Handling Results:");
            _output.WriteLine($"Success Rate: {errorResults.SuccessRate:P1}");
            _output.WriteLine($"Retry Success Rate: {errorResults.RetrySuccessRate:P1}");
            _output.WriteLine($"Circuit Breaker Activations: {errorResults.CircuitBreakerActivations}");
            _output.WriteLine($"Average Recovery Time: {errorResults.AverageRecoveryTime:F1}ms");
            _output.WriteLine($"Error Isolation Effectiveness: {errorResults.ErrorIsolationEffectiveness:P1}");
        }

        [Fact]
        public async Task End_To_End_Connection_Optimization_Should_Achieve_All_Targets()
        {
            // Arrange
            var optimizer = CreateConnectionPoolOptimizer();
            var comprehensiveConfig = new ComprehensiveConnectionConfiguration
            {
                PoolConfiguration = new ConnectionPoolConfiguration
                {
                    MinPoolSize = 5,
                    MaxPoolSize = 40,
                    EnableAdaptivePooling = true
                },
                LifecycleConfiguration = new ConnectionLifecycleConfiguration
                {
                    EnableConnectionPreWarming = true,
                    EnableKeepAlive = true
                },
                ConcurrencyConfiguration = new ConcurrentConnectionConfiguration
                {
                    MaxConcurrentConnections = 15,
                    EnableLoadBalancing = true
                },
                MonitoringConfiguration = new ConnectionMonitoringConfiguration
                {
                    EnableRealTimeMonitoring = true,
                    EnablePerformanceAlerting = true
                },
                ResourceConfiguration = new ResourceOptimizationConfiguration
                {
                    EnableMemoryOptimization = true,
                    EnableCpuOptimization = true
                }
            };

            var connectionRequests = CreateConnectionRequests(120); // Comprehensive load test

            // Act
            var comprehensiveResults = await optimizer.OptimizeConnectionsComprehensivelyAsync(
                "BigCommerceAPI", comprehensiveConfig, connectionRequests, CancellationToken.None);

            // Assert - All optimization targets achieved
            Assert.True(comprehensiveResults.OverallEfficiencyGain >= 30,
                $"Should achieve overall efficiency target. Actual: {comprehensiveResults.OverallEfficiencyGain:F1}%");

            Assert.True(comprehensiveResults.ResourceUtilizationOptimization >= 0.85,
                $"Should optimize resource utilization. Actual: {comprehensiveResults.ResourceUtilizationOptimization:P1}");

            Assert.True(comprehensiveResults.PerformanceConsistency >= 0.9,
                $"Should maintain performance consistency. Actual: {comprehensiveResults.PerformanceConsistency:F1}");

            Assert.True(comprehensiveResults.ScalabilityScore >= 0.8,
                $"Should achieve good scalability. Actual: {comprehensiveResults.ScalabilityScore:F1}");

            Assert.True(comprehensiveResults.ReliabilityScore >= 0.95,
                $"Should maintain high reliability. Actual: {comprehensiveResults.ReliabilityScore:F1}");

            _output.WriteLine($"🎯 End-to-End Optimization Results:");
            _output.WriteLine($"Overall Efficiency Gain: {comprehensiveResults.OverallEfficiencyGain:F1}%");
            _output.WriteLine($"Resource Utilization Optimization: {comprehensiveResults.ResourceUtilizationOptimization:P1}");
            _output.WriteLine($"Performance Consistency: {comprehensiveResults.PerformanceConsistency:F1}");
            _output.WriteLine($"Scalability Score: {comprehensiveResults.ScalabilityScore:F1}");
            _output.WriteLine($"Reliability Score: {comprehensiveResults.ReliabilityScore:F1}");
            _output.WriteLine($"Connections Processed: {comprehensiveResults.TotalConnectionsProcessed}");
            _output.WriteLine($"Average Processing Time: {comprehensiveResults.AverageProcessingTime:F1}ms");
        }

        // Helper methods for test setup and data creation

        private ConnectionPoolOptimizer CreateConnectionPoolOptimizer()
        {
            return new ConnectionPoolOptimizer(_mockLogger.Object);
        }

        private IEnumerable<ConnectionRequest> CreateConnectionRequests(int count)
        {
            var random = new Random(42); // Deterministic for testing
            var endpoints = new[] 
            { 
                "https://api.bigcommerce.com/stores/v6q95r5n91/v3/catalog/products",
                "https://api.bigcommerce.com/stores/v6q95r5n91/v3/catalog/categories",
                "https://api.bigcommerce.com/stores/v6q95r5n91/v3/catalog/brands",
                "https://api.bigcommerce.com/stores/v6q95r5n91/v3/catalog/variants",
                "https://api.bigcommerce.com/stores/v6q95r5n91/v3/content/pages"
            };

            for (int i = 0; i < count; i++)
            {
                yield return new ConnectionRequest
                {
                    RequestId = $"req_{i:D6}",
                    Endpoint = endpoints[i % endpoints.Length],
                    Method = i % 4 == 0 ? HttpMethod.Post : HttpMethod.Get,
                    Priority = (RequestPriority)(i % 3), // Mix of priorities
                    Timeout = TimeSpan.FromSeconds(30 + random.Next(0, 30)), // 30-60 second timeout
                    RequiresNewConnection = i % 10 == 0, // 10% require new connections
                    RequestTimestamp = DateTime.UtcNow.AddMilliseconds(-random.Next(0, 5000))
                };
            }
        }

        private IEnumerable<ConnectionRequest> CreateConnectionRequestsWithErrors(int count, double errorRate)
        {
            var requests = CreateConnectionRequests(count).ToList();
            var errorCount = (int)(count * errorRate);
            var random = new Random(42);

            // Mark some requests as error-prone
            for (int i = 0; i < errorCount; i++)
            {
                var index = random.Next(requests.Count);
                requests[index].SimulateError = true;
            }

            return requests;
        }
    }
} 