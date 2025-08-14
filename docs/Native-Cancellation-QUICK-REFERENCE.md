# 🚀 Native Cancellation Quick Reference Guide
## *Essential Patterns and Code Snippets for Implementation*

### 📋 **Purpose**
This document provides copy-paste ready code patterns, essential commands, and quick references to continue implementation where you left off. Follow the AI Assistant Workflow Guide patterns and maintain architectural consistency.

---

## 🎯 **CORE IMPLEMENTATION PATTERN**

### **One Migration = One Orchestrator Instance**
```csharp
// RULE: Always use migrationId as instanceId
var migrationId = Guid.NewGuid().ToString();
await client.ScheduleNewOrchestrationInstanceAsync(
    "MigrationDurableOrchestrator",
    migrationRequest,
    new StartOrchestrationOptions { InstanceId = migrationId });
```

### **External Event Cancellation Pattern**
```csharp
// ORCHESTRATOR: Listen for cancel event
var cancelEvent = context.WaitForExternalEvent("Cancel");

// HTTP ENDPOINT: Send cancel event
await client.RaiseEventAsync(migrationId, "Cancel", true);

// WAVE PROCESSING: Race completion vs cancellation
var winner = await Task.WhenAny(workWave, cancelEvent);
if (winner == cancelEvent)
{
    await context.CallActivityAsync("SetCancellationFlag", migrationId);
    return CreateCancelledResult();
}
```

### **Cooperative Activity Pattern**
```csharp
// ACTIVITY: Check cancellation flag
if (await CancellationStore.IsCancelledAsync(migrationId))
{
    _logger.LogInformation("Migration {MigrationId} cancelled during processing", migrationId);
    return CreateCancelledResult();
}

// LOOP: Check every N iterations
for (int i = 0; i < items.Count; i++)
{
    if (i % 50 == 0 && await CancellationStore.IsCancelledAsync(migrationId))
        break;
    // Process item
}
```

---

## 🗂️ **ESSENTIAL CODE TEMPLATES**

### **CancellationStore Utility Class**
```csharp
// File: src/BigCommerce.Migration.Infrastructure/Services/CancellationStore.cs
public static class CancellationStore
{
    private static readonly string ConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;
    
    public static async Task SetCancelledAsync(string migrationId)
    {
        var blobClient = new BlobClient(ConnectionString, "migration-cancellation", $"{migrationId}.flag");
        await blobClient.UploadAsync(new BinaryData("cancelled"), overwrite: true);
    }
    
    public static async Task<bool> IsCancelledAsync(string migrationId)
    {
        try
        {
            var blobClient = new BlobClient(ConnectionString, "migration-cancellation", $"{migrationId}.flag");
            var response = await blobClient.ExistsAsync();
            return response.Value;
        }
        catch
        {
            return false; // Default to not cancelled on errors
        }
    }
    
    public static async Task ClearCancelledAsync(string migrationId)
    {
        try
        {
            var blobClient = new BlobClient(ConnectionString, "migration-cancellation", $"{migrationId}.flag");
            await blobClient.DeleteIfExistsAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
```

### **Cancellation Activity Functions**
```csharp
// File: src/BigCommerce.Migration.Orchestration/Activities/CancellationActivities.cs
[Function("SetCancellationFlag")]
public static async Task SetCancellationFlag([ActivityTrigger] string migrationId)
{
    await CancellationStore.SetCancelledAsync(migrationId);
}

[Function("CheckCancellationFlag")]
public static async Task<bool> CheckCancellationFlag([ActivityTrigger] string migrationId)
{
    return await CancellationStore.IsCancelledAsync(migrationId);
}
```

### **Enhanced HTTP Cancellation Endpoint**
```csharp
// File: src/BigCommerce.Migration.Functions/Functions/MigrationHttpFunctions.cs
[Function("CancelMigration")]
public async Task<HttpResponseData> CancelMigrationAsync(
    [HttpTrigger(AuthorizationLevel.Function, "post", Route = "migrations/{migrationId}/cancel")] 
    HttpRequestData req,
    string migrationId,
    [DurableClient] DurableTaskClient client)
{
    try
    {
        // 1. Set cooperative cancellation flag
        await CancellationStore.SetCancelledAsync(migrationId);
        
        // 2. Send external event for graceful orchestrator stop
        await client.RaiseEventAsync(migrationId, "Cancel", true);
        
        // 3. Update migration status in storage
        var migrationEntry = await _migrationStorageService.GetMigrationAsync(migrationId);
        if (migrationEntry != null)
        {
            migrationEntry.Status = MigrationStatus.Cancelled;
            migrationEntry.UpdatedAt = DateTime.UtcNow;
            await _migrationStorageService.UpdateMigrationAsync(migrationEntry);
        }
        
        // 4. Send SignalR notification
        await _signalRPublisher.SendToGroupAsync($"migration-{migrationId}", "CancellationRequested", new
        {
            migrationId,
            cancelledAt = DateTime.UtcNow,
            reason = "User requested cancellation"
        });
        
        return CreateSuccessResponse(req, new { 
            migrationId, 
            cancelled = true, 
            message = "Cancellation request processed" 
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to cancel migration {MigrationId}", migrationId);
        return CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
            "Failed to process cancellation request", migrationId);
    }
}
```

### **Wave-Based Orchestrator Pattern**
```csharp
// File: src/BigCommerce.Migration.Functions/Orchestrators/MigrationDurableOrchestrator.cs
[Function("MigrationDurableOrchestrator")]
public static async Task<MigrationOrchestrationResult> RunMigrationOrchestrator(
    [OrchestrationTrigger] TaskOrchestrationContext context)
{
    var input = context.GetInput<MigrationOrchestrationRequest>();
    var migrationId = context.InstanceId; // Use instanceId as migrationId
    
    // Listen for external cancel event
    var cancelEvent = context.WaitForExternalEvent("Cancel");
    
    try
    {
        // Existing initialization steps (unchanged)
        var initializeResult = await context.CallActivityAsync<InitializeMigrationResult>("InitializeMigration", ...);
        var validateResult = await context.CallActivityAsync<ValidateStoresResult>("ValidateMigrationStores", ...);
        var entitySequence = await context.CallActivityAsync<List<string>>("ResolveEntityDependencies", ...);
        
        // Process entities in small waves for quick cancellation response
        const int WaveSize = 2; // 2 entity types per wave
        
        foreach (var entityWave in entitySequence.Chunk(WaveSize))
        {
            var entityTasks = entityWave.Select(entityType =>
                context.CallSubOrchestratorAsync<EntityMigrationResult>(
                    "EntityMigrationDurableOrchestrator",
                    new EntityMigrationRequest
                    {
                        MigrationId = migrationId,
                        EntityType = entityType,
                        SourceStore = input.MigrationRequest.SourceStore,
                        DestinationStore = input.MigrationRequest.DestinationStore,
                        CategoryTreeContext = input.CategoryTreeContext
                    }));
            
            // Race wave completion vs cancellation
            var allEntityTasks = Task.WhenAll(entityTasks);
            var winner = await Task.WhenAny(allEntityTasks, cancelEvent);
            
            if (winner == cancelEvent)
            {
                // Set cooperative flag for in-flight activities
                await context.CallActivityAsync("SetCancellationFlag", migrationId);
                
                context.SetCustomStatus("Cancelled by user request");
                return new MigrationOrchestrationResult
                {
                    MigrationId = migrationId,
                    Status = "Cancelled",
                    EndTime = context.CurrentUtcDateTime,
                    Message = "Migration cancelled by user request"
                };
            }
            
            // Wave completed successfully, continue to next wave
        }
        
        // All waves completed successfully
        return new MigrationOrchestrationResult
        {
            MigrationId = migrationId,
            Status = "Completed",
            EndTime = context.CurrentUtcDateTime,
            Message = "Migration completed successfully"
        };
    }
    catch (Exception ex)
    {
        context.SetCustomStatus($"Failed: {ex.Message}");
        return new MigrationOrchestrationResult
        {
            MigrationId = migrationId,
            Status = "Failed",
            EndTime = context.CurrentUtcDateTime,
            ErrorMessage = ex.Message
        };
    }
}
```

### **Cleanup Timer Function**
```csharp
// File: src/BigCommerce.Migration.Functions/Functions/CancellationCleanupFunctions.cs
[Function("CleanupCancellationFlags")]
public static async Task CleanupCancellationFlags(
    [TimerTrigger("0 0 */6 * * *")] TimerInfo timer, // Every 6 hours
    ILogger log)
{
    try
    {
        log.LogInformation("Starting cancellation flags cleanup at {Time}", DateTime.UtcNow);
        
        // Get completed migrations from last 24 hours
        var completedMigrations = await GetCompletedMigrations(TimeSpan.FromHours(24));
        
        var cleanupTasks = completedMigrations.Select(async migrationId =>
        {
            try
            {
                await CancellationStore.ClearCancelledAsync(migrationId);
                log.LogDebug("Cleaned up cancellation flag for migration {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to cleanup cancellation flag for migration {MigrationId}", migrationId);
            }
        });
        
        await Task.WhenAll(cleanupTasks);
        
        log.LogInformation("Completed cancellation flags cleanup. Processed {Count} migrations", 
            completedMigrations.Count);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Error during cancellation flags cleanup");
    }
}

private static async Task<List<string>> GetCompletedMigrations(TimeSpan lookback)
{
    // Implementation to query completed migrations from storage
    // Return list of migrationIds for cleanup
    return new List<string>();
}
```

---

## 🔧 **ACTIVITY INTEGRATION PATTERNS**

### **Chunk Processing with Cancellation**
```csharp
// File: src/BigCommerce.Migration.Orchestration/Activities/ProcessEntityChunkActivity.cs
[Function("ProcessEntityChunkActivity")]
public async Task<BatchProcessingResult> ProcessEntityChunkAsync(
    [ActivityTrigger] ProcessEntityChunkRequest request,
    CancellationToken cancellationToken)
{
    // Check cancellation before processing
    if (await CancellationStore.IsCancelledAsync(request.MigrationId))
    {
        return CreateCancelledResult(request, "Migration cancelled before chunk processing");
    }
    
    // Fetch entities
    var entities = await FetchEntitiesForChunk(batchRequest, cancellationToken);
    
    // Check cancellation after fetch, before processing
    if (await CancellationStore.IsCancelledAsync(request.MigrationId))
    {
        return CreateCancelledResult(request, "Migration cancelled after entity fetch", entities.Count);
    }
    
    // Process in sub-batches with periodic cancellation checks
    const int SubBatchSize = 50;
    var processedCount = 0;
    var successCount = 0;
    var errors = new List<string>();
    
    foreach (var subBatch in entities.Chunk(SubBatchSize))
    {
        // Check cancellation every sub-batch
        if (await CancellationStore.IsCancelledAsync(request.MigrationId))
        {
            _logger.LogInformation("Migration {MigrationId} cancelled during chunk processing at entity {ProcessedCount}", 
                request.MigrationId, processedCount);
            break;
        }
        
        try
        {
            var subBatchResult = await ProcessSubBatch(subBatch, batchRequest, cancellationToken);
            processedCount += subBatch.Length;
            successCount += subBatchResult.SuccessCount;
            errors.AddRange(subBatchResult.Errors);
        }
        catch (Exception ex)
        {
            errors.Add($"Sub-batch processing failed: {ex.Message}");
            processedCount += subBatch.Length;
        }
    }
    
    return new BatchProcessingResult
    {
        TotalProcessed = processedCount,
        SuccessfulEntities = successCount,
        FailedEntities = processedCount - successCount,
        Errors = errors,
        ProcessingTime = DateTime.UtcNow - startTime
    };
}

private BatchProcessingResult CreateCancelledResult(ProcessEntityChunkRequest request, string reason, int? fetchedCount = null)
{
    return new BatchProcessingResult
    {
        TotalProcessed = fetchedCount ?? 0,
        SuccessfulEntities = 0,
        FailedEntities = 0,
        Errors = new List<string> { reason },
        ProcessingTime = TimeSpan.Zero,
        IsCancelled = true
    };
}
```

### **Product Components Pipeline with Cancellation**
```csharp
// File: src/BigCommerce.Migration.Orchestration/Services/ProductComponentsMigrationPipeline.cs
public async Task<BatchProcessingResult> ProcessProductComponentsAsync(
    List<Dictionary<string, object>> productEntities,
    string migrationId,
    StoreConfiguration sourceStore,
    StoreConfiguration destinationStore,
    CancellationToken cancellationToken)
{
    var componentTypes = new[] { "options", "modifiers", "images", "reviews" };
    var result = new BatchProcessingResult();
    
    // Extract components from products
    var allComponents = await ExtractComponentsWithCancellation(productEntities, migrationId, cancellationToken);
    
    // Check cancellation after extraction
    if (await CancellationStore.IsCancelledAsync(migrationId))
    {
        _logger.LogInformation("Migration {MigrationId} cancelled after component extraction", migrationId);
        return CreateCancelledResult(migrationId, "Cancelled after component extraction");
    }
    
    // Process each component type with cancellation awareness
    foreach (var componentType in componentTypes)
    {
        if (await CancellationStore.IsCancelledAsync(migrationId))
        {
            _logger.LogInformation("Migration {MigrationId} cancelled during {ComponentType} processing", 
                migrationId, componentType);
            break; // Graceful exit preserving completed components
        }
        
        if (allComponents.ContainsKey(componentType) && allComponents[componentType].Any())
        {
            await ProcessComponentTypeWithCancellation(
                componentType, 
                allComponents[componentType], 
                migrationId, 
                sourceStore, 
                destinationStore, 
                result,
                cancellationToken);
        }
    }
    
    return result;
}

private async Task<Dictionary<string, List<ComponentWithContext>>> ExtractComponentsWithCancellation(
    List<Dictionary<string, object>> products,
    string migrationId,
    CancellationToken cancellationToken)
{
    var allComponents = new Dictionary<string, List<ComponentWithContext>>();
    
    for (int i = 0; i < products.Count; i++)
    {
        // Check cancellation every 5 products during extraction
        if (i % 5 == 0 && await CancellationStore.IsCancelledAsync(migrationId))
        {
            _logger.LogInformation("Migration {MigrationId} cancelled during component extraction at product {ProductIndex}", 
                migrationId, i);
            break;
        }
        
        // Extract components from current product
        ExtractComponentsFromProduct(products[i], allComponents);
    }
    
    return allComponents;
}
```

---

## 🧪 **TESTING PATTERNS**

### **Workflow Validation Test Template**
```csharp
// File: tests/BigCommerce.Migration.Tests/WorkflowValidation/CancellationWorkflowValidator.cs
public class CancellationWorkflowValidator
{
    [Fact]
    public async Task ValidateCompleteCancellationWorkflow_MustWork()
    {
        // ARRANGE: Setup real migration scenario
        var migrationRequest = CreateRealMigrationRequest();
        var migrationId = Guid.NewGuid().ToString();
        
        // ACT: Start migration
        var migrationTask = StartRealMigration(migrationId, migrationRequest);
        
        // Wait a bit then cancel
        await Task.Delay(TimeSpan.FromSeconds(2));
        await CancelMigration(migrationId);
        
        // ASSERT: Validate cancellation response time
        var stopwatch = Stopwatch.StartNew();
        var result = await migrationTask;
        stopwatch.Stop();
        
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), 
            $"❌ CANCELLATION TOO SLOW: {stopwatch.Elapsed.TotalSeconds} seconds");
        Assert.Equal("Cancelled", result.Status);
        
        // VALIDATE: Data consistency
        await ValidateDataConsistency(migrationId);
        
        // VALIDATE: No orphaned processes
        await ValidateNoOrphanedProcesses(migrationId);
    }
    
    [Fact]
    public async Task ValidateChunkLevelCancellation_MustWork()
    {
        // Test cancellation during chunk processing
        // Ensure partial results are preserved
        // Validate quick response time
    }
    
    [Fact]
    public async Task ValidateComponentLevelCancellation_MustWork()
    {
        // Test cancellation during product components processing
        // Ensure completed components are preserved
        // Validate graceful exit
    }
}
```

### **Performance Test Pattern**
```csharp
[Fact]
public async Task ValidateCancellationPerformanceOverhead_MustBeLessThan5Percent()
{
    // ARRANGE: Same migration scenario
    var migrationRequest = CreateStandardMigrationRequest();
    
    // ACT: Run migration WITHOUT cancellation support
    var baselineTime = await MeasureMigrationTime(migrationRequest, enableCancellation: false);
    
    // ACT: Run migration WITH cancellation support
    var cancellationTime = await MeasureMigrationTime(migrationRequest, enableCancellation: true);
    
    // ASSERT: Overhead less than 5%
    var overhead = (cancellationTime - baselineTime) / baselineTime * 100;
    Assert.True(overhead < 5.0, 
        $"❌ CANCELLATION OVERHEAD TOO HIGH: {overhead:F1}% (target: <5%)");
}
```

---

## 🔍 **DEBUGGING AND TROUBLESHOOTING**

### **Common Search Queries**
```bash
# Find existing cancellation code
codebase_search("How is cancellation currently implemented in the migration system?")
grep_search("ICancellationTokenRepository")
grep_search("CancellationMiddleware")
grep_search("CheckMigrationCancellation")

# Understand orchestrator patterns
codebase_search("How are Durable Functions orchestrators implemented?")
codebase_search("How does external event handling work in orchestrators?")
grep_search("OrchestrationTrigger")

# Find activity patterns
codebase_search("How are activities structured in the migration system?")
grep_search("ActivityTrigger")
grep_search("ProcessEntityChunk")

# Understand SignalR integration
codebase_search("How does SignalR event creation work in the migration system?")
grep_search("SignalREventFactory")
```

### **Diagnostic Commands**
```bash
# Check blob storage connectivity
az storage blob exists --container-name "migration-cancellation" --name "test.flag" --connection-string "<connection>"

# Monitor orchestrator instances
az functionapp function show --resource-group <rg> --name <function-app> --function-name "MigrationDurableOrchestrator"

# Check function app logs
az functionapp log tail --resource-group <rg> --name <function-app>
```

### **Troubleshooting Checklist**
- [ ] **Blob Storage**: Ensure `migration-cancellation` container exists
- [ ] **Connection String**: Verify `AzureWebJobsStorage` is configured
- [ ] **Orchestrator Status**: Check orchestrator instance status via portal
- [ ] **SignalR**: Verify SignalR connection and event propagation
- [ ] **Activity Execution**: Check activity function logs for cancellation checks

---

## 📚 **ARCHITECTURE COMPLIANCE CHECKLIST**

### **Azure Durable Functions Determinism** [[Memory:4674884]]
- [ ] ✅ **No external calls in orchestrators** - Use activities for blob checks
- [ ] ✅ **No throwing exceptions** - Return result objects
- [ ] ✅ **External events only** - Use RaiseEventAsync for cancellation signals

### **SignalR Centralization** [[Memory:4610425]]
- [ ] ✅ **Use SignalREventFactory** - For all cancellation notifications
- [ ] ✅ **Auto-populated properties** - Factory handles base properties
- [ ] ✅ **Consistent naming** - Follow established event patterns

### **Continue-on-Error Policy** [[Memory:4674884]]
- [ ] ✅ **Individual failures OK** - Don't stop for single entity failures
- [ ] ✅ **Comprehensive logging** - Log all cancellation events
- [ ] ✅ **Progress continuation** - Preserve completed work on cancellation

### **Performance Requirements** [[Memory:4674884]]
- [ ] ✅ **Throughput target** - Maintain 12,000+ req/hour
- [ ] ✅ **Error rate target** - Keep <5% error rate
- [ ] ✅ **Response time** - Cancellation <5 seconds
- [ ] ✅ **Overhead** - <5% performance impact

---

## 🚀 **NEXT STEPS QUICK START**

### **Phase 0: Immediate Actions**
```bash
# 1. Audit existing cancellation code
codebase_search("How is cancellation currently implemented?")
grep_search("ICancellationTokenRepository")
grep_search("CancellationMiddleware")

# 2. Identify files to remove/modify
file_search("CancellationTokenRepository")
file_search("CancellationMiddleware")

# 3. Check current dependencies
grep_search("services.Add.*Cancellation")
```

### **Phase 1: Core Infrastructure Setup**
```bash
# 1. Create CancellationStore class
# 2. Create cancellation activities
# 3. Update HTTP functions for direct orchestrator management
# 4. Add cleanup timer function
```

### **Continue Where You Left Off**
1. **Current Status**: Implementation not started
2. **Next Task**: Begin Phase 0 cleanup of existing infrastructure
3. **Dependencies**: None - can start immediately
4. **Estimated Time**: 1 day for complete cleanup

This quick reference provides all essential patterns and code snippets needed to implement the native Durable Functions cancellation system efficiently and maintain architectural consistency throughout the implementation.