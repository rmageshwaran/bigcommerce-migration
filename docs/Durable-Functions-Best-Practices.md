# Durable Functions Best Practices for Real-Time Migration Progress

## 🚨 Critical Issues and Solutions

### 1. **Orchestration Replay Problem**

#### **The Issue**
Durable Functions can replay orchestrations multiple times for reliability. This causes:
- **Duplicate SignalR broadcasts** (users see inflated progress)
- **Duplicate progress updates** (incorrect state)
- **Performance issues** from excessive API calls

#### **Root Cause**
SignalR calls in **activity functions** get replayed because:
```csharp
// ❌ BAD: Activity function with SignalR call
[Function("UpdateProgress")]
public async Task UpdateProgressAsync(...)
{
    // This gets replayed on orchestration replay!
    await _signalRService.BroadcastProgressAsync(...);
}
```

#### **✅ Solution: Orchestrator-Level Broadcasting**
```csharp
// ✅ GOOD: Orchestrator controls SignalR calls
[Function("MigrationOrchestrator")]
public async Task<EntityMigrationResult> RunEntityMigrationAsync(...)
{
    // 1. Update internal state (activity - safe to replay)
    await context.CallActivityAsync("UpdateInternalProgress", request);
    
    // 2. Broadcast externally (orchestrator - not replayed)
    context.CallActivityAsync("BroadcastProgress", broadcastRequest);
    //     ☝️ Fire-and-forget to prevent timeouts
}
```

#### **Implementation Pattern**
1. **State Activities**: Update internal progress tracking only
2. **Broadcast Activities**: Handle SignalR calls separately
3. **Orchestrator Coordination**: Controls when broadcasts happen

### 2. **Azure Function Timeout Problem**

#### **The Issue**
Azure Functions have execution limits:
- **Consumption Plan**: 5 minutes default, 10 minutes max
- **Premium Plan**: 30 minutes default, unlimited max
- **SignalR calls add latency** that can push functions over limits

#### **Root Cause**
Long-running activities with synchronous SignalR calls:
```csharp
// ❌ BAD: Synchronous SignalR calls block execution
await _signalRService.BroadcastProgressAsync(...);  // Blocks for network I/O
await _signalRService.BroadcastBatchProgressAsync(...);  // More blocking
await _signalRService.BroadcastDetailedProgressAsync(...);  // Even more blocking
// Total time: Could exceed timeout limits
```

#### **✅ Solution: Fire-and-Forget Pattern**
```csharp
// ✅ GOOD: Non-blocking SignalR calls
public async Task ProcessBatchAsync(...)
{
    // 1. Do critical work first
    var result = await ProcessBatchCriticalWork(...);
    
    // 2. Fire-and-forget broadcasts (don't await)
    context.CallActivityAsync("BroadcastProgress", broadcastData);
    //     ☝️ Returns immediately, doesn't block
    
    return result;
}
```

### 3. **Comprehensive Implementation**

#### **Activity Function Design**
```csharp
// ✅ State-only activities (safe to replay)
[Function("UpdateProgressState")]
public async Task UpdateProgressStateAsync([ActivityTrigger] ProgressUpdate request)
{
    // Only update internal state - no external calls
    await _progressTracker.UpdateProgressAsync(request.MigrationId, request);
    // ☝️ Safe to replay - idempotent state updates
}

// ✅ Broadcast-only activities (fire-and-forget)
[Function("BroadcastProgress")]
public async Task BroadcastProgressAsync([ActivityTrigger] ProgressBroadcast request)
{
    try
    {
        await _signalRService.BroadcastProgressAsync(request.Data);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "SignalR broadcast failed");
        // Don't rethrow - SignalR failures shouldn't stop migration
    }
}
```

#### **Orchestrator Pattern**
```csharp
[Function("EntityMigrationOrchestrator")]
public async Task<EntityMigrationResult> RunEntityMigrationAsync(...)
{
    for (int i = 0; i < batches.Count; i++)
    {
        // 1. Critical work (awaited)
        var batchResult = await context.CallActivityAsync<BatchResult>("ProcessBatch", batch);
        
        // 2. State updates (awaited)
        await context.CallActivityAsync("UpdateProgressState", progressUpdate);
        
        // 3. Broadcasts (fire-and-forget to prevent timeouts)
        context.CallActivityAsync("BroadcastBatchCompletion", broadcastData);
        //     ☝️ Don't await - prevents timeouts and replay issues
    }
}
```

## 🔧 Configuration Recommendations

### **Function App Settings**
```json
{
  "host": {
    "functionTimeout": "00:30:00",  // 30 minutes for Premium plan
    "extensions": {
      "durableTask": {
        "hubName": "BigCommerceMigration",
        "storageProvider": {
          "maxQueuePollingInterval": "00:00:10"
        }
      }
    }
  }
}
```

### **Retry Policies**
```csharp
// Configure retry for critical activities only
var retryOptions = new TaskRetryOptions(
    firstRetryInterval: TimeSpan.FromSeconds(5),
    maxNumberOfAttempts: 3
);

// Apply to critical activities
await context.CallActivityWithRetryAsync("ProcessBatch", retryOptions, batch);

// Don't retry broadcasts - they're not critical
context.CallActivityAsync("BroadcastProgress", broadcast);
```

## 📊 Performance Optimizations

### **Batching Strategy**
```csharp
// Reduce function calls by batching broadcasts
public class BatchedBroadcast
{
    public List<ProgressUpdate> Updates { get; set; }
    public List<BatchCompletion> Completions { get; set; }
}

// Single broadcast activity handles multiple updates
[Function("BroadcastBatch")]
public async Task BroadcastBatchAsync([ActivityTrigger] BatchedBroadcast request)
{
    var tasks = new List<Task>();
    
    foreach (var update in request.Updates)
        tasks.Add(_signalRService.BroadcastProgressAsync(update));
    
    foreach (var completion in request.Completions)
        tasks.Add(_signalRService.BroadcastBatchCompletionAsync(completion));
    
    // Fire all broadcasts concurrently
    await Task.WhenAll(tasks);
}
```

### **Conditional Broadcasting**
```csharp
// Only broadcast on significant progress changes
public async Task ProcessBatchesAsync(...)
{
    var lastBroadcastProgress = 0.0;
    
    for (int i = 0; i < batches.Count; i++)
    {
        var result = await ProcessBatch(batches[i]);
        var currentProgress = (double)i / batches.Count * 100;
        
        // Only broadcast every 5% progress or last batch
        if (currentProgress - lastBroadcastProgress >= 5.0 || i == batches.Count - 1)
        {
            context.CallActivityAsync("BroadcastProgress", progressData);
            lastBroadcastProgress = currentProgress;
        }
    }
}
```

## ✅ Testing Strategy

### **Replay Testing**
```csharp
[Test]
public async Task Orchestrator_WithReplay_DoesNotDuplicateBroadcasts()
{
    // Arrange: Mock to track broadcast calls
    var broadcastCalls = new List<ProgressUpdate>();
    _mockSignalR.Setup(x => x.BroadcastProgressAsync(It.IsAny<ProgressUpdate>()))
              .Callback<ProgressUpdate>(update => broadcastCalls.Add(update));
    
    // Act: Run orchestrator with replay simulation
    await _orchestrator.RunEntityMigrationAsync(request);
    
    // Simulate orchestration replay
    await _orchestrator.RunEntityMigrationAsync(request); // Same request
    
    // Assert: No duplicate broadcasts
    Assert.Equal(expectedBroadcastCount, broadcastCalls.Count);
}
```

### **Timeout Testing**
```csharp
[Test]
public async Task ProcessBatch_WithSlowSignalR_DoesNotTimeout()
{
    // Arrange: Mock slow SignalR calls
    _mockSignalR.Setup(x => x.BroadcastProgressAsync(It.IsAny<ProgressUpdate>()))
              .Returns(Task.Delay(TimeSpan.FromMinutes(2))); // Slow call
    
    // Act & Assert: Should complete within timeout
    var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    await _orchestrator.ProcessBatchAsync(batch, cts.Token);
    // Should not throw timeout exception
}
```

## 🚀 Migration Path

### **Phase 1: Immediate Fixes**
1. ✅ **Move SignalR calls out of activities** → Completed
2. ✅ **Create broadcast-only activities** → Completed  
3. ✅ **Use fire-and-forget pattern** → Completed

### **Phase 2: Optimizations**
1. **Implement conditional broadcasting** (5% progress increments)
2. **Add batched broadcasts** for high-frequency updates
3. **Configure appropriate timeouts** based on hosting plan

### **Phase 3: Monitoring**
1. **Add telemetry** for broadcast success/failure rates
2. **Monitor orchestration replay frequency**
3. **Track function execution times** vs. timeout limits

## 📈 Expected Benefits

- **🔄 Replay Safety**: No duplicate progress updates
- **⚡ Performance**: Reduced execution time and timeout risk  
- **📊 Reliability**: SignalR failures don't stop migrations
- **🔍 Observability**: Better separation of concerns for monitoring

This architecture ensures robust, scalable real-time progress tracking while avoiding common Durable Functions pitfalls. 