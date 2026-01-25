# 🧪 Incremental Progress Integration Tests

This document provides comprehensive guidance for running and understanding the incremental progress integration tests that validate the complete end-to-end functionality of the real-time progress tracking system.

---

## 🎯 **Test Overview**

The incremental progress integration tests validate the complete flow from `ProcessEntityChunkActivity` through to Azure Table Storage persistence, ensuring that:

- ✅ **Real-time progress tracking** works correctly
- ✅ **Data persistence** survives migration cancellations  
- ✅ **Performance requirements** are met under load
- ✅ **Error handling** maintains system stability
- ✅ **Concurrency scenarios** work correctly

---

## 📋 **Test Structure**

### **Test Categories**

| **Category** | **Purpose** | **Files** | **Duration** |
|--------------|-------------|-----------|--------------|
| **Unit Tests** | Component isolation testing | `IncrementalProgress/*.cs` | ~30 seconds |
| **Integration Tests** | End-to-end system validation | `Integration/IncrementalProgress*.cs` | ~2-5 minutes |
| **Performance Tests** | Load and throughput validation | `Performance/IncrementalProgress*.cs` | ~5-10 minutes |
| **Azure Storage Tests** | Real storage integration | `Integration/IncrementalProgressAzureStorageTests.cs` | ~2-3 minutes |

### **Key Test Files**

```
tests/BigCommerce.Migration.Tests/
├── Integration/
│   ├── IncrementalProgressEndToEndTests.cs          # Complete system integration
│   └── IncrementalProgressAzureStorageTests.cs      # Azure Storage specific tests
├── Performance/
│   └── IncrementalProgressPerformanceTests.cs       # Load and performance tests
├── IncrementalProgress/
│   ├── ProgressTrackerIncrementalTests.cs           # Unit tests (existing)
│   ├── ChunkIncrementEventTests.cs                  # Unit tests (existing)
│   └── IncrementEventsServiceTests.cs               # Unit tests (existing)
└── run-incremental-progress-tests.ps1               # Test runner script
```

---

## 🚀 **Quick Start**

### **Prerequisites**

1. **Azurite Storage Emulator** (for integration tests)
   ```bash
   npm install -g azurite
   azurite --silent --location ./azurite --debug ./azurite/debug.log
   ```

2. **.NET 8 SDK** installed

3. **Test project built**
   ```bash
   cd tests/BigCommerce.Migration.Tests
   dotnet build
   ```

### **Run Tests**

#### **🚀 Quick Validation (Recommended for development)**
```powershell
./run-incremental-progress-tests.ps1 quick
```

#### **🔧 Complete Integration Tests**
```powershell
./run-incremental-progress-tests.ps1 integration
```

#### **⚡ Performance Tests**
```powershell
./run-incremental-progress-tests.ps1 performance
```

#### **🎯 All Tests**
```powershell
./run-incremental-progress-tests.ps1 all
```

---

## 📊 **Test Scenarios**

### **1. End-to-End Integration Tests** 

#### **`IncrementalProgressEndToEndTests`**

**Purpose**: Validates the complete flow from `ProcessEntityChunkActivity` to database persistence.

**Key Test Cases**:

| **Test Method** | **Scenario** | **Validation** |
|-----------------|--------------|----------------|
| `ProcessEntityChunk_ShouldRecordIncrementalProgress_EndToEnd` | Single chunk processing | Progress recorded in database |
| `MultipleChunks_ShouldRecordAllIncrementalProgress_EndToEnd` | Multiple chunks | All chunks tracked correctly |
| `RealTimeProgressAggregation_ShouldWorkCorrectly_EndToEnd` | Progress aggregation | Real-time totals accurate |
| `ErrorInIncrementalProgress_ShouldNotBreakMigration_EndToEnd` | Error handling | Migration continues despite increment failures |
| `CancellationScenario_ShouldPreserveCompletedProgress_EndToEnd` | Cancellation simulation | Completed progress preserved |

**Example Test Flow**:
```csharp
// 1. Create ProcessEntityChunkActivity with real dependencies
var chunkActivity = serviceProvider.GetRequiredService<ProcessEntityChunkActivity>();

// 2. Process chunk (simulates real migration)
var result = await chunkActivity.ProcessEntityChunkAsync(request);

// 3. Verify incremental progress was recorded
var incrementEvents = await incrementEventsService.GetChunkIncrementsAsync(migrationId);
incrementEvents.Should().NotBeEmpty();
```

### **2. Azure Storage Integration Tests**

#### **`IncrementalProgressAzureStorageTests`**

**Purpose**: Tests real Azure Table Storage operations with Azurite emulator.

**Key Test Cases**:

| **Test Method** | **Scenario** | **Validation** |
|-----------------|--------------|----------------|
| `WriteChunkIncrementAsync_ShouldPersistToAzureStorage` | Single write operation | Data persisted correctly |
| `WriteBatchChunkIncrementsAsync_ShouldHandleMultipleEvents` | Batch operations | Multiple events written |
| `GetAggregatedProgressAsync_ShouldCalculateCorrectTotals` | Aggregation queries | Totals calculated accurately |
| `ConcurrentWrites_ShouldHandleCorrectly` | Concurrent access | No data corruption |
| `InvalidEvent_ShouldBeHandledGracefully` | Error scenarios | Graceful error handling |

### **3. Performance Tests**

#### **`IncrementalProgressPerformanceTests`**

**Purpose**: Validates system performance under realistic load conditions.

**Key Test Cases**:

| **Test Method** | **Scenario** | **Performance Target** |
|-----------------|--------------|------------------------|
| `HighThroughputChunkWrites_ShouldMaintainPerformance` | 100 chunks sequential | <500ms per chunk |
| `ConcurrentMultiMigrationWrites_ShouldHandleLoad` | 5 migrations, 20 chunks each | >10 ops/sec |
| `LargeScaleAggregation_ShouldPerformWithinLimits` | 200 chunks aggregation | <5 seconds |
| `BatchWritePerformance_ShouldOutperformIndividualWrites` | Batch vs individual | 1.5x improvement |
| `MemoryUsage_ShouldRemainStable_UnderLoad` | Memory leak detection | <50MB increase |

---

## 🔧 **Test Configuration**

### **Environment Configuration**

Tests use the following configuration:

```csharp
var configData = new Dictionary<string, string>
{
    ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true", // Azurite
    ["BigCommerce:DefaultApiVersion"] = "v3",
    ["BigCommerce:DefaultTimeout"] = "30",
    ["BigCommerce:MaxRetries"] = "3"
};
```

### **Mock Services**

Integration tests use real services where critical and mocks for external dependencies:

**Real Services**:
- ✅ `IncrementEventsService` (real Azure Storage)
- ✅ `ProgressTracker` (real implementation)
- ✅ `ProcessEntityChunkActivity` (real implementation)

**Mocked Services**:
- 🔸 `IEntityFetchService` (returns test data)
- 🔸 `IEntityCreateService` (simulates API calls)
- 🔸 `ISignalREventFactory` (avoids real SignalR)

---

## 📈 **Performance Benchmarks**

### **Expected Performance Metrics**

| **Operation** | **Target** | **Measurement** |
|---------------|------------|-----------------|
| Single chunk increment | <500ms | Time to write increment event |
| Batch write (50 chunks) | 1.5x faster than individual | Batch vs sequential writes |
| Aggregation (200 chunks) | <5 seconds | Query time for progress totals |
| Concurrent operations | >10 ops/sec | Throughput under load |
| Memory usage | <50MB increase | Memory stability over time |

### **Performance Test Results Example**

```
High throughput test completed: 45.2s total, 452.0ms per chunk
Concurrent multi-migration test: 100 operations in 8.7s = 11.5 ops/sec
Large scale aggregation: 200 chunks across 4 entity types, aggregated in 2,340ms
Write performance comparison: Individual=15,234ms, Batch=8,567ms, Improvement=1.8x
```

---

## 🐛 **Troubleshooting**

### **Common Issues**

#### **1. Azurite Not Running**
```
❌ Error: Azure Storage connection failed
```
**Solution**: Start Azurite storage emulator
```bash
azurite --silent --location ./azurite
```

#### **2. Build Errors**
```
❌ Build failed. Please fix build errors before running tests.
```
**Solution**: Build the solution first
```bash
dotnet build --configuration Release
```

#### **3. Test Data Cleanup Issues**
```
⚠️ Failed to cleanup test data
```
**Solution**: Tests automatically generate unique IDs and cleanup. If issues persist, restart Azurite.

#### **4. Performance Test Failures**
```
❌ Performance test failed: operation took 1200ms, expected <500ms
```
**Solution**: Performance tests depend on system resources. Run on a dedicated test environment for accurate results.

### **Debug Mode**

For detailed debugging, run tests with verbose output:

```powershell
./run-incremental-progress-tests.ps1 integration -Verbose
```

Or run specific tests in Visual Studio/Rider with breakpoints.

---

## 📊 **Test Data**

### **Test Migration Structure**

Each test creates realistic migration scenarios:

```csharp
// Example test migration
Migration ID: "test-migration-a1b2c3d4..."
Entity Types: ["products", "categories", "brands", "variants"]
Chunks per Entity: 10-200 (depending on test)
Entities per Chunk: 100-250
Source Store: "source-store-123"  
Destination Store: "dest-store-456"
```

### **Test Data Cleanup**

- ✅ **Automatic**: Tests automatically cleanup their data using unique migration IDs
- ✅ **Isolated**: Each test uses unique identifiers to avoid conflicts
- ✅ **Robust**: Cleanup happens in `Dispose()` methods even if tests fail

---

## 🚀 **CI/CD Integration**

### **Azure DevOps Pipeline Example**

```yaml
- task: PowerShell@2
  displayName: 'Run Incremental Progress Integration Tests'
  inputs:
    targetType: 'filePath'
    filePath: 'tests/BigCommerce.Migration.Tests/run-incremental-progress-tests.ps1'
    arguments: 'integration'
    workingDirectory: 'tests/BigCommerce.Migration.Tests'
```

### **GitHub Actions Example**

```yaml
- name: Run Incremental Progress Tests
  run: |
    cd tests/BigCommerce.Migration.Tests
    ./run-incremental-progress-tests.ps1 all
  shell: pwsh
```

---

## 🎯 **Success Criteria**

### **Integration Test Success Criteria**

✅ **All tests pass** with green status  
✅ **Performance targets met** (see benchmarks above)  
✅ **No memory leaks** detected  
✅ **Error scenarios handled** gracefully  
✅ **Data integrity maintained** under all conditions  
✅ **Concurrent access** works correctly  
✅ **Real Azure Storage** operations successful  

### **Production Readiness Checklist**

- [ ] All integration tests pass consistently
- [ ] Performance tests meet targets on production-like hardware
- [ ] Error handling tests demonstrate graceful degradation
- [ ] Concurrency tests show no data corruption
- [ ] Memory usage remains stable under load
- [ ] Azure Storage operations work with real Azure (not just Azurite)

---

## 📚 **Additional Resources**

### **Related Documentation**
- [Incremental Progress Implementation Plan](./docs/INCREMENTAL-PROGRESS-IMPLEMENTATION-PLAN.md)
- [Task Tracker](./docs/TASK-TRACKER.md)
- [Quick Start Guide](./docs/INCREMENTAL-PROGRESS-QUICK-START.md)

### **Azure Storage Resources**
- [Azure Table Storage Documentation](https://docs.microsoft.com/en-us/azure/storage/tables/)
- [Azurite Storage Emulator](https://github.com/Azure/Azurite)

### **Testing Resources**
- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions Documentation](https://fluentassertions.com/)

---

*Last Updated: 2025-01-17*  
*Document Version: 1.0*  
*Next Review: After each major system change*