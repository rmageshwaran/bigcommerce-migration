namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// Result of memory usage analysis during an operation
/// </summary>
public class MemoryAnalysisResult
{
    /// <summary>
    /// Peak memory usage during the operation (bytes)
    /// </summary>
    public long PeakMemoryUsage { get; set; }

    /// <summary>
    /// Final memory usage after the operation (bytes)
    /// </summary>
    public long FinalMemoryUsage { get; set; }

    /// <summary>
    /// Memory usage at the start of the operation (bytes)
    /// </summary>
    public long InitialMemoryUsage { get; set; }

    /// <summary>
    /// Duration of the processing operation (milliseconds)
    /// </summary>
    public double ProcessingDuration { get; set; }

    /// <summary>
    /// Garbage collection statistics during the operation
    /// </summary>
    public GCCollectionData GCCollections { get; set; } = new();

    /// <summary>
    /// Rate of memory growth during the operation (bytes per millisecond)
    /// </summary>
    public double MemoryGrowthRate { get; set; }

    /// <summary>
    /// Memory samples taken during the operation
    /// </summary>
    public List<MemorySample> MemorySamples { get; set; } = new();

    /// <summary>
    /// Memory efficiency ratio (final/peak - lower is better)
    /// </summary>
    public double MemoryEfficiencyRatio => PeakMemoryUsage > 0 ? (double)FinalMemoryUsage / PeakMemoryUsage : 0;

    /// <summary>
    /// Total memory allocated during the operation (peak - initial)
    /// </summary>
    public long TotalMemoryAllocated => PeakMemoryUsage - InitialMemoryUsage;

    /// <summary>
    /// Memory recovered by garbage collection (peak - final)
    /// </summary>
    public long MemoryRecovered => PeakMemoryUsage - FinalMemoryUsage;
}

/// <summary>
/// Garbage collection statistics
/// </summary>
public class GCCollectionData
{
    /// <summary>
    /// Generation 0 garbage collections
    /// </summary>
    public int Gen0 { get; set; }

    /// <summary>
    /// Generation 1 garbage collections
    /// </summary>
    public int Gen1 { get; set; }

    /// <summary>
    /// Generation 2 garbage collections
    /// </summary>
    public int Gen2 { get; set; }

    /// <summary>
    /// Total garbage collections across all generations
    /// </summary>
    public int Total => Gen0 + Gen1 + Gen2;

    /// <summary>
    /// GC pressure score (higher means more pressure)
    /// </summary>
    public double GCPressureScore => (Gen0 * 1.0) + (Gen1 * 2.0) + (Gen2 * 3.0);
}

/// <summary>
/// Memory sample at a specific point in time during processing
/// </summary>
public class MemorySample
{
    /// <summary>
    /// Timestamp when the sample was taken
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Memory usage at this point (bytes)
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// Milliseconds elapsed since operation start
    /// </summary>
    public double ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Optional context about what was happening when sample was taken
    /// </summary>
    public string? Context { get; set; }
}

/// <summary>
/// Extended memory analysis with additional metrics
/// </summary>
public class DetailedMemoryAnalysis : MemoryAnalysisResult
{
    /// <summary>
    /// Memory usage by generation
    /// </summary>
    public Dictionary<int, long> MemoryByGeneration { get; set; } = new();

    /// <summary>
    /// Large object heap usage
    /// </summary>
    public long LargeObjectHeapUsage { get; set; }

    /// <summary>
    /// Working set memory
    /// </summary>
    public long WorkingSetMemory { get; set; }

    /// <summary>
    /// Private memory usage
    /// </summary>
    public long PrivateMemoryUsage { get; set; }

    /// <summary>
    /// Memory pressure level (0-100, higher is worse)
    /// </summary>
    public int MemoryPressureLevel { get; set; }

    /// <summary>
    /// Average memory growth rate per entity
    /// </summary>
    public double MemoryGrowthRatePerEntity { get; set; }
} 