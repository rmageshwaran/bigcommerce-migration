# Enterprise Implementation Standards - BigCommerce Migration System

## 📋 Document Purpose

This document defines the **enterprise implementation standards** established for the BigCommerce Migration System. These standards are **NON-NEGOTIABLE** and must be followed for all current and future implementations to maintain enterprise-grade quality and reliability.

---

## 🚨 CRITICAL: Comprehensive Cancellation Support

### **Mandatory Requirements**
- **EVERY component** must support `CancellationToken` parameters
- **NO exceptions** - cancellation support is not optional
- **Multi-level cancellation** must work at all levels:
  - Migration Level: Cancel entire migration
  - Entity Level: Cancel specific entity type processing  
  - Batch Level: Cancel current batch operations
  - API Level: Cancel individual BigCommerce API calls

### **Implementation Standards**
- Use **storage-based cancellation flags** via `CancellationTokenEntry` in Azure Table Storage
- Implement **graceful shutdown** with proper cleanup and status reporting
- Handle `OperationCanceledException` with meaningful error messages
- **Never use `CancellationToken.None`** - always pass through actual cancellation tokens
- Ensure **Azure Durable Functions compatibility** (deterministic, replay-safe)

### **Cancellation Architecture Pattern**
```csharp
// ✅ CORRECT - Activity with cancellation support
[Function("ActivityName")]
public async Task<ResultType> ActivityAsync([ActivityTrigger] RequestType request, CancellationToken cancellationToken = default)
{
    try
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        // Perform work with cancellation token
        var result = await _service.DoWorkAsync(request, cancellationToken);
        
        return result;
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Operation was cancelled");
        throw; // or return appropriate cancelled result
    }
}

// ✅ CORRECT - Orchestrator with storage-based cancellation checking
var isCancelled = await context.CallActivityAsync<bool>("CheckMigrationCancellation", migrationId);
if (isCancelled)
{
    result.Status = MigrationStatus.Cancelled;
    result.Errors.Add("Migration was cancelled");
    return result;
}
```

---

## 🧪 Test-Driven Development (TDD) Standards

### **Implementation Approach**
- **Write comprehensive tests FIRST** defining expected behavior
- **Test coverage** must include ALL scenarios:
  - ✅ Success scenarios
  - ✅ Partial failure scenarios  
  - ✅ Complete failure scenarios
  - ✅ **Cancellation scenarios** (mandatory)
- **Continue-on-failure processing** with individual error tracking
- **Mock BigCommerce API responses** for unit tests
- **Real APIs for integration tests** using sandbox credentials

### **Test Pattern Standards**
```csharp
[Fact]
public async Task ComponentName_WithCancellation_HandlesCancellationGracefully()
{
    // Arrange
    var request = CreateValidRequest();
    var cancellationToken = new CancellationToken(true); // Already cancelled

    // Act
    var result = await _component.ProcessAsync(request, cancellationToken);

    // Assert
    Assert.NotNull(result);
    Assert.Contains("cancelled", result.ErrorMessage);
}
```

---

## ⚡ Azure Durable Functions Constraints

### **Deterministic Behavior Requirements**
- ✅ Use `context.CurrentUtcDateTime` (NOT `DateTime.Now` or `DateTime.UtcNow`)
- ✅ Use `context.NewGuid()` (NOT `Guid.NewGuid()`)
- ✅ Use `context.CreateTimer()` (NOT `Task.Delay` or `Thread.Sleep`)
- ✅ **No `ConfigureAwait(false)`** in orchestrators
- ✅ **All I/O operations in activity functions**, not orchestrators
- ✅ Ensure **deterministic behavior** for replay compatibility

### **Orchestrator Pattern**
```csharp
// ✅ CORRECT - Deterministic orchestrator
[Function("MigrationOrchestrator")]
public async Task<MigrationResult> RunOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
{
    var startTime = context.CurrentUtcDateTime; // ✅ Deterministic
    var correlationId = context.NewGuid(); // ✅ Deterministic
    
    // Check cancellation before each major step
    var isCancelled = await context.CallActivityAsync<bool>("CheckMigrationCancellation", migrationId);
    if (isCancelled) return CancelledResult();
    
    // Process with rate limiting
    if (delayNeeded)
    {
        var delayUntil = context.CurrentUtcDateTime.AddMilliseconds(delayMs);
        await context.CreateTimer(delayUntil); // ✅ Deterministic
    }
}
```

---

## 🛡️ Error Handling Standards

### **Error Processing Requirements**
- **Continue processing on failures** (user requirement)
- **Log all errors** at sub-component level
- **Preserve individual entity error tracking**
- **Comprehensive exception handling** with specific error types
- **No silent failures** - all errors must be logged and reported

### **Error Handling Pattern**
```csharp
try
{
    // Process individual entity
    result.TotalProcessed++;
    var processed = await ProcessEntityAsync(entity, cancellationToken);
    result.SuccessfulEntities++;
}
catch (OperationCanceledException)
{
    // Handle cancellation appropriately
    throw;
}
catch (Exception ex)
{
    // Continue processing on failures (user requirement)
    result.FailedEntities++;
    var entityId = entity.TryGetValue("id", out var id) ? id.ToString() : "unknown";
    var error = $"Failed to process {entityType} {entityId}: {ex.Message}";
    result.Errors.Add(error);
    
    _logger.LogWarning(ex, "Failed to process {EntityType} {EntityId}", entityType, entityId);
    // Continue processing other entities
}
```

---

## 🔗 BigCommerce API Compliance

### **API Requirements**
- **Rate limiting**: Maximum 12 requests/second
- **All entity types**: products, categories, brands, variants, images, modifiers
- **Channel-specific operations** with category tree ID resolution
- **Proper authentication** and credential handling
- **API version handling** (V2/V3) with appropriate endpoints
- **All API calls must respect cancellation tokens**

### **API Call Pattern**
```csharp
// ✅ CORRECT - API call with cancellation
public async Task<List<Product>> GetProductsAsync(StoreConfiguration store, CancellationToken cancellationToken)
{
    // Check rate limiting first
    await _rateLimitService.CheckAndWaitAsync(store.StoreId, cancellationToken);
    
    // Make API call with cancellation
    var response = await _httpClient.GetAsync(url, cancellationToken);
    
    return ParseResponse(response);
}
```

---

## 📊 Quality Standards

### **Quality Metrics**
- **All tests must pass** (currently 238/238)
- **Zero regressions** when adding new features
- **Enterprise-scale support** (10M+ entities)
- **Production-ready** error handling and monitoring
- **Comprehensive logging** for troubleshooting

### **Pre-Implementation Checklist**
- [ ] Cancellation support planned and designed
- [ ] TDD test scenarios defined (including cancellation)
- [ ] Azure Durable Functions constraints considered
- [ ] Error handling strategy defined
- [ ] BigCommerce API compliance verified
- [ ] Quality standards review completed

### **Post-Implementation Checklist**
- [ ] All tests passing (including new cancellation tests)
- [ ] Zero regressions in existing functionality
- [ ] Cancellation support verified at all levels
- [ ] Error handling tested under failure scenarios
- [ ] Performance and rate limiting validated
- [ ] Documentation updated

---

## 🏗️ Architecture Components Implemented

### **Orchestration Layer (COMPLETED)**
- `MigrationOrchestrator`: Main orchestrator with storage-based cancellation checks
- `EntityMigrationOrchestrator`: Entity-specific processing with batch-level cancellation
- Entity dependency order: categories→brands→products→variants→images→modifiers

### **Activity Functions (COMPLETED WITH CANCELLATION)**
- `ProcessEntityBatchActivity`: Full BigCommerce integration, 12 test scenarios
- `DiscoverEntitiesActivity`: Pagination-based entity discovery
- `InitializeMigrationActivity`: Migration session initialization
- `ValidateMigrationStoresActivity`: Store credential validation
- `CheckRateLimitActivity`: BigCommerce API rate limiting
- `UpdateEntityProgressActivity`: Progress tracking (non-critical)
- `CheckMigrationCancellationActivity`: Storage-based cancellation checking

### **Service Interfaces (DEFINED)**
- `IBigCommerceApiClient`: API operations with cancellation support
- `IMigrationStorageService`: Storage operations including cancellation tokens
- `IRateLimitService`: Rate limiting compliance (12 req/sec)
- `IProgressTracker`: Progress tracking and reporting  
- `IOpenSearchService`: Logging and monitoring

---

## 🎯 Implementation Reminders

### **For Every New Component:**
1. **Start with cancellation support** - design it in from the beginning
2. **Write cancellation tests first** - define expected behavior
3. **Follow TDD approach** - tests define the contract
4. **Implement error handling** - continue-on-failure with logging
5. **Verify Azure Durable Functions** compliance
6. **Test BigCommerce API** integration with rate limiting
7. **Validate quality standards** - no regressions

### **Red Flags to Avoid:**
- ❌ Using `CancellationToken.None`
- ❌ Missing cancellation tests
- ❌ Silent failures without logging
- ❌ Breaking existing tests
- ❌ Non-deterministic orchestrator code
- ❌ Exceeding BigCommerce rate limits
- ❌ Missing error handling for individual entities

---

## 📝 Maintenance Notes

This document must be updated whenever:
- New enterprise standards are established
- Architecture patterns change
- Quality requirements evolve
- New compliance requirements are added

**Document Version**: 1.0 (2025-01-06)  
**Last Updated**: Implementation of comprehensive cancellation support  
**Next Review**: When adding new major components or changing architecture 