using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.PerformanceTests.Infrastructure;

/// <summary>
/// TDD RED PHASE: These tests will fail initially because PerformanceBenchmark doesn't exist yet
/// This defines the requirements for our performance benchmarking infrastructure
/// </summary>
public class PerformanceBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Performance_Test_Should_Measure_Execution_Time_Accurately()
    {
        // ARRANGE
        var benchmark = new PerformanceBenchmark();
        
        // ACT
        var result = await benchmark.MeasureAsync(async () =>
        {
            await Task.Delay(100).ConfigureAwait(false); // Simulate 100ms operation
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.InRange(result.ElapsedMilliseconds, 95, 110); // 5ms tolerance for timing
        Assert.True(result.MemoryBefore > 0);
        Assert.True(result.MemoryAfter >= result.MemoryBefore);
        
        _output.WriteLine($"Execution Time: {result.ElapsedMilliseconds}ms");
        _output.WriteLine($"Memory Before: {result.MemoryBefore:N0} bytes");
        _output.WriteLine($"Memory After: {result.MemoryAfter:N0} bytes");
        _output.WriteLine($"Memory Used: {result.MemoryUsed:N0} bytes");
    }

    [Fact]
    public async Task Performance_Test_Should_Handle_Synchronous_Operations()
    {
        // ARRANGE
        var benchmark = new PerformanceBenchmark();
        
        // ACT
        var (performance, operationResult) = await benchmark.MeasureAsync(() =>
        {
            // Simulate CPU-intensive work
            var sum = 0;
            for (int i = 0; i < 1_000_000; i++)
            {
                sum += i;
            }
            return Task.FromResult(sum);
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(performance.ElapsedMilliseconds > 0);
        Assert.True(performance.ElapsedMilliseconds < 1000); // Should complete quickly
        Assert.True(operationResult > 0); // Should have calculated a sum
        
        _output.WriteLine($"CPU Work Execution Time: {performance.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Performance_Test_Should_Track_Memory_Usage_Changes()
    {
        // ARRANGE
        var benchmark = new PerformanceBenchmark();
        
        // ACT
        var result = await benchmark.MeasureAsync(async () =>
        {
            // Allocate some memory
            var largeArray = new byte[1024 * 1024]; // 1MB
            await Task.Delay(10).ConfigureAwait(false);
            
            // Keep reference to prevent immediate GC
            GC.KeepAlive(largeArray);
        }).ConfigureAwait(false);
        
        // ASSERT
        Assert.True(result.MemoryUsed >= 0); // Memory usage should be tracked
        
        _output.WriteLine($"Memory allocation test - Used: {result.MemoryUsed:N0} bytes");
    }

    [Fact]
    public async Task Performance_Test_Should_Provide_Detailed_Results()
    {
        // ARRANGE
        var benchmark = new PerformanceBenchmark();
        
        // ACT
        var result = await benchmark.MeasureAsync(async () =>
        {
            await Task.Delay(50).ConfigureAwait(false);
        }).ConfigureAwait(false);
        
        // ASSERT - Verify all required properties exist
        Assert.True(result.ElapsedMilliseconds >= 0);
        Assert.True(result.ElapsedTicks >= 0);
        Assert.True(result.MemoryBefore >= 0);
        Assert.True(result.MemoryAfter >= 0);
        Assert.NotEqual(default(DateTime), result.StartTime);
        Assert.NotEqual(default(DateTime), result.EndTime);
        Assert.True(result.EndTime >= result.StartTime);
        
        // Verify computed properties
        Assert.Equal(result.MemoryAfter - result.MemoryBefore, result.MemoryUsed);
        
        // Allow for reasonable timing tolerance (within 5ms)
        var expectedElapsedMs = (result.EndTime - result.StartTime).TotalMilliseconds;
        Assert.True(Math.Abs(expectedElapsedMs - result.ElapsedMilliseconds) <= 5.0, 
            $"Expected ElapsedMilliseconds to be within 5ms of calculated time. Expected: {expectedElapsedMs:F1}ms, Actual: {result.ElapsedMilliseconds:F1}ms");
        
        _output.WriteLine($"Start Time: {result.StartTime:HH:mm:ss.fff}");
        _output.WriteLine($"End Time: {result.EndTime:HH:mm:ss.fff}");
        _output.WriteLine($"Elapsed Ticks: {result.ElapsedTicks:N0}");
    }

    [Fact]
    public async Task Performance_Test_Should_Handle_Exceptions_Gracefully()
    {
        // ARRANGE
        var benchmark = new PerformanceBenchmark();
        
        // ACT & ASSERT
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await benchmark.MeasureAsync(async () =>
            {
                await Task.Delay(10).ConfigureAwait(false);
                throw new InvalidOperationException("Test exception");
            }).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    [Fact]
    public void Performance_Result_Should_Support_Comparison()
    {
        // ARRANGE
        var result1 = new PerformanceResult
        {
            ElapsedMilliseconds = 100,
            MemoryBefore = 1000,
            MemoryAfter = 2024  // MemoryUsed will be 1024
        };
        
        var result2 = new PerformanceResult
        {
            ElapsedMilliseconds = 200,
            MemoryBefore = 1000,
            MemoryAfter = 3048  // MemoryUsed will be 2048
        };
        
        // ACT & ASSERT
        Assert.True(result1.IsFasterThan(result2));
        Assert.False(result2.IsFasterThan(result1));
        Assert.True(result1.UsesLessMemoryThan(result2));
        Assert.False(result2.UsesLessMemoryThan(result1));
        
        var improvement = result1.GetImprovementOver(result2);
        Assert.Equal(50.0, improvement.TimeImprovement, precision: 1); // 50% faster
        Assert.Equal(50.0, improvement.MemoryImprovement, precision: 1); // 50% less memory
    }
} 