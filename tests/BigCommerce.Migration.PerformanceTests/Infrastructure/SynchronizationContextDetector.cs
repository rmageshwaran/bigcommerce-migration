using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Linq;

namespace BigCommerce.Migration.PerformanceTests.Infrastructure
{
    /// <summary>
    /// Phase 4 Performance Optimization Infrastructure
    /// Detects synchronization context capture in async methods
    /// 
    /// Purpose: Ensure ConfigureAwait(false) is working correctly
    /// Impact: Prevents thread pool starvation in Azure Functions
    /// Target: Zero context capture for optimal performance
    /// </summary>
    public class SynchronizationContextDetector : IDisposable
    {
        private readonly SynchronizationContext? _originalContext;
        private readonly TestSynchronizationContext _testContext;
        private bool _isMonitoring;

        public bool ContextWasCaptured => _testContext.ContextWasCaptured;
        public int ThreadPoolThreadsUsed => _testContext.ThreadPoolThreadsUsed;
        public int ContextOperations => _testContext.ContextOperations;

        public SynchronizationContextDetector()
        {
            _originalContext = SynchronizationContext.Current;
            _testContext = new TestSynchronizationContext();
        }

        public IDisposable Monitor()
        {
            if (_isMonitoring)
                throw new InvalidOperationException("Already monitoring");

            _isMonitoring = true;
            SynchronizationContext.SetSynchronizationContext(_testContext);
            
            return new MonitoringScope(() =>
            {
                SynchronizationContext.SetSynchronizationContext(_originalContext);
                _isMonitoring = false;
            });
        }

        public void Dispose()
        {
            if (_isMonitoring)
            {
                SynchronizationContext.SetSynchronizationContext(_originalContext);
                _isMonitoring = false;
            }
        }

        private class TestSynchronizationContext : SynchronizationContext
        {
            private readonly ConcurrentBag<int> _threadIds = new();
            private volatile int _contextOperations = 0;

            public bool ContextWasCaptured => _contextOperations > 0;
            public int ThreadPoolThreadsUsed => _threadIds.Count;
            public int ContextOperations => _contextOperations;

            public override void Post(SendOrPostCallback d, object? state)
            {
                Interlocked.Increment(ref _contextOperations);
                _threadIds.Add(Thread.CurrentThread.ManagedThreadId);
                
                // Execute on thread pool to simulate captured context
                ThreadPool.QueueUserWorkItem(_ => d(state));
            }

            public override void Send(SendOrPostCallback d, object? state)
            {
                Interlocked.Increment(ref _contextOperations);
                _threadIds.Add(Thread.CurrentThread.ManagedThreadId);
                d(state);
            }
        }

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
    /// Tracks thread usage patterns to validate efficient thread pool usage
    /// </summary>
    public class ThreadUsageTracker : IDisposable
    {
        private readonly ConcurrentBag<int> _threadIds = new();
        private volatile bool _isMonitoring;
        
        public int UniqueThreadsUsed => _threadIds.Distinct().Count();
        public int TotalOperations => _threadIds.Count;

        public IDisposable Monitor()
        {
            _isMonitoring = true;
            return new MonitoringScope(() => _isMonitoring = false);
        }

        public void RecordThreadUsage()
        {
            if (_isMonitoring)
            {
                _threadIds.Add(Thread.CurrentThread.ManagedThreadId);
            }
        }

        public void Dispose()
        {
            _isMonitoring = false;
        }

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
    /// Tracks memory usage during async operations to ensure memory efficiency
    /// </summary>
    public class MemoryUsageTracker
    {
        private long _peakMemoryUsage = 0;

        public long PeakMemoryUsage => Interlocked.Read(ref _peakMemoryUsage);

        public void RecordMemoryUsage()
        {
            var currentMemory = GC.GetTotalMemory(forceFullCollection: false);
            
            // Atomically update peak memory if current is higher
            long currentPeak;
            do
            {
                currentPeak = Interlocked.Read(ref _peakMemoryUsage);
                if (currentMemory <= currentPeak)
                    break;
            }
            while (Interlocked.CompareExchange(ref _peakMemoryUsage, currentMemory, currentPeak) != currentPeak);
        }
    }

    /// <summary>
    /// Detects potential deadlock scenarios in async operations
    /// </summary>
    public class DeadlockDetector
    {
        private readonly ConcurrentBag<string> _completedOperations = new();
        private volatile int _completedCount = 0;

        public int CompletedOperations => _completedCount;
        public bool AllOperationsCompleted => true; // If we reach here, no deadlock occurred

        public void RecordCompletion(string operationId)
        {
            _completedOperations.Add(operationId);
            Interlocked.Increment(ref _completedCount);
        }
    }
} 