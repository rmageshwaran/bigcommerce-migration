# 🤖 **AI ASSISTANT WORKFLOW GUIDE**
## *Systematic Approach to BigCommerce Migration System Development*

### 🎯 **PURPOSE**
This guide ensures that AI assistants **ALWAYS** follow established patterns, reference relevant documentation, and maintain architectural consistency for ALL work on the BigCommerce Migration System.

### 🚨 **CRITICAL CONSTRAINTS** *(Check BEFORE starting any work)*
- **NO RETRY LOGIC**: Never implement retry mechanisms for API calls (wastes calls, increases rate limits)
- **USE CONTINUE-ON-ERROR**: Log failures and continue processing other items
- **SINGLE API ATTEMPTS**: Make one API call per operation, then move on
- **SIGNALR CENTRALIZATION**: Always use SignalREventFactory, never manual events
- **MANDATORY ERROR HANDLING**: ALL exceptions MUST be logged to OpenSearch with proper categorization

---

## 🚨 **MANDATORY ERROR HANDLING STANDARDS**

### **Error Type Classification & Handling Rules**

#### **🏗️ INFRASTRUCTURE ERRORS** (THROW - System Must Stop)
```csharp
// ✅ INFRASTRUCTURE ERRORS - Must throw to stop migration
if (IsInfrastructureException(ex))
{
    _logger.LogCritical("🚨 Infrastructure failure - stopping migration: {Error}", ex.Message);
    
    // Log to OpenSearch for monitoring
    await _openSearchService.LogErrorAsync(
        "Infrastructure Failure", 
        ex, 
        new { migrationId, component = "ServiceName", severity = "Critical" }
    );
    
    throw; // Let infrastructure failures bubble up
}

// Infrastructure error types:
// - Storage unavailable, network errors, authentication failures
// - Azure service outages, connectivity issues  
// - Memory exhaustion, system resource failures
```

#### **📋 APPLICATION LOGIC ERRORS** (LOG & CONTINUE)
```csharp
// ✅ APPLICATION ERRORS - Log to OpenSearch & continue with safe defaults
catch (Exception ex) when (!IsInfrastructureException(ex))
{
    _logger.LogError(ex, "❌ Application error: {Error}", ex.Message);
    
    // MANDATORY: Log ALL application errors to OpenSearch
    await _openSearchService.LogErrorAsync(
        $"{nameof(ServiceName)}.{nameof(MethodName)}", 
        ex, 
        new { 
            migrationId, 
            entityType,
            errorType = CategorizeErrorType(ex),
            operationContext = "detailed context"
        }
    );
    
    // Return safe default to continue migration
    return new ResultType { Success = false, ErrorMessage = ex.Message };
}
```

#### **🔄 DURABLE FUNCTIONS COMPONENTS** (NEVER THROW)
```csharp
// ✅ ORCHESTRATORS & ACTIVITIES - Always return result objects
catch (Exception ex)
{
    _logger.LogError(ex, "Activity failed: {ActivityName}", nameof(ActivityName));
    
    // Log to OpenSearch with context
    await _openSearchService.LogErrorAsync(
        $"Activity.{nameof(ActivityName)}", 
        ex, 
        new { migrationId, activityContext = "context" }
    );
    
    // Return proper result object - NEVER throw in Durable Functions
    return new ActivityResult
    {
        Success = false,
        ErrorMessage = ex.Message,
        CanRetry = DetermineRetryability(ex)
    };
}
```

### **🏷️ Error Type Categorization Helper**
```csharp
private static bool IsInfrastructureException(Exception ex)
{
    return ex.Message.Contains("Storage") ||
           ex.Message.Contains("unavailable") ||
           ex.Message.Contains("timeout") ||
           ex.Message.Contains("connection") ||
           ex.Message.Contains("network") ||
           ex is TimeoutException ||
           ex is HttpRequestException ||
           ex is SocketException ||
           (ex is InvalidOperationException && ex.Message.Contains("storage"));
}

private static string CategorizeErrorType(Exception ex)
{
    return ex switch
    {
        ArgumentException _ => "ValidationError",
        HttpRequestException http when http.Message.Contains("429") => "RateLimitError", 
        HttpRequestException _ => "ApiError",
        TimeoutException _ => "TimeoutError",
        SocketException _ => "NetworkError",
        _ => "SystemError"
    };
}
```

### **📊 Required OpenSearch Fields & User Visibility**
```csharp
// MANDATORY: All OpenSearch error logs must include these fields
new { 
    migrationId,           // For correlation
    entityType,            // Entity being processed
    errorType,             // Categorized error type
    severity,              // Critical/Error/Warning
    component,             // Service/Activity name
    operationContext,      // What was being done
    retryable = false,     // Whether error supports retry
    timestamp = DateTime.UtcNow
}
```

### **🚨 CRITICAL: User Visibility Rules**
```csharp
// ✅ USER-VISIBLE: Only BigCommerce API errors with Category = "Error"
await _openSearchService.LogErrorAsync(context, exception, new { 
    Category = "Error",  // SHOWS in user dashboard
    errorType = "ApiError",
    userMessage = "Simple, actionable message for users"
});

// 🔇 INTERNAL-ONLY: Infrastructure/system errors (no Category or different Category)  
await _openSearchService.LogErrorAsync(context, exception, new { 
    Category = "Internal", // HIDDEN from user dashboard
    errorType = "InfrastructureError", 
    severity = "Critical"
});
```

**RULE: Only set `Category = "Error"` for BigCommerce API failures that users need to see and can potentially fix!**

---

## 📋 **MANDATORY WORKFLOW FOR ALL REQUESTS**

### **🔍 STEP 1: INITIAL REQUEST ANALYSIS**

#### **Request Classification** *(Determine before proceeding)*
- [ ] **🔧 Bug Fix** - Fixing existing functionality
- [ ] **✨ New Feature** - Adding new capability
- [ ] **⚡ Enhancement** - Improving existing feature
- [ ] **🏗️ Refactoring** - Code improvement without functional changes
- [ ] **📚 Documentation** - Documentation updates
- [ ] **🧪 Testing** - Test-related work
- [ ] **🚀 Performance** - Performance optimization

#### **Impact Assessment** *(Check all applicable areas)*
- [ ] **SignalR Events** - Will affect real-time communication
- [ ] **Orchestrators** - Will modify Durable Functions orchestrators
- [ ] **API Integration** - Will change BigCommerce API interactions
- [ ] **Database/Storage** - Will modify data storage patterns
- [ ] **Performance** - May impact system performance
- [ ] **Error Handling** - Will modify error handling patterns
- [ ] **Testing** - Will require new/updated tests
- [ ] **Frontend** - Will affect React dashboard
- [ ] **Infrastructure** - Will change deployment or configuration

---

## 📚 **STEP 2: REQUIRED DOCUMENTATION REVIEW**

### **ALWAYS READ FIRST** *(Mandatory for ALL requests)*
1. **`MASTER-ARCHITECTURE-INDEX.md`** - Complete system constraints
2. **`PRE-WORK-CHECKLIST.md`** - Mandatory development checklist
3. **`CODING-PATTERNS-QUICK-REFERENCE.md`** - Copy-paste ready patterns

### **CONDITIONAL READING** *(Based on impact assessment)*

#### **IF New Entity Implementation:**
1. **`Task-7-Live-Cancellation-Integration-TRACKER.md`** - Live cancellation patterns for all entities
2. **`SOLID-Principles-Refactoring-Task-List.md`** - Strategy pattern implementation for entities
3. **`BigCommerce-API-Entity-Reference.md`** - Entity-specific API patterns
4. **`ID-Mapping-and-Dependency-Resolution.md`** - Entity mapping and dependency patterns

#### **IF SignalR Events Involved:**
```bash
# Required reading
- SignalR-Centralized-Architecture.md
- SignalR-Centralization-QUICK-REFERENCE.md
- SignalR-Frontend-Integration-Guide.md

# Search queries to run
"How does SignalR event creation work in the migration system?"
"What are the centralized SignalR patterns?"
```

#### **IF Orchestrators Involved:**
```bash
# Required reading
- Azure-Durable-Functions-Deterministic-Architecture.md
- Consistent-Cancellation-Handling-Implementation.md
- Migration-Architecture-and-Execution-Flow.md

# Search queries to run
"How are Durable Functions orchestrators implemented?"
"What are the deterministic orchestrator requirements?"
```

#### **IF API Integration Involved:**
```bash
# Required reading
- BigCommerce-API-Entity-Reference.md
- Feature-Enhancement-Distributed-Rate-Limiting.md
- Throughput-Optimization-Technical-Architecture.md

# Search queries to run
"How does BigCommerce API integration work with rate limiting?"
"What are the API integration patterns?"
```

#### **IF Performance Impact:**
```bash
# Required reading
- Performance-Optimization-Completion-Summary.md
- Throughput-Optimization-Technical-Architecture.md
- Performance-Optimization-Detailed-Task-Breakdown.md

# Search queries to run
"What are the performance optimization patterns?"
```

#### **IF Error Handling Changes:**
```bash
# MANDATORY: Review error handling standards above
# Must distinguish between Infrastructure vs Application errors
# Must log ALL exceptions to OpenSearch with proper categorization
# Must follow Continue-on-Error policy for non-infrastructure failures

# Search queries to run
"How are exceptions handled and logged in the migration system?"
"What are the error categorization patterns?"
"How is system performance validated?"
```

#### **IF Testing Required:**
```bash
# Required reading
- Testing-Strategy-Guide.md
- Coding-Standards-and-Testing-Strategy.md
- Integration-and-E2E-Testing-Strategy.md

# Search queries to run
"What are the testing patterns for [component type]?"
"How is TDD implemented in the system?"
```

---

## 🔍 **STEP 3: CODEBASE PATTERN DISCOVERY**

### **Required Searches** *(Use codebase_search tool)*

#### **Pattern Discovery Searches:**
```bash
# General functionality understanding
"How does [specific functionality] work in the migration system?"
"Where is [component name] implemented in the codebase?"

# Pattern identification
"How are [entity type] entities processed?"
"What are the error handling patterns in [component type]?"
"How is dependency injection configured for [service type]?"

# Architecture validation
"How does [similar feature] integrate with the existing architecture?"
"What are the performance patterns for [operation type]?"
```

#### **Use Parallel Tool Calls** *(For efficiency)*
```bash
# Example: For SignalR + Orchestrator work
codebase_search("How does SignalR event creation work?")
codebase_search("How are orchestrators implemented?") 
codebase_search("What are the error handling patterns?")
grep_search("SignalREventFactory")
read_file("relevant_file_from_searches.cs")
```

---

## 🏗️ **STEP 4: ARCHITECTURE COMPLIANCE VALIDATION**

### **Validate Against Critical Constraints** *(Reference memory: 4674884)*

#### **Azure Durable Functions Determinism:**
- [ ] **No external calls in orchestrators** - Use activities only
- [ ] **No throwing exceptions** - Return result objects instead
- [ ] **Deterministic execution** - No random values, external dependencies

#### **SignalR Centralization:**
- [ ] **Use SignalREventFactory** - Never create events manually
- [ ] **Auto-populated properties** - Factory handles base properties
- [ ] **Consistent naming** - Follow established event patterns

#### **Continue-on-Error Policy:**
- [ ] **Individual failures OK** - Don't stop migration for single entity failures
- [ ] **Comprehensive logging** - Log all errors with context
- [ ] **Progress continuation** - Continue processing other entities

#### **🚨 NO RETRY LOGIC POLICY** *(CRITICAL - Memory: 3357086)*:
- [ ] **No automatic retries** - Never implement retry mechanisms for API calls
- [ ] **Rate limit preservation** - Avoid wasting API calls on failed requests
- [ ] **Single attempt only** - Make one API call per operation, then continue-on-error
- [ ] **Error logging required** - Log failures without retrying

#### **Performance Requirements:**
- [ ] **Throughput target** - Maintain 12,000+ req/hour
- [ ] **Error rate target** - Keep <5% error rate
- [ ] **Rate limiting** - Respect dynamic 5-50 req/sec limits
- [ ] **Memory limits** - Stay within Azure Functions constraints

#### **Testing Standards:**
- [ ] **TDD approach** - Write tests first
- [ ] **99%+ coverage** - Comprehensive test coverage
- [ ] **562+ tests passing** - Maintain test suite integrity

---

## 💻 **STEP 5: IMPLEMENTATION APPROACH**

### **Use Established Patterns** *(Reference CODING-PATTERNS-QUICK-REFERENCE.md)*

#### **SignalR Events:**
```csharp
// ✅ ALWAYS use factory pattern
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, options);
```

#### **Error Handling:**
```csharp
// ✅ Durable Functions - return result objects
catch (OperationCanceledException)
{
    return new BatchProcessingResult { IsSuccess = false, ErrorMessage = "Cancelled" };
}
```

#### **Testing:**
```csharp
// ✅ TDD approach - write tests first
[Test]
public async Task Method_WithCondition_ExpectedBehavior()
{
    // Arrange, Act, Assert pattern
}
```

### **🛑 NEW ENTITY IMPLEMENTATION REQUIREMENTS** *(MANDATORY for all new entities)*

#### **🛑 COMPREHENSIVE LIVE CANCELLATION INTEGRATION** *(MANDATORY for ALL entity implementations)*

All entity implementations MUST include live cancellation checks at multiple levels for responsive user-initiated cancellation:

#### **📊 Required Cancellation Hierarchy:**

```
📋 Migration Level      ✅ (Before migration, before each entity type)
├─ 📂 EntityType Level  ✅ (Before entity processing starts) 
├─ 🏗️ Level Level       ✅ (Before hierarchy levels) - Categories only
├─ 📦 Batch Level       ✅ (Before each batch)
├─ 🎯 Sub-batch Level   ✅ (Before each sub-batch) - Parallel entities
└─ 🔧 Entity Level      ✅ (Before individual entity processing)
```

#### **🔧 Implementation Patterns:**

**1. Activity-Level Cancellation (All Activities):**
```csharp
[Function("ProcessEntityBatchActivity")]
public async Task<BatchProcessingResult> ProcessEntityBatch(
    [ActivityTrigger] BatchProcessingRequest request)
{
    // 🛑 BATCH-LEVEL: Check before batch processing
    var isBatchCancelled = await _liveCancellationManager.IsCancelledAsync(
        request.MigrationId,
        CancellationScope.Batch,
        request.EntityType,
        $"batch-{request.BatchNumber}",
        null);

    if (isBatchCancelled)
    {
        _logger.LogInformation("🚫 [LIVE-CANCEL] Batch {BatchNumber} cancelled for {EntityType}", 
            request.BatchNumber, request.EntityType);
        result.Errors.Add($"Batch {request.BatchNumber} processing was cancelled");
        return result;
    }

    // Process entities with frequent cancellation checks
    for (int i = 0; i < sourceEntities.Count; i++)
    {
        // 🛑 ENTITY-LEVEL: Check during entity processing loop
        var isEntityCancelled = await _liveCancellationManager.IsCancelledAsync(
            request.MigrationId,
            CancellationScope.Migration,
            request.EntityType,
            $"batch-{request.BatchNumber}",
            null);

        if (isEntityCancelled)
        {
            _logger.LogInformation("🚫 [LIVE-CANCEL] Entity processing cancelled at {EntityIndex}/{Total}", 
                i + 1, sourceEntities.Count);
            break;
        }
        
        // Process individual entity...
    }
}
```

**2. Sub-batch Level Cancellation (Parallel Processing):**
```csharp
// 🛑 SUB-BATCH LEVEL: Check before each sub-batch
for (int subBatchIndex = 0; subBatchIndex < subBatches.Count; subBatchIndex++)
{
    var isSubBatchCancelled = await _liveCancellationManager.IsCancelledAsync(
        batch.MigrationId,
        CancellationScope.Batch,
        batch.EntityType,
        $"batch-{batch.BatchNumber}-subbatch-{subBatch.SubBatchNumber}",
        null);

    if (isSubBatchCancelled)
    {
        _logger.LogInformation("🎯 [LIVE-CANCEL] Sub-batch {SubBatchNumber}/{Total} cancelled", 
            subBatch.SubBatchNumber, subBatches.Count);
        break;
    }
    
    await ProcessSingleSubBatch(subBatch, cancellationToken);
}
```

**3. Hierarchical Entity Processing (Categories):**
```csharp
// 🛑 LEVEL-BASED: Check before hierarchy level processing
var isLevelCancelled = await _liveCancellationManager.IsCancelledAsync(
    migrationId,
    CancellationScope.Batch,
    "categories",
    $"level-{level}",
    null);

// 🛑 BULK BATCH: Check before each bulk batch
foreach (var batch in batches)
{
    var isBatchCancelled = await _liveCancellationManager.IsCancelledAsync(
        migrationId,
        CancellationScope.Batch,
        "categories",
        $"level-{level}-batch-{batchNumber}",
        null);
    
    if (isBatchCancelled) break;
}

// 🛑 INDIVIDUAL: Check during individual category processing
foreach (var categoryId in categoryIds)
{
    var isCategoryCancelled = await _liveCancellationManager.IsCancelledAsync(
        migrationId,
        CancellationScope.Migration,
        "categories",
        $"level-{level}",
        null);
    
    if (isCategoryCancelled) break;
}
```

#### **🎯 Implementation Checklist for ALL Entities:**
- [ ] **Activity Dependencies**: Ensure `ILiveCancellationManager` is injected
- [ ] **Batch-Level Checks**: Before processing each batch
- [ ] **Entity-Level Checks**: During entity processing loops
- [ ] **Sub-batch Checks**: For parallel processing entities (if applicable)
- [ ] **Hierarchy Checks**: For hierarchical entities like categories (if applicable)
- [ ] **Early Exit Logic**: Proper `break`/`return` on cancellation
- [ ] **Logging**: Consistent cancellation log messages with 🚫 [LIVE-CANCEL] prefix
- [ ] **Error Reporting**: Add cancellation to error collection for tracking

#### **Orchestrator Integration:**
```csharp
// ✅ REQUIRED: Check cancellation in orchestrators
var cancellationCheck = await context.CallActivityAsync<CancellationCheckResult>(
    "CheckLiveCancellationActivity", 
    new CancellationCheckRequest 
    { 
        MigrationId = request.MigrationId,
        Scope = CancellationScope.EntityType,
        EntityType = "your-new-entity-type"  // e.g., "customfields", "redirects"
    });

if (cancellationCheck.IsCancelled)
{
    await context.CallActivityAsync("ProcessCancellationActivity", request);
    return new MigrationResult { Status = "Cancelled", Reason = cancellationCheck.Reason };
}
```

### **Follow TODO Management:**
- [ ] **Create TODOs** for complex multi-step work
- [ ] **Update progress** as work progresses
- [ ] **Mark complete** when tasks are finished

---

## ✅ **STEP 6: VALIDATION AND COMPLETION**

### **Before Submitting Work:**

#### **Architecture Compliance:**
- [ ] **No manual SignalR events** - Factory used everywhere
- [ ] **Proper error handling** - Result objects returned
- [ ] **Dependency injection** - All services properly injected
- [ ] **Performance impact** - No degradation introduced
- [ ] **🛑 COMPREHENSIVE Live cancellation integration** - All entity implementations must support 5-level cancellation hierarchy (Migration, EntityType, Batch, Sub-batch, Entity)

#### **Testing Requirements:**
- [ ] **Tests written first** - TDD approach followed
- [ ] **Comprehensive coverage** - Edge cases included
- [ ] **All tests pass** - No regression introduced
- [ ] **Performance validated** - No negative impact

#### **Documentation:**
- [ ] **Patterns followed** - Established patterns used
- [ ] **Documentation updated** - If new patterns introduced
- [ ] **Memory updated** - If architectural changes made

---

## 🧠 **MEMORY SYSTEM USAGE**

### **Key Memory References:**
- **Memory 4674884**: Core architectural constraints and patterns
- **Memory 4610425**: SignalR centralization project status and completion
- **Memory 4493720**: User preferences for deployment commands
- **Memory 4493718**: Technical workarounds for macOS terminal issues

### **When to Update Memory:**
- [ ] **New architectural patterns** - Document new patterns established
- [ ] **User corrections** - Update memory when user corrects information
- [ ] **Constraint changes** - Update if requirements change
- [ ] **Completion status** - Update project completion status

---

## 🚨 **CRITICAL FAILURE PREVENTION**

### **NEVER DO:**
❌ Start work without completing this workflow
❌ Skip documentation review
❌ Ignore established patterns
❌ Create SignalR events manually
❌ Throw exceptions in Durable Functions
❌ **🚨 Add retry logic for API calls** *(CRITICAL: Wastes API calls, increases rate limit issues)*
❌ **🛑 Implement new entities without live cancellation support** *(CRITICAL: All entities must support cancellation)*
❌ Skip writing tests
❌ Break backward compatibility
❌ Implement automatic retry mechanisms
❌ Use retry patterns like Polly or custom retry loops

### **ALWAYS DO:**
✅ Complete entire workflow for every request
✅ Read all relevant documentation
✅ Use parallel tool calls for efficiency
✅ Follow established patterns exactly
✅ Use SignalREventFactory for all events
✅ Return result objects from Durable Functions
✅ **🎯 Use continue-on-error instead of retries** *(Log failures, continue processing)*
✅ **🛑 Integrate live cancellation in all entity implementations** *(All entities must support 4 cancellation scopes)*
✅ **Single API attempt per operation** *(Respect rate limits, no wasted calls)*
✅ Write tests first (TDD)
✅ Validate performance impact
✅ Update memory when needed

---

## 📊 **WORKFLOW COMPLETION CHECKLIST**

### **For Each Request:**
- [ ] **Step 1**: Request analyzed and classified
- [ ] **Step 2**: All relevant documentation reviewed
- [ ] **Step 3**: Codebase patterns discovered using searches
- [ ] **Step 4**: Architecture compliance validated
- [ ] **Step 5**: Implementation follows established patterns
- [ ] **Step 6**: Work validated and completed properly

### **Quality Gates:**
- [ ] **Documentation Review**: All relevant docs read
- [ ] **Pattern Discovery**: Existing patterns identified and followed
- [ ] **Architecture Compliance**: All constraints respected
- [ ] **🚨 NO RETRY LOGIC**: Confirmed no retry mechanisms implemented
- [ ] **🛑 COMPREHENSIVE LIVE CANCELLATION**: All entity implementations support 5-level cancellation hierarchy (Migration, EntityType, Batch, Sub-batch, Entity) with frequent checks and responsive user-initiated cancellation
- [ ] **💰 HYBRID MESSAGING**: Consider cost-effective alternatives to expensive Azure services
- [ ] **Testing Standards**: TDD approach with 99%+ coverage
- [ ] **Performance Validation**: No negative performance impact
- [ ] **Backward Compatibility**: No breaking changes introduced

### **💰 HYBRID MESSAGING PATTERN** *(Cost-Effective Infrastructure)*

When implementing real-time messaging or event propagation, **ALWAYS** consider the hybrid approach:

**✅ RECOMMENDED: 4-Layer Hybrid Architecture**
```
Layer 1: Azure Table Storage (persistence) - $0 (existing)
Layer 2: Azure Storage Queue (propagation) - ~$0.40/month  
Layer 3: SignalR Hub (real-time UI) - $0 (existing)
Layer 4: In-Memory Cache (performance) - $0 (built-in)
Total Cost: ~$0.40/month
```

**❌ AVOID: Expensive Alternatives**
- Azure Service Bus: ~$50+/month (125x more expensive!)
- Event Grid: ~$600+/month 
- Redis Cache: ~$15-30/month (when in-memory cache sufficient)

**📋 Hybrid Implementation Checklist:**
- [ ] **Leverage existing infrastructure** (queues, SignalR, storage)
- [ ] **Multi-layer redundancy** (queue + cache + storage)
- [ ] **Cost validation** (target <$1/month for messaging)
- [ ] **Performance targets** (<100ms propagation, <50ms cache hits)

**Example Reference**: `Task-7.3-Hybrid-Storage-Implementation-GUIDE.md`

---

**🎯 REMEMBER**: 
- **Current Status**: 85% complete, 562/562 tests passing, production-ready
- **Core Principle**: Follow established patterns, maintain backward compatibility, optimize costs
- **Quality Standard**: Enterprise-grade with comprehensive testing, startup-friendly costs

**📚 WORKFLOW REFERENCES**:
- `MASTER-ARCHITECTURE-INDEX.md` - Complete system overview
- `PRE-WORK-CHECKLIST.md` - Development checklist  
- `CODING-PATTERNS-QUICK-REFERENCE.md` - Ready-to-use patterns

This workflow ensures every AI assistant interaction maintains the high quality and architectural consistency that has been established in this enterprise-grade migration system. 