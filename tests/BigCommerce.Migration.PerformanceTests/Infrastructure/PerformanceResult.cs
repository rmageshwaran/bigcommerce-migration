using System.Diagnostics;

namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// Contains performance measurement results including timing and memory usage
/// </summary>
public class PerformanceResult
{
    /// <summary>
    /// When the measurement started
    /// </summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>
    /// When the measurement ended
    /// </summary>
    public DateTime EndTime { get; set; }
    
    /// <summary>
    /// Elapsed time in milliseconds
    /// </summary>
    public double ElapsedMilliseconds { get; set; }
    
    /// <summary>
    /// Elapsed time in ticks (high precision)
    /// </summary>
    public long ElapsedTicks { get; set; }
    
    /// <summary>
    /// Memory usage before the operation (in bytes)
    /// </summary>
    public long MemoryBefore { get; set; }
    
    /// <summary>
    /// Memory usage after the operation (in bytes)
    /// </summary>
    public long MemoryAfter { get; set; }
    
    /// <summary>
    /// Net memory used during the operation (MemoryAfter - MemoryBefore)
    /// </summary>
    public long MemoryUsed => MemoryAfter - MemoryBefore;
    
    /// <summary>
    /// Checks if this result is faster than another result
    /// </summary>
    /// <param name="other">Result to compare against</param>
    /// <returns>True if this result has lower elapsed time</returns>
    public bool IsFasterThan(PerformanceResult other)
    {
        return this.ElapsedMilliseconds < other.ElapsedMilliseconds;
    }
    
    /// <summary>
    /// Checks if this result uses less memory than another result
    /// </summary>
    /// <param name="other">Result to compare against</param>
    /// <returns>True if this result uses less memory</returns>
    public bool UsesLessMemoryThan(PerformanceResult other)
    {
        return this.MemoryUsed < other.MemoryUsed;
    }
    
    /// <summary>
    /// Calculates performance improvement over another result
    /// </summary>
    /// <param name="baseline">Baseline result to compare against</param>
    /// <returns>Performance improvement percentages</returns>
    public PerformanceImprovement GetImprovementOver(PerformanceResult baseline)
    {
        var timeImprovement = baseline.ElapsedMilliseconds == 0 
            ? 0 
            : ((baseline.ElapsedMilliseconds - this.ElapsedMilliseconds) / baseline.ElapsedMilliseconds) * 100;
            
        var memoryImprovement = baseline.MemoryUsed == 0 
            ? 0 
            : ((baseline.MemoryUsed - this.MemoryUsed) / (double)baseline.MemoryUsed) * 100;
        
        return new PerformanceImprovement
        {
            TimeImprovement = timeImprovement,
            MemoryImprovement = memoryImprovement
        };
    }
    
    /// <summary>
    /// Returns a formatted string representation of the performance results
    /// </summary>
    public override string ToString()
    {
        return $"Time: {ElapsedMilliseconds:F1}ms, Memory: {MemoryUsed:N0} bytes";
    }
}

/// <summary>
/// Represents performance improvement percentages
/// </summary>
public class PerformanceImprovement
{
    /// <summary>
    /// Time improvement percentage (positive = faster)
    /// </summary>
    public double TimeImprovement { get; set; }
    
    /// <summary>
    /// Memory improvement percentage (positive = less memory used)
    /// </summary>
    public double MemoryImprovement { get; set; }
    
    /// <summary>
    /// Returns a formatted string representation of the improvements
    /// </summary>
    public override string ToString()
    {
        return $"Time: {TimeImprovement:F1}% faster, Memory: {MemoryImprovement:F1}% less";
    }
} 