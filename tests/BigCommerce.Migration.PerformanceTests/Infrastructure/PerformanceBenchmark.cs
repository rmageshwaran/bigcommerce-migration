using System.Diagnostics;

namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// High-precision performance measurement utility for benchmarking operations
/// Measures both execution time and memory usage with high accuracy
/// </summary>
public class PerformanceBenchmark
{
    /// <summary>
    /// Measures the performance of an asynchronous operation
    /// </summary>
    /// <param name="operation">The operation to measure</param>
    /// <returns>Performance measurement results</returns>
    public async Task<PerformanceResult> MeasureAsync(Func<Task> operation)
    {
        // Force garbage collection before measurement for accurate memory baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var result = new PerformanceResult();
        
        // Capture initial state
        result.StartTime = DateTime.UtcNow;
        result.MemoryBefore = GC.GetTotalMemory(forceFullCollection: false);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Execute the operation
            await operation().ConfigureAwait(false);
        }
        finally
        {
            // Stop timing immediately
            stopwatch.Stop();
            
            // Capture final state
            result.EndTime = DateTime.UtcNow;
            result.MemoryAfter = GC.GetTotalMemory(forceFullCollection: false);
            
            // Set timing measurements
            result.ElapsedTicks = stopwatch.ElapsedTicks;
            result.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }
        
        return result;
    }
    
    /// <summary>
    /// Measures the performance of an operation that returns a value
    /// </summary>
    /// <typeparam name="T">The return type of the operation</typeparam>
    /// <param name="operation">The operation to measure</param>
    /// <returns>Performance measurement results and the operation result</returns>
    public async Task<(PerformanceResult Performance, T Result)> MeasureAsync<T>(Func<Task<T>> operation)
    {
        // Force garbage collection before measurement for accurate memory baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var result = new PerformanceResult();
        T operationResult = default(T)!;
        
        // Capture initial state
        result.StartTime = DateTime.UtcNow;
        result.MemoryBefore = GC.GetTotalMemory(forceFullCollection: false);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Execute the operation and capture result
            operationResult = await operation().ConfigureAwait(false);
        }
        finally
        {
            // Stop timing immediately
            stopwatch.Stop();
            
            // Capture final state
            result.EndTime = DateTime.UtcNow;
            result.MemoryAfter = GC.GetTotalMemory(forceFullCollection: false);
            
            // Set timing measurements
            result.ElapsedTicks = stopwatch.ElapsedTicks;
            result.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }
        
        return (result, operationResult);
    }
    
    /// <summary>
    /// Measures the performance of a synchronous operation
    /// </summary>
    /// <param name="operation">The synchronous operation to measure</param>
    /// <returns>Performance measurement results</returns>
    public PerformanceResult Measure(Action operation)
    {
        // Force garbage collection before measurement for accurate memory baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var result = new PerformanceResult();
        
        // Capture initial state
        result.StartTime = DateTime.UtcNow;
        result.MemoryBefore = GC.GetTotalMemory(forceFullCollection: false);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Execute the operation
            operation();
        }
        finally
        {
            // Stop timing immediately
            stopwatch.Stop();
            
            // Capture final state
            result.EndTime = DateTime.UtcNow;
            result.MemoryAfter = GC.GetTotalMemory(forceFullCollection: false);
            
            // Set timing measurements
            result.ElapsedTicks = stopwatch.ElapsedTicks;
            result.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }
        
        return result;
    }
    
    /// <summary>
    /// Measures the performance of a synchronous operation that returns a value
    /// </summary>
    /// <typeparam name="T">The return type of the operation</typeparam>
    /// <param name="operation">The synchronous operation to measure</param>
    /// <returns>Performance measurement results and the operation result</returns>
    public (PerformanceResult Performance, T Result) Measure<T>(Func<T> operation)
    {
        // Force garbage collection before measurement for accurate memory baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var result = new PerformanceResult();
        T operationResult = default(T)!;
        
        // Capture initial state
        result.StartTime = DateTime.UtcNow;
        result.MemoryBefore = GC.GetTotalMemory(forceFullCollection: false);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Execute the operation and capture result
            operationResult = operation();
        }
        finally
        {
            // Stop timing immediately
            stopwatch.Stop();
            
            // Capture final state
            result.EndTime = DateTime.UtcNow;
            result.MemoryAfter = GC.GetTotalMemory(forceFullCollection: false);
            
            // Set timing measurements
            result.ElapsedTicks = stopwatch.ElapsedTicks;
            result.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }
        
        return (result, operationResult);
    }
    
    /// <summary>
    /// Runs multiple iterations of an operation and returns average performance metrics
    /// </summary>
    /// <param name="operation">The operation to measure</param>
    /// <param name="iterations">Number of iterations to run</param>
    /// <returns>Average performance results across all iterations</returns>
    public async Task<PerformanceResult> MeasureAverageAsync(Func<Task> operation, int iterations = 5)
    {
        if (iterations <= 0)
            throw new ArgumentException("Iterations must be greater than 0", nameof(iterations));
        
        var results = new List<PerformanceResult>();
        
        for (int i = 0; i < iterations; i++)
        {
            var result = await MeasureAsync(operation).ConfigureAwait(false);
            results.Add(result);
            
            // Small delay between iterations to allow for stabilization
            if (i < iterations - 1)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
        
        // Calculate averages
        return new PerformanceResult
        {
            StartTime = results.First().StartTime,
            EndTime = results.Last().EndTime,
            ElapsedMilliseconds = results.Average(r => r.ElapsedMilliseconds),
            ElapsedTicks = (long)results.Average(r => r.ElapsedTicks),
            MemoryBefore = (long)results.Average(r => r.MemoryBefore),
            MemoryAfter = (long)results.Average(r => r.MemoryAfter)
        };
    }
} 