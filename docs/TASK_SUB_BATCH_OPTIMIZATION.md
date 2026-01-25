# 🚀 Sub-Batch Optimization Implementation Task Document

## 📋 **PROJECT OVERVIEW**

**Objective**: Enhance parallel processing performance by implementing sub-batch optimization within existing page-based parallel processing.

**Current State**: 4x parallelism (page-level only)  
**Target State**: 20x parallelism (page-level + sub-batch level)  
**Expected Performance**: 5x speed improvement (2min 10sec → ~25 seconds)

---

## 🎯 **SUCCESS CRITERIA**

| **Metric** | **Current** | **Target** | **Validation Method** |
|------------|-------------|------------|----------------------|
| **Duration** | 126 seconds | **< 30 seconds** | End-to-end timing |
| **Parallelism** | 4x (pages) | **20x (pages + sub-batches)** | Concurrency monitoring |
| **Progress Updates** | 4 updates | **40 updates** | SignalR event counting |
| **Success Rate** | 99.48% | **> 99%** | Migration completion stats |
| **Rate Limit Usage** | 0.1-0.2% | **< 2%** | API monitoring |
| **Race Conditions** | 0 | **0** | Clean store testing |

---

## 🏗️ **ARCHITECTURE DESIGN**

### **Current Architecture:**
```
4 Pages × 50 Sequential Entities = 4x Parallelism
Page 1: [E1] → [E2] → ... → [E50]  (Sequential)
Page 2: [E1] → [E2] → ... → [E50]  (Sequential)
Page 3: [E1] → [E2] → ... → [E50]  (Sequential)
Page 4: [E1] → [E2] → ... → [E42]  (Sequential)
```

### **Target Architecture:**
```
4 Pages × 10 Sub-batches × 5 Parallel Entities = 20x Parallelism
Page 1: [Sub-batch 1: 5 parallel] → [Sub-batch 2: 5 parallel] → ... → [Sub-batch 10: 5 parallel]
Page 2: [Sub-batch 1: 5 parallel] → [Sub-batch 2: 5 parallel] → ... → [Sub-batch 10: 5 parallel]
Page 3: [Sub-batch 1: 5 parallel] → [Sub-batch 2: 5 parallel] → ... → [Sub-batch 10: 5 parallel]
Page 4: [Sub-batch 1: 5 parallel] → [Sub-batch 2: 5 parallel] → ... → [Sub-batch 9: 2 parallel]
```

### **Key Design Principles:**
1. **🔄 Sequential Sub-batch Order**: Sub-batches 1→2→3...→10 processed in order
2. **⚡ Parallel Within Sub-batch**: 5 entities processed concurrently within each sub-batch
3. **📊 Granular Progress**: Update progress after each sub-batch completion
4. **🚨 Safe Concurrency**: Use SemaphoreSlim(5) to limit concurrent entity creation
5. **🔄 Deterministic Processing**: Maintain Durable Functions compliance

---

## 📝 **IMPLEMENTATION PHASES**

## 🔧 **PHASE 1: CORE SUB-BATCH ARCHITECTURE**

### **Task 1.1: Create Sub-Batch Data Models**
**Location**: `src/BigCommerce.Migration.Orchestration/Models/`
**Files to Create/Modify**:
- `SubBatchRequest.cs` (new)
- `OrchestrationModels.cs` (modify)

**Implementation Details**:
```csharp
public class SubBatchRequest
{
    public int SubBatchNumber { get; set; }           // 1-10 for each page
    public int ParentBatchNumber { get; set; }        // Original page number (1-4)
    public List<Dictionary<string, object>> Entities { get; set; } = new(); // 5 entities max
    public int MaxConcurrency { get; set; } = 5;      // Parallel entities within sub-batch
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public StoreCredentials SourceStore { get; set; } = new();
    public StoreCredentials DestinationStore { get; set; } = new();
}

public class SubBatchResult
{
    public int SubBatchNumber { get; set; }
    public int ParentBatchNumber { get; set; }
    public int TotalEntities { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan ProcessingTime { get; set; }
    public DateTime CompletedAt { get; set; }
}
```

**Validation**:
- [ ] Models compile without errors
- [ ] All required properties included
- [ ] Proper default values set

---

### **Task 1.2: Implement Sub-Batch Processing Logic**
**Location**: `src/BigCommerce.Migration.Orchestration/Activities/ProcessParallelBatchesActivity.cs`
**Method to Add**: `ProcessSubBatchesInParallel`

**Implementation Details**:
```csharp
private async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessSubBatchesInParallel(
    List<Dictionary<string, object>> entities, 
    BatchProcessingRequest batch, 
    CancellationToken cancellationToken)
{
    var batchId = $"SUBBATCH-{batch.BatchNumber}";
    _logger.LogInformation("🎯 [SUB-BATCH-{BatchId}] ⭐ STARTING: Processing {Count} entities in 10 sub-batches of 5 parallel entities each", 
        batchId, entities.Count);

    // Split entities into sub-batches of 5
    var subBatches = CreateSubBatches(entities, batch);
    var overallResult = InitializeOverallResult(batch, entities.Count);
    
    // Process sub-batches sequentially (to maintain order)
    for (int subBatchIndex = 0; subBatchIndex < subBatches.Count; subBatchIndex++)
    {
        var subBatch = subBatches[subBatchIndex];
        var subBatchResult = await ProcessSingleSubBatch(subBatch, cancellationToken);
        
        // Update overall progress atomically
        Interlocked.Add(ref overallResult.SuccessfulEntities, subBatchResult.SuccessfulEntities);
        Interlocked.Add(ref overallResult.FailedEntities, subBatchResult.FailedEntities);
        
        // Send granular progress update
        await SendSubBatchProgressUpdate(subBatchResult);
        
        _logger.LogInformation("🎯 [SUB-BATCH-{BatchId}] ✅ COMPLETED: Sub-batch {SubBatchNumber}/10 - {Successful}/{Total} successful", 
            batchId, subBatchIndex + 1, subBatchResult.SuccessfulEntities, subBatchResult.TotalEntities);
    }
    
    _logger.LogInformation("🎯 [SUB-BATCH-{BatchId}] 🏁 ALL COMPLETED: {SuccessfulTotal}/{Total} entities processed across {SubBatchCount} sub-batches", 
        batchId, overallResult.SuccessfulEntities, entities.Count, subBatches.Count);
        
    return overallResult;
}
```

**Supporting Methods**:
```csharp
private List<SubBatchRequest> CreateSubBatches(List<Dictionary<string, object>> entities, BatchProcessingRequest batch)
{
    var subBatches = new List<SubBatchRequest>();
    var subBatchSize = 5;
    
    for (int i = 0; i < entities.Count; i += subBatchSize)
    {
        var subBatchEntities = entities.Skip(i).Take(subBatchSize).ToList();
        subBatches.Add(new SubBatchRequest
        {
            SubBatchNumber = (i / subBatchSize) + 1,
            ParentBatchNumber = batch.BatchNumber,
            Entities = subBatchEntities,
            MaxConcurrency = 5,
            MigrationId = batch.MigrationId,
            EntityType = batch.EntityType,
            SourceStore = batch.SourceStore,
            DestinationStore = batch.DestinationStore
        });
    }
    
    return subBatches;
}

private async Task<SubBatchResult> ProcessSingleSubBatch(SubBatchRequest subBatch, CancellationToken cancellationToken)
{
    var startTime = DateTime.UtcNow;
    var result = new SubBatchResult
    {
        SubBatchNumber = subBatch.SubBatchNumber,
        ParentBatchNumber = subBatch.ParentBatchNumber,
        TotalEntities = subBatch.Entities.Count
    };
    
    // Use SemaphoreSlim to limit concurrency to 5
    using var semaphore = new SemaphoreSlim(subBatch.MaxConcurrency, subBatch.MaxConcurrency);
    var tasks = new List<Task<(bool Success, string ErrorMessage)>>();
    
    // Process all entities in the sub-batch in parallel (up to 5 concurrent)
    foreach (var entity in subBatch.Entities)
    {
        tasks.Add(ProcessEntityWithSemaphore(entity, subBatch, semaphore, cancellationToken));
    }
    
    // Wait for all entities in this sub-batch to complete
    var results = await Task.WhenAll(tasks);
    
    // Aggregate results
    result.SuccessfulEntities = results.Count(r => r.Success);
    result.FailedEntities = results.Count(r => !r.Success);
    result.Errors = results.Where(r => !r.Success).Select(r => r.ErrorMessage).ToList();
    result.ProcessingTime = DateTime.UtcNow - startTime;
    result.CompletedAt = DateTime.UtcNow;
    
    return result;
}
```

**Validation**:
- [ ] Method compiles and integrates with existing code
- [ ] Proper semaphore usage for concurrency control
- [ ] Atomic counter updates implemented
- [ ] Comprehensive logging added

---

### **Task 1.3: Integrate Sub-Batch Processing**
**Location**: `src/BigCommerce.Migration.Orchestration/Activities/ProcessParallelBatchesActivity.cs`
**Method to Modify**: `ProcessBatchWithHierarchyAwareness`

**Implementation Details**:
Replace the existing sequential processing logic:
```csharp
// 🚨 OLD: Simple sequential processing
if (useSequentialProcessing)
{
    // Process entities sequentially to avoid race conditions
    for (int index = 0; index < entities.Count; index++)
    {
        // ... existing sequential logic
    }
}

// 🎯 NEW: Sub-batch parallel processing
if (useSequentialProcessing)
{
    _logger.LogInformation("🎯 [SUB-BATCH-{BatchId}] 🚀 SUB-BATCH MODE: Processing {Count} {EntityType} entities using sub-batch optimization", 
        batchId, entities.Count, batch.EntityType);
        
    return await ProcessSubBatchesInParallel(entities, batch, cancellationToken);
}
```

**Validation**:
- [ ] Integration point identified correctly
- [ ] Backwards compatibility maintained
- [ ] Feature flag consideration implemented

---

## 📊 **PHASE 2: ENHANCED PROGRESS TRACKING**

### **Task 2.1: Create Sub-Batch Progress Events**
**Location**: `src/BigCommerce.Migration.Core/Models/Events/`
**Files to Create**: `SubBatchProgressEvent.cs`

**Implementation Details**:
```csharp
public class SubBatchProgressEvent
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int BatchNumber { get; set; }              // Page number (1-4)
    public int SubBatchNumber { get; set; }           // Sub-batch number (1-10)
    public int TotalSubBatches { get; set; }          // Usually 10
    public int ProcessedInSubBatch { get; set; }      // Entities completed in this sub-batch
    public int SuccessfulInSubBatch { get; set; }     // Successful entities in this sub-batch
    public int FailedInSubBatch { get; set; }         // Failed entities in this sub-batch
    public int CumulativeProcessed { get; set; }      // Total processed so far
    public int CumulativeSuccessful { get; set; }     // Total successful so far
    public int CumulativeFailed { get; set; }         // Total failed so far
    public int OverallTotal { get; set; }             // Total entities in migration (192)
    public double ProgressPercentage { get; set; }    // 0-100 percentage
    public TimeSpan EstimatedTimeRemaining { get; set; }
    public DateTime Timestamp { get; set; }
}
```

**Validation**:
- [ ] Event model includes all necessary progress data
- [ ] Proper data types and ranges defined
- [ ] Timestamp and estimation fields included

---

### **Task 2.2: Implement Atomic Progress Updates**
**Location**: `src/BigCommerce.Migration.Infrastructure/Services/ParallelProgressAggregator.cs`
**Methods to Add**: Sub-batch progress tracking

**Implementation Details**:
```csharp
// Add fields for sub-batch tracking
private long _totalSubBatchesCompleted;
private readonly ConcurrentDictionary<string, SubBatchProgress> _subBatchProgress;

public async Task ReportSubBatchProgress(SubBatchProgressEvent progressEvent, CancellationToken cancellationToken = default)
{
    try
    {
        // Update atomic counters
        Interlocked.Add(ref _totalEntitiesProcessed, progressEvent.ProcessedInSubBatch);
        Interlocked.Add(ref _totalEntitiesFailed, progressEvent.FailedInSubBatch);
        Interlocked.Increment(ref _totalSubBatchesCompleted);
        
        // Calculate granular progress (2.5% per sub-batch for 40 total sub-batches)
        var overallProgress = (double)Interlocked.Read(ref _totalSubBatchesCompleted) / (4 * 10) * 100;
        
        // Send enhanced SignalR update
        await SendEnhancedSignalRUpdate(progressEvent, overallProgress, cancellationToken);
        
        _logger.LogInformation("📊 [PROGRESS] Sub-batch {BatchNumber}-{SubBatchNumber} completed: {Progress:F1}% overall progress", 
            progressEvent.BatchNumber, progressEvent.SubBatchNumber, overallProgress);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to report sub-batch progress for migration {MigrationId}", progressEvent.MigrationId);
    }
}
```

**Validation**:
- [ ] Atomic operations used for thread safety
- [ ] Progress calculations accurate
- [ ] Error handling implemented

---

### **Task 2.3: Optimize SignalR Update Frequency**
**Location**: `src/BigCommerce.Migration.Infrastructure/Services/ParallelProgressAggregator.cs`
**Configuration Changes**: Reduce interval and add sub-batch notifications

**Implementation Details**:
```csharp
// Reduce update interval for smoother UI
private int _signalRUpdateIntervalMs = 250; // Reduced from 500ms to 250ms

private async Task SendEnhancedSignalRUpdate(SubBatchProgressEvent subBatchEvent, double overallProgress, CancellationToken cancellationToken)
{
    var enhancedEvent = new MigrationProgressEvent
    {
        MigrationId = subBatchEvent.MigrationId,
        CurrentEntityType = subBatchEvent.EntityType,
        OverallProgress = overallProgress,
        Status = "in_progress",
        TotalEntities = subBatchEvent.OverallTotal,
        ProcessedEntities = subBatchEvent.CumulativeProcessed,
        FailedEntities = subBatchEvent.CumulativeFailed,
        
        // Enhanced sub-batch details
        CurrentBatchNumber = subBatchEvent.BatchNumber,
        CurrentSubBatchNumber = subBatchEvent.SubBatchNumber,
        TotalSubBatches = subBatchEvent.TotalSubBatches,
        EstimatedTimeRemaining = subBatchEvent.EstimatedTimeRemaining,
        
        Timestamp = subBatchEvent.Timestamp
    };
    
    await _progressEventPublisher.PublishMigrationProgressAsync(enhancedEvent, cancellationToken);
}
```

**Validation**:
- [ ] Update frequency increased appropriately
- [ ] Enhanced progress data included
- [ ] Performance impact acceptable

---

## 🧪 **PHASE 3: COMPREHENSIVE TESTING**

### **Task 3.1: Core Sub-Batch Unit Tests**
**Location**: `tests/BigCommerce.Migration.UnitTests/Activities/`
**File to Create**: `ProcessParallelBatchesSubBatchTests.cs`

**Test Cases**:
```csharp
[Fact]
public async Task ProcessSubBatches_With50Entities_ShouldCreate10SubBatchesOf5()
{
    // Arrange
    var entities = CreateMockEntities(50);
    var batch = CreateMockBatch();
    
    // Act
    var result = await _activity.ProcessSubBatchesInParallel(entities, batch, CancellationToken.None);
    
    // Assert
    result.Should().NotBeNull();
    result.TotalProcessed.Should().Be(50);
    // Verify 10 sub-batches were processed
    _mockLogger.Verify(x => x.Log(LogLevel.Information, 
        It.Is<string>(s => s.Contains("Sub-batch") && s.Contains("/10")), 
        It.IsAny<object[]>()), Times.Exactly(10));
}

[Fact]
public async Task ProcessSubBatches_With42Entities_ShouldCreate9SubBatchesCorrectly()
{
    // Test irregular batch size (like page 4 with 42 entities)
    var entities = CreateMockEntities(42);
    var batch = CreateMockBatch();
    
    // Act
    var result = await _activity.ProcessSubBatchesInParallel(entities, batch, CancellationToken.None);
    
    // Assert
    result.TotalProcessed.Should().Be(42);
    // 8 sub-batches of 5 + 1 sub-batch of 2
}

[Fact]
public async Task SubBatchParallelism_ShouldRespectConcurrencyLimit()
{
    // Test that no more than 5 entities are processed concurrently
    var entities = CreateMockEntities(10);
    var batch = CreateMockBatch();
    var concurrentCount = 0;
    var maxConcurrent = 0;
    
    // Mock entity processor to track concurrency
    // ... implementation details
    
    // Assert max concurrency never exceeded 5
    maxConcurrent.Should().BeLessOrEqualTo(5);
}
```

**Validation**:
- [ ] All core functionality tested
- [ ] Edge cases covered (42 entities, etc.)
- [ ] Concurrency limits verified

---

### **Task 3.2: Progress Update Unit Tests**
**Location**: `tests/BigCommerce.Migration.UnitTests/Services/`
**File to Create**: `SubBatchProgressTests.cs`

**Test Cases**:
```csharp
[Fact]
public async Task SubBatchProgress_ShouldUpdateAfterEachCompletion()
{
    // Test that progress updates are sent after each sub-batch
    // Verify granular progress calculations (2.5% increments)
    // Confirm atomic counter updates
}

[Fact]
public async Task ProgressCalculation_For192Entities_ShouldBe2Point5PercentPerSubBatch()
{
    // 192 entities ÷ 4 pages ÷ 10 sub-batches = 4.8 entities per sub-batch average
    // Each sub-batch completion = 2.5% progress
}
```

**Validation**:
- [ ] Progress calculations verified
- [ ] Update frequency tested
- [ ] Atomic operations confirmed

---

### **Task 3.3: Performance Validation Tests**
**Location**: `tests/BigCommerce.Migration.PerformanceTests/`
**File to Create**: `SubBatchPerformanceTests.cs`

**Test Cases**:
```csharp
[Fact]
public async Task SubBatchOptimization_ShouldProvide5xSpeedImprovement()
{
    // Benchmark current vs sub-batch processing
    // Target: 126 seconds → 25 seconds
    // Allow for 20% variance (30 seconds max)
}

[Fact]
public async Task RateLimitUsage_ShouldStayUnder2Percent()
{
    // Monitor rate limit usage during sub-batch processing
    // Ensure it stays well under BigCommerce limits
}
```

**Validation**:
- [ ] Performance benchmarks established
- [ ] Rate limit monitoring implemented
- [ ] Success criteria validated

---

## 🔄 **PHASE 4: INTEGRATION & DEPLOYMENT**

### **Task 4.1: Fresh Environment Setup**
**Commands to Execute**:
```bash
# Clean environment completely
docker-compose down -v
docker system prune -f
docker volume prune -f

# Fresh build and start
docker-compose build --no-cache
docker-compose up -d
```

**Validation**:
- [ ] All volumes and containers removed
- [ ] Fresh build completed successfully
- [ ] Services start without errors

---

### **Task 4.2: End-to-End Integration Testing**
**Test Scenario**: 192 brand migration with sub-batch optimization

**Success Criteria**:
- [ ] **Duration < 30 seconds** (target: ~25 seconds)
- [ ] **40 progress updates** received (4 pages × 10 sub-batches)
- [ ] **Success rate > 99%** (191+ successful out of 192)
- [ ] **Zero race conditions** on clean store
- [ ] **Rate limit usage < 2%** throughout migration

**Monitoring Commands**:
```bash
# Monitor sub-batch processing
docker-compose logs bigcommerce-functions --follow | grep -E "(🎯.*SUB-BATCH|Sub-batch.*completed|📊.*PROGRESS)" --line-buffered

# Monitor performance
docker-compose logs bigcommerce-functions --follow | grep -E "(Enhanced API performance|Rate Limit:|Duration.*ms)" --line-buffered
```

**Validation**:
- [ ] All success criteria met
- [ ] No errors or race conditions
- [ ] Performance targets achieved

---

### **Task 4.3: Rollback Strategy Implementation**
**Location**: `src/BigCommerce.Migration.Orchestration/`
**Feature Flag**: `UseSubBatchOptimization`

**Implementation**:
```csharp
// Add feature flag support
private bool ShouldUseSubBatchOptimization(string entityType)
{
    // Read from configuration
    var useSubBatch = _configuration.GetValue<bool>("Features:UseSubBatchOptimization", false);
    
    // Only enable for brands initially
    return useSubBatch && entityType.Equals("brands", StringComparison.OrdinalIgnoreCase);
}

// In ProcessBatchWithHierarchyAwareness
if (useSequentialProcessing)
{
    if (ShouldUseSubBatchOptimization(batch.EntityType))
    {
        return await ProcessSubBatchesInParallel(entities, batch, cancellationToken);
    }
    else
    {
        // Fall back to original sequential processing
        return await ProcessEntitiesSequentially(entities, batch, cancellationToken);
    }
}
```

**Configuration**:
```json
{
  "Features": {
    "UseSubBatchOptimization": false  // Start disabled, enable after validation
  }
}
```

**Validation**:
- [ ] Feature flag controls sub-batch usage
- [ ] Rollback to original behavior works
- [ ] Configuration changes apply without restart

---

## 📊 **PERFORMANCE MONITORING & METRICS**

### **Key Metrics to Track**:

| **Metric** | **Collection Method** | **Expected Value** | **Alert Threshold** |
|------------|----------------------|-------------------|-------------------|
| **Migration Duration** | Start/end timestamps | < 30 seconds | > 45 seconds |
| **Sub-batch Completion Rate** | Log analysis | 40 completions | < 38 completions |
| **API Rate Limit Usage** | Response headers | < 2% | > 5% |
| **Concurrent Entity Processing** | Semaphore monitoring | ≤ 5 per sub-batch | > 5 |
| **Progress Update Frequency** | SignalR event counting | ~6 updates/second | < 3 updates/second |
| **Memory Usage** | Application monitoring | < 500MB | > 1GB |

### **Success Validation Script**:
```bash
#!/bin/bash
# Automated validation script
echo "🧪 Validating Sub-Batch Optimization..."

# Extract key metrics from logs
DURATION=$(docker-compose logs bigcommerce-functions | grep "Duration.*00:" | tail -1 | grep -o "00:[0-9][0-9]:[0-9][0-9]")
SUB_BATCHES=$(docker-compose logs bigcommerce-functions | grep -c "Sub-batch.*completed")
SUCCESS_RATE=$(docker-compose logs bigcommerce-functions | grep "SuccessfulEntities.*191" | wc -l)

echo "📊 Results:"
echo "  Duration: $DURATION (target: < 00:00:30)"
echo "  Sub-batches completed: $SUB_BATCHES (target: 40)"
echo "  Success rate: $SUCCESS_RATE (target: > 0)"

if [[ $SUB_BATCHES -ge 38 && $SUCCESS_RATE -gt 0 ]]; then
    echo "✅ Sub-batch optimization SUCCESSFUL!"
else
    echo "❌ Sub-batch optimization FAILED - check logs for issues"
fi
```

---

## 🚨 **RISK MITIGATION**

### **High-Risk Areas**:

1. **Race Conditions**:
   - **Risk**: Concurrent entity creation causing 409 conflicts
   - **Mitigation**: Maintain clean store testing, proper semaphore usage
   - **Detection**: Monitor for 409 HTTP errors in logs

2. **Memory Usage**:
   - **Risk**: Increased memory from concurrent processing
   - **Mitigation**: Limit concurrency to 5, proper disposal patterns
   - **Detection**: Application memory monitoring

3. **Rate Limiting**:
   - **Risk**: Exceeding BigCommerce API limits
   - **Mitigation**: Monitor usage, implement backoff if needed
   - **Detection**: Rate limit response headers

4. **Progress Inconsistency**:
   - **Risk**: UI showing incorrect progress percentages
   - **Mitigation**: Atomic counter operations, proper synchronization
   - **Detection**: Progress validation in tests

### **Rollback Triggers**:
- Migration duration > 45 seconds
- Success rate < 95%
- Rate limit usage > 5%
- Memory usage > 1GB
- Any race condition detected

---

## 📅 **ESTIMATED TIMELINE**

| **Phase** | **Tasks** | **Estimated Time** | **Dependencies** |
|-----------|-----------|-------------------|------------------|
| **Phase 1** | Sub-batch architecture | **4-6 hours** | None |
| **Phase 2** | Progress optimization | **2-3 hours** | Phase 1 complete |
| **Phase 3** | Testing & validation | **4-5 hours** | Phases 1-2 complete |
| **Phase 4** | Integration & deployment | **2-3 hours** | All phases complete |
| **Total** | All phases | **12-17 hours** | - |

### **Critical Path**:
1. Phase 1.2 (Core sub-batch logic) → All other tasks depend on this
2. Phase 2.2 (Atomic progress updates) → Required for Phase 3 testing
3. Phase 3.4 (Performance validation) → Required for deployment decision

---

## ✅ **DEFINITION OF DONE**

### **Technical Requirements**:
- [ ] All unit tests pass (>95% code coverage)
- [ ] Integration tests validate performance targets
- [ ] No race conditions on clean store
- [ ] Feature flag implementation allows rollback
- [ ] Comprehensive logging for monitoring

### **Performance Requirements**:
- [ ] Duration < 30 seconds for 192 entities
- [ ] 40 granular progress updates
- [ ] Rate limit usage < 2%
- [ ] Success rate > 99%
- [ ] Zero 409 conflicts

### **Quality Requirements**:
- [ ] Code review completed
- [ ] Documentation updated
- [ ] Monitoring dashboards configured
- [ ] Rollback procedure tested
- [ ] Production deployment plan approved

---

**🎯 Ready to implement? Start with Phase 1.1 and proceed through each task systematically!** 