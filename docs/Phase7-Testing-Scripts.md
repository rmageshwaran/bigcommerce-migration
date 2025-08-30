# 🧪 Phase 7: Integration Testing Scripts & Tools

**Purpose**: Executable testing scripts for Phase 7 validation  
**Dependencies**: Phase 1-6 complete, test environment ready

---

## 🛠️ **Testing Infrastructure Setup**

### **Test Project Structure**
```
src/BigCommerce.Migration.Tests.Integration.Phase7/
├── EndToEndWorkflowTests.cs          # Task 7.1
├── PerformanceScalabilityTests.cs    # Task 7.2  
├── ErrorHandlingRecoveryTests.cs     # Task 7.3
├── MemoryLeakResourceTests.cs        # Task 7.4
├── TestData/
│   ├── TestDataGenerator.cs
│   ├── LargeDatasetSimulator.cs
│   └── PerformanceMetrics.cs
└── Utilities/
    ├── ResourceMonitor.cs
    ├── PerformanceMeasurement.cs
    └── TestConfiguration.cs
```

---

# 🧪 **Task 7.1: End-to-End Workflow Tests**

## **Test Class: EndToEndWorkflowTests.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.Tests.Integration.Phase7
{
    public class EndToEndWorkflowTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly ILogger<EndToEndWorkflowTests> _logger;

        public EndToEndWorkflowTests(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            _logger = _fixture.ServiceProvider.GetRequiredService<ILogger<EndToEndWorkflowTests>>();
        }

        [Fact]
        [Trait("Category", "Phase7")]
        [Trait("Task", "7.1.1")]
        public async Task SmallDataset_10KProducts_CompletesSuccessfully()
        {
            // Arrange
            const int productCount = 10_000;
            var migrationId = $"test-migration-{Guid.NewGuid():N}";
            
            var initialMemory = GC.GetTotalMemory(true);
            var stopwatch = Stopwatch.StartNew();
            
            _output.WriteLine($"🧪 [TEST-7.1.1] Starting small dataset test: {productCount:N0} products");
            _output.WriteLine($"📊 Initial memory: {initialMemory / (1024 * 1024):F1} MB");

            // Act & Assert
            var result = await ExecuteCompleteWorkflow(migrationId, productCount, expectedChunks: 10);

            stopwatch.Stop();
            var finalMemory = GC.GetTotalMemory(true);
            var memoryGrowth = (finalMemory - initialMemory) / (1024 * 1024);

            // Validate Results
            Assert.True(result.Success, $"Workflow failed: {result.ErrorMessage}");
            Assert.Equal(productCount, result.ProcessedEntities);
            Assert.True(result.RowNumbersUnique, "RowNumbers must be unique");
            Assert.True(result.RowNumbersSequential, "RowNumbers must be sequential");
            Assert.True(memoryGrowth < 50, $"Memory growth {memoryGrowth:F1} MB exceeds 50 MB limit");

            _output.WriteLine($"✅ Small dataset test completed successfully");
            _output.WriteLine($"📊 Processing time: {stopwatch.Elapsed.TotalSeconds:F1} seconds");
            _output.WriteLine($"📊 Memory growth: {memoryGrowth:F1} MB");
            _output.WriteLine($"📊 Throughput: {productCount / stopwatch.Elapsed.TotalSeconds:F0} entities/second");
        }

        [Fact]
        [Trait("Category", "Phase7")]
        [Trait("Task", "7.1.2")]
        public async Task MediumDataset_100KProducts_ScalesCorrectly()
        {
            // Arrange
            const int productCount = 100_000;
            var migrationId = $"test-migration-{Guid.NewGuid():N}";
            
            var initialMemory = GC.GetTotalMemory(true);
            var stopwatch = Stopwatch.StartNew();
            
            _output.WriteLine($"🧪 [TEST-7.1.2] Starting medium dataset test: {productCount:N0} products");

            // Act & Assert
            var result = await ExecuteCompleteWorkflow(migrationId, productCount, expectedChunks: 50);

            stopwatch.Stop();
            var finalMemory = GC.GetTotalMemory(true);
            var memoryGrowth = (finalMemory - initialMemory) / (1024 * 1024);

            // Validate Results
            Assert.True(result.Success, $"Workflow failed: {result.ErrorMessage}");
            Assert.Equal(productCount, result.ProcessedEntities);
            Assert.True(result.ParallelProcessingWorked, "Parallel processing must work correctly");
            Assert.True(memoryGrowth < 100, $"Memory growth {memoryGrowth:F1} MB exceeds 100 MB limit");
            Assert.True(result.AverageRangeQueryTime < TimeSpan.FromSeconds(2), "Range queries must be <2 seconds per 1K entities");

            _output.WriteLine($"✅ Medium dataset test completed successfully");
            _output.WriteLine($"📊 Processing time: {stopwatch.Elapsed.TotalSeconds:F1} seconds");
            _output.WriteLine($"📊 Memory growth: {memoryGrowth:F1} MB");
            _output.WriteLine($"📊 Throughput: {productCount / stopwatch.Elapsed.TotalSeconds:F0} entities/second");
        }

        [Fact]
        [Trait("Category", "Phase7")]
        [Trait("Task", "7.1.3")]
        public async Task LargeDataset_1MProducts_MaintainsPerformance()
        {
            // Arrange
            const int productCount = 1_000_000;
            var migrationId = $"test-migration-{Guid.NewGuid():N}";
            
            _output.WriteLine($"🧪 [TEST-7.1.3] Starting large dataset simulation: {productCount:N0} products");

            // Act & Assert - Using simulation for 1M products
            var result = await ExecuteLargeDatasetSimulation(migrationId, productCount);

            // Validate Results
            Assert.True(result.Success, $"Large dataset simulation failed: {result.ErrorMessage}");
            Assert.True(result.RowNumberServicePerformance > 100, "RowNumber service must maintain >100 req/sec");
            Assert.True(result.MemoryUsageConstant, "Memory usage must remain constant regardless of dataset size");
            Assert.False(result.PerformanceDegradation, "No performance degradation allowed over time");

            _output.WriteLine($"✅ Large dataset simulation completed successfully");
            _output.WriteLine($"📊 RowNumber service performance: {result.RowNumberServicePerformance:F0} req/sec");
        }

        private async Task<WorkflowTestResult> ExecuteCompleteWorkflow(string migrationId, int productCount, int expectedChunks)
        {
            var result = new WorkflowTestResult();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Step 1: Generate test data
                _output.WriteLine($"📝 Generating {productCount:N0} test products...");
                var testProducts = await _fixture.TestDataGenerator.GenerateProductsAsync(productCount);

                // Step 2: Execute Discovery Phase
                _output.WriteLine($"🔍 Executing discovery phase...");
                var discoveryResult = await ExecuteDiscoveryPhase(migrationId, testProducts);
                Assert.Equal(productCount, discoveryResult.TotalCount);

                // Step 3: Execute Fetch Phase with RowNumber pagination
                _output.WriteLine($"📥 Executing fetch phase with RowNumber pagination...");
                var fetchResult = await ExecuteFetchPhase(migrationId, expectedChunks, productCount);
                
                // Validate RowNumber assignment
                result.RowNumbersUnique = ValidateRowNumberUniqueness(fetchResult.RowNumbers);
                result.RowNumbersSequential = ValidateRowNumberSequential(fetchResult.RowNumbers);
                result.ProcessedEntities = fetchResult.ProcessedCount;
                result.ParallelProcessingWorked = fetchResult.ChunksProcessed == expectedChunks;
                result.AverageRangeQueryTime = fetchResult.AverageQueryTime;

                // Step 4: Execute Transform & Create Phases
                _output.WriteLine($"🔄 Executing transform and create phases...");
                await ExecuteTransformAndCreatePhases(migrationId, fetchResult.EntityMappings);

                result.Success = true;
                _output.WriteLine($"✅ Complete workflow executed successfully in {stopwatch.Elapsed.TotalSeconds:F1} seconds");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Workflow execution failed");
                _output.WriteLine($"❌ Workflow failed: {ex.Message}");
            }

            return result;
        }

        private bool ValidateRowNumberUniqueness(List<long> rowNumbers)
        {
            var uniqueCount = rowNumbers.Distinct().Count();
            var totalCount = rowNumbers.Count;
            
            _output.WriteLine($"📊 RowNumber uniqueness: {uniqueCount:N0} unique out of {totalCount:N0} total");
            return uniqueCount == totalCount;
        }

        private bool ValidateRowNumberSequential(List<long> rowNumbers)
        {
            var sortedNumbers = rowNumbers.OrderBy(r => r).ToList();
            var isSequential = true;
            
            for (int i = 1; i < sortedNumbers.Count; i++)
            {
                if (sortedNumbers[i] != sortedNumbers[i-1] + 1)
                {
                    isSequential = false;
                    _output.WriteLine($"❌ Gap found in sequence: {sortedNumbers[i-1]} → {sortedNumbers[i]}");
                    break;
                }
            }

            _output.WriteLine($"📊 RowNumber sequential validation: {(isSequential ? "✅ PASS" : "❌ FAIL")}");
            return isSequential;
        }

        // Additional helper methods...
    }

    public class WorkflowTestResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int ProcessedEntities { get; set; }
        public bool RowNumbersUnique { get; set; }
        public bool RowNumbersSequential { get; set; }
        public bool ParallelProcessingWorked { get; set; }
        public TimeSpan AverageRangeQueryTime { get; set; }
        public double RowNumberServicePerformance { get; set; }
        public bool MemoryUsageConstant { get; set; }
        public bool PerformanceDegradation { get; set; }
    }
}
```

---

# ⚡ **Task 7.2: Performance & Scalability Tests**

## **Test Class: PerformanceScalabilityTests.cs**

```csharp
[Fact]
[Trait("Category", "Phase7")]
[Trait("Task", "7.2.1")]
public async Task ThroughputTest_ExceedsRequirement_5000EntitiesPerSecond()
{
    // Arrange
    const int targetThroughput = 5000; // entities/second
    const int testDuration = 60; // seconds
    const int totalEntities = targetThroughput * testDuration;
    
    var migrationId = $"perf-test-{Guid.NewGuid():N}";
    var stopwatch = Stopwatch.StartNew();
    var entitiesProcessed = 0;

    _output.WriteLine($"⚡ [PERF-7.2.1] Starting throughput test: {targetThroughput:N0} entities/sec target");

    // Act
    await RunThroughputTest(migrationId, totalEntities, (processed) => {
        entitiesProcessed = processed;
        var currentThroughput = processed / stopwatch.Elapsed.TotalSeconds;
        
        if (processed % 10000 == 0) // Log every 10K entities
        {
            _output.WriteLine($"📊 Processed: {processed:N0}, Current throughput: {currentThroughput:F0}/sec");
        }
    });

    stopwatch.Stop();

    // Assert
    var actualThroughput = entitiesProcessed / stopwatch.Elapsed.TotalSeconds;
    
    Assert.True(actualThroughput >= targetThroughput, 
        $"Throughput {actualThroughput:F0}/sec is below target {targetThroughput}/sec");

    _output.WriteLine($"✅ Throughput test passed: {actualThroughput:F0} entities/sec");
}

[Fact]
[Trait("Category", "Phase7")]
[Trait("Task", "7.2.2")]
public async Task LatencyTest_BatchProcessing_Under2Seconds()
{
    // Arrange
    const int batchSize = 1000;
    const int numberOfBatches = 50;
    var migrationId = $"latency-test-{Guid.NewGuid():N}";

    _output.WriteLine($"⏱️ [LATENCY-7.2.2] Testing batch latency: {batchSize} entities per batch");

    var latencyMeasurements = new List<TimeSpan>();

    // Act
    for (int batch = 0; batch < numberOfBatches; batch++)
    {
        var batchStopwatch = Stopwatch.StartNew();
        
        await ProcessBatchWithRowNumberPagination(migrationId, batch, batchSize);
        
        batchStopwatch.Stop();
        latencyMeasurements.Add(batchStopwatch.Elapsed);

        if (batch % 10 == 0)
        {
            _output.WriteLine($"📊 Batch {batch}: {batchStopwatch.Elapsed.TotalSeconds:F2} seconds");
        }
    }

    // Assert
    var averageLatency = TimeSpan.FromMilliseconds(latencyMeasurements.Average(t => t.TotalMilliseconds));
    var maxLatency = latencyMeasurements.Max();
    var p95Latency = latencyMeasurements.OrderBy(t => t).ElementAt((int)(numberOfBatches * 0.95));

    Assert.True(averageLatency < TimeSpan.FromSeconds(2), 
        $"Average latency {averageLatency.TotalSeconds:F2}s exceeds 2s limit");

    _output.WriteLine($"✅ Latency test results:");
    _output.WriteLine($"   Average: {averageLatency.TotalSeconds:F2}s");
    _output.WriteLine($"   P95: {p95Latency.TotalSeconds:F2}s");
    _output.WriteLine($"   Max: {maxLatency.TotalSeconds:F2}s");
}

[Fact]
[Trait("Category", "Phase7")]  
[Trait("Task", "7.2.3")]
public async Task MemoryUsageTest_RemainsConstant_Under200MB()
{
    // Arrange
    const int numberOfBatches = 100;
    const int batchSize = 1000;
    var migrationId = $"memory-test-{Guid.NewGuid():N}";

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var initialMemory = GC.GetTotalMemory(false);
    var memoryReadings = new List<long>();

    _output.WriteLine($"🧠 [MEMORY-7.2.3] Memory usage test: {numberOfBatches} batches × {batchSize} entities");
    _output.WriteLine($"📊 Initial memory: {initialMemory / (1024 * 1024):F1} MB");

    // Act
    for (int batch = 0; batch < numberOfBatches; batch++)
    {
        await ProcessBatchWithRowNumberPagination(migrationId, batch, batchSize);

        if (batch % 10 == 0)
        {
            GC.Collect();
            var currentMemory = GC.GetTotalMemory(false);
            memoryReadings.Add(currentMemory);
            
            var memoryUsage = (currentMemory - initialMemory) / (1024 * 1024);
            _output.WriteLine($"📊 Batch {batch}: Memory usage +{memoryUsage:F1} MB");
        }
    }

    // Assert
    var finalMemory = GC.GetTotalMemory(true);
    var totalMemoryGrowth = (finalMemory - initialMemory) / (1024 * 1024);

    Assert.True(totalMemoryGrowth < 200, 
        $"Memory growth {totalMemoryGrowth:F1} MB exceeds 200 MB limit");

    _output.WriteLine($"✅ Memory test passed: Total growth {totalMemoryGrowth:F1} MB");
}
```

---

# 🛡️ **Task 7.3: Error Handling & Recovery Tests**

## **Test Class: ErrorHandlingRecoveryTests.cs**

```csharp
[Fact]
[Trait("Category", "Phase7")]
[Trait("Task", "7.3.1")]
public async Task CounterServiceFailure_RecoveryTest_GracefulDegradation()
{
    // Arrange
    var migrationId = $"error-test-{Guid.NewGuid():N}";
    var mockFailureService = _fixture.CreateFailingRowNumberService();

    _output.WriteLine($"🛡️ [ERROR-7.3.1] Testing counter service failure recovery");

    // Act & Assert - Test various failure scenarios
    await TestNetworkTimeoutRecovery(mockFailureService, migrationId);
    await TestServiceUnavailableRecovery(mockFailureService, migrationId);
    await TestThrottlingRecovery(mockFailureService, migrationId);
    await TestETagConflictRecovery(mockFailureService, migrationId);

    _output.WriteLine($"✅ Counter service failure recovery tests passed");
}

private async Task TestNetworkTimeoutRecovery(IFailingRowNumberService service, string migrationId)
{
    // Simulate network timeout
    service.SimulateNetworkTimeout(TimeSpan.FromSeconds(5));

    var stopwatch = Stopwatch.StartNew();
    long rowNumber;
    
    try
    {
        rowNumber = await service.GetNextRowNumberAsync(migrationId, "products");
        Assert.True(false, "Expected timeout exception");
    }
    catch (TimeoutException)
    {
        _output.WriteLine($"📊 Network timeout handled correctly after {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    // Test recovery after timeout
    service.RestoreService();
    rowNumber = await service.GetNextRowNumberAsync(migrationId, "products");
    Assert.True(rowNumber > 0, "Service should recover and return valid RowNumber");
    
    _output.WriteLine($"✅ Network timeout recovery successful");
}

[Fact]
[Trait("Category", "Phase7")]
[Trait("Task", "7.3.3")]
public async Task ConcurrentAccess_ETagConflicts_MaintainUniqueness()
{
    // Arrange
    const int numberOfThreads = 50;
    const int requestsPerThread = 100;
    var migrationId = $"concurrency-test-{Guid.NewGuid():N}";

    _output.WriteLine($"🔄 [CONCURRENCY-7.3.3] Testing concurrent access: {numberOfThreads} threads × {requestsPerThread} requests");

    var allRowNumbers = new ConcurrentBag<long>();
    var tasks = new List<Task>();
    var conflictCount = 0;

    // Act - Execute concurrent requests
    for (int thread = 0; thread < numberOfThreads; thread++)
    {
        int threadId = thread;
        tasks.Add(Task.Run(async () =>
        {
            for (int request = 0; request < requestsPerThread; request++)
            {
                try
                {
                    var rowNumber = await _fixture.RowNumberService.GetNextRowNumberAsync(migrationId, "products");
                    allRowNumbers.Add(rowNumber);
                }
                catch (RequestFailedException ex) when (ex.Status == 412)
                {
                    Interlocked.Increment(ref conflictCount);
                    // Retry logic should handle this automatically
                    request--; // Retry this request
                }
            }
        }));
    }

    await Task.WhenAll(tasks);

    // Assert
    var rowNumberList = allRowNumbers.ToList();
    var uniqueCount = rowNumberList.Distinct().Count();
    var totalExpected = numberOfThreads * requestsPerThread;

    Assert.Equal(totalExpected, rowNumberList.Count);
    Assert.Equal(totalExpected, uniqueCount);

    _output.WriteLine($"✅ Concurrency test passed:");
    _output.WriteLine($"   Total requests: {rowNumberList.Count:N0}");
    _output.WriteLine($"   Unique RowNumbers: {uniqueCount:N0}");
    _output.WriteLine($"   ETag conflicts: {conflictCount:N0}");
}
```

---

# 📊 **Task 7.4: Memory Leak & Resource Tests**

## **Test Class: MemoryLeakResourceTests.cs**

```csharp
[Fact]
[Trait("Category", "Phase7")]
[Trait("Task", "7.4.1")]
public async Task LongRunning_100Batches_NoMemoryLeaks()
{
    // Arrange
    const int totalBatches = 100;
    const int batchSize = 1000;
    var migrationId = $"longrun-test-{Guid.NewGuid():N}";

    _output.WriteLine($"🔄 [LONGRUN-7.4.1] Long running test: {totalBatches} batches × {batchSize} entities");

    GC.Collect();
    var initialMemory = GC.GetTotalMemory(true);
    var memoryReadings = new List<(int Batch, long Memory, TimeSpan Elapsed)>();
    var overallStopwatch = Stopwatch.StartNew();

    // Act
    for (int batch = 0; batch < totalBatches; batch++)
    {
        await ProcessBatchWithRowNumberPagination(migrationId, batch, batchSize);

        // Monitor every 10 batches
        if (batch % 10 == 0)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var currentMemory = GC.GetTotalMemory(false);
            memoryReadings.Add((batch, currentMemory, overallStopwatch.Elapsed));

            var memoryGrowth = (currentMemory - initialMemory) / (1024 * 1024);
            _output.WriteLine($"📊 Batch {batch}: +{memoryGrowth:F1} MB, {overallStopwatch.Elapsed.TotalSeconds:F1}s elapsed");

            // Assert no significant memory growth during execution
            Assert.True(memoryGrowth < 10, $"Memory growth {memoryGrowth:F1} MB exceeds 10 MB limit at batch {batch}");
        }
    }

    // Final validation
    var finalMemory = GC.GetTotalMemory(true);
    var totalGrowth = (finalMemory - initialMemory) / (1024 * 1024);

    Assert.True(totalGrowth < 10, $"Total memory growth {totalGrowth:F1} MB exceeds 10 MB limit");

    _output.WriteLine($"✅ Long running test completed successfully");
    _output.WriteLine($"📊 Total memory growth: {totalGrowth:F1} MB");
    _output.WriteLine($"📊 Total processing time: {overallStopwatch.Elapsed.TotalMinutes:F1} minutes");
}

[Fact]
[Trait("Category", "Phase7")]
[Trait("Task", "7.4.3")]
public async Task ResourceMonitoring_ConnectionsAndThreads_StableUsage()
{
    // Arrange
    var initialThreadCount = Process.GetCurrentProcess().Threads.Count;
    var initialHandleCount = GetHandleCount();

    _output.WriteLine($"🔍 [RESOURCE-7.4.3] Monitoring system resources");
    _output.WriteLine($"📊 Initial threads: {initialThreadCount}");
    _output.WriteLine($"📊 Initial handles: {initialHandleCount}");

    // Act - Process multiple batches while monitoring resources
    const int batchesToProcess = 50;
    for (int batch = 0; batch < batchesToProcess; batch++)
    {
        await ProcessBatchWithRowNumberPagination($"resource-test-{batch}", batch, 500);

        if (batch % 10 == 0)
        {
            var currentThreadCount = Process.GetCurrentProcess().Threads.Count;
            var currentHandleCount = GetHandleCount();
            
            _output.WriteLine($"📊 Batch {batch}: Threads={currentThreadCount}, Handles={currentHandleCount}");
        }
    }

    // Assert - Resource usage should remain stable
    var finalThreadCount = Process.GetCurrentProcess().Threads.Count;
    var finalHandleCount = GetHandleCount();

    var threadGrowth = finalThreadCount - initialThreadCount;
    var handleGrowth = finalHandleCount - initialHandleCount;

    Assert.True(threadGrowth < 10, $"Thread count grew by {threadGrowth} (too much)");
    Assert.True(handleGrowth < 100, $"Handle count grew by {handleGrowth} (too much)");

    _output.WriteLine($"✅ Resource monitoring test passed");
    _output.WriteLine($"📊 Thread growth: {threadGrowth}");
    _output.WriteLine($"📊 Handle growth: {handleGrowth}");
}

private int GetHandleCount()
{
    return Process.GetCurrentProcess().HandleCount;
}
```

---

# 🛠️ **Test Execution Commands**

## **Run All Phase 7 Tests**
```bash
# Run complete Phase 7 test suite
dotnet test --filter "Category=Phase7" --logger "console;verbosity=detailed"

# Run specific task tests
dotnet test --filter "Task=7.1.1" --logger "console;verbosity=detailed"  # Small dataset
dotnet test --filter "Task=7.2.1" --logger "console;verbosity=detailed"  # Throughput
dotnet test --filter "Task=7.3.1" --logger "console;verbosity=detailed"  # Error handling
dotnet test --filter "Task=7.4.1" --logger "console;verbosity=detailed"  # Memory leaks

# Generate test report
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults/Phase7/
```

## **Performance Profiling Commands**
```bash
# Memory profiling with dotMemory (if available)
dotnet test --filter "Task=7.4.1" -- --collect:"dotMemoryUnit"

# CPU profiling  
dotnet test --filter "Task=7.2" --collect:"XPlat Code Coverage"

# Continuous monitoring
watch -n 5 'dotnet test --filter "Category=Phase7" --logger "console;verbosity=minimal"'
```

---

**Next Steps**: 
1. Set up test infrastructure  
2. Execute tests in order: 7.1 → 7.2 → 7.3 → 7.4
3. Monitor results and address any issues
4. Generate comprehensive test report for Phase 7 completion

This comprehensive testing suite will validate that our RowNumber pagination system meets all production requirements.
