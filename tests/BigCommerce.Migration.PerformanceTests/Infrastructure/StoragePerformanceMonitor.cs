using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace BigCommerce.Migration.PerformanceTests.Infrastructure
{
    /// <summary>
    /// Phase 5: Bulk Operations & Storage Optimization Infrastructure
    /// Storage Performance Monitor - Tracks storage operation performance and efficiency metrics
    /// 
    /// Goals:
    /// - Monitor storage operation performance during bulk processing
    /// - Track efficiency improvements and optimization metrics
    /// - Provide real-time performance feedback for adaptive optimization
    /// - Support validation of 90% storage efficiency improvement targets
    /// </summary>
    public class StoragePerformanceMonitor : IDisposable
    {
        private readonly ConcurrentDictionary<string, EntityPerformanceMetrics> _entityMetrics;
        private readonly Timer _samplingTimer;
        private bool _isMonitoring;
        private DateTime _monitoringStartTime;

        /// <summary>
        /// Gets the total number of entities monitored
        /// </summary>
        public int TotalEntitiesMonitored => _entityMetrics.Values.Sum(m => m.TotalOperations);

        /// <summary>
        /// Gets the overall efficiency improvement percentage
        /// </summary>
        public double OverallEfficiencyImprovement => CalculateOverallEfficiency();

        /// <summary>
        /// Gets the current storage operations per second
        /// </summary>
        public double StorageOperationsPerSecond { get; private set; }

        /// <summary>
        /// Initializes a new instance of the StoragePerformanceMonitor
        /// </summary>
        public StoragePerformanceMonitor()
        {
            _entityMetrics = new ConcurrentDictionary<string, EntityPerformanceMetrics>();
            _samplingTimer = new Timer(UpdatePerformanceMetrics, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Starts monitoring performance for a specific entity type
        /// </summary>
        /// <param name="entityType">Type of entity to monitor</param>
        /// <returns>Disposable monitoring scope</returns>
        public IDisposable StartMonitoring(string entityType)
        {
            if (!_isMonitoring)
            {
                _isMonitoring = true;
                _monitoringStartTime = DateTime.UtcNow;
                _samplingTimer.Change(0, 1000); // Sample every second
            }

            // Initialize metrics for this entity type
            _entityMetrics.TryAdd(entityType, new EntityPerformanceMetrics
            {
                EntityType = entityType,
                StartTime = DateTime.UtcNow
            });

            return new MonitoringScope(() => StopMonitoring(entityType));
        }

        /// <summary>
        /// Records a storage operation for performance tracking
        /// </summary>
        /// <param name="entityType">Type of entity</param>
        /// <param name="operationType">Type of operation (Individual, Batch, Bulk)</param>
        /// <param name="entityCount">Number of entities in the operation</param>
        /// <param name="operationTime">Time taken for the operation</param>
        /// <param name="memoryUsed">Memory used during the operation</param>
        public void RecordStorageOperation(
            string entityType,
            StorageOperationType operationType,
            int entityCount,
            TimeSpan operationTime,
            long memoryUsed = 0)
        {
            if (!_entityMetrics.TryGetValue(entityType, out var metrics))
            {
                metrics = new EntityPerformanceMetrics { EntityType = entityType };
                _entityMetrics.TryAdd(entityType, metrics);
            }

            // Update metrics atomically
            Interlocked.Increment(ref metrics.TotalOperations);
            Interlocked.Add(ref metrics.TotalEntitiesProcessed, entityCount);
            
            // Update operation type specific metrics
            switch (operationType)
            {
                case StorageOperationType.Individual:
                    Interlocked.Increment(ref metrics.IndividualOperations);
                    break;
                case StorageOperationType.Batch:
                    Interlocked.Increment(ref metrics.BatchOperations);
                    break;
                case StorageOperationType.Bulk:
                    Interlocked.Increment(ref metrics.BulkOperations);
                    break;
            }

            // Update timing and memory metrics
            var totalTime = Interlocked.Read(ref metrics.TotalProcessingTimeMs);
            Interlocked.Exchange(ref metrics.TotalProcessingTimeMs, totalTime + (long)operationTime.TotalMilliseconds);

            if (memoryUsed > 0)
            {
                var currentMaxMemory = Interlocked.Read(ref metrics.PeakMemoryUsage);
                if (memoryUsed > currentMaxMemory)
                {
                    Interlocked.Exchange(ref metrics.PeakMemoryUsage, memoryUsed);
                }
            }

            metrics.LastOperationTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets performance metrics for a specific entity type
        /// </summary>
        /// <param name="entityType">Type of entity</param>
        /// <returns>Performance metrics or null if not found</returns>
        public EntityPerformanceMetrics? GetEntityMetrics(string entityType)
        {
            return _entityMetrics.TryGetValue(entityType, out var metrics) ? metrics : null;
        }

        /// <summary>
        /// Gets performance summary for all monitored entity types
        /// </summary>
        /// <returns>Collection of performance metrics</returns>
        public IReadOnlyCollection<EntityPerformanceMetrics> GetAllMetrics()
        {
            return _entityMetrics.Values.ToList();
        }

        /// <summary>
        /// Calculates the efficiency improvement percentage for bulk vs individual operations
        /// </summary>
        /// <param name="entityType">Type of entity to calculate efficiency for</param>
        /// <returns>Efficiency improvement percentage</returns>
        public double CalculateEfficiencyImprovement(string entityType)
        {
            if (!_entityMetrics.TryGetValue(entityType, out var metrics))
                return 0;

            var totalOperations = metrics.TotalOperations;
            var totalEntities = metrics.TotalEntitiesProcessed;

            if (totalOperations == 0 || totalEntities == 0)
                return 0;

            // Calculate efficiency: (Entities - Operations) / Entities * 100
            // Higher efficiency = fewer operations needed per entity
            return ((double)(totalEntities - totalOperations) / totalEntities) * 100;
        }

        /// <summary>
        /// Stops monitoring for a specific entity type
        /// </summary>
        private void StopMonitoring(string entityType)
        {
            if (_entityMetrics.TryGetValue(entityType, out var metrics))
            {
                metrics.EndTime = DateTime.UtcNow;
                metrics.Duration = metrics.EndTime - metrics.StartTime;
            }
        }

        /// <summary>
        /// Updates performance metrics periodically
        /// </summary>
        private void UpdatePerformanceMetrics(object? state)
        {
            if (!_isMonitoring) return;

            var totalOperations = 0;
            var elapsedSeconds = (DateTime.UtcNow - _monitoringStartTime).TotalSeconds;

            foreach (var metrics in _entityMetrics.Values)
            {
                totalOperations += metrics.TotalOperations;
                
                // Update throughput metrics
                if (metrics.Duration.TotalSeconds > 0)
                {
                    metrics.ThroughputPerSecond = metrics.TotalEntitiesProcessed / metrics.Duration.TotalSeconds;
                }
            }

            // Update overall operations per second
            StorageOperationsPerSecond = elapsedSeconds > 0 ? totalOperations / elapsedSeconds : 0;
        }

        /// <summary>
        /// Calculates overall efficiency improvement across all entity types
        /// </summary>
        private double CalculateOverallEfficiency()
        {
            var totalEntities = 0;
            var totalOperations = 0;

            foreach (var metrics in _entityMetrics.Values)
            {
                totalEntities += metrics.TotalEntitiesProcessed;
                totalOperations += metrics.TotalOperations;
            }

            if (totalEntities == 0 || totalOperations == 0)
                return 0;

            return ((double)(totalEntities - totalOperations) / totalEntities) * 100;
        }

        /// <summary>
        /// Disposes the performance monitor and stops all monitoring
        /// </summary>
        public void Dispose()
        {
            _isMonitoring = false;
            _samplingTimer?.Dispose();

            // Finalize all metrics
            foreach (var metrics in _entityMetrics.Values)
            {
                if (metrics.EndTime == default)
                {
                    metrics.EndTime = DateTime.UtcNow;
                    metrics.Duration = metrics.EndTime - metrics.StartTime;
                }
            }
        }

        /// <summary>
        /// Monitoring scope implementation
        /// </summary>
        private class MonitoringScope : IDisposable
        {
            private readonly Action _onDispose;

            public MonitoringScope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                _onDispose();
            }
        }
    }

    /// <summary>
    /// Performance metrics for a specific entity type
    /// </summary>
    public class EntityPerformanceMetrics
    {
        public string EntityType { get; set; } = "";
        public int TotalOperations { get; set; }
        public int TotalEntitiesProcessed { get; set; }
        public int IndividualOperations { get; set; }
        public int BatchOperations { get; set; }
        public int BulkOperations { get; set; }
        public long TotalProcessingTimeMs { get; set; }
        public long PeakMemoryUsage { get; set; }
        public double ThroughputPerSecond { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime LastOperationTime { get; set; }

        /// <summary>
        /// Gets the average processing time per operation in milliseconds
        /// </summary>
        public double AverageProcessingTimeMs => TotalOperations > 0 
            ? (double)TotalProcessingTimeMs / TotalOperations 
            : 0;

        /// <summary>
        /// Gets the average entities processed per operation
        /// </summary>
        public double AverageEntitiesPerOperation => TotalOperations > 0 
            ? (double)TotalEntitiesProcessed / TotalOperations 
            : 0;

        /// <summary>
        /// Gets the efficiency improvement percentage for this entity type
        /// </summary>
        public double EfficiencyImprovement => TotalEntitiesProcessed > 0 
            ? ((double)(TotalEntitiesProcessed - TotalOperations) / TotalEntitiesProcessed) * 100 
            : 0;
    }

    /// <summary>
    /// Types of storage operations for performance tracking
    /// </summary>
    public enum StorageOperationType
    {
        Individual, // One entity per operation
        Batch,      // Multiple entities per operation
        Bulk        // Large number of entities per operation
    }
} 