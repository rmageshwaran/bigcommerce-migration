using System;
using System.Collections.Generic;
using System.Net.Http;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Supporting data models for ConnectionPoolOptimizer
    /// Phase 5: Task 5.3 - Connection Pooling Optimization
    /// </summary>

    /// <summary>
    /// Configuration for connection pool optimization
    /// </summary>
    public class ConnectionPoolConfiguration
    {
        /// <summary>
        /// Gets or sets the minimum pool size
        /// </summary>
        public int MinPoolSize { get; set; } = 2;

        /// <summary>
        /// Gets or sets the maximum pool size
        /// </summary>
        public int MaxPoolSize { get; set; } = 50;

        /// <summary>
        /// Gets or sets the maximum idle time before connection disposal
        /// </summary>
        public TimeSpan MaxIdleTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the connection timeout
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets whether to enable adaptive pooling
        /// </summary>
        public bool EnableAdaptivePooling { get; set; } = true;

        /// <summary>
        /// Gets or sets the adaptive scaling threshold (0.0 to 1.0)
        /// </summary>
        public double AdaptiveScalingThreshold { get; set; } = 0.8;

        /// <summary>
        /// Gets or sets the pool health check interval
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets or sets whether to enable connection warming
        /// </summary>
        public bool EnableConnectionWarming { get; set; } = true;
    }

    /// <summary>
    /// Configuration for connection lifecycle optimization
    /// </summary>
    public class ConnectionLifecycleConfiguration
    {
        /// <summary>
        /// Gets or sets whether to enable connection pre-warming
        /// </summary>
        public bool EnableConnectionPreWarming { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to enable keep-alive
        /// </summary>
        public bool EnableKeepAlive { get; set; } = true;

        /// <summary>
        /// Gets or sets the keep-alive interval
        /// </summary>
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the maximum connection age before forced renewal
        /// </summary>
        public TimeSpan MaxConnectionAge { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Gets or sets whether to enable connection validation
        /// </summary>
        public bool EnableConnectionValidation { get; set; } = true;

        /// <summary>
        /// Gets or sets the validation interval
        /// </summary>
        public TimeSpan ValidationInterval { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Gets or sets whether to enable graceful shutdown
        /// </summary>
        public bool EnableGracefulShutdown { get; set; } = true;

        /// <summary>
        /// Gets or sets the graceful shutdown timeout
        /// </summary>
        public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Configuration for concurrent connection management
    /// </summary>
    public class ConcurrentConnectionConfiguration
    {
        /// <summary>
        /// Gets or sets the maximum concurrent connections
        /// </summary>
        public int MaxConcurrentConnections { get; set; } = 20;

        /// <summary>
        /// Gets or sets the connection queue size
        /// </summary>
        public int ConnectionQueueSize { get; set; } = 100;

        /// <summary>
        /// Gets or sets the queue timeout
        /// </summary>
        public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets whether to enable load balancing
        /// </summary>
        public bool EnableLoadBalancing { get; set; } = true;

        /// <summary>
        /// Gets or sets the load balancing strategy
        /// </summary>
        public ConnectionLoadBalancingStrategy LoadBalancingStrategy { get; set; } = ConnectionLoadBalancingStrategy.RoundRobin;

        /// <summary>
        /// Gets or sets the connection affinity strategy
        /// </summary>
        public ConnectionAffinityStrategy AffinityStrategy { get; set; } = ConnectionAffinityStrategy.None;

        /// <summary>
        /// Gets or sets whether to enable connection multiplexing
        /// </summary>
        public bool EnableConnectionMultiplexing { get; set; } = true;
    }

    /// <summary>
    /// Load balancing strategies for connections
    /// </summary>
    public enum ConnectionLoadBalancingStrategy
    {
        /// <summary>
        /// Round-robin distribution
        /// </summary>
        RoundRobin,

        /// <summary>
        /// Least connections strategy
        /// </summary>
        LeastConnections,

        /// <summary>
        /// Weighted round-robin
        /// </summary>
        WeightedRoundRobin,

        /// <summary>
        /// Random selection
        /// </summary>
        Random,

        /// <summary>
        /// Fastest response time
        /// </summary>
        FastestResponse
    }

    /// <summary>
    /// Connection affinity strategies
    /// </summary>
    public enum ConnectionAffinityStrategy
    {
        /// <summary>
        /// No connection affinity
        /// </summary>
        None,

        /// <summary>
        /// Session-based affinity
        /// </summary>
        Session,

        /// <summary>
        /// Client-based affinity
        /// </summary>
        Client,

        /// <summary>
        /// Endpoint-based affinity
        /// </summary>
        Endpoint
    }

    /// <summary>
    /// Configuration for connection monitoring
    /// </summary>
    public class ConnectionMonitoringConfiguration
    {
        /// <summary>
        /// Gets or sets whether to enable real-time monitoring
        /// </summary>
        public bool EnableRealTimeMonitoring { get; set; } = true;

        /// <summary>
        /// Gets or sets the metrics collection interval
        /// </summary>
        public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets whether to enable performance alerting
        /// </summary>
        public bool EnablePerformanceAlerting { get; set; } = true;

        /// <summary>
        /// Gets or sets the performance thresholds
        /// </summary>
        public ConnectionPerformanceThresholds PerformanceThresholds { get; set; } = new();

        /// <summary>
        /// Gets or sets whether to enable detailed logging
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = false;

        /// <summary>
        /// Gets or sets the metrics retention period
        /// </summary>
        public TimeSpan MetricsRetentionPeriod { get; set; } = TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Performance thresholds for connection monitoring
    /// </summary>
    public class ConnectionPerformanceThresholds
    {
        /// <summary>
        /// Gets or sets the maximum acceptable latency
        /// </summary>
        public TimeSpan MaxLatency { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Gets or sets the minimum success rate
        /// </summary>
        public double MinSuccessRate { get; set; } = 0.95;

        /// <summary>
        /// Gets or sets the maximum error rate
        /// </summary>
        public double MaxErrorRate { get; set; } = 0.05;

        /// <summary>
        /// Gets or sets the maximum queue wait time
        /// </summary>
        public TimeSpan MaxQueueWaitTime { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the minimum pool utilization
        /// </summary>
        public double MinPoolUtilization { get; set; } = 0.2;

        /// <summary>
        /// Gets or sets the maximum pool utilization
        /// </summary>
        public double MaxPoolUtilization { get; set; } = 0.9;
    }

    /// <summary>
    /// Configuration for resource optimization
    /// </summary>
    public class ResourceOptimizationConfiguration
    {
        /// <summary>
        /// Gets or sets whether to enable memory optimization
        /// </summary>
        public bool EnableMemoryOptimization { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to enable CPU optimization
        /// </summary>
        public bool EnableCpuOptimization { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum memory usage in bytes
        /// </summary>
        public long MaxMemoryUsageBytes { get; set; } = 100 * 1024 * 1024; // 100MB

        /// <summary>
        /// Gets or sets the maximum CPU usage percentage
        /// </summary>
        public double MaxCpuUsagePercent { get; set; } = 0.5; // 50%

        /// <summary>
        /// Gets or sets whether to enable garbage collection optimization
        /// </summary>
        public bool EnableGarbageCollectionOptimization { get; set; } = true;

        /// <summary>
        /// Gets or sets the resource optimization strategy
        /// </summary>
        public ResourceOptimizationStrategy OptimizationStrategy { get; set; } = ResourceOptimizationStrategy.Balanced;

        /// <summary>
        /// Gets or sets the resource monitoring interval
        /// </summary>
        public TimeSpan ResourceMonitoringInterval { get; set; } = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Resource optimization strategies
    /// </summary>
    public enum ResourceOptimizationStrategy
    {
        /// <summary>
        /// Optimize for minimum memory usage
        /// </summary>
        MemoryOptimized,

        /// <summary>
        /// Optimize for minimum CPU usage
        /// </summary>
        CpuOptimized,

        /// <summary>
        /// Balanced optimization
        /// </summary>
        Balanced,

        /// <summary>
        /// Optimize for maximum performance
        /// </summary>
        PerformanceOptimized,

        /// <summary>
        /// Optimize for maximum throughput
        /// </summary>
        ThroughputOptimized
    }

    /// <summary>
    /// Configuration for connection error handling
    /// </summary>
    public class ConnectionErrorHandlingConfiguration
    {
        /// <summary>
        /// Gets or sets whether to enable automatic retry
        /// </summary>
        public bool EnableAutomaticRetry { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the retry backoff strategy
        /// </summary>
        public BackoffStrategy RetryBackoffStrategy { get; set; } = BackoffStrategy.Exponential;

        /// <summary>
        /// Gets or sets whether to enable circuit breaker
        /// </summary>
        public bool EnableCircuitBreaker { get; set; } = true;

        /// <summary>
        /// Gets or sets the circuit breaker threshold
        /// </summary>
        public double CircuitBreakerThreshold { get; set; } = 0.5;

        /// <summary>
        /// Gets or sets the health check interval
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the circuit breaker reset timeout
        /// </summary>
        public TimeSpan CircuitBreakerResetTimeout { get; set; } = TimeSpan.FromMinutes(2);
    }

    /// <summary>
    /// Backoff strategies for retry operations
    /// </summary>
    public enum BackoffStrategy
    {
        /// <summary>
        /// Fixed delay between retries
        /// </summary>
        Fixed,

        /// <summary>
        /// Linear increase in delay
        /// </summary>
        Linear,

        /// <summary>
        /// Exponential increase in delay
        /// </summary>
        Exponential,

        /// <summary>
        /// Random jitter with exponential base
        /// </summary>
        ExponentialWithJitter
    }

    /// <summary>
    /// Comprehensive connection configuration
    /// </summary>
    public class ComprehensiveConnectionConfiguration
    {
        /// <summary>
        /// Gets or sets the pool configuration
        /// </summary>
        public ConnectionPoolConfiguration PoolConfiguration { get; set; } = new();

        /// <summary>
        /// Gets or sets the lifecycle configuration
        /// </summary>
        public ConnectionLifecycleConfiguration LifecycleConfiguration { get; set; } = new();

        /// <summary>
        /// Gets or sets the concurrency configuration
        /// </summary>
        public ConcurrentConnectionConfiguration ConcurrencyConfiguration { get; set; } = new();

        /// <summary>
        /// Gets or sets the monitoring configuration
        /// </summary>
        public ConnectionMonitoringConfiguration MonitoringConfiguration { get; set; } = new();

        /// <summary>
        /// Gets or sets the resource configuration
        /// </summary>
        public ResourceOptimizationConfiguration ResourceConfiguration { get; set; } = new();

        /// <summary>
        /// Gets or sets the error handling configuration
        /// </summary>
        public ConnectionErrorHandlingConfiguration ErrorHandlingConfiguration { get; set; } = new();
    }

    /// <summary>
    /// Represents a connection request for optimization
    /// </summary>
    public class ConnectionRequest
    {
        /// <summary>
        /// Gets or sets the request identifier
        /// </summary>
        public string RequestId { get; set; } = "";

        /// <summary>
        /// Gets or sets the target endpoint
        /// </summary>
        public string Endpoint { get; set; } = "";

        /// <summary>
        /// Gets or sets the HTTP method
        /// </summary>
        public HttpMethod Method { get; set; } = HttpMethod.Get;

        /// <summary>
        /// Gets or sets the request priority
        /// </summary>
        public RequestPriority Priority { get; set; } = RequestPriority.Normal;

        /// <summary>
        /// Gets or sets the request timeout
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets whether this request requires a new connection
        /// </summary>
        public bool RequiresNewConnection { get; set; } = false;

        /// <summary>
        /// Gets or sets the request timestamp
        /// </summary>
        public DateTime RequestTimestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets additional headers for the request
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// Gets or sets whether to simulate an error (for testing)
        /// </summary>
        public bool SimulateError { get; set; } = false;
    }

    /// <summary>
    /// Request priority levels
    /// </summary>
    public enum RequestPriority
    {
        /// <summary>
        /// Low priority request
        /// </summary>
        Low = 0,

        /// <summary>
        /// Normal priority request
        /// </summary>
        Normal = 1,

        /// <summary>
        /// High priority request
        /// </summary>
        High = 2,

        /// <summary>
        /// Critical priority request
        /// </summary>
        Critical = 3
    }

    /// <summary>
    /// Results from connection pool optimization
    /// </summary>
    public class ConnectionPoolOptimizationResults
    {
        /// <summary>
        /// Gets or sets the resource efficiency gain percentage
        /// </summary>
        public double ResourceEfficiencyGain { get; set; }

        /// <summary>
        /// Gets or sets the optimized pool size
        /// </summary>
        public int OptimizedPoolSize { get; set; }

        /// <summary>
        /// Gets or sets the connection reuse rate
        /// </summary>
        public double ConnectionReuseRate { get; set; }

        /// <summary>
        /// Gets or sets the average connection latency in milliseconds
        /// </summary>
        public double AverageConnectionLatency { get; set; }

        /// <summary>
        /// Gets or sets the pool utilization efficiency
        /// </summary>
        public double PoolUtilizationEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the total connections created
        /// </summary>
        public int TotalConnectionsCreated { get; set; }

        /// <summary>
        /// Gets or sets the number of connections reused
        /// </summary>
        public int ConnectionsReused { get; set; }

        /// <summary>
        /// Gets or sets the optimization timestamp
        /// </summary>
        public DateTime OptimizationTimestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Results from connection lifecycle optimization
    /// </summary>
    public class ConnectionLifecycleOptimizationResults
    {
        /// <summary>
        /// Gets or sets the connection establishment time in milliseconds
        /// </summary>
        public double ConnectionEstablishmentTime { get; set; }

        /// <summary>
        /// Gets or sets the connection validation success rate
        /// </summary>
        public double ConnectionValidationSuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the keep-alive effectiveness
        /// </summary>
        public double KeepAliveEffectiveness { get; set; }

        /// <summary>
        /// Gets or sets the pre-warming effectiveness
        /// </summary>
        public double PreWarmingEffectiveness { get; set; }

        /// <summary>
        /// Gets or sets the overall performance improvement percentage
        /// </summary>
        public double OverallPerformanceImprovement { get; set; }

        /// <summary>
        /// Gets or sets the connection lifetime statistics
        /// </summary>
        public ConnectionLifetimeStatistics LifetimeStatistics { get; set; } = new();
    }

    /// <summary>
    /// Connection lifetime statistics
    /// </summary>
    public class ConnectionLifetimeStatistics
    {
        /// <summary>
        /// Gets or sets the average connection age
        /// </summary>
        public TimeSpan AverageConnectionAge { get; set; }

        /// <summary>
        /// Gets or sets the maximum connection age
        /// </summary>
        public TimeSpan MaxConnectionAge { get; set; }

        /// <summary>
        /// Gets or sets the total connections expired
        /// </summary>
        public int ConnectionsExpired { get; set; }

        /// <summary>
        /// Gets or sets the connections gracefully closed
        /// </summary>
        public int ConnectionsGracefullyClosed { get; set; }
    }

    /// <summary>
    /// Results from concurrent connection handling
    /// </summary>
    public class ConcurrentConnectionResults
    {
        /// <summary>
        /// Gets or sets the number of concurrent requests handled
        /// </summary>
        public int ConcurrentRequestsHandled { get; set; }

        /// <summary>
        /// Gets or sets the average wait time in milliseconds
        /// </summary>
        public double AverageWaitTime { get; set; }

        /// <summary>
        /// Gets or sets the throughput per second
        /// </summary>
        public double ThroughputPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the connection pool efficiency
        /// </summary>
        public double ConnectionPoolEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the queue overflow rate
        /// </summary>
        public double QueueOverflowRate { get; set; }

        /// <summary>
        /// Gets or sets the load balancing effectiveness
        /// </summary>
        public double LoadBalancingEffectiveness { get; set; }
    }

    /// <summary>
    /// Results from connection monitoring
    /// </summary>
    public class ConnectionMonitoringResults
    {
        /// <summary>
        /// Gets or sets the performance metrics collected
        /// </summary>
        public IList<ConnectionPerformanceMetric> PerformanceMetrics { get; set; } = new List<ConnectionPerformanceMetric>();

        /// <summary>
        /// Gets or sets the connection statistics
        /// </summary>
        public ConnectionStatistics ConnectionStatistics { get; set; } = new();

        /// <summary>
        /// Gets or sets the resource utilization information
        /// </summary>
        public ResourceUtilization ResourceUtilization { get; set; } = new();

        /// <summary>
        /// Gets or sets the performance alerts
        /// </summary>
        public IList<PerformanceAlert> PerformanceAlerts { get; set; } = new List<PerformanceAlert>();

        /// <summary>
        /// Gets or sets the overall health score (0.0 to 1.0)
        /// </summary>
        public double OverallHealthScore { get; set; }

        /// <summary>
        /// Gets or sets the monitoring timestamp
        /// </summary>
        public DateTime MonitoringTimestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Performance metric for connections
    /// </summary>
    public class ConnectionPerformanceMetric
    {
        /// <summary>
        /// Gets or sets the metric name
        /// </summary>
        public string MetricName { get; set; } = "";

        /// <summary>
        /// Gets or sets the metric value
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// Gets or sets the metric unit
        /// </summary>
        public string Unit { get; set; } = "";

        /// <summary>
        /// Gets or sets the measurement timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets additional metric tags
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Connection statistics
    /// </summary>
    public class ConnectionStatistics
    {
        /// <summary>
        /// Gets or sets the total connections created
        /// </summary>
        public int TotalConnections { get; set; }

        /// <summary>
        /// Gets or sets the active connections
        /// </summary>
        public int ActiveConnections { get; set; }

        /// <summary>
        /// Gets or sets the idle connections
        /// </summary>
        public int IdleConnections { get; set; }

        /// <summary>
        /// Gets or sets the failed connections
        /// </summary>
        public int FailedConnections { get; set; }

        /// <summary>
        /// Gets or sets the average response time in milliseconds
        /// </summary>
        public double AverageResponseTime { get; set; }

        /// <summary>
        /// Gets or sets the success rate
        /// </summary>
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// Resource utilization information
    /// </summary>
    public class ResourceUtilization
    {
        /// <summary>
        /// Gets or sets the memory usage in bytes
        /// </summary>
        public long MemoryUsageBytes { get; set; }

        /// <summary>
        /// Gets or sets the CPU usage percentage
        /// </summary>
        public double CpuUsagePercent { get; set; }

        /// <summary>
        /// Gets or sets the thread count
        /// </summary>
        public int ThreadCount { get; set; }

        /// <summary>
        /// Gets or sets the handle count
        /// </summary>
        public int HandleCount { get; set; }

        /// <summary>
        /// Gets or sets the garbage collection metrics
        /// </summary>
        public GarbageCollectionMetrics GcMetrics { get; set; } = new();
    }

    /// <summary>
    /// Garbage collection metrics
    /// </summary>
    public class GarbageCollectionMetrics
    {
        /// <summary>
        /// Gets or sets the generation 0 collections
        /// </summary>
        public int Gen0Collections { get; set; }

        /// <summary>
        /// Gets or sets the generation 1 collections
        /// </summary>
        public int Gen1Collections { get; set; }

        /// <summary>
        /// Gets or sets the generation 2 collections
        /// </summary>
        public int Gen2Collections { get; set; }

        /// <summary>
        /// Gets or sets the total allocated bytes
        /// </summary>
        public long TotalAllocatedBytes { get; set; }
    }

    /// <summary>
    /// Performance alert
    /// </summary>
    public class PerformanceAlert
    {
        /// <summary>
        /// Gets or sets the alert severity
        /// </summary>
        public AlertSeverity Severity { get; set; }

        /// <summary>
        /// Gets or sets the alert message
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Gets or sets the metric name that triggered the alert
        /// </summary>
        public string MetricName { get; set; } = "";

        /// <summary>
        /// Gets or sets the threshold value
        /// </summary>
        public double ThresholdValue { get; set; }

        /// <summary>
        /// Gets or sets the actual value
        /// </summary>
        public double ActualValue { get; set; }

        /// <summary>
        /// Gets or sets the alert timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Alert severity levels
    /// </summary>
    public enum AlertSeverity
    {
        /// <summary>
        /// Informational alert
        /// </summary>
        Info,

        /// <summary>
        /// Warning alert
        /// </summary>
        Warning,

        /// <summary>
        /// Error alert
        /// </summary>
        Error,

        /// <summary>
        /// Critical alert
        /// </summary>
        Critical
    }

    /// <summary>
    /// Results from resource optimization
    /// </summary>
    public class ResourceOptimizationResults
    {
        /// <summary>
        /// Gets or sets the CPU efficiency gain percentage
        /// </summary>
        public double CpuEfficiencyGain { get; set; }

        /// <summary>
        /// Gets or sets the memory efficiency gain percentage
        /// </summary>
        public double MemoryEfficiencyGain { get; set; }

        /// <summary>
        /// Gets or sets the garbage collection optimization improvement
        /// </summary>
        public double GarbageCollectionOptimization { get; set; }

        /// <summary>
        /// Gets or sets the overall resource efficiency (0.0 to 1.0)
        /// </summary>
        public double OverallResourceEfficiency { get; set; }

        /// <summary>
        /// Gets or sets the resource usage before optimization
        /// </summary>
        public ResourceUtilization ResourceUsageBefore { get; set; } = new();

        /// <summary>
        /// Gets or sets the resource usage after optimization
        /// </summary>
        public ResourceUtilization ResourceUsageAfter { get; set; } = new();
    }

    /// <summary>
    /// Results from connection error handling
    /// </summary>
    public class ConnectionErrorHandlingResults
    {
        /// <summary>
        /// Gets or sets the overall success rate
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the retry success rate
        /// </summary>
        public double RetrySuccessRate { get; set; }

        /// <summary>
        /// Gets or sets the number of circuit breaker activations
        /// </summary>
        public int CircuitBreakerActivations { get; set; }

        /// <summary>
        /// Gets or sets the average recovery time in milliseconds
        /// </summary>
        public double AverageRecoveryTime { get; set; }

        /// <summary>
        /// Gets or sets the error isolation effectiveness
        /// </summary>
        public double ErrorIsolationEffectiveness { get; set; }

        /// <summary>
        /// Gets or sets the error statistics
        /// </summary>
        public ErrorStatistics ErrorStatistics { get; set; } = new();
    }

    /// <summary>
    /// Error statistics
    /// </summary>
    public class ErrorStatistics
    {
        /// <summary>
        /// Gets or sets the total errors encountered
        /// </summary>
        public int TotalErrors { get; set; }

        /// <summary>
        /// Gets or sets the errors recovered through retry
        /// </summary>
        public int ErrorsRecovered { get; set; }

        /// <summary>
        /// Gets or sets the permanent failures
        /// </summary>
        public int PermanentFailures { get; set; }

        /// <summary>
        /// Gets or sets the timeout errors
        /// </summary>
        public int TimeoutErrors { get; set; }

        /// <summary>
        /// Gets or sets the connection errors
        /// </summary>
        public int ConnectionErrors { get; set; }
    }

    /// <summary>
    /// Comprehensive optimization results
    /// </summary>
    public class ComprehensiveConnectionOptimizationResults
    {
        /// <summary>
        /// Gets or sets the overall efficiency gain percentage
        /// </summary>
        public double OverallEfficiencyGain { get; set; }

        /// <summary>
        /// Gets or sets the resource utilization optimization (0.0 to 1.0)
        /// </summary>
        public double ResourceUtilizationOptimization { get; set; }

        /// <summary>
        /// Gets or sets the performance consistency score (0.0 to 1.0)
        /// </summary>
        public double PerformanceConsistency { get; set; }

        /// <summary>
        /// Gets or sets the scalability score (0.0 to 1.0)
        /// </summary>
        public double ScalabilityScore { get; set; }

        /// <summary>
        /// Gets or sets the reliability score (0.0 to 1.0)
        /// </summary>
        public double ReliabilityScore { get; set; }

        /// <summary>
        /// Gets or sets the total connections processed
        /// </summary>
        public int TotalConnectionsProcessed { get; set; }

        /// <summary>
        /// Gets or sets the average processing time in milliseconds
        /// </summary>
        public double AverageProcessingTime { get; set; }

        /// <summary>
        /// Gets or sets the optimization summary
        /// </summary>
        public OptimizationSummary Summary { get; set; } = new();
    }

    /// <summary>
    /// Summary of optimization improvements
    /// </summary>
    public class OptimizationSummary
    {
        /// <summary>
        /// Gets or sets the pool optimization results
        /// </summary>
        public ConnectionPoolOptimizationResults PoolOptimization { get; set; } = new();

        /// <summary>
        /// Gets or sets the lifecycle optimization results
        /// </summary>
        public ConnectionLifecycleOptimizationResults LifecycleOptimization { get; set; } = new();

        /// <summary>
        /// Gets or sets the concurrent handling results
        /// </summary>
        public ConcurrentConnectionResults ConcurrentHandling { get; set; } = new();

        /// <summary>
        /// Gets or sets the monitoring results
        /// </summary>
        public ConnectionMonitoringResults Monitoring { get; set; } = new();

        /// <summary>
        /// Gets or sets the resource optimization results
        /// </summary>
        public ResourceOptimizationResults ResourceOptimization { get; set; } = new();

        /// <summary>
        /// Gets or sets the error handling results
        /// </summary>
        public ConnectionErrorHandlingResults ErrorHandling { get; set; } = new();
    }
} 