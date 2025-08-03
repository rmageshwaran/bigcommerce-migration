# 🚀 **Chunked Hierarchical Category Migration - Quick Reference Guide**

## 📋 **Session Startup Protocol**

When resuming work on this project, provide this context to any AI assistant:

```markdown
## Project: Chunked Hierarchical Category Migration
## Current Status: [Check TASK-TRACKER.md for latest]
## Phase: [Current Phase from Task Tracker]
## Last Completed: [Last completed task]
## Next Task: [Next task to work on]

### Project Goal:
Transform memory-intensive category migration (200MB+) to memory-safe 
chunked processing (max 25MB) while achieving 5-10x performance improvement 
via BigCommerce bulk creation API optimization.
```

---

## 🎯 **Critical Architectural Constraints** [[memory:4674884]]

### **NEVER VIOLATE THESE RULES**:
❌ **Azure Durable Functions Determinism**: No external calls in orchestrators  
❌ **SignalR Manual Creation**: Always use SignalREventFactory  
❌ **Stop Migration on Errors**: Implement continue-on-error policy  
❌ **Add Retry Logic**: No automatic retries (API rate limit protection)  
❌ **Skip Tests**: 99%+ coverage mandatory, TDD approach required  
❌ **Break Determinism**: All orchestrator operations must be deterministic  

### **ALWAYS FOLLOW THESE PATTERNS**:
✅ **Use Activities for External Calls**: API calls, database operations  
✅ **Return Result Objects**: Never throw exceptions in Durable Functions  
✅ **Use SignalREventFactory**: Centralized event creation only  
✅ **Write Tests First**: TDD approach with comprehensive coverage  
✅ **Follow SOLID Principles**: All five principles must be respected  
✅ **Memory Safety**: Monitor and limit memory usage  

---

## 🔧 **Essential Development Commands**

### **AI Assistant Interaction Commands**

#### **For Implementation Tasks**:
```markdown
Please implement Task [X.X] following these requirements:
1. TDD approach - write failing tests first
2. Follow [specific SOLID principle] 
3. Ensure Azure Durable Functions determinism
4. Add comprehensive error logging with continue-on-error
5. Include SignalR progress broadcasting using centralized factory
6. Validate memory usage stays below 25MB
7. Respect Azure Functions timeout limits (activities ≤4 minutes)
```

#### **For Code Review**:
```markdown
Please review this code for:
1. SOLID principles compliance ([specify which ones])
2. Azure Durable Functions best practices
3. Memory safety and performance
4. Continue-on-error error handling
5. Test coverage completeness
6. SignalR centralized factory usage
7. Deterministic behavior validation
```

#### **For Testing**:
```markdown
Please create tests for [component] covering:
1. Happy path scenarios with expected inputs
2. Error conditions and recovery (continue-on-error)
3. Cancellation handling and cleanup
4. Memory usage under load
5. Azure Functions deterministic behavior
6. SignalR event generation using factory
7. Performance under different data sizes
```

---

## 📁 **Key File Locations**

### **Implementation Files**:
```
Core Models:
├── src/BigCommerce.Migration.Core/Models/ChunkedHierarchyConfiguration.cs
├── src/BigCommerce.Migration.Core/Models/HierarchyModels.cs
└── src/BigCommerce.Migration.Core/Models/LevelProcessingModels.cs

Discovery Strategy:
├── src/BigCommerce.Migration.Core/Interfaces/IChunkedHierarchicalDiscoveryStrategy.cs
└── src/BigCommerce.Migration.Orchestration/Strategies/ChunkedHierarchicalDiscoveryStrategy.cs

Activities:
├── src/BigCommerce.Migration.Orchestration/Activities/FetchCategoriesForLevelActivity.cs
├── src/BigCommerce.Migration.Orchestration/Activities/ProcessCategoryLevelActivity.cs
└── src/BigCommerce.Migration.Orchestration/Activities/SignalRProgressActivities.cs

Orchestrator:
└── src/BigCommerce.Migration.Orchestration/Orchestrators/ChunkedCategoryMigrationOrchestrator.cs
```

### **Test Files**:
```
Unit Tests:
├── tests/BigCommerce.Migration.UnitTests/Core/Models/
├── tests/BigCommerce.Migration.UnitTests/Orchestration/Strategies/
├── tests/BigCommerce.Migration.UnitTests/Orchestration/Activities/
└── tests/BigCommerce.Migration.UnitTests/Orchestration/Orchestrators/

Performance Tests:
└── tests/BigCommerce.Migration.PerformanceTests/ChunkedHierarchy/
```

### **Configuration Files**:
```
├── src/BigCommerce.Migration.Functions/appsettings.json
└── src/BigCommerce.Migration.Functions/Extensions/ServiceCollectionExtensions.cs
```

---

## 💻 **Code Patterns & Templates**

### **Azure Functions Activity Template**:
```csharp
[FunctionName("ActivityName")]
public async Task<ResultType> ActivityMethod(
    [ActivityTrigger] RequestType request,
    ILogger log)
{
    var stopwatch = Stopwatch.StartTime();
    
    try
    {
        log.LogInformation("🔍 Starting {ActivityName} for migration {MigrationId}", 
            "ActivityName", request.MigrationId);
            
        // Azure Functions: Timeout consideration
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            request.CancellationToken, timeoutCts.Token);
            
        // Implementation here
        var result = await ProcessAsync(request, combinedCts.Token);
        
        log.LogInformation("✅ Completed {ActivityName} in {Duration}ms", 
            "ActivityName", stopwatch.ElapsedMilliseconds);
            
        return result;
    }
    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
    {
        log.LogWarning("⏰ {ActivityName} timeout for migration {MigrationId}", 
            "ActivityName", request.MigrationId);
            
        // Continue-on-error: Return safe default
        return CreateSafeDefaultResult();
    }
    catch (Exception ex)
    {
        log.LogError(ex, "❌ {ActivityName} failed for migration {MigrationId}", 
            "ActivityName", request.MigrationId);
            
        // Continue-on-error: Return error result, don't throw
        return CreateErrorResult(ex.Message);
    }
}
```

### **Durable Functions Orchestrator Template**:
```csharp
[FunctionName("OrchestratorName")]
public async Task<ResultType> OrchestratorMethod(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    var request = context.GetInput<RequestType>();
    var logger = context.CreateReplaySafeLogger(_logger);
    
    try
    {
        // Azure Durable Functions: Deterministic execution only
        logger.LogInformation("🚀 Starting {OrchestratorName} for {MigrationId}", 
            "OrchestratorName", request.MigrationId);
        
        // Check cancellation (deterministic pattern)
        var isCancelled = await context.CallActivityAsync<bool>(
            "CheckMigrationCancellation", request.MigrationId);
            
        if (isCancelled)
        {
            logger.LogInformation("🛑 Migration {MigrationId} cancelled", request.MigrationId);
            return CreateCancelledResult();
        }
        
        // Process via activities (no external calls in orchestrator)
        var result = await context.CallActivityAsync<ResultType>(
            "ProcessActivity", request);
            
        return result;
    }
    catch (Exception ex)
    {
        logger.LogError("❌ {OrchestratorName} failed: {Error}", 
            "OrchestratorName", ex.Message);
            
        // Return error result (don't throw in orchestrator)
        return CreateErrorResult(ex.Message);
    }
}
```

### **Unit Test Template**:
```csharp
[TestFixture]
public class ComponentNameTests
{
    private Mock<IDependency> _mockDependency;
    private ComponentName _component;
    
    [SetUp]
    public void Setup()
    {
        _mockDependency = new Mock<IDependency>();
        _component = new ComponentName(_mockDependency.Object);
    }
    
    [Test]
    public async Task Method_WithValidInput_ShouldReturnExpectedResult()
    {
        // Arrange
        var input = CreateValidInput();
        _mockDependency.Setup(x => x.Method(It.IsAny<Parameter>()))
                      .ReturnsAsync(expectedResult);
        
        // Act
        var result = await _component.Method(input);
        
        // Assert
        result.Should().NotBeNull();
        result.Property.Should().Be(expectedValue);
        _mockDependency.Verify(x => x.Method(It.IsAny<Parameter>()), Times.Once);
    }
    
    [Test]
    public async Task Method_WithException_ShouldContinueOnError()
    {
        // Arrange
        _mockDependency.Setup(x => x.Method(It.IsAny<Parameter>()))
                      .ThrowsAsync(new InvalidOperationException("Test error"));
        
        // Act
        var result = await _component.Method(CreateValidInput());
        
        // Assert - Continue-on-error: Should not throw, return error result
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Test error");
    }
}
```

### **SignalR Event Creation Pattern**:
```csharp
// ✅ CORRECT: Use centralized factory [[memory:4674884]]
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
{
    OverallProgress = progressPercentage,
    Status = "processing",
    TotalEntities = totalCount,
    ProcessedEntities = processedCount,
    EntityType = "categories",
    // Base properties auto-populated by factory
});

await _signalRService.SendToGroupAsync($"migration_{migrationId}", progressEvent);

// ❌ WRONG: Never create events manually
var manualEvent = new MigrationProgressEvent { /* ... */ }; // NEVER DO THIS
```

### **Bulk Category Creation Pattern**:
```csharp
// ✅ CORRECT: Use BigCommerce bulk creation API for 5-10x performance
public async Task<CategoryBatchResult> CreateCategoriesBulk(
    List<TransformedCategory> batch, 
    int treeId)
{
    var payload = batch.Select(c => new
    {
        name = c.Name,
        parent_id = c.ParentId,
        tree_id = treeId,
        description = c.Description,
        is_visible = c.IsVisible,
        sort_order = c.SortOrder,
        page_title = c.PageTitle,
        meta_description = c.MetaDescription,
        url = new { path = c.UrlPath, is_customized = false }
    }).ToArray();
    
    try
    {
        // Single API call for entire batch (25-50 categories)
        var response = await _apiClient.CreateCategoriesBulkAsync(payload);
        
        return new CategoryBatchResult
        {
            IsSuccess = true,
            CreatedCount = response.Data.Count,
            CreatedCategories = response.Data,
            Errors = new List<string>()
        };
    }
    catch (Exception ex)
    {
        // Continue-on-error: Log but don't stop migration
        _logger.LogError(ex, "❌ Bulk creation failed for batch of {Count}", batch.Count);
        
        return new CategoryBatchResult
        {
            IsSuccess = false,
            CreatedCount = 0,
            Errors = new List<string> { ex.Message }
        };
    }
}

// ❌ WRONG: Individual category creation (25x slower)
foreach (var category in categories)
{
    await _apiClient.CreateCategoryAsync(category); // TOO SLOW
}
```

---

## 🧪 **Testing & Quality Standards**

### **Test Coverage Requirements**:
- **Minimum**: 99% line coverage
- **Unit Tests**: All business logic, error conditions, edge cases
- **Integration Tests**: End-to-end workflows, external dependencies
- **Performance Tests**: Memory usage, processing time, scalability

### **Testing Checklist**:
- [ ] **Happy Path**: Normal operation scenarios
- [ ] **Error Conditions**: API failures, timeouts, invalid data
- [ ] **Cancellation**: Proper cleanup and resource management
- [ ] **Memory Safety**: Usage stays below 25MB limit
- [ ] **Azure Functions**: Deterministic behavior, timeout handling
- [ ] **SignalR Integration**: Factory usage, event broadcasting
- [ ] **Continue-on-Error**: Individual failures don't stop migration

### **Performance Validation**:
```csharp
// Memory monitoring pattern
var initialMemory = GC.GetTotalMemory(false);
// ... processing code ...
var finalMemory = GC.GetTotalMemory(false);
var memoryUsed = (finalMemory - initialMemory) / 1024 / 1024; // MB

// Assert memory usage is within limits
memoryUsed.Should().BeLessThan(25); // 25MB limit
```

---

## 🔧 **Configuration Reference**

### **appsettings.json - Chunked Hierarchy Section**:
```json
{
  "ChunkedHierarchy": {
    "MaxCategoriesPerLevel": 10000,
    "BatchSizePerLevel": 25,
    "MaxBulkCreateSize": 50,
    "MaxHierarchyDepth": 10,
    "FallbackThreshold": 25000,
    "EnableMemoryMonitoring": true,
    "LevelProcessingTimeoutMinutes": 4,
    "BulkCreationTimeoutMinutes": 2,
    "EnableBulkCreation": true,
    "AdaptiveBatchSizing": true
  }
}
```

### **Dependency Injection Registration**:
```csharp
// Add to ServiceCollectionExtensions.cs
services.Configure<ChunkedHierarchyConfiguration>(
    configuration.GetSection("ChunkedHierarchy"));
services.AddScoped<IChunkedHierarchicalDiscoveryStrategy, 
    ChunkedHierarchicalDiscoveryStrategy>();
```

---

## 🚨 **Troubleshooting Guide**

### **Common Issues & Solutions**:

#### **Memory Usage Exceeding Limits**:
```
Problem: Activity using > 25MB memory
Solution: 
1. Check batch sizes in configuration
2. Add memory monitoring logs
3. Force GC.Collect() after large operations
4. Reduce data held in memory simultaneously
```

#### **Azure Functions Timeout**:
```
Problem: Activity exceeding 4-minute limit
Solution:
1. Check LevelProcessingTimeout configuration
2. Optimize API call patterns
3. Reduce batch sizes for large levels
4. Add timeout cancellation tokens
```

#### **Determinism Violations**:
```
Problem: Orchestrator replay errors
Solution:
1. No DateTime.Now (use context.CurrentUtcDateTime)
2. No Guid.NewGuid() (use deterministic generation)
3. No external HTTP calls (use activities)
4. No async/await in orchestrator logic
```

#### **SignalR Events Not Working**:
```
Problem: Real-time updates not appearing
Solution:
1. Verify SignalREventFactory usage
2. Check SignalR group membership
3. Validate event serialization
4. Check continue-on-error for SignalR failures
```

#### **Missing Error Handling Issues**:
```
Problem: API errors not being logged to OpenSearch
Solution:
1. Implement Task 5.3: API Error Handling and OpenSearch Logging
2. Replicate EntityErrorHandlingService pattern for bulk creation
3. Ensure structured error logging with blob storage for large payloads
4. Add continue-on-error policy for bulk creation failures
5. Implement error recovery with fallback to individual creation
```

#### **Bulk Creation Performance Issues**:
```
Problem: Bulk creation not achieving expected performance
Solution:
1. Check batch size configuration (25-50 recommended)
2. Monitor API health metrics for adaptive sizing
3. Verify bulk creation API is enabled (EnableBulkCreation=true)
4. Check for API rate limiting affecting bulk operations
5. Ensure payload size doesn't exceed BigCommerce limits
```

#### **Bulk Creation Failures**:
```
Problem: Entire batches failing to create
Solution:
1. Validate category payload format matches BigCommerce API
2. Check for parent_id mapping issues in batch
3. Ensure tree_id is correctly set for all categories
4. Review batch size - reduce if hitting API limits
5. Implement fallback to individual creation for failed batches
```

#### **Legacy Code Duplication Issues**:
```
Problem: Multiple category migration code paths causing confusion
Solution:
1. Implement Task 6.2: Comprehensive Legacy Code Cleanup (12h)
2. Mark legacy strategies obsolete: V3HierarchicalStrategy, CategoryFetchStrategy, CategoryCreationStrategy  
3. Update EntityDiscoveryStrategyFactory to route categories to chunked strategy
4. Remove category-specific special cases from orchestrators
5. Add feature flag UseChunkedCategoryMigration (default: true)
6. Update 15+ test files to use new architecture
```

### **Debugging Commands**:
```bash
# Check current memory usage
dotnet-counters monitor --process-id <PID> --counters System.Runtime

# Validate Azure Functions locally
func start --port 7071

# Run specific test category
dotnet test --filter Category=ChunkedHierarchy

# Check test coverage
dotnet test --collect:"XPlat Code Coverage"
```

---

## 📊 **Key Metrics to Monitor**

### **Performance Metrics**:
- **Memory Usage**: Max 25MB per chunk
- **Processing Time**: Target 5-10x faster than current (via bulk creation API)
- **API Call Reduction**: 90%+ fewer HTTP requests (bulk optimization)
- **Success Rate**: Maintain >95%
- **Throughput**: Target 20,000+ req/hour (improved via bulk operations)

### **Quality Metrics**:
- **Test Coverage**: 99%+ required
- **Build Success**: 100% required
- **Code Review**: All changes reviewed
- **Documentation**: Updated with changes

### **Production Metrics**:
- **Migration Success Rate**: Track per level
- **Error Distribution**: By level and type
- **Memory Usage Patterns**: Peak and average
- **Processing Time Trends**: Per hierarchy depth

---

## 📞 **Quick Decision Matrix**

### **When to Use Chunked vs Legacy**:
```
Use Chunked When:
✅ Category count > 25,000
✅ Memory usage is a concern
✅ Performance needs optimization
✅ New implementations

Use Legacy When:
⚠️ Category count < 5,000
⚠️ Urgent hotfix needed
⚠️ Backward compatibility required
⚠️ No time for full testing
```

### **Error Handling Decisions**:
```
Stop Migration When:
🛑 Infrastructure failures (database down)
🛑 Authentication/authorization failures
🛑 Configuration errors

Continue Migration When:
✅ Individual category creation fails
✅ API rate limit errors
✅ SignalR broadcasting fails
✅ Non-critical validation errors
```

---

## 🎯 **Session End Checklist**

Before ending a development session:

- [ ] **Update Task Tracker**: Mark completed tasks, update progress
- [ ] **Run Tests**: Ensure all tests pass
- [ ] **Check Memory Usage**: Validate no memory leaks
- [ ] **Update Documentation**: Document any discoveries or changes
- [ ] **Commit Changes**: With descriptive commit messages
- [ ] **Note Next Steps**: Clear priority for next session

### **For AI Assistant Handoff**:
```markdown
## Session Summary - [Date]

### Completed:
- Task [X.X]: [Description] - [Status/Notes]

### Current Focus:
- Task [X.X]: [What was being worked on]
- Progress: [Percentage or description]
- Next Steps: [Specific next actions]

### Important Context:
- [Any important discoveries or decisions]
- [Challenges encountered and solutions]
- [Configuration changes made]

### Next Session Should Start With:
- [Specific task or area to focus on]
- [Any setup or context needed]
```

---

**📚 Related Documents:**
- **Implementation Plan**: `Chunked-Hierarchical-Category-Migration-IMPLEMENTATION-PLAN.md`
- **Task Tracker**: `Chunked-Hierarchical-Category-Migration-TASK-TRACKER.md`
- **Architecture Constraints**: `MASTER-ARCHITECTURE-INDEX.md`
- **Current System**: `Migration-Architecture-and-Execution-Flow.md`

**🔄 Last Updated**: [Date]  
**📋 Document Owner**: [Team/Individual]